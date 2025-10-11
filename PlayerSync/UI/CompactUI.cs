using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private readonly ConcurrentDictionary<GameObjectHandler, ConcurrentDictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DrawEntityFactory _drawEntityFactory;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly IBroadcastManager _broadcastManager;
    private readonly SelectTagForPairUi _selectGroupForPairUi;
    private readonly SelectPairForTagUi _selectPairsForGroupUi;
    private readonly IpcManager _ipcManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly TopTabMenu _tabMenu;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private List<IDrawFolder> _drawFolders;
    private DrawFolderBroadcasts? _broadcastsFolder;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private int _secretKeyIdx = -1;
    private bool _showModalForUserAddition;
    private bool _showThemeEditor;
    private float _transferPartHeight;
    private bool _wasOpen;
    private bool _collapsed = false;
    private bool _expanded = false;
    private bool _appliedThemeThisFrame;
    private float _windowContentWidth;
    private IDisposable? _theme;

    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ZoneSyncConfigService zoneSyncConfigService,  
        ApiController apiController, PairManager pairManager, IBroadcastManager broadcastManager,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager,
        TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, SelectPairForTagUi selectPairForTagUi,
        PerformanceCollectorService performanceCollectorService, IpcManager ipcManager)
        : base(logger, mediator, "###PlayerSyncMainUI", performanceCollectorService)
    {
        _uiSharedService = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _zoneSyncConfigService = zoneSyncConfigService;
        _broadcastManager = broadcastManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _tagHandler = tagHandler;
        _drawEntityFactory = drawEntityFactory;
        _selectGroupForPairUi = selectTagForPairUi;
        _selectPairsForGroupUi = selectPairForTagUi;
        _ipcManager = ipcManager;
        _tabMenu = new TopTabMenu(Mediator, _apiController, _pairManager, _broadcastManager, _uiSharedService, _configService, _zoneSyncConfigService);

        AllowClickthrough = false;
        AllowPinning = false;

        TitleBarButtons = new()
            {
                new TitleBarButton()
                {
                    Icon = FontAwesomeIcon.Cog,
                    Click = (msg) =>
                    {
                        Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
                    },
                    IconOffset = new(2,1),
                    ShowTooltip = () =>
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Open PlayerSync Settings");
                        ImGui.EndTooltip();
                    }
                },
                new TitleBarButton()
                {
                    Icon = FontAwesomeIcon.Book,
                    Click = (msg) =>
                    {
                        Mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
                    },
                    IconOffset = new(2,1),
                    ShowTooltip = () =>
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Open Mare PlayerSync Viewer");
                        ImGui.EndTooltip();
                    }
                }

            };

        WindowSetup();

        _drawFolders = GetDrawFolders().ToList();

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"PlayerSync {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###PlayerSyncMainUI";
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "PlayerSync " + ver.Major + "." + ver.Minor + "." + ver.Build + "###PlayerSyncMainUI";
#endif
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
        
        Mediator.Subscribe<CloseWindowMessage>(this, (msg) =>
        {
            IsOpen = false;
        });
        Mediator.Subscribe<ToggleThemeEditorMessage>(this, (msg) =>
        {
            _showThemeEditor = !_showThemeEditor;
        });

        if (_configService.Current.ShowUIOnPluginLoad) Toggle();
    }

    private bool NewUI => _uiSharedService.NewUI;

    private void WindowSetup()
    {
        if (NewUI)
        {
            SizeConstraints = _uiSharedService.ThemeManager.CompactUISizeConstraints;
            Mediator.Subscribe<ToggleCollapseMessage>(this, (msg) =>
            {
                if (_collapsed)
                {
                    SizeConstraints = _uiSharedService.ThemeManager.CompactUICollapsedSizeConstraints;
                    // We can't change ImGui from here, so we set a flag to handle it after
                    _expanded = true;
                }
                SizeConstraints = _uiSharedService.ThemeManager.CompactUISizeConstraints;
                _collapsed = !_collapsed;
            });
        }
        else
        {
            SizeConstraints = _uiSharedService.ThemeManager.ClassicUISizeConstraints;
            Mediator.Unsubscribe<ToggleCollapseMessage>(this);
        }
    }

    public override void PreDraw()
    {
        // Some things have to be pushed before the draw method is called.
        var themeManager = _uiSharedService.ThemeManager;
        var theme = themeManager.Current;

        if (_collapsed) Flags |= ImGuiWindowFlags.NoResize;
        _appliedThemeThisFrame = !themeManager.UsingDalamudTheme;
        if (_appliedThemeThisFrame)
        {
            _theme = _uiSharedService.ThemeManager.PushTheme();

            // Colors
            ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.PanelBg);
            ImGui.PushStyleColor(ImGuiCol.Border, theme.PanelBorder);
        }

        // Styles
        float windowRounding = NewUI ? 12.0f : 4.0f;
        float windowPaddingX = NewUI ? 12.0f : 8.0f;
        float windowPaddingY = NewUI ? 4.0f : 8.0f;
        float borderSize = NewUI ? 1f : 0f;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, windowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(windowPaddingX, windowPaddingY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderSize);

        base.PreDraw();
    }

    public override void PostDraw()
    {
        if (_appliedThemeThisFrame)
        {
            ImGui.PopStyleColor(2);
            _theme?.Dispose();
        }
        ImGui.PopStyleVar(3);
        _theme = null;
        _appliedThemeThisFrame = false;

        base.PostDraw();
    }

    public override void Draw()
    {
        if (NewUI)
        {
            if (_expanded)
            {
                SizeConstraints = _uiSharedService.ThemeManager.CompactUISizeConstraints;
                ImGui.SetWindowSize(_lastSize);
                _expanded = false;
            }
            if (_collapsed)
            {
                SizeConstraints = _uiSharedService.ThemeManager.CompactUICollapsedSizeConstraints;
            }
        }
        else
        {
            SizeConstraints = _uiSharedService.ThemeManager.ClassicUISizeConstraints;
            _uiSharedService.ThemeManager.Current.ChildRounding = 0f;
        }
            
        base.Draw();
    }

    protected override void DrawInternal()
    {
        UpdateWindowFlags();
        if (NewUI)
        {
            if (_collapsed)
            {
                DrawTitleBar();
                return;
            }
            DrawThemeWindow();
        }
        else
        {
            DrawClassicWindow();
        }
}

    private void DrawThemeEditor()
    {
        //var startPos = ImGui.GetCursorPos();
        //startPos.Y += 20f * ImGuiHelpers.GlobalScale;
        //ImGui.SetCursorPos(startPos);
        
        using (ImRaii.PushId("themeeditor"))
        {
            using (_uiSharedService.UidFont.Push())
            {
                var header = "Theme Editor";
                var headerTextSize = ImGui.CalcTextSize(header);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - (headerTextSize.X / 2));
                ImGui.AlignTextToFramePadding();
                ImGui.Text(header);
            }
            ImGui.Separator();
            ImGui.Dummy(new Vector2(5));
            var useUIThemeMode = _configService.Current.NewUI;
            if (ImGui.Checkbox("Enable Alternate UI", ref useUIThemeMode))
            {
                _configService.Current.NewUI = useUIThemeMode;
                _configService.Save();
                WindowSetup();
            }
            var useDalamud = _uiSharedService.ThemeManager.UsingDalamudTheme;
            var config = _uiSharedService.ThemeManager.UIThemeConfig;
            if (ImGui.Checkbox("Sync Dalamud Color Theme", ref useDalamud))
            {
                config.Current.UseDalamudTheme = useDalamud;
                config.Save();
                WindowSetup();
            }
            ImGui.Dummy(new Vector2(5));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(5));
            _uiSharedService.ThemeEditor.Draw();

            if (_uiSharedService.ThemeEditor.CloseRequested)
            {
                _showThemeEditor = false;
                _uiSharedService.ThemeEditor.ResetCloseRequest();
            }
        }
    }

    private void DrawClassicWindow()
    {
        if (_showThemeEditor)
        {
            DrawThemeEditor();
        }
        else
        {
            DrawContent();
        }
    }

    private void DrawThemeWindow()
    {
        ImGui.BeginChild("content-with-padding", new Vector2(0, 0), false, ImGuiWindowFlags.NoBackground);

        DrawTitleBar();

        var startPos = ImGui.GetCursorPos();
        float headerHeight = 10f * ImGuiHelpers.GlobalScale;
        startPos.Y += headerHeight / 2f + ImGui.GetStyle().WindowPadding.Y;
        ImGui.SetCursorPos(startPos);
        if (_showThemeEditor)
        {
            DrawThemeEditor();
        }
        else
        {
            DrawContent();
        }

        ImGui.EndChild();
        ImGui.EndChild();

        _lastSize = ImGui.GetWindowSize();
    }

    private void DrawTitleBar()
    {
        ImGui.BeginChild("title-bar", new Vector2(0, 0), false, ImGuiWindowFlags.NoBackground);

        var startPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + ImGui.GetStyle().WindowPadding.Y / 2));
        ImGui.TextUnformatted(WindowName.Split("###")[0]);

        float btnSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Times).X;
        var totalButtonsWidth = btnSize * 3 + _uiSharedService.ThemeManager.ScaledSpacing * 2;

        ImGui.SameLine(UiSharedService.GetWindowContentRegionWidth() - totalButtonsWidth);
        DrawTitleBarButtons();
    }

    private void DrawTitleBarButtons()
    {
        var size = ImGui.GetFrameHeight() * .85f;
        if (_uiSharedService.IconButton(FontAwesomeIcon.Bars, size))
        {
            ImGui.OpenPopup("##PlayerSyncHamburgerMenu");
        }
        _uiSharedService.AttachToolTip("PlayerSync Menu");

        ImGui.SameLine(0, _uiSharedService.ThemeManager.ScaledSpacing);

        var collapseIcon = _collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp;
        if (_uiSharedService.IconButton(collapseIcon, size))
        {
            Mediator.Publish(new ToggleCollapseMessage());
        }
        _uiSharedService.AttachToolTip(_collapsed ? "Expand" : "Collapse");

        ImGui.SameLine(0, _uiSharedService.ThemeManager.ScaledSpacing);

        if (_uiSharedService.IconButton(FontAwesomeIcon.Times, size))
        {
            Mediator.Publish(new CloseWindowMessage());
        }
        _uiSharedService.AttachToolTip("Close Window");

        DrawHamburgerMenuPopup();
    }

    private void DrawHamburgerMenuPopup()
    {
        if (ImGui.BeginPopup("##PlayerSyncHamburgerMenu"))
        {
            // Window Controls
            bool isPinned = AllowPinning;
            if (ImGui.MenuItem($"{FontAwesomeIcon.Thumbtack.ToIconString()}  Pin Window", "", isPinned))
            {
                AllowPinning = !AllowPinning;
            }

            bool isClickThrough = AllowClickthrough;
            if (ImGui.MenuItem($"{FontAwesomeIcon.MousePointer.ToIconString()}  Click Through", "", isClickThrough))
            {
                AllowClickthrough = !AllowClickthrough;
                if (AllowClickthrough)
                {
                    // Auto-enable pin window when click-through is enabled
                    AllowPinning = true;
                }
            }

            ImGui.Separator();

            // Event Viewer
            if (ImGui.MenuItem($"{FontAwesomeIcon.Book.ToIconString()}  Event Viewer"))
            {
                Mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
            }

            //// Additional menu items
            //if (ImGui.MenuItem($"{FontAwesomeIcon.Users.ToIconString()}  Pair Management"))
            //{
            //    Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            //}
            //ImGui.EndPopup();

            //if (ImGui.MenuItem($"{FontAwesomeIcon.BroadcastTower.ToIconString()}  Broadcast Options"))
            //{
            //    // Add broadcast functionality
            //}

            ImGui.EndPopup();
        }
    }

    private void UpdateWindowFlags()
    {
        if (!NewUI)
        {
            Flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBringToFrontOnFocus;
            return;
        }
        
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (_collapsed)
        {
            Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        }

        if (AllowPinning) // Don't do this, need to fix
            Flags |= ImGuiWindowFlags.NoMove;

        if (AllowClickthrough) // Don't do this, need to fix
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing;
    }

    private void DrawContent()
    {
        var theme = _uiSharedService.Theme;
        _windowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        if (!_apiController.IsCurrentVersion)
        {
            var ver = _apiController.CurrentClientVersion;
            var unsupported = "UNSUPPORTED VERSION";
            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(theme.StatusError, unsupported);
            }
            UiSharedService.ColorTextWrapped($"Your PlayerSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep PlayerSync up to date. Open /xlplugins and update the plugin.", theme.StatusError);
        }

        if (!_ipcManager.Initialized)
        {
            var unsupported = "MISSING ESSENTIAL PLUGINS";

            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(theme.StatusError, unsupported);
            }
            var penumAvailable = _ipcManager.Penumbra.APIAvailable;
            var glamAvailable = _ipcManager.Glamourer.APIAvailable;

            UiSharedService.ColorTextWrapped($"One or more Plugins essential for PlayerSync operation are unavailable. Enable or update following plugins:", theme.StatusError);
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

        using (ImRaii.PushId("header")) DrawUIDHeader();
        ImGui.Separator();
        using (ImRaii.PushId("serverstatus")) DrawServerStatus();
        ImGui.Separator();

        if (_apiController.ServerState is ServerState.Connected)
        {
            using (ImRaii.PushId("global-topmenu")) _tabMenu.Draw();
            using (ImRaii.PushId("pairlist")) DrawPairs();
            ImGui.Separator();
            float pairlistEnd = ImGui.GetCursorPosY();
            using (ImRaii.PushId("transfers")) DrawTransfers();
            _transferPartHeight = ImGui.GetCursorPosY() - pairlistEnd - ImGui.GetTextLineHeight();
            using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
            using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
        }
        

        if (_configService.Current.OpenPopupOnAdd && _pairManager.LastAddedUser != null)
        {
            _lastAddedUser = _pairManager.LastAddedUser;
            _pairManager.LastAddedUser = null;
            ImGui.OpenPopup("Set Notes for New User");
            _showModalForUserAddition = true;
            _lastAddedUserComment = string.Empty;
        }

        if (ImGui.BeginPopupModal("Set Notes for New User", ref _showModalForUserAddition, UiSharedService.PopupWindowFlags))
        {
            if (_lastAddedUser == null)
            {
                _showModalForUserAddition = false;
            }
            else
            {
                UiSharedService.TextWrapped($"You have successfully added {_lastAddedUser.UserData.AliasOrUID}. Set a local note for the user in the field below:");
                ImGui.InputTextWithHint("##noteforuser", $"Note for {_lastAddedUser.UserData.AliasOrUID}", ref _lastAddedUserComment, 100);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Note"))
                {
                    _serverManager.SetNoteForUid(_lastAddedUser.UserData.UID, _lastAddedUserComment);
                    _lastAddedUser = null;
                    _lastAddedUserComment = string.Empty;
                    _showModalForUserAddition = false;
                }
            }
            UiSharedService.SetScaledWindowSize(275);
            ImGui.EndPopup();
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(size, pos));
        }
    }

    private void DrawPairs()
    {
        var scaler = ImGuiHelpers.GlobalScale <= 1.5 ? 40 : 50;
        var offset = NewUI ? scaler * ImGuiHelpers.GlobalScale : scaler;
        var ySize = _transferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y
                + ImGui.GetTextLineHeight() - ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().WindowBorderSize) - _transferPartHeight - ImGui.GetCursorPosY() - offset;

        ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), border: false, ImGuiWindowFlags.NoBackground);

        _broadcastsFolder?.Draw();

        foreach (var item in _drawFolders)
        {
            item.Draw();
        }

        ImGui.EndChild();

    }
    private void DrawServerStatus()
    {
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        var theme = _uiSharedService.Theme;
        bool newUI = _uiSharedService.NewUI;
//#if DEBUG
//        string shardConnection = $"Shard: {_apiController.ServerInfo.ShardName}";
//#else
//                string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
//#endif
        //var shardTextSize = ImGui.CalcTextSize(shardConnection);
        //var printShard = !string.IsNullOrEmpty(_apiController.ServerInfo.ShardName) && shardConnection != string.Empty;

        // Align status text to the left side with some padding (like UID)
        //ImGui.SetCursorPosX(ImGui.GetStyle().FramePadding.X);
        //ImGui.AlignTextToFramePadding();

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(theme.UsersOnlineNumber, userCount);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(theme.UsersOnlineText, " Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(theme.StatusError, "Not connected to any server");
        }

        bool isConnectingOrConnected = _apiController.ServerState is ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting;
        var color = UiSharedService.GetBoolColor(!isConnectingOrConnected);
        var connectedIcon = isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalButtonsWidth = !newUI ? buttonSize.X : (buttonSize.X * 3f + spacing * 2f);

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - totalButtonsWidth);

        if (_apiController.ServerState is not (ServerState.Reconnecting or ServerState.Disconnecting))
        {
            if (NewUI)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, theme.BtnText);
                if (_uiSharedService.IconButton(FontAwesomeIcon.Cog))
                {
                    Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
                }
                ImGui.PopStyleColor();
                _uiSharedService.AttachToolTip("Open PlayerSync Settings");
            }

            // Palette/Theme Editor button
            var offset = buttonSize.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SameLine(!NewUI ? ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - offset : 0);

            ImGui.PushStyleColor(ImGuiCol.Text, theme.BtnText);
            if (_uiSharedService.IconButton(FontAwesomeIcon.Palette))
            {
                Mediator.Publish(new ToggleThemeEditorMessage());
            }
            ImGui.PopStyleColor();
            _uiSharedService.AttachToolTip("Open Theme Editor");

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                if (_uiSharedService.IconButton(connectedIcon))
                {
                    if (isConnectingOrConnected && !_serverManager.CurrentServer.FullPause)
                    {
                        _serverManager.CurrentServer.FullPause = true;
                        _serverManager.Save();
                    }
                    else if (!isConnectingOrConnected && _serverManager.CurrentServer.FullPause)
                    {
                        _serverManager.CurrentServer.FullPause = false;
                        _serverManager.Save();
                    }

                    _ = _apiController.CreateConnectionsAsync();
                }
            }
            _uiSharedService.AttachToolTip(isConnectingOrConnected ? "Disconnect from " + _serverManager.CurrentServer.ServerName : "Connect to " + _serverManager.CurrentServer.ServerName);
        }
    }

    private void DrawTransfers()
    {
        var currentUploads = _fileTransferManager.CurrentUploadList;
        ImGui.AlignTextToFramePadding();
        if (_configService.Current.DebugThrottleUploads)
        {
            _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle);
            _uiSharedService.AttachToolTip("You have upload throttling enabled, which is artificially slowing your uploads.\nYou can turn this off in Settings > Debug.");
        }
        else
        {
            _uiSharedService.IconText(FontAwesomeIcon.Upload);
        }
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.TextUnformatted($"{totalUploads} remaining");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(uploadText);
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("No uploads in progress");
        }

        var currentDownloads = _currentDownloads.SelectMany(d => d.Value.Values).ToList();
        ImGui.AlignTextToFramePadding();
        _uiSharedService.IconText(FontAwesomeIcon.Download);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentDownloads.Any())
        {
            var totalDownloads = currentDownloads.Sum(c => c.TotalFiles);
            var doneDownloads = currentDownloads.Sum(c => c.TransferredFiles);
            var totalDownloaded = currentDownloads.Sum(c => c.TransferredBytes);
            var totalToDownload = currentDownloads.Sum(c => c.TotalBytes);

            ImGui.TextUnformatted($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiSharedService.ByteToString(totalDownloaded)}/{UiSharedService.ByteToString(totalToDownload)})";
            var textSize = ImGui.CalcTextSize(downloadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(downloadText);
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("No downloads in progress");
        }
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();

        using (_uiSharedService.UidFont.Push())
        {
            var uidTextSize = ImGui.CalcTextSize(uidText);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - (uidTextSize.X / 2));
            ImGui.TextColored(GetUidColor(), uidText);
        }

        if (_apiController.ServerState is ServerState.Connected)
        {
            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(_apiController.DisplayName);
            }
            _uiSharedService.AttachToolTip("Click to copy");

            if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
            {
                var origTextSize = ImGui.CalcTextSize(_apiController.UID);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - (origTextSize.X / 2));
                ImGui.TextColored(GetUidColor(), _apiController.UID);
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(_apiController.UID);
                }
                _uiSharedService.AttachToolTip("Click to copy");
            }
        }
        else
        {
            UiSharedService.ColorTextWrapped(GetServerError(), GetUidColor());
        }
    }

    private IEnumerable<IDrawFolder> GetDrawFolders()
    {
        List<IDrawFolder> drawFolders = [];

        var allPairs = _pairManager.PairsWithGroups
            .ToDictionary(k => k.Key, k => k.Value);
        var filteredPairs = allPairs
            .Where(p =>
            {
                if (_tabMenu.Filter.IsNullOrEmpty()) return true;
                return p.Key.UserData.AliasOrUID.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase) ||
                       (p.Key.GetNote()?.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (p.Key.PlayerName?.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase) ?? false);
            })
            .ToDictionary(k => k.Key, k => k.Value);

        string? AlphabeticalSort(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => (_configService.Current.ShowCharacterNameInsteadOfNotesForVisible && !string.IsNullOrEmpty(u.Key.PlayerName)
                    ? (_configService.Current.PreferNotesOverNamesForVisible ? u.Key.GetNote() : u.Key.PlayerName)
                    : (u.Key.GetNote() ?? u.Key.UserData.AliasOrUID));
        bool FilterOnlineOrPausedSelf(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => (u.Key.IsOnline || (!u.Key.IsOnline && !_configService.Current.ShowOfflineUsersSeparately)
                    || u.Key.UserPair.OwnPermissions.IsPaused());
        Dictionary<Pair, List<GroupFullInfoDto>> BasicSortedDictionary(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u)
            => u.OrderByDescending(u => u.Key.IsVisible)
                .ThenByDescending(u => u.Key.IsOnline)
                .ThenBy(AlphabeticalSort, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(u => u.Key, u => u.Value);
        ImmutableList<Pair> ImmutablePairList(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u)
            => u.Select(k => k.Key).ToImmutableList();
        bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => u.Key.IsVisible
                && (_configService.Current.ShowSyncshellUsersInVisible || !(!_configService.Current.ShowSyncshellUsersInVisible && !u.Key.IsDirectlyPaired));
        bool FilterTagusers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, string tag)
            => u.Key.IsDirectlyPaired && !u.Key.IsOneSidedPair && _tagHandler.HasTag(u.Key.UserData.UID, tag);
        bool FilterGroupUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, GroupFullInfoDto group)
            => u.Value.Exists(g => string.Equals(g.GID, group.GID, StringComparison.Ordinal));
        bool FilterNotTaggedUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => u.Key.IsDirectlyPaired && !u.Key.IsOneSidedPair && !_tagHandler.HasAnyTag(u.Key.UserData.UID);
        bool FilterOfflineUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => ((u.Key.IsDirectlyPaired && _configService.Current.ShowSyncshellOfflineUsersSeparately)
                || !_configService.Current.ShowSyncshellOfflineUsersSeparately)
                && (!u.Key.IsOneSidedPair || u.Value.Any()) && !u.Key.IsOnline && !u.Key.UserPair.OwnPermissions.IsPaused();
        bool FilterOfflineSyncshellUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
            => (!u.Key.IsDirectlyPaired && !u.Key.IsOnline && !u.Key.UserPair.OwnPermissions.IsPaused());


        if (_configService.Current.ShowVisibleUsersSeparately)
        {
            var allVisiblePairs = ImmutablePairList(allPairs
                .Where(FilterVisibleUsers));
            var filteredVisiblePairs = BasicSortedDictionary(filteredPairs
                .Where(FilterVisibleUsers));

            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomVisibleTag, filteredVisiblePairs, allVisiblePairs));
        }

        List<IDrawFolder> groupFolders = new();
        foreach (var group in _pairManager.GroupPairs.Select(g => g.Key).OrderBy(g => _serverManager.GetNoteForGid(g.GID) ?? g.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase))
        {
            var allGroupPairs = ImmutablePairList(allPairs
                .Where(u => FilterGroupUsers(u, group)));

            var filteredGroupPairs = filteredPairs
                .Where(u => FilterGroupUsers(u, group) && FilterOnlineOrPausedSelf(u))
                .OrderByDescending(u => u.Key.IsOnline)
                .ThenBy(u =>
                {
                    if (string.Equals(u.Key.UserData.UID, group.OwnerUID, StringComparison.Ordinal)) return 0;
                    if (group.GroupPairUserInfos.TryGetValue(u.Key.UserData.UID, out var info))
                    {
                        if (info.IsModerator()) return 1;
                        if (info.IsPinned()) return 2;
                    }
                    return u.Key.IsVisible ? 3 : 4;
                })
                .ThenBy(AlphabeticalSort, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k.Key, k => k.Value);

            if (group.GID.StartsWith(Constants.GroupZoneSyncPrefix, StringComparison.Ordinal))
            {
                groupFolders.Insert(0, _drawEntityFactory.CreateDrawGroupFolder(group, filteredGroupPairs, allGroupPairs));
            }
            else
            {
                groupFolders.Add(_drawEntityFactory.CreateDrawGroupFolder(group, filteredGroupPairs, allGroupPairs));
            }
        }

        if (_configService.Current.GroupUpSyncshells)
            drawFolders.Add(new DrawGroupedGroupFolder(groupFolders, _tagHandler, _uiSharedService));
        else
            drawFolders.AddRange(groupFolders);

        var tags = _tagHandler.GetAllTagsSorted();
        foreach (var tag in tags)
        {
            var allTagPairs = ImmutablePairList(allPairs
                .Where(u => FilterTagusers(u, tag)));
            var filteredTagPairs = BasicSortedDictionary(filteredPairs
                .Where(u => FilterTagusers(u, tag) && FilterOnlineOrPausedSelf(u)));

            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(tag, filteredTagPairs, allTagPairs));
        }

        var allOnlineNotTaggedPairs = ImmutablePairList(allPairs
            .Where(FilterNotTaggedUsers));
        var onlineNotTaggedPairs = BasicSortedDictionary(filteredPairs
            .Where(u => FilterNotTaggedUsers(u) && FilterOnlineOrPausedSelf(u)));

        drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder((_configService.Current.ShowOfflineUsersSeparately ? TagHandler.CustomOnlineTag : TagHandler.CustomAllTag),
            onlineNotTaggedPairs, allOnlineNotTaggedPairs));

        if (_configService.Current.ShowOfflineUsersSeparately)
        {
            var allOfflinePairs = ImmutablePairList(allPairs
                .Where(FilterOfflineUsers));
            var filteredOfflinePairs = BasicSortedDictionary(filteredPairs
                .Where(FilterOfflineUsers));

            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomOfflineTag, filteredOfflinePairs, allOfflinePairs));
            if (_configService.Current.ShowSyncshellOfflineUsersSeparately)
            {
                var allOfflineSyncshellUsers = ImmutablePairList(allPairs
                    .Where(FilterOfflineSyncshellUsers));
                var filteredOfflineSyncshellUsers = BasicSortedDictionary(filteredPairs
                    .Where(FilterOfflineSyncshellUsers));

                drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomOfflineSyncshellTag,
                    filteredOfflineSyncshellUsers,
                    allOfflineSyncshellUsers));
            }
        }

        drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomUnpairedTag,
            BasicSortedDictionary(filteredPairs.Where(u => u.Key.IsOneSidedPair)),
            ImmutablePairList(allPairs.Where(u => u.Key.IsOneSidedPair))));

        return drawFolders;
    }

    private DrawFolderBroadcasts? GetBroadcastsFolder()
    {
        return _broadcastManager.IsListening ? _drawEntityFactory.CreateDrawFolderBroadcasts(_broadcastManager.AvailableBroadcastGroups, _pairManager.Groups.Values.ToList()) : null;
    }

    private string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "You are currently disconnected from the PlayerSync server.",
            ServerState.Disconnecting => "Disconnecting from the server",
            ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage,
            ServerState.Offline => "Your selected PlayerSync server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "You have no secret key set for this current character. Open Settings -> Service Settings and set a secret key for the current character. You can reuse the same secret key for multiple characters.",
            ServerState.MultiChara => "Your Character Configuration has multiple characters configured with same name and world. You will not be able to connect until you fix this issue. Remove the duplicates from the configuration in Settings -> Service Settings -> Character Management and reconnect manually after.",
            ServerState.OAuthMisconfigured => "OAuth2 is enabled but not fully configured, verify in the Settings -> Service Settings that you have OAuth2 connected and, importantly, a UID assigned to your current character.",
            ServerState.OAuthLoginTokenStale => "Your OAuth2 login token is stale and cannot be used to renew. Go to the Settings -> Service Settings and unlink then relink your OAuth2 configuration.",
            ServerState.NoAutoLogon => "This character has automatic login into PlayerSync disabled. Press the connect button to connect to PlayerSync.",
            _ => string.Empty
        };
    }

    private Vector4 GetUidColor()
    {
        var theme = _uiSharedService.Theme;
        return _apiController.ServerState switch
        {
            ServerState.Connecting => theme.StatusWarn,
            ServerState.Reconnecting => theme.StatusError,
            ServerState.Connected => theme.UidAliasText,
            ServerState.Disconnected => theme.StatusWarn,
            ServerState.Disconnecting => theme.StatusWarn,
            ServerState.Unauthorized => theme.StatusError,
            ServerState.VersionMisMatch => theme.StatusError,
            ServerState.Offline => theme.StatusError,
            ServerState.RateLimited => theme.StatusWarn,
            ServerState.NoSecretKey => theme.StatusWarn,
            ServerState.MultiChara => theme.StatusWarn,
            ServerState.OAuthMisconfigured => theme.StatusError,
            ServerState.OAuthLoginTokenStale => theme.StatusError,
            ServerState.NoAutoLogon => theme.StatusWarn,
            _ => theme.StatusError
        };
    }

    private string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.MultiChara => "Duplicate Characters",
            ServerState.OAuthMisconfigured => "Misconfigured OAuth2",
            ServerState.OAuthLoginTokenStale => "Stale OAuth2",
            ServerState.NoAutoLogon => "Auto Login disabled",
            ServerState.Connected => _apiController.DisplayName,
            _ => string.Empty
        };
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

}
