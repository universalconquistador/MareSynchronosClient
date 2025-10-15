using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI.Components.Theming;
using MareSynchronos.UI.Handlers;
using System.Collections.Immutable;
using System.Numerics;

namespace MareSynchronos.UI.Components;

// Does not extend/implement DrawFolderBase/IDrawFolder as this doesn't contain pairs
public class DrawFolderBroadcasts
{
    private const string _tagId = "broadcasts";

    protected readonly TagHandler _tagHandler;
    protected readonly UiSharedService _uiSharedService;

    readonly IImmutableList<DrawBroadcastGroup> _broadcasts;

    private bool _wasHovered;

    public DrawFolderBroadcasts(IImmutableList<DrawBroadcastGroup> broadcasts, TagHandler tagHandler, UiSharedService uiSharedService)
    {
        _broadcasts = broadcasts;
        _tagHandler = tagHandler;
        _uiSharedService = uiSharedService;
    }

    public void Draw()
    {
        bool newUI = _uiSharedService.NewUI;
        var theme = _uiSharedService.Theme;
        using (ImRaii.PushId("broadcasts"))
        {
            using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered))
            {
                var paddingX = newUI ? 4f : 0;
                var paddingY = newUI ? 3f : 0;
                using (ImRaii.Child("broadcasts_folder", new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight() + (paddingY * 2))))
                {
                    if (newUI) ImGui.SetCursorPos(new Vector2(paddingX, paddingY));

                    var expanderIcon = _tagHandler.IsTagOpen(_tagId) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

                    ImGui.AlignTextToFramePadding();

                    //_uiSharedService.IconText(expanderIcon, ThemePalette.GetDarkerColor(theme.Accent, _wasHovered));
                    _uiSharedService.IconText(expanderIcon, theme.TextPrimary);
                    if (ImGui.IsItemClicked())
                    {
                        _tagHandler.SetTagOpen(_tagId, !_tagHandler.IsTagOpen(_tagId));
                    }

                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.Wifi, theme.Accent);

                    ImGui.SameLine();
                    ImGui.TextUnformatted($"[{_broadcasts.Count}] Nearby Broadcasts");
                }
            }
            _wasHovered = ImGui.IsItemHovered();

            ImGui.Separator();

            if (_tagHandler.IsTagOpen(_tagId))
            {
                using (ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false))
                {
                    if (_broadcasts.Count > 0)
                    {
                        foreach (var broadcast in _broadcasts)
                        {
                            broadcast.Draw();
                        }
                    }
                    else
                    {
                        ImGui.TextWrapped("No broadcasts at your location");
                    }

                    ImGui.Separator();
                }
            }
        }
    }
}
