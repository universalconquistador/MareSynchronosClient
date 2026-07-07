using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayerSync.Interop.CrashHacks;

/// <summary>
/// Sometimes we see a crash when PartialAnimationPackResourceHandle.vf25 is destroying its hkLoader,
/// which is in turn destroying its hkPackfileData. It appears as though one of the hkPackfileData's
/// chunk pointers is no longer a valid pointer but instead is stomped with a sequence of increasing
/// signed 16-bit integers. Maybe indices or something? idk.
/// </summary>
/// <remarks>
/// This hack attempts to detect calls to hkLargeBlockAllocator.Free with invalid addresses using the
/// <c>IsBadReadPtr</c> Win32 function (which is generally discouraged but a good fit for this).
/// </remarks>
public sealed partial class AnimationFreeGuard : IHostedService, IDisposable
{
    [LibraryImport("Kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsBadReadPtr(nint address, nint length);

    private delegate void HkLargeBlockAllocatorFreeDelegate(nint largeBlockAllocator, nint memory);

    private readonly ILogger _logger;

    [Signature("48 85 D2 0F 84 ?? ?? ?? ?? 55 48 83 EC 20", DetourName = nameof(HkLargeBlockAllocatorFreeDetour))]
    private readonly Hook<HkLargeBlockAllocatorFreeDelegate>? _hkLargeBlockAllocatorFreeHook;

    public AnimationFreeGuard(ILogger<AnimationFreeGuard> logger, IGameInteropProvider gameInteropProvider)
    {
        _logger = logger;

        try
        {
            gameInteropProvider.InitializeFromAttributes(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hook hkLargeBlockAllocator Free.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hkLargeBlockAllocatorFreeHook?.Enable();
        return Task.CompletedTask;
    }

    private void HkLargeBlockAllocatorFreeDetour(nint largeBlockAllocator, nint memory)
    {
        bool isMemoryValid = !IsBadReadPtr(memory, 8);
        if (isMemoryValid)
        {
            _hkLargeBlockAllocatorFreeHook?.Original.Invoke(largeBlockAllocator, memory);
        }
        else
        {
            _logger.LogInformation("Caught invalid free of pointer 0x{addr:X}.", memory);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _hkLargeBlockAllocatorFreeHook?.Disable();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _hkLargeBlockAllocatorFreeHook?.Dispose();
    }
}
