using System.Diagnostics;

namespace MareSynchronos.Interop.Ipc;

public interface IIpcCaller : IDisposable
{
    bool APIAvailable { get; }
    void CheckAPI();

    static void GetIPCTimeToCompletion(Action act, out double elapsedTimeMilliseconds)
    {
        var startTime = Stopwatch.GetTimestamp();
        try
        {
            act();
        }
        finally
        {
            elapsedTimeMilliseconds = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        }
    }

    static T GetIPCTimeToCompletion<T>(Func<T> func, out double elapsedTimeMilliseconds)
    {
        var startTime = Stopwatch.GetTimestamp();
        try
        {
            return func();
        }
        finally
        {
            elapsedTimeMilliseconds = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        }
    }
}
