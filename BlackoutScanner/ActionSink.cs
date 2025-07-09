using Serilog.Core;
using Serilog.Events;
using System;

namespace BlackoutScanner
{
    public class ActionSink : ILogEventSink
    {
        private readonly Action<string> _action;

        public ActionSink(Action<string> action)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
            var message = logEvent.RenderMessage();
            _action(message);
        }
    }
}
