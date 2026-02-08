using Dalamud.Bindings.ImGui;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class PairingRequestsNoticeUi : WindowMediatorSubscriberBase
{
    private const float PillWidth = 300f;
    private const float PillHeight = 50f;

    private bool _isDraggingPill;
    private readonly UiSharedService _uiShared;
    private readonly PairRequestManager _pairRequestManager;

    public PairingRequestsNoticeUi(ILogger<PairingRequestsNoticeUi> logger, MareMediator mediator, UiSharedService uiShared, PerformanceCollectorService performanceCollectorService, 
        PairRequestManager pairRequestManager) : base(logger, mediator, "PlayerSync Pending Pair Notice", performanceCollectorService)
    {
        _uiShared = uiShared;
        _pairRequestManager = pairRequestManager;

        SizeConstraints = new WindowSizeConstraints()
        {
            MaximumSize = new Vector2(PillWidth, PillHeight),
            MinimumSize = new Vector2(PillWidth, PillHeight),
        };

        Flags |= ImGuiWindowFlags.NoBackground;
        Flags |= ImGuiWindowFlags.NoNavFocus;
        Flags |= ImGuiWindowFlags.NoResize;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoTitleBar;
        Flags |= ImGuiWindowFlags.NoDecoration;
        Flags |= ImGuiWindowFlags.NoFocusOnAppearing;

        DisableWindowSounds = true;
        RespectCloseHotkey = false;
        ForceMainWindow = true;
        IsOpen = true;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = true);
    }

    protected override void DrawInternal()
    {
        var pillSize = new Vector2(PillWidth, PillHeight);
        var windowRounding = PillHeight * 0.5f;
        var contentStart = ImGui.GetCursorScreenPos();

        // background
        var drawList = ImGui.GetWindowDrawList();
        var pillMin = contentStart;
        var pillMax = contentStart + pillSize;
        var pillColor = ImGui.GetColorU32(new Vector4(0.04f, 0.18f, 0.24f, 1.00f));

        drawList.AddRectFilled(pillMin, pillMax, pillColor, windowRounding);

        // click/drag
        ImGui.SetCursorPos(Vector2.Zero);
        var pillWasClicked = ImGui.InvisibleButton("##PairRequestsPill", pillSize);
        var isHovered = ImGui.IsItemHovered();
        if (isHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemActivated())
        {
            _isDraggingPill = false;
        }

        // check for movement so we know this is not a click action
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDraggingPill = true;

            var mouseDelta = ImGui.GetIO().MouseDelta;
            var currentWindowPos = ImGui.GetWindowPos();
            ImGui.SetWindowPos(currentWindowPos + mouseDelta);
        }

        // not being dragged so this must be a click action
        if (pillWasClicked && !_isDraggingPill)
        {
            Mediator.Publish(new UiToggleMessage(typeof(PairingRequestsUi)));
        }

        if (ImGui.IsItemDeactivated())
        {
            _isDraggingPill = false;
        }

        var labelText = "PlayerSync Pair Requests";
        var countValue = _pairRequestManager.ReceivedPendingCount;
        var countText = countValue.ToString();
        var labelSize = ImGui.CalcTextSize(labelText);
        var countSize = ImGui.CalcTextSize(countText);
        var centerY = pillMin.Y + (pillSize.Y * 0.5f);

        // text label
        ImGui.SetCursorScreenPos(new Vector2(pillMin.X + 16f, centerY - (labelSize.Y * 0.5f)));
        ImGui.TextUnformatted(labelText);

        // pending pair count
        ImGui.SetCursorScreenPos(new Vector2(pillMax.X - 16f - countSize.X, centerY - (countSize.Y * 0.5f)));
        ImGui.TextUnformatted(countText);
    }

    public override bool DrawConditions()
    {
#if DEBUG
        return true;
#endif
        if (_pairRequestManager.ReceivedPendingCount == 0) return false;
        if (!IsOpen) return false;
        return true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        var mainViewport = ImGui.GetMainViewport();
        var workPos = mainViewport.WorkPos;
        var workSize = mainViewport.WorkSize;
        var pillSize = new Vector2(PillWidth, PillHeight);
        var pillMargin = 32f;
        var initialPosition = new Vector2(workPos.X + workSize.X - pillSize.X - pillMargin, workPos.Y + (workSize.Y * 2/3) - pillSize.Y - pillMargin);

        // prevent spawning off the window
        var clampedPos = new Vector2(
            MathF.Max(workPos.X + pillMargin, MathF.Min(initialPosition.X, workPos.X + workSize.X - pillSize.X - pillMargin)),
            MathF.Max(workPos.Y + pillMargin, MathF.Min(initialPosition.Y, workPos.Y + (workSize.Y * 2 / 3) - pillSize.Y - pillMargin)));

        ImGui.SetNextWindowPos(clampedPos, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(pillSize, ImGuiCond.Always);
    }
}