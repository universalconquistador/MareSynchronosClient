using System.Collections.Concurrent;

namespace MareSynchronos.PlayerData.Pairs;

internal sealed class PairApplyQueue : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<CancellationToken, Task>>> _perUidQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _uidState = new(StringComparer.Ordinal); // 0 waiting, 1 scheduled, 2 running

    private readonly ConcurrentQueue<string> _uidQueue = new();
    private readonly SemaphoreSlim _workerGate = new(0);

    private readonly CancellationTokenSource _cts = new();
    private readonly object _workersLock = new();
    private readonly List<Task> _workers = new();
    private int _nextWorkerId = 0;
    private int _maxConcurrency;

    private int _processingCount = 0;
    private int _pendingCount = 0;

    public PairApplyQueue(int maxConcurrency)
    {
        SetMaxConcurrency(maxConcurrency);

    }

    public int MaxConcurrency => Volatile.Read(ref _maxConcurrency); // dictates how many in flight data applications we can have
    public int InFlightCount => Volatile.Read(ref _processingCount); // the number of data applications being worked on
    public int PendingCount => Volatile.Read(ref _pendingCount); // the number of pending data applications

    public void SetMaxConcurrency(int newMaxConcurrency)
    {
        if (newMaxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(newMaxConcurrency));

        lock (_workersLock)
        {
            Volatile.Write(ref _maxConcurrency, newMaxConcurrency);

            _workers.RemoveAll(t => t.IsCompleted);

            // add additional workers if upscaling
            while (_workers.Count < newMaxConcurrency)
            {
                int workerId = _nextWorkerId++;
                _workers.Add(Task.Run(() => WorkerLoopAsync(workerId)));
            }

            // wake and exit workers we don't need if downscaling
            int extra = _workers.Count - newMaxConcurrency;
            if (extra > 0)
                _workerGate.Release(extra);
        }
    }

    /// <summary>
    /// Enqueue a function to be called by a worker thread. This queues by UID to ensure sequential ordering
    /// </summary>
    /// <param name="uid">The UID to track and enqueue</param>
    /// <param name="work">The method to call against the target UID</param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public void Enqueue(string uid, Func<CancellationToken, Task> work)
    {
        if (string.IsNullOrWhiteSpace(uid))
            throw new ArgumentException("UID is required", nameof(uid));

        if (work is null)
            throw new ArgumentNullException(nameof(work));

        var queue = _perUidQueues.GetOrAdd(uid, static _ => new ConcurrentQueue<Func<CancellationToken, Task>>());
        queue.Enqueue(work);
        Interlocked.Increment(ref _pendingCount);

        var state = _uidState.GetOrAdd(uid, 0);
        if (state == 0 && _uidState.TryUpdate(uid, 1, 0))
        {
            ScheduleUid(uid);
        }
    }

    private void ScheduleUid(string uid)
    {
        _uidQueue.Enqueue(uid);
        _workerGate.Release();
    }

    private async Task WorkerLoopAsync(int workerId)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // exit out if worker isn't needed (downscaling)
                if (workerId >= Volatile.Read(ref _maxConcurrency))
                    return;

                await _workerGate.WaitAsync(_cts.Token).ConfigureAwait(false);

                if (!_uidQueue.TryDequeue(out var uid))
                    continue;

                // only run if this UID is scheduled, 1 -> 2
                if (!_uidState.TryUpdate(uid, 2, 1))
                    continue;

                try
                {
                    if (_perUidQueues.TryGetValue(uid, out var queue) && queue.TryDequeue(out var work))
                    {
                        Interlocked.Decrement(ref _pendingCount);
                        Interlocked.Increment(ref _processingCount);
                        try
                        {
                            await work(_cts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _processingCount);
                        }
                    }
                }
                catch
                {
                    //
                }
                finally
                {
                    // we re check twice to avoid missing a race where an item is enqueued while state = 2
                    if (_perUidQueues.TryGetValue(uid, out var queueAfter) && !queueAfter.IsEmpty)
                    {
                        _uidState[uid] = 1;
                        ScheduleUid(uid);
                    }
                    else
                    {
                        _uidState[uid] = 0;

                        if (_perUidQueues.TryGetValue(uid, out var queueRecheck) && !queueRecheck.IsEmpty)
                        {
                            if (_uidState.TryUpdate(uid, 1, 0))
                                ScheduleUid(uid);
                        }
                        else
                        {
                            // cleanup empty queues
                            if (queueAfter is not null && queueAfter.IsEmpty)
                                _perUidQueues.TryRemove(uid, out _);
                        }
                    }
                }

                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) 
        {
            //
        }
    }

    public void Dispose()
    {
        _cts.Cancel(); // this should exit any waiting workers

        _workerGate.Dispose();
        _cts.Dispose();
    }
}