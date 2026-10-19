using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class PairHandler : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly ServerConfigurationManager _serverConfigManager;

    private GameObjectHandler? _charaHandler;
    private volatile bool _isVisible;

    public PairHandler(ILogger<PairHandler> logger, Pair pair,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        DalamudUtilService dalamudUtil, MareMediator mediator,
        ServerConfigurationManager serverConfigManager, MareConfigService configService
        ) : base(logger, mediator)
    {
        Pair = pair;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _dalamudUtil = dalamudUtil;
        _serverConfigManager = serverConfigManager;

        if (!configService.Current.DebugDisableSoundIndicators)
        {
            Mediator.Subscribe<PenumbraSoundResourceLoadMessage>(this, OnPenumbraResourceLoaded);
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                string text = "User Visibility Changed, now: " + (_isVisible ? "Is Visible" : "Is not Visible");
                Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Informational, text)));
                Mediator.Publish(new RefreshUiMessage());
            }
        }
    }
    public bool IsModelReady => IsVisible && (_charaHandler?.ModelReady ?? false);
    public Pair Pair { get; private set; }
    public GameObjectHandler? CharacterHandler => _charaHandler;
    public nint PlayerCharacter => _dalamudUtil.IsOnFrameworkThread && IsVisible && _dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out var address, out _) ? address : nint.Zero;
    public ulong PlayerCharacterId => _charaHandler?.PlayerCharacterId ?? 0;
    public string? PlayerName { get; private set; }
    public ulong ContentId => _charaHandler?.ContentId ?? 0;
    public string PlayerNameHash => Pair.HashedCID;
    public override string ToString()
    {
        return Pair == null ? base.ToString() ?? string.Empty : Pair.UserData.AliasOrUID + ":" + PlayerName + ":" + (IsVisible ? "HasChar" : "NoChar");
    }

    public async Task Initialize(string name, ulong contendId)
    {
        _charaHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () =>
        {
            _dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(contendId, out var address, out _);

            return address;
        }, contendId, isOwnObject: false).ConfigureAwait(false);
        PlayerName = name;
        _serverConfigManager.AutoPopulateNoteForUid(Pair.UserData.UID, name);
    }

    public void UpdateVisibility(bool visible)
    {
        IsVisible = visible;
    }

    private void OnPenumbraResourceLoaded(PenumbraSoundResourceLoadMessage resourceLoad)
    {
        if (!_dalamudUtil.IsOnFrameworkThread || !IsVisible || resourceLoad.ContentId != Pair.ContendId)
        {
            return;
        }

        Pair.LastLoadedSoundSinceRedrawTime = DateTimeOffset.UtcNow;
        Pair.LastLoadedSoundSinceRedraw = resourceLoad.FilePath;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Mediator.Publish(new PairOfflineMessage(Pair));

        var name = PlayerName;
        Logger.LogDebug("Disposing {name} ({user})", name, Pair);
        try
        {
            _charaHandler?.Dispose();
            _charaHandler = null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error on disposal of {name}", name);
        }
        finally
        {
            PlayerName = null;
        }
    }
}
