# Magic Car Site Doctor

Arabic-first Windows desktop diagnostics for Magic Car's onsite dependencies. WPF hosts a bundled, offline WebView2 interface; .NET supplies the actual Windows and HTTP probes. This is a standalone utility with no dependency on the Spring Boot or Flutter repositories.

**Status: development implementation, read-only validation build.** Linux compilation, Core tests, and HTML browser tests have been performed; see [the implementation report](docs/IMPLEMENTATION-REPORT.md). Native Windows execution, Windows CI, and onsite validation are still pending. The supplied Windows ZIP was cross-published on Linux; it is not a Windows-CI-certified artifact.

## Run

Extract the entire `MagicCar.SiteDoctor-win-x64.zip` and open `MagicCar.SiteDoctor.exe` on a development Windows x64 PC. Microsoft Edge **WebView2 Evergreen Runtime** is required separately; the .NET runtime is included. Follow `README-FIRST-RUN.txt` before onsite use.

The UI renders offline. Internet probes naturally fail without connectivity; local checks still run. Opening `Ui/index.html` in a browser displays the interface but cannot inspect Windows. There is no production demo mode, synthetic success data or local HTTP server.

## Build and verify

Install the .NET SDK specified by `global.json` (10.0.401; latest patch allowed).

```powershell
./scripts/build.ps1
./scripts/publish.ps1
```

Equivalent commands:

```sh
dotnet restore MagicCar.SiteDoctor.slnx --locked-mode
dotnet build MagicCar.SiteDoctor.slnx -c Release --no-restore -p:EnableRecovery=false
dotnet test tests/MagicCar.SiteDoctor.Core.Tests/MagicCar.SiteDoctor.Core.Tests.csproj -c Release --no-build
dotnet publish src/MagicCar.SiteDoctor.Windows/MagicCar.SiteDoctor.Windows.csproj -c Release -r win-x64 --self-contained true -p:EnableRecovery=false -p:RestoreLockedMode=true -o artifacts/win-x64
```

Building Windows targets from Linux requires the project's `EnableWindowsTargeting` setting; this does not make WPF runnable on Linux. NuGet lock files are committed. Core targets `net10.0` and has no Windows package dependency; its optional `win-x64` restore identifier keeps the publish lock consistent without changing the portable Core target.

Optional HTML interface checks use Node only as development tooling:

```sh
cd tests/ui
npm ci
npx playwright install chromium
npm test
```

Set `SITE_DOCTOR_CHROME` to an already installed Chromium executable when necessary. These tests inject a synthetic bridge in the test process; they do not ship a fake host inside the UI.

## Structure and ownership

- `src/MagicCar.SiteDoctor.Core`: options, typed results, probe contracts, HTTP checks, classification, orchestration, recovery policy and local history. No Windows APIs.
- `src/MagicCar.SiteDoctor.Windows`: WPF/WebView2 shell, service/process/task/print adapters, native confirmation and narrowly scoped elevation.
- `tests/MagicCar.SiteDoctor.Core.Tests`: fake OS/HTTP adapters exercising the actual Core assembly.
- `tests/ui`: offline presentation and interaction tests.
- `.github/workflows/windows-ci.yml`: Release build, Core tests, self-contained publish, CLI read-only guards and artifact upload on Windows. Triggers for feature/develop and PRs into develop; no deployment job.

## Diagnostics

Nine independently represented components: Internet, Cloudflare edge/access, Tunnel, Print Gateway, Spooler, configured Epson queue, AutoPost, InterBase Server and Guardian. Network checks precede parallel Printing and Al-Arabi branches. Upstream failures do not stop local checks.

Important contracts:

- Expected public 403 is edge reachability, not proof of origin routing. Without Cloudflare response headers it is a warning because edge attribution is unconfirmed. Unexpected unauthenticated 200 is a warning.
- Local health must return HTTP 200, JSON content type and semantic success. Responses are bounded to 16 KiB; raw response content is never logged.
- Tunnel readiness requires a running service, JSON `status:200` and `readyConnections > 0`.
- Gateway and Spooler health are distinct from printer health. Offline/Error blocks the Printing branch; completed retained jobs alone do not imply failure.
- AutoPost health does not certify a database operation. InterBase is checked by service state and local TCP only. Guardian is shown as a supervisory dependency.
- Ready watchdog / disabled historical monitor are informational pending script inspection. A responding API with task metadata discrepancies is a warning; no live capability is invented from task state alone.
- Permission-denied inspection is `NotChecked`, explained to the user; aggregates cannot turn green with missing evidence.

## Recovery boundaries

The delivered build is compiled without `SITE_DOCTOR_RECOVERY`, with `recovery.enabled=false`. All four UI recovery actions are disabled. `--repair` also rejects every recovery action with exit code 2. Editing JSON cannot unlock this binary.

Cloudflared and Spooler recovery implementations exist for later controlled development validation. They stop only fixed services, refuse running dependent services, wait with bounds, and retest rather than trusting an exit code. They require both a deliberate `EnableRecovery=true` development build and configuration opt-in. They have not been runtime-tested and must not be enabled onsite yet.

Print Gateway and AutoPost have disabled contracts/UI only. No stop/start or substitute script implementation was invented: the actual gateway script, AutoPost startup script, watchdog and scheduled-task lifecycle need inspection first. No InterBase or physical-printer restart action exists. No automatic restart, print test, print queue deletion or background monitoring is implemented.

## Configuration, storage and security

`site-doctor.json` sits beside the executable. Configuration contains no credentials. Local probe URLs are restricted to `http://127.0.0.1`; public probes require HTTPS without credentials, query strings or fragments. Unknown configuration properties are rejected. Privileged service/task targets are code constants, not UI or writable-config parameters.

The portable build stores per-user state under `%LOCALAPPDATA%\MagicCar\SiteDoctor`. This avoids requiring administrator access to create a machine-wide writable directory before an installer exists. History retains 30 events; logs retain 14 daily files, bounded per file. WebView2 has its own profile subdirectory. History is operational data, not a live state source; new sessions start unchecked.

WebView2 uses one virtual origin, exact source validation, a fixed message schema, a restrictive CSP, local resources, blocked external navigation/popups/downloads/permissions and no host objects. The host never executes command text from the UI. Document names, process command lines, Cloudflare tokens, Access secrets and ERP payloads are not inspected or logged.

## Development-first workflow

Implementation lives on `feature/site-doctor-v1`, based on `develop`. No remote repository was accessible in this session, so no GitHub repository, push, PR, CI run, merge or deployment is claimed.

The source archive includes a Git bundle to retain the local branch history. It can be restored with:

```sh
git clone -b feature/site-doctor-v1 repository.bundle magic-car-site-doctor
```

Attach a private repository, push the feature branch and develop baseline, open a PR into develop, and obtain Windows CI evidence. Validate the app on a development Windows machine, then compare a read-only onsite run against fresh evidence. Production promotion and any onsite recovery tests require Jehad's explicit approval. Installer work is deferred until after validation.
