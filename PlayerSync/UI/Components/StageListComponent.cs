using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MareSynchronos.UI.Components;

public class StageListComponent
{
    private readonly ILogger _logger;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly Func<int, Task<(List<StageFullInfoDto>, bool)>> _pageLoadCallback;

    private bool _isLoading = false;
    private string? _errorMessage = null;

    private List<StageFullInfoDto> _pageResults;
    public int PageIndex { get; private set; }
    private bool _hasNextPage;

    public StageListComponent(ILogger logger, MareMediator mareMediator, ApiController apiController, PairManager pairManager, IdDisplayHandler idDisplayHandler, Func<int, Task<(List<StageFullInfoDto>, bool)>> pageLoadCallback)
    {
        _logger = logger;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _pairManager = pairManager;
        _idDisplayHandler = idDisplayHandler;
        _pageLoadCallback = pageLoadCallback;

        PageIndex = 0;
        _hasNextPage = false;
        _pageResults = new();

        if (_apiController.IsConnected)
        {
            LoadPage(0);
        }
    }

    public void Draw()
    {
        if (_errorMessage != null)
        {
            ImGui.TextColoredWrapped(ImGuiColors.ErrorForeground, _errorMessage);
        }

        if (_isLoading && _pageResults.Count == 0)
        {
            ImGui.TextDisabled("Loading..."u8);
        }

        if (_pageResults.Count > 0)
        {
            for (int i = 0; i < _pageResults.Count; i++)
            {
                using (ImRaii.PushId($"ListItem{i}"))
                {
                    DrawStageListItem(_pageResults[i]);
                }
            }

            if (PageIndex > 0 || _hasNextPage)
            {
                using (ImRaii.Disabled(PageIndex == 0 || _isLoading))
                {
                    if (ImGui.Button("Previous Page"u8))
                    {
                        LoadPage(PageIndex - 1);
                    }
                }
            }

            if (_hasNextPage)
            {
                ImGui.SameLine();

                using (ImRaii.Disabled(_isLoading))
                {
                    if (ImGui.Button("Next Page"u8))
                    {
                        LoadPage(PageIndex + 1);
                    }
                }
            }
        }
    }

    private void DrawStageListItem(StageFullInfoDto stageInfo)
    {
        var startX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() * 2 - ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.InfoCircle, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            _mareMediator.Publish(new OpenStageDetailsWindow(stageInfo, stageInfo.Info.GroupOwnerGID == "" ? null : stageInfo.Info.GroupOwnerGID));
        }
        UiSharedService.AttachToolTip("Stage Details");

        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        FontAwesomeIcon subscriptionIcon;
        string subscriptionTooltip;
        if (stageInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.DirectlySubscribed))
        {
            subscriptionIcon = FontAwesomeIcon.Check;
            subscriptionTooltip = "Subscribed to this stage." + UiSharedService.TooltipSeparator + "Click to unsubscribe from this stage.";
        }
        else if (stageInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.OwnerFeedSubscribed))
        {
            subscriptionIcon = FontAwesomeIcon.ArrowUp;
            subscriptionTooltip = $"Subscribed to this stage's owner." + UiSharedService.TooltipSeparator + "Click to subscribe to this stage directly.";
        }
        else
        {
            subscriptionIcon = FontAwesomeIcon.Plus;
            subscriptionTooltip = "Subscribe to this stage." + UiSharedService.TooltipSeparator + "Click to subscribe to this stage.";
        }
        if (ImGuiComponents.IconButton(subscriptionIcon, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            bool isSubscribed = stageInfo.SubscriptionState.HasFlag(StageSubscriptionFlags.DirectlySubscribed);
            _ = SetIsSubscribedAsync(stageInfo, !isSubscribed);
        }
        UiSharedService.AttachToolTip(subscriptionTooltip);

        ImGui.SameLine();
        ImGui.SetCursorPosX(startX);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.MapMarkerAlt.ToIconString());
        }
        ImGui.SameLine();
        using (ImRaii.TextWrapPos(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() * 2 - ImGui.GetStyle().ItemInnerSpacing.X * 2))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextWrapped(string.IsNullOrEmpty(stageInfo.Customize.DisplayName) ? stageInfo.SID : stageInfo.Customize.DisplayName);
            ImGui.SameLine();
            using (ImRaii.Disabled())
            {
                ImGui.TextWrapped(stageInfo.Customize.Version);
            }
        }

        bool ownedByGroup = stageInfo.Info.GroupOwnerGID != "";
        string ownerDisplayName;
        if (ownedByGroup)
        {
            ownerDisplayName = _idDisplayHandler.GetGroupAlias(stageInfo.Info.GroupOwnerGID, _pairManager);
        }
        else
        {
            ownerDisplayName = _idDisplayHandler.GetUserAlias(stageInfo.Info.UserOwnerUID, _apiController, _pairManager);
        }
        ImGui.TextDisabled(ownerDisplayName);
        ImGui.SameLine();
        ImGui.TextDisabled($"(updated {stageInfo.Contents.RevisionDateUtc.ToLocalTime().ToString("g")})");

        ImGui.Spacing();
        ImGui.Separator();
    }

    private async Task SetIsSubscribedAsync(StageFullInfoDto stageInfo, bool subscribed)
    {
        try
        {
            await _apiController.StageSetSubscribed(stageInfo.SID, subscribed).ConfigureAwait(false);
            stageInfo.SubscriptionState = subscribed ? stageInfo.SubscriptionState | StageSubscriptionFlags.DirectlySubscribed : stageInfo.SubscriptionState & ~StageSubscriptionFlags.DirectlySubscribed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set subscription state for stage {sid} to {value}!", stageInfo.SID, subscribed);
        }
    }

    public void LoadPage(int page)
    {
        if (!_isLoading)
        {
            _isLoading = true;
            _errorMessage = null;
            _ = LoadDataAsync(page);
        }
    }

    private async Task LoadDataAsync(int page)
    {
        try
        {
            (var results, bool hasMore) = await _pageLoadCallback.Invoke(page).ConfigureAwait(false);

            PageIndex = page;
            _pageResults = results;
            _hasNextPage = hasMore;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch stage list page!");
            _errorMessage = ex.ToString();
        }
        _isLoading = false;
    }
}
