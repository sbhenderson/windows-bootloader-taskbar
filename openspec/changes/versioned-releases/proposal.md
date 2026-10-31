# Proposal

## Why

Every CI-built MSI is version `1.0.0.0`. Windows Installer compares only the first three version fields, so a newer build cannot upgrade an installed older one, and there is no published artifact to install from besides CI run artifacts that expire.

## What Changes

- Pushing a tag `vX.Y.Z` builds an MSI versioned `X.Y.Z`, stamps the same version into both executables, and publishes a GitHub Release with the MSI attached.
- Untagged CI builds are versioned `0.0.<run number>` so they upgrade each other but never outrank a real release.
- The default local build version stays `1.0.0.0` only as a fallback; the README documents the scheme.

## Capabilities

### New Capabilities

- `release-pipeline`: how versions are derived and how releases are published.

### Modified Capabilities

- `installer`: version source changes from a fixed default to the pipeline-supplied version.

## Impact

- `.github/workflows/ci.yml` (version step, tag trigger, release job), `Directory.Build.props` (shared `Version`), installer `SetupVersion` wiring, README.
