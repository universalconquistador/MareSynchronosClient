using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.FileCache;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class IntroUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly Dictionary<string, string> _languages = new(StringComparer.Ordinal) { { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" } };
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly UiSharedService _uiShared;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private int _currentLanguage;

    private string _secretKey = string.Empty;
    private string[]? _tosParagraphs;
    private bool _useLegacyLogin = false;
    private ServerStorage? _selectedServer;

    private enum IntroUiPages
    {
        Welcome,
        Agreement,
        Storage,
        Settings,
        Service,
        Account,
    }
    private static readonly IntroUiPages[] _wizardOrder =
    {
        IntroUiPages.Welcome,
        IntroUiPages.Agreement,
        IntroUiPages.Storage,
        IntroUiPages.Settings,
        IntroUiPages.Service,
        IntroUiPages.Account,
    };
    private IntroUiPages _currentWizardPageId = IntroUiPages.Welcome;
    private readonly HashSet<IntroUiPages> _unlockedWizardPages = new() { IntroUiPages.Welcome };
    private UiNav.NavItem<IntroUiPages> _selectedNavItem = default!;
    private List<(string GroupLabel, IReadOnlyList<UiNav.NavItem<IntroUiPages>> Items)> _navItems = new();
    private const int AgreementCooldownSeconds = 30;
    private double? _agreementUnlockAt;

    public IntroUi(ILogger<IntroUi> logger, UiSharedService uiShared, MareConfigService configService,
        CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
        PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService, ZoneSyncConfigService zoneSyncConfigService)
        : base(logger, mareMediator, "PlayerSync Setup", performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _cacheMonitor = fileCacheManager;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _zoneSyncConfigService = zoneSyncConfigService;

        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(900, 600),
            MaximumSize = new Vector2(900, 2000),
        };

        GetToSLocalization();

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) =>
        {
            _configService.Current.UseCompactor = !dalamudUtilService.IsWine;
            IntroUiPages page = IntroUiPages.Welcome;
            if (_configService.Current.FirstTimeSetupComplete)
            {
                if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !_configService.Current.InitialScanComplete || !Directory.Exists(_configService.Current.CacheFolder))
                {
                    page = IntroUiPages.Storage;
                    Mediator.Publish(new NotificationMessage("Configuration Error", "PlayerSync has detected an issue with your local storage folder. Ensure it exists on your computer and is configured correctly within the plugin. " +
                        "You may need to run the file scan again as well.",
                        NotificationType.Error, TimeSpan.FromSeconds(10)));
                }
                    
                else if (!_uiShared.ApiController.ServerAlive)
                {
                    Mediator.Publish(new NotificationMessage("Service Error", "PlayerSync has detected an issue with your account. Ensure your OAuth2 is connected or secret key is valid for this service.",
                        NotificationType.Error, TimeSpan.FromSeconds(10)));
                    page = IntroUiPages.Account;
                }
            }
            ResetWizard(page);
            IsOpen = true;
        });

        RebuildWizardNav();
    }

    private void ResetWizard(IntroUiPages startPage = IntroUiPages.Welcome)
    {
        _currentWizardPageId = startPage;
        _unlockedWizardPages.Clear();
        if (_configService.Current.FirstTimeSetupComplete)
        {
            foreach (var p in _wizardOrder)
                _unlockedWizardPages.Add(p);
        }
        else
        {
            _unlockedWizardPages.Add(IntroUiPages.Welcome);
        }
        RebuildWizardNav();
    }

    private void RebuildWizardNav()
    {
        UiNav.NavItem<IntroUiPages> Item(IntroUiPages id, string label, Action draw)
            => new(id, label, draw, Enabled: _unlockedWizardPages.Contains(id));

        _navItems = new List<(string GroupLabel, IReadOnlyList<UiNav.NavItem<IntroUiPages>> Items)>
        {
            ("", new List<UiNav.NavItem<IntroUiPages>>
            {
                Item(IntroUiPages.Welcome,   "Welcome",   DrawIntro),
                Item(IntroUiPages.Agreement, "Agreement", DrawAgreement),
                Item(IntroUiPages.Storage,   "Storage",   DrawFileStorage),
                Item(IntroUiPages.Settings,  "Settings",  DrawSettings),
                Item(IntroUiPages.Service,   "Service",   DrawService),
                Item(IntroUiPages.Account,   "Account",   DrawAccount),
            }),
        };

        _selectedNavItem = _navItems[0].Items.First(i => i.Id == _currentWizardPageId);
    }

    private bool CanGoNextPage()
    {
        if (_currentWizardPageId == IntroUiPages.Agreement)
            return _configService.Current.AcceptedAgreement;

        if (_currentWizardPageId == IntroUiPages.Storage)
            return _configService.Current.InitialScanComplete;

        if (_currentWizardPageId == IntroUiPages.Account)
            return _uiShared.ApiController.ServerAlive;

        return true;
    }

    private void NextPage()
    {
        var idx = Array.IndexOf(_wizardOrder, _currentWizardPageId);
        if (idx < 0 || idx >= _wizardOrder.Length - 1)
            return;

        var nextId = _wizardOrder[idx + 1];
        _unlockedWizardPages.Add(nextId);
        _currentWizardPageId = nextId;
        RebuildWizardNav();
    }

    private void PreviousPage()
    {
        var idx = Array.IndexOf(_wizardOrder, _currentWizardPageId);
        if (idx <= 0)
            return;

        _currentWizardPageId = _wizardOrder[idx - 1];
        RebuildWizardNav();
    }

    private int _prevIdx = -1;

    protected override void DrawInternal()
    {
        if (_uiShared.IsInGpose) return;

        if (_configService.Current.AcceptedAgreement 
            && _uiShared.ApiController.ServerAlive
            && _configService.Current.InitialScanComplete
            && !string.IsNullOrEmpty(_configService.Current.CacheFolder)
            && Directory.Exists(_configService.Current.CacheFolder))
        {
            FinishSetup();
        }

        var theme = UiTheme.Default;

        //_uiShared.BigText("PlayerSync Setup Wizard");
        //Ui.DrawHorizontalRule(t);
        //ImGuiHelpers.ScaledDummy(5f);

        _selectedNavItem = UiNav.DrawSidebar(theme, "", _navItems, _selectedNavItem, widthPx: 180f, iconFont: _uiShared.IconFont);

        if (_currentWizardPageId != _selectedNavItem.Id)
            _currentWizardPageId = _selectedNavItem.Id;

        ImGui.SameLine();

        using var pane = ImRaii.Child("##setup-pane", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        Ui.AddVerticalSpace(2);

        var style = ImGui.GetStyle();
        var padFooter = UiScale.ScaledFloat(10f);
        var padRight = UiScale.ScaledFloat(26f);
        var padBottom = UiScale.ScaledFloat(10f);
        var footerExtra = UiScale.ScaledFloat(8f);
        var footerH = padFooter + padBottom + ImGui.GetFrameHeight() + footerExtra + padFooter;
        var avail = ImGui.GetContentRegionAvail();
        var contentH = Math.Max(0, avail.Y - footerH);

        using (var content = ImRaii.Child("##setup-content", new Vector2(0, contentH), false))
        {
            if (content)
                _selectedNavItem.NavAction.Invoke();
        }

        using (var footer = ImRaii.Child("##setup-footer", new Vector2(0, footerH), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (footer)
            {
                Ui.DrawHorizontalRule(theme);

                var idx = Array.IndexOf(_wizardOrder, _currentWizardPageId);
                var isFirst = idx <= 0;
                var isLast = idx >= _wizardOrder.Length - 1;

                var backLabel = "Back";
                var nextLabel = "Next";

                float ButtonWidth(string label) => ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2f);

                var spacing = style.ItemSpacing.X;
                var backW = ButtonWidth(backLabel);
                var nextW = ButtonWidth(nextLabel);
                var totalW = (isFirst ? 0f : backW + spacing) + nextW;
                var max = ImGui.GetWindowContentRegionMax();
                var y = max.Y - ImGui.GetFrameHeight() - padFooter - padBottom;
                var x = max.X - totalW - padRight;

                ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), y));
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));

                if (!(isFirst || _configService.Current.FirstTimeSetupComplete))
                {
                    if (ImGui.Button(backLabel))
                        PreviousPage();
                    ImGui.SameLine();
                }

                if (!_configService.Current.FirstTimeSetupComplete)
                {
                    using (ImRaii.Disabled(!CanGoNextPage()))
                    {
                        if (ImGui.Button(nextLabel))
                        {
                            NextPage();
                        }
                    }
                }
            }
        }
    }

    private void FinishSetup()
    {
        _configService.Current.FirstTimeSetupComplete = true;
        _configService.Save();
        Mediator.Publish(new SwitchToMainUiMessage());
        IsOpen = false;
    }

    private void DrawIntro()
    {
        _uiShared.BigText("Welcome to PlayerSync!");
        ImGui.Separator();
        UiSharedService.TextWrapped("PlayerSync is a plugin that will replicate your full current character state including all Penumbra mods to other paired PlayerSync users. " +
                          "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
        // TODO: Localizations
        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.TextWrapped("Before you get started, ensure you have completed the following:");
        using (ImRaii.PushIndent())
        {
            UiSharedService.TextWrapped("• Have both Penumbra and Glamourer installed");
            UiSharedService.TextWrapped("• Have a file storage location set for Penumbra, i.e.\"C:\\PenumbraMods\"");
            UiSharedService.TextWrapped("• Have registered a service account on the PlayeSync Discord");
        }
        ImGuiHelpers.ScaledDummy(5);

        if (ImGui.Button("Penumbra+Glamourer Repo"))
        {
            Util.OpenLink("https://github.com/Ottermandias/SeaOfStars");
        }
        ImGuiHelpers.ScaledDummy(2);
        if (ImGui.Button("PlayerSync Discord"))
        {
            Util.OpenLink("https://discord.gg/playersync");
        }

        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.ColorTextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients " +
                            "might look broken because of this or others players mods might not apply on your end altogether. " +
                            "If you want to use this plugin, ensure you only load mods via Penumbra.", ImGuiColors.DalamudYellow);

        ImGuiHelpers.ScaledDummy(5);
        if (!_uiShared.DrawOtherPluginState()) return;
        ImGuiHelpers.ScaledDummy(25);
        string text = "Once you are certain you have completed the checklist, click the Next button to continue.";
        ImGuiHelpers.CenterCursorForText(text);
        UiSharedService.ColorTextWrapped(text, ImGuiColors.DalamudYellow);
    }

    private void DrawAgreement()
    {
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
            ImGui.TextUnformatted(Strings.ToS.AgreementLabel);
        }

        ImGui.SameLine();
        var languageSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - languageSize.X - 80);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - languageSize.Y / 2);

        ImGui.TextUnformatted(Strings.ToS.LanguageLabel);
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - (languageSize.Y + ImGui.GetStyle().FramePadding.Y) / 2);
        ImGui.SetNextItemWidth(80);
        if (ImGui.Combo("", ref _currentLanguage, _languages.Keys.ToArray(), _languages.Count))
            GetToSLocalization(_currentLanguage);

        ImGui.Separator();
        ImGui.SetWindowFontScale(1.5f);
        var readThis = Strings.ToS.ReadLabel;
        textSize = ImGui.CalcTextSize(readThis);
        ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
        UiSharedService.ColorText(readThis, ImGuiColors.DalamudRed);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Separator();

        UiSharedService.TextWrapped(_tosParagraphs![0]);
        UiSharedService.TextWrapped(_tosParagraphs![1]);
        UiSharedService.TextWrapped(_tosParagraphs![2]);
        UiSharedService.TextWrapped(_tosParagraphs![3]);
        UiSharedService.TextWrapped(_tosParagraphs![4]);
        UiSharedService.TextWrapped(_tosParagraphs![5]);

        ImGui.Separator();

        StartAgreementCooldownIfNeeded();

        var remaining = GetAgreementSecondsRemaining();
        if (!_configService.Current.AcceptedAgreement)
        {
            if (remaining <= 0)
            {
                if (ImGui.Button(Strings.ToS.AgreeLabel))
                {
                    _configService.Current.AcceptedAgreement = true;
                    _configService.Save();
                }
            }
            else
            {
                UiSharedService.TextWrapped($"{Strings.ToS.ButtonWillBeAvailableIn} {remaining}s");
            }
        }
    }

    private void StartAgreementCooldownIfNeeded()
    {
        if (_agreementUnlockAt.HasValue) return;

#if DEBUG
        _agreementUnlockAt = ImGui.GetTime();
#else
    _agreementUnlockAt = ImGui.GetTime() + AgreementCooldownSeconds;
#endif
    }

    private int GetAgreementSecondsRemaining()
    {
        if (!_agreementUnlockAt.HasValue) return AgreementCooldownSeconds;

        var remaining = (int)Math.Ceiling(_agreementUnlockAt.Value - ImGui.GetTime());
        return Math.Max(0, remaining);
    }

    private void DrawFileStorage()
    {
        _uiShared.BigText("File Storage Setup");
        ImGui.Separator();

        if (!_uiShared.HasValidPenumbraModPath)
        {
            UiSharedService.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
        }
        else
        {
            UiSharedService.TextWrapped("To not unnecessary download files already present on your computer, PlayerSync will have to scan your Penumbra mod directory. " +
                                    "Additionally, a local storage folder must be set where PlayerSync will download other character files to. " +
                                    "Once the storage folder is set and the scan complete, this page will automatically forward to registration at a service.");
            UiSharedService.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.csv of PlayerSync in the Plugin Configurations folder of Dalamud. " +
                                        "Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
            UiSharedService.ColorTextWrapped("Warning: if the scan is hanging and does nothing for a long time, chances are high your Penumbra folder is not set up properly.", ImGuiColors.DalamudYellow);
            _uiShared.DrawCacheDirectorySetting();
        }

        if (!_cacheMonitor.IsScanRunning && !string.IsNullOrEmpty(_configService.Current.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_configService.Current.CacheFolder))
        {
            if (ImGui.Button("Start Scan##startScan"))
            {
                _cacheMonitor.InvokeScan();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("(You must run the scan before proceeding.)");
        }
        else
        {
            _uiShared.DrawFileScanState();
        }
        if (!_dalamudUtilService.IsWine)
        {
            var useFileCompactor = _configService.Current.UseCompactor;
            ImGuiHelpers.ScaledDummy(5);
            if (ImGui.Checkbox("Use File Compactor", ref useFileCompactor))
            {
                _configService.Current.UseCompactor = useFileCompactor;
                _configService.Save();
            }
            UiSharedService.ColorTextWrapped("The File Compactor can save a tremendeous amount of space on the hard disk for downloads through PlayerSync. It will incur a minor CPU penalty on download but can speed up " +
                "loading of other characters. It is recommended to keep it enabled. You can change this setting later anytime in the PlayerSync settings.", ImGuiColors.DalamudYellow);
        }
    }

    private void DrawService()
    {
        _uiShared.BigText("Service Registration");
        ImGui.Separator();
        UiSharedService.TextWrapped("You need a service account registerd on the PlayerSync Discord to continue.");
        ImGuiHelpers.ScaledDummy(5);
        if (ImGui.Button("Join the PlayerSync Discord"))
            {
                Util.OpenLink("https://discord.gg/BzaqbfFFmn");
            }
        ImGuiHelpers.ScaledDummy(5);

        int serverIdx = 0;
        _selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
        serverIdx = _uiShared.DrawServiceSelection(selectOnChange: true, showConnect: false);
        if (serverIdx != _prevIdx)
        {
            _uiShared.ResetOAuthTasksState();
            _prevIdx = serverIdx;
            }

        ImGuiHelpers.ScaledDummy(5);
        _selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
        _useLegacyLogin = !_selectedServer.UseOAuth2;
        if (ImGui.Checkbox("Use Legacy Login with Secret Key", ref _useLegacyLogin))
        {
            _serverConfigurationManager.GetServerByIndex(serverIdx).UseOAuth2 = !_useLegacyLogin;
            _serverConfigurationManager.Save();
        }

        ImGuiHelpers.ScaledDummy(5);
        ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "Check \"Use Proxied Server\" if you had to use the mirror repo.");
        var useBackupServer = _serverConfigurationManager.EnableBackupServer;
        if (ImGui.Checkbox("Use Proxied Server", ref useBackupServer))
        {
            _serverConfigurationManager.EnableBackupServer = useBackupServer;
        }
        _uiShared.DrawHelpText("Only use this if advised by the PlayerSync support team, or if you know there is an ISP issue affecting you.");

        if (_useLegacyLogin)
        {
            var text = "Enter Secret Key";
            var buttonText = "Save";
            var buttonWidth = _secretKey.Length != 64 ? 0 : ImGuiHelpers.GetButtonSize(buttonText).X + ImGui.GetStyle().ItemSpacing.X;
            var textSize = ImGui.CalcTextSize(text);

            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("Strongly consider to use OAuth2 to authenticate, if the server supports it (the current main server does). " +
                "The authentication flow is simpler and you do not require to store or maintain Secret Keys. " +
                "You already implicitly register using Discord, so the OAuth2 method will be cleaner and more straight-forward to use.", ImGuiColors.DalamudYellow, 500);
            ImGuiHelpers.ScaledDummy(5);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize.X);
            ImGui.InputText("", ref _secretKey, 64);
            if (_secretKey.Length > 0 && _secretKey.Length != 64)
            {
                UiSharedService.ColorTextWrapped("Your secret key must be exactly 64 characters long. Don't enter your Lodestone auth here.", ImGuiColors.DalamudRed);
            }
            else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
            {
                UiSharedService.ColorTextWrapped("Your secret key can only contain ABCDEF and the numbers 0-9.", ImGuiColors.DalamudRed);
            }
            else if (_secretKey.Length == 64)
            {
                ImGui.SameLine();
                if (ImGui.Button(buttonText))
                {
                    if (_serverConfigurationManager.CurrentServer == null) _serverConfigurationManager.SelectServer(0);
                    if (!_serverConfigurationManager.CurrentServer!.SecretKeys.Any())
                    {
                        _serverConfigurationManager.CurrentServer!.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select(k => k.Key).LastOrDefault() + 1, new SecretKey()
                        {
                            FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                            Key = _secretKey,
                        });
                        _serverConfigurationManager.AddCurrentCharacterToServer();
                    }
                    else
                    {
                        _serverConfigurationManager.CurrentServer!.SecretKeys[0] = new SecretKey()
                        {
                            FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                            Key = _secretKey,
                        };
                    }
                    _secretKey = string.Empty;
                    _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                }
            }
        }
        else
        {
            ImGuiHelpers.ScaledDummy(50);
            string text = "If you have already set up an account on Discord, you can click the Next button to continue.";
            ImGuiHelpers.CenterCursorForText(text);
            UiSharedService.ColorTextWrapped(text, ImGuiColors.DalamudYellow);
        }
    }

    private void DrawAccount()
    {
        _uiShared.BigText("Account");
        ImGui.Separator();

        if (_selectedServer is null)
            _selectedServer = _serverConfigurationManager.GetServerByIndex(0);

        if (string.IsNullOrEmpty(_selectedServer.OAuthToken))
        {
            UiSharedService.TextWrapped("Press the button below to verify the server has OAuth2 capabilities. Afterwards, authenticate using Discord in the Browser window.");
            ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, "You must sign up on Discord before this will work.");
            UiSharedService.ColorTextWrapped("Be sure to authenticate with the Discord account you joined the PlayerSync Discord with.", ImGuiColors.DalamudRed);
            _uiShared.DrawOAuth(_selectedServer);
        }
        else
        {
            UiSharedService.ColorTextWrapped($"OAuth2 is connected. Linked to: Discord User {_serverConfigurationManager.GetDiscordUserFromToken(_selectedServer)}", ImGuiColors.HealerGreen);
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.TextWrapped("Now press the update UIDs button to get a list of all of your UIDs on the server.");
            _uiShared.DrawUpdateOAuthUIDsButton(_selectedServer);
            ImGuiHelpers.ScaledDummy(5);
            var playerName = _dalamudUtilService.GetPlayerName();
            var playerWorld = _dalamudUtilService.GetHomeWorldId();
            UiSharedService.TextWrapped($"Once pressed, select the UID you want to use for your current character {_dalamudUtilService.GetPlayerName()}. If no UIDs are visible, make sure you are connected to the correct Discord account. " +
                $"If that is not the case, use the unlink button below (hold CTRL to unlink).");
            _uiShared.DrawUnlinkOAuthButton(_selectedServer);
            ImGuiHelpers.ScaledDummy(5);
            var auth = _selectedServer.Authentications.Find(a => string.Equals(a.CharacterName, playerName, StringComparison.Ordinal) && a.WorldId == playerWorld);
            if (auth == null)
            {
                auth = new Authentication()
                {
                    CharacterName = playerName,
                    WorldId = playerWorld
                };
                _selectedServer.Authentications.Add(auth);
                _serverConfigurationManager.Save();
            }

            _uiShared.DrawUIDComboForAuthentication(0, auth, _selectedServer.ServerUri);

            using (ImRaii.Disabled(string.IsNullOrEmpty(auth.UID)))
            {
                if (_uiShared.IconTextButton(FontAwesomeIcon.Link, "Connect to Service"))
                {
                    _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                }
            }
            if (string.IsNullOrEmpty(auth.UID))
                UiSharedService.AttachToolTip("Select a UID to be able to connect to the service");
        }
    }

    private void DrawSettings()
    {
        using (_uiShared.UidFont.Push())
            ImGui.TextUnformatted("Quick Start Settings");
        ImGui.Separator();
        UiSharedService.TextWrapped("You can later change these settings, and many others, within the PlayerSync Settings menu.");
        ImGuiHelpers.ScaledDummy(5);

        var showNameHighlights = _configService.Current.ShowNameHighlights;
        if (ImGui.Checkbox("Color Code Active Pair Names", ref showNameHighlights))
        {
            _configService.Current.ShowNameHighlights = showNameHighlights;
            _configService.Save();
            Mediator.Publish(new RedrawNameplateMessage());
        }
        _uiShared.DrawHelpText("This will change the name color for active pairs you can see." + Environment.NewLine +
            "Turning this off may take a moment to reflect in game.");
        UiSharedService.TextWrapped("Highlight color can later be changed in the PlayerSync Settings menu.");

        ImGuiHelpers.ScaledDummy(5);
        var enableGroupZoneSyncJoining = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
        if (ImGui.Checkbox("Enable ZoneSync", ref enableGroupZoneSyncJoining))
        {
            Mediator.Publish(new GroupZoneSetEnableState(enableGroupZoneSyncJoining));
            _zoneSyncConfigService.Current.UserHasConfirmedWarning = true;
            _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = enableGroupZoneSyncJoining;
            _zoneSyncConfigService.Save();
        }
        UiSharedService.AttachToolTip("Auto join zone-based Syncshells to see people automatically.");
        UiSharedService.ColorTextWrapped("Enabling ZoneSync will allow you to automatically see others around you in public spaces.", ImGuiColors.DalamudYellow);
        UiSharedService.TextWrapped("Check the Settings -> Pairing Settings for ZoneSync feature details.");
        UiSharedService.TextWrapped("ZoneSync can be toggled off/on at any time.");

        ImGuiHelpers.ScaledDummy(5);

        UiSharedService.ColorTextWrapped("If your PC or Internet are popotos, you may need to limit these settings.", ImGuiColors.DalamudYellow);
        UiSharedService.TextWrapped("Set the sliders to the max if you have a good PC and Internet connection. If you find your game struggling or crashing loading players, you may need to tune these values.");
        UiSharedService.TextWrapped("PlayerSync Settings -> Transfers -> Transfer Settings.");

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        int maxParallelUploads = _configService.Current.ParallelUploads;
        int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;
        ImGuiHelpers.ScaledDummy(5);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Global Download Speed Limit");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
        {
            _configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
            _configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("###speed", [DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps],
            (s) => s switch
            {
                DownloadSpeeds.Bps => "Byte/s",
                DownloadSpeeds.KBps => "KB/s",
                DownloadSpeeds.MBps => "MB/s",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                _configService.Current.DownloadSpeedType = s;
                _configService.Save();
                Mediator.Publish(new DownloadLimitChangedMessage());
            }, _configService.Current.DownloadSpeedType);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("0 = No limit/infinite");

        if (ImGui.SliderInt("Maximum Parallel Downloads", ref maxParallelDownloads, 1, 30))
        {
            _configService.Current.ParallelDownloads = maxParallelDownloads;
            _configService.Save();
        }

        if (ImGui.SliderInt("Maximum Parallel Uploads", ref maxParallelUploads, 1, 10))
        {
            _configService.Current.ParallelUploads = maxParallelUploads;
            _configService.Save();
        }
        UiSharedService.ColorTextWrapped("PlayerSync has near infinite bandwidth available to serve files, the download speed will be subject only to your system and setup.", ImGuiColors.ParsedPurple);
        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.ColorTextWrapped("PlayerSync serves BC7 compressed textures when available by default." + Environment.NewLine +
            "To change this behavior, go to Settings -> Performance -> Auto Texture Compression.", ImGuiColors.DalamudYellow);
    }

    private void GetToSLocalization(int changeLanguageTo = -1)
    {
        if (changeLanguageTo != -1)
        {
            _uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
        }

        _tosParagraphs = [Strings.ToS.Paragraph1, Strings.ToS.Paragraph2, Strings.ToS.Paragraph3, Strings.ToS.Paragraph4, Strings.ToS.Paragraph5, Strings.ToS.Paragraph6];
    }

    [GeneratedRegex("^([A-F0-9]{2})+")]
    private static partial Regex HexRegex();
}