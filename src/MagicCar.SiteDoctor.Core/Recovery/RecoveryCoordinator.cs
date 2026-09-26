using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Recovery;

public enum RepairExecution { Requested, ElevationDenied, Failed, TimedOut }
public sealed record RecoveryOutcome(RecoveryActionId Action, RepairExecution Execution, DiagnosticRun Retest)
{
    public bool Recovered => Execution == RepairExecution.Requested && Retest.Results.Any(r =>
        r.Component == (Action == RecoveryActionId.RestartCloudflared ? ComponentId.Tunnel : ComponentId.Spooler)
        && r.Status == HealthStatus.Healthy);
}
public interface IRepairExecutor
{
    Task<RepairExecution> ExecuteAsync(RecoveryActionId action, CancellationToken ct);
}
public sealed class RecoveryCoordinator(IRepairExecutor executor, bool recoveryEnabled = false)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<RecoveryOutcome> RecoverAsync(RecoveryActionId action,
        Func<CancellationToken, Task<DiagnosticRun>> retest, CancellationToken ct)
    {
        if (!recoveryEnabled || !RecoveryPolicy.IsImplemented(action)) throw new InvalidOperationException("Recovery action is not enabled.");
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("Recovery is already active.");
        try
        {
            var execution = await executor.ExecuteAsync(action, ct);
            // Even failed/denied repairs are retested; no result becomes green just because a command exited.
            return new(action, execution, await retest(ct));
        }
        finally { gate.Release(); }
    }
}
