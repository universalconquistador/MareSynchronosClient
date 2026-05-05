using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace MareSynchronos.PlayerData.Handlers
{
    public enum ContextMenuItemId
    {
        None = 0,
        OpenProfile,
        PauseForever,
        PairData,
        InviteToSyncshell,
        AddToOverrides,
        ReapplyLastData,
        ChangePermissions,
        CyclePauseState,
    }

    public static class ContextMenuSettings
    {
        public static ContextMenuItemId[] Order { get; set; } = new ContextMenuItemId[6]
        {
        ContextMenuItemId.OpenProfile,
        ContextMenuItemId.PauseForever,
        ContextMenuItemId.PairData,
        ContextMenuItemId.InviteToSyncshell,
        ContextMenuItemId.AddToOverrides,
        ContextMenuItemId.None
        };
        public static bool[] SPriority { get; set; } = new bool[6];
    }

    public class PairContextMenuHandler : DisposableMediatorSubscriberBase, IHostedService
    {
        private readonly IContextMenu _dalamudContextMenu;
        private readonly MareConfigService _configurationService;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly PairManager _pairManager;
        private readonly ApiController _apiController;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private readonly PairInviteManager _inviteManager;
        private readonly PlayerPerformanceConfigService _performanceConfigService;
        private readonly IpcManager _ipcManager;

        public PairContextMenuHandler(ILogger<PairContextMenuHandler> logger, MareMediator mediator, IContextMenu dalamudContextMenu,
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController, ServerConfigurationManager serverConfigurationManager,
            PairInviteManager pairInviteManager, PlayerPerformanceConfigService playerPerformanceConfigService,
            IpcManager ipcManager) : base(logger, mediator)
        {
            _dalamudContextMenu = dalamudContextMenu;
            _dalamudUtilService = dalamudUtilService;
            _configurationService = mareConfigService;
            _pairManager = pairManager;
            _apiController = apiController;
            _serverConfigurationManager = serverConfigurationManager;
            _inviteManager = pairInviteManager;
            _performanceConfigService = playerPerformanceConfigService;
            _ipcManager = ipcManager;
        }

        private string SelfUID => _apiController.UID;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Context Menu Handler started.");
            _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Context Menu Handler stopped.");
            _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

            return Task.CompletedTask;
        }

        private void DalamudContextMenuOnOnOpenGameObjectContextMenu(IMenuOpenedArgs args)
        {
            if (args.MenuType == ContextMenuType.Inventory) return;
            if (!_configurationService.Current.EnableRightClickMenus) return;

            Logger.LogTrace("Addon called: {addon}", args.AddonName);

            if (string.Equals(args.AddonName, "ChatLog", StringComparison.OrdinalIgnoreCase))
                AddUserPairChatContextMenu(args);

            var target = _dalamudUtilService.TargetAddress;
            var pairs = _pairManager.GetVisiblePairs();
            var currentTargetPair = pairs.SingleOrDefault(u => u.Address == target);
            if (currentTargetPair != null)
                AddContextMenu(currentTargetPair, args);
        }

        private void AddContextMenu(Pair pair, IMenuOpenedArgs args)
        {
            if (!pair.HasCachedPlayer || (args.Target is not MenuTargetDefault target) || target.TargetObjectId != pair.PlayerCharacterId || pair.IsPaused) return;

            for (int ss = 0; ss < _configurationService.Current.ContextMenuOrder.Length; ss++)
            {
                var itemS = _configurationService.Current.ContextMenuOrder[ss];
                if (itemS == ContextMenuItemId.None) continue;

                int pri = _configurationService.Current.SPriority[ss] ? -1 : 0;
                switch (itemS)
                {
                    case ContextMenuItemId.OpenProfile:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Open Profile").Build(),
                            OnClicked = (a) => Mediator.Publish(new ProfileOpenStandaloneMessage(pair)),
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                            //Priority = -1, // you can move this to the top with -1
                        });
                        break;

                    case ContextMenuItemId.PairData:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Pair Data").Build(),
                            OnClicked = (args) => DrawPairDataContenxtSubmenu(pair, args),
                            IsSubmenu = true,
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;

                    case ContextMenuItemId.InviteToSyncshell:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Invite to Syncshell").Build(),
                            OnClicked = (args) => DrawSyncshellInviteContenxtSubmenu(pair, args),
                            IsSubmenu = true,
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;

                    case ContextMenuItemId.AddToOverrides:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Add to Overrides").Build(),
                            OnClicked = (args) => DrawAddToOverridesContenxtSubmenu(pair, args),
                            IsSubmenu = true,
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;

                    // This kind of acts like a blacklist feature
                    case ContextMenuItemId.PauseForever:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Keep Paused").Build(),
                            OnClicked = (a) => Mediator.Publish(new UserPairStickyPauseAndRemoveMessage(pair.UserData)),
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 17,
                            Priority = pri,
                        });
                        break;

                    case ContextMenuItemId.ReapplyLastData:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Reapply Last Data").Build(),
                            OnClicked = (a) => pair.ApplyLastReceivedData(forced: true),
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;

                    case ContextMenuItemId.ChangePermissions:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Change Permissions").Build(),
                            OnClicked = (a) => Mediator.Publish(new OpenPermissionWindow(pair)),
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;

                    case ContextMenuItemId.CyclePauseState:
                        args.AddMenuItem(new MenuItem()
                        {
                            Name = new SeStringBuilder().AddText("Cycle Pause State").Build(),
                            OnClicked = (a) => Mediator.Publish(new CyclePauseMessage(pair.UserData)),
                            UseDefaultPrefix = false,
                            PrefixChar = 'P',
                            PrefixColor = 530,
                            Priority = pri,
                        });
                        break;
                }
            }
        }

        private void DrawPairDataContenxtSubmenu(Pair pair, IMenuItemClickedArgs clickedArgs)
        {
            var menuItems = new List<MenuItem>();

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Reapply Last Data").Build(),
                OnClicked = (a) => pair.ApplyLastReceivedData(forced: true)
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Change Permissions").Build(),
                OnClicked = (a) => Mediator.Publish(new OpenPermissionWindow(pair))
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Cycle Pause State").Build(),
                OnClicked = (a) => Mediator.Publish(new CyclePauseMessage(pair.UserData)),
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Return").Build(),
                IsReturn = true,
                OnClicked = _ => _dalamudUtilService.OpenContextMenu(clickedArgs.AgentPtr)
            });

            clickedArgs.OpenSubmenu(new SeStringBuilder().AddText("PlayerSync Pair Data").Build(), menuItems);
        }

        private void DrawSyncshellInviteContenxtSubmenu(Pair pair, IMenuItemClickedArgs clickedArgs)
        {
            var menuItems = new List<MenuItem>();

            var maxPairs = _apiController.ServerInfo.MaxGroupUserCount - 1;

            var pairCountsByGroupId = _pairManager.PairsWithGroups
                .SelectMany(pairGroups => pairGroups.Value.Select(group => group.GID))
                .GroupBy(gid => gid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.Count(), StringComparer.OrdinalIgnoreCase);

            var syncshellsOwnerOrMod = _pairManager.Groups
                .Where(group => group.Value.GroupUserInfo.IsModerator() || group.Value.OwnerUID == SelfUID);

            var openSyncshells = _pairManager.Groups
                .Where(group => 
                (group.Value.PublicData.KnownPasswordless || group.Value.GroupPermissions.IsEnableGuestMode()) 
                && !group.Value.GroupPermissions.IsDisableInvites());

            var sharedSyncshellIds = _pairManager.PairsWithGroups
                .Where(pairGroup => pairGroup.Key.UserData.UID == pair.UserData.UID)
                .SelectMany(pairGroup => pairGroup.Value.Select(group => group.GID))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var finalSyncshells = syncshellsOwnerOrMod
                .Concat(openSyncshells)
                .DistinctBy(group => group.Key.GID, StringComparer.OrdinalIgnoreCase)
                .Where(group => !sharedSyncshellIds.Contains(group.Key.GID))
                .Where(group => !pairCountsByGroupId.TryGetValue(group.Key.GID, out var count) || count < maxPairs)
                .OrderBy(group => group.Key.GID)
                .ToList();

            if (finalSyncshells.Count == 0)
            {
                menuItems.Add(new MenuItem()
                {
                    Name = new SeStringBuilder().AddText("No Syncshells with invite access").Build(),
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var syncshell in finalSyncshells)
                {
                    var shell = syncshell.Value.Group;
                    var alias = String.IsNullOrWhiteSpace(shell.Alias) ? "" : $" ({shell.Alias})";
                    menuItems.Add(new MenuItem()
                    {
                        Name = new SeStringBuilder().AddText($"{shell.GID}{alias}").Build(),
                        OnClicked = _ => _inviteManager.SendGroupInvite(new(syncshell.Key, pair.UserData))
                    });
                }
            }

            menuItems.Add(new MenuItem()
                {
                    Name = new SeStringBuilder().AddText("Return").Build(),
                    IsReturn = true,
                    OnClicked = _ => _dalamudUtilService.OpenContextMenu(clickedArgs.AgentPtr)
                });

            clickedArgs.OpenSubmenu(new SeStringBuilder().AddText("Invite to Syncshell").Build(), menuItems);
        }

        private void DrawAddToOverridesContenxtSubmenu(Pair pair, IMenuItemClickedArgs clickedArgs)
        {
            var pairUID = pair.UserData.UID;

            var menuItems = new List<MenuItem>();

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Mod Filtering").Build(),
                OnClicked = _ =>
                {
                    if (!_configurationService.Current.UIDsToOverrideFilter.Contains(pairUID, StringComparer.Ordinal))
                    {
                        _configurationService.Current.UIDsToOverrideFilter.Add(pairUID);
                        _configurationService.Save();
                        Mediator.Publish(new CyclePauseMessage(pair.UserData));
                    }
                }
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Auto Compression").Build(),
                OnClicked = _ =>
                {
                    if (!_performanceConfigService.Current.UIDsToOverride.Contains(pairUID, StringComparer.Ordinal))
                    {
                        _performanceConfigService.Current.UIDsToOverride.Add(pairUID);
                        _performanceConfigService.Save();
                        Mediator.Publish(new CyclePauseMessage(pair.UserData));
                    }
                }
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Auto Threshold Pausing").Build(),
                OnClicked = _ =>
                {
                    if (!_performanceConfigService.Current.UIDsToIgnore.Contains(pairUID, StringComparer.Ordinal))
                    {
                        _performanceConfigService.Current.UIDsToIgnore.Add(pairUID);
                        _performanceConfigService.Save();
                        Mediator.Publish(new CyclePauseMessage(pair.UserData));
                    }
                }
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Auto Height Pausing").Build(),
                OnClicked = _ =>
                {
                    if (!_performanceConfigService.Current.UIDsToIgnoreForHeightPausing.Contains(pairUID, StringComparer.Ordinal))
                    {
                        _performanceConfigService.Current.UIDsToIgnoreForHeightPausing.Add(pairUID);
                        _performanceConfigService.Save();
                        Mediator.Publish(new CyclePauseMessage(pair.UserData));
                    }
                }
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Return").Build(),
                IsReturn = true,
                OnClicked = _ => _dalamudUtilService.OpenContextMenu(clickedArgs.AgentPtr)
            });

            clickedArgs.OpenSubmenu(new SeStringBuilder().AddText("Add to Overrides").Build(), menuItems);
        }

        private void AddUserPairChatContextMenu(IMenuOpenedArgs args)
        {
            if (args.Target is not MenuTargetDefault target)
                return;

            var cid = target.TargetContentId.ToString().GetHash256();

            var pair = _pairManager.GetPairByCID(cid);
            if (pair == null || pair.IsPaused) return;

            args.AddMenuItem(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Send Lifestream Location Invite").Build(),
                OnClicked = (args) => DrawLifestreamInviteSubmenu(pair, args),
                UseDefaultPrefix = false,
                IsSubmenu = true,
                PrefixChar = 'P',
                PrefixColor = 530,
            });
        }

        private void DrawLifestreamInviteSubmenu(Pair pair, IMenuItemClickedArgs clickedArgs)
        {
            var menuItems = new List<MenuItem>();
            var addressEntries = _ipcManager.Lifestream.GetAddressBookEntries();

            foreach (var entry in addressEntries)
            {
                var addressName = _ipcManager.Lifestream.GetAddressBookEntryTextWithName(entry);

                menuItems.Add(new MenuItem()
                {
                    Name = new SeStringBuilder().AddText(addressName).Build(),
                    OnClicked = (__) => _ = _apiController.SendLifestreamInviteToPair(pair, entry)
                });
            }

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Use Current Location").Build(),
                OnClicked = (__) => _ = _apiController.SendLifestreamInviteToPair(pair)
            });

            menuItems.Add(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Return").Build(),
                IsReturn = true,
                OnClicked = _ => _dalamudUtilService.OpenContextMenu(clickedArgs.AgentPtr)
            });

            clickedArgs.OpenSubmenu(new SeStringBuilder().AddText("Lifestream Address Book").Build(), menuItems);
        }
    }
}