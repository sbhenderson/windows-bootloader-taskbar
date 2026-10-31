# Tasks

## 1. Change detection

- [x] 1.1 Add `BootStateComparer.AreEquivalent` with unit tests (equal, different default, different timeout, reordered/renamed entries, nulls)
- [x] 1.2 Skip `RebuildMenu` in `RefreshStateAsync` when status and state are unchanged

## 2. Refresh on open

- [x] 2.1 Add `MenuOpenRefreshPolicy` (injectable `TimeProvider`, 2-second window) and record each successful refresh
- [x] 2.2 Handle `ContextMenuStrip.Opening`: queue a refresh unless within the 2-second throttle; never cancel the open
- [x] 2.3 Unit tests for the policy (first open, inside and after the window, failures do not throttle) and the comparer. The WinForms wiring itself has no test harness and is covered by the manual check

## 3. Verification

- [ ] 3.1 Cross-build with 0 warnings; CI green
- [ ] 3.2 Manual check on Windows: change default with `bcdedit`, open menu, see update
