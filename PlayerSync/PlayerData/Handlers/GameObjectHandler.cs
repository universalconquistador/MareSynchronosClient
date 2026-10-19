using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;
using ObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace MareSynchronos.PlayerData.Handlers;

public sealed class GameObjectHandler : DisposableMediatorSubscriberBase, IHighPriorityMediatorSubscriber
{
    private readonly DalamudUtilService _dalamudUtil;

    private readonly Func<IntPtr> _getAddress;
    private readonly bool _isOwnedObject;
    private readonly bool _registeredForFrameworkUpdates;

    private CancellationTokenSource _zoningCts = new();
    private byte _classJob = 0;
    private Task? _delayedZoningTask;
    private volatile bool _haltProcessing = false;
    private volatile bool _modelReady;
    private int _disposed;
    private ushort _objectIndex = ushort.MaxValue;
    private bool _gameObjectPresent;
    private volatile bool _isRedrawing;
    private long _redrawTimeStart = -1;

    public GameObjectHandler(ILogger<GameObjectHandler> logger, PerformanceCollectorService performanceCollector, MareMediator mediator,
        DalamudUtilService dalamudUtil, ObjectKind objectKind, Func<IntPtr> getAddress, bool ownedObject = true,
        bool trackOnFramework = false, ulong contentId = 0) : base(logger, mediator)
    {
        ObjectKind = objectKind;
        _dalamudUtil = dalamudUtil;
        _getAddress = () =>
        {
            _dalamudUtil.EnsureIsOnFramework();
            return getAddress.Invoke();
        };
        _isOwnedObject = ownedObject;
        ContentId = contentId;
        Name = string.Empty;

        if (ownedObject)
        {
            Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
            {
                if (_delayedZoningTask?.IsCompleted ?? true)
                {
                    if (msg.ObjectKind != null && msg.ObjectKind != ObjectKind)
                    {
                        return;
                    }
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }
            });
        }

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ZoneSwitchEnd());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => ZoneSwitchStart());

        Mediator.Subscribe<CutsceneStartMessage>(this, (_) =>
        {
            _haltProcessing = true;
        });

        Mediator.Subscribe<CutsceneEndMessage>(this, (_) =>
        {
            _haltProcessing = false;
            ZoneSwitchEnd();
        });

        // TODO: do something with this, use or delete it
        Mediator.Subscribe<PenumbraStartRedrawMessage>(this, (msg) =>
        {
            if (string.Equals(msg.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                _haltProcessing = true;
            }
        });

        // TODO: do something with this, use or delete it
        Mediator.Subscribe<PenumbraEndRedrawMessage>(this, (msg) =>
        {
            if (string.Equals(msg.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                _haltProcessing = false;
            }
        });

        Mediator.Subscribe<PenumbraRedrawMessage>(this, (msg) =>
        {
            if (_dalamudUtil.IsOnFrameworkThread && msg.Address != nint.Zero && msg.Address == _getAddress())
            {
                _gameObjectPresent = false;
                ModelReady = false;
                IsRedrawingViaIpcRequest = false;
            }
        });

        _registeredForFrameworkUpdates = _isOwnedObject || trackOnFramework;
        Logger.LogTrace("Handler {handler} is being tracked: {tracked}", this, _registeredForFrameworkUpdates);
        if (_registeredForFrameworkUpdates)
        {
            _dalamudUtil.RegisterGameObjectHandler(this);
        }
        Mediator.Publish(new GameObjectHandlerCreatedMessage(this, _isOwnedObject));
    }

    // do not use this
    public IntPtr Address { get; private set; } = IntPtr.Zero;
    public ulong ContentId { get; }
    public DrawCondition CurrentDrawCondition { get; set; } = DrawCondition.None;
    public bool ModelReady
    {
        get => _modelReady;
        private set => _modelReady = value;
    }
    public bool IsRedrawingViaIpcRequest
    {
        get
        {
            return _isRedrawing;
        }
        set
        {
            if (value)
            {
                if (_redrawTimeStart != -1)
                {
                    Logger.LogWarning("{this} called to redraw but redraw already requested!", this);
                }
                else
                {
                    _redrawTimeStart = Stopwatch.GetTimestamp();
                }
                _haltProcessing = true;
                _isRedrawing = true;
            }
            else
            {
                if (_redrawTimeStart != -1) // another plugin may have triggered this redraw
                {
                    Logger.LogTrace("Total redraw time for {this}: {time:F3}ms", this, Stopwatch.GetElapsedTime(_redrawTimeStart).TotalMilliseconds);
                }
                _haltProcessing = false;
                _isRedrawing = false;
                _redrawTimeStart = -1;
            }
        }
    }
    internal bool IsOwnedObject => _isOwnedObject;
    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    public ulong PlayerCharacterId { get; private set; }
    private byte[] CustomizeData { get; set; } = new byte[26];
    private byte[] EquipSlotData { get; set; } = new byte[40];
    private ushort[] MainHandData { get; set; } = new ushort[3];
    private ushort[] OffHandData { get; set; } = new ushort[3];

    public unsafe Dalamud.Game.ClientState.Objects.Types.IGameObject? GetGameObject()
    {
        return _dalamudUtil.GetGameObjectByAddress(_getAddress());
    }

    public void Invalidate()
    {
        Address = IntPtr.Zero;
        _gameObjectPresent = false;
        _objectIndex = ushort.MaxValue;
        ModelReady = false;
        PlayerCharacterId = 0;
        CurrentDrawCondition = DrawCondition.ObjectZero;
    }

    public override string ToString()
    {
        var owned = _isOwnedObject ? "Self" : "Other";

        return $"{owned}/{ObjectKind}:{Name} ({ContentId})";
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        base.Dispose(disposing);

        try
        {
            _zoningCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_registeredForFrameworkUpdates)
        {
            _dalamudUtil.UnregisterGameObjectHandler(this);
        }
        Mediator.Publish(new GameObjectHandlerDestroyedMessage(this, _isOwnedObject));
    }

    public unsafe CharacterDrawState GetChareacterDrawStateFromFramework(nint? address = null)
    {
        _dalamudUtil.EnsureIsOnFramework();
        ModelReady = false;

        var localPlayer = _dalamudUtil.GetPlayerCharacter();
        if (localPlayer == null)
        {
            return new(DrawState.Missing, DrawCondition.ObjectZero);
        }
        if (_dalamudUtil.IsZoning || (localPlayer.IsDead && _dalamudUtil.IsBoundByDuty) || _delayedZoningTask is { IsCompleted: false })
        {
            return new(DrawState.Loading, DrawCondition.Zoning);
        }

        if (address == null)
        {
            address = _getAddress();
        }
        var drawState = _dalamudUtil.GetCharacterDrawStateFromFramework(address.Value);
        if (drawState.State == DrawState.Missing)
        {
            _objectIndex = ushort.MaxValue;

            return drawState;
        }
        if (drawState.State == DrawState.Loading)
        {
            _objectIndex = ((GameObject*)address.Value)->ObjectIndex;
        }
        ModelReady = drawState.State == DrawState.Ready;

        return drawState;
    }

    internal nint GetAddressFromFramework()
    {
        return _getAddress();
    }

    private unsafe void CheckAndUpdateObject(CharacterDrawState drawState, nint address)
    {
        bool hadGameObject = _gameObjectPresent;
        bool haveGameObject = address != nint.Zero && drawState.State == DrawState.Ready && ModelReady;
        CurrentDrawCondition = drawState.Condition;

        if (!haveGameObject)
        {
            _gameObjectPresent = false;
            PlayerCharacterId = 0;
            if (drawState.State == DrawState.Missing)
            {
                _objectIndex = ushort.MaxValue;
            }
            if (hadGameObject && _isOwnedObject && ObjectKind != ObjectKind.Player)
            {
                Mediator.Publish(new ClearCacheForObjectMessage(this));
            }

            return;
        }

        if (_haltProcessing)
        {
            _gameObjectPresent = false;

            return;
        }

        _gameObjectPresent = true;
        var gameObject = (GameObject*)address;
        bool changedObject = !hadGameObject || _objectIndex != gameObject->ObjectIndex || PlayerCharacterId != (ulong)gameObject->EntityId;
        _objectIndex = gameObject->ObjectIndex;
        PlayerCharacterId = (ulong)gameObject->EntityId;
        var drawObjectAddress = (nint)gameObject->DrawObject;
        var chara = (Character*)address;
        var name = chara->GameObject.NameString;
        bool nameChange = !string.Equals(name, Name, StringComparison.Ordinal);
        if (nameChange)
        {
            Name = name;
        }
        bool equipDiff;
        if (((DrawObject*)drawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase
            && ((CharacterBase*)drawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
        {
            var classJob = chara->CharacterData.ClassJob;
            if (classJob != _classJob)
            {
                _classJob = classJob;
                Mediator.Publish(new ClassJobChangedMessage(this));
            }
            equipDiff = CompareAndUpdateEquipByteData((byte*)&((Human*)drawObjectAddress)->Head);
            ref var mh = ref chara->DrawData.Weapon(WeaponSlot.MainHand);
            ref var oh = ref chara->DrawData.Weapon(WeaponSlot.OffHand);
            equipDiff |= CompareAndUpdateMainHand((Weapon*)mh.DrawObject);
            equipDiff |= CompareAndUpdateOffHand((Weapon*)oh.DrawObject);
        }
        else
        {
            equipDiff = CompareAndUpdateEquipByteData((byte*)Unsafe.AsPointer(ref chara->DrawData.EquipmentModelIds[0]));
        }
        if (equipDiff && !_isOwnedObject)
        {
            return;
        }
        bool customizeDiff;
        if (((DrawObject*)drawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase
            && ((CharacterBase*)drawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
        {
            customizeDiff = CompareAndUpdateCustomizeData(((Human*)drawObjectAddress)->Customize.Data);
        }
        else
        {
            customizeDiff = CompareAndUpdateCustomizeData(chara->DrawData.CustomizeData.Data);
        }
        if ((changedObject || equipDiff || customizeDiff || nameChange) && _isOwnedObject)
        {
            Mediator.Publish(new CreateCacheForObjectMessage(this));
        }
    }

    private unsafe bool CompareAndUpdateCustomizeData(Span<byte> customizeData)
    {
        bool hasChanges = false;

        for (int i = 0; i < customizeData.Length; i++)
        {
            var data = customizeData[i];
            if (CustomizeData[i] != data)
            {
                CustomizeData[i] = data;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateEquipByteData(byte* equipSlotData)
    {
        bool hasChanges = false;
        for (int i = 0; i < EquipSlotData.Length; i++)
        {
            var data = equipSlotData[i];
            if (EquipSlotData[i] != data)
            {
                EquipSlotData[i] = data;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateMainHand(Weapon* weapon)
    {
        if ((nint)weapon == nint.Zero)
        {
            return false;
        }

        bool hasChanges = false;
        hasChanges |= weapon->ModelSetId != MainHandData[0];
        MainHandData[0] = weapon->ModelSetId;
        hasChanges |= weapon->Variant != MainHandData[1];
        MainHandData[1] = weapon->Variant;
        hasChanges |= weapon->SecondaryId != MainHandData[2];
        MainHandData[2] = weapon->SecondaryId;

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateOffHand(Weapon* weapon)
    {
        if ((nint)weapon == nint.Zero)
        {
            return false;
        }

        bool hasChanges = false;
        hasChanges |= weapon->ModelSetId != OffHandData[0];
        OffHandData[0] = weapon->ModelSetId;
        hasChanges |= weapon->Variant != OffHandData[1];
        OffHandData[1] = weapon->Variant;
        hasChanges |= weapon->SecondaryId != OffHandData[2];
        OffHandData[2] = weapon->SecondaryId;

        return hasChanges;
    }

    internal unsafe void FrameworkUpdate(bool canReadObjects)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        try
        {
            if (!canReadObjects || _delayedZoningTask is { IsCompleted: false })
            {
                Invalidate();

                return;
            }
            var address = _getAddress();
            if (address != nint.Zero)
            {
                Address = address; // for resource load event
            }
            var drawState = GetChareacterDrawStateFromFramework(address);
            if (drawState.State != DrawState.Ready || !ModelReady)
            {
                address = nint.Zero;
            }
            CheckAndUpdateObject(drawState, address);
        }
        catch (Exception ex)
        {
            Invalidate();
            Logger.LogWarning(ex, "Error during FrameworkUpdate of {this}", this);
        }
    }

    private void ZoneSwitchEnd()
    {
        if (!_isOwnedObject)
        {
            return;
        }

        try
        {
            _zoningCts?.CancelAfter(2500);
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Zoning CTS cancel issue");
        }
    }

    private void ZoneSwitchStart()
    {
        Invalidate();

        if (!_isOwnedObject)
        {
            return;
        }

        var zoningCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _zoningCts, zoningCts);
        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        Logger.LogDebug("[{obj}] Starting Delay After Zoning", this);
        _delayedZoningTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(120), zoningCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore cancelled
            }
            finally
            {
                Logger.LogDebug("[{this}] Delay after zoning complete", this);
                zoningCts.Dispose();
            }
        });
    }
}
