using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.API.Data;

namespace MareSynchronos.PlayerData.Pairs
{
    public class PairRequestManager : DisposableMediatorSubscriberBase
    {
        private readonly IContextMenu _dalamudContextMenu;
        private readonly MareConfigService _configurationService;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly PairManager _pairManager;
        private readonly ApiController _apiController;
        private List<UserPairRequestFullDto> _pendingPairRequests = new();

        public PairRequestManager(ILogger<PairRequestManager> logger, MareMediator mediator, IContextMenu dalamudContextMenu,
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController) : base(logger, mediator)
        {
            _dalamudContextMenu = dalamudContextMenu;
            _dalamudUtilService = dalamudUtilService;
            _configurationService = mareConfigService;
            _pairManager = pairManager;
            _apiController = apiController;

            Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPendingPairRequests());

            _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

        }

        private string SelfUID => _apiController.UID;

        public List<UserPairRequestFullDto> GetReceivedPendingRequests()
        {
            return _pendingPairRequests.Where(p => p.RequestTarget.UID == SelfUID).ToList();
        }

        public List<UserPairRequestFullDto> GetSentPendingRequests()
        {
            return _pendingPairRequests.Where(p => p.Requestor.UID == SelfUID).ToList();
        }

        public void AcceptPendingPairRequest(string targetIdent)
        {
            Logger.LogDebug("Accepting pair request for {ident}", targetIdent);
            _ = SendPairRequest(targetIdent);
        }

        public void RejectPendingPairRequest(UserData userData)
        {
            Logger.LogDebug("Rejecting pair request for {user}", userData.UID);
            _ = SendPairRejection(userData);
        }

        public void UpdatePairRequests(UserPairRequestsDto pairRequest)
        {
            //
        }

        private unsafe void DalamudContextMenuOnOnOpenGameObjectContextMenu(IMenuOpenedArgs args)
        {
            // make sure we're allowed to add a menu item
            if (args.MenuType == ContextMenuType.Inventory) return;
            if (!_configurationService.Current.EnableRightClickMenus) return;

            // ensure it's a player
            var target = _dalamudUtilService.TargetAddress;
            var playerCharacter = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target);
            var isPlayerCharacter = playerCharacter->GetObjectKind() == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc;
            if (!isPlayerCharacter) return;

            // check that we're not already directly paired
            var isUserPaired = _pairManager.DirectPairs.Exists(p => p.Address == target);
            if (isUserPaired) return;

            var targetName = ((BattleChara*)playerCharacter)->NameString;
            Logger.LogDebug("Target ident name is {name}", targetName);

            var targetIdent = ((BattleChara*)playerCharacter)->Character.ContentId.ToString().GetHash256();
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
                OnClicked = (a) => _ = SendPairRequest(targetIdent),
                UseDefaultPrefix = false,
                PrefixChar = 'P',
                PrefixColor = 530
            });
        }

        private async Task SendPairRequest(string targetIdent)
        {
            Logger.LogDebug("Got here when clicking {id}", targetIdent);
        }

        private async Task SendPairRejection(UserData userData)
        {
            await _apiController.UserRejectPairRequest(userData).ConfigureAwait(false);
        }

        private void ClearPendingPairRequests()
        {
            _pendingPairRequests.Clear();
        }
    }
}
