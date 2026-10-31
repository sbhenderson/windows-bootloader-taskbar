# windows-bootloader-taskbar

Windows tray app + SYSTEM service for quickly switching the default Windows boot entry and boot
menu timeout.

## Current state

Feature-complete against the v1 scope: the service, tray client, contracts, tests, CI, and WiX
installer are all in place on `main`. There are no open `TODO`/`FIXME` markers in the source.

### Known issues / limitations

- **NativeAOT is disabled for the service** (blocked on
  [dotnet/runtime#61960](https://github.com/dotnet/runtime/issues/61960)). Until AOT is viable,
  the installed product requires the **.NET Desktop Runtime 10 (x64)** — the MSI enforces this
  with a launch condition. The design spec carries a status note recording
  that this fallback is in effect.
- **WiX setup project is not in `WindowsBootSwitcher.sln`.** It was removed deliberately
  (commit `b30ecd8`) to avoid project-reference contention on `WindowsBootSwitcher.Contracts`
  during solution builds. Build/package it standalone — see "Package the MSI" below. The
  downside: opening the solution in Visual Studio will not build the installer.
- **Timeout is a two-position toggle** (`Off` / `30 seconds`), not a free-form value. This
  matches the v1 design, but the BCD API supports any integer, so extending it later is
  straightforward.
- **A missing timeout shows as "Off".** If the BCD has no timeout value at all, Windows waits at
  the boot menu indefinitely, but the tray reports `Off`. Choosing either option in the tray writes
  an explicit value and clears this up.
- **No code signing.** The MSI and executables are unsigned, so SmartScreen will warn on
  first install.

## Requirements

- Windows 10 or 11, **x64** (there is no `AnyCPU`/`Win32` configuration)
- .NET SDK 10 to build; **.NET Desktop Runtime 10 (x64)** to run the installed product
- Administrator rights to install, and to *change* boot settings at runtime

## Build and test

```powershell
dotnet restore WindowsBootSwitcher.sln
dotnet build   WindowsBootSwitcher.sln -c Debug -p:Platform=x64
dotnet test    WindowsBootSwitcher.sln -c Debug -p:Platform=x64
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, so any warning fails the build. Package
versions are managed centrally in `Directory.Packages.props`; add a `PackageVersion` there rather
than a `Version` on the `PackageReference`.

Run a subset of tests:

```powershell
dotnet test WindowsBootSwitcher.sln -p:Platform=x64 --filter "FullyQualifiedName~BootCommandRouterTests"
```

## Package the MSI

The installer publishes both executables into `artifacts\` and then packages them, so a single
command works from a clean clone:

```powershell
dotnet build installer\WindowsBootSwitcher.Setup\WindowsBootSwitcher.Setup.wixproj -c Release -p:Platform=x64
```

The MSI is written to `installer\WindowsBootSwitcher.Setup\bin\x64\Release\WindowsBootSwitcher.Setup.msi`.

Stamp a version on the MSI and both executables (local builds default to `1.0.0`):

```powershell
dotnet build installer\WindowsBootSwitcher.Setup\WindowsBootSwitcher.Setup.wixproj `
  -c Release -p:Platform=x64 -t:Rebuild -p:Version=1.2.3
```

Pass `-p:SkipPublishPayloads=true` to package whatever is already in `artifacts\` instead of
republishing.

## Releases and versioning

- **Releases:** push a tag `vMAJOR.MINOR.PATCH` (for example `git tag v0.1.0 && git push origin v0.1.0`).
  CI builds and tests it, then publishes a GitHub Release with `WindowsBootSwitcher-<version>-x64.msi`
  attached. Other tag shapes (`v1.2`, `v1.2.3-rc1`) fail the build and publish nothing.
- **Untagged builds** (branch pushes, PRs) are versioned `0.0.<run number>`. Each upgrades the
  previous one, and any release outranks them.
- Windows Installer compares only the first three version fields, and the MSI refuses to install
  over a newer version. To go back to a dev build after installing a release, uninstall first.

## Install and uninstall

```powershell
msiexec /i WindowsBootSwitcher.Setup.msi /l*v install.log
msiexec /x WindowsBootSwitcher.Setup.msi /l*v uninstall.log
```

The MSI installs the `WindowsBootSwitcher` service (LocalSystem, automatic delayed start) and
registers the tray app to start at logon from the machine-wide `Run` key.

## Verify a deployment

```powershell
Get-Service WindowsBootSwitcher
Get-WinEvent -LogName Application -MaxEvents 20 |
  Where-Object ProviderName -eq 'WindowsBootSwitcher.Service'
```

The tray icon reports connection problems in its menu and retries with backoff, so a stopped
service shows up as "service is not running" rather than a crash.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Setup blocks with a runtime message | .NET Desktop Runtime 10 (x64) is missing |
| Tray says the service is not running | `WindowsBootSwitcher` service is stopped or not installed |
| Boot changes fail with `access_denied` | The account that approved the UAC prompt is not a local administrator |
| Tray says "Change cancelled" | The UAC prompt was declined; nothing was changed |
| Boot changes fail with `wmi_error` | The service could not read/write BCD through WMI |

The service writes diagnostics to the Windows Application event log under the
`WindowsBootSwitcher.Service` source. It never writes to the console.

## Architecture

Three projects share one contract; the two executables never reference each other.

| Project | Purpose |
| --- | --- |
| `src\WindowsBootSwitcher.Contracts` | DTOs and the source-generated `ContractsJsonContext` |
| `src\WindowsBootSwitcher.Service` | SYSTEM service hosting the named pipe server |
| `src\WindowsBootSwitcher.Tray` | WinForms tray client |

Data flows **Tray → named pipe (`WindowsBootSwitcher`, message mode) → Service**. The request
envelope is `{ "command": "<name>", "payload": { ... } }` and the reply is always a serialized
`BootOperationResponse`. Commands: `get_state`, `set_default_entry`, `set_timeout`.

### Security model

- The pipe carries an explicit DACL: SYSTEM and Administrators get full control, authenticated
  users get read/write, and anonymous plus network logons are denied.
- The server verifies the client is local and impersonates the caller to build a `CallerIdentity`.
- Local interactive users **or** local admins may read (`get_state`); only an **elevated** local
  administrator token may mutate (`set_*`). With UAC on, the tray runs non-elevated, so each change
  re-launches `WindowsBootSwitcher.Tray.exe --elevated-set-default <id>` or
  `--elevated-set-timeout <Off|ThirtySeconds>` through a UAC prompt. That copy sends the one command
  and reports the result through its exit code. An elevated tray sends changes directly.
- Requests are capped at 64 KiB, connections time out, and concurrent clients are bounded.

Treat `Security\`, `Ipc\BootCommandRouter.cs`, and `Ipc\NamedPipeBootSwitcherServer.cs` as
auth-sensitive.

### Boot configuration backend

`WmiBootConfigurationAdapter` talks to BCD through the WMI provider in `root\WMI`. Note that BCD
methods return a **boolean** where `TRUE` means success, the default entry is an *object* element
(`0x23000003`) while the timeout is an *integer* element (`0x25000004`), and the boot manager's
timeout element may legitimately be absent. Failures are wrapped in `BootConfigurationException`
with a stable error code that the service maps onto `BootOperationResponse`.
