using System.Diagnostics;
using System.Text.Json;
using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Diagnostics;

public sealed class DiagnosticEngine(SiteDoctorOptions options, ISystemInspector system, IHttpProbe http)
{
    public async Task<DiagnosticResult> CheckAsync(ComponentId component, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.CheckTimeoutSeconds));
        try
        {
            var result = await CheckCoreAsync(component, deadline.Token).WaitAsync(deadline.Token);
            return result with { DurationMs = clock.ElapsedMilliseconds, CheckedAt = DateTimeOffset.Now };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Do not surface exception text: it can contain HTTP bodies, paths or credentials.
            var unavailable = ex is UnauthorizedAccessException or System.Security.SecurityException
                or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception;
            return Result(component, unavailable ? HealthStatus.NotChecked : HealthStatus.Failed,
                unavailable ? "تعذّر قراءة حالة هذا المكوّن. راجع صلاحيات المستخدم ثم أعد الفحص."
                    : ex is OperationCanceledException or TimeoutException
                        ? "انتهت مهلة الفحص دون الحصول على استجابة."
                        : "تعذّر التحقق من هذا المكوّن. أعد الفحص وراجع التفاصيل.",
                new() { ["Check"] = ex is OperationCanceledException ? "Timed out" : ex.GetType().Name })
                with { DurationMs = clock.ElapsedMilliseconds };
        }
    }

    private Task<DiagnosticResult> CheckCoreAsync(ComponentId id, CancellationToken ct) => id switch
    {
        ComponentId.Internet => InternetAsync(ct),
        ComponentId.Cloudflare => CloudflareAsync(ct),
        ComponentId.Tunnel => TunnelAsync(ct),
        ComponentId.PrintGateway => GatewayAsync(ct),
        ComponentId.Spooler => ServiceAsync(id, SiteDoctorOptions.SpoolerService, ct),
        ComponentId.Printer => PrinterAsync(ct),
        ComponentId.AutoPost => AutoPostAsync(ct),
        ComponentId.InterBase => InterBaseAsync(ct),
        ComponentId.Guardian => ServiceAsync(id, SiteDoctorOptions.GuardianService, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    private async Task<DiagnosticResult> InternetAsync(CancellationToken ct)
    {
        var evidence = new Dictionary<string, string>();
        var available = system.NetworkAvailable();
        evidence["Network interface"] = available ? "Available" : "Unavailable";
        if (!available) return Result(ComponentId.Internet, HealthStatus.Failed, "لا يوجد اتصال بالشبكة. سنواصل فحص الخدمات المحلية.", evidence);
        var uri = new Uri(options.InternetProbeUrl);
        evidence["Probe host"] = uri.Host;
        var dns = await system.ResolveAsync(uri.Host, ct);
        evidence["DNS"] = dns ? "Resolved" : "Failed";
        if (!dns) return Result(ComponentId.Internet, HealthStatus.Failed, "تعذّر الوصول إلى DNS. سنواصل فحص الخدمات المحلية.", evidence);
        var response = await http.GetAsync(uri.AbsoluteUri, false, ct);
        evidence["HTTPS"] = $"HTTP {response.StatusCode}";
        return Result(ComponentId.Internet, response.StatusCode is >= 200 and < 300 ? HealthStatus.Healthy : HealthStatus.Failed,
            response.StatusCode is >= 200 and < 300 ? "اتصال الإنترنت يعمل." : "لم ينجح اختبار الإنترنت. سنواصل فحص الخدمات المحلية.", evidence);
    }

    private async Task<DiagnosticResult> CloudflareAsync(CancellationToken ct)
    {
        var evidence = new Dictionary<string, string>();
        var status = HealthStatus.Healthy;
        foreach (var endpoint in options.PublicEndpoints)
        {
            try
            {
                var response = await http.GetAsync(endpoint.Url, false, ct);
                evidence[endpoint.Name] = $"HTTP {response.StatusCode}";
                if (response.StatusCode == endpoint.ExpectedUnauthenticatedStatus)
                {
                    evidence[endpoint.Name] += response.CloudflareEvidence ? " · expected / Cloudflare headers" : " · expected / edge identity unconfirmed";
                    if (!response.CloudflareEvidence && status != HealthStatus.Failed) status = HealthStatus.Warning;
                }
                else if (response.StatusCode is >= 200 and < 400)
                {
                    if (status != HealthStatus.Failed) status = HealthStatus.Warning;
                }
                else status = HealthStatus.Failed;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                ct.ThrowIfCancellationRequested();
                evidence[endpoint.Name] = "Unreachable / timed out";
                status = HealthStatus.Failed;
            }
        }
        evidence["Scope"] = "Edge reachability only; origin routing is not verified";
        return Result(ComponentId.Cloudflare, status, status switch
        {
            HealthStatus.Healthy => "Cloudflare متاح؛ الاستجابة 403 متوقعة للمسارات المحمية.",
            HealthStatus.Warning => "استجابة المسار العام تحتاج مراجعة إعدادات الوصول.",
            _ => "تعذّر الوصول إلى أحد مسارات Cloudflare. سنواصل الفحص المحلي."
        }, evidence);
    }

    private async Task<DiagnosticResult> TunnelAsync(CancellationToken ct)
    {
        var service = await system.ServiceAsync(SiteDoctorOptions.CloudflaredService, ct);
        var evidence = ServiceEvidence(SiteDoctorOptions.CloudflaredService, service);
        // Probe even when service metadata differs: keep both observations visible.
        var response = await http.GetAsync(options.TunnelReadinessUrl, true, ct);
        evidence["Readiness"] = $"HTTP {response.StatusCode}";
        evidence["Content type"] = response.ContentType ?? "Not provided";
        var ready = false;
        if (TryHealthJson(response, out var json))
        {
            using (json)
            {
                var root = json.RootElement;
                ready = root.TryGetProperty("status", out var s) && s.TryGetInt32(out var code) && code == 200
                    && root.TryGetProperty("readyConnections", out var c) && c.TryGetInt32(out var count) && count > 0;
                if (root.TryGetProperty("readyConnections", out var connections) && connections.TryGetInt32(out var number))
                    evidence["Ready connections"] = number.ToString();
            }
        }
        var healthy = Running(service) && ready;
        return Result(ComponentId.Tunnel, healthy ? HealthStatus.Healthy : HealthStatus.Failed,
            healthy ? "نفق Cloudflare متصل." : "نفق Cloudflare غير جاهز للاتصال.", evidence);
    }

    private async Task<DiagnosticResult> GatewayAsync(CancellationToken ct)
    {
        var task = await system.ScheduledTaskAsync(SiteDoctorOptions.PrintGatewayTask, ct);
        var evidence = TaskEvidence(SiteDoctorOptions.PrintGatewayTask, task);
        var tcp = await system.TcpAsync(new Uri(options.PrintGatewayHealthUrl).Port, ct);
        evidence["TCP"] = tcp ? "Open" : "Closed";
        var response = await http.GetAsync(options.PrintGatewayHealthUrl, true, ct);
        evidence["Health"] = $"HTTP {response.StatusCode}";
        evidence["Content type"] = response.ContentType ?? "Not provided";
        var ok = false;
        var match = false;
        if (TryHealthJson(response, out var json))
        {
            using (json)
            {
                ok = IsOk(json.RootElement);
                match = json.RootElement.TryGetProperty("printer", out var printer) && printer.ValueKind == JsonValueKind.String
                    && string.Equals(printer.GetString(), options.PrinterQueueName, StringComparison.OrdinalIgnoreCase);
            }
        }
        evidence["Configured queue"] = options.PrinterQueueName;
        evidence["Queue match"] = match ? "Yes" : "No";
        var status = !tcp || !ok || !match ? HealthStatus.Failed : !task.Exists || task.State != "Running" ? HealthStatus.Warning : HealthStatus.Healthy;
        return Result(ComponentId.PrintGateway, status, status switch
        {
            HealthStatus.Healthy => "بوابة الطباعة تستجيب للطابعة المحددة.",
            HealthStatus.Warning => "البوابة تستجيب، لكن حالة المهمة المجدولة تحتاج مراجعة.",
            _ => !match && ok ? "اسم الطابعة في البوابة لا يطابق الطابعة المحددة." : "بوابة الطباعة لا تستجيب بصورة صحيحة."
        }, evidence);
    }

    private async Task<DiagnosticResult> ServiceAsync(ComponentId component, string name, CancellationToken ct)
    {
        var service = await system.ServiceAsync(name, ct);
        var healthy = Running(service);
        var status = healthy ? HealthStatus.Healthy : component == ComponentId.Guardian ? HealthStatus.Warning : HealthStatus.Failed;
        return Result(component, status, component == ComponentId.Guardian
            ? healthy ? "خدمة مراقبة InterBase تعمل." : "خدمة المراقبة لا تعمل؛ حالة الخادم تُفحص بشكل مستقل."
            : healthy ? "خدمة الطباعة في Windows تعمل." : "خدمة الطباعة في Windows غير متاحة.", ServiceEvidence(name, service));
    }

    private async Task<DiagnosticResult> PrinterAsync(CancellationToken ct)
    {
        var printer = await system.PrinterAsync(options.PrinterQueueName, ct);
        var evidence = new Dictionary<string, string> { ["Queue"] = options.PrinterQueueName, ["Driver"] = printer.Driver, ["Status"] = printer.Status, ["Jobs"] = printer.JobCount.ToString() };
        if (!printer.Exists) return Result(ComponentId.Printer, HealthStatus.Failed, "طابعة Magic Car المحددة غير موجودة في Windows.", evidence);
        var now = DateTimeOffset.Now;
        var concerning = printer.Jobs.Count(j => j.Failed || (!j.Complete && now - j.SubmittedAt > TimeSpan.FromMinutes(options.StaleJobMinutes)));
        evidence["Jobs needing attention"] = concerning.ToString();
        evidence["Retained jobs"] = printer.Jobs.Count(j => j.Retained).ToString();
        evidence["Scope"] = "Windows queue status; physical output is not verified";
        var status = printer.Offline || printer.Error || printer.Blocked ? HealthStatus.Failed
            : printer.Degraded || concerning > 0 ? HealthStatus.Warning : HealthStatus.Healthy;
        return Result(ComponentId.Printer, status, status switch
        {
            HealthStatus.Healthy => "قائمة انتظار الطابعة جاهزة؛ لم يتم إرسال صفحة اختبار.",
            HealthStatus.Warning => "الطابعة متاحة مع تنبيه أو مهام تحتاج مراجعة.",
            _ => "طابعة Epson غير متصلة أو تحتاج تدخلاً. افحص الطاقة والورق والاتصال."
        }, evidence);
    }

    private async Task<DiagnosticResult> AutoPostAsync(CancellationToken ct)
    {
        var task = await system.ScheduledTaskAsync(SiteDoctorOptions.AutoPostTask, ct);
        var evidence = TaskEvidence(SiteDoctorOptions.AutoPostTask, task);
        var processes = await system.ProcessCountAsync("AutoPost", ct);
        evidence["Process count"] = processes.ToString();
        var tcp = await system.TcpAsync(new Uri(options.AutoPostHealthUrl).Port, ct);
        evidence["TCP"] = tcp ? "Open" : "Closed";
        // Watchdog/monitor state is informational until onsite task semantics are inspected.
        foreach (var name in new[] { SiteDoctorOptions.WatchdogTask, SiteDoctorOptions.MonitorTask })
        {
            try { var t = await system.ScheduledTaskAsync(name, ct); evidence[name] = t.Exists ? t.State : "Missing"; }
            catch (Exception ex) when (ex is not OperationCanceledException) { evidence[name] = "Not inspected"; }
        }
        var response = await http.GetAsync(options.AutoPostHealthUrl, true, ct);
        evidence["Health"] = $"HTTP {response.StatusCode}";
        evidence["Content type"] = response.ContentType ?? "Not provided";
        var ok = false;
        if (TryHealthJson(response, out var json)) { using (json) ok = IsOk(json.RootElement); }
        evidence["Scope"] = "HTTP API health only; no ERP transaction was executed";
        var status = !tcp || !ok || processes == 0 ? HealthStatus.Failed
            : processes > 1 || !task.Exists || task.State != "Running" ? HealthStatus.Warning : HealthStatus.Healthy;
        return Result(ComponentId.AutoPost, status, status switch
        {
            HealthStatus.Healthy => "واجهة AutoPost تستجيب.",
            HealthStatus.Warning => "AutoPost يستجيب، لكن المهمة أو عدد العمليات يحتاج مراجعة.",
            _ => "واجهة AutoPost غير متاحة أو لا تستجيب بصورة صحيحة."
        }, evidence);
    }

    private async Task<DiagnosticResult> InterBaseAsync(CancellationToken ct)
    {
        var service = await system.ServiceAsync(SiteDoctorOptions.InterBaseService, ct);
        var evidence = ServiceEvidence(SiteDoctorOptions.InterBaseService, service);
        var tcp = await system.TcpAsync(options.InterBasePort, ct);
        evidence["TCP"] = tcp ? "Open" : "Closed";
        evidence["Scope"] = "Service and TCP only; no database login or query";
        var healthy = Running(service) && tcp;
        return Result(ComponentId.InterBase, healthy ? HealthStatus.Healthy : HealthStatus.Failed,
            healthy ? "خادم InterBase يعمل ومنفذه متاح." : "خدمة قاعدة بيانات العربي أو منفذها غير متاح.", evidence);
    }

    private static bool Running(ServiceSnapshot s) => s.Exists && s.State == "Running";
    private static Dictionary<string, string> ServiceEvidence(string name, ServiceSnapshot s) => new() { ["Service"] = name, ["State"] = s.Exists ? s.State : "Missing" };
    private static Dictionary<string, string> TaskEvidence(string name, TaskSnapshot t) => new() { ["Task"] = name, ["State"] = t.Exists ? t.State : "Missing", ["Last result"] = $"0x{t.LastResult:X8}" };
    private static bool IsOk(JsonElement root) => root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    private static bool TryHealthJson(HttpSnapshot response, out JsonDocument json)
    {
        json = null!;
        if (response.StatusCode != 200 || !HttpProbe.IsJson(response.ContentType) || response.Body is null) return false;
        try
        {
            json = JsonDocument.Parse(response.Body, new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind == JsonValueKind.Object) return true;
            json.Dispose(); return false;
        }
        catch (JsonException) { return false; }
    }
    private static DiagnosticResult Result(ComponentId id, HealthStatus status, string message, Dictionary<string, string> evidence) =>
        new(id, status, message, evidence, 0, DateTimeOffset.Now,
            id switch { ComponentId.Tunnel => RecoveryActionId.RestartCloudflared, ComponentId.Spooler => RecoveryActionId.RestartSpooler, _ => null });
}
