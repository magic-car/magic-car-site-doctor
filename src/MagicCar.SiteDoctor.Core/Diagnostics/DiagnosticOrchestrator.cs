using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Diagnostics;

public sealed class DiagnosticOrchestrator(DiagnosticEngine engine)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<DiagnosticRun> RunAsync(IProgress<DiagnosticResult>? progress, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("A diagnostic run is already active.");
        var started = DateTimeOffset.Now;
        try
        {
            async Task<DiagnosticResult[]> Branch(params ComponentId[] ids)
            {
                var results = new List<DiagnosticResult>();
                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new(id, HealthStatus.Checking, "جارٍ الفحص…", new Dictionary<string, string>(), 0, DateTimeOffset.Now));
                    var result = await engine.CheckAsync(id, ct);
                    results.Add(result);
                    progress?.Report(result);
                }
                return results.ToArray();
            }
            var network = await Branch(ComponentId.Internet, ComponentId.Cloudflare, ComponentId.Tunnel);
            var branches = await Task.WhenAll(
                Branch(ComponentId.PrintGateway, ComponentId.Spooler, ComponentId.Printer),
                Branch(ComponentId.AutoPost, ComponentId.InterBase, ComponentId.Guardian));
            return new(Guid.NewGuid(), started, DateTimeOffset.Now, network.Concat(branches.SelectMany(b => b)).ToArray());
        }
        finally { gate.Release(); }
    }
}
