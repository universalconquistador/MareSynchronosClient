using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Interop.Ipc;

public class RedrawManager
{
    private readonly MareMediator _mareMediator;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ConcurrentDictionary<nint, bool> _penumbraRedrawRequests = [];
    private CancellationTokenSource _disposalCts = new();

    public SemaphoreSlim RedrawSemaphore { get; init; } = new(2, 2);

    public RedrawManager(MareMediator mareMediator, DalamudUtilService dalamudUtil)
    {
        _mareMediator = mareMediator;
        _dalamudUtil = dalamudUtil;
    }

    // science
    private sealed class CoalesceState
    {
        public CancellationTokenSource? DelayCts;
        public Task? RunningTask;
        public DateTime LastRequestedUtc;
    }

    // science
    private readonly ConcurrentDictionary<string, CoalesceState> _redrawCoalesce = new(StringComparer.Ordinal);

    // science
    private static readonly TimeSpan RedrawCoalesceWindow = TimeSpan.FromMilliseconds(75);

    // science
    private static string GetActorKey(GameObjectHandler handler) => handler.Address.ToString();

    public async Task CoalescedRedrawAsync(
        ILogger logger,
        GameObjectHandler handler,
        Guid applicationId,
        Action<ICharacter> action,
        CancellationToken token)
    {
        var key = GetActorKey(handler);
        var state = _redrawCoalesce.GetOrAdd(key, _ => new CoalesceState());

        CoalesceState snapshot;
        lock (state)
        {
            state.DelayCts?.Cancel();
            state.DelayCts?.Dispose();
            state.DelayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            snapshot = state;
            state.LastRequestedUtc = DateTime.UtcNow;
        }

        try
        {
            // wait the debounce window (reset each new request)
            await Task.Delay(RedrawCoalesceWindow, snapshot.DelayCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // only one actual redraw per window
        Task? toAwait;
        lock (snapshot)
        {
            toAwait = snapshot.RunningTask;
            if (toAwait == null || toAwait.IsCompleted)
            {
                // start the actual redraw task
                snapshot.RunningTask = PenumbraRedrawInternalAsync(logger, handler, applicationId, action, token);
                toAwait = snapshot.RunningTask;
            }
            else
            {
                // Running
            }
        }

        try
        {
            await toAwait!.ConfigureAwait(false);
        }
        finally
        {
            lock (snapshot)
            {
                if ((DateTime.UtcNow - snapshot.LastRequestedUtc) > RedrawCoalesceWindow
                    && (snapshot.RunningTask == null || snapshot.RunningTask.IsCompleted))
                {
                    _redrawCoalesce.TryRemove(key, out _);
                }
            }
        }
    }

    public async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<ICharacter> action, CancellationToken token)
    {
        _mareMediator.Publish(new PenumbraStartRedrawMessage(handler.Address));

        _penumbraRedrawRequests[handler.Address] = true;

        try
        {
            using CancellationTokenSource cancelToken = new CancellationTokenSource();
            using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken.Token, token, _disposalCts.Token);
            var combinedToken = combinedCts.Token;
            cancelToken.CancelAfter(TimeSpan.FromSeconds(15));
            await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, combinedToken).ConfigureAwait(false);

            if (!_disposalCts.Token.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, combinedToken).ConfigureAwait(false);
        }
        finally
        {
            _penumbraRedrawRequests[handler.Address] = false;
            _mareMediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
        }
    }

    internal void Cancel()
    {
        _disposalCts = _disposalCts.CancelRecreate();
    }
}
