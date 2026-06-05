using System;
using System.IO;

namespace Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".steamlink",
        "logs"
    );

    private static readonly string LogFile = Path.Combine(
        LogDir,
        $"steamlink-{DateTime.Now:yyyy-MM-dd}.log"
    );

    static Logger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);
        }
        catch { }
    }

    public static void Log(LogLevel level, string message, string? subsystem = null)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var subsystemTag = subsystem != null ? $"[{subsystem}]" : "";
        var levelTag = $"[{level}]";
        var logMessage = $"{timestamp} {levelTag} {subsystemTag} {message}";

        try
        {
            File.AppendAllText(LogFile, logMessage + Environment.NewLine);
        }
        catch { }
    }

    public static void Debug(string message, string? subsystem = null)
        => Log(LogLevel.Debug, message, subsystem);

    public static void Info(string message, string? subsystem = null)
        => Log(LogLevel.Info, message, subsystem);

    public static void Warning(string message, string? subsystem = null)
        => Log(LogLevel.Warning, message, subsystem);

    public static void Error(string message, string? subsystem = null)
        => Log(LogLevel.Error, message, subsystem);
}
