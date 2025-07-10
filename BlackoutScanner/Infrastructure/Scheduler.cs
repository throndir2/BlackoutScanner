using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BlackoutScanner.Interfaces;

namespace BlackoutScanner.Infrastructure
{
    public class Scheduler : IScheduler
    {
        private readonly Dispatcher _dispatcher;

        public Scheduler()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public Scheduler(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public IDisposable SchedulePeriodic(TimeSpan period, Action action)
        {
            var timer = new DispatcherTimer
            {
                Interval = period
            };

            timer.Tick += (sender, e) => action();
            timer.Start();

            return new DisposableAction(() =>
            {
                _dispatcher.Invoke(() =>
                {
                    timer.Stop();
                });
            });
        }

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return Task.Delay(delay, cancellationToken);
        }

        public Task Run(Action action, CancellationToken cancellationToken = default)
        {
            return Task.Run(action, cancellationToken);
        }

        private class DisposableAction : IDisposable
        {
            private readonly Action _action;
            private bool _disposed = false;

            public DisposableAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _action();
                    _disposed = true;
                }
            }
        }
    }
}
