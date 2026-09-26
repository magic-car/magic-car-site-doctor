namespace MagicCar.SiteDoctor.Diagnostics;

public sealed record ServiceSnapshot(bool Exists, string State);
public sealed record TaskSnapshot(bool Exists, string State, int LastResult = 0);
public sealed record JobSnapshot(bool Failed, bool Retained, bool Printing, bool Complete, DateTimeOffset SubmittedAt);
public sealed record PrinterSnapshot(bool Exists, string Driver, string Status,
    bool Offline, bool Error, bool Blocked, bool Degraded, int JobCount, IReadOnlyList<JobSnapshot> Jobs);
public sealed record HttpSnapshot(int StatusCode, string? ContentType, string? Body, bool CloudflareEvidence);

public interface ISystemInspector
{
    bool NetworkAvailable();
    Task<bool> ResolveAsync(string host, CancellationToken ct);
    Task<ServiceSnapshot> ServiceAsync(string name, CancellationToken ct);
    Task<TaskSnapshot> ScheduledTaskAsync(string name, CancellationToken ct);
    Task<int> ProcessCountAsync(string name, CancellationToken ct);
    Task<bool> TcpAsync(int port, CancellationToken ct);
    Task<PrinterSnapshot> PrinterAsync(string name, CancellationToken ct);
}

public interface IHttpProbe
{
    Task<HttpSnapshot> GetAsync(string url, bool readJsonBody, CancellationToken ct);
}
