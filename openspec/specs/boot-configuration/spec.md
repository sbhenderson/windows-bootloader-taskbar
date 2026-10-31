# boot-configuration Specification

## Purpose
Read and change the Windows boot manager's default entry and menu timeout through the BCD WMI provider (`root\WMI`), restricted to Windows OS loader entries. All BCD access lives in the SYSTEM service behind `IBootConfigurationAdapter`.

## Requirements

### Requirement: Read boot state
The service SHALL return a `BootState` containing the current default entry id, the menu timeout in seconds, and the list of switchable entries.

#### Scenario: Default entry is a Windows loader
- **WHEN** the boot manager's default object element (`0x23000003`) references a Windows OS loader entry
- **THEN** `CurrentDefaultEntryId` is that entry's id and exactly that entry has `IsDefault = true`

#### Scenario: Default entry is not switchable
- **WHEN** the default object references an entry that is not a Windows OS loader
- **THEN** `CurrentDefaultEntryId` is `null` and no entry has `IsDefault = true`

#### Scenario: Timeout element absent
- **WHEN** the boot manager has no timeout element (`0x25000004`)
- **THEN** `TimeoutSeconds` is reported as `0`
- Note: an absent element means the boot manager waits for a selection, so `0` misrepresents it (open issue).

### Requirement: Only Windows OS loader entries are switchable
The service SHALL expose only BCD objects whose `Type` is `0x10200003`, using the description element (`0x12000004`) as the display name.

#### Scenario: Non-Windows entries hidden
- **WHEN** the store contains firmware, recovery, or non-Windows entries
- **THEN** they are omitted from `BootState.Entries`

### Requirement: Set default entry
The service SHALL change the default entry only to an id present in the current switchable entry list, compared case-insensitively.

#### Scenario: Known entry
- **WHEN** `set_default_entry` names a listed entry
- **THEN** the default object element is written and the response state shows that entry as default

#### Scenario: Unknown entry
- **WHEN** the id is not in the switchable list
- **THEN** the response fails with `entry_not_found` and includes the unchanged state

### Requirement: Set menu timeout
The service SHALL support exactly two timeout modes: `Off` (0 seconds) and `ThirtySeconds` (30 seconds).

#### Scenario: Supported mode
- **WHEN** `set_timeout` is called with `Off` or `ThirtySeconds`
- **THEN** the timeout integer element is written and the response state shows the new value

#### Scenario: Unsupported mode
- **WHEN** the mode is any other value
- **THEN** the response fails with `invalid_timeout_mode` and nothing is written

### Requirement: BCD failures map to stable error codes
The adapter SHALL wrap every WMI/BCD failure in `BootConfigurationException` with a stable snake_case code (`wmi_error`, `bcd_object_not_found`, `invalid_entry_id`, `invalid_timeout`), treating a BCD method's boolean `TRUE` return as success.

#### Scenario: WMI failure on read
- **WHEN** reading state through WMI fails
- **THEN** `get_state` returns `Success = false` with `wmi_error` rather than an unhandled exception
