using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlackoutScanner.Interfaces
{
    public interface IScheduler
    {
        IDisposable SchedulePeriodic(TimeSpan period, Action action);
        Task Delay(TimeSpan delay, CancellationToken cancellationToken = default);
        Task Run(Action action, CancellationToken cancellationToken = default);
    }
}
