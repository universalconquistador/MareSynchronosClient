using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
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
using MareSynchronos.UI.Theming;
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
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DrawEntityFactory _drawEntityFactory;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly SelectTagForPairUi _selectGroupForPairUi;
    private readonly SelectPairForTagUi _selectPairsForGroupUi;
    private readonly IpcManager _ipcManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly TopTabMenu _tabMenu;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private List<IDrawFolder> _drawFolders;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private int _secretKeyIdx = -1;
    private bool _showModalForUserAddition;
    private float _transferPartHeight;
    private bool _wasOpen;
    private float _windowContentWidth;
    private readonly string _titleBarText;
    private bool _collapsed = false;

    // theme state
    private ThemePalette _theme = new();         
    private ThemePalette _themeWorking = new();   
    private string _selectedPreset = "Blue";
    private bool _showThemeInline = false;
    private ThemePalette _themeBeforeEdit = new();
    private enum SurfaceBg { Popup, Window } 


    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager,
        TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, SelectPairForTagUi selectPairForTagUi,
        PerformanceCollectorService performanceCollectorService, IpcManager ipcManager)
        : base(logger, mediator, "###MareSynchronosMainUI", performanceCollectorService)
    {
        _uiSharedService = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _tagHandler = tagHandler;
        _drawEntityFactory = drawEntityFactory;
        _selectGroupForPairUi = selectTagForPairUi;
        _selectPairsForGroupUi = selectPairForTagUi;
        _ipcManager = ipcManager;
        _tabMenu = new TopTabMenu(Mediator, _apiController, _pairManager, _uiSharedService);




        if (_configService.Current?.Theme != null)
        {
            _theme = Clone(_configService.Current.Theme);
        }

        AllowPinning = false;
        AllowClickthrough = false;
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
                    ImGui.Text("Open Player Sync Settings");
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
                    ImGui.Text("Open Player Sync Event Viewer");
                    ImGui.EndTooltip();
                }
            }
        };

        _drawFolders = GetDrawFolders().ToList();

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        _titleBarText = $"Player Sync {dev} ({ver.Major}.{ver.Minor}.{ver.Build})";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _titleBarText  = "Player Sync " + ver.Major + "." + ver.Minor + "." + ver.Build;
#endif
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
        Mediator.Subscribe<RefreshUiMessage>(this, (msg) => _drawFolders = GetDrawFolders().ToList());

        Flags |= ImGuiWindowFlags.NoDocking;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(375, 400),
            MaximumSize = new Vector2(375, 2000),
        };
    }

    protected override void DrawInternal()
    {
        float headerHeight = 30f * ImGuiHelpers.GlobalScale;


        if (_collapsed)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoBackground
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoResize;

            SizeConstraints = new() { MinimumSize = new(375, 40f), MaximumSize = new(375, 40f) };
            DrawCustomTitleBarOverlay(headerHeight);
            return;
        }

        using (new GlobalThemeScope(_theme))
        {
            Flags |= ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoBackground
                  | ImGuiWindowFlags.NoBringToFrontOnFocus;

            SizeConstraints = new() { MinimumSize = new(375, 600), MaximumSize = new(375, 2000) };

            DrawCustomTitleBarOverlay(headerHeight);

            using var roundRoot = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10f);
            using var borderRoot = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f);
            using var paddingRoot = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 8f));
            using var childBg = ImRaii.PushColor(ImGuiCol.ChildBg, _theme.PanelBg);
            using var childBrd = ImRaii.PushColor(ImGuiCol.Border, _theme.PanelBorder);

            using var btn = ImRaii.PushColor(ImGuiCol.Button, _theme.Btn);
            using var btnH = ImRaii.PushColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
            using var btnA = ImRaii.PushColor(ImGuiCol.ButtonActive, _theme.BtnActive);
            using var frameRnd = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f);
            using var framePad = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));

            using var textPrimary = ImRaii.PushColor(ImGuiCol.Text, _theme.TextPrimary);
            using var textDisabled = ImRaii.PushColor(ImGuiCol.TextDisabled, _theme.TextDisabled);

            ImGui.BeginChild("root-surface",
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 15f),
                true);

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
                    ImGui.TextColored(ImGuiColors.DalamudRed, unsupported);
                }
                UiSharedService.ColorTextWrapped(
                    $"Your XIVSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                    "It is highly recommended to keep XIVSync up to date. Open /xlplugins and update the plugin.",
                    ImGuiColors.DalamudRed);
            }

            if (!_ipcManager.Initialized)
            {
                var unsupported = "MISSING ESSENTIAL PLUGINS";
                using (_uiSharedService.UidFont.Push())
                {
                    var uidTextSize = ImGui.CalcTextSize(unsupported);
                    ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(ImGuiColors.DalamudRed, unsupported);
                }
                var penumAvailable = _ipcManager.Penumbra.APIAvailable;
                var glamAvailable = _ipcManager.Glamourer.APIAvailable;

                UiSharedService.ColorTextWrapped("One or more Plugins essential for Mare operation are unavailable. Enable or update following plugins:", ImGuiColors.DalamudRed);
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
                if (_showThemeInline)
                {
                    using (ImRaii.PushId("theme-inline"))
                        DrawThemeInline();
                }
                else
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
            }

            if (_configService.Current.OpenPopupOnAdd && _pairManager.LastAddedUser != null)
            {
                _lastAddedUser = _pairManager.LastAddedUser;
                _pairManager.LastAddedUser = null;
                ImGui.OpenPopup("Set Notes for New User");
                _showModalForUserAddition = true;
                _lastAddedUserComment = string.Empty;
            }

            using (new ThemedWindowScope(_theme, SurfaceBg.Window))
            {
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
            }

            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            if (_lastSize != size || _lastPosition != pos)
            {
                _lastSize = size;
                _lastPosition = pos;
                Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
            }

            ImGui.EndChild();
        } 
    }

    private void DrawPairs()
    {
        float availY = ImGui.GetContentRegionAvail().Y;

        float transfersH = _transferPartHeight > 0f
            ? _transferPartHeight
            : EstimateTransfersHeightFromStyle();

        float spacingY = ImGui.GetStyle().ItemSpacing.Y;

        float listH = MathF.Max(1f, availY - transfersH  - spacingY);
        listH *= 0.95f; 


        var rowFramePadding = new Vector2(8f, 6f);    
        var rowItemSpacing = new Vector2(6f, 4f);    

        var header = _theme.Btn;
        var headerHovered = _theme.BtnHovered;
        var headerActive = _theme.BtnActive;

        ImGui.BeginChild("list", new Vector2(_windowContentWidth, listH), border: false);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, rowFramePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, rowItemSpacing);
        ImGui.PushStyleColor(ImGuiCol.Header, header);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, headerHovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, headerActive);


        foreach (var folder in _drawFolders)
            folder.Draw();

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);

        ImGui.EndChild();
    }


    private static float EstimateTransfersHeightFromStyle()
    {
        var style = ImGui.GetStyle();
        float rowH = ImGui.GetTextLineHeightWithSpacing();
        float padding = style.ItemInnerSpacing.Y + style.ItemSpacing.Y;
        return (rowH * 2f) + padding;
    }

    private void DrawServerStatus()
    {
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
#if DEBUG
        string shardConnection = $"Shard: {_apiController.ServerInfo.ShardName}";
#else
        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
#endif
        var shardTextSize = ImGui.CalcTextSize(shardConnection);
        var printShard = !string.IsNullOrEmpty(_apiController.ServerInfo.ShardName) && shardConnection != string.Empty;

        if (_apiController.ServerState is ServerState.Connected)
        {

            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextColored(_theme.Accent, userCount);
            ImGui.SameLine();
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudRed, "Not connected to any server");
        }

        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine();
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }
        bool isConnectingOrConnected = _apiController.ServerState is ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting;
        var color = UiSharedService.GetBoolColor(!isConnectingOrConnected);
        var connectedIcon = isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link;

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

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
        var currentUploads = _fileTransferManager.CurrentUploads.ToList();
        ImGui.AlignTextToFramePadding();
        _uiSharedService.IconText(FontAwesomeIcon.Upload);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.TextUnformatted($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(_windowContentWidth - textSize.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(uploadText);
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextSecondary))
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
            using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextSecondary))
                ImGui.TextUnformatted("No downloads in progress");
        }
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();

        using (_uiSharedService.UidFont.Push())
            ImGui.TextColored(_theme.Accent, uidText);

        if (_apiController.ServerState is ServerState.Connected)
        {
            if (ImGui.IsItemClicked())
                ImGui.SetClipboardText(_apiController.DisplayName);
            UiSharedService.AttachToolTip("Click to copy");

            if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
            {
                ImGui.TextColored(_theme.Accent, _apiController.UID);
                if (ImGui.IsItemClicked())
                    ImGui.SetClipboardText(_apiController.UID);
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
            var allVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));
            var filteredVisiblePairs = BasicSortedDictionary(filteredPairs.Where(FilterVisibleUsers));
            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomVisibleTag, filteredVisiblePairs, allVisiblePairs));
        }

        List<IDrawFolder> groupFolders = new();
        foreach (var group in _pairManager.GroupPairs.Select(g => g.Key).OrderBy(g => g.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase))
        {
            var allGroupPairs = ImmutablePairList(allPairs.Where(u => FilterGroupUsers(u, group)));

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

            groupFolders.Add(_drawEntityFactory.CreateDrawGroupFolder(group, filteredGroupPairs, allGroupPairs));
        }

        if (_configService.Current.GroupUpSyncshells)
            drawFolders.Add(new DrawGroupedGroupFolder(groupFolders, _tagHandler, _uiSharedService));
        else
            drawFolders.AddRange(groupFolders);

        var tags = _tagHandler.GetAllTagsSorted();
        foreach (var tag in tags)
        {
            var allTagPairs = ImmutablePairList(allPairs.Where(u => FilterTagusers(u, tag)));
            var filteredTagPairs = BasicSortedDictionary(filteredPairs.Where(u => FilterTagusers(u, tag) && FilterOnlineOrPausedSelf(u)));

            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(tag, filteredTagPairs, allTagPairs));
        }

        var allOnlineNotTaggedPairs = ImmutablePairList(allPairs.Where(FilterNotTaggedUsers));
        var onlineNotTaggedPairs = BasicSortedDictionary(filteredPairs.Where(u => FilterNotTaggedUsers(u) && FilterOnlineOrPausedSelf(u)));

        drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder((_configService.Current.ShowOfflineUsersSeparately ? TagHandler.CustomOnlineTag : TagHandler.CustomAllTag),
            onlineNotTaggedPairs, allOnlineNotTaggedPairs));

        if (_configService.Current.ShowOfflineUsersSeparately)
        {
            var allOfflinePairs = ImmutablePairList(allPairs.Where(FilterOfflineUsers));
            var filteredOfflinePairs = BasicSortedDictionary(filteredPairs.Where(FilterOfflineUsers));

            drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(TagHandler.CustomOfflineTag, filteredOfflinePairs, allOfflinePairs));
            if (_configService.Current.ShowSyncshellOfflineUsersSeparately)
            {
                var allOfflineSyncshellUsers = ImmutablePairList(allPairs.Where(FilterOfflineSyncshellUsers));
                var filteredOfflineSyncshellUsers = BasicSortedDictionary(filteredPairs.Where(FilterOfflineSyncshellUsers));

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

    private string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "You are currently disconnected from the XIVSync server.",
            ServerState.Disconnecting => "Disconnecting from the server",
            ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage,
            ServerState.Offline => "Your selected XIVSync server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "You have no secret key set for this current character. Open Settings -> Service Settings and set a secret key for the current character. You can reuse the same secret key for multiple characters.",
            ServerState.MultiChara => "Your Character Configuration has multiple characters configured with same name and world. You will not be able to connect until you fix this issue. Remove the duplicates from the configuration in Settings -> Service Settings -> Character Management and reconnect manually after.",
            ServerState.OAuthMisconfigured => "OAuth2 is enabled but not fully configured, verify in the Settings -> Service Settings that you have OAuth2 connected and, importantly, a UID assigned to your current character.",
            ServerState.OAuthLoginTokenStale => "Your OAuth2 login token is stale and cannot be used to renew. Go to the Settings -> Service Settings and unlink then relink your OAuth2 configuration.",
            ServerState.NoAutoLogon => "This character has automatic login into Mare disabled. Press the connect button to connect to Mare.",
            _ => string.Empty
        };
    }

    private static Vector4 ThemedSemanticFromAccent(ThemePalette t, float hue /*0..1*/)
    {
        RgbToHsv(t.Accent.X, t.Accent.Y, t.Accent.Z, out _, out float s, out float v);

        s = MathF.Max(s, 0.50f);
        v = MathF.Max(v, 0.85f);

        HsvToRgb(hue, s, v, out float r, out float g, out float b);
        return new Vector4(r, g, b, t.Accent.W);
    }

    private static class H
    {
        public const float Red = 0.00f; 
        public const float Yellow = 0.12f; 
        public const float Green = 0.33f; 
        public const float Blue = 0.58f; 
    }

    private Vector4 GetUidColor()
    {
        var success = ThemedSemanticFromAccent(_theme, H.Green);
        var warning = ThemedSemanticFromAccent(_theme, H.Yellow);
        var danger = ThemedSemanticFromAccent(_theme, H.Red);

        return _apiController.ServerState switch
        {
            ServerState.Connected => success,

            ServerState.Connecting => warning,
            ServerState.Reconnecting => warning,
            ServerState.Disconnected => warning,
            ServerState.Disconnecting => warning,
            ServerState.RateLimited => warning,
            ServerState.NoSecretKey => warning,
            ServerState.MultiChara => warning,
            ServerState.NoAutoLogon => warning,

            ServerState.Unauthorized => danger,
            ServerState.VersionMisMatch => danger,
            ServerState.Offline => danger,
            ServerState.OAuthMisconfigured => danger,
            ServerState.OAuthLoginTokenStale => danger,

            _ => danger
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

    private void UiSharedService_GposeEnd() => IsOpen = _wasOpen;
    private void UiSharedService_GposeStart() { _wasOpen = IsOpen; IsOpen = false; }

    private void DrawCustomTitleBarOverlay(float headerH)
    {
        if (_collapsed)
        {
            float spacing = 6f * ImGuiHelpers.GlobalScale;
            float btnSide = 22f * ImGuiHelpers.GlobalScale;
            float btnH = btnSide;
            float leftPad = 10f * ImGuiHelpers.GlobalScale;
            float rightPad = ImGui.GetStyle().WindowPadding.X + 6f * ImGuiHelpers.GlobalScale;

            var winPos = ImGui.GetWindowPos();
            var crMin = ImGui.GetWindowContentRegionMin();
            var crMax = ImGui.GetWindowContentRegionMax();
            float contentW = crMax.X - crMin.X;

            var headerMin = new Vector2(winPos.X + crMin.X, ImGui.GetCursorScreenPos().Y);
            var headerMax = new Vector2(headerMin.X + contentW, headerMin.Y + headerH);

            var dl = ImGui.GetWindowDrawList();

            dl.AddRectFilled(headerMin, headerMax, ImGui.ColorConvertFloat4ToU32(_theme.HeaderBg), 10f);
            dl.AddLine(new Vector2(headerMin.X, headerMax.Y),
                       new Vector2(headerMax.X, headerMax.Y),
                       ImGui.ColorConvertFloat4ToU32(_theme.Accent));

            int buttonCount = 4;
            float buttonsW = (btnSide * buttonCount) + (spacing * (buttonCount - 1));
            float btnStartX = headerMax.X - rightPad - buttonsW;
            float btnStartY = headerMin.Y + (headerH - btnH) * 0.5f;

            var title = _titleBarText;
            var tSz = ImGui.CalcTextSize(title);
            float tX = headerMin.X + leftPad;
            float tY = headerMin.Y + (headerH - tSz.Y) * 0.5f;
            ImGui.SetCursorScreenPos(new Vector2(tX, tY));
            ImGui.TextColored(_theme.TextPrimary, title);

            float dragLeft = tX + tSz.X + spacing;
            float dragRight = btnStartX - spacing;
            float dragW = MathF.Max(0f, dragRight - dragLeft);
            ImGui.SetCursorScreenPos(new Vector2(dragLeft, headerMin.Y));
            ImGui.InvisibleButton("##dragzone_titlebar", new Vector2(dragW, headerH));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);

            ImGui.PushStyleColor(ImGuiCol.Button, _theme.Btn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, _theme.BtnActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));

            float x = btnStartX;

            ImGui.SetCursorScreenPos(new Vector2(x, btnStartY));
            var collapseIcon = _collapsed ? FontAwesomeIcon.AngleDown : FontAwesomeIcon.AngleUp;
            if (_uiSharedService.IconButton(collapseIcon)) _collapsed = !_collapsed;
            UiSharedService.AttachToolTip(_collapsed ? "Expand" : "Collapse");
            x += btnSide + spacing;

            ImGui.SetCursorScreenPos(new Vector2(x, btnStartY));
            if (_uiSharedService.IconButton(FontAwesomeIcon.Cog))
                Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            UiSharedService.AttachToolTip("Open Settings");
            x += btnSide + spacing;

            ImGui.SetCursorScreenPos(new Vector2(x, btnStartY));
            if (_uiSharedService.IconButton(FontAwesomeIcon.Book))
                Mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
            UiSharedService.AttachToolTip("Open Event Viewer");
            x += btnSide + spacing;

            ImGui.SetCursorScreenPos(new Vector2(x, btnStartY));
            if (_uiSharedService.IconButton(FontAwesomeIcon.Times))
                IsOpen = false;
            UiSharedService.AttachToolTip("Close");

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(3);
        }
        else
        {
            var collapseIcon = _collapsed ? FontAwesomeIcon.AngleDown : FontAwesomeIcon.AngleUp;

            float spacing = 6f * ImGuiHelpers.GlobalScale;
            float btnSide = 22f * ImGuiHelpers.GlobalScale;
            float rightPad = ImGui.GetStyle().WindowPadding.X + 35f * ImGuiHelpers.GlobalScale;

            var style = ImGui.GetStyle();
            float topOffset = style.FramePadding.Y + style.ItemSpacing.Y + ImGuiHelpers.GlobalScale;

            var winPos = ImGui.GetWindowPos();
            var crMin = ImGui.GetWindowContentRegionMin();
            var crMax = ImGui.GetWindowContentRegionMax();

            float stripWidth = (btnSide * 5f) + spacing * 4f;
            float overlayX = winPos.X + crMax.X - rightPad - stripWidth;
            float overlayY = winPos.Y + crMin.Y + topOffset;

            ImGui.SetNextWindowPos(new Vector2(overlayX, overlayY), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0f);
            ImGui.Begin("##xivsync-floating-controls",
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking);

            ImGui.PushStyleColor(ImGuiCol.Button, _theme.Btn);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, _theme.BtnActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));

            if (_uiSharedService.IconButton(collapseIcon))
                _collapsed = !_collapsed;
            UiSharedService.AttachToolTip(_collapsed ? "Expand" : "Collapse");
            ImGui.SameLine(0, spacing);

            if (_uiSharedService.IconButton(FontAwesomeIcon.Cog))
                Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            UiSharedService.AttachToolTip("Open Settings");
            ImGui.SameLine(0, spacing);

            if (_uiSharedService.IconButton(FontAwesomeIcon.Book))
                Mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
            UiSharedService.AttachToolTip("Open Event Viewer");
            ImGui.SameLine(0, spacing);

            if (_uiSharedService.IconButton(FontAwesomeIcon.Palette))
            {
                _themeBeforeEdit = Clone(_theme);
                _themeWorking = Clone(_theme);
                _showThemeInline = true;
            }
            UiSharedService.AttachToolTip("Customize Theme");

            ImGui.SameLine(0, spacing);

            if (_uiSharedService.IconButton(FontAwesomeIcon.Times))
                IsOpen = false;
            UiSharedService.AttachToolTip("Close");

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(3);

            ImGui.End();
        }
    }

    private void DrawThemeInline()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(_theme.Accent, "Theme");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextSecondary))
            ImGui.TextUnformatted("(preview changes live, then Save)");

        ImGui.Separator();

        ImGui.TextUnformatted("Preset");
        ImGui.SameLine();
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 8f))
        using (ImRaii.PushColor(ImGuiCol.PopupBg, _theme.PanelBg))
        using (ImRaii.PushColor(ImGuiCol.Border, _theme.PanelBorder))
        using (ImRaii.PushColor(ImGuiCol.ScrollbarBg, _theme.Btn))
        using (ImRaii.PushColor(ImGuiCol.ScrollbarGrab, _theme.BtnHovered))
        using (ImRaii.PushColor(ImGuiCol.ScrollbarGrabHovered, _theme.Accent))
        using (ImRaii.PushColor(ImGuiCol.ScrollbarGrabActive, _theme.BtnActive))
            if (ImGui.BeginCombo("##theme-preset-inline", _selectedPreset))
            {
                foreach (var kv in ThemePresets.Presets)
                {
                    bool sel = kv.Key == _selectedPreset;
                    if (ImGui.Selectable(kv.Key, sel))
                    {
                        _selectedPreset = kv.Key;
                        _themeWorking = Clone(kv.Value);
                        _theme = Clone(_themeWorking); 
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

        ImGui.Separator();

        DrawColorRow("Panel Background", () => _themeWorking.PanelBg, v => _themeWorking.PanelBg = v);
        DrawColorRow("Panel Border", () => _themeWorking.PanelBorder, v => _themeWorking.PanelBorder = v);
        DrawColorRow("Header Background", () => _themeWorking.HeaderBg, v => _themeWorking.HeaderBg = v);
        DrawColorRow("Accent", () => _themeWorking.Accent, v => _themeWorking.Accent = v);

        DrawColorRow("Button", () => _themeWorking.Btn, v => _themeWorking.Btn = v);
        DrawColorRow("Button Hovered", () => _themeWorking.BtnHovered, v => _themeWorking.BtnHovered = v);
        DrawColorRow("Button Active", () => _themeWorking.BtnActive, v => _themeWorking.BtnActive = v);

        DrawColorRow("Text Primary", () => _themeWorking.TextPrimary, v => _themeWorking.TextPrimary = v);
        DrawColorRow("Text Secondary", () => _themeWorking.TextSecondary, v => _themeWorking.TextSecondary = v);
        DrawColorRow("Text Disabled", () => _themeWorking.TextDisabled, v => _themeWorking.TextDisabled = v);

        DrawColorRow("Link", () => _themeWorking.Link, v => _themeWorking.Link = v);
        DrawColorRow("Link Hover", () => _themeWorking.LinkHover, v => _themeWorking.LinkHover = v);


        ImGui.Separator();

        if (ImGui.Button("Preview"))
            _theme = Clone(_themeWorking);

        ImGui.SameLine();
        if (ImGui.Button("Reset to Preset"))
            _themeWorking = ThemePresets.Presets.TryGetValue(_selectedPreset, out var p) ? Clone(p) : new ThemePalette();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            _showThemeInline = false;

        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            _theme = Clone(_themeWorking);
            PersistTheme(_theme);
            _showThemeInline = false;
        }

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextSecondary))
            ImGui.TextUnformatted("Tip: Click Preview to try colors without saving.");
    }

    private static ThemePalette Clone(ThemePalette p) => new ThemePalette
    {
        PanelBg = p.PanelBg,
        PanelBorder = p.PanelBorder,
        HeaderBg = p.HeaderBg,
        Accent = p.Accent,

        TextPrimary = p.TextPrimary,
        TextSecondary = p.TextSecondary,
        TextDisabled = p.TextDisabled,
        Link = p.Link,
        LinkHover = p.LinkHover,

        Btn = p.Btn,
        BtnHovered = p.BtnHovered,
        BtnActive = p.BtnActive,
    };

    private void PersistTheme(ThemePalette theme)
    {
        try
        {
            _configService.Current.Theme = theme;
            _configService.Save();

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save theme");
        }
    }



    private static void DrawColorRow(string label, Func<Vector4> get, Action<Vector4> set)
    {
        float labelWidth = 200f * ImGuiHelpers.GlobalScale;
        float swatchSize = 22f * ImGuiHelpers.GlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(labelWidth);

        ImGui.PushID(label);

        var color = get();

        ImGui.ColorButton("##swatch", color,
            ImGuiColorEditFlags.AlphaPreviewHalf,
            new Vector2(swatchSize, swatchSize));

        if (ImGui.IsItemClicked())
            ImGui.OpenPopup("picker");

        if (ImGui.BeginPopup("picker"))
        {
            var flags = ImGuiColorEditFlags.AlphaBar
                      | ImGuiColorEditFlags.PickerHueWheel
                      | ImGuiColorEditFlags.NoSidePreview;

            ImGui.ColorPicker4("##picker", ref color, flags);

            set(color);

            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private bool ThemedLink(string text)
    {
        bool clicked = false;

        ImGui.TextColored(_theme.Link, text);
        bool hovered = ImGui.IsItemHovered();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(
            new(min.X, max.Y), new(max.X, max.Y),
            ImGui.ColorConvertFloat4ToU32(hovered ? _theme.LinkHover : _theme.Link), 1.0f);

        if (ImGui.IsItemClicked()) clicked = true;
        return clicked;
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        v = max;

        float d = max - min;
        s = max <= 0f ? 0f : d / max;

        if (d <= 0f) { h = 0f; return; }

        if (max == r) h = ((g - b) / d) % 6f;
        else if (max == g) h = ((b - r) / d) + 2f;
        else h = ((r - g) / d) + 4f;

        h /= 6f;
        if (h < 0f) h += 1f;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        h = (h % 1f + 1f) % 1f;
        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f % 2f) - 1f));
        float m = v - c;

        float r1 = 0f, g1 = 0f, b1 = 0f;
        float seg = h * 6f;
        if (seg < 1f) { r1 = c; g1 = x; b1 = 0; }
        else if (seg < 2f) { r1 = x; g1 = c; b1 = 0; }
        else if (seg < 3f) { r1 = 0; g1 = c; b1 = x; }
        else if (seg < 4f) { r1 = 0; g1 = x; b1 = c; }
        else if (seg < 5f) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = r1 + m; g = g1 + m; b = b1 + m;
    }

    private sealed class ThemedWindowScope : IDisposable
    {
        private readonly int _colorCount;
        private readonly int _styleCount;

        public ThemedWindowScope(ThemePalette theme, SurfaceBg bgKind, float rounding = 8f, float borderSize = 1f)
        {
            if (bgKind == SurfaceBg.Popup)
                ImGui.PushStyleColor(ImGuiCol.PopupBg, theme.PanelBg);
            else
                ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.PanelBg);

            ImGui.PushStyleColor(ImGuiCol.Border, theme.PanelBorder);

            ImGui.PushStyleColor(ImGuiCol.TitleBg, theme.HeaderBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, theme.HeaderBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, theme.HeaderBg);

            ImGui.PushStyleColor(ImGuiCol.Text, theme.TextPrimary);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.TextDisabled);

            ImGui.PushStyleColor(ImGuiCol.Header, theme.Btn);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, theme.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, theme.BtnActive);

            ImGui.PushStyleColor(ImGuiCol.CheckMark, theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, theme.Btn);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, theme.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, theme.BtnActive);
            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg,
                new Vector4(theme.PanelBg.X, theme.PanelBg.Y, theme.PanelBg.Z, 0.50f));

            _colorCount = 18; 

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderSize);
            _styleCount = 2;
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(_styleCount);
            ImGui.PopStyleColor(_colorCount);
        }
    }

    private sealed class GlobalThemeScope : IDisposable
    {
        private readonly int _c, _v;
        public GlobalThemeScope(ThemePalette t)
        {

            ImGui.PushStyleColor(ImGuiCol.Separator, t.PanelBorder);
            ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, t.Accent);
            ImGui.PushStyleColor(ImGuiCol.SeparatorActive, t.Accent);

            ImGui.PushStyleColor(ImGuiCol.Header, t.Btn);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, t.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, t.BtnActive);

            ImGui.PushStyleColor(ImGuiCol.CheckMark, t.Accent);

            ImGui.PushStyleColor(ImGuiCol.FrameBg, t.Btn);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, t.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, t.BtnActive);

            ImGui.PushStyleColor(ImGuiCol.SliderGrab, t.Accent);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, t.BtnActive);

            ImGui.PushStyleColor(ImGuiCol.Tab, t.Btn);
            ImGui.PushStyleColor(ImGuiCol.TabHovered, t.BtnHovered);
            ImGui.PushStyleColor(ImGuiCol.TabActive, t.BtnActive);

            _c = 13;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
            _v = 2;
        }
        public void Dispose()
        {
            ImGui.PopStyleVar(_v);
            ImGui.PopStyleColor(_c);
        }
    }


}

public sealed class ThemePalette
{
    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 PanelBg { get; set; } = new(0.07f, 0.08f, 0.12f, 0.98f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 PanelBorder { get; set; } = new(0.25f, 0.45f, 0.95f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 HeaderBg { get; set; } = new(0.12f, 0.20f, 0.36f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Accent { get; set; } = new(0.25f, 0.55f, 0.95f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 TextPrimary { get; set; } = new(0.85f, 0.90f, 1.00f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 TextSecondary { get; set; } = new(0.70f, 0.75f, 0.85f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 TextDisabled { get; set; } = new(0.50f, 0.55f, 0.65f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Link { get; set; } = new(0.30f, 0.70f, 1.00f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 LinkHover { get; set; } = new(0.45f, 0.82f, 1.00f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Btn { get; set; } = new(0.15f, 0.18f, 0.25f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 BtnHovered { get; set; } = new(0.25f, 0.45f, 0.95f, 1.00f);

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 BtnActive { get; set; } = new(0.20f, 0.35f, 0.75f, 1.00f);
}
public sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read(); var x = reader.GetSingle();
            reader.Read(); var y = reader.GetSingle();
            reader.Read(); var z = reader.GetSingle();
            reader.Read(); var w = reader.GetSingle();
            reader.Read(); 
            return new Vector4(x, y, z, w);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0, y = 0, z = 0, w = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                var v = reader.GetSingle();
                switch (name!.ToLowerInvariant())
                {
                    case "x": case "r": case "red": x = v; break;
                    case "y": case "g": case "green": y = v; break;
                    case "z": case "b": case "blue": z = v; break;
                    case "w": case "a": case "alpha": w = v; break;
                }
            }
            return new Vector4(x, y, z, w);
        }
        throw new JsonException("Invalid Vector4 JSON.");
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteNumberValue(value.W);
        writer.WriteEndArray();
    }
}