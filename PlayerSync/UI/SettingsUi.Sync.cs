using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<SyncTabs>? _selectedTabSync;

    private enum SyncTabs
    {
        Zone,
        Broadcast,
        Filter,
        Permissions
    }

    private void DrawSyncSettings()
    {
        _lastTab = "zone";

        _selectedTabSync = UiNav.DrawTabsUnderline(_theme,
            [
                new(SyncTabs.Zone, "ZoneSync", DrawSyncZone),
                new(SyncTabs.Broadcast, "Broadcasts", DrawSyncBroadcast),
                new(SyncTabs.Filter, "Filtering", DrawSyncFilter),
                new(SyncTabs.Permissions, "Permissions", GoToPermissions),
            ],
            _selectedTabSync, 
            _uiShared.IconFont);
            
        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabSync.TabAction.Invoke();
    }

    private void DrawSyncZone()
    {
        _uiShared.BigText("ZoneSync");
        ImGuiHelpers.ScaledDummy(2);
        if (!_zoneSyncConfigService.Current.UserHasConfirmedWarning)
        {
            ImGui.Separator();
            _uiShared.BigText("READ THIS! YOU ARE RESPONSIBLE FOR YOUR ACTIONS!", ImGuiColors.DalamudRed);
            UiSharedService.ColorTextWrapped("This feature enables joining server controlled syncshells AUTOMATICALLY.", ImGuiColors.DalamudRed);
            UiSharedService.ColorTextWrapped("You should NOT enable this feature if you are unwilling to self-moderate and pause others.", ImGuiColors.DalamudRed);
            UiSharedService.ColorTextWrapped("Using this feature to break PlayerSync ToS will result in a ban from PlayerSync.", ImGuiColors.DalamudRed);
            ImGui.Dummy(new Vector2(10));

            // Use the windowing system to store the values
            var storage = ImGui.GetStateStorage();
            var timerId = ImGui.GetID("WarningTimer##PairingSettings");
            var tickTime = storage.GetInt(timerId);
            var countId = ImGui.GetID("WarningCounter##PairingSettings");
            var endCount = storage.GetInt(countId);

            var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - tickTime > 1 && endCount < 11)
            {
                storage.SetInt(timerId, now);
                endCount++;
                storage.SetInt(countId, endCount);
            }

            if (endCount < 11)
            {
                ImGui.BeginDisabled();
                ImGui.Button($"I Understand and Confirm ({11 - endCount})");
                ImGui.EndDisabled();
            }
            else
            {
                using (ImRaii.Disabled(!UiSharedService.ShiftPressed()))
                {
                    if (ImGui.Button("I Understand and Confirm"))
                    {
                        _zoneSyncConfigService.Current.UserHasConfirmedWarning = true;
                        _zoneSyncConfigService.Save();
                    }
                    UiSharedService.AttachToolTip("Hold SHIFT and click to confirm.");
                }
            }
            ImGui.Dummy(new Vector2(10));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(10));
        }

        UiSharedService.ColorTextWrapped("Read these rules before proceeding:", ImGuiColors.DalamudRed);
        UiSharedService.TextWrapped("1) You are responsible for your conduct and should self-moderate your appearance and actions.");
        UiSharedService.TextWrapped("2) Pause unwanted user pairs as needed.");
        UiSharedService.TextWrapped("3) No nuisance behavior (crashing people, taking up entire screen, etc.)");
        UiSharedService.TextWrapped("4) All player actions are subject to the PlayerSync Terms of Service.");
        ImGuiHelpers.ScaledDummy(10f);

        bool warningConfirmed = !_zoneSyncConfigService.Current.UserHasConfirmedWarning;
        ImGui.BeginDisabled(warningConfirmed);

        bool enableGroupZoneSyncJoining = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
        using (ImRaii.Disabled(_globalControlCountdown > 0 && !enableGroupZoneSyncJoining))
        {
            if (ImGui.Checkbox("Enable automatic joining of zone-based syncshells.", ref enableGroupZoneSyncJoining))
            {
                if (!enableGroupZoneSyncJoining)
                {
                    _ = GlobalControlCountdown(5);
                }
                Mediator.Publish(new GroupZoneSetEnableState(enableGroupZoneSyncJoining));
                _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = enableGroupZoneSyncJoining;
                _zoneSyncConfigService.Save();
            }
            if (_globalControlCountdown != 0 && !enableGroupZoneSyncJoining)
            {
                UiSharedService.AttachToolTip("You can enable ZoneSync again in " + _globalControlCountdown + " seconds.");
            }
        }

        ImGuiHelpers.ScaledDummy(5f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "This does not work for instanced areas.");
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(_globalControlCountdown > 0 && enableGroupZoneSyncJoining))
        {
            _uiShared.DrawCombo("###zonefilter", [ZoneSyncFilter.All, ZoneSyncFilter.ResidentialOnly, ZoneSyncFilter.TownOnly, ZoneSyncFilter.ResidentialTown],
            (s) => s switch
            {
                ZoneSyncFilter.All => "All",
                ZoneSyncFilter.ResidentialOnly => "Residential Only",
                ZoneSyncFilter.TownOnly => "Town Only",
                ZoneSyncFilter.ResidentialTown => "Residential + Town",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                _zoneSyncConfigService.Current.ZoneSyncFilter = s;
                _zoneSyncConfigService.Save();
                if (enableGroupZoneSyncJoining)
                {
                    _ = GlobalControlCountdown(5);
                }
                Mediator.Publish(new GroupZoneSyncUpdateMessage());
            }, _zoneSyncConfigService.Current.ZoneSyncFilter);
            if (_globalControlCountdown != 0 && enableGroupZoneSyncJoining)
            {
                UiSharedService.AttachToolTip("Wait a moment before changing ");
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("ZoneSync Allowed Areas");
        ImGuiHelpers.ScaledDummy(5f);

        ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "Setting this too low may not give your PC enough time to unload/load other players.");
        var zoneSyncJoinDelay = _zoneSyncConfigService.Current.ZoneJoinDelayTime;
        ImGui.SetNextItemWidth(400);
        if (ImGui.SliderInt("ZoneSync Join Delay", ref zoneSyncJoinDelay, 5, 30))
        {
            _zoneSyncConfigService.Current.ZoneJoinDelayTime = zoneSyncJoinDelay;
            _zoneSyncConfigService.Save();
        }
        _uiShared.DrawHelpText("Set the wait time in seconds between entering a zone and joining a ZoneSync. Increase this if you have pairing issues after zoning.");

        ImGuiHelpers.ScaledDummy(5f);
        UiSharedService.TextWrapped("ZoneSync Synchshell permissions are based on your Default Permission Settings.");
        UiSharedService.TextWrapped("Permissions can be found under Settings > Service Settings > Permission Settings.");

        ImGui.EndDisabled();
    }

    private void DrawSyncBroadcast()
    {
        _uiShared.BigText("Broadcasts");
        ImGuiHelpers.ScaledDummy(2);
        UiSharedService.TextWrapped("Viewing Broadcast Syncshells is on by default. You can find active broadcasts in the Nearby Broadcast section of the Main UI.");
        UiSharedService.TextWrapped("To enable broadcasting of a Syncshell, use the Syncshell menu next to the Syncshell name in the main UI list of Syncshells.");
        ImGuiHelpers.ScaledDummy(5f);
        var showBroadcastingSyncshells = _configService.Current.ListenForBroadcasts;
        if (ImGui.Checkbox("Show Available Broadcasts", ref showBroadcastingSyncshells))
        {
            if (showBroadcastingSyncshells) _broadcastManager.StartListening();
            else _broadcastManager.StopListening();

            _configService.Current.ListenForBroadcasts = showBroadcastingSyncshells;
            _configService.Save();
        }
        UiSharedService.AttachToolTip(
            showBroadcastingSyncshells
                ? "Click to turn OFF broadcast features." + UiSharedService.TooltipSeparator + "Stops showing nearby Syncshell broadcasts."
                : "Click to turn ON broadcast features." + UiSharedService.TooltipSeparator + "Shows Syncshells broadcasting in your location for easy joining."
        );
        //ImGui.Dummy(new Vector2(10));
        //ImGui.Separator();
    }
    private void DrawSyncFilter()
    {
        bool filterSounds = _configService.Current.FilterSounds;
        bool filterVfx = _configService.Current.FilterVfx;
        bool filterAnimations = _configService.Current.FilterAnimations;

        _uiShared.BigText("Filtering");
        ImGuiHelpers.ScaledDummy(2);
        UiSharedService.TextWrapped("These options do NOT change your per-pair permissions. Think of these as global overrides you can toggle on/off.");
        UiSharedService.TextWrapped("You will not see the filtered sfx/ani/vfx for other players, but they will still be able to see you (if permissions allow).");
        UiSharedService.ColorTextWrapped("Changing these options will redraw all visible pairs around you.", ImGuiColors.DalamudRed);
        ImGuiHelpers.ScaledDummy(5f);
        if (ImGui.Checkbox("Filter out modded sounds", ref filterSounds))
        {
            _configService.Current.FilterSounds = filterSounds;
            _configService.Save();
            Mediator.Publish(new ChangeFilterMessage());
        }
        _uiShared.DrawHelpText("This setting will prevent modded sounds from being heard.");
        if (ImGui.Checkbox("Filter out modded vfx", ref filterVfx))
        {
            _configService.Current.FilterVfx = filterVfx;
            _configService.Save();
            Mediator.Publish(new ChangeFilterMessage());
        }
        _uiShared.DrawHelpText("This setting will prevent modded vfx from being displayed.");
        if (ImGui.Checkbox("Filter out modded animations", ref filterAnimations))
        {
            _configService.Current.FilterAnimations = filterAnimations;
            _configService.Save();
            Mediator.Publish(new ChangeFilterMessage());
        }
        _uiShared.DrawHelpText("This setting will prevent modded animations from being displayed.");
    }

    private void GoToPermissions()
    {
        _selectedTabSync = null;
        _selectedNavItem = new(SettingsNav.Service, "Service Settings", DrawServiceSettings, FontAwesomeIcon.Server);
        DrawService();
        _selectedTabService = new(ServiceTabs.Permissions, "Permissions", DrawServicePermissions);
    }
}
