using System.Net;
using System.Text;

namespace MagicCar.SiteDoctor.Diagnostics;

public sealed class HttpProbe : IHttpProbe, IDisposable
{
    private readonly HttpClient client;
    public HttpProbe(int timeoutSeconds, HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MagicCar-SiteDoctor/0.1");
    }

    public async Task<HttpSnapshot> GetAsync(string url, bool readJsonBody, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(client.Timeout);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        var type = response.Content.Headers.ContentType?.MediaType;
        string? body = null;
        // Public responses, including Access HTML, are never read or persisted.
        if (readJsonBody && response.StatusCode == HttpStatusCode.OK && IsJson(type))
        {
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[16_385];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read), deadline.Token);
                if (count == 0) break;
                read += count;
            }
            if (read == buffer.Length) throw new InvalidDataException("Health response exceeds the limit.");
            body = Encoding.UTF8.GetString(buffer, 0, read);
        }
        return new HttpSnapshot((int)response.StatusCode, type, body,
            response.Headers.Contains("CF-Ray") || response.Headers.Server.Any(s => s.Product?.Name.Equals("cloudflare", StringComparison.OrdinalIgnoreCase) == true));
    }

    public static bool IsJson(string? type) => type is not null &&
        (type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    public void Dispose() => client.Dispose();
}
