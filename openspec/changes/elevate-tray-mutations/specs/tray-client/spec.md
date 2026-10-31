## ADDED Requirements

### Requirement: Elevated mutations
When the tray is not elevated, it SHALL perform each boot-configuration change by starting an elevated copy of itself through a UAC prompt. That copy sends the single change to the service and exits with a code that identifies the outcome. Reading state SHALL never prompt.

#### Scenario: Non-elevated admin changes the default
- **WHEN** a non-elevated administrator selects a boot entry and approves the UAC prompt
- **THEN** the change is applied, a success notification is shown, and state is refreshed

#### Scenario: Prompt declined
- **WHEN** the user declines the UAC prompt
- **THEN** no change is made and a non-error "change cancelled" status is shown

#### Scenario: Elevated copy reports a failure
- **WHEN** the service rejects the change made by the elevated copy (for example `entry_not_found`)
- **THEN** the tray shows the error notification for that error code

#### Scenario: Tray already elevated
- **WHEN** the tray itself runs elevated
- **THEN** changes are sent directly with no UAC prompt

#### Scenario: Unsafe argument
- **WHEN** an entry id contains characters that cannot be passed safely on a command line
- **THEN** no process is started and the change fails with `invalid_entry_id`

#### Scenario: Helper mode bypasses single instance
- **WHEN** the elevated copy starts while the normal tray is running
- **THEN** it performs the change and exits without being blocked by the single-instance check
