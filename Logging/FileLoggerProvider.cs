using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CerberusDashboard.Logging
{
    /// <summary>
    /// Minimal daily-rolling file logger. Writes one file per day
    /// (cerberus-yyyyMMdd.log) and deletes files older than the retention period.
    /// Level filtering is controlled by the "Logging:File:LogLevel" configuration
    /// section (provider alias "File"), falling back to <see cref="MinLevel"/>.
    /// </summary>
    [ProviderAlias("File")]
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        public const string DefaultDirectory = @"C:\ProgramData\CerberusDashboard\logs";

        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
        private readonly object _writeLock = new object();
        private readonly string _directory;
        private readonly int _retentionDays;
        private DateTime _lastCleanupDate = DateTime.MinValue;

        public LogLevel MinLevel { get; }

        public FileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Warning, int retentionDays = 30)
        {
            _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
            _retentionDays = retentionDays < 1 ? 30 : retentionDays;
            MinLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
            => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

        public void Dispose() => _loggers.Clear();

        internal void Write(string category, LogLevel level, string message, Exception exception)
        {
            var now = DateTime.Now;
            var sb = new StringBuilder();
            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(LevelName(level)).Append("] ")
              .Append(category).Append(": ")
              .AppendLine(message);
            if (exception != null)
                sb.AppendLine(exception.ToString());

            lock (_writeLock)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    string path = Path.Combine(_directory,
                        "cerberus-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);

                    if (_lastCleanupDate != now.Date)
                    {
                        _lastCleanupDate = now.Date;
                        DeleteOldFiles(now);
                    }
                }
                catch
                {
                    // Logging must never take the application down.
                }
            }
        }

        private void DeleteOldFiles(DateTime now)
        {
            var cutoff = now.Date.AddDays(-_retentionDays);
            foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("cerberus-*.log")
                         .Where(f => f.LastWriteTime < cutoff))
            {
                try { file.Delete(); } catch { /* in use or no permission */ }
            }
        }

        private static string LevelName(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _category;

            public FileLogger(FileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel)
                => logLevel != LogLevel.None && logLevel >= _provider.MinLevel;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                                    Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                _provider.Write(_category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
