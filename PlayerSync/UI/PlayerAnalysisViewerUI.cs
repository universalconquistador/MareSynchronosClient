using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Numerics;

namespace MareSynchronos.UI;

internal class PlayerAnalysisViewerUI : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfig;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private RefreshMode _refreshMode = RefreshMode.Live;
    private DateTime _lastUpdate = DateTime.MinValue;
    private ImmutableList<Pair> _cachedVisiblePairs = ImmutableList<Pair>.Empty;
    private bool _manualRefresh = false;
    private readonly HashSet<string> _pauseClicked = new();
    private readonly Dictionary<string, UserPermissions> _edited = new(StringComparer.Ordinal);


    public PlayerAnalysisViewerUI(ILogger<PlayerAnalysisViewerUI> logger, MareMediator mediator, PerformanceCollectorService performanceCollector,
        UiSharedService uiSharedService, PairManager pairManager, PlayerPerformanceConfigService playerPerformanceConfigService, 
        ApiController apiController, DalamudUtilService dalamudUtilService) : base(logger, mediator, "Player Analysis Viewer", performanceCollector)
    { 
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _playerPerformanceConfig = playerPerformanceConfigService;
        _apiController = apiController;
        _dalamudUtil = dalamudUtilService;
        SizeConstraints = new()
        {
            MinimumSize = new(600, 500),
            MaximumSize = new(1000, 2000)
        };
        Mediator.Subscribe<OpenPlayerAnalysisViewerUIUiMessage>(this, (_) => Toggle());
    }

    private enum RefreshMode
    {
        Live,
        Sec5,
        Sec30,
        Manual,
    }

    private static bool PermissionsEqual(UserPermissions a, UserPermissions b) => a == b;

    private static ImmutableList<Pair> ImmutablePairList(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u) => u.Select(k => k.Key).ToImmutableList();

    protected override void DrawInternal()
    {
        using var tabBar = ImRaii.TabBar("playerInfoTabBar");
        using (var tabItem = ImRaii.TabItem("Visible Players"))
        {
            if (tabItem)
            {
                using var id = ImRaii.PushId("visible");
                DrawVisible();
            }
        }
        using (var tabItem = ImRaii.TabItem("Paused Pairs"))
        {
            if (tabItem)
            {
                using var id = ImRaii.PushId("pausedPairs");
                DrawPaused();
            }
        }
        using (var tabItem = ImRaii.TabItem("Permissions"))
        {
            if (tabItem)
            {
                using var id = ImRaii.PushId("permissions");
                DrawPermissions();
            }
        }
    }

    private void DrawVisible()
    {

        var shouldUpdate = false;
        var now = DateTime.UtcNow;

        switch (_refreshMode)
        {
            case RefreshMode.Live:
                _lastUpdate = now;
                shouldUpdate = true;
                break;
            case RefreshMode.Sec5:
                if ((now - _lastUpdate).TotalSeconds > 5)
                {
                    _lastUpdate = now;
                    shouldUpdate = true;
                }
                break;
            case RefreshMode.Sec30:
                if ((now - _lastUpdate).TotalSeconds > 30)
                {
                    _lastUpdate = now;
                    shouldUpdate = true;
                }
                break;
            case RefreshMode.Manual:
                break;
            default:
                break;
        }
        _uiSharedService.BigText("Visible Players (" + _cachedVisiblePairs.Count().ToString() + ")");
        ImGuiHelpers.ScaledDummy(2f);

        ImGui.TextUnformatted("Refresh:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        _uiSharedService.DrawCombo("###refreshInterval", [RefreshMode.Live, RefreshMode.Sec5, RefreshMode.Sec30, RefreshMode.Manual],
            (s) => s switch
            {
                RefreshMode.Live => "Live",
                RefreshMode.Sec5 => "Every 5 Sec",
                RefreshMode.Sec30 => "Every 30 Sec",
                RefreshMode.Manual => "Manual",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                switch (s)
                {
                    case RefreshMode.Live:
                        _refreshMode = RefreshMode.Live; break;
                        case RefreshMode.Sec5:
                        _refreshMode = RefreshMode.Sec5; break;
                        case RefreshMode.Sec30:
                        _refreshMode = RefreshMode.Sec30; break;
                        case RefreshMode.Manual:
                        _refreshMode = RefreshMode.Manual; break;
                }
            }, RefreshMode.Live);
        ImGui.SameLine();
        if (ImGui.Button("Update"))
        {
            _manualRefresh = true;
        }

        UiSharedService.TextWrapped("Players showing -- have no mods or have not been loaded yet.");

        if (shouldUpdate || _manualRefresh)
        {
            _manualRefresh = false;
            var allPairs = _pairManager.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
            bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsVisible;
            _cachedVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));
        }
        var allVisiblePairs = _cachedVisiblePairs;

        var cursorPos = ImGui.GetCursorPosY();
        var max = ImGui.GetWindowContentRegionMax();
        var min = ImGui.GetWindowContentRegionMin();
        var width = max.X - min.X;
        var height = max.Y - cursorPos;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding,new Vector2(8f * ImGuiHelpers.GlobalScale, 4f * ImGuiHelpers.GlobalScale));
        using var table = ImRaii.Table("eventTable", 7, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti, new Vector2(width, height));

        void CText(string text) //center regular text function
        {
            float cellWidth = ImGui.GetColumnWidth();
            float textWidth = ImGui.CalcTextSize(text).X;
            float indent = (cellWidth - textWidth) * 0.5f;
            
            if (indent > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
            
            ImGui.Text(text);
        }

        void CCText(string text, Vector4 color) //center colored text function
        {
            Vector2 textSize = ImGui.CalcTextSize(text);
            float cellWidth = ImGui.GetColumnWidth();
            float indent = (cellWidth - textSize.X) * 0.5f;

            if (indent > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);

            UiSharedService.ColorText(text, color);
        }

        if (table)
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.NoSort);
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("Alias");
            ImGui.TableSetupColumn("File Size");
            ImGui.TableSetupColumn("Approx. VRAM Usage", ImGuiTableColumnFlags.DefaultSort);
            ImGui.TableSetupColumn("Approx. Triangle Count");
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);
            ImGui.TableHeadersRow();

            var sortedPairs = allVisiblePairs.ToList();
            var sortSpecs = ImGui.TableGetSortSpecs();
            if (sortSpecs.SpecsCount > 0)
            {
                sortedPairs.Sort((a, b) => ComparePairsForSort(a, b, sortSpecs));
            }

            foreach (var pair in sortedPairs)
            {
                bool highlightRow = false;

                var target = 

                // Target
                ImGui.TableNextColumn();
                _uiSharedService.IconText(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
                UiSharedService.AttachToolTip("Target " + pair.PlayerName);
                if (ImGui.IsItemClicked())
                {
                    Mediator.Publish(new TargetPairMessage(pair));
                }

                // UID
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(pair.UserData.UID);
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(pair.UserData.UID);
                }
                UiSharedService.AttachToolTip("Click to copy");

                // Alias
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (pair.UserData.Alias != null)
                    {
                        ImGui.TextUnformatted(pair.UserData.Alias);
                    }                
                
                // File Size
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();                
                string FData(long bytes)
                    {
                        return bytes >= 0 ? UiSharedService.ByteToString(bytes, true) : "--";
                    }
                CText(FData(pair.LastAppliedDataBytes));

                // VRAM
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                string FVram(long bytes)
                    {
                        return bytes >= 0 ? UiSharedService.ByteToString(bytes, true) : "--";
                    }
                
                var currentVramWarning = _playerPerformanceConfig.Current.VRAMSizeWarningThresholdMiB;
                var approxVram = pair.LastAppliedApproximateVRAMBytes;
                if (pair.LastAppliedDataBytes >= 0)
                {
                    if ((currentVramWarning * 1024 * 1024 < approxVram))
                    {
                        CCText($"{UiSharedService.ByteToString(pair.LastAppliedApproximateVRAMBytes, true)}", ImGuiColors.DalamudYellow);
                        UiSharedService.AttachToolTip($"Exceeds your threshold by " + $"{UiSharedService.ByteToString(approxVram - (currentVramWarning * 1024 * 1024))}.");
                    }
                    else
                    {
                        CText(FVram(pair.LastAppliedApproximateVRAMBytes));
                    }
                }
                else
                {
                    CText("--");
                }

                // Triangles
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var currentTriWarning = _playerPerformanceConfig.Current.TrisWarningThresholdThousands;
                var approxTris = pair.LastAppliedDataTris;
                // For some reason we get triangles sometimes without data, so only display if we have data applied
                if (pair.LastAppliedDataTris > 0 && pair.LastAppliedDataBytes >= 0)
                {
                    if ((currentTriWarning * 1000 < approxTris))
                    {
                        CCText(pair.LastAppliedDataTris > 1000 ?
                            (pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : pair.LastAppliedDataTris.ToString(), ImGuiColors.DalamudYellow);
                        UiSharedService.AttachToolTip($"Exceeds your threshold by " + $"{approxTris - (currentTriWarning * 1000):N0} triangles.");
                    }
                    else
                    {
                        CText(pair.LastAppliedDataTris > 1000 ? (pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : pair.LastAppliedDataTris.ToString());
                    }
                }
                else
                {
                    CText("--");
                }

                // Actions
                var uid = pair.UserData.UID;
                bool isBusy = _pauseClicked.Contains(uid);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.BeginDisabled(isBusy);
                if (ImGui.Button($"Pause##{uid}"))
                {
                    // It can take a moment to dispose a large player, so we don't let the user spam the button
                    _pauseClicked.Add(uid);
                    _ = _apiController.PauseAsync(pair.UserData).ContinueWith(_ => _pauseClicked.Remove(uid));
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    highlightRow = true;
                }

                ImGui.SameLine();

                if (ImGui.Button($"Refresh##{pair.UserData.UID}"))
                {
                    _ = _apiController.CyclePauseAsync(pair.UserData);
                }
                if (ImGui.IsItemHovered())
                {
                    highlightRow = true;
                }

                // Row highlighting
                if (highlightRow)
                {
                    var rowIndex = ImGui.TableGetRowIndex();
                    var color = ImGui.GetColorU32(ImGuiCol.TableRowBgAlt);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color, rowIndex);
                }
            }
        }
    }

    private static int ComparePairsForSort(Pair a, Pair b, ImGuiTableSortSpecsPtr sortSpecs)
    {
        for (int i = 0; i < sortSpecs.SpecsCount; i++)
        {
            var spec = sortSpecs.Specs[i];
            int cmp = 0;
            switch (spec.ColumnIndex)
            {
                case 1:
                    cmp = string.Compare(a.UserData.UID, b.UserData.UID, StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    {
                        var aAlias = a.UserData.Alias ?? string.Empty;
                        var bAlias = b.UserData.Alias ?? string.Empty;
                        cmp = string.Compare(aAlias, bAlias, StringComparison.OrdinalIgnoreCase);
                        break;
                    }
                case 3:
                    cmp = a.LastAppliedDataBytes.CompareTo(b.LastAppliedDataBytes);
                    break;
                case 4:
                    cmp = a.LastAppliedApproximateVRAMBytes.CompareTo(b.LastAppliedApproximateVRAMBytes);
                    break;
                case 5:
                    cmp = a.LastAppliedDataTris.CompareTo(b.LastAppliedDataTris);
                    break;
                default:
                    continue;
            }
            if (cmp != 0)
            {
                return spec.SortDirection == ImGuiSortDirection.Ascending ? cmp : -cmp;
            }
        }
        return 0;
    }

    private void DrawPaused()
    {
        var allPairs = _pairManager.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
        bool FilterPausedUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsPaused;
        var allPausedPairs = ImmutablePairList(allPairs.Where(FilterPausedUsers));

        _uiSharedService.BigText("Paused Pairs");
        ImGuiHelpers.ScaledDummy(2f);

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8f * ImGuiHelpers.GlobalScale, 4f * ImGuiHelpers.GlobalScale));
        using var table = ImRaii.Table("pauseTable", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("UID");
        ImGui.TableSetupColumn("Alias");
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var pair in allPausedPairs)
        {
            bool highlightRow = false;

            // UID
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(pair.UserData.UID);
            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(pair.UserData.UID);
            }
            UiSharedService.AttachToolTip("Click to copy");

            // Alias
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (pair.UserData.Alias != null)
            {
                ImGui.TextUnformatted(pair.UserData.Alias);
            }

            // Actions
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (ImGui.Button($"Unpause##{pair.UserData.UID}"))
            {
                var perm = pair.UserPair!.OwnPermissions;
                perm.SetPaused(paused: false);
                _ = _apiController.UserSetPairPermissions(new(pair.UserData, perm));
            }
            if (ImGui.IsItemHovered())
            {
                highlightRow = true;
            }

            // Row highlighting
            if (highlightRow)
            {
                var rowIndex = ImGui.TableGetRowIndex();
                var color = ImGui.GetColorU32(ImGuiCol.TableRowBgAlt);
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color, rowIndex);
            }
        }
    }

    private void DrawPermissions()
    {
        _uiSharedService.BigText("Permissions Matrix");
        ImGuiHelpers.ScaledDummy(2f);

        UiSharedService.TextWrapped("Checking Preferred Permissions means Syncshells won't overwrite permissions, such as pause/unpause.");
        ImGui.Separator();
        UiSharedService.ColorTextWrapped("Checking the permission boxes means disabling that permission.", ImGuiColors.DalamudYellow);
        UiSharedService.TextWrapped("Both sides must have the permission enabled for it to take effect on either side.");
        UiSharedService.TextWrapped("This means, make sure you uncheck the box and click save to enable permissions with the paired player.");
        UiSharedService.TextWrapped("If any of their permissions are red, that means they need to unpause you to make it work.");
        ImGuiHelpers.ScaledDummy(2f);

        var shouldUpdate = false;
        var now = DateTime.UtcNow;

        if ((now - _lastUpdate).TotalSeconds > 5)
        {
            _lastUpdate = now;
            shouldUpdate = true;
        }

        if (shouldUpdate)
        {
            var allPairs = _pairManager.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
            bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsVisible;
            _cachedVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));
        }

        foreach (var p in _cachedVisiblePairs)
        {
            var uid = p.UserData.UID;
            if (!_edited.TryGetValue(uid, out var existing))
                _edited[uid] = p.UserPair.OwnPermissions.DeepClone();
        }

        var stillVisible = new HashSet<string>(_cachedVisiblePairs.Select(p => p.UserData.UID), StringComparer.Ordinal);
        var toRemove = _edited.Keys.Where(k => !stillVisible.Contains(k)).ToList();
        foreach (var k in toRemove) _edited.Remove(k);

        var allVisiblePairs = _cachedVisiblePairs;

        var style = ImGui.GetStyle();
        float footerH = ImGui.GetFrameHeight() + style.FramePadding.Y * 6f * ImGuiHelpers.GlobalScale;
        float tableH = MathF.Max(0f, ImGui.GetContentRegionAvail().Y - footerH);

        using var pad = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8f * ImGuiHelpers.GlobalScale, 4f * ImGuiHelpers.GlobalScale));
        using (var table = ImRaii.Table("permMatrix", 9, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.NoHostExtendX, new Vector2(0, tableH)))
        {
            if (table)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 160f);
                ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 160f);
                ImGui.TableSetupColumn("Preferred", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 80f);
                ImGui.TableSetupColumn("Disable Sounds", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 160f);
                ImGui.TableSetupColumn("Disable Animations", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 180f);
                ImGui.TableSetupColumn("Disable VFX", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 120f);
                ImGui.TableSetupColumn("Their Sounds", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 120f);
                ImGui.TableSetupColumn("Their Animations", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 160f);
                ImGui.TableSetupColumn("Their VFX", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 120f);
                ImGui.TableHeadersRow();

                foreach (var pair in allVisiblePairs)
                {
                    var uid = pair.UserData.UID;
                    if (!_edited.TryGetValue(uid, out var edit)) continue;

                    var other = pair.UserPair.OtherPermissions;
                    bool otherSound = other.IsDisableSounds();
                    bool otherAnimations = other.IsDisableAnimations();
                    bool otherVfx = other.IsDisableVFX();

                    bool pref = edit.IsSticky();
                    bool ownSound = edit.IsDisableSounds();
                    bool ownAnimations = edit.IsDisableAnimations();
                    bool ownVfx = edit.IsDisableVFX();

                    // Track change for state
                    bool changed = false;

                    ImGui.TableNextRow();

                    // UID
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(uid);

                    // Alias
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(pair.UserData.Alias ?? "--");

                    // Preferred
                    ImGui.TableNextColumn();
                    if (CenteredCheckbox($"##pref_{uid}", ref pref))
                    {
                        edit.SetSticky(pref);
                        changed = true;
                    }
                    UiSharedService.AttachToolTip("Set Preferred permissions status");

                    // Own — Sounds
                    ImGui.TableNextColumn();
                    if (CenteredCheckbox($"##ownS_{uid}", ref ownSound))
                    {
                        edit.SetDisableSounds(ownSound);
                        changed = true;
                    }
                    UiSharedService.AttachToolTip("Disable Sounds (yours for this pair)");

                    // Own — Animations
                    ImGui.TableNextColumn();
                    if (CenteredCheckbox($"##ownA_{uid}", ref ownAnimations))
                    {
                        edit.SetDisableAnimations(ownAnimations);
                        changed = true;
                    }   
                    UiSharedService.AttachToolTip("Disable Animations (yours for this pair)");

                    // Own — VFX
                    ImGui.TableNextColumn();
                    if (CenteredCheckbox($"##ownV_{uid}", ref ownVfx))
                    {
                        edit.SetDisableVFX(ownVfx);
                        changed = true;
                    }
                    UiSharedService.AttachToolTip("Disable VFX (yours for this pair)");

                    // Other - Sounds
                    ImGui.TableNextColumn();
                    CenteredBooleanIcon(!otherSound, false);
                    UiSharedService.AttachToolTip("Other side: sounds are " + (otherSound ? "disabled" : "enabled"));

                    // Other — Animations
                    ImGui.TableNextColumn();
                    CenteredBooleanIcon(!otherAnimations, false);
                    UiSharedService.AttachToolTip("Other side: animations are " + (otherAnimations ? "disabled" : "enabled"));

                    // Other — VFX
                    ImGui.TableNextColumn();
                    CenteredBooleanIcon(!otherVfx, false);
                    UiSharedService.AttachToolTip("Other side: VFX are " + (otherVfx ? "disabled" : "enabled"));

                    if (changed)
                        _edited[uid] = edit;
                }
            }
        }

        ImGui.BeginChild("permFooter", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        {
            float right = ImGui.GetWindowContentRegionMax().X;
            float left = ImGui.GetWindowContentRegionMin().X;
            float saveW = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Save, "Save");
            float revW = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Undo, "Revert");
            float w = saveW + style.ItemSpacing.X + revW;

            ImGui.SetCursorPosX(MathF.Max(left, right - w));

            // Bottom action bar
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(2f);

            // Compute changed entries only
            var changed = new Dictionary<string, UserPermissions>(StringComparer.Ordinal);
            foreach (var pair in allVisiblePairs)
            {
                var uid = pair.UserData.UID;
                if (!_edited.TryGetValue(uid, out var edit)) continue;
                if (!PermissionsEqual(edit, pair.UserPair.OwnPermissions))
                    changed[uid] = edit;
            }

            var hasChanges = changed.Count > 0;

            using (ImRaii.Disabled(!hasChanges))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, $"Save ({changed.Count})"))
                {
                    var payload = new Dictionary<string, UserPermissions>(changed, StringComparer.Ordinal);

                    _ = _apiController.SetBulkPermissions(new(payload, new(StringComparer.Ordinal)
                    ));
                }
            }
            UiSharedService.AttachToolTip("Apply all edited permissions");

            ImGui.SameLine();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Revert All"))
            {
                _edited.Clear();
                foreach (var p in allVisiblePairs)
                    _edited[p.UserData.UID] = p.UserPair.OwnPermissions.DeepClone();
            }
            UiSharedService.AttachToolTip("Discard all unsaved changes");
        }
        ImGui.EndChild();
    }

    private static bool CenteredCheckbox(string id, ref bool value)
    {
        var colW = ImGui.GetColumnWidth();
        var box = ImGui.GetFrameHeight();
        var x = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(x + MathF.Max(0f, (colW - box) * 0.5f));
        return ImGui.Checkbox(id, ref value);
    }

    private static void CenterInCell(float itemWidth)
    {
        var colW = ImGui.GetColumnWidth();
        var x = ImGui.GetCursorPosX();
        var off = MathF.Max(0f, (colW - itemWidth) * 0.5f);
        ImGui.SetCursorPosX(x + off);
    }

    private void CenteredBooleanIcon(bool ok, bool invert = false)
    {
        CenterInCell(ImGui.GetFrameHeight());
        _uiSharedService.BooleanToColoredIcon(ok, invert);
    }

    //public bool IsTargetPaired()
    //{
    //    return _dalamudUtil.RunOnFrameworkThread(() =>
    //    {
    //        var pc = _targetManager.Target as Dalamud.Plugin.Services.IPlayerCharacter;
    //        if (pc == null) return false;

    //        nint addr = pc.Address;
    //        if (addr == nint.Zero) return false;

    //        // 1) Address match (strongest)
    //        if (_pairManager.PairsWithGroups.Keys.Any(p => p.Address == addr))
    //            return true;

    //    }).GetAwaiter().GetResult();
    //}
}
