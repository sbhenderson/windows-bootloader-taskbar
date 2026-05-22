# Windows Boot Switcher Design

## Summary

Windows Boot Switcher is a Windows 10/11 utility that lets a user switch the default Windows boot entry and toggle the boot menu timeout from a notification tray icon. The system uses a privileged Windows service for all boot configuration changes, a per-user tray application for the UI, and an MSI installer for simple deployment.

## Goals

- Let a user quickly switch the default Windows boot entry from the notification area.
- Let a user toggle the Windows boot menu timeout between `0` seconds and `30` seconds.
- Keep all privileged BCD access inside a narrow, testable service boundary.
- Support Windows 10 and Windows 11.
- Produce a simple MSI installer and a GitHub Actions workflow that builds distributable artifacts.

## Non-Goals

- Managing non-Windows boot entries.
- Adding, deleting, renaming, or reordering BCD entries.
- Editing any boot settings other than the default entry and boot menu timeout.
- Building a large desktop UI beyond a tray menu.
- Requiring MSIX packaging for v1.

## Recommended Technology

- **Language/runtime:** C# on .NET 10.
- **Privileged component:** NativeAOT-enabled Windows service.
- **Tray UI:** Minimal WinForms tray application built around `NotifyIcon`.
- **Installer:** MSI built with WiX.
- **CI:** GitHub Actions on Windows runners.
- **Primary architecture target:** x64 for v1.

This is the best fit for the product because Windows service hosting, installer tooling, Event Log integration, and tray icon support are all straightforward in the .NET ecosystem. NativeAOT should be used where it reduces footprint without adding UI framework friction, especially in the service.

## Solution Overview

The application is split into four projects:

1. `WindowsBootSwitcher.Service` - Windows service running as `LocalSystem`
2. `WindowsBootSwitcher.Tray` - per-user tray application
3. `WindowsBootSwitcher.Contracts` - shared request/response contracts
4. `WindowsBootSwitcher.Setup` - MSI packaging project

The service is the only component allowed to read or modify BCD settings. The tray application never talks to BCD directly. It only requests current state and sends a small set of validated commands across a local IPC boundary.

## Components

### WindowsBootSwitcher.Service

Responsibilities:

- Read current boot state
- Enumerate switchable Windows boot entries
- Change the default boot entry
- Change the boot menu timeout between `0` and `30`
- Enforce authorization
- Log operational failures

Internal structure:

- **IPC host** receives requests from the tray application.
- **Authorization layer** validates the caller identity and whether the command is allowed.
- **Boot configuration service** exposes application-level operations such as `GetState`, `SetDefaultEntry`, and `SetTimeout`.
- **BCD adapter** wraps the OS boot configuration tooling so the privileged implementation stays isolated and replaceable.
- **Event logging** writes unexpected failures and rejected privileged operations to the Windows Event Log.

### WindowsBootSwitcher.Tray

Responsibilities:

- Start at user logon
- Show current boot state in the notification area
- Let the user change the default entry
- Let the user toggle timeout between `Off` and `30 seconds`
- Refresh state after every action
- Show clear notifications for failures or permission issues

The tray UI stays intentionally small. It is a tray-only utility, not a multi-window desktop application.

### WindowsBootSwitcher.Contracts

Responsibilities:

- Define strongly typed request and response messages
- Define shared enums and result models
- Keep the IPC boundary explicit and versionable

### WindowsBootSwitcher.Setup

Responsibilities:

- Install and register the Windows service
- Install the tray application
- Register the tray application for user startup
- Support interactive and silent installation
- Produce a single MSI artifact

## IPC and Data Flow

The service exposes a minimal command surface:

- `GetState`
- `SetDefaultEntry(entryId)`
- `SetTimeout(mode)`

Recommended transport: local named pipes.

Startup flow:

1. The tray application starts at logon.
2. It calls `GetState`.
3. The service reads the current boot manager state and Windows boot entries.
4. The service returns the current default entry, the current timeout, and the list of switchable Windows entries.
5. The tray application renders the menu using that state.

Mutation flow:

1. The user selects a boot entry or timeout menu option.
2. The tray application sends one command to the service.
3. The service validates the caller and input.
4. The service applies the change through the BCD adapter.
5. The service returns refreshed state.
6. The tray application updates the menu and shows a short success or failure notification when appropriate.

## Boot Configuration Handling

For v1, the privileged adapter should use the Windows BCD WMI provider in `root\WMI` rather than parsing localized `bcdedit.exe` output. That keeps the risky code path structured and testable while avoiding locale-sensitive text parsing.

NativeAOT remains the preferred deployment target for the service. If WMI interop proves incompatible with NativeAOT during implementation, the fallback is to keep the service contract unchanged and temporarily publish the service as a regular self-contained executable rather than replacing the BCD access strategy with text parsing.

Behavior rules:

- Only Windows OS loader entries are surfaced in the tray menu.
- The selected default entry becomes the persistent boot default until changed again.
- Timeout values are limited to:
  - `Off` -> `0`
  - `30 seconds` -> `30`

Unsupported or unexpected boot configuration states should fail explicitly and surface a clear error rather than being silently ignored.

## Authorization and Security

Authorization policy:

- Any signed-in local user may read current state.
- Only local administrators may request boot configuration changes.

Security design:

- IPC stays local to the machine through named pipes.
- The named pipe must reject remote clients and must not trust any client-declared role or identity data.
- The pipe ACL should allow local interactive users to connect for `GetState`, while the service keeps final authorization decisions server-side for every request.
- The service validates the caller identity for every mutating operation using the Windows access token and local group membership resolved on the service side.
- Requested entry identifiers must match the current enumerated set before a change is applied.
- Unknown timeout values are rejected.
- The tray app must not display success unless the service confirms the change.

Admin/UAC policy for v1:

- A user whose Windows account is a member of the local `Administrators` group may perform changes without relaunching the tray app as elevated.
- A standard user remains read-only.
- This is an intentional product decision to keep the tray workflow fast and simple while still requiring administrator identity for machine-wide changes.

UI behavior for restricted users:

- A non-admin user can see current state.
- Mutating actions are disabled or fail with a clear permission message.

## Windows Runtime Constraints

- The Windows service should use `Automatic (Delayed Start)` so it comes up reliably after boot without competing with earlier startup work.
- The tray application should autostart via a per-machine `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` entry so installed users get the icon automatically at sign-in.
- If the tray app starts before the service is ready, it should enter a degraded state: show the icon, disable mutating actions, surface a `Service starting` or `Service unavailable` status, and retry connection with bounded backoff until the pipe becomes available.
- `Refresh` should always retry an immediate reconnect attempt.

## Tray Menu Design

The tray menu should contain:

- A disabled header showing the current default boot entry
- One selectable item per available Windows boot entry, with the active default checked
- A timeout submenu with:
  - `Off`
  - `30 seconds`
- `Refresh`
- `Exit`

The menu should refresh after each successful change and on explicit user request.

## Error Handling

- Service failures are returned as structured errors to the tray app.
- Unexpected service-side failures are written to the Windows Event Log.
- Permission failures are shown clearly in the tray app.
- Invalid entry identifiers and invalid timeout values are rejected with explicit errors.
- The application should avoid broad fallback behavior that could hide real BCD or permission problems.

## Installer and Deployment

The v1 distribution format is an MSI.

Installer requirements:

- Install the service as `LocalSystem` with `Automatic (Delayed Start)` startup
- Install the tray application binaries
- Register the tray application to start at user logon through `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`
- Support silent deployment for IT or power-user rollout
- Support install, upgrade, repair, and uninstall without orphaning the service or tray startup registration

MSIX is not required for v1 because it adds packaging constraints that are not a good fit for a Windows service.

## CI/CD

GitHub Actions should build the project on a Windows runner and publish installable artifacts.

Recommended workflow stages:

1. Checkout
2. Setup .NET 10
3. Restore dependencies
4. Build solution
5. Run automated tests
6. Publish x64 service and tray binaries
7. Build the MSI
8. Upload build artifacts:
   - x64 published service output
   - x64 published tray output
   - MSI installer

Code signing should be optional in v1:

- Default path: unsigned build artifacts
- Later path: enable signing through repository secrets and workflow conditionals

## Testing Strategy

### Automated tests

- Unit tests for boot entry filtering
- Unit tests for timeout mapping
- Unit tests for request validation and authorization decisions
- Unit tests for IPC contract serialization
- Service-level tests around the boot configuration abstraction
- Tray-facing tests for menu-state generation so checked items, disabled actions, and permission-state UX are validated without relying on WinForms UI automation

### Manual validation

- Windows 10 smoke test
- Windows 11 smoke test
- Installer install/uninstall validation
- Installer install-upgrade-repair-uninstall validation
- Tray app startup at logon validation
- End-to-end default entry switch validation
- End-to-end timeout toggle validation
- Non-admin permission behavior validation

Automated tests should avoid mutating the real system boot store. Real BCD mutation belongs in controlled manual or VM-based validation.

## Initial Project Layout

```text
src/
  WindowsBootSwitcher.Service/
  WindowsBootSwitcher.Tray/
  WindowsBootSwitcher.Contracts/
installer/
  WindowsBootSwitcher.Setup/
tests/
  WindowsBootSwitcher.Service.Tests/
  WindowsBootSwitcher.Contracts.Tests/
```

## Decisions Captured

- Use C# and .NET 10.
- Use a Windows service plus per-user tray application.
- Use the `WindowsBootSwitcher` project and namespace prefix.
- Support Windows 10 and Windows 11.
- Limit v1 scope to switching existing Windows boot entries and toggling timeout.
- Use MSI, not MSIX, for deployment.
- Allow all local users to read state, but require administrator rights for changes.

