# Design

## Context

`SetupVersion` (default `1.0.0.0`) feeds the WiX `Package/@Version`. The executables carry no explicit version. CI runs on every push and PR with `contents: read`.

## Goals / Non-Goals

**Goals:** one version number from the tag through to the MSI and both exes; upgrades that work; a durable download.

**Non-Goals:** automatic version bumping or changelog generation; signing; prerelease tags (`v1.2.3-rc1` is treated as malformed for now).

## Decisions

- **One `Version` property.** Set `<Version Condition="'$(Version)' == ''">1.0.0</Version>` in `Directory.Build.props`. The SDK derives `FileVersion`/`AssemblyVersion` from it, and the wixproj passes `$(Version)` to WiX as `ProductVersion`. `SetupVersion` is removed so there is one knob. The MSI uses three fields because Windows Installer ignores the fourth.
- **Version computed in one CI step.** A PowerShell step sets `VERSION`: from `GITHUB_REF_NAME` when `GITHUB_REF` starts with `refs/tags/v` (validated against `^v\d+\.\d+\.\d+$`, failing otherwise), else `0.0.$GITHUB_RUN_NUMBER`. Every build/publish/package command gets `-p:Version=$env:VERSION`. Alternative: MinVer or Nerdbank.GitVersioning. Rejected: a new dependency and shallow-clone handling for a three-line rule.
- **Separate `release` job.** It runs `needs: build`, only on `refs/tags/v*`, with `permissions: contents: write` scoped to that job (the workflow default stays read). It downloads the build artifact, renames the MSI to `WindowsBootSwitcher-<version>-x64.msi`, and runs `gh release create <tag> <msi> --generate-notes` using the runner's preinstalled `gh` and `GITHUB_TOKEN`. Alternative: a third-party release action. Rejected: an extra supply-chain dependency for one command.
- **Tag trigger.** Add `tags: ['v*']` under `push`, together with `branches: ['**']`: once `push` lists only tags, GitHub stops running it for branch pushes.

## Risks / Trade-offs

- Dev builds are `0.0.N`, so any release `0.1.0` or later outranks them and upgrades cleanly. The first release must therefore be at least `0.1.0`.
- Re-pushing an existing tag fails `gh release create`. This is intended; delete the release first.
