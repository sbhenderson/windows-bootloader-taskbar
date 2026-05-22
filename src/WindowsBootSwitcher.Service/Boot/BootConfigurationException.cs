namespace WindowsBootSwitcher.Service.Boot;

public sealed class BootConfigurationException : Exception
{
    public BootConfigurationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public BootConfigurationException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
