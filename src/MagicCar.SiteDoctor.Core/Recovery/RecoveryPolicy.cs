using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Recovery;

public sealed record RecoveryCapability(RecoveryActionId Action, bool Enabled, string Reason);
public static class RecoveryPolicy
{
    public static bool BuildAllowsRecovery
    {
        get
        {
#if SITE_DOCTOR_RECOVERY
            return true;
#else
            return false;
#endif
        }
    }
    public static bool IsImplemented(RecoveryActionId action) => action is RecoveryActionId.RestartCloudflared or RecoveryActionId.RestartSpooler;
    public static bool IsEnabled(RecoveryActionId action) => BuildAllowsRecovery && IsImplemented(action);
    public static RecoveryCapability[] Capabilities(bool configured) =>
    [
        new(RecoveryActionId.RestartCloudflared, configured && IsEnabled(RecoveryActionId.RestartCloudflared), "إعادة التشغيل معطلة في نسخة التحقق المخصصة للتشخيص فقط."),
        new(RecoveryActionId.RestartSpooler, configured && IsEnabled(RecoveryActionId.RestartSpooler), "إعادة التشغيل معطلة في نسخة التحقق المخصصة للتشخيص فقط."),
        new(RecoveryActionId.RestartPrintGateway, false, "يلزم فحص سكربت البوابة وإعدادات المهمة قبل تفعيل إعادة التشغيل."),
        new(RecoveryActionId.RestartAutoPost, false, "يلزم فحص سكربت التشغيل وWatchdog لمنع تشغيل نسختين.")
    ];
}
