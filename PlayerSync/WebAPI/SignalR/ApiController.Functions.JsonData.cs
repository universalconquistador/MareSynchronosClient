using MareSynchronos.API.Data.AdditionalTypes;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.Interop.Utils;
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
            if (!_dalamudUtil.TryGetCurrentPlotInfo(out var ward, out var plot))
                return;

            var ownLocation = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);

            bool isInResedential = ward >= 0 && plot >= 0;
            bool isInApartment = ward >= 0 && ownLocation.HouseId == 100;

            Logger.LogTrace("Ward {ward} Plot {plot}", ward, plot);

            if (!(isInResedential || isInApartment))
            {
                Mediator.Publish(new NotificationMessage("Lifestream Invite", "Using current location for Lifestream invite requires you to be at a housing plot or an apartment.",
                    MareConfiguration.Models.NotificationType.Error));

                return;
            }

            var kind = _dalamudUtil.GetResidentialAetheryteByTerritoryType(ownLocation.TerritoryId);
            if (kind == null) return;

            lifestreamAddress = (
                Name: $"{_dalamudUtil.PlayerName}'s Location",
                World: (int)ownLocation.ServerId,
                City: (int)kind,
                Ward: ward + 1,
                PropertyType: isInResedential ? 0 : 1,
                Plot: isInResedential ? plot + 1 : 1,
                Apartment: (int)ownLocation.RoomId,
                ApartmentSubdivision: ownLocation.DivisionId - 1 != 0,
                AliasEnabled: true,
                Alias: "PSIPC"
                );
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
