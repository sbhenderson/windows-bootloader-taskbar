# Proposal

## Why

With UAC on, an administrator's tray runs with a filtered token where BUILTIN\Administrators is deny-only. `WindowsIdentity.Groups` omits deny-only groups, so the service sees a non-admin and refuses every `set_*` with `access_denied`. Because the tray autostarts non-elevated, boot switching does not work for the normal case.

## What Changes

- The tray performs each mutation through a short-lived copy of itself started with the UAC `runas` verb. That elevated process sends the one command over the existing pipe and reports the outcome through its exit code.
- Declining the UAC prompt is reported as a cancelled change, not an error.
- When the tray is already elevated, it sends mutations directly with no prompt.
- The service authorization policy is unchanged: mutations still require an active (elevated) Administrators group.
- The `access_denied` hint and README troubleshooting text are updated to match.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tray-client`: adds elevated, per-change mutations.
- `caller-authorization`: replaces the open-issue scenario with the confirmed elevated-token rule.

## Impact

- `src/WindowsBootSwitcher.Tray`: new elevation client decorator, helper mode in `Program`, exit-code mapping.
- No contract, service, or installer changes.
