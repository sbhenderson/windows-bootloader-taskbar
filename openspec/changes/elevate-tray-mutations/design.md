# Design

## Context

The tray reaches the service only through `IBootSwitchClient`, and `TrayApplicationContext` already turns failed responses into notifications. The service decides authorization from the impersonated token, so a change is authorized exactly when the process that sends it is elevated.

## Goals / Non-Goals

**Goals:** one UAC prompt per change; no change to the service, contracts or installer; testable without UAC.

**Non-Goals:** caching elevation across changes; a separate helper executable; scheduled-task or service-side elevation tricks.

## Decisions

- **Re-launch the tray exe in a helper mode instead of shipping a new exe.** `WindowsBootSwitcher.Tray.exe --elevated-set-default <id>` or `--elevated-set-timeout <Off|ThirtySeconds>`. Helper mode is handled in `Program.Main` before the single-instance mutex and never shows UI. Alternative: a separate helper project. Rejected: it would mean another publish dir and MSI payload for about 50 lines of code.
- **Decorator `ElevatingBootSwitchClient : IBootSwitchClient`.** `GetStateAsync` passes straight through. `Set*` calls the inner client directly when the process is elevated, and otherwise goes through `IElevatedProcessLauncher`. `TrayApplicationContext` changes only in how it is constructed and in treating `elevation_cancelled` as a status message.
- **Outcome through the exit code.** `ElevatedCommand` owns argument parsing and a fixed exit-code table: 0 = success, 2 = transport failure, 3 = bad arguments, 10+n = known service error code n, and 1 = any other failure. The tray maps the code back to a `BootOperationResponse` so the existing formatter produces the message. Alternative: a second pipe or file to return the full response. Rejected: it adds IPC surface for a message the hint table already covers.
- **Launching:** `ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" }` with a hand-built quoted `Arguments` string, then `WaitForExitAsync`. A `Win32Exception` with native error 1223 (ERROR_CANCELLED) means the user declined the prompt.
- **Elevation check:** `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` is true only for an elevated token.

## Risks / Trade-offs

- A UAC prompt on every click is the intended trade-off for safety.
- The service's specific error text is reduced to the code's hint. This is acceptable because every code has a hint.
- An entry id is placed on a command line. Ids are validated (printable, no quotes, backslashes or whitespace, at most 128 characters) before launch. The service validates them again.
