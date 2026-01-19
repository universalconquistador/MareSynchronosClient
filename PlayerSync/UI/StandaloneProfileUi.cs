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
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
    private readonly MareProfileManager _mareProfileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly UiSharedService _uiSharedService;

    private byte[] _lastProfilePicture = [];
    private byte[] _lastSupporterPicture = [];
    private IDalamudTextureWrap? _supporterTextureWrap;
    private IDalamudTextureWrap? _textureWrap;
    private bool _editingNotes;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, PairManager pairManager, Pair pair,
        PerformanceCollectorService performanceCollector)
        : base(logger, mediator, "PlayerSync Profile of " + pair.UserData.AliasOrUID + "##PlayerSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareProfileManager = mareProfileManager;
        Pair = pair;
        _pairManager = pairManager;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar 
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground;

        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 700),
            MaximumSize = new Vector2(400, 700),
        };

        IsOpen = true;
    }

    public Pair Pair { get; init; }

    public override void PreDraw()
    {
        base.PreDraw();

        var r = UiScale.S(24f);
        // memo: alway scheck the count in PostDraw()
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, r);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, r);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiScale.S(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        // memo: always check the count in PreDraw()
        ImGui.PopStyleVar(5);

        base.PostDraw();
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

        // set up profile pictures
        try
        {
            if (_textureWrap == null || !psProfile.ImageData.Value.SequenceEqual(_lastProfilePicture))
            {
                _textureWrap?.Dispose();
                _lastProfilePicture = psProfile.ImageData.Value;
                _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
            }

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
        catch
        {
            // don't kill UI if there is an error
        }

        bool supporter = profile.IsSupporter;

        var t = UiTheme.Default.WithFonts(body: _uiSharedService.GameFont, heading: _uiSharedService.HeaderFont);
        using var theme = t.PushWindowStyle();

        const float bannerHeightPx = 250f;
        const float headerFillPx = bannerHeightPx * 0.5f;
        const float radiusPx = 24f;

        var bannerH = UiScale.S(bannerHeightPx);
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);

        try
        {
            var profileName = profile.PreferredName != "" ? profile.PreferredName : Pair.UserData.AliasOrUID;
            UiProfile.DrawBackGroundWindow(UiTheme.ToVec4(profile.Theme.Primary), 24f, 0.5f);
            UiProfile.DrawGradientWindow(headerColor: UiTheme.ToVec4(profile.Theme.Secondary), bodyColor: UiTheme.ToVec4(profile.Theme.Primary),
                headerHeightPx: headerFillPx, radiusPx: radiusPx, UiTheme.ToVec4(profile.Theme.Accent), 3.0f, insetPx: 0.0f);
            UiProfile.DrawAvatar(t, _textureWrap, _supporterTextureWrap, UiTheme.ToVec4(profile.Theme.Accent), UiTheme.ToVec4(profile.Theme.Primary), 
                out var nameMin, out var nameMax, bannerHeightPx);
            UiProfile.DrawNameInfo(t, profileName, Pair.UserData.UID, profile, true, nameMin, nameMax);

            // force spacing for ImGui
            ImGui.Dummy(new Vector2(width, bannerH));

            UiProfile.DrawInterests(t, profile);
            UiProfile.DrawAboutMe(t, profile);

            var oldNotes = _serverManager.GetNoteForUid(Pair.UserData.UID);
            var newNotes = _serverManager.GetProfileNoteForUid(Pair.UserData.UID);
            if (!String.IsNullOrEmpty(oldNotes) && String.IsNullOrEmpty(newNotes))
            {
                notesDraft = oldNotes;
                _serverManager.SetProfileNoteForUid(Pair.UserData.UID, notesDraft, save: true);
            }

            var changed = UiProfile.DrawNotes(t, profile, ref notesDraft, ref _editingNotes, id: $"##ps_notes_{Pair.UserData.UID}",
                heading: "Note (only visible to you)", placeholder: "Click to add a note", maxLen: 200, lines: 4);

            if (changed)
                _serverManager.SetProfileNoteForUid(Pair.UserData.UID, notesDraft, save: true);

            DrawPairingSyncshells(t, supporter ? 16f : 12f);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Window Draw Failed");
        }

        // this has to go at the very end to ensure nothing is draw over it
        if (supporter)
            UiFx.DrawWindowShimmerBorderFull(gold: new Vector4(1.0f, 0.85f, 0.25f, 1.0f), thicknessPx: 2f, outerInsetPx: 0f, innerInsetPx: 0f, drawInner: true);
    }

    private void DrawPairingSyncshells(UiTheme t, float marginPx = 12f)
    {
        var dl = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var restoreCursor = ImGui.GetCursorPos();
        var margin = UiScale.S(marginPx);
        var btn = MathF.Max(ImGui.GetFrameHeight(), UiScale.S(24f));
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
            var iconSize = UiScale.S(24f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var baseCol = ImGui.GetColorU32(t.TextMuted);
            var outlineCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f));
            var ts = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var tp = boxMin + (btnSize - ts) * 0.5f;
            var o = UiScale.S(1.25f);

            // background
            dl.AddText(font, iconSize, tp + new Vector2(-o, 0), outlineCol, icon);
            dl.AddText(font, iconSize, tp + new Vector2(o, 0), outlineCol, icon);
            dl.AddText(font, iconSize, tp + new Vector2(0, -o), outlineCol, icon);
            dl.AddText(font, iconSize, tp + new Vector2(0, o), outlineCol, icon);

            // info icon
            dl.AddText(font, iconSize, tp, baseCol, icon);
        }


        using (_uiSharedService.IconFont.Push())
        {
            var iconSize = UiScale.S(24f);
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var ts = ImGui.CalcTextSize(icon) * (iconSize / baseSize);
            var tp = boxMin + (btnSize - ts) * 0.5f;

            dl.AddText(font, iconSize, tp, ImGui.GetColorU32(t.TextMuted), icon);
        }

        if (hovered)
        {
            // offset of box to icon
            var gap = UiScale.S(4f);

            // position top right of the icon
            var ttPos = new Vector2(boxMax.X + gap, boxMin.Y - gap);
            ImGui.SetNextWindowPos(ttPos, ImGuiCond.Always, new Vector2(0f, 1f));

            using (ImRaii.PushColor(ImGuiCol.PopupBg, new Vector4(0f, 0f, 0f, 0.95f)))
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f)))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(UiScale.S(10f), UiScale.S(8f))))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, UiScale.S(8f)))
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
        Mediator.Publish(new RemoveWindowMessage(this));
        _mareProfileManager.RemoveMareProfile(Pair.UserData);
    }
}
