using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs
{
    public class PairRequestManager : DisposableMediatorSubscriberBase
    {
        private readonly IContextMenu _dalamudContextMenu;
        private readonly MareConfigService _configurationService;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly PairManager _pairManager;
        private readonly ApiController _apiController;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private List<UserPairRequestFullDto> _pendingPairRequests = new();

        public PairRequestManager(ILogger<PairRequestManager> logger, MareMediator mediator, IContextMenu dalamudContextMenu,
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
        {
            _dalamudContextMenu = dalamudContextMenu;
            _dalamudUtilService = dalamudUtilService;
            _configurationService = mareConfigService;
            _pairManager = pairManager;
            _apiController = apiController;
            _serverConfigurationManager = serverConfigurationManager;

            Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPendingPairRequests());
            Mediator.Subscribe<PairRequestsUpdate>(this, (msg) => UpdatePairRequests(msg.Dto));

            _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

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

        public List<UserPairRequestFullDto> GetReceivedPendingRequests()
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

                if (_configurationService.Current.ShowPairingRequestNotification)
                {
                    var msg = name != "Unknown" ? $"Player {name} ({req.Requestor.AliasOrUID}) " : $"UID/Alias {req.Requestor.AliasOrUID} ";
                    Mediator.Publish(new NotificationMessage("New Pair Request", msg + "has sent you a request to pair directly.", MareConfiguration.Models.NotificationType.Info));
                }
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
            if (args.MenuType == ContextMenuType.Inventory) return;
            if (!_configurationService.Current.EnableRightClickMenus) return;

            var target = _dalamudUtilService.TargetAddress;

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
        }
    }
}
