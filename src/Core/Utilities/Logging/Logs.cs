using Microsoft.Extensions.Logging;

namespace DatasetStudio.Core.Utilities.Logging;

/// <summary>
/// Thin static wrapper around a real <see cref="ILogger"/>, used by the ~40 call sites across
/// Core/ClientApp that predate this project's real logging pipeline. Call <see cref="Initialize"/>
/// once at startup (both APIBackend's and ClientApp's Program.cs do) so these calls route
/// through the app's actual configured logging (respects Logging:LogLevel, and in WASM goes to
/// the browser console via the built-in provider) instead of a hardcoded Console.WriteLine.
/// Converting every call site to constructor-injected ILogger&lt;T&gt; instead of this wrapper
/// was considered and rejected for Core/ClientApp specifically: ~280 call sites across ~35
/// files, many of them static utility classes with no DI-constructed instance to inject into --
/// out of proportion to what a hardening pass should touch. APIBackend's own services use real
/// ILogger&lt;T&gt; injection; this wrapper is only for the shared/client side.
/// </summary>
public static class Logs
{
    // Console fallback (not NullLogger) until Initialize() runs, so nothing logged during the
    // narrow startup window before DI is available (see ServiceCollectionExtensions.
    // AddDatasetServices, called pre-Build()) is silently dropped.
    private static ILogger _logger = new ConsoleFallbackLogger();

    /// <summary>Routes all subsequent Logs.* calls through a real logger. Call once at startup.</summary>
    public static void Initialize(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("DatasetStudio");
    }

    /// <summary>Logs an informational message</summary>
    public static void Info(string message) => _logger.LogInformation("{Message}", message);

    /// <summary>Logs a warning message</summary>
    public static void Warning(string message) => _logger.LogWarning("{Message}", message);

    /// <summary>Logs an error message</summary>
    public static void Error(string message) => _logger.LogError("{Message}", message);

    /// <summary>Logs an error message with exception details</summary>
    public static void Error(string message, Exception exception) => _logger.LogError(exception, "{Message}", message);

    /// <summary>Logs a debug message. Filtered by the configured minimum log level, not a DEBUG build check.</summary>
    public static void Debug(string message) => _logger.LogDebug("{Message}", message);

    private sealed class ConsoleFallbackLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string line = $"[{logLevel}] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {formatter(state, exception)}";
            if (logLevel >= LogLevel.Error)
            {
                Console.Error.WriteLine(line);
                if (exception is not null)
                {
                    Console.Error.WriteLine($"Exception: {exception.GetType().Name} - {exception.Message}");
                    Console.Error.WriteLine($"StackTrace: {exception.StackTrace}");
                }
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }
}
