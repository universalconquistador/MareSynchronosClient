using Dalamud.Bindings.ImGui;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class PairingRequestsNoticeUi : WindowMediatorSubscriberBase
{
    private bool _isDraggingPill;
    private readonly PairRequestManager _pairRequestManager;

    public PairingRequestsNoticeUi(ILogger<PairingRequestsNoticeUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
        PairRequestManager pairRequestManager) : base(logger, mediator, "PlayerSync Pending Pair Notice", performanceCollectorService)
    {
        _pairRequestManager = pairRequestManager;

        SizeConstraints = new WindowSizeConstraints()
        {
            MaximumSize = new Vector2(10000f, 10000f),
            MinimumSize = new Vector2(1f, 1f),
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
        var winPos = ImGui.GetWindowPos();
        var contentMin = winPos + ImGui.GetWindowContentRegionMin();
        var contentMax = winPos + ImGui.GetWindowContentRegionMax();

        var pillMin = contentMin;
        var pillMax = contentMax;
        var pillSize = pillMax - pillMin;

        var drawList = ImGui.GetWindowDrawList();
        var windowRounding = pillSize.Y * 0.5f;
        var pillColor = ImGui.GetColorU32(new Vector4(0.04f, 0.18f, 0.24f, 1.00f)); // should pull this from theme
        drawList.AddRectFilled(pillMin, pillMax, pillColor, windowRounding, ImDrawFlags.RoundCornersAll);

        ImGui.SetCursorPos(Vector2.Zero);
        var pillWasClicked = ImGui.InvisibleButton("##PairRequestsPill", pillSize);

        var isHovered = ImGui.IsItemHovered();
        if (isHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemActivated())
            _isDraggingPill = false;

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDraggingPill = true;

            var mouseDelta = ImGui.GetIO().MouseDelta;
            var currentWindowPos = ImGui.GetWindowPos();
            ImGui.SetWindowPos(currentWindowPos + mouseDelta);
        }

        if (pillWasClicked && !_isDraggingPill)
            Mediator.Publish(new UiToggleMessage(typeof(PairingRequestsUi)));

        if (ImGui.IsItemDeactivated())
            _isDraggingPill = false;

        // text
        var labelText = "PlayerSync Pair Requests";
        var countValue = _pairRequestManager.ReceivedPendingCount;
        var countText = countValue.ToString();

        var labelSize = ImGui.CalcTextSize(labelText);
        var countSize = ImGui.CalcTextSize(countText);

        var padX = UiScale.ScaledFloat(16f);
        var centerY = pillMin.Y + (pillSize.Y * 0.5f);

        // label
        ImGui.SetCursorScreenPos(new Vector2(pillMin.X + padX, centerY - (labelSize.Y * 0.5f)));
        ImGui.TextUnformatted(labelText);

        // count
        ImGui.SetCursorScreenPos(new Vector2(pillMax.X - padX - countSize.X, centerY - (countSize.Y * 0.5f)));
        ImGui.TextUnformatted(countText);
    }

    public override bool DrawConditions()
    {
        if (_pairRequestManager.ReceivedPendingCount == 0) return false;
        if (!IsOpen) return false;
        return true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        var labelText = "PlayerSync Pair Requests";
        var countText = _pairRequestManager.ReceivedPendingCount.ToString();

        var labelSize = ImGui.CalcTextSize(labelText);
        var countSize = ImGui.CalcTextSize(countText);
        var padX = UiScale.ScaledFloat(16f);
        var padY = UiScale.ScaledFloat(16f);
        var gap = UiScale.ScaledFloat(8f);

        var requiredWidth = (padX * 2f) + labelSize.X + gap + countSize.X;
        var requiredHeight = MathF.Max(labelSize.Y, countSize.Y) + (padY * 2f);
        var height = MathF.Max(requiredHeight, requiredWidth / 5f);
        var width = height * 4.5f;

        var pillSize = new Vector2(MathF.Ceiling(width), MathF.Ceiling(height));

        var mainViewport = ImGui.GetMainViewport();
        var workPos = mainViewport.WorkPos;
        var workSize = mainViewport.WorkSize;

        var pillMargin = UiScale.ScaledFloat(32f);
        var initialPosition = new Vector2(
            workPos.X + workSize.X - pillSize.X - pillMargin,
            workPos.Y + (workSize.Y * 2f / 3f) - pillSize.Y - pillMargin);

        var clampedPos = new Vector2(
            MathF.Max(workPos.X + pillMargin, MathF.Min(initialPosition.X, workPos.X + workSize.X - pillSize.X - pillMargin)),
            MathF.Max(workPos.Y + pillMargin, MathF.Min(initialPosition.Y, workPos.Y + (workSize.Y * 2f / 3f) - pillSize.Y - pillMargin)));

        ImGui.SetNextWindowPos(clampedPos, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(pillSize, ImGuiCond.Always);
    }
}