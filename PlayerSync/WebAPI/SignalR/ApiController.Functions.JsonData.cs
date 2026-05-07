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
        AddressBookEntry lifestreamAddress;

        Logger.LogTrace("{service} AddressBookEntry: {entry}", nameof(SendLifestreamInviteToPair), entry);

        if (entry is null)
        {
            var lifeStreamPlotInfo = _ipcManager.Lifestream.GetCurrentPlotInfo();
            var ownLocation = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);

            if (lifeStreamPlotInfo != null && ownLocation.HouseId != 100) // house
            {

                Logger.LogTrace("Lifestream info: {ward} {plot}", lifeStreamPlotInfo.Value.Ward, lifeStreamPlotInfo.Value.Plot);

                ownLocation.WardId = (uint)lifeStreamPlotInfo.Value.Ward;
                ownLocation.HouseId = (uint)lifeStreamPlotInfo.Value.Plot;

                lifestreamAddress = (
                    Name: $"{_dalamudUtil.PlayerName}'s Location",
                    World: (int)ownLocation.ServerId,
                    City: (int)lifeStreamPlotInfo.Value.Kind,
                    Ward: (int)ownLocation.WardId + 1,
                    PropertyType: 0,
                    Plot: (int)ownLocation.HouseId + 1,
                    Apartment: 1,
                    ApartmentSubdivision: false,
                    AliasEnabled: true,
                    Alias: "PSIPC"
                    );
            }
            else if (ownLocation.HouseId == 100) // handle apartments
            {
                var kind = _ipcManager.Lifestream.GetApartmentResidentialAetheryteKindFromTerritoryId((int)ownLocation.TerritoryId);
                if (kind is not null)
                {
                    lifestreamAddress = (
                    Name: $"{_dalamudUtil.PlayerName}'s Location",
                    World: (int)ownLocation.ServerId,
                    City: (int)kind,
                    Ward: (int)ownLocation.WardId,
                    PropertyType: 1,
                    Plot: 1, // 100 but that's out of range for validation checks...
                    Apartment: (int)ownLocation.RoomId,
                    ApartmentSubdivision: ownLocation.DivisionId-1 != 0,
                    AliasEnabled: true,
                    Alias: "PSIPC"
                    );
                }
                else
                {
                    return;
                }
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
            lifestreamAddress = entry.Value;
        }

        address = new(new AddressBookEntryDto(
            lifestreamAddress.Name,
            lifestreamAddress.World,
            lifestreamAddress.City,
            lifestreamAddress.Ward,
            lifestreamAddress.PropertyType,
            lifestreamAddress.Plot,
            lifestreamAddress.Apartment,
            lifestreamAddress.ApartmentSubdivision,
            lifestreamAddress.AliasEnabled,
            lifestreamAddress.Alias
            ));

        Logger.LogTrace("{service} address: {address}", nameof(SendLifestreamInviteToPair), address);

        var jsonData = System.Text.Json.JsonSerializer.Serialize(address);

        Logger.LogTrace("{service} json: {json}", nameof(SendLifestreamInviteToPair), jsonData);

        var response = await UserSendJsonData(new(pair.UserData, JsonDataType.LifestreamLocationInvite, jsonData)).ConfigureAwait(false);
        Logger.LogDebug("Lifestream Invite {1} {2}", response.WasSuccessful, response.ResponseMessage);
        if (!response.WasSuccessful && response.ResponseMessage != null)
        {
            Mediator.Publish(new NotificationMessage("Lifestream Invite", response.ResponseMessage, MareConfiguration.Models.NotificationType.Error));
        }
    }
}
