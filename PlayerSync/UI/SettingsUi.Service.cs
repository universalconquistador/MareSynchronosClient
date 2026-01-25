using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Routes;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.AspNetCore.Http.Connections;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<ServiceTabs>? _selectedTabService;

    private enum ServiceTabs
    {
        Service,
        Permissions,
        Account
    }

    private void DrawServiceSettings()
    {
        _lastTab = "Service";

        var theme = UiTheme.Default;
        _selectedTabService = UiNav.DrawTabsUnderline(theme,
            [
            new(ServiceTabs.Service, "Service", DrawService),
            new(ServiceTabs.Permissions, "Permissions", DrawServicePermissions),
            new(ServiceTabs.Account, "Account", DrawServiceAccount),
            ],
            _selectedTabService, 
            _uiShared.IconFont);

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabService.TabAction.Invoke();
    }

    private int _lastSelectedServerIndex = -1;
    private Task<(bool Success, bool PartialSuccess, string Result)>? _secretKeysConversionTask = null;
    private CancellationTokenSource _secretKeysConversionCts = new CancellationTokenSource();
    private ServerStorage _selectedServer = null;
    private async Task<(bool Success, bool partialSuccess, string Result)> ConvertSecretKeysToUIDs(ServerStorage serverStorage, CancellationToken token)
    {
        List<Authentication> failedConversions = serverStorage.Authentications.Where(u => u.SecretKeyIdx == -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> conversionsToAttempt = serverStorage.Authentications.Where(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> successfulConversions = [];
        Dictionary<string, List<Authentication>> secretKeyMapping = new(StringComparer.Ordinal);
        foreach (var authEntry in conversionsToAttempt)
        {
            if (!serverStorage.SecretKeys.TryGetValue(authEntry.SecretKeyIdx, out var secretKey))
            {
                failedConversions.Add(authEntry);
                continue;
            }

            if (!secretKeyMapping.TryGetValue(secretKey.Key, out List<Authentication>? authList))
            {
                secretKeyMapping[secretKey.Key] = authList = [];
            }

            authList.Add(authEntry);
        }

        if (secretKeyMapping.Count == 0)
        {
            return (false, false, $"Failed to convert {failedConversions.Count} entries: " + string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        var baseUri = serverStorage.ServerUri.Replace("wss://", "https://").Replace("ws://", "http://");
        var oauthCheckUri = MareAuth.GetUIDsBasedOnSecretKeyFullPath(new Uri(baseUri));
        var requestContent = JsonContent.Create(secretKeyMapping.Select(k => k.Key).ToList());
        HttpRequestMessage requestMessage = new(HttpMethod.Post, oauthCheckUri);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverStorage.OAuthToken);
        requestMessage.Content = requestContent;

        using var response = await _httpClient.SendAsync(requestMessage, token).ConfigureAwait(false);
        Dictionary<string, string>? secretKeyUidMapping = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>
            (await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
        if (secretKeyUidMapping == null)
        {
            return (false, false, $"Failed to parse the server response. Failed to convert all entries.");
        }

        foreach (var entry in secretKeyMapping)
        {
            if (!secretKeyUidMapping.TryGetValue(entry.Key, out var assignedUid) || string.IsNullOrEmpty(assignedUid))
            {
                failedConversions.AddRange(entry.Value);
                continue;
            }

            foreach (var auth in entry.Value)
            {
                auth.UID = assignedUid;
                successfulConversions.Add(auth);
            }
        }

        if (successfulConversions.Count > 0)
            _serverConfigurationManager.Save();

        StringBuilder sb = new();
        sb.Append("Conversion complete." + Environment.NewLine);
        sb.Append($"Successfully converted {successfulConversions.Count} entries." + Environment.NewLine);
        if (failedConversions.Count > 0)
        {
            sb.Append($"Failed to convert {failedConversions.Count} entries, assign those manually: ");
            sb.Append(string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        return (true, failedConversions.Count != 0, sb.ToString());
    }

    private void DrawService()
    {
        _lastTab = "Service";

        _uiShared.BigText("Service");
        ImGuiHelpers.ScaledDummy(2);
        var useBackupServer = _serverConfigurationManager.EnableBackupServer;
        if (ImGui.Checkbox("Use Proxied Server", ref useBackupServer))
        {
            _serverConfigurationManager.EnableBackupServer = useBackupServer;
            _ = _apiController.CreateConnectionsAsync();
        }
        _uiShared.DrawHelpText("Only use this if advised by the PlayerSync support team, or if you know there is an ISP issue affecting you.");

        var idx = _uiShared.DrawServiceSelection();
        if (_lastSelectedServerIndex != idx)
        {
            _uiShared.ResetOAuthTasksState();
            _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
            _secretKeysConversionTask = null;
            _lastSelectedServerIndex = idx;
        }

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        _selectedServer = selectedServer;
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
        }

        bool useOauth = selectedServer.UseOAuth2;

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("Character Management"))
            {
                if (selectedServer.SecretKeys.Any() || useOauth)
                {
                    UiSharedService.ColorTextWrapped("Characters listed here will automatically connect to the selected PlayerSync service with the settings as provided below." +
                        " Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
                    int i = 0;
                    _uiShared.DrawUpdateOAuthUIDsButton(selectedServer);

                    if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken))
                    {
                        bool hasSetSecretKeysButNoUid = selectedServer.Authentications.Exists(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID));
                        if (hasSetSecretKeysButNoUid)
                        {
                            ImGui.Dummy(new(5f, 5f));
                            UiSharedService.TextWrapped("Some entries have been detected that have previously been assigned secret keys but not UIDs. " +
                                "Press this button below to attempt to convert those entries.");
                            using (ImRaii.Disabled(_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted))
                            {
                                if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsLeftRight, "Try to Convert Secret Keys to UIDs"))
                                {
                                    _secretKeysConversionTask = ConvertSecretKeysToUIDs(selectedServer, _secretKeysConversionCts.Token);
                                }
                            }
                            if (_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted)
                            {
                                UiSharedService.ColorTextWrapped("Converting Secret Keys to UIDs", ImGuiColors.DalamudYellow);
                            }
                            if (_secretKeysConversionTask != null && _secretKeysConversionTask.IsCompletedSuccessfully)
                            {
                                Vector4? textColor = null;
                                if (_secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    textColor = ImGuiColors.DalamudYellow;
                                }
                                if (!_secretKeysConversionTask.Result.Success)
                                {
                                    textColor = ImGuiColors.DalamudRed;
                                }
                                string text = $"Conversion has completed: {_secretKeysConversionTask.Result.Result}";
                                if (textColor == null)
                                {
                                    UiSharedService.TextWrapped(text);
                                }
                                else
                                {
                                    UiSharedService.ColorTextWrapped(text, textColor!.Value);
                                }
                                if (!_secretKeysConversionTask.Result.Success || _secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    UiSharedService.TextWrapped("In case of conversion failures, please set the UIDs for the failed conversions manually.");
                                }
                            }
                        }
                    }
                    ImGui.Separator();
                    string youName = _dalamudUtilService.GetPlayerName();
                    uint youWorld = _dalamudUtilService.GetHomeWorldId();
                    ulong youCid = _dalamudUtilService.GetCID();
                    if (!selectedServer.Authentications.Exists(a => string.Equals(a.CharacterName, youName, StringComparison.Ordinal) && a.WorldId == youWorld))
                    {
                        _uiShared.BigText("Your Character is not Configured", ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("You have currently no character configured that corresponds to your current name and world.", ImGuiColors.DalamudRed);
                        var authWithCid = selectedServer.Authentications.Find(f => f.LastSeenCID == youCid);
                        if (authWithCid != null)
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            UiSharedService.ColorText("A potential rename/world change from this character was detected:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("Entry: " + authWithCid.CharacterName + " - " + _dalamudUtilService.WorldData.Value[(ushort)authWithCid.WorldId], ImGuiColors.ParsedGreen);
                            UiSharedService.ColorText("Press the button below to adjust that entry to your current character:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("Current: " + youName + " - " + _dalamudUtilService.WorldData.Value[(ushort)youWorld], ImGuiColors.ParsedGreen);
                            ImGuiHelpers.ScaledDummy(5);
                            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowRight, "Update Entry to Current Character"))
                            {
                                authWithCid.CharacterName = youName;
                                authWithCid.WorldId = youWorld;
                                _serverConfigurationManager.Save();
                            }
                        }
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.Separator();
                        ImGuiHelpers.ScaledDummy(5);
                    }
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        using var charaId = ImRaii.PushId("selectedChara" + i);

                        var worldIdx = (ushort)item.WorldId;
                        var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                        if (!data.TryGetValue(worldIdx, out string? worldPreview))
                        {
                            worldPreview = data.First().Value;
                        }

                        Dictionary<int, SecretKey> keys = [];

                        if (!useOauth)
                        {
                            var secretKeyIdx = item.SecretKeyIdx;
                            keys = selectedServer.SecretKeys;
                            if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                            {
                                secretKey = new();
                            }
                        }

                        bool thisIsYou = false;
                        if (string.Equals(youName, item.CharacterName, StringComparison.OrdinalIgnoreCase)
                            && youWorld == worldIdx)
                        {
                            thisIsYou = true;
                        }
                        bool misManaged = false;
                        if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken) && string.IsNullOrEmpty(item.UID))
                        {
                            misManaged = true;
                        }
                        if (!selectedServer.UseOAuth2 && item.SecretKeyIdx == -1)
                        {
                            misManaged = true;
                        }
                        Vector4 color = ImGuiColors.ParsedGreen;
                        string text = thisIsYou ? "Your Current Character" : string.Empty;
                        if (misManaged)
                        {
                            text += " [MISMANAGED (" + (selectedServer.UseOAuth2 ? "No UID Set" : "No Secret Key Set") + ")]";
                            color = ImGuiColors.DalamudRed;
                        }
                        if (selectedServer.Authentications.Where(e => e != item).Any(e => string.Equals(e.CharacterName, item.CharacterName, StringComparison.Ordinal)
                            && e.WorldId == item.WorldId))
                        {
                            text += " [DUPLICATE]";
                            color = ImGuiColors.DalamudRed;
                        }

                        if (!string.IsNullOrEmpty(text))
                        {
                            text = text.Trim();
                            _uiShared.BigText(text, color);
                        }

                        var charaName = item.CharacterName;
                        if (ImGui.InputText("Character Name", ref charaName, 64))
                        {
                            item.CharacterName = charaName;
                            _serverConfigurationManager.Save();
                        }

                        _uiShared.DrawCombo("World##" + item.CharacterName + i, data, (w) => w.Value,
                            (w) =>
                            {
                                if (item.WorldId != w.Key)
                                {
                                    item.WorldId = w.Key;
                                    _serverConfigurationManager.Save();
                                }
                            }, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault(f => f.Key == worldIdx), default) ? data.First() : data.First(f => f.Key == worldIdx));

                        if (!useOauth)
                        {
                            _uiShared.DrawCombo("Secret Key###" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                (w) =>
                                {
                                    if (w.Key != item.SecretKeyIdx)
                                    {
                                        item.SecretKeyIdx = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));
                        }
                        else
                        {
                            _uiShared.DrawUIDComboForAuthentication(i, item, selectedServer.ServerUri, _logger);
                        }
                        bool isAutoLogin = item.AutoLogin;
                        if (ImGui.Checkbox("Automatically login to PlayerSync", ref isAutoLogin))
                        {
                            item.AutoLogin = isAutoLogin;
                            _serverConfigurationManager.Save();
                        }
                        _uiShared.DrawHelpText("When enabled and logging into this character in XIV, PlayerSync will automatically connect to the current service.");
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Character") && UiSharedService.CtrlPressed())
                            _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                        UiSharedService.AttachToolTip("Hold CTRL to delete this entry.");

                        i++;
                        if (item != selectedServer.Authentications.ToList()[^1])
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            ImGui.Separator();
                            ImGuiHelpers.ScaledDummy(5);
                        }
                    }

                    if (selectedServer.Authentications.Any())
                        ImGui.Separator();

                    if (!selectedServer.Authentications.Exists(c => string.Equals(c.CharacterName, youName, StringComparison.Ordinal)
                        && c.WorldId == youWorld))
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.User, "Add current character"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new character"))
                    {
                        _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("You need to add a Secret Key first before adding Characters.", ImGuiColors.DalamudYellow);
                }
                ImGuiHelpers.ScaledDummy(5);
                ImGui.EndTabItem();
            }

            if (!useOauth && ImGui.BeginTabItem("Secret Key Management"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    using var id = ImRaii.PushId("key" + item.Key);
                    var friendlyName = item.Value.FriendlyName;
                    if (ImGui.InputText("Secret Key Display Name", ref friendlyName, 255))
                    {
                        item.Value.FriendlyName = friendlyName;
                        _serverConfigurationManager.Save();
                    }
                    var key = item.Value.Key;
                    if (ImGui.InputText("Secret Key", ref key, 64))
                    {
                        item.Value.Key = key;
                        _serverConfigurationManager.Save();
                    }
                    if (!selectedServer.Authentications.Exists(p => p.SecretKeyIdx == item.Key))
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key") && UiSharedService.CtrlPressed())
                        {
                            selectedServer.SecretKeys.Remove(item.Key);
                            _serverConfigurationManager.Save();
                        }
                        UiSharedService.AttachToolTip("Hold CTRL to delete this secret key entry");
                    }
                    else
                    {
                        UiSharedService.ColorTextWrapped("This key is in use and cannot be deleted", ImGuiColors.DalamudYellow);
                    }

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "New Secret Key",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Configuration"))
            {
                var serverName = selectedServer.ServerName;
                var serverUri = selectedServer.ServerUri;
                var isMain = string.Equals(serverName, ApiController.MainServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;

                if (ImGui.InputText("Service URI", ref serverUri, 255, flags))
                {
                    selectedServer.ServerUri = serverUri;
                }
                if (isMain)
                {
                    _uiShared.DrawHelpText("You cannot edit the URI of the main service.");
                }

                if (ImGui.InputText("Service Name", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    _uiShared.DrawHelpText("You cannot edit the name of the main service.");
                }

                ImGui.SetNextItemWidth(200);
                var serverTransport = _serverConfigurationManager.GetTransport();
                _uiShared.DrawCombo("Server Transport Type", Enum.GetValues<HttpTransportType>().Where(t => t != HttpTransportType.None),
                    (v) => v.ToString(),
                    onSelected: (t) => _serverConfigurationManager.SetTransportType(t),
                    serverTransport);
                _uiShared.DrawHelpText("You normally do not need to change this, if you don't know what this is or what it's for, keep it to WebSockets." + Environment.NewLine
                    + "If you run into connection issues with e.g. VPNs, try ServerSentEvents first before trying out LongPolling." + UiSharedService.TooltipSeparator
                    + "Note: if the server does not support a specific Transport Type it will fall through to the next automatically: WebSockets > ServerSentEvents > LongPolling");

                if (_dalamudUtilService.IsWine)
                {
                    bool forceWebSockets = selectedServer.ForceWebSockets;
                    if (ImGui.Checkbox("[wine only] Force WebSockets", ref forceWebSockets))
                    {
                        selectedServer.ForceWebSockets = forceWebSockets;
                        _serverConfigurationManager.Save();
                    }
                    _uiShared.DrawHelpText("On wine, PlayerSync will automatically fall back to ServerSentEvents/LongPolling, even if WebSockets is selected. "
                        + "WebSockets are known to crash XIV entirely on wine 8.5 shipped with Dalamud. "
                        + "Only enable this if you are not running wine 8.5." + Environment.NewLine
                        + "Note: If the issue gets resolved at some point this option will be removed.");
                }

                ImGuiHelpers.ScaledDummy(5);

                if (ImGui.Checkbox("Use Discord OAuth2 Authentication", ref useOauth))
                {
                    selectedServer.UseOAuth2 = useOauth;
                    _serverConfigurationManager.Save();
                }
                _uiShared.DrawHelpText("Use Discord OAuth2 Authentication to identify with this server instead of secret keys");
                if (useOauth)
                {
                    _uiShared.DrawOAuth(selectedServer);
                    if (string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("You have enabled OAuth2 but it is not linked. Press the buttons Check, then Authenticate to link properly.", ImGuiColors.DalamudRed);
                    }
                    if (!string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer))
                        && selectedServer.Authentications.TrueForAll(u => string.IsNullOrEmpty(u.UID)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("You have enabled OAuth2 but no characters configured. Set the correct UIDs for your characters in \"Character Management\".",
                            ImGuiColors.DalamudRed);
                    }
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    ImGui.Separator();
                    if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Service") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    _uiShared.DrawHelpText("Hold CTRL to delete this service");
                }

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawServicePermissions()
    {
        _uiShared.BigText("Default Permission Settings");
        ImGuiHelpers.ScaledDummy(2);
        if (_selectedServer == _serverConfigurationManager.CurrentServer && _apiController.IsConnected)
        {
            UiSharedService.TextWrapped("Note: The default permissions settings here are not applied retroactively to existing pairs or joined Syncshells.");
            UiSharedService.TextWrapped("Note: The default permissions settings here are sent and stored on the connected service.");
            ImGuiHelpers.ScaledDummy(5f);
            var perms = _apiController.DefaultPermissions!;
            bool individualIsSticky = perms.IndividualIsSticky;
            bool disableIndividualSounds = perms.DisableIndividualSounds;
            bool disableIndividualAnimations = perms.DisableIndividualAnimations;
            bool disableIndividualVFX = perms.DisableIndividualVFX;
            if (ImGui.Checkbox("Individually set permissions become preferred permissions", ref individualIsSticky))
            {
                perms.IndividualIsSticky = individualIsSticky;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("The preferred attribute means that the permissions to that user will never change through any of your permission changes to Syncshells " +
                "(i.e. if you have paused one specific user in a Syncshell and they become preferred permissions, then pause and unpause the same Syncshell, the user will remain paused - " +
                "if a user does not have preferred permissions, it will follow the permissions of the Syncshell and be unpaused)." + Environment.NewLine + Environment.NewLine +
                "This setting means:" + Environment.NewLine +
                "  - All new individual pairs get their permissions defaulted to preferred permissions." + Environment.NewLine +
                "  - All individually set permissions for any pair will also automatically become preferred permissions. This includes pairs in Syncshells." + Environment.NewLine + Environment.NewLine +
                "It is possible to remove or set the preferred permission state for any pair at any time." + Environment.NewLine + Environment.NewLine +
                "If unsure, leave this setting off.");
            ImGuiHelpers.ScaledDummy(3f);

            if (ImGui.Checkbox("Disable individual pair sounds", ref disableIndividualSounds))
            {
                perms.DisableIndividualSounds = disableIndividualSounds;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable sound sync for all new individual pairs.");
            if (ImGui.Checkbox("Disable individual pair animations", ref disableIndividualAnimations))
            {
                perms.DisableIndividualAnimations = disableIndividualAnimations;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable animation sync for all new individual pairs.");
            if (ImGui.Checkbox("Disable individual pair VFX", ref disableIndividualVFX))
            {
                perms.DisableIndividualVFX = disableIndividualVFX;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable VFX sync for all new individual pairs.");
            ImGuiHelpers.ScaledDummy(5f);
            bool disableGroundSounds = perms.DisableGroupSounds;
            bool disableGroupAnimations = perms.DisableGroupAnimations;
            bool disableGroupVFX = perms.DisableGroupVFX;
            if (ImGui.Checkbox("Disable Syncshell pair sounds", ref disableGroundSounds))
            {
                perms.DisableGroupSounds = disableGroundSounds;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable sound sync for all non-sticky pairs in newly joined syncshells.");
            if (ImGui.Checkbox("Disable Syncshell pair animations", ref disableGroupAnimations))
            {
                perms.DisableGroupAnimations = disableGroupAnimations;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable animation sync for all non-sticky pairs in newly joined syncshells.");
            if (ImGui.Checkbox("Disable Syncshell pair VFX", ref disableGroupVFX))
            {
                perms.DisableGroupVFX = disableGroupVFX;
                _ = _apiController.UserUpdateDefaultPermissions(perms);
            }
            _uiShared.DrawHelpText("This setting will disable VFX sync for all non-sticky pairs in newly joined syncshells.");
        }
        else
        {
            UiSharedService.ColorTextWrapped("Default Permission Settings unavailable for this service. " +
                "You need to connect to this service to change the default permissions since they are stored on the service.", ImGuiColors.DalamudYellow);
        }
    }

    private void DrawServiceAccount()
    {
        _lastTab = "Account";

        _uiShared.BigText("Account");
        ImGuiHelpers.ScaledDummy(2);
        var sendCensus = _serverConfigurationManager.SendCensusData;
        if (ImGui.Checkbox("Send Statistical Census Data", ref sendCensus))
        {
            _serverConfigurationManager.SendCensusData = sendCensus;
        }
        _uiShared.DrawHelpText("This will allow sending census data to the currently connected service." + UiSharedService.TooltipSeparator
            + "Census data contains:" + Environment.NewLine
            + "- Current World" + Environment.NewLine
            + "- Current Gender" + Environment.NewLine
            + "- Current Race" + Environment.NewLine
            + "- Current Clan (this is not your Free Company, this is e.g. Keeper or Seeker for Miqo'te)" + UiSharedService.TooltipSeparator
            + "The census data is only saved temporarily and will be removed from the server on disconnect. It is stored temporarily associated with your UID while you are connected." + UiSharedService.TooltipSeparator
            + "If you do not wish to participate in the statistical census, untick this box and reconnect to the server.");

        if (ApiController.ServerAlive)
        {
            ImGuiHelpers.ScaledDummy(5);
            if (ImGui.Button("Delete all my files"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("Delete all your files?");
            }

            _uiShared.DrawHelpText("Completely deletes all your uploaded files on the service.");

            if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete everything", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(_fileTransferManager.DeleteAllFiles);
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }

            ImGuiHelpers.ScaledDummy(5);
            if (ImGui.Button("Delete account"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("Delete your account?");
            }

            _uiShared.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

            if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "Your account and all associated files and data on the service will be deleted.");
                UiSharedService.TextWrapped("Your UID will be removed from all pairing lists.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete account", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(ApiController.UserDelete);
                    _deleteAccountPopupModalShown = false;
                    _configService.Current.FirstTimeSetupComplete = false;
                    _configService.Save();
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
        }
    }
}
