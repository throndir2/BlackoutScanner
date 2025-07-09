using System;
using BlackoutScanner.Interfaces;
using Serilog;
using Serilog.Events;

namespace BlackoutScanner.Infrastructure
{
    public class LoggerWithEvents : BlackoutScanner.Interfaces.ILogger
    {
        private readonly Serilog.ILogger _serilogLogger;
        public event Action<string>? LogMessage;

        public LoggerWithEvents(Serilog.ILogger serilogLogger)
        {
            _serilogLogger = serilogLogger ?? throw new ArgumentNullException(nameof(serilogLogger));
        }

        public void Information(string messageTemplate, params object[] propertyValues)
        {
            _serilogLogger.Information(messageTemplate, propertyValues);
            RaiseLogMessage(LogEventLevel.Information, messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object[] propertyValues)
        {
            _serilogLogger.Warning(messageTemplate, propertyValues);
            RaiseLogMessage(LogEventLevel.Warning, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object[] propertyValues)
        {
            _serilogLogger.Error(messageTemplate, propertyValues);
            RaiseLogMessage(LogEventLevel.Error, messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            _serilogLogger.Error(exception, messageTemplate, propertyValues);
            RaiseLogMessage(LogEventLevel.Error, $"{messageTemplate} - Exception: {exception.Message}", propertyValues);
        }

        public void Debug(string messageTemplate, params object[] propertyValues)
        {
            _serilogLogger.Debug(messageTemplate, propertyValues);
            RaiseLogMessage(LogEventLevel.Debug, messageTemplate, propertyValues);
        }

        private void RaiseLogMessage(LogEventLevel level, string messageTemplate, params object[] propertyValues)
        {
            try
            {
                // Format the message
                string formattedMessage;
                if (propertyValues.Length > 0)
                {
                    formattedMessage = string.Format(messageTemplate.Replace("{", "{{").Replace("}", "}}"), propertyValues);
                }
                else
                {
                    formattedMessage = messageTemplate;
                }

                // Add timestamp and level
                string finalMessage = $"[{DateTime.Now:HH:mm:ss}] [{level}] {formattedMessage}";

                // Raise event
                LogMessage?.Invoke(finalMessage);
            }
            catch
            {
                // Swallow exceptions in logging
            }
        }
    }
}
