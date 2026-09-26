# Decisions, open verification, and rejected options

## Confirmed scope

- Implement the standalone Windows Site Doctor described in the September 25 report.
- The later implementation update supersedes the original one-project guidance: Core + Windows + Core.Tests; .NET 10; Windows CI; ZIP first; recovery disabled by default; installer deferred.
- Onsite dependencies, queue and endpoint names come from the supplied Windows snapshot. They are historical evidence, not live observations from this workspace.
- Keep the Spring Boot and Flutter applications unchanged; develop first; no production deployment or service mutations.

## Implementation choices

- One combined read-only OS inspector contract is sufficient for the present checks; HTTP and recovery have separate contracts. No additional service container or framework was added.
- Core is an actual referenced `net10.0` assembly, not copied source linked into tests.
- Missing inspection permission is `NotChecked` with an explanation; a complete aggregate with unknown results is Warning. This extends the original status guidance to avoid claiming an unreadable service is broken or healthy.
- The source plan gives both failure examples for task state and a Warning rule for task-metadata differences while an endpoint works. We apply the latter when the actual API/port contract succeeds. Broken API/port/queue semantics remain Failed.
- Expected public 403 with Cloudflare headers is Healthy for the edge check. Without edge attribution it is Warning; neither case verifies authenticated origin routing or the exact Access policy.
- Recovery has a build-time lock as well as configuration opt-in. The shipped binary cannot be unlocked by editing configuration or invoking `--repair`.
- Per-user LocalAppData history is appropriate for a portable, normal-user ZIP. Shared ProgramData directories/ACLs belong to later installer work.
- Missing WebView2 displays an actionable startup message. No second GUI or automatic runtime installer is introduced.

## Still open / unverified

- Native Windows UI launch, virtual-host resource loading, WebView2 behavior and offline startup.
- Read-only Windows adapter behavior under the onsite user's permissions and installed printer driver.
- Exact local endpoint Content-Type headers; the snapshot included bodies and status but not headers.
- Actual Windows product identity (reported product name and build are retained as evidence; no OS upgrade is inferred).
- Current onsite printer state and end-to-end routing. No authenticated public origin probe is included.
- Gateway, AutoPost and watchdog scripts, scheduled-task triggers, clean stop behavior and duplicate-process prevention.
- Windows CI run and Windows-built artifact. The workflow is supplied but has not been executed.
- UAC, both implemented service recovery paths, WebView2 installation, clean install/uninstall, signing and onsite acceptance.

## Rejected / deferred

No web-hosted replacement, Spring module, Flutter Windows feature, Node server, Electron, database, queue, permanent agent, automatic restart, arbitrary command bridge, credentials, physical-print success claim, queue purge, InterBase restart, unverified AutoPost/Gateway restart, installer, or production deployment.
