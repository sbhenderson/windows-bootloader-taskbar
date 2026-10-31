## Purpose

Derive product versions from git tags and publish installable releases so installs can be upgraded predictably.

## ADDED Requirements

### Requirement: Version from tag
A build triggered by a tag matching `vMAJOR.MINOR.PATCH` SHALL use `MAJOR.MINOR.PATCH` as the MSI product version and as the file and product version of both executables.

#### Scenario: Release tag
- **WHEN** tag `v1.2.3` is pushed
- **THEN** the MSI version is `1.2.3` and both executables report version `1.2.3`

#### Scenario: Malformed tag
- **WHEN** a tag such as `v1.2` or `release-1` is pushed
- **THEN** the build fails before packaging and no release is created

### Requirement: Development build versions
A build not triggered by a release tag SHALL use version `0.0.<CI run number>`.

#### Scenario: Successive main builds
- **WHEN** run 41 and then run 42 are installed in order
- **THEN** the second install upgrades the first

#### Scenario: Release installed
- **WHEN** a development build is run over an installed release
- **THEN** setup refuses with the downgrade message

### Requirement: Published release
A successful release-tag build SHALL create a GitHub Release named after the tag with the MSI attached, only after build and tests pass.

#### Scenario: Tests fail on a tag
- **WHEN** tests fail for tag `v1.2.3`
- **THEN** no GitHub Release is created

#### Scenario: Successful tag
- **WHEN** tag `v1.2.3` builds and tests pass
- **THEN** release `v1.2.3` exists with `WindowsBootSwitcher-1.2.3-x64.msi` attached
