# ipc-protocol Specification

## Purpose
Define the local named-pipe protocol between the tray client and the SYSTEM service.

## Requirements

### Requirement: Message-mode named pipe
The service SHALL listen on the named pipe `WindowsBootSwitcher` in message mode, with one JSON request and one JSON response per connection.

#### Scenario: Request envelope
- **WHEN** a client sends `{ "command": "<name>", "payload": { ... } }`
- **THEN** the service replies with exactly one serialized `BootOperationResponse(Success, ErrorCode, ErrorMessage, State)`

### Requirement: Command set
The service SHALL accept the commands `get_state`, `set_default_entry` (payload `EntryId`), and `set_timeout` (payload `Mode`), matched after trimming and lower-casing.

#### Scenario: Unknown command
- **WHEN** the command name is not recognised
- **THEN** the response fails with `unknown_command`

#### Scenario: Missing command
- **WHEN** the envelope is not an object or lacks a non-empty string `command`
- **THEN** the response fails with `invalid_request`

### Requirement: Errors are codes, not exceptions
The service SHALL report every failure as a `BootOperationResponse` with a snake_case `ErrorCode` and never let a request fault the server.

#### Scenario: Malformed input
- **WHEN** the request is invalid JSON or truncated
- **THEN** the response fails with `invalid_request`

#### Scenario: Unexpected failure
- **WHEN** an unexpected exception occurs while handling a request
- **THEN** the response fails with `internal_error` and the error is written to the event log

### Requirement: Resource limits
The service SHALL cap requests at 64 KiB, at most 16 concurrent clients, and 30 seconds per client.

#### Scenario: Oversized request
- **WHEN** a request exceeds 64 KiB
- **THEN** the service stops reading and fails with `request_too_large`

#### Scenario: Stalled client
- **WHEN** a client does not complete its request within 30 seconds
- **THEN** the connection is abandoned, its slot is released, and a warning is logged

### Requirement: Entry id length limit
The service SHALL reject `set_default_entry` ids that are empty or longer than 128 characters with `invalid_request`.

#### Scenario: Overlong id
- **WHEN** `EntryId` is 129 characters
- **THEN** the response fails with `invalid_request`

### Requirement: Source-generated serialization
All IPC types SHALL serialize through `ContractsJsonContext` (System.Text.Json source generation), with no reflection-based serialization.

#### Scenario: Round trip
- **WHEN** any contract type is serialized and deserialized through `ContractsJsonContext`
- **THEN** the result equals the original
