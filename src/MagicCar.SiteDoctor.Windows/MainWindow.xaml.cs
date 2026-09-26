using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Diagnostics;
using MagicCar.SiteDoctor.Models;
using MagicCar.SiteDoctor.Recovery;
using MagicCar.SiteDoctor.Storage;
using MagicCar.SiteDoctor.Windows;
using Microsoft.Web.WebView2.Core;

namespace MagicCar.SiteDoctor;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagicCar", "SiteDoctor");
    private SiteDoctorOptions options = null!;
    private HttpProbe http = null!;
    private DiagnosticOrchestrator orchestrator = null!;
    private HistoryStore history = null!;
    private bool initialized;
    private bool repairing;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += InitializeAsync;
        Closing += OnClosing;
        Closed += (_, _) => { lifetime.Cancel(); http?.Dispose(); Browser.Dispose(); };
    }

    private async void InitializeAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            options = SiteDoctorOptions.Load(Path.Combine(AppContext.BaseDirectory, "site-doctor.json"));
            history = new HistoryStore(dataDirectory);
            http = new HttpProbe(options.HttpTimeoutSeconds);
            orchestrator = new(new DiagnosticEngine(options, new WindowsSystemInspector(), http));
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDirectory, "WebView2"));
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("site-doctor.local", Path.Combine(AppContext.BaseDirectory, "Ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NavigationStarting += (_, args) => args.Cancel = !UiProtocol.IsDocument(args.Uri);
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != UiProtocol.Origin)
                    args.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked", "Content-Type: text/plain");
            };
            core.WebMessageReceived += OnWebMessage;
            initialized = true;
            core.Navigate(UiProtocol.DocumentUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("يلزم تثبيت Microsoft Edge WebView2 Evergreen Runtime لتشغيل الواجهة.\nراجع README-FIRST-RUN.txt المرفق ثم أعد فتح التطبيق.", "Magic Car Site Doctor", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception)
        {
            MessageBox.Show("تعذّر بدء التطبيق. تحقق من استخراج المجلد كاملاً، وصحة site-doctor.json، وتوفر WebView2 وصلاحية الكتابة في مجلد المستخدم.", "Magic Car Site Doctor", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var command = UiProtocol.Parse(args.WebMessageAsJson, args.Source);
        if (command is null) return;
        try
        {
            if (command.Type == "ready")
            {
                Send(new { type = "initialized", readOnly = !(options.Recovery.Enabled && RecoveryPolicy.BuildAllowsRecovery),
                    machine = Environment.MachineName, queue = options.PrinterQueueName,
                    recovery = RecoveryPolicy.Capabilities(options.Recovery.Enabled), history = await history.LoadAsync(lifetime.Token) });
                return;
            }
            if (!await operation.WaitAsync(0, lifetime.Token)) return;
            try
            {
                if (command.Type == "runDiagnostics") await DiagnoseAsync(lifetime.Token);
                else if (command.Type == "openPrinterQueue") OpenPrinterQueue();
                else if (command.Type == "repair" && command.Action is { } action) await RepairAsync(action);
            }
            finally { operation.Release(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            Send(new { type = "operationError", message = "تعذّر إكمال العملية. لم يتم تأكيد أي إصلاح؛ أعد الفحص." });
        }
    }

    private async Task<DiagnosticRun> DiagnoseAsync(CancellationToken ct)
    {
        Send(new { type = "runStarted" });
        var progress = new Progress<DiagnosticResult>(r => Send(new { type = "componentStatus", result = r }));
        var run = await orchestrator.RunAsync(progress, ct);
        string? historyWarning = null;
        try { await history.SaveRunAsync(run, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { historyWarning = "اكتمل الفحص، لكن تعذّر حفظ السجل المحلي."; }
        Send(new { type = "runCompleted", run, historyWarning });
        return run;
    }

    private async Task RepairAsync(RecoveryActionId action)
    {
        if (!options.Recovery.Enabled || !RecoveryPolicy.IsEnabled(action))
        {
            Send(new { type = "operationError", message = "إعادة التشغيل معطلة في هذه النسخة. التشخيص متاح دون تغيير الخدمات." });
            return;
        }
        var message = action == RecoveryActionId.RestartSpooler
            ? "قد تتوقف مهام الطباعة لجميع الطابعات مؤقتاً. لن تُحذف مهام الطباعة.\nهل تريد إعادة تشغيل Windows Print Spooler؟"
            : "قد ينقطع اتصال Magic Car الخارجي مؤقتاً.\nهل تريد إعادة تشغيل Cloudflare Tunnel؟";
        if (MessageBox.Show(this, message, "تأكيد إعادة التشغيل", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        repairing = true;
        Send(new { type = "repairStarted", action });
        try
        {
            var coordinator = new RecoveryCoordinator(new ElevatedRepairExecutor(), true);
            var outcome = await coordinator.RecoverAsync(action, DiagnoseAsync, lifetime.Token);
            try { await history.SaveRecoveryAsync(outcome, lifetime.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Diagnosis remains visible. */ }
            Send(new { type = "repairCompleted", outcome });
        }
        finally { repairing = false; }
    }

    private void OpenPrinterQueue()
    {
        // Fixed Windows executable + separate arguments, never command text from the WebView.
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe")) { UseShellExecute = false };
        start.ArgumentList.Add("printui.dll,PrintUIEntry");
        start.ArgumentList.Add("/o");
        start.ArgumentList.Add("/n");
        start.ArgumentList.Add(options.PrinterQueueName);
        using var process = Process.Start(start);
    }

    private void Send(object message)
    {
        if (!initialized || lifetime.IsCancellationRequested || Browser.CoreWebView2?.Source != UiProtocol.DocumentUrl) return;
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonDefaults.Options));
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!repairing) return;
        e.Cancel = true;
        MessageBox.Show(this, "انتظر انتهاء عملية إعادة التشغيل وإعادة الفحص قبل إغلاق التطبيق.", "Magic Car Site Doctor");
    }
}
