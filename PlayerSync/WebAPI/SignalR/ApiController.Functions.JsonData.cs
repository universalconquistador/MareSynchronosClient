using MareSynchronos.API.Data.AdditionalTypes;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;


namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task<JsonDataResponseDto> UserSendJsonData(JsonDataTypeDto dto)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<JsonDataResponseDto>(nameof(UserSendJsonData), dto).ConfigureAwait(false);
    }

    public async Task SendLifestreamInviteToPair(Pair pair)
    {
        bool validAddress = false;
        var ownLocation = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);
        var worldName = _dalamudUtil.WorldData.Value.First(w => w.Key == ownLocation.ServerId);
        var territory = _dalamudUtil.TerritoryData.Value.First(t => t.Key == ownLocation.TerritoryId);

        string? world = API.Data.GameData.Worlds.FirstOrDefault(w => worldName.Value.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (world == null)
        {
            Logger.LogWarning("Got invalid world name for Lifestream invite: {name}", world);
            return;
        }

        string? residential = API.Data.GameData.HousingDistricts.FirstOrDefault(r => territory.Value.Contains(r, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        if (residential != null && ownLocation.WardId > 0 && ownLocation.HouseId != 0)
            validAddress = true;

        LifestreamParseableAddress address;
        if (validAddress)
        {
            var ward = ownLocation.WardId.ToString();
            var plot = (ownLocation.HouseId + 1).ToString();
            address = new(world, residential, ward, plot);
        }
        else
        {
            address = new(world, null, null, null);
            Mediator.Publish(new NotificationMessage("Lifestream Invite", "Lifestream invites outside of a house will only invite to the same World.",
                MareConfiguration.Models.NotificationType.Info));
        }

        var jsonData = System.Text.Json.JsonSerializer.Serialize(address);

        var response = await UserSendJsonData(new(pair.UserData, JsonDataType.LifestreamLocationInvite, jsonData)).ConfigureAwait(false);
        Logger.LogDebug("Lifestream Invite {1} {2}", response.WasSuccessful, response.ResponseMessage);
        if (!response.WasSuccessful && response.ResponseMessage != null)
        {
            Mediator.Publish(new NotificationMessage("Lifestream Invite", response.ResponseMessage, MareConfiguration.Models.NotificationType.Error));
        }
    }
}
