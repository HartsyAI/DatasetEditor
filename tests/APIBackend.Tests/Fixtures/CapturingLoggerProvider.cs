using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace DatasetStudio.Tests.APIBackend.Fixtures
{
    /// <summary>
    /// Records every log entry the API writes, so a test can assert that a security refusal was
    /// logged (audit F-4: refusals used to answer 404 with no log at all). Register it through
    /// <see cref="DatasetStudioApiFactory.ConfigureTestServices"/> as an <see cref="ILoggerProvider"/>.
    /// </summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        /// <summary>Everything logged so far, in order.</summary>
        public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToList();

        /// <summary>Warnings (or worse) whose rendered message contains every given fragment.</summary>
        public IReadOnlyList<CapturedLogEntry> WarningsContaining(params string[] fragments)
        {
            return _entries
                .Where(e => e.Level >= LogLevel.Warning && fragments.All(f => e.Message.Contains(f, StringComparison.Ordinal)))
                .ToList();
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, _entries);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<CapturedLogEntry> _entries;

            public CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _entries.Enqueue(new CapturedLogEntry(_category, logLevel, formatter(state, exception)));
            }
        }
    }

    /// <summary>One captured log entry.</summary>
    public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);
}
