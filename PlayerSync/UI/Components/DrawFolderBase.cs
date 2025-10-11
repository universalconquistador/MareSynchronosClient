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
        var newUI = _uiSharedService.NewUI;
        var theme = _uiSharedService.Theme;
        using var id = ImRaii.PushId("folder_" + _id);
        // Hover effect for the Syncshells
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        var paddingX = newUI ? 4f : 0;
        var paddingY = newUI ? 3f : 0;
        
        using (ImRaii.Child("folder__" + _id, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight() + (paddingY * 2))))
        {
            if (newUI) ImGui.SetCursorPos(new Vector2(paddingX, paddingY));

            // draw opener
            var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

            ImGui.AlignTextToFramePadding();

            //_uiSharedService.IconText(icon, ThemePalette.GetDarkerColor(theme.Accent, _wasHovered));
            _uiSharedService.IconText(icon, theme.TextPrimary);
            if (ImGui.IsItemClicked())
            {
                _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
            }

            ImGui.SameLine();
            var leftSideEnd = DrawIcon();

            ImGui.SameLine();
            var rightSideStart = DrawRightSideInternal();

            // draw name
            ImGui.SameLine(leftSideEnd);
            DrawName(rightSideStart - leftSideEnd);
        }

        _wasHovered = ImGui.IsItemHovered();

        color.Dispose();

        ImGui.Separator();

        // if opened draw content
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
        var theme = _uiSharedService.Theme;
        bool newUI = _uiSharedService.NewUI;
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();

        var rightSideStart = windowEndX - (RenderMenu ? (barButtonSize.X + spacingX) : spacingX);

        // Flyout Menu
        if (RenderMenu)
        {
            ImGui.SameLine(windowEndX - barButtonSize.X);

            var isRowHovered = _wasHovered;
            if (isRowHovered && newUI)
            {
                //var style = ImGui.GetStyle();
                //var currentButton = style.Colors[(int)ImGuiCol.Button];
                //var currentButtonHovered = style.Colors[(int)ImGuiCol.ButtonHovered];
                //var currentButtonActive = style.Colors[(int)ImGuiCol.ButtonActive];

                ImGui.PushStyleColor(ImGuiCol.Button, ThemePalette.GetDarkerColor(theme.Btn, true));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ThemePalette.GetDarkerColor(theme.BtnHovered, true));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ThemePalette.GetDarkerColor(theme.BtnActive, true));
                
            }
            
            if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
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
            if (isRowHovered && newUI) ImGui.PopStyleColor(3);
        }

        return DrawRightSide(rightSideStart);
    }
}