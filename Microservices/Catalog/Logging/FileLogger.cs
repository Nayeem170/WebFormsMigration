using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Catalog.Logging
{
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private static readonly object Gate = new();
        private readonly string _path;

        public FileLoggerProvider(string path) => _path = path;

        public ILogger CreateLogger(string categoryName) => new FileLogger(_path);

        public void Dispose() { }

        private sealed class FileLogger : ILogger
        {
            private readonly string _path;

            public FileLogger(string path) => _path = path;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var line = string.Format("{0:u} {1} {2}", DateTime.Now, logLevel, formatter(state, exception));
                if (exception != null)
                    line += Environment.NewLine + exception;
                lock (Gate)
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
        }
    }
}
