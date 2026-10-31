# tray-client Specification

## Purpose
Per-user WinForms tray application that shows boot state and sends commands to the service.

## Requirements

### Requirement: Single instance per session
The tray SHALL run at most one instance per logon session, using the `Local\WindowsBootSwitcher.Tray` mutex.

#### Scenario: Second launch
- **WHEN** the tray is started while an instance runs in the same session
- **THEN** the new process exits immediately

### Requirement: Menu reflects state
The tray menu SHALL be built by the pure `TrayMenuBuilder` from an immutable `TrayState`.

#### Scenario: Available
- **WHEN** state is available
- **THEN** the menu shows a disabled "Current default: <name>" header, one checkable item per entry (default checked), a "Timeout" submenu with "Off" and "30 seconds" (current value checked), then "Refresh" and "Exit"

#### Scenario: Unavailable
- **WHEN** the service cannot be reached
- **THEN** a disabled status line is shown first, last-known entries stay visible but disabled, and "Refresh" and "Exit" stay enabled

### Requirement: Connection resilience
The tray SHALL retry `get_state` with exponential backoff from 1 s up to 10 s while the service is unavailable, and notify only once per outage.

#### Scenario: Service starts late
- **WHEN** the service becomes reachable after the tray started
- **THEN** the tray switches to available, resets backoff, and shows a "Connected" notification

#### Scenario: Client timeouts
- **WHEN** a pipe connect takes longer than 250 ms per attempt (6 attempts) or a request exceeds 15 s
- **THEN** the call fails as a transport error and the retry loop handles it

### Requirement: Serialized mutations with feedback
The tray SHALL run at most one mutation at a time, show a success or error notification for each, and refresh state after a success.

#### Scenario: Double click
- **WHEN** a second mutation is clicked while one is in flight
- **THEN** the second click is ignored

#### Scenario: Service rejects change
- **WHEN** a mutation returns `Success = false`
- **THEN** an error notification shows a message formatted from the error code

### Requirement: Never crash
The tray SHALL contain all exceptions from background refreshes and click handlers.

#### Scenario: Unexpected exception
- **WHEN** a handler throws an unexpected exception
- **THEN** the tray shows an error or unavailable state and keeps running

### Requirement: Application icon
The tray SHALL use the embedded `app.ico`, falling back to the system application icon if it cannot be loaded.

#### Scenario: Missing resource
- **WHEN** the embedded icon is missing or invalid
- **THEN** the tray starts with `SystemIcons.Application`
