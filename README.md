# windows-bootloader-taskbar
Windows tray app + SYSTEM service for quickly switching the default Windows boot entry and boot menu timeout.

## Build

```powershell
dotnet build WindowsBootSwitcher.sln -c Release -p:Platform=x64
```

## Package

The CI workflow restores, builds, tests, publishes the service and tray app, and produces the MSI installer artifact.

## Deploy

The MSI installs the `WindowsBootSwitcher` service and registers the tray app to start at logon from the machine-wide Run key.
