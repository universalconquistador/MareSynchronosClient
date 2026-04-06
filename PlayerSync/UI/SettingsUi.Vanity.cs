using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Validators;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.UI.ModernUi;
using System.Numerics;


namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<VanityTabs>? _selectedTabVanity;

    private IReadOnlyList<UiNav.Tab<VanityTabs>>? _vanityTabs;
    private IReadOnlyList<UiNav.Tab<VanityTabs>> VanityTabsList => _vanityTabs ??=
    [
        new(VanityTabs.Vanity, "Vanity", DrawServiceVanity),
    ];

    private enum VanityTabs
    {
        Vanity
    }

    private void DrawVanitySettings()
    {
        _lastTab = "Service";

        _selectedTabVanity = UiNav.DrawTabsUnderline(_theme, VanityTabsList, _selectedTabVanity, _uiShared.IconFont);

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabVanity.TabAction.Invoke();
    }

    private void DrawServiceVanity()
    {
        _lastTab = "Vanity";

        if (_currentServer != _serverConfigurationManager.CurrentServerIndex || !_apiController.IsConnected)
        {
            _currentServer = _serverConfigurationManager.CurrentServerIndex;
            _accountInfo = null;
            _retrieveAccountInfoTask = null;

            _selectedUid = null;
            _selectedSyncshellGid = null;
            _selectedSyncshell = null;

            _selectedAliasText = string.Empty;
            _selectedAliasCurrent = string.Empty;
            _selectedSyncshellAliasText = string.Empty;
            _selectedSyncshellAliasCurrent = string.Empty;
            _uidResult = null;
            _groupResult = null;

            if (!_apiController.IsConnected)
            {
                _accountInfo = null;
                ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "You must be connected to the service to use this feature.");
                return;
            }
        }

        _uiShared.BigText("Vanity");
        ImGuiHelpers.ScaledDummy(2);

        _uiShared.HeaderText("Update Vanity IDs/Alias");

        if (ImGui.Button("Retrieve Account Info"))
        {
            _retrieveAccountInfoTask = _apiController.GetAccountInfo();
            _accountInfo = null;
        }

        ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "Vanity IDs/Alias must be 5-15 characters, underscore, dash.");

        ImGuiHelpers.ScaledDummy(2);

        if (_retrieveAccountInfoTask is { IsCompleted: true } && _accountInfo == null)
        {
            if (_retrieveAccountInfoTask.IsFaulted)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, _retrieveAccountInfoTask.Exception?.GetBaseException().Message ?? "Failed.");
            }
            else
            {
                _accountInfo = _retrieveAccountInfoTask.Result;
            }
        }

        if (_accountInfo == null)
            return;

        float itemWidth = ImGui.GetContentRegionAvail().X / 2f;

        // UIDs
        var uids = _accountInfo.AccountUids.OrderByDescending(userData => string.Equals(userData.UID, _apiController.UID, StringComparison.OrdinalIgnoreCase))
            .ThenBy(userData => userData.UID, StringComparer.Ordinal).ToList();

        if (uids.Count > 0)
        {
            static string UidLabel(UserData userData) => $"{userData.UID} ({(string.IsNullOrWhiteSpace(userData.Alias) ? "None" : userData.Alias)})";

            UserData? selectedUidEntry = uids.FirstOrDefault(userData => string.Equals(userData.UID, _selectedUid, StringComparison.OrdinalIgnoreCase));

            bool uidSelectionChanged = false;
            if (selectedUidEntry == null)
            {
                selectedUidEntry = uids[0];
                _selectedUid = selectedUidEntry.UID;
                uidSelectionChanged = true;
            }

            string serverUidAlias = selectedUidEntry.Alias ?? "";
            if (uidSelectionChanged || string.Equals(_selectedAliasText, _selectedAliasCurrent, StringComparison.OrdinalIgnoreCase))
            {
                _selectedAliasText = serverUidAlias;
                _selectedAliasCurrent = serverUidAlias;
            }

            ImGui.SetNextItemWidth(itemWidth);
            if (ImGui.BeginCombo("UID##uid_combo", UidLabel(selectedUidEntry)))
            {
                foreach (var uidEntry in uids)
                {
                    bool selected = string.Equals(uidEntry.UID, _selectedUid, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(UidLabel(uidEntry), selected))
                    {
                        _selectedUid = uidEntry.UID;
                        selectedUidEntry = uidEntry;
                        _selectedAliasText = uidEntry.Alias ?? "";
                        _selectedAliasCurrent = uidEntry.Alias ?? "";
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.SetNextItemWidth(itemWidth);
            ImGui.InputText("Vanity/Alias##uid_alias", ref _selectedAliasText, 15);

            bool canUpdate = !string.Equals(_selectedAliasText, _selectedAliasCurrent, StringComparison.OrdinalIgnoreCase) 
                && AliasValidator.IsValidAlias(_selectedAliasText);

            using (ImRaii.Disabled(!canUpdate || (_uidUpdateTask is { IsCompleted: false })))
            {
                if (ImGui.Button("Update UID##uid_update"))
                {
                    _uidResult = null;
                    _groupResult = null;
                    _uidUpdateTask = _apiController.UpdateAlias(userData: new(selectedUidEntry.UID, _selectedAliasText));
                }
            }

            if (_uidUpdateTask is { IsCompleted: true })
            {
                if (_uidUpdateTask.IsFaulted)
                {
                    var ex = _uidUpdateTask.Exception?.GetBaseException();
                    _uidResult = (false, ex?.Message ?? "Update failed.");
                }
                else
                {
                    _uidResult = _uidUpdateTask.Result;
                    if (_uidResult.Value.ok)
                        _selectedAliasCurrent = _selectedAliasText;

                    _retrieveAccountInfoTask = _apiController.GetAccountInfo();
                    _accountInfo = null;
                }

                _uidUpdateTask = null;
            }

            if (_uidResult != null)
            {
                var color = _uidResult.Value.ok ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
                ImGui.TextColoredWrapped(color, _uidResult.Value.msg);
            }
        }

        ImGuiHelpers.ScaledDummy(2);

        // Syncshells
        var groups = _accountInfo?.AccountGroups.OrderBy(groupData => groupData.GID, StringComparer.OrdinalIgnoreCase).ToList();

        if (groups != null && groups.Count > 0)
        {
            static string GroupLabel(GroupData groupData)
                => $"{groupData.GID} ({(string.IsNullOrWhiteSpace(groupData.Alias) ? "None" : groupData.Alias)})";

            GroupData? selectedGroupEntry = groups.FirstOrDefault(groupData => string.Equals(groupData.GID, _selectedSyncshellGid, StringComparison.OrdinalIgnoreCase));

            bool groupSelectionChanged = false;
            if (selectedGroupEntry == null)
            {
                selectedGroupEntry = groups[0];
                _selectedSyncshellGid = selectedGroupEntry.GID;
                groupSelectionChanged = true;
            }

            _selectedSyncshell = selectedGroupEntry;

            string serverGroupAlias = selectedGroupEntry.Alias ?? "";
            if (groupSelectionChanged || string.Equals(_selectedSyncshellAliasText, _selectedSyncshellAliasCurrent, StringComparison.OrdinalIgnoreCase))
            {
                _selectedSyncshellAliasText = serverGroupAlias;
                _selectedSyncshellAliasCurrent = serverGroupAlias;
            }

            ImGui.SetNextItemWidth(itemWidth);
            if (ImGui.BeginCombo("Syncshell##ss_combo", GroupLabel(selectedGroupEntry)))
            {
                foreach (var groupEntry in groups)
                {
                    bool selected = string.Equals(groupEntry.GID, _selectedSyncshellGid, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(GroupLabel(groupEntry), selected))
                    {
                        _selectedSyncshellGid = groupEntry.GID;
                        _selectedSyncshell = groupEntry;
                        selectedGroupEntry = groupEntry;
                        _selectedSyncshellAliasText = groupEntry.Alias ?? "";
                        _selectedSyncshellAliasCurrent = groupEntry.Alias ?? "";
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.SetNextItemWidth(itemWidth);
            ImGui.InputText("Vanity/Alias##ss_alias", ref _selectedSyncshellAliasText, 15);

            bool canUpdateGroup = selectedGroupEntry != null 
                && !string.Equals(_selectedSyncshellAliasText, _selectedSyncshellAliasCurrent, StringComparison.OrdinalIgnoreCase) 
                && AliasValidator.IsValidAlias(_selectedSyncshellAliasText);

            using (ImRaii.Disabled(!canUpdateGroup || (_groupUpdateTask is { IsCompleted: false })))
            {
                if (ImGui.Button("Update Syncshell##ss_update"))
                {
                    _uidResult = null;
                    _groupResult = null;
                    _groupUpdateTask = _apiController.UpdateAlias(groupData: new(selectedGroupEntry!.GID, _selectedSyncshellAliasText));
                }
            }

            if (_groupUpdateTask is { IsCompleted: true })
            {
                if (_groupUpdateTask.IsFaulted)
                {
                    var ex = _groupUpdateTask.Exception?.GetBaseException();
                    _groupResult = (false, ex?.Message ?? "Update failed.");
                }
                else
                {
                    _groupResult = _groupUpdateTask.Result;
                    if (_groupResult.Value.ok)
                        _selectedSyncshellAliasCurrent = _selectedSyncshellAliasText;

                    _retrieveAccountInfoTask = _apiController.GetAccountInfo();
                    _accountInfo = null;
                }

                _groupUpdateTask = null;
            }

            if (_groupResult != null)
            {
                var color = _groupResult.Value.ok ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
                ImGui.TextColoredWrapped(color, _groupResult.Value.msg);
            }
        }
    }
}
