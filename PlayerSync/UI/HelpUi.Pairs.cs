using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.Utils;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class HelpUi : WindowMediatorSubscriberBase
{
    private sealed record PairDataResult(Pair Pair, Dictionary<PlayerChanges, bool>? PlayerChanges, Dictionary<ObjectKind, List<FileReplacementData>>? MissingFiles, CharacterData? CharacterData);

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    private List<Pair> _cachedPairsDetail = [];
    private Dictionary<Pair, DataApplicationEvent?> _pairDataApplicationEvents = new();
    private string _pairFilter = string.Empty;
    private CancellationTokenSource _pairDataDetailCt;
    private Task<PairDataResult?>? _pairDataDetailTask;
    private string _pairUidForDataTask = string.Empty;
    private PairDataResult? _pairDataResult;

    private UiNav.Tab<DiagnosticPairsTabs>? _selectedTabDiagnosticPairs;

    private IReadOnlyList<UiNav.Tab<DiagnosticPairsTabs>>? _diagnosticPairsTab;
    private IReadOnlyList<UiNav.Tab<DiagnosticPairsTabs>> DiagnosticPairsTabsList => _diagnosticPairsTab ??=
    [
        //new(DiagnosticPairsTabs.Troubleshooting, "Troubleshooting", DrawPairTroubleshooting),
        new(DiagnosticPairsTabs.PairStatus, "Pair Status", DrawPairStatus),
        new(DiagnosticPairsTabs.PairLogs, "Pair Logs", DrawPairApplicationLogs),
        new(DiagnosticPairsTabs.PairTiming, "Framework Timing", DrawPairTiming),
        new(DiagnosticPairsTabs.PairDetail, "Pair Detail", DrawPairDataDetail),
    ];

    private enum DiagnosticPairsTabs
    {
        Troubleshooting,
        PairStatus,
        PairLogs,
        PairTiming,
        PairDetail
    }

    private void OnOpenPairDataWindow()
    {
        _pairDataDetailCt = _pairDataDetailCt.CancelRecreate();
        _pairDataDetailTask?.Dispose();
        _pairDataDetailTask = null;
    }

    private void OnClosePairDataWindow()
    {
        _pairDataDetailCt?.CancelDispose();
        _pairDataDetailTask?.Dispose();
        _pairDataDetailTask = null;
    }

    private void DrawDiagnosticsPairs()
    {
        _selectedTabDiagnosticPairs = UiNav.DrawTabsUnderline(_theme, DiagnosticPairsTabsList, _selectedTabDiagnosticPairs, _uiShared.IconFont);

        using var child = ImRaii.Child("##diagPairsPanel", new Vector2(0, 0), false);

        _selectedTabDiagnosticPairs.TabAction.Invoke();
    }

    private void DrawPairTroubleshooting()
    {
        _uiShared.BigText("Troubleshooting Pairs");
        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawPairStatus()
    {
        _uiShared.BigText("Pair Data Application Status");
        ImGuiHelpers.ScaledDummy(2);

        using var imGuiId = ImRaii.PushId("pairTable");

        _cachedPairsDetail = _pairManager.GetVisiblePairs().OrderBy(pair => pair.PlayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
        _pairDataApplicationEvents.Clear();
        foreach (var pair in _cachedPairsDetail)
        {
            _pairDataApplicationEvents[pair] = _eventAggregator.GetLatestDataEventByUid(pair.UserData.UID);
        }

        var scale = ImGuiHelpers.GlobalScale;
        var width = 28.0f * scale;

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.Reorderable |
            ImGuiTableFlags.Hideable |
            ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        var tableSize = new Vector2(0.0f, Math.Max(100.0f * scale, ImGui.GetContentRegionAvail().Y - 50.0f * scale));

        ImGui.SetNextItemWidth(400.0f * scale);
        ImGui.InputTextWithHint("##filterPairs", "Filter...", ref _pairFilter, 250);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _pairFilter = string.Empty;
        }

        var filterText = _pairFilter.Trim();
        var filteredPairs = string.IsNullOrWhiteSpace(filterText) ? _cachedPairsDetail : _cachedPairsDetail.Where(pair => FilterPairs(pair, filterText)).ToList();

        static void DrawCheckmark(bool isChecked)
        {
            if (isChecked)
            {
                string checkmark = "\u2713";
                var width = ImGui.GetContentRegionAvail().X;
                var checkmarkWidth = ImGui.CalcTextSize(checkmark).X;
                var startPos = ImGui.GetCursorPosX();

                ImGui.SetCursorPosX(startPos + Math.Max(0.0f, (width - checkmarkWidth) / 2.0f));
                ImGui.TextColored(ImGuiColors.HealerGreen, checkmark);
            }
        }

        if (ImGui.BeginTable("##PairDataTable2", 15, tableFlags, tableSize))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(2, 1);

                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthFixed, 120.0f * scale);
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, 150.0f * scale);
                ImGui.TableSetupColumn("Last Complete Application", ImGuiTableColumnFlags.WidthFixed, 165.0f * scale);
                ImGui.TableSetupColumn("Is Visible", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 55.0f * scale);
                ImGui.TableSetupColumn("Has Data", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 55.0f * scale);

                ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Penumbra", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Heels", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Honorific", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Moodles", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Loci", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Pet Names", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);

                ImGui.TableSetupColumn("Latest Status", ImGuiTableColumnFlags.WidthFixed, 140.0f * scale);
                ImGui.TableSetupColumn("Latest Status Message", ImGuiTableColumnFlags.WidthFixed, 600.0f * scale);

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

                static void DrawTableHeader(int column, string label, string tooltip)
                {
                    ImGui.TableSetColumnIndex(column);
                    ImGui.PushID(column);
                    ImGui.TableHeader(label);
                    UiSharedService.AttachToolTip(tooltip);
                    ImGui.PopID();
                }

                DrawTableHeader(0, "UID", "PlayerSync user ID");
                DrawTableHeader(1, "Character", "Character name");
                DrawTableHeader(2, "Last Complete Application", "Last successful data application");
                DrawTableHeader(3, "Is Visible", "This pair is current loaded");
                DrawTableHeader(4, "Has Data", "This pair has cached data");

                DrawTableHeader(5, "G", "Glamourer");
                DrawTableHeader(6, "P", "Penumbra");
                DrawTableHeader(7, "C", "Customize+");
                DrawTableHeader(8, "H", "Heels");
                DrawTableHeader(9, "H", "Honorific");
                DrawTableHeader(10, "M", "Moodles");
                DrawTableHeader(11, "L", "Loci");
                DrawTableHeader(12, "P", "Pet Nicknames");

                DrawTableHeader(13, "Latest Status", "Most recent application status");
                DrawTableHeader(14, "Latest Status Message", "Most recent application error");

                var clipper = ImGui.ImGuiListClipper();

                try
                {
                    clipper.Begin(filteredPairs.Count);

                    while (clipper.Step())
                    {
                        for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                        {
                            var pair = filteredPairs[index];
                            using var pairId = ImRaii.PushId(pair.UserData.UID);
                            var pairData = pair.GetPairPlayerChanges();
                            var pairDataEvent = _pairDataApplicationEvents[pair];

                            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetTextLineHeightWithSpacing());

                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted(pair.UserData.UID);
                            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            if (ImGui.IsItemClicked())
                            {
                                ImGui.SetClipboardText(pair.UserData.UID);
                            }
                            UiSharedService.AttachToolTip("Click to copy UID to clipboard");

                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted(pair.PlayerName);

                            ImGui.TableSetColumnIndex(2);
                            ImGui.TextUnformatted(pair.LastDataApplicationTime > DateTimeOffset.MinValue ? pair.LastDataApplicationTime.ToLocalTime().ToString("G", CultureInfo.CurrentCulture) : "Never");

                            ImGui.TableSetColumnIndex(3);
                            DrawCheckmark(pair.IsVisible);

                            ImGui.TableSetColumnIndex(4);
                            DrawCheckmark(pair.HasCachedData);

                            ImGui.TableSetColumnIndex(5);
                            DrawCheckmark(pairData[PlayerChanges.Glamourer]);

                            ImGui.TableSetColumnIndex(6);
                            DrawCheckmark(pairData[PlayerChanges.ModFiles]);

                            ImGui.TableSetColumnIndex(7);
                            DrawCheckmark(pairData[PlayerChanges.Customize]);

                            ImGui.TableSetColumnIndex(8);
                            DrawCheckmark(pairData[PlayerChanges.Heels]);

                            ImGui.TableSetColumnIndex(9);
                            DrawCheckmark(pairData[PlayerChanges.Honorific]);

                            ImGui.TableSetColumnIndex(10);
                            DrawCheckmark(pairData[PlayerChanges.Moodles]);

                            ImGui.TableSetColumnIndex(11);
                            DrawCheckmark(pairData[PlayerChanges.Loci]);

                            ImGui.TableSetColumnIndex(12);
                            DrawCheckmark(pairData[PlayerChanges.PetNames]);

                            ImGui.TableSetColumnIndex(13);
                            ImGui.TextUnformatted(pairDataEvent?.ApplicationStatus.ToString() ?? "Unknown");

                            ImGui.TableSetColumnIndex(14);
                            ImGui.TextWrapped(pairDataEvent?.Message.ToString() ?? "Unknown");
                        }
                    }
                }
                finally
                {
                    clipper.End();
                    clipper.Destroy();
                }
            }
            finally
            {
                ImGui.EndTable();
            }
        }
    }

    private void DrawPairApplicationLogs()
    {
        _uiShared.BigText("Pair Data Application Detail");
        ImGuiHelpers.ScaledDummy(2);

        var scale = ImGuiHelpers.GlobalScale;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastUpdate >= UpdateInterval)
        {
            _cachedPairsDetail.RemoveAll(pair => !pair.HasCachedPlayer);
            var cachedUids = _cachedPairsDetail.Select(pair => pair.UserData.UID).ToHashSet(StringComparer.Ordinal);
            foreach (var pair in _pairManager.GetVisiblePairs())
            {
                if (cachedUids.Add(pair.UserData.UID))
                {
                    _cachedPairsDetail.Add(pair);
                }
            }
            _cachedPairsDetail.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.PlayerName ?? string.Empty, right.PlayerName ?? string.Empty));

            _lastUpdate = now;
        }

        ImGui.SetNextItemWidth(400.0f * scale);
        ImGui.InputTextWithHint("##filterPairsDetail", "Filter...", ref _pairFilter, 250);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _pairFilter = string.Empty;
        }

        using (ImRaii.Child("##filterPairsDetailList"))
        {
            var filterText = _pairFilter.Trim();
            var filteredPairs = string.IsNullOrWhiteSpace(filterText) ? _cachedPairsDetail : _cachedPairsDetail.Where(pair => FilterPairs(pair, filterText)).ToList();

            if (filteredPairs.Count == 0)
            {
                ImGuiHelpers.ScaledDummy(2);
                ImGui.TextUnformatted("No Visible Pairs");
            }
            else
            {
                foreach (var pair in filteredPairs)
                {
                    using var pairId = ImRaii.PushId(pair.UserData.UID);

                    UiSharedService.DrawTree(pair.PairUIDName, () =>
                    {
                        foreach (var dataEvent in _eventAggregator.GetAllDataEventsByUid(pair.UserData.UID))
                        {
                            ImGui.TextWrapped(dataEvent.ToLogFormat());
                        }
                    });
                }

                ImGuiHelpers.ScaledDummy(2);
            }
        }
    }

    private void DrawPairTiming()
    {
        _uiShared.BigText("Pair Data Application Timing");
        ImGuiHelpers.ScaledDummy(2);

        using var imGuiId = ImRaii.PushId("pairTimingTable");

        _cachedPairsDetail = _pairManager.GetVisiblePairs().OrderBy(pair => pair.PlayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();

        var scale = ImGuiHelpers.GlobalScale;
        var width = 90.0f * scale;

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.Sortable |
            ImGuiTableFlags.Reorderable |
            ImGuiTableFlags.Hideable |
            ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        var tableSize = new Vector2(0.0f, Math.Max(100.0f * scale, ImGui.GetContentRegionAvail().Y - 50.0f * scale));

        ImGui.SetNextItemWidth(400.0f * scale);
        ImGui.InputTextWithHint("##filterPairs", "Filter...", ref _pairFilter, 250);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _pairFilter = string.Empty;
        }

        var filterText = _pairFilter.Trim();
        var filteredPairs = string.IsNullOrWhiteSpace(filterText) ? _cachedPairsDetail : _cachedPairsDetail.Where(pair => FilterPairs(pair, filterText)).ToList();

        static string FormatTiming(double millis) => $"{millis:F3}ms";

        if (ImGui.BeginTable("##PairTimingTable3", 12, tableFlags, tableSize))
        {
            try
            {
                ImGui.TableSetupScrollFreeze(2, 1);

                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.WidthFixed, 120.0f * scale);
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, 150.0f * scale);
                ImGui.TableSetupColumn("Last Complete Application", ImGuiTableColumnFlags.WidthFixed, 165.0f * scale);

                ImGui.TableSetupColumn("Total Time", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, width);
                ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Penumbra", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Heels", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Honorific", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Moodles", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Loci", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);
                ImGui.TableSetupColumn("Pet Names", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, width);

                ImGui.TableHeadersRow();

                var clipper = ImGui.ImGuiListClipper();

                var sortSpecs = ImGui.TableGetSortSpecs();
                var sortBy = sortSpecs.Specs[0];

                IOrderedEnumerable<Pair> pairOrder = sortBy.ColumnIndex switch
                {
                    0 => filteredPairs.OrderBy(pair => pair.UserData.UID, StringComparer.OrdinalIgnoreCase),
                    1 => filteredPairs.OrderBy(pair => pair.PlayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                    2 => filteredPairs.OrderBy(pair => pair.LastDataApplicationTime),
                    3 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming.TryGetValue(ObjectKind.Player, out var timings) ? timings.Values.Sum() : 0d),
                    4 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Glamourer] ?? 0d),
                    5 => filteredPairs.OrderBy(pair =>
                        (pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.ModFiles] ?? 0d) +
                        (pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.ModManip] ?? 0d)),
                    6 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Customize] ?? 0d),
                    7 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Heels] ?? 0d),
                    8 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Honorific] ?? 0d),
                    9 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Moodles] ?? 0d),
                    10 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.Loci] ?? 0d),
                    11 => filteredPairs.OrderBy(pair => pair.DataApplicationTiming?[ObjectKind.Player][PlayerChanges.PetNames] ?? 0d),
                    _ => filteredPairs.OrderBy(pair => pair.PlayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                };

                filteredPairs = sortBy.SortDirection == ImGuiSortDirection.Descending ? pairOrder.Reverse().ToList() : pairOrder.ToList();

                try
                {
                    clipper.Begin(filteredPairs.Count);

                    while (clipper.Step())
                    {
                        for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                        {
                            var pair = filteredPairs[index];
                            using var pairId = ImRaii.PushId(pair.UserData.UID);
                            var pairTimings = pair.DataApplicationTiming?[ObjectKind.Player];
                            if (pairTimings == null)
                            {
                                continue;
                            }

                            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetTextLineHeightWithSpacing());

                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted(pair.UserData.UID);
                            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            if (ImGui.IsItemClicked())
                            {
                                ImGui.SetClipboardText(pair.UserData.UID);
                            }
                            UiSharedService.AttachToolTip("Click to copy UID to clipboard");

                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted(pair.PlayerName);

                            ImGui.TableSetColumnIndex(2);
                            ImGui.TextUnformatted(pair.LastDataApplicationTime > DateTimeOffset.MinValue ? pair.LastDataApplicationTime.ToLocalTime().ToString("G", CultureInfo.CurrentCulture) : "Never");

                            ImGui.TableSetColumnIndex(3);
                            ImGui.TextUnformatted(FormatTiming(pairTimings.Values.Sum()));

                            ImGui.TableSetColumnIndex(4);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Glamourer]));

                            ImGui.TableSetColumnIndex(5);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.ModFiles] + pairTimings[PlayerChanges.ModManip]));

                            ImGui.TableSetColumnIndex(6);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Customize]));

                            ImGui.TableSetColumnIndex(7);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Heels]));

                            ImGui.TableSetColumnIndex(8);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Honorific]));

                            ImGui.TableSetColumnIndex(9);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Moodles]));

                            ImGui.TableSetColumnIndex(10);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.Loci]));

                            ImGui.TableSetColumnIndex(11);
                            ImGui.TextUnformatted(FormatTiming(pairTimings[PlayerChanges.PetNames]));
                        }
                    }
                }
                finally
                {
                    clipper.End();
                    clipper.Destroy();
                }
            }
            finally
            {
                ImGui.EndTable();
            }
        }
    }

    private void DrawPairDataDetail()
    {
        _uiShared.BigText("Pair Data Detail");
        ImGuiHelpers.ScaledDummy(2);

        if (_mareConfigService.Current.FilterMods)
        {
            UiSharedService.ColorText("You have mod filtering enabled.", ImGuiColors.DalamudYellow);
        }
        ImGui.SetNextItemWidth(200.0f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##pairDataDetail", "Enter Pair UID or Alias", ref _pairUidForDataTask, 250);
        ImGui.SameLine();
        if (ImGui.Button("Search"))
        {
            _ = Task.Run(async () =>
            {
                if (_pairDataDetailTask != null && !(_pairDataDetailTask.IsCompleted || _pairDataDetailTask.IsCanceled))
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(_pairUidForDataTask))
                {
                    _pairDataDetailTask?.Dispose();
                    _pairDataDetailTask = null;
                    _pairDataResult = null;

                    return;
                }

                _pairDataDetailTask = PairDataDetailTask(_pairDataDetailCt.Token);
                _pairDataResult = null;
                _pairDataResult = await _pairDataDetailTask.ConfigureAwait(false);

            }, _pairDataDetailCt.Token);
        }

        if (_pairDataDetailTask != null && !_pairDataDetailTask.IsCompleted)
        {
            ImGuiHelpers.ScaledDummy(5f);
            ImGui.TextUnformatted("Running..."u8);
        }
        if (_pairDataDetailTask != null && _pairDataDetailTask.IsCompleted && _pairDataResult == null)
        {
            ImGuiHelpers.ScaledDummy(5f);
            ImGui.TextUnformatted("Pair is offline, or no Pair by this Alias/UID was found!"u8);
        }
        if (_pairDataResult != null)
        {
            using (ImRaii.Child("##pairDataResults"))
            {
                var pair = _pairDataResult.Pair;
                var playerChanges = _pairDataResult.PlayerChanges;

                if (pair.IsPaused)
                {
                    ImGuiHelpers.ScaledDummy(5f);
                    ImGui.TextUnformatted($"You have paused this pair with reason: {pair.GetPauseReason()}");

                    return;
                }
                if (!pair.HasCachedData)
                {
                    ImGuiHelpers.ScaledDummy(5f);
                    ImGui.TextUnformatted("This pair has not yet beed loaded."u8);

                    return;
                }

                ImGuiHelpers.ScaledDummy(5f);

                _uiShared.HeaderText("Data");
                ImGui.TextUnformatted($"Data Hash: {_pairDataResult.CharacterData?.DataHash.Value ?? string.Empty}");
                ImGui.TextUnformatted($"Pair has Penumbra data: {playerChanges?[PlayerChanges.ModFiles] ?? false}");
                ImGui.TextUnformatted($"Pair has Glamourer data: {playerChanges?[PlayerChanges.Glamourer] ?? false}");
                ImGui.TextUnformatted($"Pair has Customize+ data: {playerChanges?[PlayerChanges.Customize] ?? false}");
                ImGui.TextUnformatted($"Pair has Heels data: {playerChanges?[PlayerChanges.Heels] ?? false}");

                ImGuiHelpers.ScaledDummy(2f);

                ImGui.TextUnformatted($"Player load time: {pair.DataApplicationTiming?[ObjectKind.Player].Values.Sum() ?? 0d:F3}ms");
                ImGui.TextUnformatted($"Minion/Mount load time: {pair.DataApplicationTiming?[ObjectKind.MinionOrMount].Values.Sum() ?? 0d:F3}ms");
                ImGui.TextUnformatted($"Pet load time: {pair.DataApplicationTiming?[ObjectKind.Pet].Values.Sum() ?? 0d:F3}ms");

                ImGuiHelpers.ScaledDummy(2f);

                ImGui.TextUnformatted($"Pair data size: {pair.LastAppliedDataBytes.ToByteString()}");
                ImGui.TextUnformatted($"Pair VRAM: {pair.LastAppliedApproximateVRAMBytes.ToByteString()}");
                ImGui.TextUnformatted($"Pair Triangles: {pair.LastAppliedDataTris.ToByteString()}");
                ImGui.TextUnformatted($"Pair total files: {_pairDataResult.CharacterData?.FileReplacements[ObjectKind.Player].Count ?? 0}");
                ImGui.TextUnformatted($"Pair compressed alternative files: {pair.LastAppliedCompressedAlternates}");

                int missingFileCount = pair.MissingFiles.Count;
                ImGui.TextUnformatted($"Missing files count: {missingFileCount}");
                if (missingFileCount > 0)
                {
                    ImGuiHelpers.ScaledDummy(2f);
                    foreach (var missingFileReplacments in pair.MissingFiles)
                    {
                        ImGui.TextUnformatted($"Hash: {missingFileReplacments.Key}");
                        if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsItemClicked())
                        {
                            ImGui.SetClipboardText(missingFileReplacments.Key);
                        }
                        UiSharedService.AttachToolTip("Click to copy to clipboard");
                        ImGui.SameLine();
                        ImGui.TextUnformatted($" Path(s): {missingFileReplacments.Value}");
                    }
                }

                ImGuiHelpers.ScaledDummy(5f);
                _uiShared.HeaderText("Permissions");

                var yourPerms = pair.UserPair.OwnPermissions;
                var theirPerms = pair.UserPair.OtherPermissions;
                ImGui.TextUnformatted($"You have their animations paused: {yourPerms.IsDisableAnimations()}");
                ImGui.TextUnformatted($"You have their sounds paused: {yourPerms.IsDisableSounds()}");
                ImGui.TextUnformatted($"You have their vfx paused: {yourPerms.IsDisableVFX()}");

                ImGuiHelpers.ScaledDummy(2);

                ImGui.TextUnformatted($"They have your animations paused: {theirPerms.IsDisableAnimations()}");
                ImGui.TextUnformatted($"They have your sounds paused: {theirPerms.IsDisableSounds()}");
                ImGui.TextUnformatted($"They have your vfx paused: {theirPerms.IsDisableVFX()}");

                ImGuiHelpers.ScaledDummy(2);
            }
        }
    }

    private bool FilterPairs(Pair pair, string filterText)
    {
        _pairDataApplicationEvents.TryGetValue(pair, out var latestEvent);

        var status = latestEvent?.ApplicationStatus.ToString() ?? "Unknown";
        var message = latestEvent?.Message ?? "Unknown";

        return pair.HasCachedPlayer
            && ((!string.IsNullOrEmpty(pair.UserData.UID) && pair.UserData.UID.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(pair.UserData.AliasOrUID) && pair.UserData.AliasOrUID.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(pair.PlayerName) && pair.PlayerName.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                || status.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                || message.Contains(filterText, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<PairDataResult?> PairDataDetailTask(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        PairDataResult? returnData = null;

        var pair = _pairManager.GetPairByAliasOrUID(_pairUidForDataTask);
        if (!(pair == null || !pair.IsOnline))
        {
            if (pair.IsPaused)
            {
                returnData = new(pair, null, null, null);
            }
            else
            {
                var pairData = pair.GetCopyOfPairCharacterData();
                var playerChanges = pair.GetPairPlayerChanges();
                Dictionary<ObjectKind, List<FileReplacementData>>? missingFiles = null;
                if (pair.HasCachedData)
                {
                    missingFiles = await pair.TryGetMissingFileReplacementData().ConfigureAwait(false);
                }

                returnData = new(pair, playerChanges, missingFiles, pairData);
            }
        }

        return returnData;
    }
}
