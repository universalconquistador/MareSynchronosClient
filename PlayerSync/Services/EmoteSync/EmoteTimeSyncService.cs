using MareSynchronos.API.Dto.Emote;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MareSynchronos.Services.EmoteSync;

public sealed class EmoteTimeSyncService : IDisposable
{
    private const int MaxTimeSyncSamples = 12;
    private const int TimeSyncSamplesToAverage = 4;

    private const int MaxGameLatencySamples = 16;
    private static readonly TimeSpan GamePingInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GamePingTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LocalSendSafetyMargin = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan MaxJitterContribution = TimeSpan.FromMilliseconds(20);

    private readonly object _timeLock = new();
    private readonly object _gameLatencyLock = new();
    private readonly Queue<TimeSyncSample> _timeSyncSamples = new();
    private readonly Queue<long> _gameServerRttSamples = new();
    private readonly Ping _ping = new();

    private long _estimatedServerUtcTicksAtLastSync;
    private long _localStopwatchTicksAtLastSync;
    private bool _hasSync;

    private string? _gameServerHost;
    private CancellationTokenSource? _gamePingCts;
    private Task? _gamePingTask;

    private long _averageGameServerRttTicks;
    private long _gameServerJitterTicks;

    private sealed record TimeSyncSample(long OffsetTicks, long RoundTripTicks);

    public TimeSpan EstimatedCompensatedGameServerOneWay
    {
        get
        {
            lock (_gameLatencyLock)
            {
                return TimeSpan.FromTicks(GetCompensatedGameServerOneWayTicks());
            }
        }
    }

    public void Reset()
    {
        lock (_timeLock)
        {
            _timeSyncSamples.Clear();
            _estimatedServerUtcTicksAtLastSync = 0;
            _localStopwatchTicksAtLastSync = 0;
            _hasSync = false;
        }

        lock (_gameLatencyLock)
        {
            _gameServerRttSamples.Clear();
            _averageGameServerRttTicks = 0;
            _gameServerJitterTicks = 0;
        }
    }

    public string? GetLobbyHostForDataCenter(string? dataCenterName)
    {
        if (string.IsNullOrWhiteSpace(dataCenterName))
            return null;

        return dataCenterName switch
        {
            "Elemental" => "neolobby01.ffxiv.com",
            "Gaia" => "neolobby03.ffxiv.com",
            "Mana" => "neolobby05.ffxiv.com",
            "Aether" => "neolobby02.ffxiv.com",
            "Primal" => "neolobby04.ffxiv.com",
            "Crystal" => "neolobby08.ffxiv.com",
            "Chaos" => "neolobby06.ffxiv.com",
            "Light" => "neolobby07.ffxiv.com",
            "Materia" => "neolobby09.ffxiv.com",
            _ => null
        };
    }

    public async Task SetGameServerHostAsync(string? host)
    {
        CancellationTokenSource? oldCts = null;
        Task? oldTask = null;

        lock (_gameLatencyLock)
        {
            if (string.Equals(_gameServerHost, host, StringComparison.OrdinalIgnoreCase))
                return;

            oldCts = _gamePingCts;
            oldTask = _gamePingTask;

            _gamePingCts = null;
            _gamePingTask = null;
            _gameServerHost = host;

            _gameServerRttSamples.Clear();
            _averageGameServerRttTicks = 0;
            _gameServerJitterTicks = 0;
        }

        if (oldCts != null)
        {
            try
            {
                await oldCts.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                oldCts.Dispose();
            }
        }

        if (oldTask != null)
        {
            try
            {
                await oldTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // swallow
            }
        }

        if (string.IsNullOrWhiteSpace(host))
            return;

        var newCts = new CancellationTokenSource();
        Task newTask = RunGamePingLoopAsync(host, newCts.Token);

        lock (_gameLatencyLock)
        {
            _gamePingCts = newCts;
            _gamePingTask = newTask;
        }
    }

    public Task StopGameServerPingAsync()
    {
        return SetGameServerHostAsync(null);
    }

    public void AcceptSample(ServerTimeResponseDto response, long clientReceiveUtcTicks, long clientReceiveStopwatchTicks)
    {
        long roundTripTicks = (clientReceiveUtcTicks - response.ClientSendUtcTicks)
            - (response.ServerSendUtcTicks - response.ServerReceiveUtcTicks);

        if (roundTripTicks < 0)
        {
            roundTripTicks = 0;
        }

        long offsetTicks =
            ((response.ServerReceiveUtcTicks - response.ClientSendUtcTicks)
            + (response.ServerSendUtcTicks - clientReceiveUtcTicks)) / 2;

        lock (_timeLock)
        {
            _timeSyncSamples.Enqueue(new TimeSyncSample(offsetTicks, roundTripTicks));

            while (_timeSyncSamples.Count > MaxTimeSyncSamples)
            {
                _timeSyncSamples.Dequeue();
            }

            TimeSyncSample[] selectedSamples = _timeSyncSamples
                .OrderBy(sample => sample.RoundTripTicks)
                .Take(Math.Min(TimeSyncSamplesToAverage, _timeSyncSamples.Count))
                .ToArray();

            if (selectedSamples.Length == 0)
            {
                return;
            }

            long averageOffsetTicks = (long)selectedSamples.Average(sample => sample.OffsetTicks);

            _estimatedServerUtcTicksAtLastSync = clientReceiveUtcTicks + averageOffsetTicks;
            _localStopwatchTicksAtLastSync = clientReceiveStopwatchTicks;
            _hasSync = true;
        }
    }

    public bool TryGetEstimatedServerUtcTicksNow(out long estimatedServerUtcTicksNow)
    {
        lock (_timeLock)
        {
            if (!_hasSync)
            {
                estimatedServerUtcTicksNow = 0;
                return false;
            }

            long elapsedStopwatchTicks = Stopwatch.GetTimestamp() - _localStopwatchTicksAtLastSync;
            long elapsedTimeTicks = elapsedStopwatchTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency;

            estimatedServerUtcTicksNow = _estimatedServerUtcTicksAtLastSync + elapsedTimeTicks;
            return true;
        }
    }

    public long GetEstimatedServerUtcTicksNow()
    {
        if (!TryGetEstimatedServerUtcTicksNow(out long estimatedServerUtcTicksNow))
            throw new InvalidOperationException("Server time has not been synchronized yet.");

        return estimatedServerUtcTicksNow;
    }

    public DateTime GetEstimatedServerUtcNow()
    {
        return new DateTime(GetEstimatedServerUtcTicksNow(), DateTimeKind.Utc);
    }

    public bool TryGetDelayUntilServerUtcTicks(long executeAtServerUtcTicks, out TimeSpan delay, bool compensateForGameServerLatency = true)
    {
        if (!TryGetEstimatedServerUtcTicksNow(out long estimatedServerUtcTicksNow))
        {
            delay = TimeSpan.Zero;
            return false;
        }

        long compensatedExecuteAtTicks = executeAtServerUtcTicks;

        if (compensateForGameServerLatency)
        {
            lock (_gameLatencyLock)
            {
                if (_gameServerRttSamples.Count > 0)
                {
                    compensatedExecuteAtTicks -= GetCompensatedGameServerOneWayTicks();
                }
            }
        }

        long delayTicks = compensatedExecuteAtTicks - estimatedServerUtcTicksNow;
        delay = delayTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(delayTicks);
        return true;
    }

    public TimeSpan GetDelayUntilServerUtcTicks(long executeAtServerUtcTicks, bool compensateForGameServerLatency = true)
    {
        if (!TryGetDelayUntilServerUtcTicks(executeAtServerUtcTicks, out TimeSpan delay, compensateForGameServerLatency))
            throw new InvalidOperationException("Server time has not been synchronized yet.");

        return delay;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_gameLatencyLock)
        {
            cts = _gamePingCts;
            task = _gamePingTask;
            _gamePingCts = null;
            _gamePingTask = null;
        }

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        if (task != null)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // swallow
            }
        }

        _ping.Dispose();
    }

    private async Task RunGamePingLoopAsync(string host, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Stopwatch intervalStopwatch = Stopwatch.StartNew();

            try
            {
                PingReply reply = await _ping
                    .SendPingAsync(host, (int)GamePingTimeout.TotalMilliseconds)
                    .ConfigureAwait(false);

                if (reply.Status == IPStatus.Success)
                {
                    AcceptGameServerPingSample(TimeSpan.FromMilliseconds(reply.RoundtripTime).Ticks);
                }
            }
            catch (PingException)
            {
                // we don't really care, it's going to either work or not
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // we don't really care, it's going to either work or not
            }

            TimeSpan remainingDelay = GamePingInterval - intervalStopwatch.Elapsed;
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void AcceptGameServerPingSample(long roundTripTicks)
    {
        lock (_gameLatencyLock)
        {
            _gameServerRttSamples.Enqueue(roundTripTicks);

            while (_gameServerRttSamples.Count > MaxGameLatencySamples)
            {
                _gameServerRttSamples.Dequeue();
            }

            long sum = 0;
            foreach (long sample in _gameServerRttSamples)
            {
                sum += sample;
            }

            long averageTicks = _gameServerRttSamples.Count == 0 ? 0 : sum / _gameServerRttSamples.Count;

            long totalDeviation = 0;
            foreach (long sample in _gameServerRttSamples)
            {
                totalDeviation += Math.Abs(sample - averageTicks);
            }

            long meanAbsoluteDeviationTicks = _gameServerRttSamples.Count == 0
                ? 0
                : totalDeviation / _gameServerRttSamples.Count;

            _averageGameServerRttTicks = averageTicks;
            _gameServerJitterTicks = meanAbsoluteDeviationTicks;
        }
    }

    private long GetCompensatedGameServerOneWayTicks()
    {
        long oneWayTicks = _averageGameServerRttTicks / 2;
        long jitterContributionTicks = Math.Min(_gameServerJitterTicks / 2, MaxJitterContribution.Ticks);

        return oneWayTicks + jitterContributionTicks - LocalSendSafetyMargin.Ticks;
    }
}