using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
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
    private readonly UiSharedService _uiSharedService;
    private readonly FileDownloadManager _fileDownloadManager;

    private byte[]? _lastProfilePicture;
    private byte[] _lastSupporterPicture = [];
    private IDalamudTextureWrap? _supporterTextureWrap;
    private IDalamudTextureWrap? _textureWrap;
    private Task? _profileImageDownloadTask;
    private CancellationTokenSource? _profileImageDownloadCts;
    private bool _editingNotes;
    private readonly UiTheme _theme;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, PairManager pairManager, Pair pair,
        PerformanceCollectorService performanceCollector, UiTheme theme, FileDownloadManager fileDownloadManager)
        : base(logger, mediator, "PlayerSync Profile of " + pair.UserData.AliasOrUID + "##PlayerSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareProfileManager = mareProfileManager;
        _theme = theme;
        Pair = pair;
        _pairManager = pairManager;
        _fileDownloadManager = fileDownloadManager;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar 
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground;

        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 700),
            MaximumSize = new Vector2(400, 700),
        };

        _theme.FontHeading = _uiSharedService.HeaderFont;
        _theme.FontBody = _uiSharedService.GameFont;

        IsOpen = true;
    }

    public Pair Pair { get; init; }

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
        _profileImageDownloadTask = _fileDownloadManager.DownloadProfileImageAsync(Pair.UserData.UID, cancellationToken,
            imageBytes => _lastProfilePicture = imageBytes);
    }

    protected override void DrawInternal()
    {
        // close window if we click off of it
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            IsOpen = false;

        // get profile data
        var psProfile = _mareProfileManager.GetMareProfile(Pair.UserData);
        var profile = MareProfileManager.ProfileHandler.Read(psProfile.Description);
        var notesDraft = _serverManager.GetProfileNoteForUid(Pair.UserData.UID) ?? "";

        try
        {
            _textureWrap ??= _uiSharedService.LoadImage(psProfile.ImageData.Value);

            if (_lastProfilePicture != null && _profileImageDownloadTask != null && _profileImageDownloadTask.IsCompleted)
            {
                _textureWrap?.Dispose();
                _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
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

        bool supporter = profile.IsSupporter;

        const float bannerHeightPx = 250f;
        const float headerFillPx = bannerHeightPx * 0.5f;
        const float radiusPx = 24f;

        var bannerHeight = UiScale.ScaledFloat(bannerHeightPx);
        var windowWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var colorPrimary = profile.Theme.PrimaryV4;
        var colorSecondary = profile.Theme.SecondaryV4;
        var colorAccent = profile.Theme.AccentV4;
        var profileName = profile.PreferredName != "" ? profile.PreferredName : Pair.UserData.AliasOrUID;

        using var windowStyle = _theme.PushWindowStyle();

        try
        {
            ProfileBuilder.DrawBackgroundWindow(colorPrimary, radiusPx);
            ProfileBuilder.DrawGradientWindow(colorSecondary, colorPrimary, headerFillPx, radiusPx, colorAccent, 3.0f, insetPx: 0.0f);
            ProfileBuilder.DrawAvatar(_theme, _textureWrap, _supporterTextureWrap, colorAccent, colorPrimary, out var nameMin, out var nameMax, bannerHeightPx);
            ProfileBuilder.DrawNameInfo(_theme, profileName, Pair.UserData.UID, profile, true, nameMin, nameMax);

            // force spacing for ImGui
            ImGui.Dummy(new Vector2(windowWidth, bannerHeight));

            ProfileBuilder.DrawInterests(_theme, profile);
            ProfileBuilder.DrawAboutMe(_theme, profile);

            var oldNotes = _serverManager.GetNoteForUid(Pair.UserData.UID);
            var newNotes = _serverManager.GetProfileNoteForUid(Pair.UserData.UID);
            if (!String.IsNullOrEmpty(oldNotes) && String.IsNullOrEmpty(newNotes))
            {
                notesDraft = oldNotes;
                _serverManager.SetProfileNoteForUid(Pair.UserData.UID, notesDraft, save: true);
            }

            var changed = ProfileBuilder.DrawNotes(_theme, profile, ref notesDraft, ref _editingNotes, id: $"##ps_notes_{Pair.UserData.UID}",
                heading: "Note (only visible to you)", placeholder: "Click to add a note", maxLen: 200, lines: 4);

            if (changed)
                _serverManager.SetProfileNoteForUid(Pair.UserData.UID, notesDraft, save: true);

            DrawPairingSyncshells(_theme, supporter ? 16f : 12f);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Window Draw Failed");
        }

        // this has to go at the very end to ensure nothing is draw over it
        if (supporter)
            UiFx.DrawWindowShimmerBorderFull(gold: new Vector4(1.0f, 0.85f, 0.25f, 1.0f), thicknessPx: 2f, outerInsetPx: 0f, innerInsetPx: 0f, drawInner: true);
    }

    private void DrawPairingSyncshells(UiTheme theme, float marginPx = 12f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
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
            var iconSize = UiScale.ScaledFloat(24f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var baseCol = ImGui.GetColorU32(theme.TextMuted);
            var outlineCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f));
            var textSize = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var textPadding = boxMin + (btnSize - textSize) * 0.5f;
            var offset = UiScale.ScaledFloat(1.25f);

            // background
            drawList.AddText(font, iconSize, textPadding + new Vector2(-offset, 0), outlineCol, icon);
            drawList.AddText(font, iconSize, textPadding + new Vector2(offset, 0), outlineCol, icon);
            drawList.AddText(font, iconSize, textPadding + new Vector2(0, -offset), outlineCol, icon);
            drawList.AddText(font, iconSize, textPadding + new Vector2(0, offset), outlineCol, icon);

            // info icon
            drawList.AddText(font, iconSize, textPadding, baseCol, icon);
        }


        using (_uiSharedService.IconFont.Push())
        {
            var iconSize = UiScale.ScaledFloat(24f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var textPadding = boxMin + (btnSize - textSize) * 0.5f;

            drawList.AddText(font, iconSize, textPadding, ImGui.GetColorU32(theme.TextMuted), icon);
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
                        ImGui.TextUnformatted("Paired through Syncshells:");
                        foreach (var group in Pair.UserPair.Groups)
                        {
                            var groupNote = _serverManager.GetNoteForGid(group);
                            var groupName = _pairManager.GroupPairs.First(f => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
                            var groupString = string.IsNullOrEmpty(groupNote) ? groupName : $"{groupNote} ({groupName})";
                            ImGui.TextUnformatted("- " + groupString);
                        }
                    }
                }
                ImGui.End();
            }
        }

        ImGui.SetCursorPos(restoreCursor);
    }

    public override void OnClose()
    {
        _profileImageDownloadCts?.CancelDispose();
        _profileImageDownloadCts = null;

        _textureWrap?.Dispose();
        _textureWrap = null;

        _profileImageDownloadTask = null;

        Mediator.Publish(new RemoveWindowMessage(this));
        _mareProfileManager.RemoveMareProfile(Pair.UserData);

        base.OnClose();
    }
}
