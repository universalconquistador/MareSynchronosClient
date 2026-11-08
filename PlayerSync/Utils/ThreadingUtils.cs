using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Utils;

public static class ThreadingUtils
{
    public static Task RunOnLowestPriorityThread(Action action, CancellationToken ct)
    {
        return Task.Factory.StartNew(() =>
        {
            var previousThreadPriority = Thread.CurrentThread.Priority;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                action();
            }
            finally
            {
                Thread.CurrentThread.Priority = previousThreadPriority;
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
