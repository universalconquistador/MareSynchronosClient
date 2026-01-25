using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.Services.Mediator;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<PerformanceTabs>? _selectedTabPerformance;

    private enum PerformanceTabs
    {
        Texture,
        Threshold,
        Height
    }

    private void DrawPerformanceSettings()
    {
        _lastTab = "Performance";

        _selectedTabPerformance = UiNav.DrawTabsUnderline(_theme,
            [
            new(PerformanceTabs.Texture, "Auto Texture Compression", DrawPerformanceTextureCompression),
            new(PerformanceTabs.Threshold, "Auto Threshold Pausing", DrawPerformanceThresholdPausing),
            new(PerformanceTabs.Height, "Auto Height Pausing", DrawPerformanceHeightPausing),
            ],
            _selectedTabPerformance,
            _uiShared.IconFont
            );

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabPerformance.TabAction.Invoke();
    }

    private void DrawPerformanceTextureCompression()
    {
        _uiShared.BigText("Auto Texture Compression");
        ImGuiHelpers.ScaledDummy(2);

        UiSharedService.TextWrapped("Options for using the PlayerSync servers' automatically-compressed versions of players' uncompressed textures.");
        UiSharedService.ColorTextWrapped("Conversion to BC7 does not happen on your PC. There is no negative performance impact in using this feature.", ImGuiColors.DalamudYellow);
        UiSharedService.TextWrapped("This option applies to texture downloads for other players, you should still compress your own mods as well.");
        ImGui.Dummy(new Vector2(10));
        ImGui.Text("Automatic Compression Mode");
        using (ImRaii.PushIndent())
        {
            var textureCompressionMode = _playerPerformanceConfigService.Current.TextureCompressionModeOrDefault;
            _uiShared.DrawHelpText("The PlayerSync server automatically compresses uncompressed textures from other players. Here you can opt to use these compressed textures when available.");

            if (ImGui.RadioButton("Always use source quality", ref textureCompressionMode, MareConfiguration.Configurations.CompressedAlternateUsage.AlwaysSourceQuality))
            {
                _playerPerformanceConfigService.Current.TextureCompressionMode = textureCompressionMode;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("Downloads and applies the exact textures uploaded by other players, even if uncompressed.\nThis is the the same behavior as before server compression was introduced.\n\nThis choice uses more bandwidth, storage, and VRAM.");

            if (ImGui.RadioButton("Use automatically compressed textures for new downloads", ref textureCompressionMode, MareConfiguration.Configurations.CompressedAlternateUsage.CompressedNewDownloads))
            {
                _playerPerformanceConfigService.Current.TextureCompressionMode = textureCompressionMode;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("Downloads and applies automatically-compressed versions of other players' uncompressed textures, unless the original texture has already been downloaded.\n\nThis choice saves bandwidth, storage, and VRAM, but only with new downloads.");

            if (ImGui.RadioButton("Always use automatically compressed textures", ref textureCompressionMode, MareConfiguration.Configurations.CompressedAlternateUsage.AlwaysCompressed))
            {
                _playerPerformanceConfigService.Current.TextureCompressionMode = textureCompressionMode;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("Downloads and applies automatically-compressed versions of other players' uncompressed textures.\n\nThis choice saves the most bandwidth, storage, and VRAM, but it does not clear out any uncompressed textures that have been already downloaded.");
        }

        _uiShared.BigText("Override UIDs");
        ImGuiHelpers.ScaledDummy(2);

        UiSharedService.TextWrapped("The entries in the list below will always use source quality.");
        ImGui.Dummy(new Vector2(10));
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##overrideuids", ref _uidToAddForOverride, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForOverride)))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add UID/Vanity ID to overrides"))
            {
                if (!_playerPerformanceConfigService.Current.UIDsToOverride.Contains(_uidToAddForOverride, StringComparer.Ordinal))
                {
                    _playerPerformanceConfigService.Current.UIDsToOverride.Add(_uidToAddForOverride);
                    _playerPerformanceConfigService.Save();
                }
                _uidToAddForOverride = string.Empty;
            }
        }
        _uiShared.DrawHelpText("Hint: UIDs are case sensitive.");
        var overrideList = _playerPerformanceConfigService.Current.UIDsToOverride;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        using (var lb = ImRaii.ListBox("UID overrides"))
        {
            if (lb)
            {
                for (int i = 0; i < overrideList.Count; i++)
                {
                    bool shouldBeSelected = _selectedOverrideEntry == i;
                    if (ImGui.Selectable(overrideList[i] + "##" + i, shouldBeSelected))
                    {
                        _selectedOverrideEntry = i;
                    }
                }
            }
        }
        using (ImRaii.Disabled(_selectedOverrideEntry == -1))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete selected UID"))
            {
                _playerPerformanceConfigService.Current.UIDsToOverride.RemoveAt(_selectedOverrideEntry);
                _selectedOverrideEntry = -1;
                _playerPerformanceConfigService.Save();
            }
        }

        ImGui.Dummy(new Vector2(10));
    }

    private void DrawPerformanceThresholdPausing()
    {
        _uiShared.BigText("Auto Threshold Pausing");
        ImGuiHelpers.ScaledDummy(2);

        UiSharedService.TextWrapped("Configure options to warn and/or pause players exceeding your performance thresholds.");
        ImGui.Dummy(new Vector2(10));
        bool showPerformanceIndicator = _playerPerformanceConfigService.Current.ShowPerformanceIndicator;
        if (ImGui.Checkbox("Show performance indicator", ref showPerformanceIndicator))
        {
            _playerPerformanceConfigService.Current.ShowPerformanceIndicator = showPerformanceIndicator;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("Will show a performance indicator when players exceed defined thresholds in PlayerSync's UI." + Environment.NewLine + "Will use warning thresholds.");
        bool warnOnExceedingThresholds = _playerPerformanceConfigService.Current.WarnOnExceedingThresholds;
        if (ImGui.Checkbox("Warn on loading in players exceeding performance thresholds", ref warnOnExceedingThresholds))
        {
            _playerPerformanceConfigService.Current.WarnOnExceedingThresholds = warnOnExceedingThresholds;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("PlayerSync will print a warning in chat once per session of meeting those people. Will not warn on players with preferred permissions.");
        using (ImRaii.Disabled(!warnOnExceedingThresholds && !showPerformanceIndicator))
        {
            using var indent = ImRaii.PushIndent(2);
            var warnOnPref = _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds;
            if (ImGui.Checkbox("Warn/Indicate also on players with preferred permissions", ref warnOnPref))
            {
                _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds = warnOnPref;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("PlayerSync will also print warnings and show performance indicator for players where you enabled preferred permissions. If warning in general is disabled, this will not produce any warnings.");
        }
        using (ImRaii.Disabled(!showPerformanceIndicator && !warnOnExceedingThresholds))
        {
            var vram = _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB;
            var tris = _playerPerformanceConfigService.Current.TrisWarningThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Warning VRAM threshold", ref vram))
            {
                _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB = vram;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("Limit in MiB of approximate VRAM usage to trigger warning or performance indicator on UI." + UiSharedService.TooltipSeparator
                + "Default: 375 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Warning Triangle threshold", ref tris))
            {
                _playerPerformanceConfigService.Current.TrisWarningThresholdThousands = tris;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(thousand triangles)");
            _uiShared.DrawHelpText("Limit in approximate used triangles from mods to trigger warning or performance indicator on UI." + UiSharedService.TooltipSeparator
                + "Default: 165 thousand");
        }
        ImGui.Dummy(new Vector2(10));
        bool autoPause = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds;
        bool autoPauseEveryone = _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds;
        if (ImGui.Checkbox("Automatically pause players exceeding thresholds", ref autoPause))
        {
            _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds = autoPause;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("When enabled, it will automatically pause all players without preferred permissions that exceed the thresholds defined below." + Environment.NewLine
            + "Will print a warning in chat when a player got paused automatically."
            + UiSharedService.TooltipSeparator + "Warning: this will not automatically unpause those people again, you will have to do this manually.");
        using (ImRaii.Disabled(!autoPause))
        {
            using var indent = ImRaii.PushIndent(2);
            if (ImGui.Checkbox("Automatically pause also players with preferred permissions", ref autoPauseEveryone))
            {
                _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds = autoPauseEveryone;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("When enabled, will automatically pause all players regardless of preferred permissions that exceed thresholds defined below." + UiSharedService.TooltipSeparator +
                "Warning: this will not automatically unpause those people again, you will have to do this manually.");
            var vramAuto = _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB;
            var trisAuto = _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Auto Pause VRAM threshold", ref vramAuto))
            {
                _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB = vramAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("When a loading in player and their VRAM usage exceeds this amount, automatically pauses the synced player." + UiSharedService.TooltipSeparator
                + "Default: 550 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Auto Pause Triangle threshold", ref trisAuto))
            {
                _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands = trisAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(thousand triangles)");
            _uiShared.DrawHelpText("When a loading in player and their triangle count exceeds this amount, automatically pauses the synced player." + UiSharedService.TooltipSeparator
                + "Default: 250 thousand");
        }
        ImGui.Dummy(new Vector2(10));
        _uiShared.BigText("Whitelisted UIDs");
        UiSharedService.TextWrapped("The entries in the list below will be ignored for all warnings and auto pause operations.");
        ImGui.Dummy(new Vector2(10));
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##ignoreuid", ref _uidToAddForIgnore, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForIgnore)))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add UID/Vanity ID to whitelist"))
            {
                if (!_playerPerformanceConfigService.Current.UIDsToIgnore.Contains(_uidToAddForIgnore, StringComparer.Ordinal))
                {
                    _playerPerformanceConfigService.Current.UIDsToIgnore.Add(_uidToAddForIgnore);
                    _playerPerformanceConfigService.Save();
                }
                _uidToAddForIgnore = string.Empty;
            }
        }
        _uiShared.DrawHelpText("Hint: UIDs are case sensitive.");
        var playerList = _playerPerformanceConfigService.Current.UIDsToIgnore;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        using (var lb = ImRaii.ListBox("UID whitelist"))
        {
            if (lb)
            {
                for (int i = 0; i < playerList.Count; i++)
                {
                    bool shouldBeSelected = _selectedEntry == i;
                    if (ImGui.Selectable(playerList[i] + "##" + i, shouldBeSelected))
                    {
                        _selectedEntry = i;
                    }
                }
            }
        }
        using (ImRaii.Disabled(_selectedEntry == -1))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete selected UID"))
            {
                _playerPerformanceConfigService.Current.UIDsToIgnore.RemoveAt(_selectedEntry);
                _selectedEntry = -1;
                _playerPerformanceConfigService.Save();
            }
        }

        ImGui.Dummy(new Vector2(10));
    }

    private void DrawPerformanceHeightPausing()
    {
        var maxHeightManual = _playerPerformanceConfigService.Current.MaxHeightManual;
        var maxHeightActual = _playerPerformanceConfigService.Current.MaxHeightAbsolute;
        var maxHeightMultiplier = _playerPerformanceConfigService.Current.MaxHeightMultiplier;
        var shouldPauseHeight = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingHeightThresholds;
        var shouldNotifyOnHeight = _playerPerformanceConfigService.Current.WarnOnAutoHeightExceedingThreshold;
        var noAutoPausePairs = _playerPerformanceConfigService.Current.NoAutoPauseDirectPairs;

        _uiShared.BigText("Auto Height Pausing");

        UiSharedService.TextWrapped("Configure auto pausing for players based on their scaled height.");
        ImGui.Dummy(new Vector2(10));

        if (ImGui.Checkbox("Auto pause players exceeding thresholds", ref shouldPauseHeight))
        {
            _playerPerformanceConfigService.Current.AutoPausePlayersExceedingHeightThresholds = shouldPauseHeight;
            _playerPerformanceConfigService.Save();
            if (shouldPauseHeight)
            {
                Mediator.Publish(new ChangeFilterMessage());
            }
        }
        UiSharedService.ColorTextWrapped("Toggle this feature off/on again after changing values to refresh pairs immediately.", ImGuiColors.DalamudRed);

        if (ImGui.Checkbox("Don't auto pause direct pairs exceeding thresholds", ref noAutoPausePairs))
        {
            _playerPerformanceConfigService.Current.NoAutoPauseDirectPairs = noAutoPausePairs;
            _playerPerformanceConfigService.Save();
            if (!noAutoPausePairs && shouldPauseHeight)
            {
                Mediator.Publish(new ChangeFilterMessage());
            }
        }

        if (ImGui.Checkbox("Warn on loading player who exceed your height thresholds", ref shouldNotifyOnHeight))
        {
            _playerPerformanceConfigService.Current.WarnOnAutoHeightExceedingThreshold = shouldNotifyOnHeight;
            _playerPerformanceConfigService.Save();
        }
        ImGui.Dummy(new Vector2(4));
        UiSharedService.ColorTextWrapped("Values are scaled by race and M/F vanilla defaults.", ImGuiColors.DalamudYellow);
        UiSharedService.ColorTextWrapped("Set slider to 100% to pause anyone not vanilla height.", ImGuiColors.DalamudYellow);

        using (ImRaii.Disabled(maxHeightManual))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Pause players above ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200f);
            if (ImGui.SliderFloat("##max", ref maxHeightMultiplier, 100.0f, 500.0f, "%.0f%%"))
            {
                _playerPerformanceConfigService.Current.MaxHeightMultiplier = maxHeightMultiplier;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("their normal max height.");
        }

        if (ImGui.Checkbox("Manually set max height threshold applied to all players", ref maxHeightManual))
        {
            _playerPerformanceConfigService.Current.MaxHeightManual = maxHeightManual;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("This will effectivley pause all players beyond a specified height.");

        using (ImRaii.Disabled(!maxHeightManual))
        {
            using var ident = ImRaii.PushIndent(2);
            bool changedImperial = false;
            bool changedMetric = false;
            int _maxHeightCm = _playerPerformanceConfigService.Current.MaxHeightAbsolute;
            CmToFeetInches(_maxHeightCm, out var _maxHeightFeet, out var _maxHeightInches);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Max height:");
            ImGui.SameLine();

            // Feet
            ImGui.SetNextItemWidth(60);
            changedImperial |= ImGui.InputInt("##maxFeet", ref _maxHeightFeet);
            ImGui.SameLine();
            ImGui.TextUnformatted("feet");

            ImGui.SameLine();

            // Inches
            ImGui.SetNextItemWidth(60);
            changedImperial |= ImGui.InputInt("##maxInches", ref _maxHeightInches);
            ImGui.SameLine();
            ImGui.TextUnformatted("inches or");

            ImGui.SameLine();

            // Centimeters
            ImGui.SetNextItemWidth(60);
            changedMetric |= ImGui.InputInt("##maxCm", ref _maxHeightCm);
            ImGui.SameLine();
            ImGui.TextUnformatted("cm");

            if (changedImperial)
            {
                if (_maxHeightFeet < 0) _maxHeightFeet = 0;
                if (_maxHeightInches < 0) _maxHeightInches = 0;

                int totalInches = _maxHeightFeet * 12 + _maxHeightInches;
                if (totalInches < 0) totalInches = 0;

                _maxHeightFeet = totalInches / 12;
                _maxHeightInches = totalInches % 12;
                _maxHeightCm = FeetInchesToCm(_maxHeightFeet, _maxHeightInches);

                _playerPerformanceConfigService.Current.MaxHeightAbsolute = _maxHeightCm;
                _playerPerformanceConfigService.Save();
            }
            else if (changedMetric)
            {
                if (_maxHeightCm < 0) _maxHeightCm = 0;

                _playerPerformanceConfigService.Current.MaxHeightAbsolute = _maxHeightCm;
                _playerPerformanceConfigService.Save();
            }
        }

        UiSharedService.ColorTextWrapped("Paused pairs must be manually unpaused.", ImGuiColors.DalamudYellow);
        ImGui.Dummy(new Vector2(10));

        _uiShared.BigText("Whitelisted UIDs");
        UiSharedService.TextWrapped("The entries in the list below will be ignored for all warnings and auto HEIGHT pause operations.");
        ImGui.Dummy(new Vector2(10));
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##ignoreheightuid", ref _uidToAddForHeightIgnore, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForHeightIgnore)))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add UID/Vanity ID to Whitelist"))
            {
                if (!_playerPerformanceConfigService.Current.UIDsToIgnoreForHeightPausing.Contains(_uidToAddForHeightIgnore, StringComparer.Ordinal))
                {
                    _playerPerformanceConfigService.Current.UIDsToIgnoreForHeightPausing.Add(_uidToAddForHeightIgnore);
                    _playerPerformanceConfigService.Save();
                }
                _uidToAddForHeightIgnore = string.Empty;
            }
        }
        _uiShared.DrawHelpText("Hint: UIDs are case sensitive.");
        var playerHeightList = _playerPerformanceConfigService.Current.UIDsToIgnoreForHeightPausing;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        using (var lb = ImRaii.ListBox("UID Whitelist"))
        {
            if (lb)
            {
                for (int i = 0; i < playerHeightList.Count; i++)
                {
                    bool shouldBeSelected = _selectedHeightEntry == i;
                    if (ImGui.Selectable(playerHeightList[i] + "##" + i, shouldBeSelected))
                    {
                        _selectedHeightEntry = i;
                    }
                }
            }
        }
        using (ImRaii.Disabled(_selectedHeightEntry == -1))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Selected UID"))
            {
                _playerPerformanceConfigService.Current.UIDsToIgnoreForHeightPausing.RemoveAt(_selectedHeightEntry);
                _selectedHeightEntry = -1;
                _playerPerformanceConfigService.Save();
            }
        }
        ImGui.Dummy(new Vector2(10));
    }
}
