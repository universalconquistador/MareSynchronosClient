using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
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

    public PlayerAnalysisViewerUI(ILogger<PlayerAnalysisViewerUI> logger, MareMediator mediator, PerformanceCollectorService performanceCollector,
        UiSharedService uiSharedService, PairManager pairManager, PlayerPerformanceConfigService playerPerformanceConfigService, ApiController apiController)
        : base(logger, mediator, "Player Analysis Viewer", performanceCollector)
    { 
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _playerPerformanceConfig = playerPerformanceConfigService;
        _apiController = apiController;
        SizeConstraints = new()
        {
            MinimumSize = new(600, 500),
            MaximumSize = new(1000, 2000)
        };
        Mediator.Subscribe<OpenPlayerAnalysisViewerUIUiMessage>(this, (_) => Toggle());
    }

    protected override void DrawInternal()
    {
        ImmutableList<Pair> ImmutablePairList(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u)
            => u.Select(k => k.Key).ToImmutableList();
        var allPairs = _pairManager.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
        bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsVisible;
        var allVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));

        _uiSharedService.BigText("Visible Players (" + allVisiblePairs.Count().ToString() + ")");
        ImGuiHelpers.ScaledDummy(2f);

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
                if (pair.LastAppliedDataTris > 0)
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
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (ImGui.Button($"Pause##{pair.UserData.UID}"))
                {
                    _ = _apiController.PauseAsync(pair.UserData);
                }
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
}
