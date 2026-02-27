using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
    private readonly MareProfileManager _mareProfileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly MareConfigService _mareConfigService;
    private readonly UiSharedService _uiSharedService;
    private readonly FileImageTransferHandler _fileImageTransferHandler;
    private readonly PairRequestManager _pairRequestManager;

    private byte[]? _lastProfilePicture;
    private byte[] _lastSupporterPicture = [];
    private IDalamudTextureWrap? _supporterTextureWrap;
    private IDalamudTextureWrap? _textureWrap;
    private IDalamudTextureWrap? _playerSyncWatermark;
    private Task? _profileImageDownloadTask;
    private CancellationTokenSource? _profileImageDownloadCts;
    private bool _editingNotes;
    private readonly UiTheme _theme;
    private bool _confirmNsfwWarning = false;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, PairManager pairManager, Pair pair, MareConfigService mareConfigService,
        PerformanceCollectorService performanceCollector, UiTheme theme, FileImageTransferHandler fileImageTransferHandler, PairRequestManager pairRequestManager)
        : base(logger, mediator, "PlayerSync Profile of " + pair.UserData.AliasOrUID + "##PlayerSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareConfigService = mareConfigService;
        _mareProfileManager = mareProfileManager;
        _theme = theme;
        Pair = pair;
        _pairManager = pairManager;
        _fileImageTransferHandler = fileImageTransferHandler;
        _pairRequestManager = pairRequestManager;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar 
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground;

        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 711),
            MaximumSize = new Vector2(400, 711),
        };

        _theme.FontHeading = _uiSharedService.HeaderFont;
        _theme.FontBody = _uiSharedService.GameFont;

        IsOpen = true;
    }

    public Pair Pair { get; init; }

    private IDalamudTextureWrap Watermark => _playerSyncWatermark ??= _uiSharedService.LoadImage(_mareProfileManager.PlayerSyncWatermark);

    public override void PreDraw()
    {
        base.PreDraw();

        var r = UiScale.ScaledFloat(24f);
        // memo: alway scheck the count in PostDraw()
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, r);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, r);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiScale.ScaledFloat(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        // memo: always check the count in PreDraw()
        ImGui.PopStyleVar(5);

        base.PostDraw();
    }

    public override void OnOpen()
    {
        base.OnOpen();

        if (_textureWrap != null)
            return;

        _profileImageDownloadCts = _profileImageDownloadCts.CancelRecreate();
        var cancellationToken = _profileImageDownloadCts.Token;

        // let download run in background
        _profileImageDownloadTask = _fileImageTransferHandler.DownloadProfileImageAsync(Pair.UserData.UID, cancellationToken,
            imageBytes => _lastProfilePicture = imageBytes);
    }

    public override void OnClose()
    {
        _profileImageDownloadCts?.CancelDispose();
        _profileImageDownloadCts = null;

        _textureWrap?.Dispose();
        _textureWrap = null;
        _playerSyncWatermark?.Dispose();
        _playerSyncWatermark = null;

        _lastProfilePicture = null;

        _profileImageDownloadTask = null;

        Mediator.Publish(new RemoveWindowMessage(this));
        _mareProfileManager.RemoveMareProfile(Pair.UserData);

        base.OnClose();
    }

    protected override void DrawInternal()
    {
        // close window if we click off of it
        //if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        //    IsOpen = false;

        // user might unpair us, pause, etc.
        if (!Pair.IsPaired) IsOpen = false;

        // get profile data
        var psProfile = _mareProfileManager.GetMareProfile(Pair.UserData);
        var profile = MareProfileManager.ProfileHandler.Read(psProfile.Description);
        var notesDraft = _serverManager.GetProfileNoteForUid(Pair.UserData.UID) ?? "";
        try
        {
            _textureWrap ??= _uiSharedService.LoadImage(psProfile.ImageData.Value);

            if (_lastProfilePicture != null && _profileImageDownloadTask != null && _profileImageDownloadTask.IsCompleted && _lastProfilePicture.Length > 0)
            {
                if (_mareProfileManager.IsProfileMarkedNsfwOrAllowed(psProfile, Pair.UserData))
                {
                    _textureWrap?.Dispose();
                    _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture!);
                }
                _profileImageDownloadTask = null;
            }

            // use legacy support profile for now to load the InSync image
            if (profile.IsSupporter)
            {
                if (_supporterTextureWrap == null || !psProfile.SupporterImageData.Value.SequenceEqual(_lastSupporterPicture))
                {
                    _supporterTextureWrap?.Dispose();
                    _supporterTextureWrap = null;
                    if (!string.IsNullOrEmpty(psProfile.Base64SupporterPicture))
                    {
                        _lastSupporterPicture = psProfile.SupporterImageData.Value;
                        _supporterTextureWrap = _uiSharedService.LoadImage(_lastSupporterPicture);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            //
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading/creating profile texture wrap.");
        }

        const float radiusPx = 24f;
        var colorPrimary = profile.Theme.PrimaryV4;
        var colorAccent = profile.Theme.AccentV4;
        var profileName = profile.PreferredName != "" ? profile.PreferredName : Pair.UserData.AliasOrUID;

        using var windowStyle = _theme.PushWindowStyle();

        try
        {
            var uid = Pair.UserData.UID;

            var windowPos = ImGui.GetWindowPos();
            var contentMin = windowPos + ImGui.GetWindowContentRegionMin();
            var contentMax = windowPos + ImGui.GetWindowContentRegionMax();
            var contentSize = contentMax - contentMin;

            ProfileBuilder.DrawBackgroundWindow(colorPrimary, radiusPx);
            ProfileBuilder.DrawBackgroundImage(_theme, _textureWrap);
            ProfileBuilder.DrawGradient(profile.Theme.SecondaryV4, profile.Theme.PrimaryV4);
            ProfileBuilder.DrawWindowBorder(colorAccent, radiusPx, 3.0f, 0.0f);
            ProfileBuilder.DrawNameInfo(_theme, profileName, uid, profile);

            var isDirectlyPaired = Pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional;
            var pairIcon = isDirectlyPaired ? FontAwesomeIcon.UserCheck : FontAwesomeIcon.UserPlus;
            var hoverText = isDirectlyPaired ? "Directly paired" : $"Click to send {Pair.PlayerName} a direct pair request.";
            if (DrawTopRightIconButton("##pair_status", pairIcon, profile.Theme.TextPrimaryV4, 1, 14f, 8f, !isDirectlyPaired, hoverText))
            {
                if (isDirectlyPaired) return;

                _pairRequestManager.SendPairRequest(userData: Pair.UserData);
            }
            
            var gapHeight = ProfileBuilder.CalculateGapHeight(_theme, profile);
            var pad = _theme.PanelPad * ImGuiHelpers.GlobalScale;

            ImGui.SetCursorPos(new(pad, gapHeight));

            ProfileBuilder.DrawInterests(_theme, profile);
            ProfileBuilder.DrawAboutMe(_theme, profile);

            //ImGui.SetCursorPos(new(pad, contentSize.Y - 135f * ImGuiHelpers.GlobalScale));

            //var oldNotes = _serverManager.GetNoteForUid(uid);
            //var newNotes = _serverManager.GetProfileNoteForUid(uid);
            //if (!String.IsNullOrEmpty(oldNotes) && String.IsNullOrEmpty(newNotes))
            //{
            //    notesDraft = oldNotes;
            //    _serverManager.SetProfileNoteForUid(uid, notesDraft, save: true);
            //}

            //var changed = ProfileBuilder.DrawNotes(_theme, profile, ref notesDraft, ref _editingNotes, id: $"##ps_notes_{uid}",
            //    heading: "Note (only visible to you)", placeholder: "Click to add a note", maxLen: 200, lines: 2);

            //if (changed)
            //    _serverManager.SetProfileNoteForUid(uid, notesDraft, save: true);

            var watermarkColor = profile.Theme.AccentV4;
            watermarkColor.W = 0.3f;
            ProfileBuilder.DrawWatermark(Watermark, new(72, 72), watermarkColor, 7f);

            DrawPairingSyncshells(_theme, profile.Theme.TextPrimaryV4, 14f);

            if (psProfile.IsNSFW && !_mareConfigService.Current.ProfileSkipNsfwWarning)
            {
                if (!_confirmNsfwWarning)
                {
                    ProfileBuilder.DrawBackgroundWindow(new(0.04f, 0.18f, 0.24f, 0.92f), radiusPx, 0f);

                    const string buttonText = "Show NSFW Profile";

                    var style = ImGui.GetStyle();
                    Vector2 buttonSize = new(ImGui.CalcTextSize(buttonText).X + style.FramePadding.X * 2f, ImGui.GetFrameHeight());
                    Vector2 contentMin2 = ImGui.GetWindowContentRegionMin();
                    Vector2 contentMax2 = ImGui.GetWindowContentRegionMax();
                    Vector2 contentSize2 = contentMax2 - contentMin2;
                    Vector2 centeredPos = contentMin2 + (contentSize2 - buttonSize) * 0.5f;

                    ImGui.SetCursorPos(centeredPos);
                    if (ImGui.Button(buttonText, buttonSize))
                    {
                        _confirmNsfwWarning = true;
                    }
                    UiSharedService.AttachToolTip("Disable warning in settings");
                }
            }

            if (DrawCloseButton("##close_profile", profile.Theme.TextPrimaryV4, 14f))
            {
                IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Window Draw Failed");
        }
    }

    private void DrawPairingSyncshells(UiTheme theme, Vector4 color, float marginPx = 12f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var restoreCursor = ImGui.GetCursorPos();
        var margin = UiScale.ScaledFloat(marginPx);
        var btn = MathF.Max(ImGui.GetFrameHeight(), UiScale.ScaledFloat(24f));
        var btnSize = new Vector2(btn, btn);
        var winPos = ImGui.GetWindowPos();
        var contentMin = winPos + ImGui.GetWindowContentRegionMin();
        var contentMax = winPos + ImGui.GetWindowContentRegionMax();
        var boxMin = new Vector2(contentMin.X + margin, contentMax.Y - margin - btnSize.Y);
        var boxMax = boxMin + btnSize;

        ImGui.SetCursorScreenPos(boxMin);
        ImGui.InvisibleButton("##ps_pair_info", btnSize);

        var hovered = ImGui.IsItemHovered();

        var icon = FontAwesomeIcon.InfoCircle.ToIconString();

        using (_uiSharedService.IconFont.Push())
        {
            var iconSize = UiScale.ScaledFloat(20f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var textPadding = boxMin + (btnSize - textSize) * 0.5f;

            drawList.AddText(font, iconSize, textPadding, ImGui.GetColorU32(color), icon);
        }

        if (hovered)
        {
            // offset of box to icon
            var gap = UiScale.ScaledFloat(4f);

            // position top right of the icon
            var boxPos = new Vector2(boxMax.X + gap, boxMin.Y - gap);
            ImGui.SetNextWindowPos(boxPos, ImGuiCond.Always, new Vector2(0f, 1f));

            using (ImRaii.PushColor(ImGuiCol.PopupBg, new Vector4(0f, 0f, 0f, 0.95f)))
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f)))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(UiScale.ScaledFloat(10f), UiScale.ScaledFloat(8f))))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, UiScale.ScaledFloat(8f)))
            {
                var flags = ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;

                if (ImGui.Begin("##ps_pair_info_tt", flags))
                {
                    if (!Pair.IsOnline || Pair == null)
                    {
                        ImGui.TextUnformatted("Pair info unavailable");
                    }
                    else 
                    {
                        if (Pair.UserPair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
                        {
                            ImGui.TextUnformatted("Directly paired");
                            if (Pair.UserPair.OwnPermissions.IsPaused())
                            {
                                ImGui.SameLine();
                                UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
                            }
                            if (Pair.UserPair.OtherPermissions.IsPaused())
                            {
                                ImGui.SameLine();
                                UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
                            }
                        }

                        if (Pair.UserPair.Groups.Any())
                        {
                            var groups = Pair.UserPair.Groups.Distinct(StringComparer.OrdinalIgnoreCase);
                            ImGui.TextUnformatted("Paired through Syncshells:");
                            foreach (var group in groups)
                            {
                                var groupNote = _serverManager.GetNoteForGid(group);
                                var groupName = _pairManager.GroupPairs.First(f => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
                                var groupString = string.IsNullOrEmpty(groupNote) ? groupName : $"{groupNote} ({groupName})";
                                ImGui.TextUnformatted("- " + groupString);
                            }
                        }
                    }
                }
                ImGui.End();
            }
        }

        ImGui.SetCursorPos(restoreCursor);
    }

    private static bool DrawCloseButton(string id, Vector4 color, float marginPx = 12f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var margin = UiScale.ScaledFloat(marginPx);

        var btn = MathF.Max(ImGui.GetFrameHeight(), UiScale.ScaledFloat(24f));
        var btnSize = new Vector2(btn, btn);
        var buttonMin = new Vector2(windowPos.X + windowSize.X - margin - btnSize.X, windowPos.Y + margin);

        // click hitbox
        ImGui.SetCursorScreenPos(buttonMin);
        ImGui.InvisibleButton(id, btnSize);

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var icon = FontAwesomeIcon.TimesCircle.ToIconString();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var iconSize = UiScale.ScaledFloat(20f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var textPadding = buttonMin + (btnSize - textSize) * 0.5f;

            drawList.AddText(font, iconSize, textPadding, ImGui.GetColorU32(color), icon);
        }

        return clicked;
    }

    private static bool DrawTopRightIconButton(string id, FontAwesomeIcon icon, Vector4 color, int offsetCount, float marginPx = 12f, float gapPx = 6f,
        bool hoverHand = false, string hoverText = "")
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var margin = UiScale.ScaledFloat(marginPx);
        var gap = UiScale.ScaledFloat(gapPx);
        var btn = MathF.Max(ImGui.GetFrameHeight(), UiScale.ScaledFloat(24f));
        var btnSize = new Vector2(btn, btn);
        var clampedOffsetCount = Math.Max(0, offsetCount);
        var stepX = btnSize.X + gap;

        var buttonMin = new Vector2(windowPos.X + windowSize.X - margin - btnSize.X - (stepX * clampedOffsetCount), windowPos.Y + margin);

        ImGui.SetCursorScreenPos(buttonMin);
        ImGui.InvisibleButton(id, btnSize);
        if (!String.IsNullOrWhiteSpace(hoverText))
            UiSharedService.AttachToolTip(hoverText);

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (hovered && hoverHand)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var iconString = icon.ToIconString();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var iconSize = UiScale.ScaledFloat(20f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(iconString) * (iconSize / baseSize);
            var textPadding = buttonMin + (btnSize - textSize) * 0.5f;

            drawList.AddText(font, iconSize, textPadding, ImGui.GetColorU32(color), iconString);
        }

        return clicked;
    }

}
