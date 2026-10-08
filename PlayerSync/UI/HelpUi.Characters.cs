using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class HelpUi : WindowMediatorSubscriberBase
{
    private UiNav.Tab<DiagnosticCharacterTabs>? _selectedTabDiagnosticCharacter;

    private IReadOnlyList<UiNav.Tab<DiagnosticCharacterTabs>>? _diagnosticCharacterTab;
    private IReadOnlyList<UiNav.Tab<DiagnosticCharacterTabs>> DiagnosticCharacterTabsList => _diagnosticCharacterTab ??=
    [
        new(DiagnosticCharacterTabs.Troubleshooting, "Troubleshooting", DrawCharacterTroubleshooting),
        new(DiagnosticCharacterTabs.CharacterData, "Character Data", DrawCharacterData),
    ];

    private enum DiagnosticCharacterTabs
    {
        Troubleshooting,
        CharacterData
    }

    private void DrawDiagnosticsCharacter()
    {
        _selectedTabDiagnosticCharacter = UiNav.DrawTabsUnderline(_theme, DiagnosticCharacterTabsList, _selectedTabDiagnosticCharacter, _uiShared.IconFont);

        using var child = ImRaii.Child("##diagCharacterPanel", new Vector2(0, 0), false);

        _selectedTabDiagnosticCharacter.TabAction.Invoke();
    }

    private void DrawCharacterTroubleshooting()
    {
        return;
    }

    private void DrawCharacterData()
    {
        return;
    }
}
