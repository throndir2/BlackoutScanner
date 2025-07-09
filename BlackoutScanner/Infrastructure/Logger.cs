using System;
using BlackoutScanner.Interfaces;
using Serilog;
using ILogger = BlackoutScanner.Interfaces.ILogger;

namespace BlackoutScanner.Infrastructure
{
    public class Logger : ILogger
    {
        private readonly Serilog.ILogger _logger;
        public event Action<string>? LogMessage;

        public Logger(Serilog.ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Information(string messageTemplate, params object[] propertyValues)
        {
            _logger.Information(messageTemplate, propertyValues);
            LogMessage?.Invoke($"[INFO] {string.Format(messageTemplate, propertyValues)}");
        }

        public void Warning(string messageTemplate, params object[] propertyValues)
        {
            _logger.Warning(messageTemplate, propertyValues);
            LogMessage?.Invoke($"[WARN] {string.Format(messageTemplate, propertyValues)}");
        }

        public void Error(string messageTemplate, params object[] propertyValues)
        {
            _logger.Error(messageTemplate, propertyValues);
            LogMessage?.Invoke($"[ERROR] {string.Format(messageTemplate, propertyValues)}");
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            _logger.Error(exception, messageTemplate, propertyValues);
            LogMessage?.Invoke($"[ERROR] {string.Format(messageTemplate, propertyValues)} - Exception: {exception.Message}");
        }

        public void Debug(string messageTemplate, params object[] propertyValues)
        {
            _logger.Debug(messageTemplate, propertyValues);
            LogMessage?.Invoke($"[DEBUG] {string.Format(messageTemplate, propertyValues)}");
        }
    }
}
