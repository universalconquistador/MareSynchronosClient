using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task GroupBanUser(GroupPairDto dto, string reason)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupBanUser), dto, reason).ConfigureAwait(false);
    }

    public async Task GroupChangeGroupPermissionState(GroupPermissionDto dto)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupChangeGroupPermissionState), dto).ConfigureAwait(false);
    }

    public async Task GroupChangeIndividualPermissionState(GroupPairUserPermissionDto dto)
    {
        CheckConnection();
        await SetBulkPermissions(new(new(StringComparer.Ordinal),
            new(StringComparer.Ordinal) {
                { dto.Group.GID, dto.GroupPairPermissions }
            })).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupChangeOwnership), groupPair).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(GroupPasswordDto groupPassword)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupChangePassword), groupPassword).ConfigureAwait(false);
    }

    public async Task GroupSetDescription(GroupDto group, string newDescription)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(GroupSetDescription), group, newDescription).ConfigureAwait(false);
    }

    public async Task GroupClear(GroupDto group, bool clearOnlyGuestUsers)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupClear), group, clearOnlyGuestUsers).ConfigureAwait(false);
    }

    public async Task<GroupJoinDto> GroupCreate()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<GroupJoinDto>(nameof(GroupCreate)).ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(GroupDto group, int amount)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<string>>(nameof(GroupCreateTempInvite), group, amount).ConfigureAwait(false);
    }

    public async Task GroupDelete(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupDelete), group).ConfigureAwait(false);
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<BannedGroupUserDto>>(nameof(GroupGetBannedUsers), group).ConfigureAwait(false);
    }

    public async Task<GroupJoinInfoDto> GroupJoin(GroupPasswordDto passwordedGroup)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<GroupJoinInfoDto>(nameof(GroupJoin), passwordedGroup).ConfigureAwait(false);
    }

    public async Task<bool> GroupJoinFinalize(GroupJoinDto passwordedGroup)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupJoinFinalize), passwordedGroup).ConfigureAwait(false);
    }

    public async Task GroupZoneJoin(GroupZoneJoinDto groupZoneJoin)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupZoneJoin), groupZoneJoin).ConfigureAwait(false);
    }

    public async Task GroupLeave(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupLeave), group).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupRemoveUser), groupPair).ConfigureAwait(false);
    }

    public async Task GroupSetUserInfo(GroupPairUserInfoDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupSetUserInfo), groupPair).ConfigureAwait(false);
    }

    public async Task<int> GroupPrune(GroupDto group, int days, bool execute)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<int>(nameof(GroupPrune), group, days, execute).ConfigureAwait(false);
    }

    public async Task<List<GroupFullInfoDto>> GroupsGetAll()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<GroupFullInfoDto>>(nameof(GroupsGetAll)).ConfigureAwait(false);
    }

    public async Task GroupUnbanUser(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupUnbanUser), groupPair).ConfigureAwait(false);
    }

    public async Task BroadcastStartListening(string ident)
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(BroadcastStartListening), ident).ConfigureAwait(false);
    }

    public async Task BroadcastStopListening()
    {
        CheckConnection();
        await _mareHub!.InvokeAsync(nameof(BroadcastStopListening)).ConfigureAwait(false);
    }

    public async Task<List<GroupBroadcastDto>> BroadcastSendReceive(WorldData location, List<string> visibleIdents, BroadcastSendDto sendDto)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<GroupBroadcastDto>>(nameof(BroadcastSendReceive), location, visibleIdents, sendDto).ConfigureAwait(false);
    }

    public async Task<List<GroupBroadcastDto>> BroadcastReceive(WorldData location)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<GroupBroadcastDto>>(nameof(BroadcastReceive), location).ConfigureAwait(false);
    }

    private void CheckConnection()
    {
        if (ServerState is not (ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting)) throw new InvalidDataException("Not connected");
    }
}