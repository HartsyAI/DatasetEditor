namespace HartsysDatasetEditor.Core.Utilities;

/// <summary>Custom logging utility for consistent logging across the application. In browser, logs to console.</summary>
public static class Logs
{
    /// <summary>Logs an informational message</summary>
    public static void Info(string message)
    {
        Console.WriteLine($"[INFO] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
    }
    
    /// <summary>Logs a warning message</summary>
    public static void Warning(string message)
    {
        Console.WriteLine($"[WARN] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
    }
    
    /// <summary>Logs an error message</summary>
    public static void Error(string message)
    {
        Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
    }
    
    /// <summary>Logs an error message with exception details</summary>
    public static void Error(string message, Exception exception)
    {
        Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        Console.Error.WriteLine($"Exception: {exception.GetType().Name} - {exception.Message}");
        Console.Error.WriteLine($"StackTrace: {exception.StackTrace}");
    }
    
    /// <summary>Logs a debug message (only in development)</summary>
    public static void Debug(string message)
    {
        #if DEBUG
        Console.WriteLine($"[DEBUG] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        #endif
    }
    
    // TODO: Add support for log levels configuration
    // TODO: Add support for structured logging
    // TODO: Add support for log sinks (file, remote, etc.)
    // TODO: Integration with ILogger when server added
}
