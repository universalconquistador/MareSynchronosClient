using System;
using System.Collections.Generic;
using System.Text;
using MareSynchronos.API.Dto.Stage;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task<StageFullInfoDto> StageCreate(string stageFileHash, List<StageModUsageDto> mods, string ownerGroupId, StageCustomizeDto customization, StageStateDto state)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<StageFullInfoDto>(nameof(StageCreate), stageFileHash, mods, ownerGroupId, customization, state).ConfigureAwait(false);
    }

    public async Task<StageFullInfoDto> StageGetInfo(string stageId)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<StageFullInfoDto>(nameof(StageGetInfo), stageId).ConfigureAwait(false);
    }

    public async Task<(List<StageFullInfoDto> Stages, bool HasMore)> StageListForUser(string userId, int page)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<(List<StageFullInfoDto> Stages, bool HasMore)>(nameof(StageListForUser), userId, page).ConfigureAwait(false);
    }

    public async Task<(List<StageFullInfoDto> Stages, bool HasMore)> StageListForGroup(string groupId, int page)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<(List<StageFullInfoDto> Stages, bool HasMore)>(nameof(StageListForGroup), groupId, page).ConfigureAwait(false);
    }

    public async Task<(List<StageFullInfoDto> Stages, bool HasMore)> StageListSubscribed(int page)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<(List<StageFullInfoDto> Stages, bool HasMore)>(nameof(StageListSubscribed), page).ConfigureAwait(false);
    }

    public async Task<(List<string> UserIds, List<string> GroupIds)> StageGetSubscribedFeeds()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<(List<string> UserIds, List<string> GroupIds)>(nameof(StageGetSubscribedFeeds)).ConfigureAwait(false);
    }

    public async Task StageDelete(string stageId)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageDelete), stageId).ConfigureAwait(false);
    }

    public async Task<StageContentsDto> StageOverwriteContents(string stageId, string newStageFileHash, List<StageModUsageDto> newMods)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<StageContentsDto>(nameof(StageOverwriteContents), stageId, newStageFileHash, newMods).ConfigureAwait(false);
    }

    public async Task StageUpdateCustomize(string stageId, StageCustomizeDto newCustomization)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageUpdateCustomize), stageId, newCustomization).ConfigureAwait(false);
    }

    public async Task StageUpdateState(string stageId, StageStateDto newState)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageUpdateState), stageId, newState).ConfigureAwait(false);
    }

    public async Task StageSetSubscribed(string stageId, bool subscribed)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageSetSubscribed), stageId, subscribed).ConfigureAwait(false);
    }

    public async Task StageSetUserFeedSubscribed(string userId, bool subscribed)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageSetUserFeedSubscribed), userId, subscribed).ConfigureAwait(false);
    }

    public async Task StageSetGroupFeedSubscribed(string groupId, bool subscribed)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(StageSetGroupFeedSubscribed), groupId, subscribed).ConfigureAwait(false);
    }

    public async Task<StageFullInfoDto[]> StageGetSubscribedForLocation(int worldId, int territoryId, int wardId, int divisionId, int houseId, int roomId)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<StageFullInfoDto[]>(nameof(StageGetSubscribedForLocation), worldId, territoryId, wardId, divisionId, houseId, roomId).ConfigureAwait(false);
    }
}
