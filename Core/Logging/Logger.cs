namespace Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static readonly object FileLock = new();
    private static readonly string LogDir = Path.Combine(Environment.CurrentDirectory, "logs");
    private static readonly string LogFile = Path.Combine(LogDir, $"steamlink-{DateTime.UtcNow:yyyy-MM-dd}.log");

    static Logger()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
        }
        catch
        {
            // Logging must never break the shell.
        }
    }

    public static void Debug(string message, string? subsystem = null) => Log(LogLevel.Debug, message, subsystem);
    public static void Info(string message, string? subsystem = null) => Log(LogLevel.Info, message, subsystem);
    public static void Warning(string message, string? subsystem = null) => Log(LogLevel.Warning, message, subsystem);
    public static void Error(string message, string? subsystem = null) => Log(LogLevel.Error, message, subsystem);

    public static void Log(LogLevel level, string message, string? subsystem = null)
    {
        var subsystemTag = string.IsNullOrWhiteSpace(subsystem) ? string.Empty : $" [{subsystem}]";
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level}]{subsystemTag} {message}";

        try
        {
            lock (FileLock)
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging failures to preserve embedded shell stability.
        }
    }
}
