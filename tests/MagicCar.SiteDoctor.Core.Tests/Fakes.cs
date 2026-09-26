using System.Collections.Concurrent;
using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Diagnostics;
using MagicCar.SiteDoctor.Models;
using MagicCar.SiteDoctor.Recovery;

namespace MagicCar.SiteDoctor.Core.Tests;

internal sealed class FakeSystem : ISystemInspector
{
    public bool Network = true, Dns = true;
    public int Processes = 1;
    public Dictionary<string, ServiceSnapshot> Services = new();
    public Dictionary<string, TaskSnapshot> Tasks = new();
    public Dictionary<int, bool> Ports = new();
    public PrinterSnapshot Printer = new(true, "Synthetic driver", "Normal", false, false, false, false, 0, []);
    public Exception? PrinterError;
    public ConcurrentBag<string> Calls = [];
    public bool NetworkAvailable() { Calls.Add("Network"); return Network; }
    public Task<bool> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(Dns);
    public Task<ServiceSnapshot> ServiceAsync(string name, CancellationToken ct) { Calls.Add(name); return Task.FromResult(Services.GetValueOrDefault(name, new(true, "Running"))); }
    public Task<TaskSnapshot> ScheduledTaskAsync(string name, CancellationToken ct) { Calls.Add(name); return Task.FromResult(Tasks.GetValueOrDefault(name, new(true, "Running"))); }
    public Task<int> ProcessCountAsync(string name, CancellationToken ct) => Task.FromResult(Processes);
    public Task<bool> TcpAsync(int port, CancellationToken ct) { Calls.Add($"TCP:{port}"); return Task.FromResult(Ports.GetValueOrDefault(port, true)); }
    public Task<PrinterSnapshot> PrinterAsync(string name, CancellationToken ct)
    { Calls.Add("Printer"); return PrinterError is null ? Task.FromResult(Printer) : Task.FromException<PrinterSnapshot>(PrinterError); }
}
internal sealed class FakeHttp(SiteDoctorOptions options) : IHttpProbe
{
    public Dictionary<string, HttpSnapshot> Responses = new();
    public HashSet<string> Fail = [];
    public ConcurrentBag<string> Calls = [];
    public TaskCompletionSource? Hold;
    public async Task<HttpSnapshot> GetAsync(string url, bool readJsonBody, CancellationToken ct)
    {
        Calls.Add(url);
        if (Hold is not null) await Hold.Task.WaitAsync(ct);
        if (Fail.Contains(url)) throw new HttpRequestException("SENSITIVE_BODY_MUST_NOT_LEAK");
        if (Responses.TryGetValue(url, out var result)) return result;
        if (url == options.TunnelReadinessUrl) return Json("{\"status\":200,\"readyConnections\":4}");
        if (url == options.PrintGatewayHealthUrl) return Json(System.Text.Json.JsonSerializer.Serialize(new { ok=true, printer=options.PrinterQueueName }));
        if (url == options.AutoPostHealthUrl) return Json("{\"ok\":true}");
        if (url == options.InternetProbeUrl) return new(200, "text/html", null, false);
        return new(403, "text/html", null, true);
    }
    public static HttpSnapshot Json(string body, int status = 200, string type = "application/json") => new(status, type, body, false);
}
internal sealed class FakeRepair : IRepairExecutor
{
    public int Calls;
    public RepairExecution Result = RepairExecution.Requested;
    public Task<RepairExecution> ExecuteAsync(RecoveryActionId action, CancellationToken ct) { Calls++; return Task.FromResult(Result); }
}
internal sealed class Fixture
{
    public SiteDoctorOptions Options = new();
    public FakeSystem System = new();
    public FakeHttp Http;
    public DiagnosticEngine Engine;
    public Fixture() { Http=new(Options); Engine=new(Options,System,Http); }
    public Task<DiagnosticResult> Check(ComponentId id) => Engine.CheckAsync(id,CancellationToken.None);
    public Task<DiagnosticRun> Run() => new DiagnosticOrchestrator(Engine).RunAsync(null,CancellationToken.None);
}
