namespace ZKDeviceManager.Devices;

/// <summary>Raised when an SDK call fails; carries the ZK error code from GetLastError.</summary>
public class ZkDeviceException : Exception
{
    public int ErrorCode { get; }

    public ZkDeviceException(string message, int errorCode = 0) : base(BuildMessage(message, errorCode))
    {
        ErrorCode = errorCode;
    }

    private static string BuildMessage(string message, int code)
        => code == 0 ? message : $"{message} (SDK error {code}: {Describe(code)})";

    public static string Describe(int code) => code switch
    {
        -100 => "operation failed or data does not exist",
        -10 => "transmitted data length is incorrect",
        -6 => "device busy or refused the connection (try again)",
        -5 => "data already exists",
        -4 => "not enough space",
        -3 => "size error",
        -2 => "file read/write error",
        -1 => "SDK not initialized (reconnect required)",
        0 => "data not found or repeated",
        1 => "operation is correct",
        4 => "parameter is incorrect",
        101 => "error allocating buffer",
        _ => "unknown"
    };
}
