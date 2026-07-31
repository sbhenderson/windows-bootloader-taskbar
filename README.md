# windows-bootloader-taskbar

Windows tray app + SYSTEM service for quickly switching the default Windows boot entry and boot
menu timeout.

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

Stamp a release version (the default is `1.0.0.0`, which cannot be upgraded over):

```powershell
dotnet build installer\WindowsBootSwitcher.Setup\WindowsBootSwitcher.Setup.wixproj `
  -c Release -p:Platform=x64 -t:Rebuild -p:SetupVersion=1.2.3.0
```

Pass `-p:SkipPublishPayloads=true` to package whatever is already in `artifacts\` instead of
republishing.

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
| Boot changes fail with `access_denied` | Only local administrators may mutate boot configuration |
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
- Local interactive users **or** local admins may read (`get_state`); only **local administrators**
  may mutate (`set_*`).
- Requests are capped at 64 KiB, connections time out, and concurrent clients are bounded.

Treat `Security\`, `Ipc\BootCommandRouter.cs`, and `Ipc\NamedPipeBootSwitcherServer.cs` as
auth-sensitive.

### Boot configuration backend

`WmiBootConfigurationAdapter` talks to BCD through the WMI provider in `root\WMI`. Note that BCD
methods return a **boolean** where `TRUE` means success, the default entry is an *object* element
(`0x23000003`) while the timeout is an *integer* element (`0x25000004`), and the boot manager's
timeout element may legitimately be absent. Failures are wrapped in `BootConfigurationException`
with a stable error code that the service maps onto `BootOperationResponse`.
