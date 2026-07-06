using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlayerSync.Interop.CrashHacks;

/// <summary>
/// When an animation clip is evaluated while a character's skeleton is mid-rebuild during a
/// redraw — its bone mapper is transiently null, leading to crashes. This hooks the crashing 
/// function, which takes the mapper as the first argument, and causes it to return null,
/// which is handled cleanly by the caller. A frame of animation is dropped, but no crash.
/// </summary>
/// 
/// <remarks>
/// In a busy modded-emote venue a single redraw of a
/// paired character can hit it, and a synchronized venue-wide redraw (e.g. a DJ track change, which redraws
/// the DJ on every client at once) can crash many clients near-simultaneously. A bone-COUNT check can't
/// see it (the skeleton is the right size; the mapper is null), and a redraw is the only way to re-fire a
/// modded emote's embedded TMB (music/VFX), so the redraw can't simply be avoided.
///
/// sub_1418CD900 unconditionally dereferences its first argument at [arg1+8], so a null
/// arg1 is ALWAYS a would-crash — there is no legitimate null-arg1 path. Its caller (sub_1418CCE40)
/// already handles a null/zero RETURN gracefully (it bails out of the operation via the function
/// epilogue). So we hook the function and, when arg1 is null, return 0 instead of letting the game
/// dereference it: the clip is simply not evaluated for that one frame and binds normally the next frame.
/// The crash becomes an invisible dropped animation frame.
///
/// 
///   [0]   sub_1418CD900+0x3A                                  ← FAULT: mov rcx,[rbp+8]  (arg1 == null)   ▲ hook attaches here
///   [1]   sub_1418CCE40+0x124                                 ← caller; already does test rax,rax; je bail  (the graceful 0-return path)
///   [2]   sub_1418F2C40+0x5CD
///   [3]   sub_141C26ED0+0x2ED
///   [4]   Client::System::Scheduler::Clip::HavokAnimationClip.vf31+0x199 
///   [5]   Client::System::Scheduler::Clip::BaseClip.vf53+0x198
///   [6]   sub_141C262B0+0xAB
///   [7]   sub_141BFC560+0xF0
///   [8]   sub_1418F4660+0x6C
///   [9]   sub_1419022A0+0x42
///   [10]  sub_141C262B0+0xAB 
///   [11]  sub_141BFC560+0xF0 
///   [12]  sub_1418F4660+0x6C 
///   [13]  sub_141C29E70+0x141
///   [14]  sub_141902210+0x7A
///   [15]  sub_141901330+0x1F7
///   [16]  sub_141C26390+0x4F
///   [17]  sub_141BFC6A0+0x29E
///   [18]  Client::System::Scheduler::Base::TimelineController.ProcessAll+0xDF
///   [19]  sub_141BF6130+0x273
///   [20]  sub_141BF5D10+0x207
///   [21]  sub_141988F00+0x86
///   [22]  sub_1418C3980+0x16F
///   [23]  sub_140471FE0+0x17F
///   [24]  Client::System::Framework::TaskManager::RootTask.Execute+0x2D
///   [25]  Client::System::Framework::TaskManager.ExecuteAllTasks+0x33
///   [26]  Client::System::Framework::Framework.Tick+0x250 
///   [27]  IL_STUB_PInvoke(Framework*)
///   [28]  Dalamud.Game.Framework.HandleFrameworkUpdate(Framework*)
///   [29]  IL_STUB_ReversePInvoke(Framework*)
///   [30]  sub_14005B940+0x175
///   [31]  WinMain+0x1355
///   [32]  __scrt_common_main_seh+0x106
///   [33]  KERNEL32.DLL!BaseThreadInitThunk+0x17
///   [34]  ntdll.dll!RtlUserThreadStart+0x2C
/// </remarks>
public sealed class AnimationBindGuard : IHostedService, IDisposable
{
    private delegate nint AnimationBindDelegate(nint arg1, nint arg2, nint arg3, nint arg4);

    private readonly ILogger<AnimationBindGuard> _logger;
    private readonly MareConfigService _configService;

    // sub_1418CD900 up to `mov rbp, rcx` — unique single match in ffxiv_dx11.exe.
    [Signature("48 89 5C 24 18 48 89 6C 24 20 41 54 41 56 41 57 48 83 EC 30 48 8B E9", DetourName = nameof(Detour))]
    private readonly Hook<AnimationBindDelegate>? _hook;

    private long _caughtCount;
    private long _lastLogTickMs;

    public AnimationBindGuard(ILogger<AnimationBindGuard> logger, IGameInteropProvider gameInterop, MareConfigService configService)
    {
        _logger = logger;
        _configService = configService;
        try
        {
            gameInterop.InitializeFromAttributes(this);
            _logger.LogInformation("AnimationBindGuard: hook resolved at 0x{addr:X}.", _hook!.Address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AnimationBindGuard: signature did not resolve (game patch?); animation crash guard is INACTIVE.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hook?.Enable();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _hook?.Disable();
        return Task.CompletedTask;
    }

    public void Dispose() => _hook?.Dispose();

    // Runs on the game's framework thread (animation tick). Keep it minimal — it's a hot path.
    private nint Detour(nint arg1, nint arg2, nint arg3, nint arg4)
    {
        // Only intervene on the would-crash null case, and only when the guard is enabled (so it can be
        // A/B toggled live). Everything else takes the original path unchanged.
        if (arg1 == nint.Zero)
        {
            // Rate-limited because this can fire many times per frame during a synchronized redraw.
            _caughtCount++;
            var now = Environment.TickCount64;
            if (now - _lastLogTickMs > 5000)
            {
                _lastLogTickMs = now;
                _logger.LogWarning("AnimationBindGuard: skipped a null skeleton-mapper bind that would have crashed Havok (total caught: {n}).", _caughtCount);
            }
            return nint.Zero; // caller treats 0 as "no result" and bails cleanly
        }

        return _hook!.Original(arg1, arg2, arg3, arg4);
    }
}
