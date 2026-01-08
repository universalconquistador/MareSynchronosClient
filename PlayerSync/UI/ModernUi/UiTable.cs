using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MareSynchronos.UI.ModernUi;

public static class UiTable
{
    public enum WidthMode
    {
        Stretch,
        FixedPx,
        FixedPercent,
    }

    public enum SortDirection
    {
        Asc,
        Desc,
    }

    /// <summary>
    /// UI state holder for column sorting. Keep one instance per table to preserve user choice.
    /// </summary>
    public sealed class SortState
    {
        public string? ColumnId { get; private set; }
        public bool Ascending { get; private set; } = true;
        public bool HasUserChoice { get; private set; }

        public void EnsureDefault(string columnId, SortDirection direction)
        {
            if (HasUserChoice || ColumnId != null) return;
            ColumnId = columnId;
            Ascending = direction == SortDirection.Asc;
        }

        public void ToggleOrSet(string columnId, SortDirection defaultDirection)
        {
            if (string.Equals(ColumnId, columnId, StringComparison.Ordinal))
            {
                Ascending = !Ascending;
                HasUserChoice = true;
                return;
            }

            ColumnId = columnId;
            Ascending = defaultDirection == SortDirection.Asc;
            HasUserChoice = true;
        }

        public void Clear()
        {
            ColumnId = null;
            Ascending = true;
            HasUserChoice = false;
        }
    }

    public sealed record CellStyle(Vector4? TextColor = null, uint? BgColorU32 = null);
    public sealed record RowStyle(uint? BgColorU32 = null);

    public sealed record RowOptions(int SelectedIndex = -1, bool Selectable = false,
        Action<int>? OnRowClicked = null, Func<int, RowStyle?>? RowStyle = null, bool HoverHighlight = true);

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Id"></param>
    /// <param name="Header"></param>
    /// <param name="WidthMode"></param>
    /// <param name="WidthValue"></param>
    /// <param name="Justify"></param>
    /// <param name="Text"></param>
    /// <param name="Draw"></param>
    /// <param name="Style"></param>
    /// <param name="Sortable"></param>
    /// <param name="SortKey"></param>
    /// <param name="SortComparer"></param>
    /// <param name="DefaultSort"></param>
    /// <param name="DefaultSortDirection"></param>
    public sealed record ColumnSpec<T>(string Id, string Header, WidthMode WidthMode = WidthMode.Stretch, float WidthValue = 0f,
        Ui.Justify Justify = Ui.Justify.Left, Func<T, string>? Text = null, Action<T>? Draw = null, Func<T, CellStyle?>? Style = null, bool Sortable = false,
        Func<T, IComparable?>? SortKey = null, Comparison<T>? SortComparer = null, bool DefaultSort = false, SortDirection DefaultSortDirection = SortDirection.Asc);

    private static int CompareComparable(IComparable? a, IComparable? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        try { return a.CompareTo(b); }
        catch { return string.CompareOrdinal(a.ToString(), b.ToString()); }
    }

    private static bool ApplySortingDefaults<T>(IReadOnlyList<ColumnSpec<T>> columns, SortState sort)
    {
        if (sort.HasUserChoice || sort.ColumnId != null)
            return false;

        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            if (!c.DefaultSort) continue;
            if (!c.Sortable) continue;
            if (c.SortKey == null && c.SortComparer == null) continue;

            sort.EnsureDefault(c.Id, c.DefaultSortDirection);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<T> SortRowsIfNeeded<T>(IReadOnlyList<T> rows, IReadOnlyList<ColumnSpec<T>> columns, SortState sort)
    {
        if (sort.ColumnId == null) return rows;

        ColumnSpec<T>? col = null;
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].Id, sort.ColumnId, StringComparison.Ordinal))
            {
                col = columns[i];
                break;
            }
        }

        if (col == null || !col.Sortable || (col.SortKey == null && col.SortComparer == null))
            return rows;

        var sorted = new List<T>(rows);

        if (col.SortComparer != null)
        {
            sorted.Sort((a, b) =>
            {
                var cmp = col.SortComparer(a, b);
                return sort.Ascending ? cmp : -cmp;
            });
            return sorted;
        }

        var keyFn = col.SortKey!;
        sorted.Sort((a, b) =>
        {
            var cmp = CompareComparable(keyFn(a), keyFn(b));
            return sort.Ascending ? cmp : -cmp;
        });

        return sorted;
    }

    private static void DrawSortableHeaders<T>(UiTheme t, IReadOnlyList<ColumnSpec<T>> columns, SortState sort)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            ImGui.TableSetColumnIndex(i);

            var isSortable = c.Sortable && (c.SortKey != null || c.SortComparer != null);
            var isActive = isSortable && string.Equals(sort.ColumnId, c.Id, StringComparison.Ordinal);
            var arrow = isActive ? (sort.Ascending ? " ▲" : " ▼") : string.Empty;
            var label = string.IsNullOrEmpty(c.Header) ? string.Empty : c.Header + arrow;

            if (!isSortable)
            {
                ImGui.TextUnformatted(c.Header);
                continue;
            }

            var avail = ImGui.GetContentRegionAvail();
            using var id = ImRaii.PushId($"hdr_{c.Id}");

            if (ImGui.Selectable(label, isActive, ImGuiSelectableFlags.None, new Vector2(avail.X, 0)))
            {
                sort.ToggleOrSet(c.Id, c.DefaultSortDirection);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="t"></param>
    /// <param name="id"></param>
    /// <param name="columns"></param>
    /// <param name="rows"></param>
    /// <param name="rowOptions"></param>
    /// <param name="flags"></param>
    /// <param name="sort"></param>
    /// <returns></returns>
    public static bool Draw<T>(UiTheme t, string id, IReadOnlyList<ColumnSpec<T>> columns, IReadOnlyList<T> rows, RowOptions? rowOptions = null,
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, SortState? sort = null)
    {
        if (columns.Count == 0) return false;

        rowOptions ??= new RowOptions();

        if (sort != null)
        {
            ApplySortingDefaults(columns, sort);
            rows = SortRowsIfNeeded(rows, columns, sort);
        }

        using var table = ImRaii.Table(id, columns.Count, flags, new Vector2(0, 0));
        if (!table) return false;

        ImGui.TableSetupScrollFreeze(0, 1);

        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var colFlags = c.WidthMode switch
            {
                WidthMode.Stretch => ImGuiTableColumnFlags.WidthStretch,
                _ => ImGuiTableColumnFlags.WidthFixed,
            };

            var w = c.WidthMode switch
            {
                WidthMode.FixedPx => c.WidthValue,
                WidthMode.FixedPercent => MathF.Max(0f, c.WidthValue) * MathF.Max(1f, ImGui.GetContentRegionAvail().X),
                _ => MathF.Max(0f, c.WidthValue),
            };
            ImGui.TableSetupColumn(c.Header, colFlags, w);
        }

        if (sort != null)
            DrawSortableHeaders(t, columns, sort);
        else
            ImGui.TableHeadersRow();

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            ImGui.TableNextRow();

            var rs = rowOptions.RowStyle?.Invoke(r);
            if (rs?.BgColorU32 is uint rowBg)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, rowBg);

            if (rowOptions.Selectable)
            {
                ImGui.TableSetColumnIndex(0);
                var selected = rowOptions.SelectedIndex == r;
                var sFlags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap;
                ImGui.Selectable($"##row_{id}_{r}", selected, sFlags, new Vector2(0, ImGui.GetFrameHeight()));
                ImGui.SetItemAllowOverlap();

                if (rowOptions.HoverHighlight && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                {
                    var idx = ImGui.TableGetRowIndex();
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(t.HoverOverlay), idx);
                }

                if (rowOptions.OnRowClicked != null && ImGui.IsItemClicked())
                    rowOptions.OnRowClicked(r);
            }

            for (var c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                ImGui.TableSetColumnIndex(c);

                var cs = col.Style?.Invoke(row);
                ImRaii.Color? tc = null;
                try
                {
                    if (cs?.TextColor is Vector4 fg)
                        tc = ImRaii.PushColor(ImGuiCol.Text, fg);

                    if (cs?.BgColorU32 is uint bg)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, bg);

                    if (col.Draw != null)
                        col.Draw(row);
                    else
                        Ui.TextJustified(col.Text?.Invoke(row) ?? string.Empty, col.Justify);
                }
                finally
                {
                    tc?.Dispose();
                }
            }
        }

        return true;
    }
}
