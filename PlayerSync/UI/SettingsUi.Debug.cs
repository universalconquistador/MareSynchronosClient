using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using PlayerSync.WebAPI.SignalR;
using System.Numerics;
using System.Text.Json;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private readonly List<string> _overrideGateways = new();
    private bool _isLoadingGateways;
    private bool _gatewayLoadRequested;
    private string? _selectedGateway;

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
        bool disableSoundIndicators = _configService.Current.DebugDisableSoundIndicators;
        if (ImGui.Checkbox("Disable sound indicators", ref disableSoundIndicators))
        {
            _configService.Current.DebugDisableSoundIndicators = disableSoundIndicators;
            _configService.Save();
        }
        _uiShared.DrawHelpText("You must reconnect to the service for this to take effect for existing pairs.");

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

            bool ignoreCdnOverrideWarning = _configService.Current.IgnoreWarningOverrideCdnTimeZone;
            if (ImGui.Checkbox("Ignore CDN Override Warning", ref ignoreCdnOverrideWarning))
            {
                _configService.Current.IgnoreWarningOverrideCdnTimeZone = ignoreCdnOverrideWarning;
                _configService.Save();
            }
            _uiShared.DrawHelpText("Only ignore the warnings if you know for sure you must use an override to access the PlayerSync file services." + UiSharedService.TooltipSeparator
                + "Leaving the override enabled may result in suboptimal load times. Keep the warning enabled as a reminder if you are using the override temporarily.");
        }

        ImGuiHelpers.ScaledDummy(5);
        bool overrideGatewaySelection = _configService.Current.OverrideGatewaySelection;
        if (ImGui.Checkbox("Override Gateway Selection", ref overrideGatewaySelection))
        {
            _configService.Current.OverrideGatewaySelection = overrideGatewaySelection;
            _configService.Save();

            if (overrideGatewaySelection && !_gatewayLoadRequested)
            {
                _gatewayLoadRequested = true;
                _ = LoadGatewaysAsync();
            }
        }
        _uiShared.DrawHelpText("Pick a gateway to override the discovery process.");

        LoadGateways();
        _selectedGateway ??= _serverConfigurationManager.ManualGatewaySelection;

        using (ImRaii.Disabled(!overrideGatewaySelection))
        {
            if (_isLoadingGateways)
            {
                ImGui.TextUnformatted("Loading gateways...");
            }
            else
            {
                if (ImGui.BeginCombo("Gateway Override", _selectedGateway ?? "Select Gateway"))
                {
                    foreach (string gateway in _overrideGateways)
                    {
                        bool selected = gateway == _selectedGateway;

                        if (ImGui.Selectable(gateway, selected))
                        {
                            _selectedGateway = gateway;

                            _serverConfigurationManager.ManualGatewaySelection = gateway;
                        }

                        if (selected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }

                    ImGui.EndCombo();
                }
            }
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

    private void LoadGateways()
    {
        if (!_configService.Current.OverrideGatewaySelection)
            return;

        if (_gatewayLoadRequested || _isLoadingGateways || _overrideGateways.Count > 0)
            return;

        _gatewayLoadRequested = true;
        _ = LoadGatewaysAsync();
    }

    private async Task LoadGatewaysAsync()
    {
        if (_isLoadingGateways)
            return;

        _isLoadingGateways = true;

        try
        {
            Uri serviceUri = new Uri(_serverConfigurationManager.RealApiUrl);

            List<string> gateways = await GatewayManager.GetListOfServiceGateways(serviceUri).ConfigureAwait(false);

            lock (_overrideGateways)
            {
                _overrideGateways.Clear();
                _overrideGateways.AddRange(gateways);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load service gateways");
        }
        finally
        {
            _isLoadingGateways = false;
        }
    }
}
