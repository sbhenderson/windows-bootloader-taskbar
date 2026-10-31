## REMOVED Requirements

### Requirement: Upgradable versions
**Reason**: Replaced by "Pipeline-supplied version", which takes the version from the release pipeline.
**Migration**: Pass `-p:Version=X.Y.Z` instead of `-p:SetupVersion=...`.

## ADDED Requirements

### Requirement: Pipeline-supplied version
The MSI SHALL use a fixed UpgradeCode with MajorUpgrade, block downgrades, and take its product version from the build's `Version` property (default `1.0.0` for local builds).

#### Scenario: Newer version installed
- **WHEN** an older MSI is run over a newer installed version
- **THEN** setup refuses with the downgrade message

#### Scenario: Upgrade
- **WHEN** MSI `1.2.4` is run over installed `1.2.3`
- **THEN** the old version is removed and the new one installed, with the service running afterwards
