using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.UI.Components;

public class DrawBroadcastGroup
{
    // Dependencies
    protected readonly ApiController _apiController;
    protected readonly MareMediator _mediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly IBroadcastManager _broadcastManager;

    // Broadcast info
    private GroupBroadcastDto _broadcast;
    IReadOnlyList<GroupFullInfoDto> _joinedGroups;

    // State
    private readonly string _id;
    private float _menuWidth = -1;
    private bool _wasHovered = false;

    public DrawBroadcastGroup(string id, GroupBroadcastDto broadcast,
        IReadOnlyList<GroupFullInfoDto> joinedGroups,
        ApiController apiController,
        MareMediator mediator,
        ServerConfigurationManager serverConfigurationManager,
        UiSharedService uiSharedService,
        IBroadcastManager broadcastManager)
    {
        _id = id;
        _broadcast = broadcast;
        _joinedGroups = joinedGroups;
        _apiController = apiController;
        _mediator = mediator;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _broadcastManager = broadcastManager;
    }

    public bool IsJoined => _joinedGroups.Any(joined => joined.GID == _broadcast.Group.GID);

    public void Draw()
    {
        using (ImRaii.PushId(GetType() + _id))
        using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered))
        using (ImRaii.Child(GetType() + _id, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            var rightSide = DrawRightSide();
            DrawName(posX, rightSide);
        }
        _wasHovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            // TODO: Show broadcast join modal
            _mediator.Publish(new NotificationMessage("TEMP: Joining Group", $"TEMP: Joining {_broadcast.GroupAliasOrGID}", MareConfiguration.Models.NotificationType.Info));
        }
    }

    private void DrawMenu()
    {
        ImGui.TextUnformatted("Common Broadcast Functions");
    }

    private void DrawLeftSide()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, _broadcastManager.BroadcastingGroupId == _broadcast.Group.GID))
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.BroadcastTower);
        }
    }

    private void DrawName(float leftSide, float rightSide)
    {
        ImGui.SameLine(leftSide);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"[{_broadcast.CurrentMemberCount}]");
        UiSharedService.AttachToolTip($"{_broadcast.CurrentMemberCount} online");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            ImGui.TextUnformatted($"{_broadcast.GroupAliasOrGID}");
        }
        UiSharedService.AttachToolTip($"Syncshell {_broadcast.Group.GID}\nOwner: {_broadcast.OwnerAliasOrGID}\nBroadcast by: {string.Join(", ", _broadcast.Broadcasters.Select(user => user.AliasOrUID))}");
    }

    private float DrawRightSide()
    {
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        float currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
        {
            ImGui.OpenPopup("Broadcast Context Menu");
        }

        var joinIcon = IsJoined ? FontAwesomeIcon.Check : FontAwesomeIcon.ArrowRightToBracket;
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(joinIcon);
        currentRightSide -= (pauseButtonSize.X + spacingX);
        ImGui.SameLine(currentRightSide);
        using (ImRaii.Disabled(IsJoined))
        {
            if (_uiSharedService.IconButton(joinIcon))
            {
                // TODO: Show broadcast join modal
            }
        }
        UiSharedService.AttachToolTip(IsJoined
            ? $"Already member of {_broadcast.GroupAliasOrGID}"
            : $"Join {_broadcast.GroupAliasOrGID}");

        if (ImGui.BeginPopup("Broadcast Context Menu"))
        {
            using (ImRaii.PushId($"broadcast-context-{_broadcast.Group.GID}"))
            {
                DrawMenu();

                if (_menuWidth <= 0)
                {
                    _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                }
            }

            ImGui.EndPopup();
        }

        return currentRightSide - spacingX;
    }
}
