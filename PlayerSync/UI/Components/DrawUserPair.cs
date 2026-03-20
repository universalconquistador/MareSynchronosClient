using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawUserPair
{
    protected readonly ApiController _apiController;
    protected readonly IdDisplayHandler _displayHandler;
    protected readonly MareMediator _mediator;
    protected readonly List<GroupFullInfoDto> _syncedGroups;
    private readonly GroupFullInfoDto? _currentGroup;
    protected Pair _pair;
    private readonly string _id;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerPerformanceConfigService _performanceConfigService;
    private readonly CharaDataManager _charaDataManager;
    private float _menuWidth = -1;
    private bool _wasHovered = false;
    private PairDrawSavedCache _drawSavedCache;
    private PairDrawCache? _drawCache;

    public DrawUserPair(string id, Pair entry, List<GroupFullInfoDto> syncedGroups,
        GroupFullInfoDto? currentGroup,
        ApiController apiController, IdDisplayHandler uIDDisplayHandler,
        MareMediator mareMediator, SelectTagForPairUi selectTagForPairUi,
        ServerConfigurationManager serverConfigurationManager,
        UiSharedService uiSharedService, PlayerPerformanceConfigService performanceConfigService,
        CharaDataManager charaDataManager)
    {
        _id = id;
        _pair = entry;
        _syncedGroups = syncedGroups;
        _currentGroup = currentGroup;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
        _mediator = mareMediator;
        _selectTagForPairUi = selectTagForPairUi;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _performanceConfigService = performanceConfigService;
        _charaDataManager = charaDataManager;
    }

    public Pair Pair => _pair;
    public UserFullPairDto UserPair => _pair.UserPair!;

    public void DrawPairedClient()
    {
        UpdateCacheIfNeeded();
        using var id = ImRaii.PushId(GetType() + _id);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        using (ImRaii.Child(GetType() + _id, new System.Numerics.Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            var rightSide = DrawRightSide();
            DrawName(posX, rightSide);
        }
        _wasHovered = ImGui.IsItemHovered();
        color.Dispose();
    }

    private void DrawCommonClientMenu()
    {
        if (!_pair.IsPaused)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy UID", _menuWidth, true))
            {
                ImGui.SetClipboardText(_pair.UserPair.User.UID);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Copy to clipboard the UID for this user");
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile", _menuWidth, true))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (_pair.IsVisible)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, "Reload last data", _menuWidth, true))
            {
                _pair.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state", _menuWidth, true))
        {
            _ = _apiController.CyclePauseAsync(_pair.UserData);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("Pair Permission Functions");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.WindowMaximize, "Open Permissions Window", _menuWidth, true))
        {
            _mediator.Publish(new OpenPermissionWindow(_pair));
            ImGui.CloseCurrentPopup();
        }
        UiSharedService.AttachToolTip("Opens the Permissions Window which allows you to manage multiple permissions at once.");

        var isSticky = _pair.UserPair!.OwnPermissions.IsSticky();
        string stickyText = isSticky ? "Disable Preferred Permissions" : "Enable Preferred Permissions";
        var stickyIcon = isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp;
        if (_uiSharedService.IconTextButton(stickyIcon, stickyText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetSticky(!isSticky);
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Preferred permissions means that this pair will not" + Environment.NewLine + " be affected by any syncshell permission changes through you.");

        string individualText = Environment.NewLine + Environment.NewLine + "Note: changing this permission will turn the permissions for this"
            + Environment.NewLine + "user to preferred permissions. You can change this behavior"
            + Environment.NewLine + "in the permission settings.";
        bool individual = !_pair.IsDirectlyPaired && _apiController.DefaultPermissions!.IndividualIsSticky;

        var isDisableSounds = _pair.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "Enable sound sync" : "Disable sound sync";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (_uiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes sound sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableAnims = _pair.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes animation sync permissions with this user." + (individual ? individualText : string.Empty));

        var isDisableVFX = _pair.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Changes VFX sync permissions with this user." + (individual ? individualText : string.Empty));
    }

    private void DrawIndividualMenu()
    {
        ImGui.TextUnformatted("Individual Pair Functions");
        var entryUID = _pair.UserData.AliasOrUID;

        if (_pair.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups", _menuWidth, true))
            {
                _selectTagForPairUi.Open(_pair);
            }
            UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Unpair User", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                _ = _apiController.UserRemovePair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to unpair from " + entryUID);
        }
        if (_pair.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.Bidirectional)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Send Pair Request", _menuWidth, true))
            {
                _ = _apiController.UserMakePairRequest(new(UserData: _pair.UserData));
            }
            UiSharedService.AttachToolTip("Send pair request to " + entryUID);
        }
        if (!_pair.UserPair!.OwnPermissions.IsPaused())
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Keep Paused", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                _ = _apiController.UserPairStickyPauseAndRemove(_pair.UserData);
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to keep paused " + entryUID);
        }
        else
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Play, "Resume Pairing", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                var perm = _pair.UserPair!.OwnPermissions;
                perm.SetSticky(true);
                perm.SetPaused(paused: false);
                _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to resume pairing with " + entryUID);
        }
    }

    private void DrawLeftSide()
    {
        ImGui.AlignTextToFramePadding();

        _uiSharedService.IconText(_drawCache!.LeftIcon, _drawCache.LeftIconColor);
        if (_drawCache.LeftIconClickable && ImGui.IsItemClicked())
            _mediator.Publish(new TargetPairMessage(_pair));

        UiSharedService.AttachToolTip(_drawCache.UserPairTooltip);

        if (_drawCache.ShowPerfWarning)
        {
            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
            UiSharedService.AttachToolTip(_drawCache.PerfWarningTooltip);
        }

        ImGui.SameLine();
    }

    private void DrawName(float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, () => rightSide - leftSide);
    }

    private void DrawPairedClientMenu()
    {
        DrawIndividualMenu();

        if (_syncedGroups.Any()) ImGui.Separator();
        foreach (var entry in _syncedGroups)
        {
            bool selfIsOwner = string.Equals(_apiController.UID, entry.Owner.UID, StringComparison.Ordinal);
            bool selfIsModerator = entry.GroupUserInfo.IsModerator();
            bool userIsModerator = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var modinfo) && modinfo.IsModerator();
            bool userIsPinned = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsPinned();
            if (selfIsOwner || selfIsModerator)
            {
                var groupNote = _serverConfigurationManager.GetNoteForGid(entry.GID);
                var groupString = string.IsNullOrEmpty(groupNote) ? entry.GroupAliasOrGID : $"{groupNote} ({entry.GroupAliasOrGID})";

                if (ImGui.BeginMenu(groupString + " Moderation Functions"))
                {
                    DrawSyncshellMenu(entry, selfIsOwner, selfIsModerator, userIsPinned, userIsModerator);
                    ImGui.EndMenu();
                }
            }
        }
    }

    private float DrawRightSide()
    {
        var cache = _drawCache!;
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(cache.PauseIcon);
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        float currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }

        currentRightSide -= (pauseButtonSize.X + spacingX);
        ImGui.SameLine(currentRightSide);
        if (_uiSharedService.IconButton(cache.PauseIcon))
        {
            var perm = _pair.UserPair!.OwnPermissions;

            if (UiSharedService.CtrlPressed() && !perm.IsPaused())
            {
                perm.SetSticky(true);
            }

            if (!_pair.IsPaused)
                _serverConfigurationManager.SetPauseReasonForUid(_pair.UserData.UID, PauseReason.Manual);

            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
        }
        UiSharedService.AttachToolTip(cache.PauseTooltip);

        if (cache.ShowIndividualBlock)
        {
            currentRightSide -= (_uiSharedService.GetIconSize(cache.IndividualIcon).X + spacingX);
            ImGui.SameLine(currentRightSide);
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, cache.IndividualAnimDisabled || cache.IndividualSoundsDisabled || cache.IndividualVFXDisabled))
                _uiSharedService.IconText(cache.IndividualIcon);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Individual User permissions");
                ImGui.Separator();

                if (cache.IndividualIsSticky)
                {
                    _uiSharedService.IconText(cache.IndividualIcon);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Preferred permissions enabled");
                    if (cache.IndividualAnimDisabled || cache.IndividualSoundsDisabled || cache.IndividualVFXDisabled)
                        ImGui.Separator();
                }

                if (cache.IndividualSoundsDisabled)
                {
                    _uiSharedService.IconText(FontAwesomeIcon.VolumeOff);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Sound sync");
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("You");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableSounds());
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("They");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableSounds());
                }

                if (cache.IndividualAnimDisabled)
                {
                    _uiSharedService.IconText(FontAwesomeIcon.Stop);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Animation sync");
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("You");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableAnimations());
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("They");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableAnimations());
                }

                if (cache.IndividualVFXDisabled)
                {
                    _uiSharedService.IconText(FontAwesomeIcon.Circle);
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("VFX sync");
                    ImGui.NewLine();
                    ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("You");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableVFX());
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("They");
                    _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableVFX());
                }

                ImGui.EndTooltip();
            }
        }

        if (cache.ShowSharedData)
        {
            currentRightSide -= (_uiSharedService.GetIconSize(FontAwesomeIcon.Running).X + (spacingX / 2f));
            ImGui.SameLine(currentRightSide);
            _uiSharedService.IconText(FontAwesomeIcon.Running);
            UiSharedService.AttachToolTip(cache.SharedDataTooltip);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
            }
        }

        if (cache.GroupRoleIcon != FontAwesomeIcon.None)
        {
            currentRightSide -= (_uiSharedService.GetIconSize(cache.GroupRoleIcon).X + spacingX);
            ImGui.SameLine(currentRightSide);
            _uiSharedService.IconText(cache.GroupRoleIcon);
            UiSharedService.AttachToolTip(cache.GroupRoleTooltip);
        }

        if (cache.ShowSoundIcon)
        {
            var icon = FontAwesomeIcon.VolumeOff;
            currentRightSide -= _uiSharedService.GetIconSize(icon).X + spacingX;
            ImGui.SameLine(currentRightSide);
            _uiSharedService.IconText(icon, ImGuiColors.HealerGreen);
            UiSharedService.AttachToolTip($"Started playing modded audio {UiSharedService.ApproxElapsedTimeToString(DateTimeOffset.UtcNow - Pair.LastLoadedSoundSinceRedraw!.Value)}.{UiSharedService.TooltipSeparator}CTRL + Click to disable sound sync with {_pair.UserData.AliasOrUID}.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && UiSharedService.CtrlPressed())
            {
                var perm = _pair.UserPair!.OwnPermissions;
                perm.SetSticky(true);
                perm.SetDisableSounds(true);
                _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
            }
        }

        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{_pair.UserData.UID}"))
            {
                ImGui.TextUnformatted("Common Pair Functions");
                DrawCommonClientMenu();
                ImGui.Separator();
                DrawPairedClientMenu();
                if (_menuWidth <= 0)
                {
                    _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                }
            }

            ImGui.EndPopup();
        }

        return currentRightSide - spacingX;
    }

    private void DrawSyncshellMenu(GroupFullInfoDto group, bool selfIsOwner, bool selfIsModerator, bool userIsPinned, bool userIsModerator)
    {
        if (selfIsOwner || ((selfIsModerator) && (!userIsModerator)))
        {
            ImGui.TextUnformatted("Syncshell Moderator Functions");
            var pinText = userIsPinned ? "Unpin user" : "Pin user";
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText, _menuWidth, true))
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsPinned;
                }
                else
                {
                    userinfo.SetPinned(!userinfo.IsPinned());
                }
                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove user", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupRemoveUser(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User", _menuWidth, true))
            {
                _mediator.Publish(new OpenBanUserPopupMessage(_pair, group));
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Ban user from this Syncshell");

            ImGui.Separator();
        }

        if (selfIsOwner)
        {
            ImGui.TextUnformatted("Syncshell Owner Functions");
            string modText = userIsModerator ? "Demod user" : "Mod user";
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText, _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsModerator;
                }
                else
                {
                    userinfo.SetModerator(!userinfo.IsModerator());
                }

                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + (_pair.UserData.AliasOrUID) + Environment.NewLine +
                "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership", _menuWidth, true) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupChangeOwnership(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to "
                + (_pair.UserData.AliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
        }
    }

    private FontAwesomeIcon ComputeGroupRoleIcon()
    {
        if (_currentGroup == null) return FontAwesomeIcon.None;
        if (string.Equals(_currentGroup.OwnerUID, _pair.UserData.UID, StringComparison.Ordinal))
            return FontAwesomeIcon.Crown;
        if (_currentGroup.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
        {
            if (userinfo.IsModerator()) return FontAwesomeIcon.UserShield;
            if (userinfo.IsPinned()) return FontAwesomeIcon.Thumbtack;
            if (userinfo.IsGuest()) return FontAwesomeIcon.PersonWalkingLuggage;
        }
        return FontAwesomeIcon.None;
    }

    private int ComputeSyncedGroupsHash()
    {
        var h = new HashCode();
        foreach (var g in _syncedGroups)
            h.Add(HashCode.Combine(g.GID, _serverConfigurationManager.GetNoteForGid(g.GID) ?? string.Empty));
        return h.ToHashCode();
    }

    private PairDrawSavedCache BuildSavedCache()
    {
        bool hasSharedData = _charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData);
        var perfCfg = _performanceConfigService.Current;
        return new PairDrawSavedCache(
            _pair.IsPaused,
            _pair.IsOnline,
            _pair.IsVisible,
            _pair.IndividualPairStatus,
            _pair.IsPaired,
            _pair.PlayerName,
            _pair.LastAppliedDataBytes,
            _pair.LastAppliedApproximateVRAMBytes,
            _pair.LastAppliedDataTris,
            _pair.UserPair!.OwnPermissions,
            _pair.UserPair.OtherPermissions,
            hasSharedData,
            hasSharedData ? sharedData!.Count : 0,
            _pair.LastLoadedSoundSinceRedraw.HasValue,
            ComputeGroupRoleIcon(),
            ComputeSyncedGroupsHash(),
            perfCfg.ShowPerformanceIndicator,
            perfCfg.UIDsToIgnore.Exists(uid =>
                string.Equals(uid, UserPair.User.Alias, StringComparison.Ordinal) ||
                string.Equals(uid, UserPair.User.UID, StringComparison.Ordinal)),
            perfCfg.VRAMSizeWarningThresholdMiB,
            perfCfg.TrisWarningThresholdThousands,
            perfCfg.WarnOnPreferredPermissionsExceedingThresholds);
    }

    private void UpdateCacheIfNeeded()
    {
        var savedCache = BuildSavedCache();
        if (_drawCache != null && savedCache == _drawSavedCache) return;
        RebuildCache(savedCache);
    }

    private void RebuildCache(PairDrawSavedCache savedCache)
    {
        var cache = _drawCache ?? new PairDrawCache();
        var pair = _pair;
        var ownPerm = pair.UserPair!.OwnPermissions;
        var otherPerm = pair.UserPair.OtherPermissions;
        var permSticky = ownPerm.IsSticky();
        var aliasOrUID = pair.UserData.AliasOrUID;

        // Left side
        if (pair.IsPaused && !permSticky)
        {
            cache.LeftIcon = FontAwesomeIcon.PauseCircle;
            cache.LeftIconColor = ImGui.GetColorU32(ImGuiColors.DalamudYellow);
            cache.LeftIconClickable = false;
            cache.UserPairTooltip = BuildLeftTooltip(aliasOrUID + " is paused", pair);
        }
        else if (pair.IsPaused)
        {
            cache.LeftIcon = FontAwesomeIcon.FilterCircleXmark;
            cache.LeftIconColor = ImGui.GetColorU32(ImGuiColors.DalamudRed);
            cache.LeftIconClickable = false;
            cache.UserPairTooltip = BuildLeftTooltip(aliasOrUID + " is paused (sticky)", pair);
        }
        else if (!pair.IsOnline)
        {
            cache.LeftIcon = pair.IndividualPairStatus == IndividualPairStatus.OneSided
                ? FontAwesomeIcon.ArrowsLeftRight
                : (pair.IndividualPairStatus == IndividualPairStatus.Bidirectional
                    ? FontAwesomeIcon.User : FontAwesomeIcon.Users);
            cache.LeftIconColor = ImGui.GetColorU32(ImGuiColors.DalamudRed);
            cache.LeftIconClickable = false;
            cache.UserPairTooltip = BuildLeftTooltip(aliasOrUID + " is offline", pair);
        }
        else if (pair.IsVisible)
        {
            cache.LeftIcon = FontAwesomeIcon.Eye;
            cache.LeftIconColor = ImGui.GetColorU32(ImGuiColors.ParsedGreen);
            cache.LeftIconClickable = true;
            cache.UserPairTooltip = BuildLeftTooltip(aliasOrUID + " is visible: " + pair.PlayerName + Environment.NewLine + "Click to target this player", pair);
        }
        else
        {
            cache.LeftIcon = pair.IndividualPairStatus == IndividualPairStatus.Bidirectional
                ? FontAwesomeIcon.User : FontAwesomeIcon.Users;
            cache.LeftIconColor = ImGui.GetColorU32(ImGuiColors.HealerGreen);
            cache.LeftIconClickable = false;
            cache.UserPairTooltip = BuildLeftTooltip(aliasOrUID + " is online", pair);
        }

        // Perf warning
        var perfCfg = _performanceConfigService.Current;
        cache.ShowPerfWarning = perfCfg.ShowPerformanceIndicator
            && !savedCache.IsInIgnoreList
            && ((perfCfg.VRAMSizeWarningThresholdMiB > 0 && perfCfg.VRAMSizeWarningThresholdMiB * 1024 * 1024 < pair.LastAppliedApproximateVRAMBytes)
                || (perfCfg.TrisWarningThresholdThousands > 0 && perfCfg.TrisWarningThresholdThousands * 1000 < pair.LastAppliedDataTris))
            && (!ownPerm.IsSticky() || perfCfg.WarnOnPreferredPermissionsExceedingThresholds);

        if (cache.ShowPerfWarning)
        {
            string warningText = "WARNING: This user exceeds one or more of your defined thresholds:" + UiSharedService.TooltipSeparator;
            bool shownVram = false;
            if (perfCfg.VRAMSizeWarningThresholdMiB > 0 && perfCfg.VRAMSizeWarningThresholdMiB * 1024 * 1024 < pair.LastAppliedApproximateVRAMBytes)
            {
                shownVram = true;
                warningText += $"Approx. VRAM Usage: Used: {UiSharedService.ByteToString(pair.LastAppliedApproximateVRAMBytes)}, Threshold: {perfCfg.VRAMSizeWarningThresholdMiB} MiB";
            }
            if (perfCfg.TrisWarningThresholdThousands > 0 && perfCfg.TrisWarningThresholdThousands * 1000 < pair.LastAppliedDataTris)
            {
                if (shownVram) warningText += Environment.NewLine;
                warningText += $"Approx. Triangle count: Used: {pair.LastAppliedDataTris}, Threshold: {perfCfg.TrisWarningThresholdThousands * 1000}";
            }
            cache.PerfWarningTooltip = warningText;
        }

        // Right side
        cache.PauseIcon = ownPerm.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        cache.PauseTooltip = !ownPerm.IsPaused()
            ? ("Pause pairing with " + aliasOrUID
                + (ownPerm.IsSticky()
                    ? string.Empty
                    : UiSharedService.TooltipSeparator + "Hold CTRL to enable preferred permissions while pausing." + Environment.NewLine + "This will leave this pair paused even if unpausing syncshells including this pair."))
            : "Resume pairing with " + aliasOrUID;

        if (pair.IsPaired)
        {
            var soundsDisabled = ownPerm.IsDisableSounds() || otherPerm.IsDisableSounds();
            var animDisabled = ownPerm.IsDisableAnimations() || otherPerm.IsDisableAnimations();
            var vfxDisabled = ownPerm.IsDisableVFX() || otherPerm.IsDisableVFX();
            cache.IndividualSoundsDisabled = soundsDisabled;
            cache.IndividualAnimDisabled = animDisabled;
            cache.IndividualVFXDisabled = vfxDisabled;
            cache.IndividualIsSticky = permSticky;
            cache.IndividualIcon = permSticky ? FontAwesomeIcon.ArrowCircleUp : FontAwesomeIcon.InfoCircle;
            cache.ShowIndividualBlock = animDisabled || soundsDisabled || vfxDisabled || permSticky;
        }
        else
        {
            cache.ShowIndividualBlock = false;
            cache.IndividualSoundsDisabled = false;
            cache.IndividualAnimDisabled = false;
            cache.IndividualVFXDisabled = false;
            cache.IndividualIsSticky = false;
            cache.IndividualIcon = FontAwesomeIcon.None;
        }

        cache.ShowSharedData = savedCache.HasSharedData;
        cache.SharedDataCount = savedCache.SharedDataCount;
        if (savedCache.HasSharedData)
            cache.SharedDataTooltip = $"This user has shared {savedCache.SharedDataCount} Character Data Sets with you."
                + UiSharedService.TooltipSeparator + "Click to open the Character Data Hub and show the entries.";

        cache.GroupRoleIcon = savedCache.GroupRoleIcon;
        cache.GroupRoleTooltip = savedCache.GroupRoleIcon switch
        {
            FontAwesomeIcon.Crown => "User is owner of this syncshell",
            FontAwesomeIcon.UserShield => "User is moderator in this syncshell",
            FontAwesomeIcon.Thumbtack => "User is pinned in this syncshell",
            FontAwesomeIcon.PersonWalkingLuggage => "User is guest in this syncshell",
            _ => string.Empty
        };

        cache.ShowSoundIcon = savedCache.HasSoundIcon;

        _drawCache = cache;
        _drawSavedCache = savedCache;
    }

    private string BuildLeftTooltip(string baseText, Pair pair)
    {
        var text = baseText;

        if (pair.IndividualPairStatus == IndividualPairStatus.OneSided)
            text += UiSharedService.TooltipSeparator + "User has not added you back";
        else if (pair.IndividualPairStatus == IndividualPairStatus.Bidirectional)
            text += UiSharedService.TooltipSeparator + "You are directly Paired";

        if (pair.LastAppliedDataBytes >= 0)
        {
            text += UiSharedService.TooltipSeparator;
            text += ((!pair.IsPaired) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
            text += "Files Size: " + UiSharedService.ByteToString(pair.LastAppliedDataBytes, true);
            if (pair.LastAppliedApproximateVRAMBytes >= 0)
                text += Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(pair.LastAppliedApproximateVRAMBytes, true);
            if (pair.LastAppliedDataTris >= 0)
                text += Environment.NewLine + "Approx. Triangle Count (excl. Vanilla): "
                    + (pair.LastAppliedDataTris > 1000 ? (pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : pair.LastAppliedDataTris);
        }

        if (_syncedGroups.Any())
        {
            text += UiSharedService.TooltipSeparator + string.Join(Environment.NewLine,
                _syncedGroups.Select(g =>
                {
                    var groupNote = _serverConfigurationManager.GetNoteForGid(g.GID);
                    var groupString = string.IsNullOrEmpty(groupNote) ? g.GroupAliasOrGID : $"{groupNote} ({g.GroupAliasOrGID})";
                    return "Paired through " + groupString;
                }));
        }

        return text;
    }

    private readonly record struct PairDrawSavedCache(
        bool IsPaused,
        bool IsOnline,
        bool IsVisible,
        IndividualPairStatus PairStatus,
        bool IsPaired,
        string? PlayerName,
        long LastAppliedDataBytes,
        long LastAppliedApproximateVRAMBytes,
        long LastAppliedDataTris,
        UserPermissions OwnPermissions,
        UserPermissions OtherPermissions,
        bool HasSharedData,
        int SharedDataCount,
        bool HasSoundIcon,
        FontAwesomeIcon GroupRoleIcon,
        int SyncedGroupsHash,
        bool ShowPerfIndicator,
        bool IsInIgnoreList,
        int VRAMThresholdMiB,
        int TrisThresholdThousands,
        bool WarnOnPreferred);

    private sealed class PairDrawCache
    {
        public FontAwesomeIcon LeftIcon;
        public uint LeftIconColor;
        public bool LeftIconClickable;
        public string UserPairTooltip = string.Empty;
        public bool ShowPerfWarning;
        public string PerfWarningTooltip = string.Empty;
        public FontAwesomeIcon PauseIcon;
        public string PauseTooltip = string.Empty;
        public bool ShowIndividualBlock;
        public FontAwesomeIcon IndividualIcon;
        public bool IndividualIsSticky;
        public bool IndividualSoundsDisabled;
        public bool IndividualAnimDisabled;
        public bool IndividualVFXDisabled;
        public bool ShowSharedData;
        public int SharedDataCount;
        public string SharedDataTooltip = string.Empty;
        public FontAwesomeIcon GroupRoleIcon;
        public string GroupRoleTooltip = string.Empty;
        public bool ShowSoundIcon;
    }
}