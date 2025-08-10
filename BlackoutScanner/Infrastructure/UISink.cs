using System;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace BlackoutScanner.Infrastructure
{
    public class UISink : ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        private static LogEventLevel _minimumLevel = LogEventLevel.Information;
        public static event Action<string>? LogMessage;

        public static LogEventLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        public UISink(string outputTemplate = "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        {
            _formatter = new MessageTemplateTextFormatter(outputTemplate);
        }

        public void Emit(LogEvent logEvent)
        {
            // Only emit if the log level meets the minimum threshold
            if (logEvent.Level < _minimumLevel)
                return;

            var renderSpace = new System.IO.StringWriter();
            _formatter.Format(logEvent, renderSpace);
            var message = renderSpace.ToString().TrimEnd('\r', '\n');

            LogMessage?.Invoke(message);
        }
    }

    public static class UISinkExtensions
    {
        public static Serilog.LoggerConfiguration UI(
            this Serilog.Configuration.LoggerSinkConfiguration loggerConfiguration,
            string outputTemplate = "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        {
            return loggerConfiguration.Sink(new UISink(outputTemplate));
        }
    }
}
