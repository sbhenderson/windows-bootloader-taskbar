# Copilot instructions for windows-bootloader-taskbar

Windows tray app + SYSTEM service for switching the default Windows boot entry
and boot-menu timeout. Targets **.NET 10**, **x64 only**, Windows.

## Build, test, lint

All commands assume the repo root and the `x64` platform (there is no `AnyCPU`/`Win32`).

```powershell
dotnet restore WindowsBootSwitcher.sln
dotnet build WindowsBootSwitcher.sln -c Debug -p:Platform=x64
dotnet test  WindowsBootSwitcher.sln -c Debug -p:Platform=x64
```

- CI builds `-c Release`; use Release when reproducing CI failures.
- `Directory.Build.props` sets `TreatWarningsAsErrors=true` for every project, so a
  warning fails the build. Nullable reference types and implicit usings are enabled solution-wide,
  and .NET analyzers run at `latest-recommended`.
- NuGet versions are managed centrally in `Directory.Packages.props`. Add a `PackageVersion`
  there and reference the package **without** a `Version` attribute, or restore fails with NU1008.
- Run a single test class or method with a filter:

  ```powershell
  dotnet test WindowsBootSwitcher.sln -p:Platform=x64 --filter "FullyQualifiedName~BootCommandRouterTests"
  dotnet test WindowsBootSwitcher.sln -p:Platform=x64 --filter "DisplayName~rejects_unknown_commands"
  ```

- Build the MSI (publishes service + tray, then packages):

  ```powershell
  dotnet build installer\WindowsBootSwitcher.Setup\WindowsBootSwitcher.Setup.wixproj -c Release -p:Platform=x64
  ```

Tests use **xUnit**. There is no separate linter; the analyzers + warnings-as-errors
during build are the lint gate.

## Architecture: three projects around a shared contract

Data flows **Tray (client) → named pipe → Service (SYSTEM)**. The two executables never
reference each other; they only share `WindowsBootSwitcher.Contracts`.

- **`src/WindowsBootSwitcher.Contracts`** (`net10.0`, library) — DTOs (`BootEntry`,
  `BootState`, `BootMenuTimeoutMode`), request/response records, and
  `ContractsJsonContext`, a `System.Text.Json` **source-generated** serializer context.
  All IPC serialization goes through `ContractsJsonContext.Default.*` — do not add
  reflection-based `JsonSerializer` calls; register new types with a `[JsonSerializable]`
  attribute on `ContractsJsonContext` instead.
- **`src/WindowsBootSwitcher.Service`** (`net10.0-windows`, console exe, runs as the
  `WindowsBootSwitcher` Windows service under `LocalSystem`). Generic-host app
  (`Host.CreateApplicationBuilder` + `AddWindowsService`). `WindowsBootSwitcherWorker`
  (a `BackgroundService`) hosts `NamedPipeBootSwitcherServer`.
- **`src/WindowsBootSwitcher.Tray`** (`net10.0-windows`, WinExe, WinForms) — system-tray
  UI. `TrayApplicationContext` polls the service via `NamedPipeBootSwitchClient` and
  renders menu/state from an immutable `TrayState`.

### IPC wire protocol

Named pipe **`WindowsBootSwitcher`**, `PipeTransmissionMode.Message`. Request envelope is
`{ "command": "<name>", "payload": { ... } }`; response is always a serialized
`BootOperationResponse`. Commands are dispatched in `BootCommandRouter.Route` by lowercased
name: `get_state`, `set_default_entry`, `set_timeout`. To add a command, add the case in
the router, a request record + `[JsonSerializable]` entry in Contracts, and a client method.

### Security model (do not weaken without intent)

- `NamedPipeBootSwitcherServer` rejects non-local clients (`GetNamedPipeClientComputerName`)
  and impersonates the caller (`pipe.RunAsClient`) to build a `CallerIdentity`.
- `CallerAuthorizationPolicy`: local interactive users **or** local admins may **read**
  (`get_state`); only **local administrators** may **mutate** (`set_*`). 🔴 Treat
  `Security/`, the router, and the pipe server as auth-sensitive surface.

### Boot configuration backend

`WmiBootConfigurationAdapter` (`IBootConfigurationAdapter`) talks to BCD via the WMI provider in
`root\WMI` (`BcdStore`/`BcdObject`). Facts that are easy to get wrong here:

- BCD methods are declared `boolean Method(...)` where **`TRUE` means success** — the opposite of
  the classic WMI "`ReturnValue == 0` means success" convention. `BcdValueReader.IsSuccess` owns
  this.
- There is **no** `GetIntegerElement` or `SetDefaultObject` method. Values are *elements* of the
  boot manager object, read via `GetElement` and written via `SetIntegerElement` /
  `SetObjectElement`.
- The default entry is an **object** element (`0x23000003`); the menu timeout is an **integer**
  element (`0x25000004`). The timeout element may legitimately be absent.
- Entries are filtered on `BcdObject.Type == 0x10200003` (the property is `Type`, not
  `ApplicationType`); the display name is the `0x12000004` description element.
- The scope is connected with `ImpersonationLevel.Impersonate` and `EnablePrivileges = true`,
  which BCD requires.

All WMI failures are wrapped in `BootConfigurationException` with a
stable string error code; the service maps these to `BootOperationResponse` error codes.

## Conventions

- **Error handling is code-based, not exception-based across the wire.** Service paths
  return `BootOperationResponse(Success, ErrorCode, ErrorMessage, State?)` with snake_case
  error codes (`access_denied`, `invalid_request`, `unknown_command`, `wmi_error`, ...).
  Reuse existing codes where applicable.
- **DI registration** for new service components goes in `Service/Program.cs` as
  singletons; constructors validate args with `ArgumentNullException`/`ThrowIfNull`.
- **Diagnostics**: the service logs through `EventLogWriter` (Windows Event Log), never
  `Console`. Warnings for rejected/invalid requests, errors for unexpected failures.
- **Immutability**: prefer `record` / `record struct` for state and DTOs (see `TrayState`,
  `CallerIdentity`, contract types); `ImmutableArray<T>` for collections from WMI.
- **Files** are UTF-8 **with BOM**, CRLF line endings, 4-space indent (`.editorconfig`).
- **Namespaces/prefix**: everything is under `WindowsBootSwitcher.*`. Test projects mirror
  the source namespace with a `.Tests` suffix and live under `tests/`.

## Packaging notes

The WiX installer (`installer/WindowsBootSwitcher.Setup`, WiX v4 SDK) `dotnet publish`es
both exes into `artifacts\service` and `artifacts\tray` before packaging, installs the
`WindowsBootSwitcher` service (`LocalSystem`, auto/delayed start), and registers the tray
under the machine-wide `HKLM ...\CurrentVersion\Run` key.
