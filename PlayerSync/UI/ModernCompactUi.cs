using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.Themes;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;

namespace MareSynchronos.UI;

public class ModernCompactUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, ConcurrentDictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DrawEntityFactory _drawEntityFactory;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly IBroadcastManager _broadcastManager;
    private readonly SelectTagForPairUi _selectGroupForPairUi;
    private readonly SelectPairForTagUi _selectPairsForGroupUi;
    private readonly ServerConfigurationManager _serverManager;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private readonly ThemeManager _themeManager;
    private readonly LeftNavigation _leftNavigation;
    private readonly HoverToolbar _hoverToolbar;
    private readonly IpcManager _ipcManager;
    
    private List<IDrawFolder> _drawFolders;
    private DrawFolderBroadcasts? _broadcastsFolder;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private bool _showModalForUserAddition;
    private float _transferPartHeight;
    private bool _wasOpen;
    private float _windowContentWidth;

    public ModernCompactUi(ILogger<ModernCompactUi> logger, UiSharedService uiShared, MareConfigService configService, 
        ApiController apiController, PairManager pairManager, IBroadcastManager broadcastManager,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager,
        TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, 
        SelectPairForTagUi selectPairForTagUi, PerformanceCollectorService performanceCollectorService,
        ThemeManager themeManager, IpcManager ipcManager)
        : base(logger, mediator, "###MareSynchronosModernUI", performanceCollectorService)
    {
        _uiSharedService = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _broadcastManager = broadcastManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _tagHandler = tagHandler;
        _drawEntityFactory = drawEntityFactory;
        _selectGroupForPairUi = selectTagForPairUi;
        _selectPairsForGroupUi = selectPairForTagUi;
        _themeManager = themeManager;
        _ipcManager = ipcManager;

        // Initialize components
        _leftNavigation = new LeftNavigation(mediator, apiController, pairManager, broadcastManager, 
            uiShared, configService, themeManager);
        _hoverToolbar = new HoverToolbar(themeManager, uiShared);

        AllowClickthrough = false;
        
        // Remove title bar buttons since we're using custom toolbar
        TitleBarButtons = new();

        _drawFolders = GetDrawFolders().ToList();

        // Set window name based on version
#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Player Sync {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosModernUI";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "Player Sync " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosModernUI";
#endif

        // Subscribe to events
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
        Mediator.Subscribe<RefreshUiMessage>(this, (msg) =>
        {
            _drawFolders = GetDrawFolders().ToList();
            _broadcastsFolder = GetBroadcastsFolder();
        });

        // Apply theme
        _themeManager.ThemeChanged += OnThemeChanged;
        ApplyCurrentTheme();

        // Configure window
        Flags |= ImGuiWindowFlags.NoDocking;
        if (_configService.Current.CurvedWindows)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar;
        }

        var minWidth = _configService.Current.UseLeftNavigation ? _configService.Current.NavigationWidth + 200f : 375f;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(minWidth, 400),
            MaximumSize = new Vector2(minWidth + 200f, 2000),
        };
    }

    protected override void DrawInternal()
    {
        var theme = _themeManager.CurrentTheme;
        _windowContentWidth = UiSharedService.GetWindowContentRegionWidth();

        // Apply theme styling
        using (ApplyThemeStyle(theme))
        {
            // Update hover toolbar
            if (_configService.Current.ShowHoverToolbar)
            {
                var windowPos = ImGui.GetWindowPos();
                var windowSize = ImGui.GetWindowSize();
                var isHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
                _hoverToolbar.Update(windowPos, windowSize, isHovered);
            }

            DrawMainContent();

            // Draw hover toolbar
            if (_configService.Current.ShowHoverToolbar)
            {
                _hoverToolbar.Draw();
            }
        }

        // Handle modals and popups
        DrawUserAdditionModal();
        
        // Track window changes
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }
    }

    private ImRaii.Color ApplyThemeStyle(Theme theme)
    {
        // Apply custom window styling for curved appearance
        if (_configService.Current.CurvedWindows)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, theme.WindowRounding);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, theme.ChildRounding);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, theme.FrameRounding);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, theme.WindowBorderSize);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, theme.WindowPadding);
        }

        return ImRaii.PushColor(ImGuiCol.WindowBg, theme.Background);
    }

    private void DrawMainContent()
    {
        // Draw version/connection warnings first
        DrawVersionWarnings();
        DrawMissingPluginsWarning();

        if (!_apiController.IsCurrentVersion || !_ipcManager.Initialized)
        {
            return;
        }

        // Draw connection status
        using (ImRaii.PushId("header")) DrawUIDHeader();
        ImGui.Separator();
        using (ImRaii.PushId("serverstatus")) DrawServerStatus();
        ImGui.Separator();

        if (_apiController.ServerState is ServerState.Connected)
        {
            if (_configService.Current.UseLeftNavigation)
            {
                DrawWithLeftNavigation();
            }
            else
            {
                DrawWithTopNavigation();
            }
        }
    }

    private void DrawWithLeftNavigation()
    {
        var navWidth = _configService.Current.NavigationWidth;
        var contentWidth = _windowContentWidth - navWidth - ImGui.GetStyle().ItemSpacing.X;

        // Left navigation panel
        using (ImRaii.PushId("left-nav"))
        {
            _leftNavigation.Draw(navWidth);
        }

        ImGui.SameLine();

        // Main content area
        using (ImRaii.PushId("main-content"))
        using (var contentChild = ImRaii.Child("MainContent", new Vector2(contentWidth, -1)))
        {
            if (contentChild)
            {
                DrawPairsContent();
                ImGui.Separator();
                DrawTransfers();
            }
        }

        // Draw popups
        using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
        using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
    }

    private void DrawWithTopNavigation()
    {
        var theme = _themeManager.CurrentTheme;
        
        // Simple fallback - just show basic info
        ImGui.TextUnformatted("Mare Synchronos");
        ImGui.Separator();
        
        using (ImRaii.PushId("pairlist")) DrawPairsContent();
        ImGui.Separator();
        using (ImRaii.PushId("transfers")) DrawTransfers();
    }

    private void DrawPairsContent()
    {
        var ySize = _transferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y
                + ImGui.GetTextLineHeight() - ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().WindowBorderSize) - _transferPartHeight - ImGui.GetCursorPosY();

        ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), border: false);

        _broadcastsFolder?.Draw();

        foreach (var item in _drawFolders)
        {
            item.Draw();
        }

        ImGui.EndChild();
    }

    private void DrawVersionWarnings()
    {
        if (!_apiController.IsCurrentVersion)
        {
            var theme = _themeManager.CurrentTheme;
            var ver = _apiController.CurrentClientVersion;
            var unsupported = "UNSUPPORTED VERSION";
            
            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                
                using (theme.PushThemedColor(ImGuiCol.Text))
                {
                    ImGui.TextUnformatted(unsupported);
                }
            }
            
            UiSharedService.ColorTextWrapped($"Your Player Sync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep Player Sync up to date. Open /xlplugins and update the plugin.", _themeManager.CurrentTheme.Error);
        }
    }

    private void DrawMissingPluginsWarning()
    {
        if (!_ipcManager.Initialized)
        {
            var theme = _themeManager.CurrentTheme;
            var unsupported = "MISSING ESSENTIAL PLUGINS";

            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                
                using (theme.PushThemedColor(ImGuiCol.Text))
                {
                    ImGui.TextUnformatted(unsupported);
                }
            }
            
            var penumAvailable = _ipcManager.Penumbra.APIAvailable;
            var glamAvailable = _ipcManager.Glamourer.APIAvailable;

            UiSharedService.ColorTextWrapped($"One or more Plugins essential for Player Sync operation are unavailable. Enable or update following plugins:", theme.Error);
            using var indent = ImRaii.PushIndent(10f);
            if (!penumAvailable)
            {
                UiSharedService.TextWrapped("Penumbra");
                _uiSharedService.BooleanToColoredIcon(penumAvailable, true);
            }
            if (!glamAvailable)
            {
                UiSharedService.TextWrapped("Glamourer");
                _uiSharedService.BooleanToColoredIcon(glamAvailable, true);
            }
            ImGui.Separator();
        }
    }

    // Copy existing methods from CompactUI (DrawServerStatus, DrawUIDHeader, DrawTransfers, etc.)
    // These are omitted for brevity but would need to be included

    private void DrawServerStatus()
    {
        var theme = _themeManager.CurrentTheme;
        ImGui.TextUnformatted("Server: Connected");
    }

    private void DrawUIDHeader()
    {
        var theme = _themeManager.CurrentTheme;
        ImGui.TextUnformatted("Mare Synchronos - Modern UI");
    }

    private void DrawTransfers()
    {
        var theme = _themeManager.CurrentTheme;
        ImGui.TextUnformatted("File Transfers");
    }

    private void DrawUserAdditionModal()
    {
        // Implementation from original CompactUI
    }

    private void OnThemeChanged(Theme newTheme)
    {
        ThemeApplier.ApplyTheme(newTheme);
    }

    private void ApplyCurrentTheme()
    {
        ThemeApplier.ApplyTheme(_themeManager.CurrentTheme);
    }

    private List<IDrawFolder> GetDrawFolders()
    {
        return new List<IDrawFolder>();
    }

    private DrawFolderBroadcasts? GetBroadcastsFolder()
    {
        return null;
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _themeManager.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }
}