# installer Specification

## Purpose
Package the service and tray as a single per-machine x64 MSI (WiX).

## Requirements

### Requirement: Runtime prerequisite
The MSI SHALL block installation unless the .NET Desktop Runtime 10 (x64) is present, while the service ships framework-dependent (NativeAOT deferred).

#### Scenario: Runtime missing
- **WHEN** setup runs without the runtime and the product is not already installed
- **THEN** setup stops with a message naming the required runtime

### Requirement: Service installation
The MSI SHALL install the `WindowsBootSwitcher` service as LocalSystem with automatic delayed start, start it on install, stop it on install and uninstall, and remove it on uninstall.

#### Scenario: Fresh install
- **WHEN** the MSI is installed
- **THEN** the service is registered with `DelayedAutostart = 1` and is running

### Requirement: Tray autostart
The MSI SHALL register the tray to start at logon for all users via `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`.

#### Scenario: User logs on
- **WHEN** any user logs on after install
- **THEN** the tray starts in that user's session

### Requirement: Self-contained packaging build
Building the setup project SHALL publish both executables into `artifacts\` before WiX harvests them, and fail if either executable is missing.

#### Scenario: Clean clone
- **WHEN** the setup project is built from a clean clone
- **THEN** the MSI is produced without a separate publish step

### Requirement: Upgradable versions
The MSI SHALL use a fixed UpgradeCode with MajorUpgrade, block downgrades, and take its version from `SetupVersion` (default `1.0.0.0`).

#### Scenario: Newer version installed
- **WHEN** an older MSI is run over a newer installed version
- **THEN** setup refuses with the downgrade message
