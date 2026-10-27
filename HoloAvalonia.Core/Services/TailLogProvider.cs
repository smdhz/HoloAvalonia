using Microsoft.Extensions.Logging;
using System;

namespace HoloAvalonia.Services;

public class TailLogProvider : ILoggerProvider
{
    private readonly UtilityService _uiStatusService;

    /// <summary>
    /// Initializes a logger provider that forwards log lines to UI status output.
    /// </summary>
    /// <param name="uiStatusService">Service that stores and exposes recent log lines.</param>
    public TailLogProvider(UtilityService uiStatusService)
    {
        _uiStatusService = uiStatusService;
    }

    /// <summary>
    /// Creates a logger instance for the specified category.
    /// </summary>
    /// <param name="categoryName">Logger category name.</param>
    /// <returns>A logger that writes to the UI log tail.</returns>
    public ILogger CreateLogger(string categoryName) => new UiLogTailLogger(categoryName, _uiStatusService);

    /// <summary>
    /// Disposes this provider instance.
    /// </summary>
    public void Dispose() { }

    private sealed class UiLogTailLogger : ILogger
    {
        private readonly UtilityService _uiStatusService;

        /// <summary>
        /// Initializes a UI log tail logger.
        /// </summary>
        /// <param name="categoryName">Logger category name.</param>
        /// <param name="uiStatusService">Service that receives rendered log lines.</param>
        public UiLogTailLogger(string categoryName, UtilityService uiStatusService)
        {
            _uiStatusService = uiStatusService;
        }

        /// <summary>
        /// Begins a logical logging scope.
        /// </summary>
        /// <typeparam name="TState">The scope payload type.</typeparam>
        /// <param name="state">Scope payload value.</param>
        /// <returns>A no-op disposable scope.</returns>
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        /// <summary>
        /// Indicates whether a log level is enabled.
        /// </summary>
        /// <param name="logLevel">The level to evaluate.</param>
        /// <returns><see langword="true"/> when the level is enabled; otherwise, <see langword="false"/>.</returns>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <summary>
        /// Writes a formatted log entry to the UI log tail.
        /// </summary>
        /// <typeparam name="TState">The state payload type.</typeparam>
        /// <param name="logLevel">Entry severity.</param>
        /// <param name="eventId">Associated event identifier.</param>
        /// <param name="state">State payload.</param>
        /// <param name="exception">Optional exception.</param>
        /// <param name="formatter">Formatter used to render message text.</param>
        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                if (exception is null)
                {
                    return;
                }

                message = exception.Message;
            }

            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            _uiStatusService.PublishLog($"[{logLevel}] {message}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        /// <summary>
        /// Disposes the no-op logging scope.
        /// </summary>
        public void Dispose() { }
    }
}
