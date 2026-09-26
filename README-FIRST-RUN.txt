MAGIC CAR SITE DOCTOR — v0.1.0 — READ-ONLY VALIDATION BUILD

هذه نسخة تشخيص فقط. جميع عمليات إعادة التشغيل معطلة، بما فيها سطر الأوامر.
لا يشير نجاح التجميع أو اختبارات Core إلى اختبار التطبيق فعلياً على Windows.

First run (use a development Windows PC before the onsite machine):
1. Extract the entire ZIP to a local folder. Do not run the EXE from inside the ZIP.
2. Windows x64 and Microsoft Edge WebView2 Evergreen Runtime are required.
   The .NET 10 runtime is included. WebView2 is a separate prerequisite.
   If missing, obtain the Evergreen Runtime from Microsoft's official page:
   https://developer.microsoft.com/microsoft-edge/webview2/
   Have WebView2 installed before testing offline launch. No UI assets require Internet.
3. Double-click MagicCar.SiteDoctor.exe as your normal Windows user.
4. Click بدء الفحص. Click a component to inspect its evidence.
5. Services/tasks/queue absent on a development PC should be reported as such.

Before onsite use, review the source implementation report and run Windows CI.
The first onsite test must be read-only and compared with fresh PowerShell evidence.
Do not assume that the September 25 snapshot still represents the machine now.

Reference snapshot:
- Cloudflare unauthenticated HTTP 403 is expected edge/access reachability.
- Tunnel readiness requires status 200 and readyConnections > 0.
- Print Gateway and Spooler were healthy; Epson queue was Offline/Error.
- AutoPost HTTP health and InterBase service/TCP checks were healthy.

Important interpretation:
- A green gateway does not mean the printer is healthy.
- A green Windows print queue does not prove paper came out.
- Service/TCP health does not prove a successful database transaction.
- Edge + tunnel + local-origin checks do not prove authenticated end-to-end routing.
- No credentials, print test, queue deletion, database queries or service restart
  are part of diagnosis.

Configuration: site-doctor.json next to the EXE.
History/logs: %LOCALAPPDATA%\MagicCar\SiteDoctor\
WebView2 profile: %LOCALAPPDATA%\MagicCar\SiteDoctor\WebView2\
History is per Windows user and bounded. Logs do not include HTTP bodies,
process command lines, document names, ERP data or credentials.

All recovery buttons are disabled in this build. Editing recovery.enabled alone
cannot enable them. AutoPost and Print Gateway lifecycle scripts have not been inspected.

Portable build: no installer, service, startup task or desktop shortcut is added.
Close the app before removing its extracted folder. Removing the app does not
remove any existing Magic Car services, printer queues, tasks or ERP files.

This application is unsigned. Windows runtime, WebView2 integration, device API
behavior and onsite diagnosis still require Windows verification.
