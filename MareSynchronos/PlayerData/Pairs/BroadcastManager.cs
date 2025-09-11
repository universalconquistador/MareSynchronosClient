using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.PlayerData.Pairs
{
    /// <summary>
    /// Manages the broadcasts of groups to and from other players.
    /// </summary>
    public interface IBroadcastManager
    {
        /// <summary>
        /// Whether broadcasts are currently being listened for.
        /// </summary>
        /// <remarks>
        /// If this is false, outgoing broadcasts will not be sent either.
        /// </remarks>
        bool IsListening { get; }

        /// <summary>
        /// The group ID (GID) of the group that is currently being broadcast by the player, or null if the player is
        /// not broadcasting any group.
        /// </summary>
        string? BroadcastingGroupId { get; }

        /// <summary>
        /// Gets the broadcasts that are available to the player.
        /// </summary>
        IReadOnlyList<GroupBroadcastDto> AvailableBroadcastGroups { get; }

        /// <summary>
        /// Starts listening for incoming broadcasts, if not already listening.
        /// </summary>
        void StartListening();

        /// <summary>
        /// Stops listening for incoming broadcasts, if listening, and stops any broadcast from this player.
        /// </summary>
        void StopListening();

        /// <summary>
        /// Starts broadcasting the group from the player.
        /// </summary>
        /// <param name="groupId">The ID of the group to broadcast.</param>
        void StartBroadcasting(string groupId);

        /// <summary>
        /// Stops broadcasting any group from the player.
        /// </summary>
        void StopBroadcasting();
    }

    public static class BroadcastManagerExtensions
    {
        public static bool IsBroadcasting(this IBroadcastManager broadcastManager)
        {
            return !string.IsNullOrEmpty(broadcastManager.BroadcastingGroupId);
        }
    }

    public class BroadcastManager : DisposableMediatorSubscriberBase, IBroadcastManager
    {
        private readonly ILogger<BroadcastManager> _logger;
        private readonly ApiController _apiController;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly PairManager _pairManager;
        private readonly MareConfigService _mareConfigService;

        private DateTimeOffset _nextPeriodicPoll;
        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

        private volatile int _startingListening = 0;
        private volatile int _stoppingListening = 0;
        private volatile int _pollingBroadcasts = 0;

        public BroadcastManager(ILogger<BroadcastManager> logger, MareMediator mediator,
            ApiController apiController,
            DalamudUtilService dalamudUtilService,
            PairManager pairManager,
            MareConfigService mareConfigService)
            : base(logger, mediator)
        {
            _logger = logger;
            _apiController = apiController;
            _dalamudUtilService = dalamudUtilService;
            _pairManager = pairManager;
            _mareConfigService = mareConfigService;

            Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, OnDelayedFrameworkUpdate);
            Mediator.Subscribe<BroadcastListeningChanged>(this, message =>
            {
                mareConfigService.Current.ListenForBroadcasts = message.isListening;
                mareConfigService.Save();
            });
            Mediator.Subscribe<ConnectedMessage>(this, _ =>
            {
                // TEMP: For now, reset to not listening when connected to make sure we don't overwhelm the servers
                _mareConfigService.Current.ListenForBroadcasts = false;
                _mareConfigService.Save();

                IsListening = false;
                if (_mareConfigService.Current.ListenForBroadcasts)
                {
                    StartListening();
                }
            });
            Mediator.Subscribe<DisconnectedMessage>(this, _ =>
            {
                // TEMP: For now, reset to not listening when disconnected to make sure we don't overwhelm the servers
                _mareConfigService.Current.ListenForBroadcasts = false;
                _mareConfigService.Save();

                IsListening = false;
            });
            Mediator.Subscribe<BroadcastListeningChanged>(this, message => IsListening = message.isListening);
            Mediator.Subscribe<GroupMembershipChanged>(this, message =>
            {
                // If we have been demodded, make sure we aren't still trying to broadcast
                if (message.Dto.GID == BroadcastingGroupId)
                {
                    var isNowMod = message.Dto.GroupUserInfo.IsModerator();
                    var group = _pairManager.Groups.Values.FirstOrDefault(g => g.GID == message.Dto.GID);

                    var isOwner = false;
                    if (group != null)
                    {
                        isOwner = group.OwnerUID == _apiController.UID;
                    }

                    if (!(isOwner || isNowMod))
                    {
                        _logger.LogDebug("Demodded while broadcasting group {gid} - stopping broadcast attempts.", message.Dto.GID);
                        StopBroadcasting();
                    }
                }
            });
            Mediator.Subscribe<GroupInfoChanged>(this, message =>
            {
                // If we have lost ownership, make sure we aren't still trying to broadcast
                if (message.GroupInfo.GID == BroadcastingGroupId)
                {
                    var isMod = false;
                    var group = _pairManager.Groups.Values.FirstOrDefault(g => g.GID == message.GroupInfo.GID);
                    if (group != null)
                    {
                        isMod = group.GroupPairUserInfos[_apiController.UID].IsModerator();
                    }
                    
                    var isNowOwner = message.GroupInfo.OwnerUID == _apiController.UID;

                    if (!(isNowOwner || isMod))
                    {
                        _logger.LogDebug("No longer owner or mod while broadcasting group {gid} - stopping broadcast attempts.", message.GroupInfo.GID);
                        StopBroadcasting();
                    }
                }
            });
        }

        public IReadOnlyList<GroupBroadcastDto> AvailableBroadcastGroups { get; private set; } = Array.Empty<GroupBroadcastDto>();
        private bool _isListening = false;
        public bool IsListening
        {
            get => _isListening;
            set
            {
                if (value != IsListening)
                {
                    _isListening = value;

                    if (IsListening)
                    {
                        // Kick off a poll right away
                        _nextPeriodicPoll = default;
                        PollBroadcasts();
                    }
                    else
                    {
                        AvailableBroadcastGroups = Array.Empty<GroupBroadcastDto>();
                        BroadcastingGroupId = null;
                    }

                    Mediator.Publish(new RefreshUiMessage());
                }
            }
        }
        public string? BroadcastingGroupId { get; private set; } = null;
        public bool IsBroadcastingGroup => BroadcastingGroupId != null;

        public void StartListening()
        {
            if (!IsListening && Interlocked.CompareExchange(ref _startingListening, 1, 0) == 0)
            {
                _ = StartListeningInternal().ConfigureAwait(false);
            }
        }

        private async Task StartListeningInternal()
        {
            var ident = await _dalamudUtilService.GetPlayerNameHashedAsync().ConfigureAwait(false);

            try
            {
                await _apiController.BroadcastStartListening(ident).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception attempting to start listening for broadcasts!");
            }
            _startingListening = 0;
        }

        public void StopListening()
        {
            if (IsListening && Interlocked.CompareExchange(ref _stoppingListening, 1, 0) == 0)
            {
                _ = StopListeningInternal().ConfigureAwait(false);
            }
        }

        private async Task StopListeningInternal()
        {
            try
            {
                await _apiController.BroadcastStopListening().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception attempting to stop listening for broadcasts!");
            }
            _stoppingListening = 0;
        }

        public void StartBroadcasting(string groupId)
        {
            BroadcastingGroupId = groupId;

            Mediator.Publish(new NotificationMessage($"Broadcasting {groupId}", $"You are now broadcasting {groupId} to Player Sync players around you. You can use the Syncshell Broadcast tab to stop broadcasting at any time.", MareConfiguration.Models.NotificationType.Info));

            PollBroadcasts();
        }

        public void StopBroadcasting()
        {
            BroadcastingGroupId = null;
        }

        private void OnDelayedFrameworkUpdate(DelayedFrameworkUpdateMessage message)
        {
            if (IsListening && _nextPeriodicPoll < DateTimeOffset.UtcNow && _dalamudUtilService.IsLoggedIn)
            {
                Logger.LogTrace("Time for the periodic broadcast poll...");
                PollBroadcasts();
            }
        }

        private void PollBroadcasts()
        {
            if (Interlocked.CompareExchange(ref _pollingBroadcasts, 1, 0) == 0)
            {
                _ = PollBroadcastsInternal().ConfigureAwait(false);
            }
        }

        private async Task PollBroadcastsInternal()
        {
            try
            {
                WorldData location = await _dalamudUtilService.RunOnFrameworkThread(() =>
                {
                    var player = _dalamudUtilService.GetPlayerCharacter();

                    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, player?.Rotation ?? 0.0f);
                    WorldData result = new WorldData
                    {
                        LocationInfo = _dalamudUtilService.GetMapData(),
                        PositionX = player?.Position.X ?? 0.0f,
                        PositionY = player?.Position.Y ?? 0.0f,
                        PositionZ = player?.Position.Z ?? 0.0f,
                        RotationX = rotation.X,
                        RotationY = rotation.Y,
                        RotationZ = rotation.Z,
                        RotationW = rotation.W,
                        ScaleX = 1.0f,
                        ScaleY = 1.0f,
                        ScaleZ = 1.0f,
                    };

                    return result;
                }).ConfigureAwait(false);
                string locationString = $"Server {location.LocationInfo.ServerId} / Map {location.LocationInfo.MapId} / Territory {location.LocationInfo.TerritoryId} / Division {location.LocationInfo.DivisionId} / Ward {location.LocationInfo.WardId} / House {location.LocationInfo.HouseId} / Room {location.LocationInfo.RoomId}";

                string? broadcastGroupId = BroadcastingGroupId;
                List<GroupBroadcastDto> broadcasts;
                List<string> visibleIdents = _dalamudUtilService.GetVisiblePlayerIdents().ToList();
                if (broadcastGroupId != null)
                {
                    Logger.LogTrace("Sending and receiving broadcast groups for {location}...", locationString);

                    broadcasts = await _apiController.BroadcastSendReceive(location, visibleIdents, new(broadcastGroupId)).ConfigureAwait(false);
                }
                else
                {
                    Logger.LogTrace("Receiving broadcast groups for {location}...", locationString);

                    broadcasts = await _apiController.BroadcastReceive(location).ConfigureAwait(false);
                }

                Logger.LogTrace("Received {count} groups.", broadcasts.Count);

                if (IsListening)
                {
                    AvailableBroadcastGroups = broadcasts.AsReadOnly();
                    Mediator.Publish(new RefreshUiMessage());
                }

                _nextPeriodicPoll = DateTimeOffset.UtcNow + _pollInterval;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception attempting to poll broadcasts!");
            }

            _pollingBroadcasts = 0;
        }
    }
}
