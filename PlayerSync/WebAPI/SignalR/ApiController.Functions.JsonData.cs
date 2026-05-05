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

    public async Task SendLifestreamInviteToPair(Pair pair, AddressBookEntry? entry = null)
    {
        LifestreamParseableAddress address;

        if (entry is null)
        {
            var lifeStreamPlotInfo = _ipcManager.Lifestream.GetCurrentPlotInfo();

            if (lifeStreamPlotInfo != null)
            {

                Logger.LogTrace("Lifestream info: {ward} {plot}", lifeStreamPlotInfo.Value.Ward, lifeStreamPlotInfo.Value.Plot);

                var ownLocation = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);

                ownLocation.WardId = (uint)lifeStreamPlotInfo.Value.Ward;
                ownLocation.HouseId = (uint)lifeStreamPlotInfo.Value.Plot;

                AddressBookEntry lifestreamAddress = (
                    Name: $"{_dalamudUtil.PlayerName}'s Location",
                    World: (int)ownLocation.ServerId,
                    City: (int)lifeStreamPlotInfo.Value.Kind,
                    Ward: (int)ownLocation.WardId,
                    PropertyType: ownLocation.HouseId == 100 ? 1 : 0,
                    Plot: (int)ownLocation.HouseId,
                    Apartment: (int)ownLocation.RoomId,
                    ApartmentSubdivision: ownLocation.DivisionId != 0,
                    AliasEnabled: true,
                    Alias: "PSIPC"
                );

                address = new(lifestreamAddress);
            }
            else
            {
                Mediator.Publish(new NotificationMessage("Lifestream Invite", "Using current location for Lifestream invite requires you to be at a housing plot or an apartment.",
                    MareConfiguration.Models.NotificationType.Error));

                return;
            }
        }
        else
        {
            var lifestreamAddress = entry ?? new AddressBookEntry();
            address = new(lifestreamAddress);
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
