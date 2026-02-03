using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly CacheMonitor _cacheMonitor;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, ConcurrentDictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly HttpClient _httpClient;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly IpcManager _ipcManager;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private readonly IProgress<(int, int, FileCacheEntity)> _validationProgress;
    private readonly IBroadcastManager _broadcastManager;
    private (int, int, FileCacheEntity) _currentProgress;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _lastTab = string.Empty;
    private readonly UiTheme _theme;
    private UiNav.NavItem<SettingsNav>? _selectedNavItem;

    private bool _readClearCache = false;
    private int _selectedEntry = -1;
    private int _selectedHeightEntry = -1;
    private int _selectedOverrideEntry = -1;
    private string _uidToAddForIgnore = string.Empty;
    private string _uidToAddForHeightIgnore = string.Empty;
    private string _uidToAddForOverride = string.Empty;
    private CancellationTokenSource? _validationCts;
    private Task<List<FileCacheEntity>>? _validationTask;
    private bool _wasOpen = false;
    private int _globalControlCountdown = 0;

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        PlayerPerformanceConfigService playerPerformanceConfigService,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        ZoneSyncConfigService zoneSyncConfigService,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager,
        FileCompactor fileCompactor, ApiController apiController,
        IpcManager ipcManager, CacheMonitor cacheMonitor,
        IBroadcastManager broadcastManager,
        DalamudUtilService dalamudUtilService, HttpClient httpClient, UiTheme theme) : base(logger, mediator, "PlayerSync Settings", performanceCollector)
    {
        _configService = configService;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _zoneSyncConfigService = zoneSyncConfigService;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _apiController = apiController;
        _ipcManager = ipcManager;
        _cacheMonitor = cacheMonitor;
        _dalamudUtilService = dalamudUtilService;
        _httpClient = httpClient;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;
        _broadcastManager = broadcastManager;
        _theme = theme;

        _validationProgress = new Progress<(int, int, FileCacheEntity)>(v => _currentProgress = v);

        AllowClickthrough = false;
        AllowPinning = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(1000, 800),
            MaximumSize = new Vector2(1000, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = msg.CharacterData);
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public CharacterData? LastCreatedCharacterData { private get; set; }
    private ApiController ApiController => _uiShared.ApiController;

    private static int FeetInchesToCm(int feet, int inches)
    {
        int totalInches = feet * 12 + inches;
        return (int)Math.Round(totalInches * 2.54f);
    }

    private static void CmToFeetInches(int cm, out int feet, out int inches)
    {
        int totalInches = (int)Math.Round(cm / 2.54f);
        if (totalInches < 0) totalInches = 0;

        feet = totalInches / 12;
        inches = totalInches % 12;
    }

    private async Task GlobalControlCountdown(int countdown)
    {
        _globalControlCountdown = countdown;
        while (_globalControlCountdown > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            _globalControlCountdown--;
        }
        if (_globalControlCountdown < 0)
        {
            _globalControlCountdown = 0;
        }
    }

    protected override void DrawInternal()
    {
        using var _ = _theme.PushWindowStyle();

        _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }

    public override void OnOpen()
    {
        _uiShared.ResetOAuthTasksState();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        _uidToAddForIgnore = string.Empty;
        _uidToAddForHeightIgnore = string.Empty;
        _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
        base.OnClose();
    }

    private enum SettingsNav
    {
        General,
        Pairing,
        Performance,
        Analysis,
        Hub,
        Profile,
        Storage,
        Transfers,
        Service,
        Debug
    }

    private List<(string GroupLabel, IReadOnlyList<UiNav.NavItem<SettingsNav>> Items)>? _navItems;

    private IReadOnlyList<(string GroupLabel, IReadOnlyList<UiNav.NavItem<SettingsNav>> Items)> NavItems => _navItems ??= new()
    {
        ("General", new List<UiNav.NavItem<SettingsNav>>
        {
            new(SettingsNav.General, "Interface", DrawInterfaceSettings, FontAwesomeIcon.Display),
            new(SettingsNav.Pairing, "Sync Settings", DrawSyncSettings, FontAwesomeIcon.Link),
            new(SettingsNav.Performance, "Performance", DrawPerformanceSettings, FontAwesomeIcon.TachometerAlt),
        }),
        ("Character", new List<UiNav.NavItem<SettingsNav>>
        {
            new(SettingsNav.Analysis, "Analysis (Compress Mods)", OpenCharaDataAnalysisUi, FontAwesomeIcon.PersonCircleQuestion),
            new(SettingsNav.Hub, "Hub (MCDF/MCDO)", OpenCharaDataHubUi, FontAwesomeIcon.Running),
            new(SettingsNav.Profile, "Profile", OpenCharaProfileUi, FontAwesomeIcon.UserCircle),
        }),
        ("Data", new List<UiNav.NavItem<SettingsNav>>
        {
            new(SettingsNav.Storage, "Storage", DrawStorageSettings, FontAwesomeIcon.Hdd),
            new(SettingsNav.Transfers, "Transfers", DrawTransferSettings, FontAwesomeIcon.ExchangeAlt),
        }),
        ("Service", new List<UiNav.NavItem<SettingsNav>>
        {
            new(SettingsNav.Service, "Service Settings", DrawServiceSettings, FontAwesomeIcon.Server),
            new(SettingsNav.Debug, "Debug", DrawDebugSettings, FontAwesomeIcon.Bug),
        }),
    };

    private void DrawSettingsContent()
    {
        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.TextUnformatted("Service " + _serverConfigurationManager.CurrentServer!.ServerName + ":");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, "Available");
            ImGui.SameLine();
            ImGui.TextUnformatted("(");
            ImGui.SameLine(0);
            ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine(0);
            ImGui.TextUnformatted("Users Online");
            ImGui.SameLine(0);
            ImGui.TextUnformatted(")");
        }
        else
        {
            ImGui.TextUnformatted("Service:");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudRed, "Unavailable");
        }

        Ui.AddVerticalSpace(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Community and Support:");
        ImGui.SameLine();
        if (ImGui.Button("PlayerSync Discord"))
        {
            Util.OpenLink("https://discord.gg/BzaqbfFFmn");
        }

        ImGuiHelpers.ScaledDummy(5);

        // we could have 'out' the selected item, but it was messy to keep state in ImGui when we wanted to be able to "link" to other windows
        _selectedNavItem = UiNav.DrawSidebar(_theme, "Settings", NavItems, _selectedNavItem, widthPx: 240f, iconFont: _uiShared.IconFont);

        var panePad = UiScale.ScaledFloat(_theme.PanelPad);
        var paneGap = UiScale.ScaledFloat(_theme.PanelGap);

        ImGui.SameLine(0, paneGap);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(panePad, panePad));
        using var pane = ImRaii.Child("##settings-pane", new Vector2(0, 0), false);
        Ui.AddVerticalSpace(2);

        _selectedNavItem.NavAction.Invoke();
    }

    private static bool InputColorPicker(string label, ref SeStringTextColors colors, bool drawDtr = false)
    {
        using var id = ImRaii.PushId(label);
        var innerSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var foregroundColor = ConvertColor(colors.Foreground);
        var glowColor = ConvertColor(colors.Glow);

        var ret = ImGui.ColorEdit3("###foreground", ref foregroundColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);

        if (ImGui.IsItemHovered() && drawDtr)
            ImGui.SetTooltip("Foreground Color - Set to pure black (#000000) to use the default color");

        ImGui.SameLine(0.0f, innerSpacing);
        ret |= ImGui.ColorEdit3("###glow", ref glowColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);
        if (ImGui.IsItemHovered() && drawDtr)
            ImGui.SetTooltip("Glow Color - Set to pure black (#000000) to use the default color");

        ImGui.SameLine(0.0f, innerSpacing);
        ImGui.TextUnformatted(label);

        if (ret)
            colors = new(ConvertBackColor(foregroundColor), ConvertBackColor(glowColor));

        return ret;

        static Vector3 ConvertColor(uint color)
            => unchecked(new((byte)color / 255.0f, (byte)(color >> 8) / 255.0f, (byte)(color >> 16) / 255.0f));

        static uint ConvertBackColor(Vector3 color)
            => byte.CreateSaturating(color.X * 255.0f) | ((uint)byte.CreateSaturating(color.Y * 255.0f) << 8) | ((uint)byte.CreateSaturating(color.Z * 255.0f) << 16);
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void OpenCharaDataAnalysisUi()
    {
        _selectedNavItem = null;
        Mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        this.IsOpen = false;
    }

    private void OpenCharaDataHubUi()
    {
        _selectedNavItem = null;
        Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        this.IsOpen = false;
    }

    private void OpenCharaProfileUi()
    {
        _selectedNavItem = null;
        Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
        this.IsOpen = false;
    }
}
