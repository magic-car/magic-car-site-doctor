using System.Net;
using System.Text.Json;
using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Configuration;

public sealed record PublicEndpoint(string Name, string Url, int ExpectedUnauthenticatedStatus = 403);
public sealed record RecoveryOptions { public bool Enabled { get; init; } = false; }

public sealed record SiteDoctorOptions
{
    public RecoveryOptions Recovery { get; init; } = new();
    public string InternetProbeUrl { get; init; } = "https://example.com/";
    public string TunnelReadinessUrl { get; init; } = "http://127.0.0.1:20241/ready";
    public PublicEndpoint[] PublicEndpoints { get; init; } =
    [
        new("Al-Arabi", "https://arabi-api.e-mall.site/Health"),
        new("Printing", "https://print.magic-car.app/health")
    ];
    public string PrintGatewayHealthUrl { get; init; } = "http://127.0.0.1:8787/health";
    public string PrinterQueueName { get; init; } = "EPSON77D4D3 (L3250 Series)";
    public string AutoPostHealthUrl { get; init; } = "http://127.0.0.1:5555/Health";
    public int InterBasePort { get; init; } = 3050;
    public int HttpTimeoutSeconds { get; init; } = 5;
    public int CheckTimeoutSeconds { get; init; } = 20;
    public int StaleJobMinutes { get; init; } = 15;

    // Fixed operation targets cannot be changed by writable configuration or UI messages.
    public const string CloudflaredService = "Cloudflared";
    public const string SpoolerService = "Spooler";
    public const string InterBaseService = "IBS_gds_db";
    public const string GuardianService = "IBG_gds_db";
    public const string PrintGatewayTask = "MagicCar-Print-Gateway";
    public const string AutoPostTask = "MagicCar-AutoPost";
    public const string WatchdogTask = "MagicCar-AutoPost-Watchdog";
    public const string MonitorTask = "MagicCar-AutoPost-Monitor";

    public static SiteDoctorOptions Load(string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length > 16_384)
            throw new InvalidDataException("Configuration is missing or exceeds 16 KiB.");
        var options = JsonSerializer.Deserialize<SiteDoctorOptions>(File.ReadAllText(file), JsonDefaults.Options)
            ?? throw new InvalidDataException("Configuration is empty.");
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Recovery is null) throw new InvalidDataException("Recovery configuration is required.");
        ValidateUri(InternetProbeUrl, false);
        foreach (var url in new[] { TunnelReadinessUrl, PrintGatewayHealthUrl, AutoPostHealthUrl }) ValidateUri(url, true);
        if (PublicEndpoints is not { Length: 2 }) throw new InvalidDataException("Two public endpoints are required.");
        foreach (var endpoint in PublicEndpoints)
        {
            if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Name) || endpoint.Name.Length > 40
                || endpoint.ExpectedUnauthenticatedStatus != 403) throw new InvalidDataException("Invalid public endpoint.");
            ValidateUri(endpoint.Url, false);
        }
        if (string.IsNullOrWhiteSpace(PrinterQueueName) || PrinterQueueName.Length > 200 || PrinterQueueName.Any(char.IsControl))
            throw new InvalidDataException("Invalid printer queue.");
        if (InterBasePort is < 1 or > 65535 || HttpTimeoutSeconds is < 1 or > 10
            || CheckTimeoutSeconds is < 10 or > 45 || StaleJobMinutes is < 1 or > 1440)
            throw new InvalidDataException("Invalid timeout, job age, or port.");
    }

    private static void ValidateUri(string value, bool local)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || value.Length > 256
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Invalid probe URL (credentials and query strings are not permitted).");
        if (local ? uri.Scheme != "http" || uri.Host != "127.0.0.1" : uri.Scheme != "https" || uri.IsLoopback)
            throw new InvalidDataException("Local probes must use HTTP on 127.0.0.1; public probes must use HTTPS.");
    }
}
