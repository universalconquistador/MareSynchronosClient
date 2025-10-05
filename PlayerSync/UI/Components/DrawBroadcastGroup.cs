using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.UI.Components.Theming;
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
    GroupFullInfoDto? _joinedGroup;

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
        _joinedGroup = joinedGroups.FirstOrDefault(group => group.GID == _broadcast.Group.GID);
        _apiController = apiController;
        _mediator = mediator;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _broadcastManager = broadcastManager;
    }

    public bool IsJoined => _joinedGroup != null;
    public bool IsModerator => _joinedGroup?.GroupUserInfo.IsModerator() ?? false;
    public bool IsOwner => string.Equals(_joinedGroup?.OwnerUID, _apiController.UID, StringComparison.Ordinal);

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
    }

    private void DrawMenu()
    {
        ImGui.TextUnformatted("Common Broadcast Functions");
    }

    private void DrawLeftSide()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ThemeManager.Instance?.Current.StatusBroadcasting ?? new Vector4(0.094f, 0.835f, 0.369f, 1f), _broadcastManager.BroadcastingGroupId == _broadcast.Group.GID))
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.BroadcastTower, _broadcastManager.BroadcastingGroupId == _broadcast.Group.GID ? null : ThemeManager.Instance?.Current.Accent);
        }
    }

    private void DrawName(float leftSide, float rightSide)
    {
        ImGui.SameLine(leftSide);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"[{_broadcast.CurrentMemberCount}]");
        UiSharedService.AttachToolTip($"{_broadcast.CurrentMemberCount} members");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.MonoFont))
        {
            ImGui.TextUnformatted($"{_broadcast.GroupAliasOrGID}");
        }
        UiSharedService.AttachToolTip($"Syncshell {_broadcast.Group.AliasOrGID}\nOwner: {_broadcast.Owner.UID}\nBroadcast by: {string.Join(", ", _broadcast.Broadcasters.Select(user => user.UID))}");
    }

    private float DrawRightSide()
    {
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        float currentRightSide = windowEndX;

        // Uncomment this if we add menu commands
        //var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        //currentRightSide -= barButtonSize.X;
        //ImGui.SameLine(currentRightSide);
        //ImGui.AlignTextToFramePadding();
        //if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
        //{
        //    ImGui.OpenPopup("Broadcast Context Menu");
        //}
        //currentRightSide -= spacingX;

        FontAwesomeIcon joinIcon;
        string joinTooltip;
        if (IsJoined)
        {
            joinIcon = FontAwesomeIcon.Check;
            if (IsOwner)
            {
                joinTooltip = $"You are already the owner of {_broadcast.GroupAliasOrGID}";
            }
            else if (IsModerator)
            {
                joinTooltip = $"You are already a moderator in {_broadcast.GroupAliasOrGID}";
            }
            else
            {
                joinTooltip = $"You are already a member of {_broadcast.GroupAliasOrGID}";
            }
        }
        else
        {
            joinIcon = FontAwesomeIcon.ArrowRightToBracket;
            joinTooltip = $"Join {_broadcast.GroupAliasOrGID}";
        }
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(joinIcon);
        currentRightSide -= pauseButtonSize.X;
        ImGui.SameLine(currentRightSide);
        using (ImRaii.Disabled(IsJoined))
        {
            if (_uiSharedService.IconButton(joinIcon))
            {
                _mediator.Publish(new UiToggleMessage(typeof(JoinSyncshellUI)));
                _mediator.Publish(new PrefillJoinSyncshellParameters(_broadcast.Group.GID, _broadcast.Passwordless, _broadcast.IsGuestModeEnabled));
            }
        }
        UiSharedService.AttachToolTip(joinTooltip);

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
