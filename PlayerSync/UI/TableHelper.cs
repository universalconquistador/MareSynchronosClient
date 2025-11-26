using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace MareSynchronos.UI
{
    public static class TableHelper
    {
        /// <summary>
        /// multiple uses,padding, centering or just text
        /// </summary>
        /// <param name="text"></param>
        /// <param name="centerHorizontally"></param>
        /// <param name="leftPadding"></param>
        public static void CText(string text, bool centerHorizontally = true, float leftPadding = 10f)
        {
            float cellWidth = ImGui.GetColumnWidth();
            Vector2 textSize = ImGui.CalcTextSize(text);

            if (centerHorizontally)
            {
                float indent = (cellWidth - textSize.X) * 0.5f;
                if (indent > 0)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
            }
            else if (leftPadding > 0f)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + leftPadding);
            }

            ImGui.Text(text);
        }

        /// <summary>
        /// table centering text in column with color no padding varient wasnt needed but can be added later
        /// </summary>
        /// <param name="text"></param>
        /// <param name="color"></param>
        public static void CCText(string text, Vector4 color)
        {
            Vector2 textSize = ImGui.CalcTextSize(text);
            float cellWidth = ImGui.GetColumnWidth();
            float indent = (cellWidth - textSize.X) * 0.5f;

            if (indent > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);

            UiSharedService.ColorText(text, color);
        }

        /// <summary>
        /// returns true/false if a row is being hovered
        /// </summary>
        /// <returns></returns>
        public static bool SRowhovered(float rowHeightStart, float rowHeightEnd)
        {
            if (!IsMouseWithinWindow()) return false;
            float smousepos = GetMousePosInWindow().Y;
            return rowHeightStart < smousepos && rowHeightEnd >= smousepos;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static float SRowheight()
        {
            int ScolumncountMax2 = ImGui.TableGetColumnCount();

            float rowheight2 = 0f;
            for (int Scol2 = 0; Scol2 < ScolumncountMax2; Scol2++)
            {
                ImGui.TableSetColumnIndex(Scol2);
                float cellheight2 = ImGui.GetItemRectSize().Y;
                rowheight2 = Math.Max(rowheight2, cellheight2);
            }
            rowheight2 += ImGui.GetStyle().CellPadding.Y * 2;
            return rowheight2;
        }

        public static bool IsMouseWithinWindow()
        {
            Vector2 smousepos = ImGui.GetMousePos();
            Vector2 swindowpos = ImGui.GetWindowPos();
            Vector2 swindowsize = ImGui.GetWindowSize();

            bool Smouseinwindow =
                smousepos.X >= swindowpos.X && smousepos.X < swindowpos.X + swindowsize.X &&
                smousepos.Y >= swindowpos.Y && smousepos.Y < swindowpos.Y + swindowsize.Y;

            return Smouseinwindow;
        }

        public static Vector2 GetMousePosInWindow()
        {
            var mouseScreen = ImGui.GetMousePos();   // screen space
            var winPos = ImGui.GetWindowPos();  // screen space
            return mouseScreen - winPos;             // window-local space
        }

        public static Vector2 GetMousePosInContent()
        {
            var mouseScreen = ImGui.GetMousePos();                     // screen space
            var winPos = ImGui.GetWindowPos();                    // screen space
            var contentMin = ImGui.GetWindowContentRegionMin();       // window-local
            var contentTopLeftScreen = winPos + contentMin;            // screen space
            return (mouseScreen - contentTopLeftScreen)
                   + new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
        }

    }
 
}