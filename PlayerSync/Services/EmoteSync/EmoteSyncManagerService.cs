using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Emote;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MareSynchronos.Services.EmoteSync;

public sealed class EmoteSyncManagerService : IHostedService, IDisposable
{
    private static readonly TimeSpan TimeSyncInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FinalSpinThreshold = TimeSpan.FromMilliseconds(3);

    private readonly ILogger<EmoteSyncManagerService> _logger;
    private readonly ApiController _apiController;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _timeSyncCts;
    private Task? _timeSyncTask;
    private bool _timeSyncEnabled;

    private string? _currentGroupId;
    private string? _leadUserUid;
    private Dictionary<UserData, bool> _groupMembers = [];
    private readonly ConcurrentDictionary<Guid, byte> _executedEventIds = new();

    public EmoteSyncManagerService(ILogger<EmoteSyncManagerService> logger, ApiController apiController)
    {
        _logger = logger;
        _apiController = apiController;
        TimeSync = new EmoteTimeSyncService();
    }

    public EmoteTimeSyncService TimeSync { get; }

    public event Action? StateChanged;

    /// <summary>
    /// Wire this from the actual local emote execution implementation later.
    /// </summary>
    public Func<ScheduledEmoteActionDto, CancellationToken, Task>? ExecuteScheduledEmoteAsync { get; set; }

    public bool IsTimeSyncEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _timeSyncEnabled;
            }
        }
    }

    public bool HasTimeSync => TimeSync.HasSync;
    public TimeSpan LastRoundTrip => TimeSync.LastRoundTrip;

    public string? CurrentGroupId
    {
        get
        {
            lock (_stateLock)
            {
                return _currentGroupId;
            }
        }
    }

    public string? LeadUserUid
    {
        get
        {
            lock (_stateLock)
            {
                return _leadUserUid;
            }
        }
    }

    public IReadOnlyDictionary<UserData, bool> GroupMembers
    {
        get
        {
            lock (_stateLock)
            {
                return new Dictionary<UserData, bool>(_groupMembers);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("EmoteSync manager started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("EmoteSync manager stopping.");
        await SetTimeSyncEnabledAsync(false).ConfigureAwait(false);
        _logger.LogDebug("EmoteSync manager stopped.");
    }

    public void Dispose()
    {
        _timeSyncCts?.Cancel();
        _timeSyncCts?.Dispose();
    }

    /// <summary>
    /// Public toggle for the UI to enable/disable background time sync.
    /// </summary>
    public async Task SetTimeSyncEnabledAsync(bool isEnabled)
    {
        CancellationTokenSource? ctsToCancel = null;
        Task? taskToAwait = null;
        bool raiseChanged = false;

        lock (_stateLock)
        {
            if (_timeSyncEnabled == isEnabled)
                return;

            _timeSyncEnabled = isEnabled;
            raiseChanged = true;

            if (isEnabled)
            {
                _timeSyncCts = new CancellationTokenSource();
                _timeSyncTask = RunTimeSyncLoopAsync(_timeSyncCts.Token);
            }
            else
            {
                ctsToCancel = _timeSyncCts;
                taskToAwait = _timeSyncTask;
                _timeSyncCts = null;
                _timeSyncTask = null;
            }
        }

        if (isEnabled)
        {
            RaiseStateChangedIfNeeded(raiseChanged);
            return;
        }

        if (ctsToCancel != null)
        {
            try
            {
                await ctsToCancel.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                ctsToCancel.Dispose();
            }
        }

        if (taskToAwait != null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // swallow
            }
        }

        TimeSync.Reset();
        RaiseStateChangedIfNeeded(raiseChanged);
    }

    public async Task RequestTimeSyncNowAsync()
    {
        if (!_apiController.IsConnected)
        {
            _logger.LogDebug("Skipping time sync request, not connected.");
            return;
        }

        long clientSendUtcTicks = DateTime.UtcNow.Ticks;

        ServerTimeRequestDto request = new()
        {
            RequestId = Guid.NewGuid(),
            ClientSendUtcTicks = clientSendUtcTicks
        };

        try
        {
            ServerTimeResponseDto response = await _apiController.GetServerTime(request).ConfigureAwait(false);

            long clientReceiveUtcTicks = DateTime.UtcNow.Ticks;
            long clientReceiveStopwatchTicks = Stopwatch.GetTimestamp();

            TimeSync.AcceptSample(response, clientReceiveUtcTicks, clientReceiveStopwatchTicks);
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to synchronize emote server time.");
        }
    }

    /// <summary>
    /// Update current emote group state from the server.
    /// Pass the groupId explicitly so the manager does not need to guess it from the DTO.
    /// </summary>
    public void ApplyGroupUpdate(string? groupId, EmoteResponseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        lock (_stateLock)
        {
            _currentGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId;
            _leadUserUid = dto.EmoteLeadUser;
            _groupMembers = dto.EmoteGroupMembers?.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value) ?? [];
        }

        RaiseStateChanged();
    }

    public void ClearGroupState()
    {
        lock (_stateLock)
        {
            _currentGroupId = null;
            _leadUserUid = null;
            _groupMembers = [];
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Call this from your incoming SignalR handler when the server sends a scheduled emote action.
    /// </summary>
    public async Task HandleScheduledEmoteActionAsync(ScheduledEmoteActionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!_executedEventIds.TryAdd(dto.EventId, 0))
        {
            _logger.LogDebug("Ignoring duplicate scheduled emote event {eventId}.", dto.EventId);
            return;
        }

        try
        {
            if (!TimeSync.HasSync)
            {
                _logger.LogWarning("Received scheduled emote action without time sync, executing immediately.");
                await ExecuteScheduledAsync(dto, cancellationToken).ConfigureAwait(false);
                return;
            }

            TimeSpan delay = TimeSync.GetDelayUntilServerUtcTicks(dto.ExecuteAtServerUtcTicks);

            if (delay > FinalSpinThreshold)
            {
                await Task.Delay(delay - FinalSpinThreshold, cancellationToken).ConfigureAwait(false);
            }

            while (TimeSync.GetEstimatedServerUtcTicksNow() < dto.ExecuteAtServerUtcTicks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.SpinWait(50);
            }

            await ExecuteScheduledAsync(dto, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _executedEventIds.TryRemove(dto.EventId, out _);
        }
    }

    private async Task RunTimeSyncLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Emote time sync loop started.");

        try
        {
            await RequestTimeSyncNowAsync().ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSyncInterval, cancellationToken).ConfigureAwait(false);
                await RequestTimeSyncNowAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Emote time sync loop canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unhandled exception in emote time sync loop.");
        }
        finally
        {
            _logger.LogDebug("Emote time sync loop stopped.");
        }
    }

    private async Task ExecuteScheduledAsync(ScheduledEmoteActionDto dto, CancellationToken cancellationToken)
    {
        if (ExecuteScheduledEmoteAsync == null)
        {
            _logger.LogDebug("No local emote execution callback is attached for event {eventId}.", dto.EventId);
            return;
        }

        await ExecuteScheduledEmoteAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseStateChangedIfNeeded(bool shouldRaise)
    {
        if (shouldRaise)
        {
            RaiseStateChanged();
        }
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke();
    }
}