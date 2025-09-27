using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI.Components.Theming;
using MareSynchronos.UI.Handlers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.UI.Components;

public class DrawFolderBroadcasts
{
    private const string _tagId = "broadcasts";

    protected readonly TagHandler _tagHandler;
    protected readonly UiSharedService _uiSharedService;

    readonly IImmutableList<DrawBroadcastGroup> _broadcasts;

    private bool _wasHovered;

    protected Vector4 GetDarkerColor(Vector4 color) => _wasHovered
        ? new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, color.W)
        : color;

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
            {
                var paddingX = 4f;
                var paddingY = 3f;
                using (ImRaii.Child("broadcasts_folder", new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight() + (paddingY * 2))))
                {
                    ImGui.SetCursorPos(new Vector2(paddingX, paddingY));

                    var expanderIcon = _tagHandler.IsTagOpen(_tagId) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

                    ImGui.AlignTextToFramePadding();

                    var accentColor = ThemeManager.Instance?.Current.Accent ?? ImGuiColors.HealerGreen;
                    _uiSharedService.IconText(expanderIcon, GetDarkerColor(accentColor));
                    if (ImGui.IsItemClicked())
                    {
                        _tagHandler.SetTagOpen(_tagId, !_tagHandler.IsTagOpen(_tagId));
                    }

                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.Wifi, GetDarkerColor(accentColor));

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
