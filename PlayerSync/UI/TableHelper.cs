using Dalamud.Bindings.ImGui;
using MareSynchronos.UI;
using System.Numerics;

namespace MyTableHelper
{

    public class Thelper
    {
        //multiple uses,padding, centering or just text
        public void CText(string text, bool centerHorizontally = true, float leftPadding = 10f)
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

        //table centering text in column with color no padding varient wasnt needed but can be added later
        public void CCText(string text, Vector4 color)
        {
            Vector2 textSize = ImGui.CalcTextSize(text);
            float cellWidth = ImGui.GetColumnWidth();
            float indent = (cellWidth - textSize.X) * 0.5f;

            if (indent > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);

            UiSharedService.ColorText(text, color);
        }
        public static bool SRowhovered()
        {
            Vector2 rowstart = ImGui.GetCursorScreenPos();
            float rowheight = 0f;

            int ScolumncountMax = ImGui.TableGetColumnCount();

            for (int Scol = 0; Scol < ScolumncountMax; Scol++)
            {
                ImGui.TableSetColumnIndex(Scol);
                float cellheight = ImGui.GetItemRectSize().Y;
                rowheight = Math.Max(rowheight, cellheight);
            }

            float paddingY = ImGui.GetStyle().FramePadding.Y;
            rowheight += paddingY * 4;
            rowstart.Y += paddingY * 2;

            Vector2 mousepos = ImGui.GetMousePos();
            return mousepos.Y >= rowstart.Y && mousepos.Y < rowstart.Y + rowheight;
        }
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
    }
}