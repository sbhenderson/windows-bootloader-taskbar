# Proposal

## Why

The tray reads boot state only at startup, after its own changes, and on Refresh. A change made elsewhere (`bcdedit`, Windows Update, another user) stays invisible until the user thinks to click Refresh, and the checkmarks lie in the meantime.

## What Changes

- Opening the tray menu triggers a background state refresh.
- The menu is not rebuilt if the refreshed state is unchanged, so an open menu does not flicker.
- Refreshes on open are throttled so rapid open/close does not hammer the service.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tray-client`: adds the "Refresh on menu open" requirement.

## Impact

- `src/WindowsBootSwitcher.Tray`: `TrayApplicationContext` (Opening handler, throttle, change detection) and a value-equality helper for `BootState`.
- No service, contract, or installer changes.
