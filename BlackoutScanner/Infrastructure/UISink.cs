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
        private static string? _lastMessage = null;
        private static int _duplicateCount = 0;
        private static readonly object _lock = new object();

        public static event Action<string>? LogMessage;
        public static event Action<string, int>? UpdateLastMessage; // For updating the last message with counter

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

            // Extract message without timestamp for comparison
            // Format is: [HH:mm:ss] [LEVEL] Message
            // We want to compare everything after the first timestamp bracket
            var messageForComparison = message;
            var firstBracketEnd = message.IndexOf(']');
            if (firstBracketEnd > 0 && firstBracketEnd < message.Length - 1)
            {
                // Get everything after "[HH:mm:ss] " - this is the level + actual message
                messageForComparison = message.Substring(firstBracketEnd + 2); // +2 to skip "] "
            }

            // Variables to store what action to take outside the lock
            bool shouldUpdate = false;
            bool shouldEmit = false;
            int currentCount = 0;
            string messageToEmit = message;

            lock (_lock)
            {
                // Debug logging
                var isDuplicate = _lastMessage != null && _lastMessage == messageForComparison;
                System.Diagnostics.Debug.WriteLine($"[UISink] Emit - Full: '{message.Substring(0, Math.Min(50, message.Length))}...'");
                System.Diagnostics.Debug.WriteLine($"[UISink] Compare: '{messageForComparison.Substring(0, Math.Min(50, messageForComparison.Length))}...'");
                System.Diagnostics.Debug.WriteLine($"[UISink] LastMsg: '{(_lastMessage ?? "NULL").Substring(0, Math.Min(50, (_lastMessage ?? "NULL").Length))}...'");
                System.Diagnostics.Debug.WriteLine($"[UISink] IsDuplicate: {isDuplicate}, Count: {_duplicateCount}");

                // Check if this message is the same as the last one (ignoring timestamp)
                if (isDuplicate)
                {
                    // Increment duplicate counter
                    _duplicateCount++;
                    currentCount = _duplicateCount;
                    shouldUpdate = true;
                    System.Diagnostics.Debug.WriteLine($"[UISink] -> shouldUpdate=true, count={currentCount}");
                }
                else
                {
                    // Different message - reset counter and emit new message
                    _duplicateCount = 1;
                    _lastMessage = messageForComparison;
                    shouldEmit = true;
                    System.Diagnostics.Debug.WriteLine($"[UISink] -> shouldEmit=true (new message)");
                }
            }

            // Invoke events OUTSIDE the lock to prevent deadlock
            if (shouldUpdate)
            {
                System.Diagnostics.Debug.WriteLine($"[UISink] Invoking UpdateLastMessage with count {currentCount}");
                UpdateLastMessage?.Invoke(messageToEmit, currentCount);
            }
            else if (shouldEmit)
            {
                System.Diagnostics.Debug.WriteLine($"[UISink] Invoking LogMessage");
                LogMessage?.Invoke(messageToEmit);
            }
        }

        /// <summary>
        /// Reset the duplicate tracking (useful when clearing the log)
        /// </summary>
        public static void ResetDuplicateTracking()
        {
            lock (_lock)
            {
                _lastMessage = null;
                _duplicateCount = 0;
            }
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
