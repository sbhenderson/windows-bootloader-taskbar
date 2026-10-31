## ADDED Requirements

### Requirement: Refresh on menu open
The tray SHALL request fresh boot state whenever its menu is opened, unless a successful refresh completed within the last 2 seconds. The menu SHALL be rebuilt only when the refreshed state differs from what is shown.

#### Scenario: State changed outside the tray
- **WHEN** the default entry was changed with `bcdedit` and the user opens the menu
- **THEN** the menu updates to show the new default without the user clicking Refresh

#### Scenario: State unchanged
- **WHEN** the menu is opened and the refreshed state equals the displayed state
- **THEN** the menu items are not rebuilt

#### Scenario: Rapid reopen
- **WHEN** the menu is opened again within 2 seconds of a successful refresh
- **THEN** no new request is sent to the service

#### Scenario: Service unavailable
- **WHEN** the menu is opened while the service is unreachable
- **THEN** the existing retry behavior applies and the menu still opens immediately with the last known state
