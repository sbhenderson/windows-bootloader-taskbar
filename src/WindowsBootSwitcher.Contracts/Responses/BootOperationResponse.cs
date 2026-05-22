namespace WindowsBootSwitcher.Contracts.Responses;

public sealed record BootOperationResponse(bool Success, string? ErrorCode, string? ErrorMessage, BootState? State);
