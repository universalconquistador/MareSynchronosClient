using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Models;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class HelpUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PairManager _pairManager;
    private readonly EventAggregator _eventAggregator;
    private readonly UiSharedService _uiShared;
    private readonly MareConfigService _mareConfigService;

    private readonly Progress<(DiagnosticsTestState State, string Status)> _diagnosticsProgress = new();
    private readonly HttpClientProvider _httpClientProvider;
    private readonly ConcurrentQueue<(DiagnosticsTestState State, string Status)> _pendingResultTexts = new();
    private CancellationTokenSource? _diagnosticsCancellationTokenSource;
    private Task? _diagnosticsRunTask;
    private bool _isDiagTaskRunning = false;
    private readonly List<(DiagnosticsTestState State, string Status)> _resultTexts = new();
    private string _finalResults = "";

    
    private readonly UiTheme _theme;
    private UiNav.NavItem<DiagnosticsNav>? _selectedNavItem;

    public HelpUi(ILogger<HelpUi> logger, MareMediator mediator, PairManager pairmanager, UiSharedService uiSharedService, EventAggregator eventAggregator,
        ApiController apiController, ServerConfigurationManager serverConfigurationManager, MareConfigService mareConfigService,
        PerformanceCollectorService performanceCollectorService, HttpClientProvider httpClientProvider, UiTheme theme)
        : base(logger, mediator, "PlayerSync Help", performanceCollectorService)
    {
        _apiController = apiController;
        _serverConfigurationManager = serverConfigurationManager;
        _pairManager = pairmanager;
        _eventAggregator = eventAggregator;
        _mareConfigService = mareConfigService;
        _httpClientProvider = httpClientProvider;
        _uiShared = uiSharedService;
        _theme = theme;

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 600,
                Y = 400
            },
            MaximumSize = new()
            {
                X = 2000,
                Y = 2000
            }
        };

        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => _cachedPairsDetail.Clear());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => _cachedPairsDetail.Clear());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _cachedPairsDetail.Clear());

        //Flags |= ImGuiWindowFlags.NoResize;

        // network diag
        _diagnosticsProgress.ProgressChanged += DiagnosticsProgress_ProgressChanged;

        // event log
        _filteredEvents = RecreateFilter();
    }

    private enum DiagnosticsNav
    {
        Guides,
        Events,
        Network,
        Character,
        Pairs
    }

    private List<(string GroupLabel, IReadOnlyList<UiNav.NavItem<DiagnosticsNav>> Items)>? _navItems;

    private IReadOnlyList<(string GroupLabel, IReadOnlyList<UiNav.NavItem<DiagnosticsNav>> Items)> NavItems => _navItems ??= new()
    {
        ("Information", new List<UiNav.NavItem<DiagnosticsNav>>
        {
            //new(DiagnosticsNav.Guides, "Guides", DrawDiagnosticsInformation, FontAwesomeIcon.Info),
            new(DiagnosticsNav.Events, "Event Log", DrawDiagnosticsEvents, FontAwesomeIcon.Book),
        }),
        ("Troubleshooting", new List<UiNav.NavItem<DiagnosticsNav>>
        {
            new(DiagnosticsNav.Network, "Network", DrawDiagnosticsNetwork, FontAwesomeIcon.NetworkWired),
            //new(DiagnosticsNav.Character, "Your Character Data", DrawDiagnosticsCharacter, FontAwesomeIcon.CloudUploadAlt),
            new(DiagnosticsNav.Pairs, "Their Pair Data", DrawDiagnosticsPairs, FontAwesomeIcon.CloudDownloadAlt),
        }),
    };

    private void DrawHelpContent()
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
            ImGui.SameLine(0);
            string connection = _serverConfigurationManager.CurrentApiUrl.Replace("wss://", "").Replace("gw-", "");
            ImGui.TextUnformatted($"Connected via {connection}");
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

        _selectedNavItem = UiNav.DrawSidebar(_theme, "Diagnostics", NavItems, _selectedNavItem, widthPx: 240f, iconFont: _uiShared.IconFont);

        var panePad = UiScale.ScaledFloat(_theme.PanelPad);
        var paneGap = UiScale.ScaledFloat(_theme.PanelGap);

        ImGui.SameLine(0, paneGap);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(panePad, panePad));
        using var pane = ImRaii.Child("##diagnostics-pane", new Vector2(0, 0), false);
        Ui.AddVerticalSpace(2);

        try
        {
            _selectedNavItem.NavAction.Invoke();
        }
        catch { }

    }

    public override void OnOpen()
    {
        OnOpenEventsWindow();
        OnOpenPairDataWindow();

        base.OnOpen();
    }

    protected override void DrawInternal()
    {
        using var _ = _theme.PushWindowStyle();

        _uiShared.DrawOtherPluginState();

        DrawHelpContent();
    }

    public override void OnClose()
    {
        base.OnClose();

        OnClosePairDataWindow();
        CancelDiagnostics();
        _resultTexts.Clear();
        _pendingResultTexts.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _resultTexts.Clear();
        _pendingResultTexts.Clear();
        CancelDiagnostics();
        _diagnosticsProgress.ProgressChanged -= DiagnosticsProgress_ProgressChanged;
    }
}
