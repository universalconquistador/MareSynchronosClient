using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs
{
    public class PairInviteManager : DisposableMediatorSubscriberBase, IHostedService
    {
        private readonly IContextMenu _dalamudContextMenu;
        private readonly MareConfigService _configurationService;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly PairManager _pairManager;
        private readonly ApiController _apiController;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private List<UserPairRequestFullDto> _pendingPairRequests = new();
        private List<GroupJoinInviteDto> _pendingGroupInvites = new();

        public PairInviteManager(ILogger<PairInviteManager> logger, MareMediator mediator, IContextMenu dalamudContextMenu,
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
        {
            _dalamudContextMenu = dalamudContextMenu;
            _dalamudUtilService = dalamudUtilService;
            _configurationService = mareConfigService;
            _pairManager = pairManager;
            _apiController = apiController;
            _serverConfigurationManager = serverConfigurationManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Pair Request Manger started.");
            Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPendingPairRequests());
            Mediator.Subscribe<PairRequestsUpdateMessage>(this, (msg) => UpdatePairRequests(msg.Dto));
            Mediator.Subscribe<UpdateGroupInvitesMessage>(this, (msg) => UpdateGroupInvites(msg.Dto));

            _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Pair Request Manger stopped.");
            _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

            return Task.CompletedTask;
        }

        private string SelfUID => _apiController.UID;

        public int ReceivedPendingCount
        {
            get
            {
                var tempPairRequestList = _pendingPairRequests;
                return tempPairRequestList.Count;
            }
        }

        public int ReceivedGroupInviteCount
        {
            get
            {
                var tempPendingGroupInvites = _pendingGroupInvites;
                return tempPendingGroupInvites.Count;
            }
        }

        public List<UserPairRequestFullDto> GetPendingRequests()
        {
            var tempPairRequestList = _pendingPairRequests;
            return tempPairRequestList.ToList();
        }

        public void SendPairRequest(string targetIdent)
        {
            Logger.LogDebug("Sending pair request for {ident}", targetIdent);
            _ = SendPairRequestInternal(targetIdent: targetIdent);
        }

        public void SendPairRequest(UserData userData)
        {
            Logger.LogDebug("Sending pair request for {user}", userData.UID);
            _ = SendPairRequestInternal(userData: userData);
        }

        public void SendPairRejection(string targetIdent)
        {
            Logger.LogDebug("Rejecting pair request for {user}", targetIdent);
            _ = SendPairRejectionInternal(targetIdent: targetIdent);
        }

        public void SendPairRejection(UserData userData)
        {
            Logger.LogDebug("Rejecting pair request for {user}", userData.UID);
            _ = SendPairRejectionInternal(userData: userData);
        }

        private void UpdatePairRequests(UserPairRequestsDto pairRequest)
        {
            var incomingPairRequests = pairRequest?.PairingRequests ?? [];
            var existingRequestorUids = new HashSet<string>(_pendingPairRequests.Select(r => r.Requestor.UID), StringComparer.Ordinal);
            var newRequests = incomingPairRequests.Where(r => existingRequestorUids.Add(r.Requestor.UID)).ToList();

            // handle new requests
            foreach (var req in newRequests)
            {
                if (_serverConfigurationManager.IsUidBlacklistedForPairRequest(req.Requestor.UID))
                {
                    _ = SendPairRejectionInternal(userData: req.Requestor);
                    incomingPairRequests.Remove(req);
                    continue;
                }

                var name = "";
                name = _dalamudUtilService.FindPlayerByNameHash(req.RequestorIdent).Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var pair = _pairManager.GetPairByUID(req.Requestor.UID);
                    if (pair != null)
                    {
                        name = pair.PlayerName; // this may be empty string
                    }
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "Unknown";
                }

                _serverConfigurationManager.AddPendingRequestForIdent(req.RequestorIdent, name);

                var msg = name != "Unknown" ? $"Player {name} ({req.Requestor.AliasOrUID}) " : $"UID/Alias {req.Requestor.AliasOrUID} ";
                Mediator.Publish(new NotificationMessage("New Pair Request", msg + "has sent you a request to pair directly.",
                    MareConfiguration.Models.NotificationType.Invite));
            }

            // clean up the local ident cache
            var pendingIdents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in incomingPairRequests)
            {
                if (!string.IsNullOrWhiteSpace(request.RequestorIdent))
                    pendingIdents.Add(request.RequestorIdent);

                if (!string.IsNullOrWhiteSpace(request.RequestTargetIdent))
                    pendingIdents.Add(request.RequestTargetIdent);
            }

            var knownIdents = _serverConfigurationManager.GetAllPendingPairRequestIdent();
            foreach (var ident in knownIdents)
            {
                if (!pendingIdents.Contains(ident))
                {
                    _serverConfigurationManager.RemovePendingRequestForIdent(ident);
                }
            }

            _pendingPairRequests = incomingPairRequests.ToList();
        }

        private unsafe void DalamudContextMenuOnOnOpenGameObjectContextMenu(IMenuOpenedArgs args)
        {
            // make sure we're allowed to add a menu item
            if (!_configurationService.Current.EnableRightClickMenus) return;
            if (args.MenuType == ContextMenuType.Inventory) return;
            if (args.Target is not MenuTargetDefault) return;
            if (args.AddonName != null) return; // This should prevent most game windows from registering

            var target = _dalamudUtilService.TargetAddress;
            if (target == nint.Zero) return;

            // don't add menu to self
            if (_dalamudUtilService.GetPlayerPtr() == target) return;

            // ensure it's a player
            var playerCharacter = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target);
            var isPlayerCharacter = playerCharacter->GetObjectKind() == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc;
            if (!isPlayerCharacter) return;

            // check that we're not already directly paired
            var existingPair = _pairManager.DirectPairs.SingleOrDefault(p => p.Address == target);
            if (existingPair != null && existingPair.UserPair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional) return;

            var targetIdent = DalamudUtilService.GetHashedCIDFromPlayerPointer(target);
            if (targetIdent == null) return;

            try
            {
                AddContextMenu(args, targetIdent);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Something went wrong adding context menu.");
            }
        }

        private void AddContextMenu(IMenuOpenedArgs args, string targetIdent)
        {
            if (!_apiController.IsConnected) return;

            SeStringBuilder seStringBuilder = new();
            var pairIndividually = seStringBuilder.AddText("Send Pair Request").Build();

            args.AddMenuItem(new MenuItem()
            {
                Name = pairIndividually,
                OnClicked = (a) => _ = SendPairRequestInternal(targetIdent),
                UseDefaultPrefix = false,
                PrefixChar = 'P',
                PrefixColor = 530
            });
        }

        private async Task SendPairRequestInternal(string? targetIdent = null, UserData? userData = null)
        {
            await _apiController.UserMakePairRequest(new(RequestTargetIdent: targetIdent, UserData: userData)).ConfigureAwait(false);
        }

        private async Task SendPairRejectionInternal(string? targetIdent = null, UserData? userData = null)
        {
            await _apiController.UserRejectPairRequest(new(RequestTargetIdent: targetIdent, UserData: userData)).ConfigureAwait(false);
        }

        private void ClearPendingPairRequests()
        {
            _pendingPairRequests = new List<UserPairRequestFullDto>();
            _pendingGroupInvites = new List<GroupJoinInviteDto>();
        }

        // groups/syncshells
        public void SendGroupInvite(GroupPairDto dto)
        {
            Logger.LogDebug("Sending group invite for {user} to {group}", dto.UID, dto.Group.GID);
            _ = SendGroupInviteInternal(dto);
        }

        public void SendRejectGroupInvite(string inviteId)
        {
            var invite = GetGroupJoinInviteDto(inviteId);
            if (invite == null) return;

            GroupJoinInviteDto dto = new(invite.RequestId, invite.Group, new(SelfUID));
            Logger.LogDebug("Rejecting group invite request from {user} to {group} with key {key}", 
                dto.InvitingUser?.UID ?? "Unknown", dto.Group.GID, dto.RequestId);

            _ = SendRejectGroupInviteInternal(dto);
        }

        public void SendGroupInviteJoin(string inviteId)
        {
            var invite = GetGroupJoinInviteDto(inviteId);
            if (invite == null) return;

            GroupPasswordDto dto = new(invite.Group, invite.RequestId);
            Logger.LogDebug("Sending join for group {group}", dto.Group.GID);

            _ = SendGroupInviteJoinInternal(dto);
        }

        public List<GroupJoinInviteDto> GetPendingGroupInvites()
        {
            var tempGroupInvitesList = _pendingGroupInvites;
            return tempGroupInvitesList.ToList();
        }

        private async Task SendGroupInviteInternal(GroupPairDto dto)
        {
            await _apiController.GroupUserInvite(dto).ConfigureAwait(false);
        }

        private async Task SendRejectGroupInviteInternal(GroupJoinInviteDto dto)
        {
            await _apiController.GroupUserRejectInvite(dto).ConfigureAwait(false);
        }

        private async Task SendGroupInviteJoinInternal(GroupPasswordDto dto)
        {
            var result = await _apiController.GroupJoin(dto).ConfigureAwait(false);
            if (result == null) return;

            Mediator.Publish(new UiToggleMessage(typeof(JoinSyncshellUI)));
            Mediator.Publish(new PreloadJoinSyncshellDtoMessage(result, dto.Password));
        }

        private GroupJoinInviteDto? GetGroupJoinInviteDto(string inviteId)
        {
            return _pendingGroupInvites.FirstOrDefault(i => string.Equals(i.RequestId, inviteId, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateGroupInvites(GroupJoinInvitesDto dto)
        {
            var incomingGroupInvites = dto?.GroupJoinInvites ?? [];
            var existingGroupIds = new HashSet<string>(_pendingGroupInvites.Select(invite => invite.GID), StringComparer.OrdinalIgnoreCase);
            var newInvites = incomingGroupInvites.Where(invite => existingGroupIds.Add(invite.GID)).ToList();

            // handle new requests
            foreach (var inv in newInvites)
            {
                if (_serverConfigurationManager.IsUidBlacklistedForPairRequest(inv.InvitingUser!.UID))
                {
                    SendRejectGroupInvite(inv.RequestId);
                    incomingGroupInvites.Remove(inv);
                    continue;
                }

                // we aren't really going to worry about someone inviting someone while they are offline
                // if it becomes an issue later on, we'll deal with it
                var thisPair = _pairManager.GetPairByUID(inv.InvitingUser!.UID);
                string name = thisPair?.PlayerName ?? "Unknown";

                var msg = name != "Unknown" ? $"Player {name} ({inv.InvitingUser.AliasOrUID}) " : $"UID/Alias {inv.InvitingUser.AliasOrUID} ";
                var alias = string.IsNullOrWhiteSpace(inv.GroupAlias) ? "." : $" ({inv.GroupAlias}).";
                Mediator.Publish(new NotificationMessage("Syncshell Invite", msg + "has sent you an invite to join Syncshell " +
                    $"{inv.GID}" + alias, MareConfiguration.Models.NotificationType.Invite));
            }

            _pendingGroupInvites = incomingGroupInvites.ToList();
        }
    }
}
