using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MareSynchronos.UI.Components.Popup;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly bool _isModerator = false;
    private readonly bool _isOwner = false;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private readonly IBroadcastManager _broadcastManager;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private int _multiInvites;
    private string _newPassword;
    private bool _pwChangeSuccess;
    private Task<int>? _pruneTestTask;
    private Task<int>? _pruneTask;
    private int _pruneDays = 14;
    private Memory<byte> _rulesBuffer = new byte[2000];
    private Memory<byte> _descriptionBuffer = new byte[2000];
    private bool _isProfileSaved;
    private readonly UiTheme _theme = new();
    private UiNav.NavItem<SyncshellAdminNav>? _selectedNavItem;
    private UiNav.Tab<SyncshellAdminTabs>? _selectedTab;

    public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, MareMediator mediator, ApiController apiController,
        UiSharedService uiSharedService, IBroadcastManager broadcastManager, PairManager pairManager, GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Syncshell Admin Panel (" + groupFullInfo.GroupAliasOrGID + ")", performanceCollectorService)
    {
        GroupFullInfo = groupFullInfo;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _broadcastManager = broadcastManager;
        _pairManager = pairManager;
        _isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.UID, System.StringComparison.Ordinal);
        _isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _multiInvites = 30;
        _pwChangeSuccess = true;
        IsOpen = true;
        _isProfileSaved = true;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(1020, 700),
            MaximumSize = new(1020, 2000),
        };
        Mediator.Subscribe<GroupInfoChanged>(this, message =>
        {
            Encoding.UTF8.GetBytes(message.GroupInfo.PublicData.GroupProfile?.Description ?? "", _descriptionBuffer.Span);
            Encoding.UTF8.GetBytes(message.GroupInfo.PublicData.GroupProfile?.Rules ?? "", _rulesBuffer.Span);
        });
        Encoding.UTF8.GetBytes(GroupFullInfo.PublicData.GroupProfile?.Description ?? "", _descriptionBuffer.Span);
        Encoding.UTF8.GetBytes(GroupFullInfo.PublicData.GroupProfile?.Rules ?? "", _rulesBuffer.Span);
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    private enum SyncshellAdminNav
    {
        Access,
        UserManagement,
        Permissions,
        Profile,
        Owner
    }
    private enum SyncshellAdminTabs
    {
        UserList,
        Cleanup,
        Bans
    }
    protected override void DrawInternal()
    {
        using var windowStyle = _theme.PushWindowStyle();

        if (!_isModerator && !_isOwner) return;

        GroupFullInfo = _pairManager.Groups[GroupFullInfo.Group];

        using var id = ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID);

        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");

        if (GroupFullInfo.PublicData.KnownPasswordless)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
            {
                _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle);

                ImGui.SameLine();
                ImGui.TextUnformatted("This Syncshell has no password.");
            }
        }

        if (GroupFullInfo.GroupPermissions.IsEnableGuestMode())
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
            {
                _uiSharedService.IconText(FontAwesomeIcon.PersonWalkingLuggage);

                ImGui.SameLine();
                ImGui.TextUnformatted("This Syncshell has guest mode enabled.");
            }
        }

        //Ui.AddVerticalSpace(6);
        //ImGui.Separator();
        Ui.AddVerticalSpace(6);

        var groupItems = new List<UiNav.NavItem<SyncshellAdminNav>>
        {
            new(SyncshellAdminNav.Access, "Access", DrawAccess, FontAwesomeIcon.DoorOpen),
            new(SyncshellAdminNav.UserManagement, "User Management", DrawUserManagement, FontAwesomeIcon.Users),
            new(SyncshellAdminNav.Permissions, "Permissions", DrawPermissions, FontAwesomeIcon.Key),
            new(SyncshellAdminNav.Profile, "Profile", DrawProfile, FontAwesomeIcon.User),
        };

        if (_isOwner)
            groupItems.Add(new(SyncshellAdminNav.Owner, "Owner Settings", DrawOwnerSettings, FontAwesomeIcon.Crown));

        var navGroups = new List<(string GroupLabel, IReadOnlyList<UiNav.NavItem<SyncshellAdminNav>> Items)>
        {
            ("", groupItems),
        };

        _selectedNavItem = UiNav.DrawSidebar(_theme, "Syncshell Admin", navGroups, _selectedNavItem, widthPx: 220f, iconFont: _uiSharedService.IconFont);

        var panePad = UiScale.ScaledFloat(_theme.PanelPad);
        var paneGap = UiScale.ScaledFloat(_theme.PanelGap);

        ImGui.SameLine(0, paneGap);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(panePad, panePad));
        using var content = ImRaii.Child("##content", new Vector2(0, 0), false, ImGuiWindowFlags.None);

        _selectedNavItem.NavAction.Invoke();

    }

    private void DrawProfile()
    {
        _uiSharedService.BigText("Profile");
        ImGuiHelpers.ScaledDummy(2);
        _uiSharedService.HeaderText("Syncshell Description");
        if (ImGui.InputTextMultiline("###description_input", _descriptionBuffer.Span, new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 300)))
            _isProfileSaved = false;

        ImGuiHelpers.ScaledDummy(2f);

        ImGuiHelpers.ScaledDummy(2f);
        _uiSharedService.HeaderText("Syncshell Rules");
        if (ImGui.InputTextMultiline("###rules_input", _rulesBuffer.Span, new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 300)))
            _isProfileSaved = false;

        ImGuiHelpers.ScaledDummy(2f);
        using (ImRaii.Disabled(_isProfileSaved))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save"))
            {
                string? rules = Encoding.UTF8.GetString(_rulesBuffer.Span.Slice(0, _rulesBuffer.Span.IndexOf((byte)0))) ?? "";
                string? description = Encoding.UTF8.GetString(_descriptionBuffer.Span.Slice(0, _descriptionBuffer.Span.IndexOf((byte)0))) ?? "";
                _ = _apiController.GroupSetProfile(new(GroupFullInfo.Group), new(rules, description));
                _isProfileSaved = true;
            }
        }
        ImGuiHelpers.ScaledDummy(2f);
            
    }

    private void DrawAccess()
    {
        _uiSharedService.BigText("Access");
        ImGuiHelpers.ScaledDummy(2);

        var perm = GroupFullInfo.GroupPermissions;
        bool isInvitesDisabled = perm.IsDisableInvites();

        _uiSharedService.HeaderText("Lock Syncshell");
        UiSharedService.TextWrapped("Locked Syncshells will prevent anyone from being able to join.");
        if (_uiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
            isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
        {
            perm.SetDisableInvites(!isInvitesDisabled);
            _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
        }
        ImGuiHelpers.ScaledDummy(5f);
        _uiSharedService.HeaderText("Guest Mode");
        UiSharedService.TextWrapped("Guest Mode enables joining a Syncshell without a password." + Environment.NewLine 
            + "Users joining without a password will be marked as \"guests\" in the User Management tab.");
        bool enabledGuest = perm.IsEnableGuestMode();
        if (!enabledGuest)
        {
            using (ImRaii.Disabled(!GroupFullInfo.GroupPermissions.IsEnableGuestMode() && !UiSharedService.CtrlPressed()))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonWalkingLuggage, "Enable Guest Mode"))
                {
                    perm.SetEnableGuestMode(true);
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }
            }
            UiSharedService.AttachToolTip("Players will be able to join the Syncshell without a password.\nHold CTRL and click if you are sure you want to enable this.");
        }
        else
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Disable Guest Mode"))
            {
                perm.SetEnableGuestMode(false);
                _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
            }
        }
        ImGuiHelpers.ScaledDummy(5f);
        _uiSharedService.HeaderText("Broadcast Mode");
        UiSharedService.TextWrapped("Enabling Broadcast Mode will allow other players around you see and join your Syncshell from the Nearby Broadcasts list." + Environment.NewLine +
            "The owner of a Syncshell and anyone with mod access may enable broadcasting at any time. This setting is per-player, meaning only those with it enabled with be broadcasting the Syncshell.");
        UiSharedService.ColorTextWrapped("You can also find this setting in the Main UI below the \"Open Admin Panel\" in the Syncshell options.", ImGuiColors.DalamudYellow);

        if (_broadcastManager.IsListening)
        {
            if (_broadcastManager.BroadcastingGroupId == GroupFullInfo.GID)
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Stop, "Stop Broadcasting"))
                {
                    _broadcastManager.StopBroadcasting();
                }
            }
            else
            {
                using (ImRaii.Disabled((GroupFullInfo.PublicData.KnownPasswordless || GroupFullInfo.GroupPermissions.IsEnableGuestMode()) && !UiSharedService.CtrlPressed()))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Wifi, "Start Broadcasting"))
                    {
                        _broadcastManager.StartBroadcasting(GroupFullInfo.Group.GID);
                        var allPairsInGroup = _pairManager.GroupPairs[GroupFullInfo].Count;
                        if (allPairsInGroup == (_apiController.ServerInfo.MaxGroupUserCount - 1))
                        {
                            Mediator.Publish(new NotificationMessage("Group Full", $"Group {GroupFullInfo.Group.GID} is being broadcasted but is capped on players who can join.", MareConfiguration.Models.NotificationType.Error));
                        }
                    }
                }
                UiSharedService.AttachToolTip("Begin broadcasting your Syncshell to players around you.");
                if (GroupFullInfo.PublicData.KnownPasswordless)
                {
                    UiSharedService.AttachToolTip("This Syncshell has no password!\nHold CTRL and click if you are sure you want to broadcast this passwordless Syncshell.");
                }
                else if (GroupFullInfo.GroupPermissions.IsEnableGuestMode())
                {
                    UiSharedService.AttachToolTip("This Syncshell has Guest Mode enabled!\nHold CTRL and click if you are sure you want to broadcast this Syncshell with no password.");
                }
            }
        }

        ImGuiHelpers.ScaledDummy(5f);
        _uiSharedService.HeaderText("One-time Invites");
        UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
        {
            ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
        }
        UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("##amountofinvites", ref _multiInvites);
        ImGui.SameLine();
        using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
            {
                _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), _multiInvites).Result);
            }
        }

        if (_oneTimeInvites.Any())
        {
            var invites = string.Join(Environment.NewLine, _oneTimeInvites);
            ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
            {
                ImGui.SetClipboardText(invites);
            }
        }                
            
    }

    private void DrawUserManagement()
    {
        _uiSharedService.BigText("User Management");
        ImGuiHelpers.ScaledDummy(2);

        _selectedTab = UiNav.DrawTabsUnderline(_theme,
            [
                new(SyncshellAdminTabs.UserList, "User List & Administration", DrawUserList, FontAwesomeIcon.Users),
                new(SyncshellAdminTabs.Cleanup, "Mass Cleanup", DrawCleanup, FontAwesomeIcon.Broom),
                new(SyncshellAdminTabs.Bans, "User Bans", DrawBans, FontAwesomeIcon.Ban),
            ],
            _selectedTab,
            iconFont: _uiSharedService.IconFont);
        ImGuiHelpers.ScaledDummy(5f);

        _selectedTab.TabAction.Invoke();
    }

    private void DrawUserList()
    {
        if (!_pairManager.GroupPairs.TryGetValue(GroupFullInfo, out var pairs))
        {
            UiSharedService.ColorTextWrapped("No users found in this Syncshell", ImGuiColors.DalamudYellow);
        }
        else
        {
            using var table = ImRaii.Table("userList#" + GroupFullInfo.Group.GID, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
            if (table)
            {
                ImGui.TableSetupColumn("Alias/UID/Note", ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn("Online/Name", ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 2);
                ImGui.TableHeadersRow();

                var groupedPairs = new Dictionary<Pair, GroupPairUserInfo?>(pairs.Select(p => new KeyValuePair<Pair, GroupPairUserInfo?>(p,
                    GroupFullInfo.GroupPairUserInfos.TryGetValue(p.UserData.UID, out GroupPairUserInfo value) ? value : null)));

                foreach (var pair in groupedPairs.OrderBy(p =>
                {
                    if (p.Value == null) return 10;
                    if (p.Value.Value.IsModerator()) return 1;
                    if (p.Value.Value.IsPinned()) return 2;
                    if (p.Value.Value.IsGuest()) return 0;
                    return 10;
                }).ThenBy(p => p.Key.GetNote() ?? p.Key.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase))
                {
                    using var tableId = ImRaii.PushId("userTable_" + pair.Key.UserData.UID);

                    ImGui.TableNextColumn(); // alias/uid/note
                    var note = pair.Key.GetNote();
                    var text = note == null ? pair.Key.UserData.AliasOrUID : note + " (" + pair.Key.UserData.AliasOrUID + ")";
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(text);

                    ImGui.TableNextColumn(); // online/name
                    string onlineText = pair.Key.IsOnline ? "Online" : "Offline";
                    if (!string.IsNullOrEmpty(pair.Key.PlayerName))
                    {
                        onlineText += " (" + pair.Key.PlayerName + ")";
                    }
                    var boolcolor = UiSharedService.GetBoolColor(pair.Key.IsOnline);
                    ImGui.AlignTextToFramePadding();
                    UiSharedService.ColorText(onlineText, boolcolor);

                    ImGui.TableNextColumn(); // special flags
                    if (pair.Value != null && (pair.Value.Value.IsModerator() || pair.Value.Value.IsPinned() || pair.Value.Value.IsGuest()))
                    {
                        if (pair.Value.Value.IsModerator())
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.UserShield);
                            UiSharedService.AttachToolTip("Moderator");
                            if (pair.Value.Value.IsGuest()) ImGui.SameLine();
                        }
                        if (pair.Value.Value.IsPinned())
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
                            UiSharedService.AttachToolTip("Pinned");
                            if (pair.Value.Value.IsGuest()) ImGui.SameLine();
                        }
                        if (pair.Value.Value.IsGuest())
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.PersonWalkingLuggage);
                            UiSharedService.AttachToolTip("Guest");
                        }
                    }
                    else
                    {
                        _uiSharedService.IconText(FontAwesomeIcon.None);
                    }

                    ImGui.TableNextColumn(); // actions
                    if (_isOwner)
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.UserShield))
                        {
                            GroupPairUserInfo userInfo = pair.Value ?? GroupPairUserInfo.None;

                            userInfo.SetModerator(!userInfo.IsModerator());

                            _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                        }
                        UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsModerator() ? "Demod user" : "Mod user");
                        ImGui.SameLine();
                    }

                    if (_isOwner || (pair.Value == null || (pair.Value != null && !pair.Value.Value.IsModerator())))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Thumbtack))
                        {
                            GroupPairUserInfo userInfo = pair.Value ?? GroupPairUserInfo.None;

                            userInfo.SetPinned(!userInfo.IsPinned());

                            _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                        }
                        UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsPinned() ? "Unpin user" : "Pin user");
                        ImGui.SameLine();

                        bool isGuest = pair.Value?.IsGuest() ?? false;
                        using (ImRaii.Disabled(!isGuest))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.HouseMedicalCircleCheck))
                            {
                                GroupPairUserInfo userInfo = pair.Value ?? GroupPairUserInfo.None;
                                userInfo.SetGuest(true);
                                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                            }
                        }
                        UiSharedService.AttachToolTip("Remove Guest status from user");
                        ImGui.SameLine();

                        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                            {
                                _ = _apiController.GroupRemoveUser(new GroupPairDto(GroupFullInfo.Group, pair.Key.UserData));
                            }
                        }
                        UiSharedService.AttachToolTip("Remove user from Syncshell"
                            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

                        ImGui.SameLine();
                        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Ban))
                            {
                                Mediator.Publish(new OpenBanUserPopupMessage(pair.Key, GroupFullInfo));
                            }
                        }
                        UiSharedService.AttachToolTip("Ban user from Syncshell"
                            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                    }
                }
            }
        }
    }

    private void DrawCleanup()
    {
        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
            {
                _ = _apiController.GroupClear(new(GroupFullInfo.Group), false);
            }
        }
        UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell."
            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Guests Only"))
            {
                _ = _apiController.GroupClear(new(GroupFullInfo.Group), true);
            }
        }
        UiSharedService.AttachToolTip("This will remove all users who joined with no password (guests) from the Syncshell."
            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Unlink, "Check for Inactive Users"))
        {
            _pruneTestTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: false);
            _pruneTask = null;
        }
        UiSharedService.AttachToolTip($"This will start the prune process for this Syncshell of inactive PlayerSync users that have not logged in in the past {_pruneDays} days."
            + Environment.NewLine + "You will be able to review the amount of inactive users before executing the prune."
            + UiSharedService.TooltipSeparator + "Note: this check excludes pinned users and moderators of this Syncshell.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        _uiSharedService.DrawCombo("Days of inactivity", [1, 7, 14, 30, 90], (count) =>
        {
            return count + (count == 1 ? " day" : " days");
        },
        (selected) =>
        {
            _pruneDays = selected;
            _pruneTestTask = null;
            _pruneTask = null;
        },
        _pruneDays);

        if (_pruneTestTask != null)
        {
            if (!_pruneTestTask.IsCompleted)
            {
                UiSharedService.ColorTextWrapped("Calculating inactive users...", ImGuiColors.DalamudYellow);
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                UiSharedService.TextWrapped($"Found {_pruneTestTask.Result} user(s) that have not logged into PlayerSync in the past {_pruneDays} days.");
                if (_pruneTestTask.Result > 0)
                {
                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Prune Inactive Users"))
                        {
                            _pruneTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: true);
                            _pruneTestTask = null;
                        }
                    }
                    UiSharedService.AttachToolTip($"Pruning will remove {_pruneTestTask?.Result ?? 0} inactive user(s)."
                        + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                }
            }
        }
        if (_pruneTask != null)
        {
            if (!_pruneTask.IsCompleted)
            {
                UiSharedService.ColorTextWrapped("Pruning Syncshell...", ImGuiColors.DalamudYellow);
            }
            else
            {
                UiSharedService.TextWrapped($"Syncshell was pruned and {_pruneTask.Result} inactive user(s) have been removed.");
            }
        }
    }

    private void DrawBans()
    {
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
        {
            _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
        }

        if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

            ImGui.TableHeadersRow();

            foreach (var bannedUser in _bannedUsers.ToList())
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.UID);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.BannedBy);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                ImGui.TableNextColumn();
                UiSharedService.TextWrapped(bannedUser.Reason);
                ImGui.TableNextColumn();
                using var _ = ImRaii.PushId(bannedUser.UID);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban"))
                {
                    _apiController.GroupUnbanUser(bannedUser);
                    _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, System.StringComparison.Ordinal));
                }
            }

            ImGui.EndTable();
        }
    }
    private void DrawPermissions()
    {
        _uiSharedService.BigText("Permissions");
        ImGuiHelpers.ScaledDummy(2);

        var perm = GroupFullInfo.GroupPermissions;
        bool isDisableAnimations = perm.IsPreferDisableAnimations();
        bool isDisableSounds = perm.IsPreferDisableSounds();
        bool isDisableVfx = perm.IsPreferDisableVFX();

        float PositionalX2 = 200f * ImGui.GetIO().FontGlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Suggest Sound Sync");
        _uiSharedService.BooleanToColoredIcon(!isDisableSounds);
        ImGui.SameLine(PositionalX2);
        if (_uiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
            isDisableSounds ? "Suggest to enable sound sync" : "Suggest to disable sound sync"))
        {
            perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
            _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Suggest Animation Sync");
        _uiSharedService.BooleanToColoredIcon(!isDisableAnimations);
        ImGui.SameLine(PositionalX2);
        if (_uiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
            isDisableAnimations ? "Suggest to enable animation sync" : "Suggest to disable animation sync"))
        {
            perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
            _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Suggest VFX Sync");
        _uiSharedService.BooleanToColoredIcon(!isDisableVfx);
        ImGui.SameLine(PositionalX2);
        if (_uiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
            isDisableVfx ? "Suggest to enable vfx sync" : "Suggest to disable vfx sync"))
        {
            perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
            _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
        }

        UiSharedService.TextWrapped("Note: those suggested permissions will be shown to users on joining the Syncshell.");
            
    }

    private void DrawOwnerSettings()
    {
        _uiSharedService.BigText("Owner Settings");
        ImGuiHelpers.ScaledDummy(2);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("New Password");
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password");
        var textSize = ImGui.CalcTextSize("New Password").X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
        ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
        ImGui.SameLine();
        using (ImRaii.Disabled((_newPassword.Length > 0 && _newPassword.Length < 10) || (_newPassword == string.Empty && !UiSharedService.CtrlPressed())))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
            {
                _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                _newPassword = string.Empty;
            }
        }
        var tooltip = "Password requires to be at least 10 characters long. This action is irreversible.";
        if (_newPassword == string.Empty)
        {
            tooltip += Environment.NewLine + Environment.NewLine + "WARNING: A Syncshell without a password can be joined by anyone with the Syncshell ID\nor that it is broadcast to. Hold CTRL if you are sure you want to not have a password\non this Syncshell.";
        }
        UiSharedService.AttachToolTip(tooltip);

        if (!_pwChangeSuccess)
        {
            UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
        }



        ImGuiHelpers.ScaledDummy(2f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
        {
            IsOpen = false;
            _ = _apiController.GroupDelete(new(GroupFullInfo.Group));
        }
        UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                
    }
public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}