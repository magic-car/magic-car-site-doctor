using System.Text.Json;
using System.Text.Json.Serialization;

namespace MagicCar.SiteDoctor.Models;

public enum HealthStatus { NotChecked, Checking, Healthy, Warning, Failed }
public enum ComponentId { Internet, Cloudflare, Tunnel, PrintGateway, Spooler, Printer, AutoPost, InterBase, Guardian }
public enum RecoveryActionId { RestartCloudflared, RestartPrintGateway, RestartSpooler, RestartAutoPost }

public sealed record DiagnosticResult(
    ComponentId Component, HealthStatus Status, string UserMessage,
    IReadOnlyDictionary<string, string> Evidence, long DurationMs,
    DateTimeOffset CheckedAt, RecoveryActionId? RecoveryAction = null);

public sealed record DiagnosticRun(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    IReadOnlyList<DiagnosticResult> Results)
{
    public HealthStatus Printing => Aggregate(Results.Where(r => r.Component is ComponentId.PrintGateway or ComponentId.Spooler or ComponentId.Printer));
    public HealthStatus AlArabi => Aggregate(Results.Where(r => r.Component is ComponentId.AutoPost or ComponentId.InterBase or ComponentId.Guardian));
    public HealthStatus Overall => Results.Count != 9 && !Results.Any(r => r.Status == HealthStatus.Failed)
        ? HealthStatus.Warning : Aggregate(Results);

    public static HealthStatus Aggregate(IEnumerable<DiagnosticResult> results)
    {
        var states = results.Select(r => r.Status).ToArray();
        if (states.Contains(HealthStatus.Failed)) return HealthStatus.Failed;
        if (states.Length == 0) return HealthStatus.NotChecked;
        return states.All(s => s == HealthStatus.Healthy) ? HealthStatus.Healthy : HealthStatus.Warning;
    }
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
}
