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
    private readonly DalamudUtilService _dalamudUtilService;
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
        _dalamudUtilService = dalamudUtilService;
        SizeConstraints = new()
        {
            MinimumSize = new(1000, 500),
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

    private enum SortByID
    {
        None,
        UID,
        Alias,
        FileSize,
        VRAM,
        Triangles
    }

    private SortByID sortBy = SortByID.None;
    private bool sortAscending = true;

    public static void CenterItemInColumn(Action drawAction)
    {
        float cellWidth = ImGui.GetColumnWidth();
        Vector2 itemSize = ImGui.CalcTextSize("A");
        float cursorX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorX + (cellWidth - itemSize.X) / 2);

        drawAction.Invoke();
    }

    private bool defaultSortApplied = false;

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
        _uiSharedService.BigText("Visible Players (" + _cachedVisiblePairs.Count.ToString() + ")");
        var fps = (int)_dalamudUtilService.FPSCounter;
        var fpsText = $"FPS: {fps}";
        var fpsWidth = ImGui.GetContentRegionMax().X;
        var fpsTextSize = ImGui.CalcTextSize("fpsText").X;
        ImGui.SameLine(fpsWidth - fpsTextSize - 60f * ImGuiHelpers.GlobalScale);
        _uiSharedService.BigText(fpsText);
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
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.Separator();

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
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8f * ImGuiHelpers.GlobalScale, 4f * ImGuiHelpers.GlobalScale));

        if (ImGui.BeginTable("AnalysisTable", 7,
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SortMulti,
                new Vector2(width, height)))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(0, 1);

                ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 24f * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("UID");
                ImGui.TableSetupColumn("Alias");
                ImGui.TableSetupColumn("File Size");
                ImGui.TableSetupColumn("Approx. VRAM Usage");
                ImGui.TableSetupColumn("Approx. Triangle Count");
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);

                // draw manual heeders clickable for sort
                ImGui.TableNextRow();

                void HeaderCell(string label, int colIndex, SortByID sortKey, bool totheleft = false)
                {
                    // sort only columns 2 to 6
                    if (colIndex >= 1 && colIndex <= 5)
                    {
                        ImGui.TableSetColumnIndex(colIndex);

                    Vector2 cellStart = ImGui.GetCursorPos();
                    float cellWidth = ImGui.GetColumnWidth();
                    Vector2 textSize = ImGui.CalcTextSize(label);

                    if (ImGui.InvisibleButton(label + "Btn", new Vector2(cellWidth, ImGui.GetTextLineHeight())))
                        {
                            if (sortBy == sortKey)
                                sortAscending = !sortAscending;
                            else
                            {
                                sortBy = sortKey;
                                sortAscending = true;
                            }
                        }

                    float indent = totheleft ? 0.0f : (cellWidth - textSize.X) * 0.5f;
                    ImGui.SetCursorPos(new Vector2(cellStart.X + indent, cellStart.Y));

                    ImGui.TextUnformatted(label);

                    ImGui.SetCursorPosY(cellStart.Y + ImGui.GetTextLineHeight());
                    }
                    else
                    {
                        // If outside of the valid range, just render the header cell without sorting logic
                        ImGui.TableSetColumnIndex(colIndex);
                        ImGui.TextUnformatted(label);
                    }
                }

                HeaderCell("", 0, SortByID.None);
                HeaderCell("UID", 1, SortByID.UID, true);
                HeaderCell("Alias", 2, SortByID.Alias, true);
                HeaderCell("File Size", 3, SortByID.FileSize);
                HeaderCell("Approx. VRAM Usage", 4, SortByID.VRAM);
                HeaderCell("Approx. Triangle Count", 5, SortByID.Triangles);
                HeaderCell("Actions", 6, SortByID.None, true);

                // Sort my table
                var sortedPairs = allVisiblePairs.ToList();

                if (!defaultSortApplied)
                {
                    sortBy = SortByID.VRAM;
                    sortAscending = false; // false = decending / big to small 
                    defaultSortApplied = true;
                }

                if (sortBy != SortByID.None)
                {
                    sortedPairs.Sort((a, b) =>
                    {
                        int cmp = sortBy switch
                        {
                            SortByID.UID => string.Compare(a.UserData.UID, b.UserData.UID, StringComparison.OrdinalIgnoreCase),
                            SortByID.Alias => string.Compare(a.UserData.Alias ?? "", b.UserData.Alias ?? "", StringComparison.OrdinalIgnoreCase),
                            SortByID.FileSize => a.LastAppliedDataBytes.CompareTo(b.LastAppliedDataBytes),
                            SortByID.VRAM => a.LastAppliedApproximateVRAMBytes.CompareTo(b.LastAppliedApproximateVRAMBytes),
                            SortByID.Triangles => a.LastAppliedDataTris.CompareTo(b.LastAppliedDataTris),
                            _ => 0
                        };
                        return sortAscending ? cmp : -cmp;
                    });
                }

                // get indent for vram, filesize, and triangles for nubmers right allignment <3

                //triangle numbers
                int maxTriWidth = 0;
                Dictionary<Pair, string> formattedTris = new();

                foreach (var p in sortedPairs)
                {
                    string t;

                    if (p.LastAppliedDataTris > 0)
                        t = p.LastAppliedDataTris >= 1000
                            ? (p.LastAppliedDataTris / 1000d).ToString("0.0'k'")
                            : p.LastAppliedDataTris.ToString();
                    else
                        t = "--";

                    formattedTris[p] = t;

                    int trianglewidth = (int)ImGui.CalcTextSize(t).X;
                    if (trianglewidth > maxTriWidth)
                        maxTriWidth = trianglewidth;
                }

                // fize size
                int maxFileSizeWidth = 0;
                Dictionary<Pair, string> formattedFileSizes = new();

                foreach (var p in sortedPairs)
                {
                    string s = p.LastAppliedDataBytes >= 0
                        ? UiSharedService.ByteToString(p.LastAppliedDataBytes, true)
                        : "--";

                    formattedFileSizes[p] = s;

                    int fswidth = (int)ImGui.CalcTextSize(s).X;
                    if (fswidth > maxFileSizeWidth)
                        maxFileSizeWidth = fswidth;
                }

                // vram numbers
                int maxVRAMWidth = 0;
                Dictionary<Pair, string> formattedVRAM = new();
                Dictionary<Pair, string> compressionRedirects = new();

                foreach (var p in sortedPairs)
                {
                    string s = p.LastAppliedApproximateVRAMBytes >= 0
                        ? UiSharedService.ByteToString(p.LastAppliedApproximateVRAMBytes, true)
                        : "--";

                    formattedVRAM[p] = s;
                    compressionRedirects[p] = p.LastAppliedCompressedAlternates >= 0 ? p.LastAppliedCompressedAlternates.ToString() : "--";

                    int vrwidth = (int)ImGui.CalcTextSize(s).X;
                    if (vrwidth > maxVRAMWidth)
                        maxVRAMWidth = vrwidth;
                }

                // time to draw table data.
                foreach (var pair in sortedPairs)
                {
                    bool shouldHighlight = _dalamudUtilService.TargetName == pair.PlayerName;
                    float rowStartHeightStart = ImGui.GetCursorPosY();

                    ImGui.TableNextRow();

                    // Visible Eyeball Icon
                    ImGui.TableSetColumnIndex(0);
                    float cellWidth = ImGui.GetColumnWidth();
                    Vector2 iconSize = ImGui.CalcTextSize(FontAwesomeIcon.Eye.ToIconString()); // approximate width of icon
                    float indent = (cellWidth - iconSize.X) * 0.5f;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
                    _uiSharedService.IconText(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
                    UiSharedService.AttachToolTip("Target " + pair.PlayerName);
                    if (ImGui.IsItemClicked()) Mediator.Publish(new TargetPairMessage(pair));

                    // UID Column                     
                    ImGui.TableSetColumnIndex(1);
                    ImGui.AlignTextToFramePadding();
                    using var targetColor = ImRaii.PushColor(ImGuiCol.Text, UiSharedService.Color(ImGuiColors.ParsedGreen), shouldHighlight);
                    TableHelper.CText(pair.UserData.UID, centerHorizontally: false, leftPadding: 0f);
                    targetColor.Dispose();
                    if (ImGui.IsItemClicked())
                    {
                        ImGui.SetClipboardText(pair.UserData.UID);
                    }
                    UiSharedService.AttachToolTip("Click to copy");

                    // Alias/vanity Column
                    ImGui.TableSetColumnIndex(2);
                    ImGui.AlignTextToFramePadding();
                    using var aliasColor = ImRaii.PushColor(ImGuiCol.Text, UiSharedService.Color(ImGuiColors.ParsedGreen), shouldHighlight);
                    TableHelper.CText(pair.UserData.Alias ?? "", centerHorizontally: false, leftPadding: 0f);
                    aliasColor.Dispose();                    

                    // file size column
                    ImGui.TableSetColumnIndex(3);
                    ImGui.AlignTextToFramePadding();
                    string FData(long bytes) => bytes >= 0 ? UiSharedService.ByteToString(bytes, true) : "--";
                    string fileSizeText = FData(pair.LastAppliedDataBytes);
                    float space3width = ImGui.CalcTextSize(" ").X;  // Width of a single space character
                    int spaces3 = (int)((maxFileSizeWidth - ImGui.CalcTextSize(fileSizeText).X) / space3width);
                    string mypaddedfilesize = new string(' ', Math.Max(0, spaces3)) + fileSizeText;

                    if (string.Equals(fileSizeText, "--", StringComparison.Ordinal))
                    {
                        TableHelper.CText("--");
                    }
                    else
                    {
                        TableHelper.CText(mypaddedfilesize, centerHorizontally: true);
                        UiSharedService.AttachToolTip($"Compression redirects: {compressionRedirects[pair]} (Go to Settings > Performance to manage automatic compression)");
                    }

                    // VRAM Column 
                    ImGui.TableSetColumnIndex(4);
                    ImGui.AlignTextToFramePadding();
                    string vramText = formattedVRAM[pair];
                    float space2width = ImGui.CalcTextSize(" ").X;
                    int spaces2 = (int)((maxVRAMWidth - ImGui.CalcTextSize(vramText).X) / space2width);
                    string mypaddedVRAM = new string(' ', Math.Max(0, spaces2)) + vramText;

                    var currentVramWarning = _playerPerformanceConfig.Current.VRAMSizeWarningThresholdMiB;
                    var approxVram = pair.LastAppliedApproximateVRAMBytes;

                    if (pair.LastAppliedDataBytes >= 0)
                    {
                        if (currentVramWarning * 1024 * 1024 < approxVram)
                        {
                            TableHelper.CCText(mypaddedVRAM, ImGuiColors.DalamudYellow);
                            UiSharedService.AttachToolTip($"Exceeds your threshold by {UiSharedService.ByteToString(approxVram - (currentVramWarning * 1024 * 1024))}.");
                        }
                        else
                            TableHelper.CText(mypaddedVRAM);
                    }
                    else
                        TableHelper.CText("--");

                    // Triangle Column
                    ImGui.TableSetColumnIndex(5);
                    ImGui.AlignTextToFramePadding();
                    var currentTriWarning = _playerPerformanceConfig.Current.TrisWarningThresholdThousands;
                    var approxTris = pair.LastAppliedDataTris;
                    string t = formattedTris[pair];
                    float space1width = ImGui.CalcTextSize(" ").X;
                    int spaces1 = (int)((maxTriWidth - ImGui.CalcTextSize(t).X) / space1width);
                    string mypaddedtriangles = new string(' ', Math.Max(0, spaces1)) + t;

                    if (approxTris > 0)
                    {
                        if (currentTriWarning * 1000 < approxTris)
                        {
                            TableHelper.CCText(mypaddedtriangles, ImGuiColors.DalamudYellow);
                            UiSharedService.AttachToolTip($"Exceeds your threshold by {approxTris - currentTriWarning * 1000:N0} triangles.");
                        }
                        else
                            TableHelper.CText(mypaddedtriangles);
                    }
                    else
                        TableHelper.CText("--");

                    // Button options Column
                    var uid = pair.UserData.UID;
                    bool isBusy = _pauseClicked.Contains(uid);

                    ImGui.TableSetColumnIndex(6);
                    ImGui.AlignTextToFramePadding();
                    ImGui.BeginDisabled(isBusy);
                    if (ImGui.Button($"Pause##{pair.UserData.UID}"))
                    {
                        // It can take a moment to dispose a large player, so we don't let the user spam the button
                        if (_pauseClicked.Add(uid))
                        {
                            _ = _apiController.PauseAsync(pair.UserData).ContinueWith(_ => _pauseClicked.Remove(uid));
                        }
                    }
                    ImGui.EndDisabled();

                    ImGui.SameLine();
                    if (ImGui.Button($"Refresh##{pair.UserData.UID}"))
                    {
                        _ = _apiController.CyclePauseAsync(pair.UserData);
                    }
 
                    if (TableHelper.SRowhovered(rowStartHeightStart, ImGui.GetCursorPosY()))
                    {
                        var rowIndex = ImGui.TableGetRowIndex();
                        //uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.6f, 1.0f, 0.5f));
                        var color = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color, rowIndex);
                    }
                }
            }
            finally
            {
                ImGui.EndTable();
            }
        }
    }

    private void DrawPaused()
    {
        var allPairs = _pairManager.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
        bool FilterPausedUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsPaused;
        var allPausedPairs = ImmutablePairList(allPairs.Where(FilterPausedUsers));

        _uiSharedService.BigText("Paused Pairs");
        ImGuiHelpers.ScaledDummy(2f);

        UiSharedService.TextWrapped("This shows all pairs you have paused, not just players around you.");
        UiSharedService.TextWrapped("Players may be paused manually, or automatically from exceeding performance thresholds.");
        ImGuiHelpers.ScaledDummy(2f);

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8f * ImGuiHelpers.GlobalScale, 4f * ImGuiHelpers.GlobalScale));
        using var table = ImRaii.Table("pauseTable", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg, new Vector2(0, 0));

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthFixed, 240f);
        ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.WidthFixed, 240f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 200f);
        ImGui.TableHeadersRow();

        foreach (var pair in allPausedPairs)
        {
            float rowStartHeightStart = ImGui.GetCursorPosY();

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

            // Row highlighting
            if (TableHelper.SRowhovered(rowStartHeightStart, ImGui.GetCursorPosY()))
            {
                var rowIndex = ImGui.TableGetRowIndex();
                var color = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
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
        using (var table = ImRaii.Table("permMatrix", 9, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
             | ImGuiTableFlags.NoHostExtendX, new Vector2(0, tableH)))
        {
            if (table)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Preferred", ImGuiTableColumnFlags.WidthStretch, .8f);
                ImGui.TableSetupColumn("Disable Sounds", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Disable Animations", ImGuiTableColumnFlags.WidthStretch, 1.8f);
                ImGui.TableSetupColumn("Disable VFX", ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("Their Sounds", ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("Their Animations", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Their VFX", ImGuiTableColumnFlags.WidthStretch, 1.2f);
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
                    
                    bool shouldHighlight = _dalamudUtilService.TargetName == pair.PlayerName;

                    // Track change for state
                    bool changed = false;

                    float rowStartHeightStart = ImGui.GetCursorPosY();
                    ImGui.TableNextRow();

                    // UID
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    using var targetColor = ImRaii.PushColor(ImGuiCol.Text, UiSharedService.Color(ImGuiColors.ParsedGreen), shouldHighlight);
                    ImGui.TextUnformatted(uid);
                    if (ImGui.IsItemClicked())
                    {
                        ImGui.SetClipboardText(pair.UserData.UID);
                    }
                    UiSharedService.AttachToolTip("Click to copy");

                    // Alias
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(pair.UserData.Alias ?? "");
                    targetColor.Dispose();

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

                    // Row highlighting
                    if (TableHelper.SRowhovered(rowStartHeightStart, ImGui.GetCursorPosY()))
                    {
                        var rowIndex = ImGui.TableGetRowIndex();
                        var color = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color, rowIndex);
                    }

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
                    )).ContinueWith((_) => Mediator.Publish(new RedrawNameplateMessage()));
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

            if (hasChanges)
            {
                ImGui.SameLine();
                UiSharedService.ColorText("Changes must be saved before taking effect.", ImGuiColors.DalamudYellow);
            }
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
}
