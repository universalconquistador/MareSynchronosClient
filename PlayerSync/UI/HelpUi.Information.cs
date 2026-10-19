using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Models;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class HelpUi : WindowMediatorSubscriberBase
{
    private void DrawDiagnosticsInformation()
    {
        // have the hash you sent and who it went to
        // last data generated time
        // most current data of each type
        // history of character data that expands by type
        return;
    }
}
