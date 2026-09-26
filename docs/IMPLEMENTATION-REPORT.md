# Magic Car Site Doctor — Implementation handoff

Prepared 2026-09-26 (Asia/Hebron). Development build **0.1.0**.

**Outcome:** the standalone read-only Windows utility has been implemented and cross-published. It is ready for source review and development Windows verification. Native Windows and onsite acceptance remain outstanding; this is not a production-readiness claim.

## Repository and provenance

- Branch: `feature/site-doctor-v1`.
- Application/CI implementation commit: `a3ed23440fd2d61fedeb5a7c33385bcbd52c142f`.
- Base: local `develop`, commit `d0276b4` (empty standalone baseline).
- The subsequent handoff commit contains this report and the publish inventory only. The complete history is retained in `repository.bundle` inside the source ZIP; the bundle's HEAD identifies the delivered source revision.
- No remote configured. No GitHub repository, push, pull request, Windows CI execution, merge into develop/main, deployment, or onsite service change occurred.
- The interrupted final stage was resumed from existing files. Final build/test logs were inspected and retained. Source code was unchanged after those checks; only packaging/CI checks and the handoff documentation were finalized.

## What changed

The application now provides nine real diagnostic checks, a bundled Arabic RTL interface with topology/status animation, expandable safe evidence, independent Printing/Al-Arabi summaries, per-user bounded history, and manual retest.

`Core` owns classification/orchestration/configuration/probe contracts. `Windows` references Core and supplies WPF, WebView2, ServiceController, Task Scheduler COM, process count, TCP and Windows print-queue adapters. Core tests reference the actual Core assembly. No business-backend/client code was changed.

The September 25 reference case is covered: the gateway and Spooler can remain Healthy while the configured Epson queue is Offline/Error and Printing is Failed. Guardian is explicitly supervisory. Internet/edge/tunnel failures do not stop local checks.

## Frameworks and dependencies

| Project/tool | Target / pinned version |
| --- | --- |
| SDK | .NET SDK 10.0.401; global.json allows latest patch only |
| Core | net10.0; no NuGet package dependencies |
| Windows executable | net10.0-windows; WPF; win-x64 publish |
| Published runtime | .NET / Windows Desktop 10.0.12, self-contained |
| Microsoft.Web.WebView2 SDK | 1.0.4191.47 |
| System.ServiceProcess.ServiceController | 10.0.12 |
| Core test target | net10.0 |
| xUnit | 2.9.3 |
| xunit.runner.visualstudio | 4.0.0 |
| Microsoft.NET.Test.Sdk | 18.10.1 |
| UI test tooling | Playwright 1.62.1; development-only Node tooling |

NuGet and UI npm lock files are included. WebView2 Evergreen Runtime is a separate Windows prerequisite; it is not bundled in this ZIP. All application HTML/CSS/JS/SVG assets are local.

## Verification evidence

| Level | Result | Limit |
| --- | --- | --- |
| Linux locked NuGet restore | Passed | Restore/build dependencies only |
| Linux Release solution build | Passed; **0 warnings, 0 errors** | Cross-compilation does not run WPF |
| Linux Core tests | **108 passed; 0 failed; 0 skipped** | Fake OS/HTTP adapters; no onsite checks |
| Linux UI tests | **33 assertions passed; 0 failures; 0 browser JS errors; 0 HTTP UI requests** | Headless Chromium with a synthetic host bridge, not native WebView2 |
| Linux Windows x64 self-contained publish | Passed | Windows binary produced on Linux, not Windows CI |
| ZIP verification | Passed; all **417 entries** readable and byte-identical to publish output | Packaging verification only |
| Source/CI inspection | Git whitespace check and YAML/develop/read-only/artifact checks passed | PowerShell scripts and Actions workflow not executed on Windows |
| Windows-CI-verified | **None yet** | Workflow supplied; no remote/CI run available |
| Windows development runtime | **Not verified** | WPF startup, WebView2, native adapters and UAC need a Windows machine |
| Onsite | **Not verified** | No access to RYAH; no live probes or repairs performed |

The attempted `dotnet format whitespace` workspace operation could not run because its build-host named pipe was denied by this environment. No formatting pass is claimed. Git whitespace checks passed. The browser CLI also could not launch its daemon here; the 33 UI checks were executed directly through Playwright instead.

Evidence files in `docs/evidence/` preserve the final restore/build/test/publish logs, the TRX result, UI result JSON, two explicitly synthetic screenshots and the complete publish inventory. Screenshots are labelled **SYNTHETIC TEST FIXTURE** and must not be read as observations from RYAH.

## Exact verification commands

Run from the repository root; this session used its locally installed SDK to execute the same dotnet commands. `-m:1` was used for the solution restore/build in this restricted Linux environment.

```sh
dotnet restore MagicCar.SiteDoctor.slnx --locked-mode -m:1
dotnet build MagicCar.SiteDoctor.slnx -c Release --no-restore -p:EnableRecovery=false -m:1
dotnet test tests/MagicCar.SiteDoctor.Core.Tests/MagicCar.SiteDoctor.Core.Tests.csproj -c Release --no-build --logger "trx;LogFileName=core-tests.trx" --results-directory artifacts/test-results
dotnet publish src/MagicCar.SiteDoctor.Windows/MagicCar.SiteDoctor.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableRecovery=false -p:RestoreLockedMode=true -o artifacts/win-x64
node tests/ui/verify.cjs
git diff --cached --check
```

The UI command requires Playwright and Chromium as documented in README. In this environment `NODE_PATH` pointed to the provided Playwright installation and `SITE_DOCTOR_CHROME` pointed to the downloaded Chromium executable.

Tests cover expected/unexpected edge responses, malformed health contracts, tunnel readiness, gateway/queue mismatches, missing/stopped services, printer failures and retained/stale jobs, independent dependency checks, continued orchestration after failure, concurrent-run rejection, cancellation, read-only recovery policy, repair result/retest behavior, UI message origin/schema checks, configuration validation, HTTP body limits, sensitive-field non-retention and corrupt/bounded history.

## Recovery state

| Action | Implementation | Delivered state |
| --- | --- | --- |
| Restart Cloudflared | Fixed-service stop/start with bounded waits, dependent-service guard, UAC path and retest coordination | Disabled by build and config; native recovery untested |
| Restart Spooler | Same fixed-service safeguards; explicit interruption warning; no queue deletion | Disabled by build and config; native recovery untested |
| Restart Print Gateway | Contract and disabled UI only | Blocked pending actual script/task lifecycle inspection |
| Restart AutoPost | Contract and disabled UI only | Blocked pending startup/watchdog inspection and duplicate-process analysis |
| InterBase/Guardian/physical printer restart | Not implemented, by design | Unavailable |

The package contains `recovery.enabled=false` and was compiled with `EnableRecovery=false`. The executable's repair entry point checks the build policy before any Windows identity/service operation. Editing JSON alone cannot unlock this binary. Tests verify the Core read-only policy; Windows CI additionally contains published-EXE rejection checks for all four repair IDs. Those EXE checks have not run yet.

## Windows CI configuration

`.github/workflows/windows-ci.yml` uses `windows-latest`, read-only repository permissions, a 20-minute job timeout, and feature/develop pushes or PRs targeting develop. It does not deploy anything.

Sequence: checkout; install SDK from global.json; locked restore; Release build; Core tests with TRX; self-contained read-only publish; verify required runtime/UI files and disabled configuration; create/inspect ZIP; emit SHA-256; run published-EXE read-only guard checks; upload ZIP/checksum and test evidence.

PowerShell entry points are `scripts/build.ps1` and `scripts/publish.ps1`. Packaging uses a temporary ZIP and renames it only after validation. Windows CI must be run from an attached private repository before claiming Windows-built evidence.

## Delivered Windows package

- File: `MagicCar.SiteDoctor-win-x64.zip`.
- Size: **63,714,075 bytes** (60.76 MiB).
- SHA-256: `d167bb046209a6b25eeb539af1a87a5d205923d46475da1243a6a71f1daa251e`.
- Full file inventory and per-file hashes: `docs/evidence/publish-inventory.json`.

Main contents:

```text
MagicCar.SiteDoctor.exe
MagicCar.SiteDoctor.dll
MagicCar.SiteDoctor.Core.dll
MagicCar.SiteDoctor.deps.json
MagicCar.SiteDoctor.runtimeconfig.json
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.Wpf.dll
WebView2Loader.dll
coreclr.dll
PresentationFramework.dll
site-doctor.json
Ui/index.html
Ui/app.css
Ui/app.js
README-FIRST-RUN.txt
THIRD-PARTY-NOTICES.md
[remaining self-contained runtime files and notices]
```

The source archive includes the full repository, evidence and a Git bundle. It excludes transient SDK/browser downloads, NuGet build caches, node_modules and application publish binaries. The Windows ZIP is supplied separately.

## Important decisions and remaining risks

- The later implementation update controls: separate Core/Windows projects, .NET 10, Windows CI, ZIP first, all recovery disabled, installer deferred.
- Public 403 is expected edge reachability. Cloudflare headers are required to attribute it to that edge; missing attribution is a Warning. None of these probes verifies authenticated end-to-end origin routing or the exact Access policy.
- An API that responds correctly while task metadata differs is Warning, following the original plan's warning semantics. Broken health/port/queue contracts are Failed.
- Permission-denied inspection is NotChecked and prevents a green aggregate. This avoids reporting an unreadable component as confirmed healthy or broken.
- Health Content-Type must be JSON. The supplied snapshot did not include response headers; those need checking onsite if a real endpoint is classified differently.
- History uses per-user LocalAppData, with 30 events and 14 bounded daily logs. A portable normal-user application does not provision machine-wide ProgramData ACLs.
- The artifact is unsigned. WebView2 presence and Windows native operation remain prerequisites to verify on a development machine.
- AutoPost/Print Gateway scripts remain unavailable; their recovery cannot be completed honestly from this workspace.
- The actual Epson state may have changed since the snapshot. Compare against fresh read-only evidence.

## Next verification gate

Review the source/report; attach a private repository and open the feature PR into develop; obtain Windows CI evidence; launch the read-only app on a development Windows machine with WebView2; verify native API behavior and offline rendering; then perform the separately approved read-only onsite comparison. Enable and validate recovery actions individually only after those gates and explicit authorization. Installer work follows onsite validation.

## Complete repository file tree

Generated bin/obj/artifacts/.git directories are excluded. The Git bundle in the source archive retains repository metadata.

```text
.github/workflows/windows-ci.yml
.gitignore
Directory.Build.props
MagicCar.SiteDoctor.slnx
README-FIRST-RUN.txt
README.md
THIRD-PARTY-NOTICES.md
docs/DECISIONS.md
docs/IMPLEMENTATION-REPORT.md
docs/evidence/core-tests.trx
docs/evidence/final-build.log
docs/evidence/final-publish.log
docs/evidence/final-restore.log
docs/evidence/final-tests.log
docs/evidence/printer-detail.png
docs/evidence/publish-inventory.json
docs/evidence/reference-fixture.png
docs/evidence/ui-results.json
global.json
scripts/build.ps1
scripts/publish.ps1
src/MagicCar.SiteDoctor.Core/Configuration/SiteDoctorOptions.cs
src/MagicCar.SiteDoctor.Core/Configuration/site-doctor.json
src/MagicCar.SiteDoctor.Core/Diagnostics/Adapters.cs
src/MagicCar.SiteDoctor.Core/Diagnostics/DiagnosticEngine.cs
src/MagicCar.SiteDoctor.Core/Diagnostics/DiagnosticOrchestrator.cs
src/MagicCar.SiteDoctor.Core/Diagnostics/HttpProbe.cs
src/MagicCar.SiteDoctor.Core/MagicCar.SiteDoctor.Core.csproj
src/MagicCar.SiteDoctor.Core/Models/Diagnostics.cs
src/MagicCar.SiteDoctor.Core/Recovery/RecoveryCoordinator.cs
src/MagicCar.SiteDoctor.Core/Recovery/RecoveryPolicy.cs
src/MagicCar.SiteDoctor.Core/Storage/HistoryStore.cs
src/MagicCar.SiteDoctor.Core/UiProtocol.cs
src/MagicCar.SiteDoctor.Core/packages.lock.json
src/MagicCar.SiteDoctor.Windows/App.xaml
src/MagicCar.SiteDoctor.Windows/App.xaml.cs
src/MagicCar.SiteDoctor.Windows/MagicCar.SiteDoctor.Windows.csproj
src/MagicCar.SiteDoctor.Windows/MainWindow.xaml
src/MagicCar.SiteDoctor.Windows/MainWindow.xaml.cs
src/MagicCar.SiteDoctor.Windows/Recovery/ElevatedRepair.cs
src/MagicCar.SiteDoctor.Windows/Ui/app.css
src/MagicCar.SiteDoctor.Windows/Ui/app.js
src/MagicCar.SiteDoctor.Windows/Ui/index.html
src/MagicCar.SiteDoctor.Windows/Windows/WindowsSystemInspector.cs
src/MagicCar.SiteDoctor.Windows/app.manifest
src/MagicCar.SiteDoctor.Windows/packages.lock.json
tests/MagicCar.SiteDoctor.Core.Tests/DiagnosticTests.cs
tests/MagicCar.SiteDoctor.Core.Tests/Fakes.cs
tests/MagicCar.SiteDoctor.Core.Tests/MagicCar.SiteDoctor.Core.Tests.csproj
tests/MagicCar.SiteDoctor.Core.Tests/SecurityAndRecoveryTests.cs
tests/MagicCar.SiteDoctor.Core.Tests/packages.lock.json
tests/ui/package-lock.json
tests/ui/package.json
tests/ui/verify.cjs
```

## Primary technical references

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Windows targeting from another OS](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100)
- [WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
- [WebView2 runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Official setup-dotnet action](https://github.com/actions/setup-dotnet)
