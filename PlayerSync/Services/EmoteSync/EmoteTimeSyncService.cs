using MareSynchronos.API.Dto.Emote;
using System.Diagnostics;

namespace MareSynchronos.Services.EmoteSync;

public sealed class EmoteTimeSyncService
{
    private readonly object _timeLock = new();

    private long _estimatedServerUtcTicksAtLastSync;
    private long _localStopwatchTicksAtLastSync;
    private long _lastRoundTripTicks = long.MaxValue;
    private bool _hasSync;

    public bool HasSync
    {
        get
        {
            lock (_timeLock)
            {
                return _hasSync;
            }
        }
    }

    public TimeSpan LastRoundTrip
    {
        get
        {
            lock (_timeLock)
            {
                return _lastRoundTripTicks == long.MaxValue ? TimeSpan.Zero : TimeSpan.FromTicks(_lastRoundTripTicks);
            }
        }
    }

    public void Reset()
    {
        lock (_timeLock)
        {
            _estimatedServerUtcTicksAtLastSync = 0;
            _localStopwatchTicksAtLastSync = 0;
            _lastRoundTripTicks = long.MaxValue;
            _hasSync = false;
        }
    }

    public void AcceptSample(ServerTimeResponseDto response, long clientReceiveUtcTicks, long clientReceiveStopwatchTicks)
    {
        long roundTripTicks = (clientReceiveUtcTicks - response.ClientSendUtcTicks)
            - (response.ServerSendUtcTicks - response.ServerReceiveUtcTicks);

        long offsetTicks =
            ((response.ServerReceiveUtcTicks - response.ClientSendUtcTicks)
            + (response.ServerSendUtcTicks - clientReceiveUtcTicks)) / 2;

        long estimatedServerUtcTicksNow = clientReceiveUtcTicks + offsetTicks;

        lock (_timeLock)
        {
            if (!_hasSync || roundTripTicks < _lastRoundTripTicks)
            {
                _estimatedServerUtcTicksAtLastSync = estimatedServerUtcTicksNow;
                _localStopwatchTicksAtLastSync = clientReceiveStopwatchTicks;
                _lastRoundTripTicks = roundTripTicks;
                _hasSync = true;
            }
        }
    }

    public long GetEstimatedServerUtcTicksNow()
    {
        lock (_timeLock)
        {
            if (!_hasSync)
                throw new InvalidOperationException("Server time has not been synchronized yet.");

            long elapsedStopwatchTicks = Stopwatch.GetTimestamp() - _localStopwatchTicksAtLastSync;
            long elapsedTimeTicks = elapsedStopwatchTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency;

            return _estimatedServerUtcTicksAtLastSync + elapsedTimeTicks;
        }
    }

    public DateTime GetEstimatedServerUtcNow()
    {
        return new DateTime(GetEstimatedServerUtcTicksNow(), DateTimeKind.Utc);
    }

    public TimeSpan GetDelayUntilServerUtcTicks(long executeAtServerUtcTicks)
    {
        long estimatedServerUtcTicksNow = GetEstimatedServerUtcTicksNow();
        long delayTicks = executeAtServerUtcTicks - estimatedServerUtcTicksNow;

        return delayTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(delayTicks);
    }
}