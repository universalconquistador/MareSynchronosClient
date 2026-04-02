using MareSynchronos.API.Dto.Emote;
using Microsoft.AspNetCore.SignalR.Client;


namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task UserEmoteSyncAction(EmoteActionDto dto)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(UserEmoteSyncAction), dto).ConfigureAwait(false);
    }

    public async Task<ServerTimeResponseDto> GetServerTime(ServerTimeRequestDto dto)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<ServerTimeResponseDto>(nameof(GetServerTime), dto).ConfigureAwait(false);
    }
}
