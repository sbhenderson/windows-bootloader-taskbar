# Tray app menu/state design

## Goal

Build the Windows tray frontend that:
- runs as a tray-only WinForms app
- reads boot state from the service over named pipes
- renders a pure, testable tray menu model
- shows success/failure notifications
- retries startup connection attempts in the background until the service is available

## Recommended approach

### 1) Pure menu model + WinForms renderer
**Recommended.** `TrayMenuBuilder` will convert `TrayState` into a small immutable menu model. `TrayApplicationContext` will render that model into `ContextMenuStrip` items and wire click handlers.

**Why:** menu behavior stays easy to test without UI plumbing, and the tray context stays focused on orchestration.

### 2) Build `ContextMenuStrip` directly in the builder
Less code, but the menu logic becomes harder to test and mixes state decisions with WinForms concerns.

### 3) Use a hidden form instead of `ApplicationContext`
Works, but it adds unnecessary UI surface. `ApplicationContext` is the simpler fit for a tray-only app.

## Components

### `TrayState`
Immutable UI state with:
- connection status
- status message for unavailable/connecting states
- optional `BootState`

The state layer will explicitly represent:
- connecting
- available
- unavailable / retrying

### `TrayMenuBuilder`
Pure mapper from `TrayState` to a menu model.

Menu rules:
- show a disabled status item when the service is unavailable
- show a disabled header for the current default entry
- show one enabled item per available boot entry
- show a timeout submenu with `Off` and `30 seconds`
- include `Refresh` and `Exit`
- disable mutation actions when the service is unavailable

### `BootSwitchClient`
Small abstraction for the tray app’s IPC needs:
- get current state
- set default boot entry
- set timeout

Transport failures are surfaced as exceptions so the tray context can mark the service unavailable and keep retrying.

### `NamedPipeBootSwitchClient`
Implements the stateless request/response protocol:
- one JSON request per pipe connection
- one JSON response per connection
- uses `ContractsJsonContext`
- retries connection with bounded backoff before failing

### `TrayNotificationService`
Wrapper over `NotifyIcon` balloon tips for:
- success messages
- failure messages
- unavailable-service messaging

### `TrayApplicationContext`
Owns:
- `NotifyIcon`
- menu rebuilding
- startup/background retry scheduling
- refresh / mutation orchestration
- exit shutdown

Flow:
1. start with a disabled “connecting” / unavailable state
2. try to load state
3. on success, rebuild the menu from the current state
4. on transport failure, show unavailable status and retry with backoff
5. after successful mutations, show a success notification and refresh state again

## Error handling

- transport/connectivity failure: mark the tray unavailable, disable mutation actions, retry
- service failure response: show the service error message in a failure notification
- successful mutation: always refresh state afterward
- exit: dispose the icon, cancel retry work, and stop the app cleanly

## Testing

### `TrayStateTests`
Verify state factories/projections for:
- connecting/unavailable states
- connected state wrapping `BootState`

### `TrayMenuBuilderTests`
Verify menu structure for:
- connected state
- unavailable state
- timeout submenu selection
- current default header and entry list
- refresh/exit presence

## Out of scope

- installer changes
- service-host changes
- changing the named-pipe protocol
- adding extra boot commands beyond the existing service contract
