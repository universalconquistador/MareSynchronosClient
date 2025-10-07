using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
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
using MareSynchronos.UI.Components.Theming;
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
using System.Runtime.ConstrainedExecution;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
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
    private readonly IpcManager _ipcManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly TopTabMenu _tabMenu;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private readonly ThemeManager _themeManager;
    private readonly ThemeEditor _themeEditor;
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
    private float _windowContentWidth;

    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager,
        IBroadcastManager broadcastManager,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager,
        TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, SelectPairForTagUi selectPairForTagUi,
        PerformanceCollectorService performanceCollectorService, IpcManager ipcManager, ThemeManager themeManager)
        : base(logger, mediator, "###PlayerSyncMainUI", performanceCollectorService)
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
        _ipcManager = ipcManager;
        _tabMenu = new TopTabMenu(Mediator, _apiController, _pairManager, _broadcastManager, _uiSharedService, _configService);
        _themeManager = themeManager;
        _themeEditor = new ThemeEditor(_themeManager, _uiSharedService);

        AllowClickthrough = false;
        AllowPinning = false;

        _drawFolders = GetDrawFolders().ToList();

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"PlayerSync {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###PlayerSyncMainUI";
        Toggle();
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
        Mediator.Subscribe<ToggleThemeEditorMessage>(this, (msg) =>
        {
            _showThemeEditor = !_showThemeEditor;
        });

        Mediator.Subscribe<CloseWindowMessage>(this, (msg) =>
        {
            IsOpen = false;
        });

        Mediator.Subscribe<ToggleCollapseMessage>(this, (msg) =>
        {
            if (_collapsed)
            {
                // We can't change UmGui from here, so we set a flag to handle it after
                _expanded = true;
            }

            _collapsed = !_collapsed;
        });

        SizeConstraints = _themeManager.CompactUISizeConstraints;

    }

    public override void Draw()
    {
        ImGui.SetWindowSize(new Vector2(_themeManager.WindowWidth, 800f), ImGuiCond.FirstUseEver);
        if (_expanded)
        {
            ImGui.SetWindowSize(_lastSize);
            _expanded = false;
        }
        base.Draw();
    }

    protected override void DrawInternal()
    {
        UpdateWindowFlags();

        if (_collapsed)
        {
            SizeConstraints = _themeManager.CompactUICollapsedSizeConstraints;
            DrawCollapsedTitleBar();

            return;
        }

        SizeConstraints = _themeManager.CompactUISizeConstraints;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, _themeManager.Current.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, _themeManager.Current.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);
        var childFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        if (AllowClickthrough)
            childFlags |= ImGuiWindowFlags.NoInputs;

        ImGui.BeginChild("themed-background", new Vector2(0, 0), true, childFlags);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + _themeManager.Padding);
        var contentWidth = ImGui.GetContentRegionAvail().X - _themeManager.Padding;
        ImGui.BeginChild("content-with-padding", new Vector2(contentWidth, 0), false, ImGuiWindowFlags.NoBackground);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var title = "PlayerSync " + ver.Major + "." + ver.Minor + "." + ver.Build;
        var startPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + ImGui.GetStyle().WindowPadding.Y / 2));
        ImGui.TextUnformatted(title);

        float btnSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Times).X;
        var totalButtonsWidth = btnSize * 3 + _themeManager.ScaledSpacing * 2;

        ImGui.SameLine(UiSharedService.GetWindowContentRegionWidth() - totalButtonsWidth);
        DrawTitleBarButtons();

        float headerHeight = 30f * ImGuiHelpers.GlobalScale;
        startPos.Y += headerHeight / 2f + ImGui.GetStyle().WindowPadding.Y;
        ImGui.SetCursorPos(startPos);
        DrawContent();

        ImGui.EndChild();
        ImGui.PopStyleColor(3);
        ImGui.EndChild();

        _lastSize = ImGui.GetWindowSize();
    }

    private void DrawCollapsedTitleBar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _themeManager.Current.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, _themeManager.Current.PanelBorder);
        var childFlags = ImGuiWindowFlags.NoResize;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var title = "PlayerSync " + ver.Major + "." + ver.Minor + "." + ver.Build;
        float btnSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Times).X;
        var totalButtonsWidth = btnSize * 3 + _themeManager.ScaledSpacing * 2;

        var y = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Times).Y + ImGui.GetStyle().WindowPadding.Y * 3;

        ImGui.BeginChild("collapsed-titlebar", new Vector2(0, y), true, childFlags);
        Flags |= childFlags;

        var contentWidth = ImGui.GetContentRegionAvail().X - _themeManager.Padding;
        ImGui.BeginChild("collapsed-titlebar-content", new Vector2(contentWidth, 0), false, childFlags);
        Flags |= childFlags | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + _themeManager.Padding);
        var startPos = ImGui.GetCursorPos();


        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + ImGui.GetStyle().WindowPadding.Y / 2));
        ImGui.TextUnformatted(title);

        ImGui.SameLine(UiSharedService.GetWindowContentRegionWidth() - totalButtonsWidth);

        DrawTitleBarButtons();

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.EndChild();
    }

    private void UpdateWindowFlags()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (_collapsed)
        {
            Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        }

        // Apply pinning state
        if (AllowPinning)
            Flags |= ImGuiWindowFlags.NoMove;

        // Apply click-through state
        if (AllowClickthrough)
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing;
    }

    private void DrawContent()
    {
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
                ImGui.TextColored(ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f), unsupported);
            }
            UiSharedService.ColorTextWrapped($"Your PlayerSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep PlayerSync up to date. Open /xlplugins and update the plugin.", ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f));
        }

        if (!_ipcManager.Initialized)
        {
            var unsupported = "MISSING ESSENTIAL PLUGINS";

            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f), unsupported);
            }
            var penumAvailable = _ipcManager.Penumbra.APIAvailable;
            var glamAvailable = _ipcManager.Glamourer.APIAvailable;

            UiSharedService.ColorTextWrapped($"One or more Plugins essential for PlayerSync operation are unavailable. Enable or update following plugins:", ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f));
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

        if (_showThemeEditor)
        {
            using (var editingTheme = _themeEditor.PushEditingTheme())
            {
                using (ImRaii.PushId("themeeditor"))
                {
                    using (_uiSharedService.UidFont.Push())
                    {
                        ImGui.Text("Theme Editor");
                    }
                    ImGui.Separator();
                    _themeEditor.Draw();

                    if (_themeEditor.CloseRequested)
                    {
                        _showThemeEditor = false;
                        _themeEditor.ResetCloseRequest();
                    }
                }
            }
        }
        else
        {
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
            }

            if (_apiController.ServerState is ServerState.Connected)
            {
                using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
                using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
            }
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
            //_lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(size, pos));
        }
    }

    private void DrawPairs()
    {
        var ySize = _transferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y
                + ImGui.GetTextLineHeight() - ImGui.GetStyle().WindowPadding.Y - ImGui.GetStyle().WindowBorderSize) - _transferPartHeight - ImGui.GetCursorPosY() - 40 * ImGuiHelpers.GlobalScale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0)))
        using (ImRaii.PushColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0)))
        {
            ImGui.BeginChild("list", new Vector2(_windowContentWidth, ySize), border: false);

            _broadcastsFolder?.Draw();

            foreach (var item in _drawFolders)
            {
                item.Draw();
            }

            ImGui.EndChild();
        }
    }
    private void DrawServerStatus()
    {
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        //#if DEBUG
        //        string shardConnection = $"Shard: {_apiController.ServerInfo.ShardName}";
        //#else
        //        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
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
            ImGui.TextColored(ThemeManager.Instance?.Current.UsersOnlineNumber ?? new Vector4(0.212f, 0.773f, 0.416f, 1f), userCount);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ThemeManager.Instance?.Current.UsersOnlineText ?? new Vector4(0.86f, 0.86f, 0.86f, 1.00f), " Users Online");
        }
        else
        {
            ImGui.TextColored(ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f), "Not connected to any server");
        }

        bool isConnectingOrConnected = _apiController.ServerState is ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting;
        var color = UiSharedService.GetBoolColor(!isConnectingOrConnected);
        var connectedIcon = isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalButtonsWidth = buttonSize.X * 3 + spacing * 2;

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - totalButtonsWidth);

        var currentTheme = ThemeManager.Instance?.Current;

        // Settings button
        if (currentTheme != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, currentTheme.BtnText);
        }
        if (_uiSharedService.IconButton(FontAwesomeIcon.Cog))
        {
            Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
        if (currentTheme != null)
        {
            ImGui.PopStyleColor();
        }
        UiSharedService.AttachToolTip("Open PlayerSync Settings");

        // Palette/Theme Editor button
        ImGui.SameLine();
        if (currentTheme != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, currentTheme.BtnText);
        }
        if (_uiSharedService.IconButton(FontAwesomeIcon.Palette))
        {
            Mediator.Publish(new ToggleThemeEditorMessage());
        }
        if (currentTheme != null)
        {
            ImGui.PopStyleColor();
        }
        UiSharedService.AttachToolTip("Open Theme Editor");

        // Disconnect/Connect button
        ImGui.SameLine();
        if (_apiController.ServerState is not (ServerState.Reconnecting or ServerState.Disconnecting))
        {
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

            UiSharedService.AttachToolTip(isConnectingOrConnected ? "Disconnect from " + _serverManager.CurrentServer.ServerName : "Connect to " + _serverManager.CurrentServer.ServerName);
        }
    }

    private void DrawTransfers()
    {
        var currentUploads = _fileTransferManager.CurrentUploadList;
        ImGui.AlignTextToFramePadding();
        if (_configService.Current.DebugThrottleUploads)
        {
            _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle);
            UiSharedService.AttachToolTip("You have upload throttling enabled, which is artificially slowing your uploads.\nYou can turn this off in Settings > Debug.");
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

    private void DrawTitleBarButtons()
    {
        if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("##PlayerSyncHamburgerMenu");
        }
        UiSharedService.AttachToolTip("PlayerSync Menu");

        ImGui.SameLine(0, _themeManager.ScaledSpacing);

        var collapseIcon = _collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp;
        if (_uiSharedService.IconButton(collapseIcon))
        {
            Mediator.Publish(new ToggleCollapseMessage());
        }
        UiSharedService.AttachToolTip(_collapsed ? "Expand" : "Collapse");

        ImGui.SameLine(0, _themeManager.ScaledSpacing);

        if (_uiSharedService.IconButton(FontAwesomeIcon.Times))
        {
            Mediator.Publish(new CloseWindowMessage());
        }
        UiSharedService.AttachToolTip("Close Window");

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
            UiSharedService.AttachToolTip("Click to copy");

            if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
            {
                var origTextSize = ImGui.CalcTextSize(_apiController.UID);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - (origTextSize.X / 2));
                ImGui.TextColored(GetUidColor(), _apiController.UID);
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(_apiController.UID);
                }
                UiSharedService.AttachToolTip("Click to copy");
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
        return _apiController.ServerState switch
        {
            ServerState.Connecting => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.Reconnecting => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.Connected => ThemeManager.Instance?.Current.UidAliasText ?? new Vector4(0.212f, 0.773f, 0.416f, 1f),
            ServerState.Disconnected => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.Disconnecting => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.Unauthorized => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.VersionMisMatch => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.Offline => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.RateLimited => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.NoSecretKey => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.MultiChara => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            ServerState.OAuthMisconfigured => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.OAuthLoginTokenStale => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f),
            ServerState.NoAutoLogon => ThemeManager.Instance?.Current.StatusConnecting ?? new Vector4(1f, 0.800f, 0.282f, 1f),
            _ => ThemeManager.Instance?.Current.StatusDisconnected ?? new Vector4(1f, 0.322f, 0.322f, 1f)
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
