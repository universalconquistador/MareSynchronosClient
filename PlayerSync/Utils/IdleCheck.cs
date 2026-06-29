using System.Runtime.InteropServices;

namespace MareSynchronos.Utils;

// Adapted from https://stackoverflow.com/questions/1037595/c-sharp-detect-time-of-last-user-interaction-with-the-os

public static class IdleCheck
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint cwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    public static bool TryGetIdleTime(out TimeSpan idleTime)
    {
        idleTime = TimeSpan.Zero;

        if (!OperatingSystem.IsWindows())
            return false;

        LastInputInfo lastInputInfo = new()
        {
            cbSize = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref lastInputInfo))
            return false;

        uint idleMilliseconds = unchecked((uint)Environment.TickCount - lastInputInfo.cwTime);
        idleTime = TimeSpan.FromMilliseconds(idleMilliseconds);

        return true;
    }

    public static bool IsIdleFor(TimeSpan threshold)
    {
        return TryGetIdleTime(out TimeSpan idleTime) && idleTime >= threshold;
    }
}