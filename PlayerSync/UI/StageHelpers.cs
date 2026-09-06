using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MareSynchronos.UI;

public static class StageHelpers
{
    public static void DrawStagePairButton(StageFullInfoDto stageInfo, ApiController apiController, ILogger logger)
    {
        static async Task SetIsSubscribedAsync(StageFullInfoDto stageInfo, bool subscribed, ApiController apiController, ILogger logger)
        {
            try
            {
                await apiController.StageSetSubscribed(stageInfo.SID, subscribed).ConfigureAwait(false);
                stageInfo.SubscriptionState = subscribed ? stageInfo.SubscriptionState | StageSubscriptionFlags.DirectlySubscribed : stageInfo.SubscriptionState & ~StageSubscriptionFlags.DirectlySubscribed;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to set subscription state for stage {sid} to {value}!", stageInfo.SID, subscribed);
            }
        }

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
            _ = SetIsSubscribedAsync(stageInfo, !isSubscribed, apiController, logger);
        }
        UiSharedService.AttachToolTip(subscriptionTooltip);
    }
}
