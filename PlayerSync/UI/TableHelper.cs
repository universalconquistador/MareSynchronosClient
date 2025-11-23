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
    }
}