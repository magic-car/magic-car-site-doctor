using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Printing;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using MagicCar.SiteDoctor.Diagnostics;

namespace MagicCar.SiteDoctor.Windows;

public sealed class WindowsSystemInspector : ISystemInspector
{
    // A hung native call retains its slot. Repeated diagnosis cannot create unlimited native threads.
    private readonly SemaphoreSlim printerSlot = new(1, 1);
    private readonly SemaphoreSlim taskSlot = new(1, 1);

    public bool NetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();
    public async Task<bool> ResolveAsync(string host, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try { return (await Dns.GetHostAddressesAsync(host, deadline.Token)).Length > 0; }
        catch (SocketException) { return false; }
    }

    public Task<ServiceSnapshot> ServiceAsync(string name, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        try { using var service = new ServiceController(name); return new ServiceSnapshot(true, service.Status.ToString()); }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
        { return new ServiceSnapshot(false, "Missing"); }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        { throw new UnauthorizedAccessException("Windows service inspection denied."); }
    }, ct);

    public Task<TaskSnapshot> ScheduledTaskAsync(string name, CancellationToken ct) => InStaAsync(taskSlot, () =>
    {
        object? scheduler = null, folder = null, task = null;
        try
        {
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Task Scheduler API unavailable."));
            ((dynamic)scheduler!).Connect();
            folder = ((dynamic)scheduler!).GetFolder("\\");
            try { task = ((dynamic)folder).GetTask(name); }
            catch (COMException ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x8004130F))
            { return new TaskSnapshot(false, "Missing"); }
            int state = ((dynamic)task).State;
            return new TaskSnapshot(true, state switch { 1 => "Disabled", 2 => "Queued", 3 => "Ready", 4 => "Running", _ => "Unknown" },
                (int)((dynamic)task).LastTaskResult);
        }
        finally
        {
            foreach (var value in new[] { task, folder, scheduler })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }, ct);

    public Task<int> ProcessCountAsync(string name, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length; }
        finally { foreach (var p in processes) p.Dispose(); }
    }, ct);

    public async Task<bool> TcpAsync(int port, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        try { await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token); return true; }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public Task<PrinterSnapshot> PrinterAsync(string name, CancellationToken ct) => InStaAsync(printerSlot, () =>
    {
        using var server = new LocalPrintServer();
        // Inspect only the selected queue. Never enumerate documents from unrelated printers.
        try
        {
            using var queue = server.GetPrintQueue(name);
            queue.Refresh();
            var flags = queue.QueueStatus;
            var jobs = new List<JobSnapshot>();
            using var collection = queue.GetPrintJobInfoCollection();
            foreach (var job in collection)
            {
                using (job)
                {
                    jobs.Add(new(job.IsInError || job.IsOffline || job.IsPaperOut || job.IsBlocked,
                        job.IsRetained, job.IsPrinting, job.IsCompleted || job.IsPrinted,
                        new DateTimeOffset(job.TimeJobSubmitted)));
                }
            }
            var blocked = PrintQueueStatus.PaperOut | PrintQueueStatus.PaperJam | PrintQueueStatus.Paused
                | PrintQueueStatus.NotAvailable | PrintQueueStatus.UserIntervention | PrintQueueStatus.DoorOpen
                | PrintQueueStatus.NoToner | PrintQueueStatus.PendingDeletion;
            var warning = PrintQueueStatus.TonerLow | PrintQueueStatus.PaperProblem | PrintQueueStatus.OutputBinFull;
            return new PrinterSnapshot(true, queue.QueueDriver.Name, flags.ToString(), queue.IsOffline, queue.IsInError,
                (flags & blocked) != 0, (flags & warning) != 0, queue.NumberOfJobs, jobs);
        }
        catch (PrintQueueException ex) when ((ex.HResult & 0xFFFF) == 1801)
        { return new PrinterSnapshot(false, "", "Missing", false, false, false, false, 0, []); }
        catch (PrintSystemException ex) when ((ex.HResult & 0xFFFF) == 5)
        { throw new UnauthorizedAccessException("Windows printer inspection denied."); }
    }, ct);

    private static async Task<T> InStaAsync<T>(SemaphoreSlim slot, Func<T> action, CancellationToken ct)
    {
        await slot.WaitAsync(ct);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { slot.Release(); }
        }) { IsBackground = true, Name = "SiteDoctor Windows inspection" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task.WaitAsync(ct);
    }
}
