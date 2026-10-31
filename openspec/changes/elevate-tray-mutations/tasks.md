# Tasks

## 1. Helper mode

- [x] 1.1 Add `ElevatedCommand` (argument build/parse, id validation, exit-code table, response mapping)
- [x] 1.2 Handle helper arguments in `Program.Main` before the single-instance mutex

## 2. Tray client

- [x] 2.1 Add `IElevatedProcessLauncher` and a `runas` implementation that maps ERROR_CANCELLED to `elevation_cancelled`
- [x] 2.2 Add the `ElevatingBootSwitchClient` decorator and wire it into `TrayApplicationContext`
- [x] 2.3 Show `elevation_cancelled` as a status message; update the `access_denied` hint

## 3. Verification and docs

- [x] 3.1 Unit tests for `ElevatedCommand` and `ElevatingBootSwitchClient`
- [x] 3.2 Build with zero warnings; update README troubleshooting (tests run in Windows CI only; no WindowsDesktop runtime on the Linux dev host)
- [ ] 3.3 Manual check on Windows: approve, decline, failure, elevated tray (needs hardware)
