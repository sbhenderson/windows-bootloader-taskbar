## REMOVED Requirements

### Requirement: Mutation authorization
**Reason**: Replaced by "Mutation requires an elevated administrator", which resolves the open UAC question.
**Migration**: None; service behavior is unchanged.

## ADDED Requirements

### Requirement: Mutation requires an elevated administrator
The service SHALL allow `set_default_entry` and `set_timeout` only when the impersonated token contains an enabled BUILTIN\Administrators group (an elevated token), checked before the payload is parsed. A UAC-filtered administrator token, where the group is deny-only, SHALL be treated as non-admin.

#### Scenario: Non-admin mutates
- **WHEN** a non-admin sends a `set_*` command
- **THEN** the response fails with `access_denied` and BCD is not touched

#### Scenario: Filtered admin token
- **WHEN** an administrator sends a `set_*` command from a non-elevated process
- **THEN** the response fails with `access_denied`; the tray avoids this by elevating mutations

#### Scenario: Elevated admin
- **WHEN** an elevated administrator process sends a `set_*` command
- **THEN** the command is processed
