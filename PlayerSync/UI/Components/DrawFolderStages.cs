using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using PlayerSync.PlayerData.Services;
using Stagehand.Definitions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MareSynchronos.UI.Components;

// Does not extend/implement DrawFolderBase/IDrawFolder as this doesn't contain pairs
public class DrawFolderStages
{
    private const string _tagId = "stages";

    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly PairManager _pairManager;
    private readonly IStageDisplayService _stageDisplayService;

    private bool _wasHovered;
    private IActiveStage? _hoveredStage = null;

    public DrawFolderStages(TagHandler tagHandler, UiSharedService uiSharedService, MareMediator mareMediator, ApiController apiController, IdDisplayHandler idDisplayHandler, PairManager pairManager, IStageDisplayService stageDisplayService)
    {
        _tagHandler = tagHandler;
        _uiSharedService = uiSharedService;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _idDisplayHandler = idDisplayHandler;
        _pairManager = pairManager;
        _stageDisplayService = stageDisplayService;
    }

    public void Draw()
    {
        var stages = _stageDisplayService.GetActiveStages();
        using (ImRaii.PushId("stages"))
        {
            using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered))
            using (ImRaii.Child("stages_folder", new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
            {
                var expanderIcon = _tagHandler.IsTagOpen(_tagId) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

                ImGui.AlignTextToFramePadding();

                _uiSharedService.IconText(expanderIcon);
                if (ImGui.IsItemClicked())
                {
                    _tagHandler.SetTagOpen(_tagId, !_tagHandler.IsTagOpen(_tagId));
                }

                ImGui.SameLine();
                _uiSharedService.IconText(FontAwesomeIcon.MapMarkedAlt);

                ImGui.SameLine();
                ImGui.TextUnformatted($"[{stages.Count(activeStage => !activeStage.IsHidden)}] Visible Stages");
            }
            _wasHovered = ImGui.IsItemHovered();

            ImGui.Separator();

            if (_tagHandler.IsTagOpen(_tagId))
            {
                using (ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false))
                {
                    if (stages.Count > 0)
                    {
                        foreach (var stage in stages)
                        {
                            DrawStage(stage);
                        }
                    }
                    else
                    {
                        ImGui.TextWrapped("No stages visible.");
                    }

                    ImGui.Separator();
                }
            }
        }
    }

    private void DrawStage(IActiveStage activeStage)
    {
        bool ownedByGroup = activeStage.StageFullInfo.Info.GroupOwnerGID != "";
        string ownerDisplayName;
        if (ownedByGroup)
        {
            var ownerGroup = _pairManager.Groups.FirstOrDefault(pair => pair.Key.GID == activeStage.StageFullInfo.Info.GroupOwnerGID).Value;
            if (ownerGroup != null)
            {
                ownerDisplayName = _idDisplayHandler.GetGroupText(ownerGroup).text;
            }
            else
            {
                ownerDisplayName = activeStage.StageFullInfo.Info.GroupOwnerGID;
            }
        }
        else
        {
            if (activeStage.StageFullInfo.Info.UserOwnerUID == _apiController.UID)
            {
                ownerDisplayName = _apiController.DisplayName;
            }
            else
            {
                var ownerPair = _pairManager.GetPairByUID(activeStage.StageFullInfo.Info.UserOwnerUID);
                if (ownerPair != null)
                {
                    ownerDisplayName = _idDisplayHandler.GetPlayerText(ownerPair).text;
                }
                else
                {
                    ownerDisplayName = activeStage.StageFullInfo.Info.UserOwnerUID;
                }
            }
        }

        using (ImRaii.PushId(activeStage.StageFullInfo.SID))
        using (ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _hoveredStage == activeStage))
        using (ImRaii.Child("###hover", new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            //
            // Left side
            //
            ImGui.AlignTextToFramePadding();
            unsafe
            {
                if (activeStage.IsHidden)
                {
                    _uiSharedService.IconText(FontAwesomeIcon.EyeSlash);
                }
                else
                {
                    _uiSharedService.IconText(FontAwesomeIcon.MapMarkerAlt, activeStage.State == ActiveStageState.Loading ? (*ImGui.GetStyleColorVec4(ImGuiCol.Text)) with { W = 0.5f } : null);
                }
            }
            UiSharedService.AttachToolTip(activeStage.State.ToString());

            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();

            //
            // Right side
            //
            var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
            var spacingX = ImGui.GetStyle().ItemSpacing.X;
            var currentRightSide = windowEndX;

            var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
            currentRightSide -= barButtonSize.X;
            ImGui.SameLine(currentRightSide);
            ImGui.AlignTextToFramePadding();
            if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
            {
                ImGui.OpenPopup("Stage Context Menu");
            }
            currentRightSide -= spacingX;

            var showHideIcon = activeStage.IsHidden ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
            var pauseButtonSize = _uiSharedService.GetIconButtonSize(showHideIcon);
            currentRightSide -= pauseButtonSize.X;
            ImGui.SameLine(currentRightSide);
            if (ImGuiComponents.IconButton(showHideIcon))
            {
                _stageDisplayService.SetStageHidden(activeStage.StageFullInfo.SID, !activeStage.IsHidden);
            }

            string stageName = string.IsNullOrEmpty(activeStage.StageFullInfo.Customize.DisplayName) ? activeStage.StageFullInfo.SID : activeStage.StageFullInfo.Customize.DisplayName;
            if (ImGui.BeginPopup("Stage Context Menu"))
            {
                using (ImRaii.PushId($"stage-context-{activeStage.StageFullInfo.SID}"))
                {
                    ImGui.TextUnformatted("Common Stage Functions");

                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.InfoCircle, "Stage Details", ImGui.GetContentRegionAvail().X, true))
                    {
                        _mareMediator.Publish(new OpenStageDetailsWindow(activeStage.StageFullInfo, null));
                    }
                    UiSharedService.AttachToolTip("Opens the details for this stage in a new window");

                    FontAwesomeIcon unsubscribeIcon;
                    string subscriptionTooltip;
                    string subscriptionName;
                    if (activeStage.StageFullInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.DirectlySubscribed))
                    {
                        unsubscribeIcon = FontAwesomeIcon.Times;
                        subscriptionTooltip = $"Unsubscribe from {stageName}";
                        subscriptionName = "Unsubscribe";
                        if (activeStage.StageFullInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.OwnerFeedSubscribed))
                        {
                            subscriptionTooltip += $"\nYou will still be subscribed through {(ownedByGroup ? "syncshell" : "user")} {ownerDisplayName}.";
                        }
                    }
                    else
                    {
                        unsubscribeIcon = FontAwesomeIcon.UserSlash;
                        subscriptionTooltip = $"Unsubscribe from {(ownedByGroup ? "syncshell" : "user")} {ownerDisplayName} to no longer see the stage.";
                        subscriptionName = "Unsubscribe from Owner";
                    }
                    using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.LeftCtrl)))
                    {
                        if (_uiSharedService.IconTextButton(unsubscribeIcon, subscriptionName, ImGui.GetContentRegionAvail().X, true))
                        {
                            if (activeStage.StageFullInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.DirectlySubscribed))
                            {
                                _ = _apiController.StageSetSubscribed(activeStage.StageFullInfo.SID, false);
                            }
                            else if (ownedByGroup)
                            {
                                _ = _apiController.StageSetGroupFeedSubscribed(activeStage.StageFullInfo.Info.GroupOwnerGID, false);
                            }
                            else
                            {
                                _ = _apiController.StageSetUserFeedSubscribed(activeStage.StageFullInfo.Info.UserOwnerUID, false);
                            }
                        }
                    }
                    UiSharedService.AttachToolTip(subscriptionTooltip + UiSharedService.TooltipSeparator + "Hold Ctrl to enable.");
                }

                ImGui.EndPopup();
            }

            //
            // Name
            //
            ImGui.SameLine(posX);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(stageName);
            UiSharedService.AttachToolTip($"Stage {activeStage.StageFullInfo.Customize.DisplayName} ({activeStage.StageFullInfo.SID})\nOwner: {ownerDisplayName}");
        }

        if (ImGui.IsItemHovered())
        {
            _hoveredStage = activeStage;
        }
        else if (_hoveredStage == activeStage)
        {
            _hoveredStage = null;
        }
    }
}
