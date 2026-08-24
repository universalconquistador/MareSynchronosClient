using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
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
    // Company workshops have an IntendedTerritoryType for housing but do not actually support housing stuff
    // (e.g. cannot check the ward/division/house/room via HousingManager)
    private static readonly uint[] WorkshopTerritoryTypes =
    [
        423, // Company Workshop - Mist
        424, // Company Workshop - The Goblet
        425, // Company Workshop - The Lavender Beds
        653, // Company Workshop - Shirogane
        984, // Company Workshop - Empyreum
    ];
    private const int TerritoryUseHousingOutdoor = 13;
    private const int TerritoryUseHousingIndoor = 14;

    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly IpcManager _ipcManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly UiSharedService _uiSharedService;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly StageConfigService _stageConfigService;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IDataManager _dataManager;

    private record GroupPresence(string GroupId, string GroupIdOrAlias, bool IsOwnerOrModerator);

    private GroupPresence[] _groups;

    public StageFullInfoDto? StageInfo { get; set; }

    public bool HasEditPermissions { get; set; } = false;
    public bool IsEditingContents { get; set; } = false;
    public bool IsEditingCustomization { get; set; } = false;
    public bool IsEditingState { get; set; } = false;

    private GroupPresence? _newStageOwner = null;

    // Contents
    private string? _newStageFilename = null;
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

    private string _worldFilter = "";
    private string _territoryFilter = "";

    private const float WindowWidth = 800.0f;

    public StageDetailsUi(ILogger<StageDetailsUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollector,
        StageFullInfoDto? startingStageInfo, string? owningGroupId, ApiController apiController, PairManager pairManager,
        IpcManager ipcManager, FileUploadManager fileUploadManager, UiSharedService uiSharedService, IdDisplayHandler idDisplayHandler,
        StageConfigService stageConfigService, IClientState clientState, IPlayerState playerState, IDataManager dataManager)
        : base(logger, mediator, $"{startingStageInfo?.Customize.DisplayName ?? "New Stage"}###StageDetails{Guid.NewGuid()}", performanceCollector)
    {
        StageInfo = startingStageInfo;
        _apiController = apiController;
        _pairManager = pairManager;
        _ipcManager = ipcManager;
        _fileUploadManager = fileUploadManager;
        _uiSharedService = uiSharedService;
        _idDisplayHandler = idDisplayHandler;
        _stageConfigService = stageConfigService;
        _clientState = clientState;
        _playerState = playerState;
        _dataManager = dataManager;

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

        Mediator.Subscribe<StageSubscriptionsChangedMessage>(this, OnStageSubscriptionsChanged);
        Mediator.Subscribe<StageSubscribedContentsChangedMessage>(this, OnStageSubscribedContentsChanged);
        Mediator.Subscribe<StageSubscribedStateChangedMessage>(this, OnStageSubscribedStateChanged);
        Mediator.Subscribe<StageCustomizeChangedMessage>(this, OnStageCustomizeChanged);
    }

    private void OnStageSubscriptionsChanged(StageSubscriptionsChangedMessage message)
    {
        if (StageInfo != null && message.AddedSubscribedStages.FirstOrDefault(info => info.SID == StageInfo.SID) is StageFullInfoDto newInfo)
        {
            StageInfo.SubscriptionState = newInfo.SubscriptionState;
        }
        else if (StageInfo != null && message.RemovedSubscribedStageIds.Contains(StageInfo.SID))
        {
            StageInfo.SubscriptionState = StageSubscriptionFlags.None;
        }
    }

    private void OnStageSubscribedContentsChanged(StageSubscribedContentsChangedMessage message)
    {
        if (StageInfo != null && message.StageId == StageInfo.SID)
        {
            StageInfo.Contents = message.NewContents;
        }
    }

    private void OnStageSubscribedStateChanged(StageSubscribedStateChangedMessage message)
    {
        if (StageInfo != null && message.StageId == StageInfo.SID)
        {
            StageInfo.State = message.NewState;
        }
    }

    private void OnStageCustomizeChanged(StageCustomizeChangedMessage message)
    {
        if (StageInfo != null && message.StageId == StageInfo.SID)
        {
            StageInfo.Customize = message.NewCustomize;
        }
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

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            StageHelpers.DrawStagePairButton(StageInfo, _apiController, _logger);

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
                Mediator.Publish(new StageDeletedMessage(StageInfo.SID));
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

            if (_newStageFilename != null)
            {
                _stageConfigService.Current.UploadedDefinitionPathToStageId[_newStageFilename] = StageInfo.SID;
                _stageConfigService.Save();
            }

            Mediator.Publish(new StageCreatedMessage(StageInfo));
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
        _uiSharedService.HeaderText(StageInfo == null ? "Select Stage" : "Update Stage");
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
                            SelectDefinition(availableStage.Filename);
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

    public void SelectDefinition(string filename)
    {
        if (!_isLoadingStageDefinition)
        {
            if (StageInfo != null)
            {
                IsEditingContents = true;
            }
            _isLoadingStageDefinition = true;
            _newStageFilename = filename;
            _ = LoadStageDefinitionAsync(filename);
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

                if (_newStageFilename != null)
                {
                    _stageConfigService.Current.UploadedDefinitionPathToStageId[_newStageFilename] = StageInfo.SID;
                    _stageConfigService.Save();
                }
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
                Mediator.Publish(new StageCustomizeChangedMessage(StageInfo.SID, StageInfo.Customize));
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

        string WorldIdToString(int worldId)
        {
            if (worldId >= 0 && _dataManager.Excel.GetSheet<World>().TryGetRow((uint)worldId, out var worldRow))
            {
                return $"{worldRow.Name} ({worldRow.DataCenter.Value.Name})";
            }
            else
            {
                return $"World {worldId}";
            }
        }
        using (var worldCombo = ImRaii.Combo("World"u8, WorldIdToString(_locationWorldId)))
        {
            if (worldCombo.Success)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.InputTextWithHint("###WorldFilter"u8, "Filter"u8, ref _worldFilter);
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times, new(ImGui.GetFrameHeight())))
                {
                    _worldFilter = "";
                }
                
                using (var items = ImRaii.Child("###WorldOptions", new Vector2(ImGui.GetContentRegionAvail().X, 100.0f * ImGuiHelpers.GlobalScale), border: false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                {
                    if (items.Success)
                    {
                        var worldSheet = _dataManager.Excel.GetSheet<World>();
                        foreach (var worldRow in worldSheet)
                        {
                            if (worldRow.IsPublic && (_worldFilter == "" || worldRow.Name.ToString().Contains(_worldFilter, StringComparison.CurrentCultureIgnoreCase)))
                            {
                                var startX = ImGui.GetCursorPosX();
                                if (ImGui.Selectable($"###{worldRow.RowId}", _locationWorldId == (int)worldRow.RowId, size: new(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight())))
                                {
                                    _locationWorldId = (int)worldRow.RowId;
                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.SameLine(startX);
                                ImGui.SetCursorPosX(startX + ImGui.GetStyle().CellPadding.X);
                                ImGui.TextUnformatted(WorldIdToString((int)worldRow.RowId));
                            }
                        }
                    }
                }
            }
        }

        string TerritoryIdToString(int territoryId)
        {
            if (territoryId >= 0 && _dataManager.Excel.GetSheet<TerritoryType>().TryGetRow((uint)territoryId, out var territoryRow))
            {
                bool inHousingIndoor = territoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingIndoor && !WorkshopTerritoryTypes.Contains((uint)territoryId);
                bool inHousingOutdoor = inHousingIndoor || territoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingOutdoor;
                return $"{territoryRow.PlaceName.Value.Name.ToString()}{(inHousingIndoor ? " (Housing Indoors)" : (inHousingOutdoor ? " (Housing Outdoors)" : ""))}";
            }
            else
            {
                return $"Unknown ({territoryId})";
            }
        }
        using (var territoryCombo = ImRaii.Combo("Zone"u8, TerritoryIdToString(_locationTerritoryId)))
        {
            if (territoryCombo.Success)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.InputTextWithHint("###TerritoryFilter"u8, "Filter"u8, ref _territoryFilter);
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Times, new(ImGui.GetFrameHeight())))
                {
                    _territoryFilter = "";
                }

                using (var items = ImRaii.Child("###TerritoryOptions", new Vector2(ImGui.GetContentRegionAvail().X, 100.0f * ImGuiHelpers.GlobalScale), border: false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                {
                    if (items.Success)
                    {
                        foreach (var territoryRow in _dataManager.Excel.GetSheet<TerritoryType>())
                        {
                            if (territoryRow.PlaceName.IsValid && !string.IsNullOrEmpty(territoryRow.PlaceName.Value.Name.ToString()) && (_territoryFilter == "" || territoryRow.PlaceName.Value.Name.ToString().Contains(_territoryFilter, StringComparison.CurrentCultureIgnoreCase)))
                            {
                                var startX = ImGui.GetCursorPosX();
                                if (ImGui.Selectable($"###{territoryRow.RowId}", _locationTerritoryId == territoryRow.RowId, size: new(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight())))
                                {
                                    _locationTerritoryId = (int)territoryRow.RowId;

                                    bool selectionHousingIndoor = territoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingIndoor && !WorkshopTerritoryTypes.Contains(territoryRow.RowId);
                                    bool selectionHousingOutdoor = selectionHousingIndoor || territoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingOutdoor;

                                    if (selectionHousingOutdoor)
                                    {
                                        if (_locationDivisionId <= 0)
                                        {
                                            _locationDivisionId = 1;
                                        }
                                        if (_locationWardId <= 0)
                                        {
                                            _locationWardId = 1;
                                        }
                                    }
                                    else
                                    {
                                        _locationDivisionId = -1;
                                        _locationWardId = -1;
                                    }

                                    if (selectionHousingIndoor)
                                    {
                                        if (_locationHouseId < 0)
                                        {
                                            _locationHouseId = 1;
                                        }
                                        if (_locationRoomId < 0)
                                        {
                                            _locationRoomId = 0;
                                        }
                                    }
                                    else
                                    {
                                        _locationHouseId = -1;
                                        _locationRoomId = -1;
                                    }

                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.SameLine(startX);
                                ImGui.SetCursorPosX(startX + ImGui.GetStyle().CellPadding.X);
                                ImGui.TextUnformatted(TerritoryIdToString((int)territoryRow.RowId));
                            }
                        }
                    }
                }
            }
        }

        bool inHousingIndoor = false;
        bool inHousingOutdoor = false;
        if (_locationTerritoryId >= 0 && _dataManager.Excel.GetSheet<TerritoryType>().TryGetRow((uint)_locationTerritoryId, out var currentTerritoryRow))
        {
            inHousingIndoor = currentTerritoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingIndoor && !WorkshopTerritoryTypes.Contains((uint)_locationTerritoryId);
            inHousingOutdoor = inHousingIndoor || currentTerritoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingOutdoor;
        }

        if (inHousingOutdoor)
        {
            using (var wardCombo = ImRaii.Combo("Ward"u8, $"Ward {_locationWardId}"))
            {
                if (wardCombo.Success)
                {
                    for (int i = 1; i <= 30; i++)
                    {
                        if (ImGui.Selectable($"Ward {i}", i == _locationWardId))
                        {
                            _locationWardId = i;
                        }
                    }
                }
            }

            bool isSubdivision = _locationDivisionId == 2;
            ImGui.Checkbox("Subdivision"u8, ref isSubdivision);
            _locationDivisionId = isSubdivision ? 2 : 1;

            if (inHousingIndoor)
            {
                static string HouseIdToString(int houseId, bool isSubdivision)
                {
                    if (houseId < 0)
                    {
                        return houseId.ToString();
                    }
                    else if (houseId == 0)
                    {
                        return $"Apartment Building{(isSubdivision ? " (Subdivision)" : "")}";
                    }
                    else
                    {
                        if (isSubdivision)
                        {
                            houseId += 30;
                        }
                        return $"House {houseId}";
                    }
                }
                using (var houseCombo = ImRaii.Combo("House"u8, HouseIdToString(_locationHouseId, isSubdivision)))
                {
                    if (houseCombo)
                    {
                        for (int houseId = 0; houseId <= 30; houseId++)
                        {
                            if (ImGui.Selectable(HouseIdToString(houseId, isSubdivision), _locationHouseId == houseId))
                            {
                                _locationHouseId = houseId;
                            }
                        }
                    }
                }

                DrawIntProperty("Room"u8, ref _locationRoomId, IsEditingState);

                ImGui.SameLine();
                ImGuiComponents.HelpMarker("The apartment number/personal chamber number, or 0 for a house's main room or apartment lobby.");
            }
        }

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

        // Strip irrelevant parts of the location depending on what kind of housing the territory is
        bool inHousingIndoor = false;
        bool inHousingOutdoor = false;
        if (_locationTerritoryId >= 0 && _dataManager.Excel.GetSheet<TerritoryType>().TryGetRow((uint)_locationTerritoryId, out var currentTerritoryRow))
        {
            inHousingIndoor = currentTerritoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingIndoor && !WorkshopTerritoryTypes.Contains((uint)_locationTerritoryId);
            inHousingOutdoor = inHousingIndoor || currentTerritoryRow.TerritoryIntendedUse.RowId == TerritoryUseHousingOutdoor;
        }
        if (!inHousingIndoor)
        {
            dto.LocationHouseId = -1;
            dto.LocationRoomId = -1;
        }
        if (!inHousingOutdoor)
        {
            dto.LocationWardId = -1;
            dto.LocationDivisionId = -1;
        }

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
