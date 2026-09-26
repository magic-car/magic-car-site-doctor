using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Recovery;

public sealed class ElevatedRepairExecutor : IRepairExecutor
{
    public async Task<RepairExecution> ExecuteAsync(RecoveryActionId action, CancellationToken ct)
    {
        if (!RecoveryPolicy.IsEnabled(action)) throw new InvalidOperationException("Action is blocked.");
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Executable unavailable."))
            {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add("--repair");
            start.ArgumentList.Add(action.ToString());
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Repair did not start.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return RepairExecution.TimedOut; }
            return process.ExitCode == 0 ? RepairExecution.Requested : RepairExecution.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return RepairExecution.ElevationDenied; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return RepairExecution.Failed; }
    }
}

public static class ElevatedRepair
{
    // The elevated path does not read configuration, local logs, UI data, task scripts or network responses.
    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] != "--repair" || !Enum.TryParse<RecoveryActionId>(args[1], out var action)
            || !Enum.IsDefined(action) || args[1] != action.ToString() || !RecoveryPolicy.IsEnabled(action)) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        using var mutex = new Mutex(false, "Global\\MagicCar.SiteDoctor.Repair");
        var locked = false;
        try
        {
            try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
            if (!locked) return 4;
            var name = action == RecoveryActionId.RestartCloudflared ? SiteDoctorOptions.CloudflaredService : SiteDoctorOptions.SpoolerService;
            using var service = new ServiceController(name);
            service.Refresh();
            // Refuse to stop dependent services implicitly.
            var dependents = service.DependentServices;
            try { if (dependents.Any(s => s.Status != ServiceControllerStatus.Stopped)) return 5; }
            finally { foreach (var dependent in dependents) dependent.Dispose(); }
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.Paused) || !service.CanStop) return 6;
                service.Stop(false);
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException or UnauthorizedAccessException) { return 7; }
        finally { if (locked) mutex.ReleaseMutex(); }
    }
}
