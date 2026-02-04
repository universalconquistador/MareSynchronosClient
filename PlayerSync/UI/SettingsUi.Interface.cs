using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;

    private UiNav.Tab<InterfaceTabs>? _selectedTabInterface;

    private IReadOnlyList<UiNav.Tab<InterfaceTabs>>? _interfaceTabs;
    private IReadOnlyList<UiNav.Tab<InterfaceTabs>> InterfaceTabsList => _interfaceTabs ??=
    [
        new(InterfaceTabs.Ui, "PlayerSync UI", DrawInterfacePlayerSyncUi),
        new(InterfaceTabs.Game, "Game UI", DrawInterfaceGameUi),
        new(InterfaceTabs.Notes, "Notes", DrawInterfaceNotes),
        new(InterfaceTabs.Notifications, "Notifications", DrawInterfaceNotifications),
    ];

    private enum InterfaceTabs
    {
        Ui,
        Game,
        Notes,
        Notifications
    }

    private void DrawInterfaceSettings()
    {
        _selectedTabInterface = UiNav.DrawTabsUnderline(_theme, InterfaceTabsList, _selectedTabInterface, _uiShared.IconFont);

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabInterface.TabAction.Invoke();
    }

    private void DrawInterfacePlayerSyncUi()
    {
        var showNameInsteadOfNotes = _configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
        var showVisibleSeparate = _configService.Current.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _configService.Current.ShowOfflineUsersSeparately;
        var showProfiles = _configService.Current.ProfilesShow;
        var showNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
        var profileDelay = _configService.Current.ProfileDelay;
        var profileOnRight = _configService.Current.ProfilePopoutRight;
        var preferNotesInsteadOfName = _configService.Current.PreferNotesOverNamesForVisible;
        var useFocusTarget = _configService.Current.UseFocusTarget;
        var groupUpSyncshells = _configService.Current.GroupUpSyncshells;
        var groupInVisible = _configService.Current.ShowSyncshellUsersInVisible;
        var syncshellOfflineSeparate = _configService.Current.ShowSyncshellOfflineUsersSeparately;
        var showWindowOnPluginLoad = _configService.Current.ShowUIOnPluginLoad;
        var showAnalysisOnUi = _configService.Current.ShowAnalysisOnCompactUi;
        var showAnalysisBottom = _configService.Current.ShowAnalysisCompactUiBottom;
        var showAnalysisColor = _configService.Current.ShowAnalysisCompactUiColor;
        var showCompactStats = _configService.Current.ShowCompactStats;
        var mysterySetting = _configService.Current.MysterySetting;

        _uiShared.BigText("PlayerSync UI");
        ImGuiHelpers.ScaledDummy(2);
        if (ImGui.Checkbox("Show the plugin UI automatically", ref showWindowOnPluginLoad))
        {
            _configService.Current.ShowUIOnPluginLoad = showWindowOnPluginLoad;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This opens the UI automatically whenever the plugin is loaded/reloaded.");
        if (ImGui.Checkbox("Show VRAM/triangle usage on main UI", ref showAnalysisOnUi))
        {
            _configService.Current.ShowAnalysisOnCompactUi = showAnalysisOnUi;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This shows your current VRAM usage and triangle count on the main UI.");

        using (ImRaii.PushIndent(2))
        {
            if (!showAnalysisOnUi) ImGui.BeginDisabled();
            if (ImGui.Checkbox("Show VRAM/triangle usage color coded", ref showAnalysisColor))
            {
                _configService.Current.ShowAnalysisCompactUiColor = showAnalysisColor;
                _configService.Save();
            }
            _uiShared.DrawHelpText("This will turn the values yellow if you exceed your configured threshold.");
            if (ImGui.Checkbox("Show usage on a single line", ref showCompactStats))
            {
                _configService.Current.ShowCompactStats = showCompactStats;
                _configService.Save();
            }
            _uiShared.DrawHelpText("Show player VRAM and triangle usage on a single line.");
            if (ImGui.Checkbox("Display at bottom of the UI window", ref showAnalysisBottom))
            {
                _configService.Current.ShowAnalysisCompactUiBottom = showAnalysisBottom;
                _configService.Save();
            }
            if (!showAnalysisOnUi) ImGui.EndDisabled();
        }

        if (ImGui.Checkbox("Show separate Visible group", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will show all currently visible users in a special 'Visible' group in the main UI.");

        using (ImRaii.Disabled(!showVisibleSeparate))
        {
            using var indent = ImRaii.PushIndent(2);
            if (ImGui.Checkbox("Show Syncshell Users in Visible Group", ref groupInVisible))
            {
                _configService.Current.ShowSyncshellUsersInVisible = groupInVisible;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("Show separate Offline group", ref showOfflineSeparate))
        {
            _configService.Current.ShowOfflineUsersSeparately = showOfflineSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will show all currently offline users in a special 'Offline' group in the main UI.");

        using (ImRaii.Disabled(!showOfflineSeparate))
        {
            using var indent = ImRaii.PushIndent(2);
            if (ImGui.Checkbox("Show separate Offline group for Syncshell users", ref syncshellOfflineSeparate))
            {
                _configService.Current.ShowSyncshellOfflineUsersSeparately = syncshellOfflineSeparate;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("Group up all syncshells in one folder", ref groupUpSyncshells))
        {
            _configService.Current.GroupUpSyncshells = groupUpSyncshells;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will group up all Syncshells in a special 'All Syncshells' folder in the main UI.");

        if (ImGui.Checkbox("Show player name for visible players", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will show the character name instead of custom set note when a character is visible");

        using (ImRaii.PushIndent(2))
        {
            if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.BeginDisabled();
            if (ImGui.Checkbox("Prefer notes over player names for visible players", ref preferNotesInsteadOfName))
            {
                _configService.Current.PreferNotesOverNamesForVisible = preferNotesInsteadOfName;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
            _uiShared.DrawHelpText("If you set a note for a player it will be shown instead of the player name");
            if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.EndDisabled();
        }

        if (ImGui.Checkbox("Set visible pairs as focus targets when clicking the eye", ref useFocusTarget))
        {
            _configService.Current.UseFocusTarget = useFocusTarget;
            _configService.Save();
        }

        if (ImGui.Checkbox("Show PlayerSync Profiles on Hover", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will show the configured user profile after a set delay");
        using (ImRaii.PushIndent(2))
        {
            if (!showProfiles) ImGui.BeginDisabled();
            if (ImGui.Checkbox("Popout profiles on the right", ref profileOnRight))
            {
                _configService.Current.ProfilePopoutRight = profileOnRight;
                _configService.Save();
                Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
            }
            _uiShared.DrawHelpText("Will show profiles on the right side of the main UI");
            ImGui.SetNextItemWidth(400);
            if (ImGui.SliderFloat("Hover Delay", ref profileDelay, 1, 10))
            {
                _configService.Current.ProfileDelay = profileDelay;
                _configService.Save();
            }
            _uiShared.DrawHelpText("Delay until the profile should be displayed");
            if (!showProfiles) ImGui.EndDisabled();
        }
        
        if (ImGui.Checkbox("Show profiles marked as NSFW", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Will show profiles that have the NSFW tag enabled");
        if (ImGui.Checkbox("Mystery Setting", ref mysterySetting))
        {
            _configService.Current.MysterySetting = mysterySetting;
            _configService.Save();
        }
        _uiShared.DrawHelpText("???");
    }

    private void DrawInterfaceGameUi()
    {
        var enableRightClickMenu = _configService.Current.EnableRightClickMenus;
        var showPairedIndicator = _configService.Current.ShowPairedIndicator;
        var showSoundIndicator = _configService.Current.ShowSoundSourceIndicator;
        var showPermsOverFC = _configService.Current.ShowPermsInsteadOfFCTags;
        var enableDtrEntry = _configService.Current.EnableDtrEntry;
        var showUidInDtrTooltip = _configService.Current.ShowUidInDtrTooltip;
        var preferNoteInDtrTooltip = _configService.Current.PreferNoteInDtrTooltip;
        var useColorsInDtr = _configService.Current.UseColorsInDtr;
        var dtrColorsDefault = _configService.Current.DtrColorsDefault;
        var dtrColorsNotConnected = _configService.Current.DtrColorsNotConnected;
        var dtrColorsPairsInRange = _configService.Current.DtrColorsPairsInRange;
        var dtrColorsBroadcasting = _configService.Current.DtrColorsBroadcasting;
        var showNameHighlights = _configService.Current.ShowNameHighlights;
        var showFriendsHighlights = _configService.Current.IncludeFriendHighlights;
        var highlightNameColor = _configService.Current.NameHighlightColor;
        var permColorsEnabled = _configService.Current.PermsColorsEnabled;
        var permsColorsDisabled = _configService.Current.PermsColorsDisabled;

        _uiShared.BigText("Game UI");
        ImGuiHelpers.ScaledDummy(2);
        if (ImGui.Checkbox("Enable Game Right Click Menu Entries", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will add PlayerSync related right click menu entries in the game UI on paired players.");

        if (ImGui.Checkbox("Show Paired Indicator", ref showPairedIndicator))
        {
            _configService.Current.ShowPairedIndicator = showPairedIndicator;
            _configService.Save();
            Mediator.Publish(new RedrawNameplateMessage());
        }
        _uiShared.DrawHelpText("This will draw a ⇔ icon next to names for visibile pairs.");

        if (ImGui.Checkbox("Color Code Active Pair Names" , ref showNameHighlights))
        {
            _configService.Current.ShowNameHighlights = showNameHighlights;
            _configService.Save();
            Mediator.Publish(new RedrawNameplateMessage());
        }
        _uiShared.DrawHelpText("This will change the name color for active pairs you can see." + Environment.NewLine +
            "Turning this off may take a moment to reflect in game.");

        using (ImRaii.Disabled(!showNameHighlights))
        {
            using var indent = ImRaii.PushIndent(2);
            if (InputColorPicker("Name Color", ref highlightNameColor))
            {
                _configService.Current.NameHighlightColor = highlightNameColor;
                _configService.Save();
                Mediator.Publish(new RedrawNameplateMessage());
            }

            if (ImGui.Checkbox("Include Friend List Names", ref showFriendsHighlights))
            {
                _configService.Current.IncludeFriendHighlights = showFriendsHighlights;
                _configService.Save();
                Mediator.Publish(new RedrawNameplateMessage());
            }
            _uiShared.DrawHelpText("This will also change the color of players on your Friend List.");

        }

        if (ImGui.Checkbox("Replace FC tags with PlayerSync permissions", ref showPermsOverFC))
        {
            _configService.Current.ShowPermsInsteadOfFCTags = showPermsOverFC;
            _configService.Save();
            Mediator.Publish(new RedrawNameplateMessage());
        }
        _uiShared.DrawHelpText("This will replace FC tags with your visible pairs permissions, color coded based on permission status.");

        using (ImRaii.Disabled(!showPermsOverFC))
        {
            using var indent = ImRaii.PushIndent(2);
            if (InputColorPicker("Enabled Color", ref permColorsEnabled))
            {
                _configService.Current.PermsColorsEnabled = permColorsEnabled;
                _configService.Save();
                Mediator.Publish(new RedrawNameplateMessage());
            }
            ImGui.SameLine();
            if (InputColorPicker("Disabled Color", ref permsColorsDisabled))
            {
                _configService.Current.PermsColorsDisabled = permsColorsDisabled;
                _configService.Save();
                Mediator.Publish(new RedrawNameplateMessage());
            }
        }

        if (ImGui.Checkbox("Display status and visible pair count in Server Info Bar", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will add PlayerSync connection status and visible pair count in the Server Info Bar.\nYou can further configure this through your Dalamud Settings.");

        using (ImRaii.Disabled(!enableDtrEntry))
        {
            using var indent = ImRaii.PushIndent(2);
            if (ImGui.Checkbox("Show visible character's UID in tooltip", ref showUidInDtrTooltip))
            {
                _configService.Current.ShowUidInDtrTooltip = showUidInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("Prefer notes over player names in tooltip", ref preferNoteInDtrTooltip))
            {
                _configService.Current.PreferNoteInDtrTooltip = preferNoteInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("Color-code the Server Info Bar entry according to status", ref useColorsInDtr))
            {
                _configService.Current.UseColorsInDtr = useColorsInDtr;
                _configService.Save();
            }

            using (ImRaii.Disabled(!useColorsInDtr))
            {
                using var indent2 = ImRaii.PushIndent(2);
                if (InputColorPicker("Default", ref dtrColorsDefault, true))
                {
                    _configService.Current.DtrColorsDefault = dtrColorsDefault;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputColorPicker("Not Connected", ref dtrColorsNotConnected, true))
                {
                    _configService.Current.DtrColorsNotConnected = dtrColorsNotConnected;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputColorPicker("Pairs in Range", ref dtrColorsPairsInRange, true))
                {
                    _configService.Current.DtrColorsPairsInRange = dtrColorsPairsInRange;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputColorPicker("Broadcasting", ref dtrColorsBroadcasting, true))
                {
                    _configService.Current.DtrColorsBroadcasting = dtrColorsBroadcasting;
                    _configService.Save();
                }
            }
        }
    }

    private void DrawInterfaceNotes()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        
        _uiShared.BigText("Notes");
        ImGuiHelpers.ScaledDummy(2);
        if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Export all your user notes to clipboard"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (_uiShared.IconTextButton(FontAwesomeIcon.FileImport, "Import notes from clipboard"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Overwrite existing notes", ref _overwriteExistingLabels);
        _uiShared.DrawHelpText("If this option is selected all already existing notes for UIDs will be overwritten by the imported notes.");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("User Notes successfully imported", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("Attempt to import notes from clipboard failed. Check formatting and try again", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("Open Notes Popup on user addition", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        var autoPopulateNotes = _configService.Current.AutoPopulateEmptyNotesFromCharaName;
        if (ImGui.Checkbox("Automatically populate notes using player names", ref autoPopulateNotes))
        {
            _configService.Current.AutoPopulateEmptyNotesFromCharaName = autoPopulateNotes;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will automatically populate user notes using the first encountered player name if the note was not set prior");
    }

    private void DrawInterfaceNotifications()
    {
        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var syncConflictNotifs = _configService.Current.ShowSyncConflictNotifications;
        var pairingRequestNotifs = _configService.Current.ShowPairingRequestNotification;
        var broadcastNotifs = _configService.Current.ShowAvailableBroadcastsNotification;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;

        _uiShared.BigText("Notifications");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.SetNextItemWidth(400);
        _uiShared.DrawCombo("Info Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        _uiShared.DrawHelpText("The location where \"Info\" notifications will display."
                      + Environment.NewLine + "'Nowhere' will not show any Info notifications"
                      + Environment.NewLine + "'Chat' will print Info notifications in chat"
                      + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                      + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        ImGui.SetNextItemWidth(400);
        _uiShared.DrawCombo("Warning Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.WarningNotification = i;
            _configService.Save();
        }, _configService.Current.WarningNotification);
        _uiShared.DrawHelpText("The location where \"Warning\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Warning notifications"
                              + Environment.NewLine + "'Chat' will print Warning notifications in chat"
                              + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        ImGui.SetNextItemWidth(400);
        _uiShared.DrawCombo("Error Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.ErrorNotification = i;
            _configService.Save();
        }, _configService.Current.ErrorNotification);
        _uiShared.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        ImGuiHelpers.ScaledDummy(5);
        if (ImGui.Checkbox("Disable optional plugin warnings", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will not show any \"Warning\" labeled messages for missing optional plugins.");
        if (ImGui.Checkbox("Enable sync conflict notifications", ref syncConflictNotifs))
        {
            _configService.Current.ShowSyncConflictNotifications = syncConflictNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will show chat notifications when loading PlayerSync with a potentially conflicting plugin.");
        if (ImGui.Checkbox("Enable pairing request notifications", ref pairingRequestNotifs))
        {
            _configService.Current.ShowPairingRequestNotification = pairingRequestNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when a player requests to pair through the context menu.");
        if (ImGui.Checkbox("Enable broadcast notifications", ref broadcastNotifs))
        {
            _configService.Current.ShowAvailableBroadcastsNotification = broadcastNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner the first time a broadcast is available in your zone.");
        if (ImGui.Checkbox("Enable online notifications", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when pairs go online.");

        using var ident = ImRaii.PushIndent(2);
        using var disabled = ImRaii.Disabled(!onlineNotifs);
        if (ImGui.Checkbox("Notify only for individual pairs", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for individual pairs.");
        if (ImGui.Checkbox("Notify only for named pairs", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for pairs where you have set an individual note.");
    }
}
