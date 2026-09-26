using System.Text.Json;
using MagicCar.SiteDoctor.Models;
using MagicCar.SiteDoctor.Recovery;

namespace MagicCar.SiteDoctor.Storage;

public sealed record HistoryEntry(DateTimeOffset At, string Kind, HealthStatus Status,
    RecoveryActionId? Action = null, RepairExecution? Execution = null);
public sealed record HistoryState(DiagnosticRun? LastRun, IReadOnlyList<HistoryEntry> Recent);

public sealed class HistoryStore(string directory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<HistoryState> LoadAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(directory, "state.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 256_000) return new(null, []);
        try
        {
            var state = JsonSerializer.Deserialize<HistoryState>(await File.ReadAllTextAsync(path, ct), JsonDefaults.Options);
            if (state?.Recent is null || state.Recent.Count > 30 || state.Recent.Any(e => e is null)) return new(null, []);
            if (state.LastRun is { } run && (run.Results is null || run.Results.Count != 9
                || run.Results.Any(r => r is null || r.Evidence is null))) return new(null, []);
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(null, []); }
    }

    public Task SaveRunAsync(DiagnosticRun run, CancellationToken ct = default) =>
        SaveAsync(run, new(run.FinishedAt, "diagnosis", run.Overall), ct);
    public Task SaveRecoveryAsync(RecoveryOutcome outcome, CancellationToken ct = default) =>
        SaveAsync(outcome.Retest, new(DateTimeOffset.Now, "recovery", outcome.Retest.Overall, outcome.Action, outcome.Execution), ct);

    private async Task SaveAsync(DiagnosticRun run, HistoryEntry entry, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var previous = await LoadAsync(ct);
            var state = new HistoryState(run, previous.Recent.Append(entry).TakeLast(30).ToArray());
            var path = Path.Combine(directory, "state.json");
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonDefaults.Options), ct);
            File.Move(temporary, path, true);
            var logs = Path.Combine(directory, "logs");
            Directory.CreateDirectory(logs);
            var log = Path.Combine(logs, $"site-doctor-{DateTime.Now:yyyy-MM-dd}.log");
            if (File.Exists(log) && new FileInfo(log).Length > 262_144) File.Delete(log);
            // The log is a typed event, never an exception message, command line, job name or response body.
            await File.AppendAllTextAsync(log, JsonSerializer.Serialize(entry, JsonDefaults.Options) + Environment.NewLine, ct);
            foreach (var old in Directory.EnumerateFiles(logs, "site-doctor-*.log").OrderByDescending(Path.GetFileName).Skip(14)) File.Delete(old);
        }
        finally { gate.Release(); }
    }
}
