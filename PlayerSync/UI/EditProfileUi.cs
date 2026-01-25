using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MareSynchronos.UI.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

namespace MareSynchronos.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiSharedService _uiSharedService;

    private bool _showFileDialogError;
    private bool _wasOpen;

    private IDalamudTextureWrap? _pfpTextureWrap;
    private IDalamudTextureWrap? _supporterTextureWrap;
    private byte[] _lastProfilePicture = [];
    private byte[] _lastSupporterPicture = [];

    private bool _hasProfileLoaded = false;
    private bool _dirty = false;
    private readonly List<string> _errors = [];

    private ProfileV1 _liveProfile;
    private string _descriptionText = string.Empty;

    private bool _isNsfw;

    private static readonly string[] InterestOptions =
    [
        "Gaming", "Gposing", "Venues", "Socializing", "Fashion",
        "Roleplay", "Raiding", "Housing",
        "Music", "Streaming", "Art", "Modding", "Events", "Gooning", "Eeping"
    ];

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator, ApiController apiController, UiSharedService uiSharedService, 
        FileDialogManager fileDialogManager, MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "PlayerSync Profile Editor###PlayerSyncEditProfileUI", performanceCollectorService)
    {
        IsOpen = false;

        Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize;

        SizeConstraints = new()
        {
            MinimumSize = new(1000, 800),
            MaximumSize = new(1000, 800),
        };

        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _mareProfileManager = mareProfileManager;

        Mediator.Subscribe<GposeStartMessage>(this, (_) =>
        {
            _wasOpen = IsOpen;
            IsOpen = false;
        });

        Mediator.Subscribe<GposeEndMessage>(this, (_) =>
        {
            IsOpen = _wasOpen;
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            IsOpen = false;
        });

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;

                _supporterTextureWrap?.Dispose();
                _supporterTextureWrap = null;

                _lastProfilePicture = [];
                _lastSupporterPicture = [];
            }
        });
    }

    private UserData Self => new(_apiController.UID);

    private static bool IsProfileLoaded(string? raw)
    {
        //// this can only happen if a legacy profile was saved as blank
        if (String.IsNullOrEmpty(raw))
            return true;

        if (raw.StartsWith("{", StringComparison.Ordinal))
            return true;

        if (raw.Contains("Loading Data", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsNewProfile(string? raw)
    {
        if (String.IsNullOrEmpty(raw))
            return true;

        if (raw.StartsWith("{", StringComparison.Ordinal))
            return false;

        if (raw.Contains("Loading Data", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    protected override void DrawInternal()
    {
        var theme = UiTheme.Default;
        using var windowStyle = theme.PushWindowStyle();

        var psProfile = _mareProfileManager.GetMareProfile(Self);

        if (psProfile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(psProfile.Description, ImGuiColors.DalamudRed);
            return;
        }

        UpdateSelfImages(psProfile);
        var raw = psProfile.Description;

        if (!_hasProfileLoaded)
        {
            var profile = MareProfileManager.ProfileHandler.Read(raw);
            _liveProfile = profile;
        }

        if (IsProfileLoaded(raw) && IsNewProfile(raw))
        {
            // first time profile/nothing on server
            _descriptionText = MareProfileManager.ProfileHandler.WriteJson(_liveProfile, Formatting.None);
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID),
                        Disabled: false, IsNSFW: null, ProfilePictureBase64: null, Description: _descriptionText));
            _hasProfileLoaded = true;
        }
        else if (IsProfileLoaded(raw) && !IsNewProfile(raw))
        {
            _hasProfileLoaded = true;
        }

        // Split editor & preview into 2 columns
        if (!ImGui.BeginTable("##edit_profile_split", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
            return;

        try
        {
            ImGui.TableSetupColumn("editor", ImGuiTableColumnFlags.WidthStretch, 0.52f);
            ImGui.TableSetupColumn("preview", ImGuiTableColumnFlags.WidthStretch, 0.48f);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            using (ImRaii.Child("##profile_editor", new Vector2(0, 0), false))
                _liveProfile = DrawEditor();

            ImGui.TableSetColumnIndex(1);
            DrawProfilePreview(_liveProfile);
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawProfilePreview(ProfileV1 profile)
    {
        var hostFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.BeginChild("##profile_preview_parent", new Vector2(0, 0), false, hostFlags);
        try
        {
            // match standalone profile size
            var target = new Vector2(400f, 700f) * ImGuiHelpers.GlobalScale;
            var avail = ImGui.GetContentRegionAvail();
            var inner = new Vector2(MathF.Min(target.X, avail.X), MathF.Min(target.Y, avail.Y));

            // center the preview child
            var cursor = ImGui.GetCursorPos();
            var offX = MathF.Max(0f, (avail.X - inner.X) * 0.5f);
            var offY = MathF.Max(0f, (avail.Y - inner.Y) * 0.5f);
            ImGui.SetCursorPos(cursor + new Vector2(offX, offY));

            var r = UiScale.S(24f);
            var previewFlags = hostFlags | ImGuiWindowFlags.NoBackground;

            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
            using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, r))
            {
                ImGui.BeginChild("##profile_preview", inner, false, previewFlags);
                try
                {
                    DrawPreview(profile);
                }
                finally
                {
                    ImGui.EndChild();
                }
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>
    /// This returns the live profile profile to keep state for preview and save.
    /// </summary>
    private ProfileV1 DrawEditor()
    {
        var editorProfile = _liveProfile;

        if (ImGui.BeginTable("##name_pronouns", 2,
                ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("c1", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("c2", ImGuiTableColumnFlags.WidthStretch, 1f);

            // labels
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Preferred Name");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Pronouns");

            // inputs
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var preferredName = editorProfile.PreferredName;
            if (ImGui.InputText("##preferredname", ref preferredName, 20))
            {
                editorProfile.PreferredName = preferredName;
                _dirty = true;
            }

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var pronouns = editorProfile.Pronouns;
            if (ImGui.InputText("##pronouns", ref pronouns, 20))
            {
                editorProfile.Pronouns = pronouns;
                _dirty = true;
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Relation Status");
        var statusCurrent = _liveProfile.Status;
        ImGui.SetNextItemWidth(-1);
        var statucChanged = DrawStatusCombo("##status", ref statusCurrent);
        if (statucChanged)
        {
            _liveProfile.Status = statusCurrent;
            _dirty = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Profile Picture");
        DrawProfilePictureUpload();

        ImGui.Separator();
        var nsfw = _isNsfw;
        if (ImGui.Checkbox("Profile is NSFW", ref nsfw))
        {
            _isNsfw = nsfw;
            _dirty = true;

            // update on server to keep the same API surface
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID),
                Disabled: false, IsNSFW: _isNsfw, ProfilePictureBase64: null, Description: null));
        }
        _uiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");

        if (editorProfile.IsSupporter)
        {
            ImGui.SameLine();
            var enableSupporter = editorProfile.EnableSupporterElements;
            if (ImGui.Checkbox("Supporter Frame", ref enableSupporter))
            {
                editorProfile.EnableSupporterElements = enableSupporter;
                _dirty = true;
            }
        }

        ImGui.Separator();

        var selected = new HashSet<string>((editorProfile.Interests ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()),StringComparer.OrdinalIgnoreCase);

        bool interestsChanged = false;

        ImGui.TextUnformatted("Interests");
        if (ImGui.BeginTable("##interests", 3, ImGuiTableFlags.SizingStretchSame))
        {
            try
            {
                for (var i = 0; i < InterestOptions.Length; i++)
                {
                    ImGui.TableNextColumn();

                    var key = InterestOptions[i];
                    bool v = selected.Contains(key);

                    if (ImGui.Checkbox(key, ref v))
                    {
                        interestsChanged = true;
                        _dirty = true;

                        if (v) selected.Add(key);
                        else selected.Remove(key);
                    }
                }
            }
            finally
            {
                ImGui.EndTable();
            }
        }

        if (interestsChanged)
        {
            editorProfile.Interests = selected.Count == 0 ? [] : InterestOptions.Where(selected.Contains).ToList();
        }

        ImGui.Separator();

        var aboutMe = editorProfile.AboutMe;
        ImGui.TextUnformatted("About Me");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##aboutme", ref aboutMe, 2048, new Vector2(-1, ImGui.GetTextLineHeight() * 5f)))
        {
            editorProfile.AboutMe = aboutMe;
            _dirty = true;
        }

        ImGui.Separator();

        ImGui.TextUnformatted("Theme Colors");
        DrawThemeColorEditors();

        ImGui.Separator();
        ImGui.Spacing();

        if (_errors.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.35f, 0.35f, 1f));
            foreach (var e in _errors)
                ImGui.TextWrapped($"• {e}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        ImGui.TextWrapped("!!! AVOID: Any profile image that can be considered highly illegal or obscene " +
            "(bestiality, anything that could be considered a sexual act with a minor), as well as " +
            "slurs of any kind in the description that can be considered highly offensive. " +
            "In case of valid reports from other users, this could lead to a ban from PlayerSync.");
        ImGui.TextWrapped("If your profile picture or profile description could be considered NSFW, enable the toggle for it.");

        ImGui.Spacing();

        ImGui.BeginDisabled(!_dirty);
        if (ImGui.Button("Save Profile"))
        {
            _errors.Clear();

            try
            {
                if (!MareProfileManager.ProfileValidator.TryValidate(editorProfile, out var errs))
                {
                    _errors.AddRange(errs);
                }
                else
                {
                    if (_apiController.ServerInfo.FileServerAddress.ToString().Contains("dev"))
                    {
                        _descriptionText = MareProfileManager.ProfileHandler.WriteJson(editorProfile, Formatting.None);
                    }
                    else
                    {
                        _descriptionText = editorProfile.AboutMe.Trim();
                    }

                    _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID),
                        Disabled: false, IsNSFW: null, ProfilePictureBase64: null, Description: _descriptionText));

                    _liveProfile = editorProfile;
                    _dirty = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to save profile.");
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Default Colors"))
        {
            editorProfile.Theme = new();
            _dirty = true;
            _errors.Clear();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close"))
            IsOpen = false;

        return editorProfile;
    }

    private void DrawThemeColorEditors()
    {
        var availW = ImGui.GetContentRegionAvail().X;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        const int cols = 4;

        var colW = (availW - gap * (cols - 1)) / cols;

        if (!ImGui.BeginTable("##theme_colors", cols, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadInnerX))
            return;

        try
        {
            ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthFixed, colW);
            ImGui.TableSetupColumn("Secondary", ImGuiTableColumnFlags.WidthFixed, colW);
            ImGui.TableSetupColumn("Accent", ImGuiTableColumnFlags.WidthFixed, colW);
            ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthFixed, colW);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var primary = UiTheme.ToVec4(_liveProfile.Theme.Primary);
            if (ImGui.ColorEdit4("Primary##col", ref primary, ImGuiColorEditFlags.NoInputs))
            {
                _liveProfile.Theme.Primary = UiTheme.FromVec4(primary);
                _dirty = true;
            }

            ImGui.TableSetColumnIndex(1);          
            var secondary = UiTheme.ToVec4(_liveProfile.Theme.Secondary);
            if (ImGui.ColorEdit4("Secondary##col", ref secondary, ImGuiColorEditFlags.NoInputs))
            {
                _liveProfile.Theme.Secondary = UiTheme.FromVec4(secondary);
                _dirty = true;
            }
            
            ImGui.TableSetColumnIndex(2);            
            var accent = UiTheme.ToVec4(_liveProfile.Theme.Accent);
            if (ImGui.ColorEdit4("Accent##col", ref accent, ImGuiColorEditFlags.NoInputs))
            {
                _liveProfile.Theme.Accent = UiTheme.FromVec4(accent);
                _dirty = true;
            }

            ImGui.TableSetColumnIndex(3);
            var text = UiTheme.ToVec4(_liveProfile.Theme.TextPrimary);
            if (ImGui.ColorEdit4("Text##col", ref text, ImGuiColorEditFlags.NoInputs))
            {
                _liveProfile.Theme.TextPrimary = UiTheme.FromVec4(text);
                _dirty = true;
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawPreview(ProfileV1 profile)
    {
        var theme = UiTheme.Default;
        theme.FontHeading = _uiSharedService.GameFont;
        theme.FontBody = _uiSharedService.HeaderFont;
        const float bannerHeightPx = 250f;
        const float headerFillPx = bannerHeightPx * 0.5f;
        const float radiusPx = 24f;
        var bannerH = UiScale.S(bannerHeightPx);
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);

        ProfileBuilder.DrawBackGroundWindow(UiTheme.ToVec4(profile.Theme.Primary), 24f, 0.5f);

        using var windowStyle = theme.PushWindowStyle();

        var displayName = !string.IsNullOrWhiteSpace(_liveProfile.PreferredName) ? _liveProfile.PreferredName : _apiController.DisplayName;
        ProfileBuilder.DrawGradientWindow(UiTheme.ToVec4(_liveProfile.Theme.Secondary), UiTheme.ToVec4(_liveProfile.Theme.Primary), 
            headerFillPx, radiusPx, UiTheme.ToVec4(_liveProfile.Theme.Accent), 3.0f, 0.0f);

        ProfileBuilder.DrawAvatar(theme, _pfpTextureWrap, _supporterTextureWrap, UiTheme.ToVec4(_liveProfile.Theme.Accent), UiTheme.ToVec4(_liveProfile.Theme.Primary),
            out var nameMin, out var nameMax, bannerHeightPx);

        ProfileBuilder.DrawNameInfo(theme, displayName, _apiController.UID, profile, true, nameMin, nameMax);
        ImGui.Dummy(new Vector2(width, bannerH));

        ProfileBuilder.DrawInterests(theme, profile);
        ProfileBuilder.DrawAboutMe(theme, profile);
    }

    private void UpdateSelfImages(MareProfileData psProfile)
    {
        try
        {
            var avatarBytes = psProfile.ImageData.Value;
            if (_pfpTextureWrap == null || !_lastProfilePicture.SequenceEqual(avatarBytes))
            {
                _pfpTextureWrap?.Dispose();
                _lastProfilePicture = avatarBytes;
                _pfpTextureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
            }

            var supporterBytes = psProfile.SupporterImageData.Value;
            if (_supporterTextureWrap == null || !_lastSupporterPicture.SequenceEqual(supporterBytes))
            {
                _supporterTextureWrap?.Dispose();
                _supporterTextureWrap = null;

                _lastSupporterPicture = supporterBytes;

                var base64 = psProfile.Base64SupporterPicture;
                if (!string.IsNullOrEmpty(base64))
                    _supporterTextureWrap = _uiSharedService.LoadImage(_lastSupporterPicture);
            }
        }
        catch
        {
            // don't kill UI if there is an error
        }
    }

    private void DrawProfilePictureUpload()
    {
        var style = ImGui.GetStyle();
        var width = (ImGui.GetContentRegionAvail().X - style.ItemSpacing.X) / 2f;

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile picture", width))
        {
            _fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", (success, file) =>
            {
                if (!success) return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        // set the pfp size/shape values
                        const int MaxUploadBytes = 2 * 1024 * 1024;
                        const float AspectW = 9f;
                        const float AspectH = 16f;
                        const int MaxOutputHeight = 512;
                        const int MaxOutputWidth = 512;

                        var fileContent = File.ReadAllBytes(file);

                        if (fileContent.Length <= 0 || fileContent.Length > MaxUploadBytes)
                        {
                            _showFileDialogError = true;
                            return;
                        }

                        await using (var ms = new MemoryStream(fileContent, writable: false))
                        {
                            var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                            if (format == null || !format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                            {
                                _showFileDialogError = true;
                                return;
                            }
                        }

                        // crop to aspect + resize
                        var processedPng = RescaleProfilePic(fileContent, AspectW, AspectH, MaxOutputWidth, MaxOutputHeight);

                        _showFileDialogError = false;

                        // update pfp on server
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), 
                            false, null, Convert.ToBase64String(processedPng), Description: null)).ConfigureAwait(false);

                        _dirty = true; // don't really need the user to save, but they might be worried if they can't
                    }
                    catch
                    {
                        _showFileDialogError = true;
                    }
                });
            });
        }
        UiSharedService.AttachToolTip("Select and upload a new profile picture");

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture", width))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, ProfilePictureBase64: "", Description: null));
        }
        UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");

        if (_showFileDialogError)
            UiSharedService.ColorTextWrapped("Upload failed. Please select a PNG file up to 2MB.", ImGuiColors.DalamudRed);
    }

    private static byte[] RescaleProfilePic(byte[] pngBytes, float aspectW, float aspectH, int maxOutW, int maxOutH)
    {
        using var image = Image.Load<Rgba32>(pngBytes);

        var targetAspect = aspectW / aspectH;
        var srcW = image.Width;
        var srcH = image.Height;
        var srcAspect = srcW / (float)srcH;

        Rectangle crop;
        if (srcAspect > targetAspect)
        {
            // crop width
            var cropW = (int)MathF.Round(srcH * targetAspect);
            var x = (srcW - cropW) / 2;
            crop = new Rectangle(x, 0, Math.Max(1, cropW), srcH);
        }
        else
        {
            // crop height
            var cropH = (int)MathF.Round(srcW / targetAspect);
            var y = (srcH - cropH) / 2;
            crop = new Rectangle(0, y, srcW, Math.Max(1, cropH));
        }

        // fit within maxOutW/maxOutH
        var outW = crop.Width;
        var outH = crop.Height;
        var scale = 1f;
        if (outW > maxOutW) scale = MathF.Min(scale, maxOutW / (float)outW);
        if (outH > maxOutH) scale = MathF.Min(scale, maxOutH / (float)outH);
        var finalW = Math.Max(1, (int)MathF.Floor(outW * scale));
        var finalH = Math.Max(1, (int)MathF.Floor(outH * scale));

        image.Mutate(ctx =>
        {
            ctx.Crop(crop);
            if (scale < 1f)
            {
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(finalW, finalH),
                    Mode = ResizeMode.Stretch, // already cropped to aspect ratio
                    Sampler = KnownResamplers.Lanczos3
                });
            }
        });

        using var outMs = new MemoryStream();
        image.Save(outMs, new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.Level6
        });

        return outMs.ToArray();
    }

    private static bool DrawStatusCombo(string id, ref ProfileStatus status)
    {
        var items = new[]
        {
            "Not Shared", "Not Interested", "Taken", "Open", "Looking", "It's Complicated", "Ask Me"
        };

        int idx = status switch
        {
            ProfileStatus.NotShared => 0,
            ProfileStatus.NotInterested => 1,
            ProfileStatus.Taken => 2,
            ProfileStatus.Open => 3,
            ProfileStatus.Looking => 4,
            ProfileStatus.ItsComplicated => 5,
            _ => 6,
        };

        var changed = false;

        if (ImGui.BeginCombo(id, items[idx]))
        {
            for (int i = 0; i < items.Length; i++)
            {
                bool selected = i == idx;
                if (ImGui.Selectable(items[i], selected))
                {
                    idx = i;
                    status = i switch
                    {
                        0 => ProfileStatus.NotShared,
                        1 => ProfileStatus.NotInterested,
                        2 => ProfileStatus.Taken,
                        3 => ProfileStatus.Open,
                        4 => ProfileStatus.Looking,
                        5 => ProfileStatus.ItsComplicated,
                        6 => ProfileStatus.AskMe,
                        _ => ProfileStatus.NotShared,
                    };
                    changed = true;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }
}
