using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI.Handlers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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
        using (ImRaii.PushId("broadcasts"))
        {
            using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered))
            using (ImRaii.Child("broadcasts_folder", new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
            {
                var expanderIcon = _tagHandler.IsTagOpen(_tagId) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

                ImGui.AlignTextToFramePadding();

                _uiSharedService.IconText(expanderIcon);
                if (ImGui.IsItemClicked())
                {
                    _tagHandler.SetTagOpen(_tagId, !_tagHandler.IsTagOpen(_tagId));
                }

                ImGui.SameLine();
                _uiSharedService.IconText(FontAwesomeIcon.Wifi);

                ImGui.SameLine();
                ImGui.TextUnformatted($"[{_broadcasts.Count}] Nearby Broadcasts");
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
