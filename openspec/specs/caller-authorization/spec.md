# caller-authorization Specification

## Purpose
Restrict who can reach the pipe and what they may do once connected.

## Requirements

### Requirement: Pipe access control list
The pipe SHALL grant full control to SYSTEM and Administrators, read/write to authenticated users, and deny network and anonymous logons.

#### Scenario: Network logon
- **WHEN** a token carrying the NETWORK SID opens the pipe
- **THEN** access is denied by the DACL

### Requirement: Local clients only
The service SHALL serve only clients on the same machine, treating `ERROR_PIPE_LOCAL` from `GetNamedPipeClientComputerName` as local.

#### Scenario: Remote client
- **WHEN** the client computer name differs from the local machine
- **THEN** the response fails with `remote_client_rejected`

### Requirement: Read authorization
The service SHALL let a caller read state when the impersonated token is a local interactive user (INTERACTIVE SID) or a local administrator.

#### Scenario: Standard interactive user reads
- **WHEN** a non-admin interactive user sends `get_state`
- **THEN** the state is returned

#### Scenario: Neither interactive nor admin
- **WHEN** the caller has neither SID
- **THEN** the response fails with `access_denied`

### Requirement: Mutation authorization
The service SHALL allow `set_default_entry` and `set_timeout` only when the impersonated token contains the BUILTIN\Administrators group, checked before the payload is parsed.

#### Scenario: Non-admin mutates
- **WHEN** a non-admin sends a `set_*` command
- **THEN** the response fails with `access_denied` and BCD is not touched

#### Scenario: Admin without elevation (open issue)
- **WHEN** an administrator's tray runs with a UAC-filtered token
- **THEN** Administrators is deny-only and probably absent from `WindowsIdentity.Groups`, so mutation is likely denied
- Note: this needs confirming on hardware. If confirmed, either the policy or the tray launch model must change.
