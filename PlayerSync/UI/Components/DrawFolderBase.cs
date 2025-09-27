using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Components.Theming;
using MareSynchronos.UI.Handlers;
using System.Collections.Immutable;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public abstract class DrawFolderBase : IDrawFolder
{
    public IImmutableList<DrawUserPair> DrawPairs { get; init; }
    protected readonly string _id;
    protected readonly IImmutableList<Pair> _allPairs;
    protected readonly TagHandler _tagHandler;
    protected readonly UiSharedService _uiSharedService;
    private float _menuWidth = -1;
    public int OnlinePairs => DrawPairs.Count(u => u.Pair.IsOnline);
    public int TotalPairs => _allPairs.Count;
    protected bool _wasHovered = false;

    protected Vector4 GetDarkerColor(Vector4 color) => _wasHovered
        ? new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, color.W)
        : color;

    protected DrawFolderBase(string id, IImmutableList<DrawUserPair> drawPairs,
        IImmutableList<Pair> allPairs, TagHandler tagHandler, UiSharedService uiSharedService)
    {
        _id = id;
        DrawPairs = drawPairs;
        _allPairs = allPairs;
        _tagHandler = tagHandler;
        _uiSharedService = uiSharedService;
    }

    protected abstract bool RenderIfEmpty { get; }
    protected abstract bool RenderMenu { get; }

    public void Draw()
    {
        if (!RenderIfEmpty && !DrawPairs.Any()) return;

        using var id = ImRaii.PushId("folder_" + _id);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        var paddingX = 4f;
        var paddingY = 3f;
        using (ImRaii.Child("folder__" + _id, new System.Numerics.Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight() + (paddingY * 2))))
        {
            ImGui.SetCursorPos(new Vector2(paddingX, paddingY));

            var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

            ImGui.AlignTextToFramePadding();

            var accentColor = ThemeManager.Instance?.Current.Accent ?? ImGuiColors.HealerGreen;
            _uiSharedService.IconText(icon, GetDarkerColor(accentColor));
            if (ImGui.IsItemClicked())
            {
                _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
            }

            ImGui.SameLine();
            var leftSideEnd = DrawIcon();

            ImGui.SameLine();
            var rightSideStart = DrawRightSideInternal();

            ImGui.SameLine(leftSideEnd);
            DrawName(rightSideStart - leftSideEnd);
        }

        _wasHovered = ImGui.IsItemHovered();

        color.Dispose();

        ImGui.Separator();

        if (_tagHandler.IsTagOpen(_id))
        {
            using var indent = ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false);
            if (DrawPairs.Any())
            {
                foreach (var item in DrawPairs)
                {
                    item.DrawPairedClient();
                }
            }
            else
            {
                ImGui.TextUnformatted("No users (online)");
            }

            ImGui.Separator();
        }
    }

    protected abstract float DrawIcon();

    protected abstract void DrawMenu(float menuWidth);

    protected abstract void DrawName(float width);

    protected abstract float DrawRightSide(float currentRightSideX);

    private float DrawRightSideInternal()
    {
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();

        var rightSideStart = windowEndX - (RenderMenu ? (barButtonSize.X + spacingX) : spacingX);

        if (RenderMenu)
        {
            ImGui.SameLine(windowEndX - barButtonSize.X);

            var isRowHovered = _wasHovered;
            if (isRowHovered)
            {
                var style = ImGui.GetStyle();
                var currentButton = style.Colors[(int)ImGuiCol.Button];
                var currentButtonHovered = style.Colors[(int)ImGuiCol.ButtonHovered];
                var currentButtonActive = style.Colors[(int)ImGuiCol.ButtonActive];

                var darkerButtonColor = new Vector4(currentButton.X * 0.7f, currentButton.Y * 0.7f, currentButton.Z * 0.7f, currentButton.W);
                var darkerButtonHovered = new Vector4(currentButtonHovered.X * 0.8f, currentButtonHovered.Y * 0.8f, currentButtonHovered.Z * 0.8f, currentButtonHovered.W);
                var darkerButtonActive = new Vector4(currentButtonActive.X * 0.6f, currentButtonActive.Y * 0.6f, currentButtonActive.Z * 0.6f, currentButtonActive.W);

                ImGui.PushStyleColor(ImGuiCol.Button, darkerButtonColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, darkerButtonHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, darkerButtonActive);
            }

            bool menuButtonPressed = _uiSharedService.IconButton(FontAwesomeIcon.EllipsisV);

            if (isRowHovered)
            {
                ImGui.PopStyleColor(3);
            }

            if (menuButtonPressed)
            {
                ImGui.OpenPopup("User Flyout Menu");
            }
            if (ImGui.BeginPopup("User Flyout Menu"))
            {
                using (ImRaii.PushId($"buttons-{_id}")) DrawMenu(_menuWidth);
                _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                ImGui.EndPopup();
            }
            else
            {
                _menuWidth = 0;
            }
        }

        return DrawRightSide(rightSideStart);
    }
}