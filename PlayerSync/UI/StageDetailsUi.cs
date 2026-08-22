using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MareSynchronos.UI;

public class StageDetailsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly IpcManager _ipcManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly UiSharedService _uiSharedService;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;

    private record GroupPresence(string GroupId, string GroupIdOrAlias, bool IsOwnerOrModerator);

    private GroupPresence[] _groups;

    public StageFullInfoDto? StageInfo { get; set; }

    public bool HasEditPermissions { get; set; } = false;
    public bool IsEditingContents { get; set; } = false;
    public bool IsEditingCustomization { get; set; } = false;
    public bool IsEditingState { get; set; } = false;

    private GroupPresence? _newStageOwner = null;

    // Contents
    private StageDefinition? _newStageDefinition = null;
    private string? _loadStageDefinitionError = null;
    private bool _isLoadingStageDefinition = false;

    // Customization
    private StageVisibility _visibility = StageVisibility.AllPairs;
    private string _displayName = "";
    private string _version = "";
    private string _author = "";
    private string _description = "";

    // State
    private int _locationWorldId = 0;
    private int _locationTerritoryId = 0;
    private int _locationWardId = 0;
    private int _locationDivisionId = 0;
    private int _locationHouseId = 0;
    private int _locationRoomId = 0;

    private Vector3 _translation = Vector3.Zero;
    private Vector4 _rotationQuaternion = Vector4.UnitW;
    private float _uniformScale = 1.0f;

    private string? _updateContentsStatus = null;
    private string? _updateContentsError = null;
    private string? _updateCustomizeError = null;
    private string? _updateStateError = null;
    private string? _createStageStatus = null;
    private string? _createStageError = null;

    private bool _isSaving = false;

    private const float WindowWidth = 800.0f;

    public StageDetailsUi(ILogger<StageDetailsUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollector,
        StageFullInfoDto? startingStageInfo, string? owningGroupId, ApiController apiController, PairManager pairManager,
        IpcManager ipcManager, FileUploadManager fileUploadManager, UiSharedService uiSharedService, IdDisplayHandler idDisplayHandler,
        IClientState clientState, IPlayerState playerState)
        : base(logger, mediator, $"{startingStageInfo?.Customize.DisplayName ?? "New Stage"}###StageDetails{Guid.NewGuid()}", performanceCollector)
    {
        StageInfo = startingStageInfo;
        _apiController = apiController;
        _pairManager = pairManager;
        _ipcManager = ipcManager;
        _fileUploadManager = fileUploadManager;
        _uiSharedService = uiSharedService;
        _idDisplayHandler = idDisplayHandler;
        _clientState = clientState;
        _playerState = playerState;

        _groups = _pairManager.Groups.OrderBy(group => group.Key.AliasOrGID, StringComparer.CurrentCultureIgnoreCase).Select(pair => new GroupPresence(pair.Key.GID, pair.Key.AliasOrGID, pair.Value.OwnerUID == _apiController.UID || pair.Value.GroupUserInfo.HasFlag(GroupPairUserInfo.IsModerator))).ToArray();
        if (owningGroupId != null)
        {
            _newStageOwner = _groups.FirstOrDefault(group => group.GroupId == owningGroupId);
        }

        SizeConstraints = new()
        {
            MinimumSize = new(WindowWidth, Single.MinValue),
            MaximumSize = new(WindowWidth, Single.MinValue),
        };
        Size = new(WindowWidth, Single.MinValue);
        SizeCondition = ImGuiCond.Always;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;

        if (StageInfo == null && StageLocation.TryGetLocation(_clientState, _playerState, out var location))
        {
            _locationWorldId = (int)location.WorldId;
            _locationTerritoryId = location.TerritoryId;
            _locationWardId = location.WardId;
            _locationDivisionId = location.DivisionId;
            _locationHouseId = location.HouseId;
            _locationRoomId = location.RoomId;
        }
        else if (StageInfo != null)
        {
            CustomizeFromDto(StageInfo.Customize);
            StateFromDto(StageInfo.State);
        }

        HasEditPermissions = StageInfo == null
            || (StageInfo.Info.GroupOwnerGID == "" && StageInfo.Info.UserOwnerUID == _apiController.UID)
            || (StageInfo.Info.GroupOwnerGID != "" && _groups.Any(group => group.GroupId == StageInfo.Info.GroupOwnerGID && group.IsOwnerOrModerator));
    }

    public override void PreDraw()
    {
        base.PreDraw();
        ImGui.SetNextWindowPos(ImGui.GetWindowViewport().WorkSize / 2 - new Vector2(WindowWidth, 400.0f) / 2, ImGuiCond.FirstUseEver);
    }

    public override void OnClose()
    {
        base.OnClose();
        Mediator.Publish(new RemoveWindowMessage(this));
    }

    protected override void DrawInternal()
    {
        using var __ = ImRaii.Disabled(_isSaving);

        // Heading: display name or SID
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6.0f * ImGuiHelpers.GlobalScale);
        _uiSharedService.IconText(FontAwesomeIcon.MapMarkerAlt);
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 6.0f * ImGuiHelpers.GlobalScale);
        if (StageInfo != null)
        {
            using (_uiSharedService.HeaderFont.Push())
            {
                ImGui.TextWrapped(String.IsNullOrEmpty(StageInfo.Customize.DisplayName) ? StageInfo.SID : StageInfo.Customize.DisplayName);
            }
            bool nameClicked = ImGui.IsItemClicked();
            UiSharedService.AttachToolTip(StageInfo.SID + UiSharedService.TooltipSeparator + "Click to copy Stage ID");
            if (nameClicked)
            {
                ImGui.SetClipboardText(StageInfo.SID);
            }

            string locationString = _uiSharedService.LocationToString(
                StageInfo.State.LocationWorldId,
                StageInfo.State.LocationTerritoryId,
                StageInfo.State.LocationWardId,
                StageInfo.State.LocationDivisionId,
                StageInfo.State.LocationHouseId,
                StageInfo.State.LocationHouseId);
            ImGui.TextWrapped(locationString);
            UiSharedService.AttachToolTip(locationString + UiSharedService.TooltipSeparator + "Click to copy location");
            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(locationString);
            }

            if (!String.IsNullOrEmpty(StageInfo.Info.GroupOwnerGID))
            {
                string groupDisplayName = _idDisplayHandler.GetGroupAlias(StageInfo.Info.GroupOwnerGID, _pairManager);
                ImGui.Text($"{groupDisplayName}{(groupDisplayName.EndsWith('s') ? "'" : "'s")} stage");
                var groupClicked = ImGui.IsItemClicked();
                UiSharedService.AttachToolTip(groupDisplayName + UiSharedService.TooltipSeparator + "Click to copy Syncshell ID");
                if (groupClicked)
                {
                    ImGui.SetClipboardText(groupDisplayName);
                }
                ImGui.SameLine(0.0f, 0.0f);
                ImGui.TextDisabled(" visible to ");
                ImGui.SameLine(0.0f, 0.0f);
                ImGui.TextUnformatted(GroupStageVisibilityToString(StageInfo.Customize.Visibility));
            }
            else
            {
                string userDisplayName = _idDisplayHandler.GetUserAlias(StageInfo.Info.UserOwnerUID, _apiController, _pairManager);
                ImGui.Text($"{userDisplayName}'s stage");
                var userClicked = ImGui.IsItemClicked();
                UiSharedService.AttachToolTip(userDisplayName + UiSharedService.TooltipSeparator + "Click to copy User ID");
                if (userClicked)
                {
                    ImGui.SetClipboardText(userDisplayName);
                }
                ImGui.SameLine(0.0f, 0.0f);
                ImGui.TextDisabled(" visible to ");
                ImGui.SameLine(0.0f, 0.0f);
                ImGui.TextUnformatted(UserStageVisibilityToString(StageInfo.Customize.Visibility));
            }

            ImGuiHelpers.ScaledDummy(2.0f);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(2.0f);

            if (HasEditPermissions)
            {
                using (ImRaii.Disabled(IsEditingContents || IsEditingCustomization || IsEditingState))
                {
                    if (ImGui.Button("Edit Info"u8))
                    {
                        IsEditingCustomization = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Update Stage"u8))
                    {
                        IsEditingContents = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Edit Location"u8))
                    {
                        IsEditingState = true;
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.LeftCtrl)))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Delete Stage") && !_isSaving)
                    {
                        _isSaving = true;
                        _ = DeleteStageAsync();
                    }
                }
                UiSharedService.AttachToolTip("Delete Stage" + UiSharedService.TooltipSeparator + "Hold Ctrl to enable");
                ImGuiHelpers.ScaledDummy(5.0f);
            }

            if (!IsEditingContents && !IsEditingCustomization && !IsEditingState)
            {
                if (StageInfo.Customize.Description != "")
                {
                    ImGui.TextWrapped(StageInfo.Customize.Description);
                    ImGuiHelpers.ScaledDummy(2.0f);
                }
                if (StageInfo.Customize.Author != "" && StageInfo.Customize.Version == "")
                {
                    ImGui.TextUnformatted($"By {StageInfo.Customize.Author}");
                    ImGui.SameLine();
                }
                else if (StageInfo.Customize.Author == "" && StageInfo.Customize.Version != "")
                {
                    ImGui.TextUnformatted($"Version {StageInfo.Customize.Version}");
                    ImGui.SameLine();
                }
                else if (StageInfo.Customize.Author != "" && StageInfo.Customize.Version != "")
                {
                    ImGui.TextUnformatted($"By {StageInfo.Customize.Author}, version {StageInfo.Customize.Version}");
                    ImGui.SameLine();
                }
                ImGui.TextDisabled($"updated {StageInfo.Contents.RevisionDateUtc.ToLocalTime().ToString("g")}");
            }
        }
        else
        {
            using (_uiSharedService.HeaderFont.Push())
            {
                ImGui.TextWrapped("New Stage"u8);
            }
            using (var combo = ImRaii.Combo("Owner"u8, _newStageOwner?.GroupIdOrAlias ?? "(Personal)"))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable("(Personal)"u8, _newStageOwner == null))
                    {
                        _newStageOwner = null;
                    }
                    foreach (var group in _groups)
                    {
                        using (ImRaii.Disabled(!group.IsOwnerOrModerator))
                        {
                            if (ImGui.Selectable($"{group.GroupIdOrAlias}###{group.GroupId}", _newStageOwner == group))
                            {
                                _newStageOwner = group;
                            }
                        }
                    }
                }
            }
            ImGuiHelpers.ScaledDummy(5.0f);
        }

        // For some reason the automatic input item width breaks with auto sizing windows
        using (ImRaii.ItemWidth(ImGui.GetContentRegionAvail().X * 0.666f))
        {
            if (StageInfo == null || IsEditingContents)
            {
                DrawContentsSection();
            }

            if (StageInfo == null || IsEditingCustomization)
            {
                DrawCustomizeSection();
            }

            if (StageInfo == null || IsEditingState)
            {
                DrawStateSection();
            }
        }

        if (StageInfo == null)
        {
            using (ImRaii.Disabled(_newStageDefinition == null))
            {
                if (ImGui.Button("Create Stage") && _newStageDefinition != null && !_isSaving)
                {
                    _isSaving = true;
                    var customization = new StageCustomizeDto();
                    CustomizeToDto(customization);
                    var state = new StageStateDto();
                    StateToDto(state);
                    _ = CreateStageAsync(_newStageDefinition, _newStageOwner?.GroupId ?? "", customization, state, new Progress<string>(message => _createStageStatus = message));
                }
                if (_createStageStatus != null)
                {
                    ImGui.SameLine();
                    ImGui.TextWrapped(_createStageStatus);
                }
            }

            if (_createStageError != null)
            {
                ImGui.TextColoredWrapped(ImGuiColors.ErrorForeground, _createStageError);
            }
        }
    }

    private async Task DeleteStageAsync()
    {
        try
        {
            if (StageInfo != null)
            {
                await _apiController.StageDelete(StageInfo.SID).ConfigureAwait(false);
                IsOpen = false;
                OnClose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete stage!");
        }
        _isSaving = false;
    }

    // Returns a Stream for a mod resource that contains data, or null otherwise
    private sealed class StageModStreamVisitor : IModResourceDefinitionVisitor<object?, Stream?>
    {
        private StageModStreamVisitor()
        { }

        public static Stream? VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref object? param)
        {
            return new FileStream(definition.SourceDiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public static Stream? VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref object? param)
        {
            var bytes = EmbeddedModResourceDefinition.DecompressDataBytes(definition.CompressedDataBytes, definition.CompressionScheme);
            // Needs to be publiclyVisible so we can get the array back later without copying
            return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        }

        public static Stream? VisitGameModResourceDefinition(GameModResourceDefinition definition, ref object? param)
        {
            return null;
        }
    }

    // Uploads the mod files, then minifies the definition and uploads that too
    private async Task<(string DefinitionHash, List<StageModUsageDto> Mods)> UploadContentsAsync(StageDefinition definition, IProgress<string> progress)
    {
        // Poor dev's deep clone until there is a duplicate or clone function
        using (MemoryStream ms = new())
        {
            definition.WriteToJSONStream(ms);
            ms.Position = 0;
            StageDefinition.TryParseJSONStream(ms, out definition!);
        }

        // Upload mods
        List<(string ModpackId, Stream DataStream, string GamePath)> modStreams = new();
        foreach (var modpack in definition.EmbeddedModpacks)
        {
            foreach (var modResource in modpack.Value.ModdedResources)
            {
                object? param = null;
                var stream = modResource.Value.Visit<StageModStreamVisitor, object?, Stream?>(ref param);
                if (stream != null)
                {
                    // This resource has data, so compress and upload the data and strip it from the stage for upload.
                    stream.Position = 0;
                    modStreams.Add(new(modpack.Key, stream, modResource.Key));
                }
                else
                {
                    // This resource has no data (i.e. is a redirection), so leave it be
                }
            }
            modpack.Value.PenumbraSourceModDirectory = "";
            modpack.Value.PenumbraSourceModVersion = "";
        }

        List<string> hashes;
        if (modStreams.Count > 0)
        {
            hashes = await _fileUploadManager.UploadStreams(modStreams.Select(s => s.DataStream).ToList(), modStreams.Select(s => Path.GetExtension(s.GamePath)).ToList(), progress, 1, ct: null).ConfigureAwait(false);
        }
        else
        {
            hashes = new();
        }
        foreach (var stream in modStreams)
        {
            await stream.DataStream.DisposeAsync().ConfigureAwait(false);
        }

        // Clear out mods with independenly-stored data
        foreach (var stream in modStreams)
        {
            definition.EmbeddedModpacks[stream.ModpackId].ModdedResources.Remove(stream.GamePath);
        }

        definition.Info = new();

        // Upload the definition
        List<string> mainHashes;
        using (var mainFileStream = new MemoryStream())
        {
            definition.WriteToJSONStream(mainFileStream);
            mainFileStream.Position = 0;
            mainHashes = await _fileUploadManager.UploadStreams(new List<Stream> { mainFileStream }, new List<string> { ".json" }, progress, 2, ct: null).ConfigureAwait(false);
        }

        // Create stage!
        List<StageModUsageDto> mods = modStreams.Zip(hashes).Select(data => new StageModUsageDto() { Hash = data.Second, ModpackId = data.First.ModpackId, GamePath = data.First.GamePath }).ToList();
        return (mainHashes[0], mods);
    }

    private async Task CreateStageAsync(StageDefinition definition, string ownerGroupId, StageCustomizeDto customization, StageStateDto state, IProgress<string> progress)
    {
        try
        {
            (string mainHash, var mods) = await UploadContentsAsync(definition, progress).ConfigureAwait(false);
            StageInfo = await _apiController.StageCreate(mainHash, mods, ownerGroupId, customization, state).ConfigureAwait(false);
            IsEditingContents = false;
            IsEditingCustomization = false;
            IsEditingState = false;
            CustomizeFromDto(StageInfo.Customize);
            StateFromDto(StageInfo.State);
            _createStageError = null;
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create stage!");
            _createStageError = ex.ToString();
        }
        _isSaving = false;
        _createStageStatus = null;
    }

    private void DrawContentsSection()
    {
        _uiSharedService.HeaderText("Update Stage");
        ImGuiHelpers.ScaledDummy(3.0f);

        if (StageInfo == null || IsEditingContents)
        {
            using (ImRaii.Disabled(_isLoadingStageDefinition))
            using (var stageCombo = ImRaii.Combo("Stage"u8, _newStageDefinition != null ? $"{_newStageDefinition.Info.Name} (ver. {_newStageDefinition.Info.VersionString})" : "<Select>"u8))
            {
                if (stageCombo.Success)
                {
                    foreach (var availableStage in _ipcManager.Stagehand.StagehandApi.GetLocalStageDefinitions())
                    {
                        if (ImGui.Selectable($"{availableStage.Name} (ver. {availableStage.VersionString})###{availableStage.Filename}"))
                        {
                            if (!_isLoadingStageDefinition)
                            {
                                _isLoadingStageDefinition = true;
                                _ = LoadStageDefinitionAsync(availableStage.Filename);
                            }
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
            }
            if (_loadStageDefinitionError != null)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ErrorForeground))
                {
                    ImGui.Text(_loadStageDefinitionError);
                }
            }
        }
        else
        {
            //ImGui.TextUnformatted($"{StageInfo.Contents.StageFileHash} with ");
            //ImGui.SameLine();
            //ImGui.TextUnformatted($"{StageInfo.Contents.Mods.Count}");
            ImGui.TextUnformatted($"Revision {StageInfo.Contents.Revision} by {StageInfo.Contents.RevisionAuthorUid}");
            ImGui.TextDisabled($"{StageInfo.Contents.RevisionDateUtc.ToLocalTime().ToString("g")}");
        }

        if (StageInfo != null)
        {
            if (IsEditingContents)
            {
                ImGuiHelpers.ScaledDummy(5.0f);

                using (ImRaii.Disabled(_newStageDefinition == null))
                {
                    if (ImGui.Button("Save Stage"u8) && _newStageDefinition != null && !_isSaving)
                    {
                        _isSaving = true;
                        _ = UpdateContentsAsync(_newStageDefinition, new Progress<string>(message => _updateContentsStatus = message));
                    }
                    if (_updateContentsStatus != null)
                    {
                        ImGui.SameLine();
                        ImGui.TextWrapped(_updateContentsStatus);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                {
                    IsEditingContents = false;
                }
            }

            if (_updateContentsError != null)
            {
                ImGui.TextColoredWrapped(ImGuiColors.ErrorForeground, _updateContentsError);
            }
        }
    }

    private async Task UpdateContentsAsync(StageDefinition definition, IProgress<string> progress)
    {
        try
        {
            if (StageInfo != null)
            {
                (string mainHash, var mods) = await UploadContentsAsync(definition, progress).ConfigureAwait(false);
                var newContents = await _apiController.StageOverwriteContents(StageInfo.SID, mainHash, mods).ConfigureAwait(false);
                StageInfo.Contents = newContents;
                _updateContentsError = null;
                IsEditingContents = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stage contents!");
            _updateContentsError = ex.ToString();
        }
        _isSaving = false;
        _updateContentsStatus = null;
    }

    private async Task LoadStageDefinitionAsync(string definitionFilename)
    {
        try
        {
            using (var stream = new FileStream(definitionFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (StageDefinition.TryParseJSONStream(stream, out var definition))
                {
                    _newStageDefinition = definition;
                    
                    if (StageInfo == null)
                    {
                        _displayName = definition.Info.Name;
                        _version = definition.Info.VersionString;
                        _author = definition.Info.AuthorName;
                        _description = definition.Info.Description;
                    }

                    _loadStageDefinitionError = null;
                }
                else
                {
                    _loadStageDefinitionError = "Failed to parse stage definition.";
                }
            }
        }
        catch (Exception ex)
        {
            _loadStageDefinitionError = "Failed to load stage definition.";
            _logger.LogError(ex, "Failed to load stage definition from {filename}.", definitionFilename);
        }
        _isLoadingStageDefinition = false;
    }

    private static string GroupStageVisibilityToString(StageVisibility visibility)
    {
        return visibility switch
        {
            StageVisibility.OwnersOnly => "Syncshell Owner & Moderators",
            StageVisibility.DirectPairs => "Syncshell Members (Excluding Guests)",
            StageVisibility.AllPairs => "All Syncshell Members",
            StageVisibility.Everyone => "Everyone",
            _ => throw new ArgumentException("Unknown StageVisibility", nameof(visibility)),
        };
    }

    private static string UserStageVisibilityToString(StageVisibility visibility)
    {
        return visibility switch
        {
            StageVisibility.OwnersOnly => "Owner Only",
            StageVisibility.DirectPairs => "Direct Pairs",
            StageVisibility.AllPairs => "All Pairs (Excluding ZoneSync)",
            StageVisibility.Everyone => "Everyone",
            _ => throw new ArgumentException("Unknown StageVisibility", nameof(visibility)),
        };
    }

    private void DrawCustomizeSection()
    {
        _uiSharedService.HeaderText("Info");

        if (_newStageDefinition != null && IsEditingCustomization)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileDownload, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                IsEditingCustomization = true;

                _displayName = _newStageDefinition.Info.Name;
                _version = _newStageDefinition.Info.VersionString;
                _author = _newStageDefinition.Info.AuthorName;
                _description = _newStageDefinition.Info.Description;
            }
            UiSharedService.AttachToolTip("Load from Stage" + UiSharedService.TooltipSeparator + $"Use the name, version, author, and description from the selected stage:\n{_newStageDefinition.Info.Name}");
        }
        ImGuiHelpers.ScaledDummy(3.0f);

        bool isGroupStage;
        if (StageInfo != null)
        {
            isGroupStage = StageInfo.Info.GroupOwnerGID != "";
        }
        else
        {
            isGroupStage = _newStageOwner != null;
        }

        DrawEnumProperty("Visibility"u8, ref _visibility, isGroupStage ? GroupStageVisibilityToString : UserStageVisibilityToString, IsEditingCustomization);
        DrawStringProperty("Name"u8, ref _displayName, IsEditingCustomization);
        DrawStringProperty("Version"u8, ref _version, IsEditingCustomization);
        DrawStringProperty("Author"u8, ref _author, IsEditingCustomization);
        DrawMultilineStringProperty("Description"u8, ref _description, IsEditingCustomization);

        if (StageInfo != null)
        {
            if (IsEditingCustomization)
            {
                ImGuiHelpers.ScaledDummy(5.0f);

                if (ImGui.Button("Save Info"u8) && !_isSaving)
                {
                    _isSaving = true;
                    var newCustomization = new StageCustomizeDto();
                    CustomizeToDto(newCustomization);
                    _ = UpdateCustomizeAsync(newCustomization);
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                {
                    CustomizeFromDto(StageInfo.Customize);
                    IsEditingCustomization = false;
                }
            }

            if (_updateCustomizeError != null)
            {
                ImGui.TextColoredWrapped(ImGuiColors.ErrorForeground, _updateCustomizeError);
            }
        }
    }

    private async Task UpdateCustomizeAsync(StageCustomizeDto newCustomization)
    {
        try
        {
            if (StageInfo != null)
            {
                await _apiController.StageUpdateCustomize(StageInfo.SID, newCustomization).ConfigureAwait(false);

                CustomizeFromDto(newCustomization);
                CustomizeToDto(StageInfo.Customize);
            }

            IsEditingCustomization = false;
            _updateCustomizeError = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving customization!");
            _updateCustomizeError = ex.ToString();
        }
        _isSaving = false;
    }

    private void DrawStateSection()
    {
        _uiSharedService.HeaderText("Location");

        if (StageInfo == null || IsEditingState)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.LocationCrosshairs, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                if (StageLocation.TryGetLocation(_clientState, _playerState, out var location))
                {
                    IsEditingState = true;
                    _locationWorldId = (int)location.WorldId;
                    _locationTerritoryId = location.TerritoryId;
                    _locationWardId = location.WardId;
                    _locationDivisionId = location.DivisionId;
                    _locationHouseId = location.HouseId;
                    _locationRoomId = location.RoomId;
                }
            }
            UiSharedService.AttachToolTip("Use Current Location" + UiSharedService.TooltipSeparator + $"Use the World, territory, ward, division, house, and room.");
        }
        ImGuiHelpers.ScaledDummy(3.0f);

        DrawIntProperty("World"u8, ref _locationWorldId, IsEditingState);
        DrawIntProperty("Territory"u8, ref _locationTerritoryId, IsEditingState);
        DrawIntProperty("Ward"u8, ref _locationWardId, IsEditingState);
        DrawIntProperty("Division"u8, ref _locationDivisionId, IsEditingState);
        DrawIntProperty("House"u8, ref _locationHouseId, IsEditingState);
        DrawIntProperty("Room"u8, ref _locationRoomId, IsEditingState);

        // Disabled until support is added to Stagehand
#if false
        ImGuiHelpers.ScaledDummy(5.0f);

        DrawVector3Property("Translation"u8, ref _translation, IsEditingState);
        DrawVector4Property("Rotation Quaternion"u8, ref _rotationQuaternion, IsEditingState);
        DrawFloatProperty("Scale", ref _uniformScale, IsEditingState);
#endif

        if (StageInfo != null)
        {
            if (IsEditingState)
            {
                ImGuiHelpers.ScaledDummy(5.0f);

                if (ImGui.Button("Save Location"u8) && !_isSaving)
                {
                    _isSaving = true;
                    var newState = new StageStateDto();
                    StateToDto(newState);
                    _ = UpdateStateAsync(newState);
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                {
                    StateFromDto(StageInfo.State);
                    IsEditingState = false;
                }
            }

            if (_updateStateError != null)
            {
                ImGui.TextColoredWrapped(ImGuiColors.ErrorForeground, _updateStateError);
            }
        }
    }

    private async Task UpdateStateAsync(StageStateDto newState)
    {
        try
        {
            if (StageInfo != null)
            {
                await _apiController.StageUpdateState(StageInfo.SID, newState).ConfigureAwait(false);

                StateFromDto(newState);
                StateToDto(StageInfo.State);
            }

            IsEditingState = false;
            _updateStateError = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state!");
            _updateStateError = ex.ToString();
        }
        _isSaving = false;
    }

    private void CustomizeToDto(StageCustomizeDto dto)
    {
        dto.Visibility = _visibility;
        dto.DisplayName = _displayName;
        dto.Version = _version;
        dto.Author = _author;
        dto.Description = _description;
    }

    private void CustomizeFromDto(StageCustomizeDto dto)
    {
        _visibility = dto.Visibility;
        _displayName = dto.DisplayName;
        _version = dto.Version;
        _author = dto.Author;
        _description = dto.Description;
    }

    private void StateToDto(StageStateDto dto)
    {
        dto.LocationWorldId = _locationWorldId;
        dto.LocationTerritoryId = _locationTerritoryId;
        dto.LocationWardId = _locationWardId;
        dto.LocationDivisionId = _locationDivisionId;
        dto.LocationHouseId = _locationHouseId;
        dto.LocationRoomId = _locationRoomId;

        dto.Translation = _translation;
        dto.Rotation = _rotationQuaternion;
        dto.UniformScale = _uniformScale;
    }

    private void StateFromDto(StageStateDto dto)
    {
        _locationWorldId = dto.LocationWorldId;
        _locationTerritoryId = dto.LocationTerritoryId;
        _locationWardId = dto.LocationWardId;
        _locationDivisionId = dto.LocationDivisionId;
        _locationHouseId = dto.LocationHouseId;
        _locationRoomId = dto.LocationRoomId;

        _translation = dto.Translation;
        _rotationQuaternion = dto.Rotation;
        _uniformScale = dto.UniformScale;
    }

    private static class EnumValuesCache<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly TEnum[] Values = Enum.GetValues<TEnum>();
    }

    private void DrawEnumProperty<TEnum>(ImU8String label, ref TEnum value, Func<TEnum, string>? toString, bool isEditing)
        where TEnum : struct, Enum
    {
        if (StageInfo == null || isEditing)
        {
            int currentIndex = EnumValuesCache<TEnum>.Values.IndexOf(value);
            ImGui.Combo<TEnum>(label, ref currentIndex, EnumValuesCache<TEnum>.Values, toString ?? (static (value) => value.ToString()));
            value = EnumValuesCache<TEnum>.Values[currentIndex];
        }
        else
        {
            ImGui.LabelText(label, toString?.Invoke(value) ?? value.ToString());
        }
    }

    private void DrawIntProperty(ImU8String label, ref int value, bool isEditing)
    {
        if (StageInfo == null || isEditing)
        {
            ImGui.InputInt(label, ref value);
        }
        else
        {
            ImGui.LabelText(label, value.ToString());
        }
    }

    private void DrawUIntProperty(ImU8String label, ref uint value, bool isEditing)
    {
        if (StageInfo == null || isEditing)
        {
            ImGui.InputUInt(label, ref value);
        }
        else
        {
            ImGui.LabelText(label, value.ToString());
        }
    }

    private void DrawStringProperty(ImU8String label, ref string value, bool isEditing)
    {
        if (StageInfo == null || isEditing)
        {
            ImGui.InputText(label, ref value);
        }
        else
        {
            ImGui.LabelText(label, value);
        }
    }

    private void DrawMultilineStringProperty(ImU8String label, ref string value, bool isEditing)
    {
        if (StageInfo == null || isEditing)
        {
            // Word wrap for multi-line input texts isn't in our current ImGui version ='(
            ImGui.InputTextMultiline(label, ref value, flags: ImGuiInputTextFlags.CtrlEnterForNewLine);
        }
        else
        {
            ImGui.TextWrapped(value);
        }
    }

    private void DrawFloatProperty(ImU8String label, ref float value, bool isEditing)
    {
        if (StageInfo == null || isEditing)
        {
            ImGui.InputFloat(label, ref value);
        }
        else
        {
            ImGui.LabelText(label, value.ToString());
        }
    }

    private void DrawVector3Property(ImU8String label, ref Vector3 value, bool isEditing)
    {
        using (ImRaii.Disabled(StageInfo != null && !isEditing))
        {
            ImGui.InputFloat3(label, ref value);
        }
    }

    private void DrawVector4Property(ImU8String label, ref Vector4 value, bool isEditing)
    {
        using (ImRaii.Disabled(StageInfo != null && !isEditing))
        {
            ImGui.InputFloat4(label, ref value);
        }
    }
}
