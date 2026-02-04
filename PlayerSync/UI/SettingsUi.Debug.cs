using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.Json;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<DebugTabs>? _selectedTabDebug;

    private IReadOnlyList<UiNav.Tab<DebugTabs>>? _debugTabs;
    private IReadOnlyList<UiNav.Tab<DebugTabs>> DebugTabsList => _debugTabs ??=
    [
        new(DebugTabs.Debug, "Debug", DrawDebug),
        new(DebugTabs.Data, "Data", DrawDebugData),
    ];

    private enum DebugTabs
    {
        Debug,
        Data
    }

    private void DrawDebugSettings()
    {
        _selectedTabDebug = UiNav.DrawTabsUnderline(_theme, DebugTabsList, _selectedTabDebug, _uiShared.IconFont);

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabDebug.TabAction.Invoke();
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        _uiShared.BigText("Debug");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.SetNextItemWidth(300);
        _uiShared.DrawCombo("Log Level", Enum.GetValues<LogLevel>(), (l) => l.ToString(), (l) =>
        {
            _configService.Current.LogLevel = l;
            _configService.Save();
        }, _configService.Current.LogLevel);

        ImGuiHelpers.ScaledDummy(5);
        bool logPerformance = _configService.Current.LogPerformance;
        if (ImGui.Checkbox("Log Performance Counters", ref logPerformance))
        {
            _configService.Current.LogPerformance = logPerformance;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");

        using (ImRaii.Disabled(!logPerformance))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats to /xllog"))
            {
                _performanceCollector.PrintPerformanceStats();
            }
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats (last 60s) to /xllog"))
            {
                _performanceCollector.PrintPerformanceStats(60);
            }
        }

        ImGuiHelpers.ScaledDummy(5);
        bool stopWhining = _configService.Current.DebugStopWhining;
        if (ImGui.Checkbox("Do not notify for modified game files or enabled LOD", ref stopWhining))
        {
            _configService.Current.DebugStopWhining = stopWhining;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Having modified game files will still mark your logs with UNSUPPORTED and you will not receive support, message shown or not." + UiSharedService.TooltipSeparator
            + "Keeping LOD enabled can lead to more crashes. Use at your own risk.");

        ImGuiHelpers.ScaledDummy(5);
        bool throttleUploads = _configService.Current.DebugThrottleUploads;
        if (ImGui.Checkbox("Throttle uploads to be very very slow", ref throttleUploads))
        {
            _configService.Current.DebugThrottleUploads = throttleUploads;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Artificially slow down your uploads, for testing the upload system.");

        ImGuiHelpers.ScaledDummy(5);
        bool overrideCdnTimeOffset = _configService.Current.OverrideCdnTimeZone;
        if (ImGui.Checkbox($"Override CDN Time Zone (Current: '{(overrideCdnTimeOffset ? _configService.Current.OverrideCdnTimeZoneId : TimeZoneInfo.Local.Id)}', UTC offset: {_fileTransferOrchestrator.TimeZoneUtcOffsetMinutes} mins)", ref overrideCdnTimeOffset))
        {
            _configService.Current.OverrideCdnTimeZone = overrideCdnTimeOffset;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Overriding the time zone used to select a file transfer CDN can cause you to download and upload mod files via different servers." + UiSharedService.TooltipSeparator
            + "Only override if you are testing the CDN or if the automatic selection based on your PC's selected time zone does not result in connecting to the optimal server.\n\n"
            + "NOTE: Changing your system time zone may not reflect in Player Sync until you restart your game.");

        using (ImRaii.Disabled(!overrideCdnTimeOffset))
        {
            _uiShared.DrawCombo("Override Time Zone", TimeZoneInfo.GetSystemTimeZones().Append(null), zone => zone != null ? $"{zone?.Id}: {zone?.DisplayName}" : "(none): +0:00", zone =>
            {
                _configService.Current.OverrideCdnTimeZoneId = zone?.Id ?? string.Empty;
                _configService.Save();
            }, !string.IsNullOrEmpty(_configService.Current.OverrideCdnTimeZoneId) ? TimeZoneInfo.FindSystemTimeZoneById(_configService.Current.OverrideCdnTimeZoneId) : null);
        }
    }

    private void DrawDebugData()
    {
        _lastTab = "Debug";

        _uiShared.BigText("Data");
        ImGuiHelpers.ScaledDummy(2);

#if DEBUG
        if (LastCreatedCharacterData != null && ImGui.TreeNode("Last created character data"))
        {
            foreach (var l in JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }).Split('\n'))
            {
                ImGui.TextUnformatted($"{l}");
            }

            ImGui.TreePop();
        }
#endif
        if (_uiShared.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: No created character data, cannot copy.");
            }
        }
        UiSharedService.AttachToolTip("Use this when reporting mods being rejected from the server.");
    }
}
