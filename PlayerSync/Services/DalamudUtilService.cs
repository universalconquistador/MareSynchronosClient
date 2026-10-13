using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using TargetType = MareSynchronos.Services.Models.TargetType;

namespace MareSynchronos.Services;

public class DalamudUtilService : IHostedService, IMediatorSubscriber
{
    public readonly record struct ObjectTablePlayersFrameworkUpdate(ulong ContentId, string Ident, string Name, bool IsVisible);

    // Any class variables defined here with nint or IntPtr will be removed
    private readonly List<uint> _classJobIdsIgnoredForPets = [30];
    public readonly ulong InstalledExpansions; // each bit is whether the corresponding expansion is installed, with bit 0 being the base game
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IDataManager _gameData;
    private readonly IGameConfig _gameConfig;
    private readonly BlockedCharacterHandler _blockedCharacterHandler;
    private readonly IFramework _framework;
    private readonly IGameGui _gameGui;
    private readonly ILogger<DalamudUtilService> _logger;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly MareConfigService _configService;

    private readonly Dictionary<ulong, (string Ident, string Name)> _frameworkCharacters = new(100);
    private readonly List<ObjectTablePlayersFrameworkUpdate> _frameworkCharacterChanges = new(100);
    private HashSet<ulong> _playersSeenThisFrame = new(100);
    private HashSet<ulong> _playersSeenPreviousFrame = new(100);
    private readonly ConcurrentQueue<(GameObjectHandler Handler, bool TrackedOnFramework)> _handlersTrackedOnFrameworkQueue = new();
    private readonly List<GameObjectHandler> _ownedHandlers = [];
    private readonly Dictionary<ulong, GameObjectHandler> _pairedHandlersByContentId = [];

    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.UtcNow;
    private uint _lastZone = 0;
    private uint _lastWorld = 0;
    private bool _sentBetweenAreas = false;
    private Lazy<ulong> _cid;
    private string? _playerName;

    public event Action<IReadOnlyList<ObjectTablePlayersFrameworkUpdate>>? FrameworkCharactersChanged;

    // How to find: Look for xrefs to "/ex%d/ex%d.ver"
    [Signature("E8 ?? ?? ?? ?? 48 8B D0 80 38 20")]
    private readonly unsafe delegate* unmanaged<Framework*, int, bool, byte*> _getExVersion;

    public DalamudUtilService(ILogger<DalamudUtilService> logger, IClientState clientState, IObjectTable objectTable, IFramework framework,
        IGameGui gameGui, ICondition condition, IDataManager gameData, ITargetManager targetManager, IGameConfig gameConfig,
        IGameInteropProvider gameInteropProvider, BlockedCharacterHandler blockedCharacterHandler, MareMediator mediator, PerformanceCollectorService performanceCollector,
        MareConfigService configService)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _gameGui = gameGui;
        _targetManager = targetManager;
        _condition = condition;
        _gameData = gameData;
        _gameConfig = gameConfig;
        _blockedCharacterHandler = blockedCharacterHandler;
        Mediator = mediator;
        _performanceCollector = performanceCollector;
        _configService = configService;
        gameInteropProvider.InitializeFromAttributes(this);
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<World>(Dalamud.Game.ClientLanguage.English)!
                .Where(w => !w.Name.IsEmpty && w.DataCenter.RowId != 0 && (w.IsPublic || char.IsUpper(w.Name.ToString()[0])))
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
        WorldToDataCenterData = new(() =>
        {
            return gameData.GetExcelSheet<World>(Dalamud.Game.ClientLanguage.English)!
                .Where(w => !w.Name.IsEmpty && w.DataCenter.RowId != 0 && (w.IsPublic || char.IsUpper(w.Name.ToString()[0])))
                .ToDictionary(
                    w => (ushort)w.RowId,
                    w => w.DataCenter.Value.Name.ToString());
        });
        WorldToDataCenterIdData = new(() =>
        {
            return gameData.GetExcelSheet<World>(Dalamud.Game.ClientLanguage.English)!
                .Where(w => !w.Name.IsEmpty && w.DataCenter.RowId != 0 && (w.IsPublic || char.IsUpper(w.Name.ToString()[0])))
                .ToDictionary(
                    w => (ushort)w.RowId,
                    w => w.DataCenter.RowId);
        });
        JobData = new(() =>
        {
            return gameData.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English)!
                .ToDictionary(k => k.RowId, k => k.NameEnglish.ToString());
        });
        TerritoryData = new(() =>
        {
            return gameData.GetExcelSheet<TerritoryType>(Dalamud.Game.ClientLanguage.English)!
            .Where(w => w.RowId != 0)
            .ToDictionary(w => w.RowId, w =>
            {
                StringBuilder sb = new();
                sb.Append(w.PlaceNameRegion.Value.Name);
                if (w.PlaceName.ValueNullable != null)
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceName.Value.Name);
                }
                return sb.ToString();
            });
        });
        MapData = new(() =>
        {
            return gameData.GetExcelSheet<Map>(Dalamud.Game.ClientLanguage.English)!
            .Where(w => w.RowId != 0)
            .ToDictionary(w => w.RowId, w =>
            {
                StringBuilder sb = new();
                sb.Append(w.PlaceNameRegion.Value.Name);
                if (w.PlaceName.ValueNullable != null)
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceName.Value.Name);
                }
                if (w.PlaceNameSub.ValueNullable != null && !string.IsNullOrEmpty(w.PlaceNameSub.Value.Name.ToString()))
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceNameSub.Value.Name);
                }
                return (w, sb.ToString());
            });
        });

        mediator.Subscribe<TargetPairMessage>(this, (msg) => TargetPairByTargetType(msg.Pair, msg.TargetType));

        IsWine = Util.IsWine();
        _cid = RebuildCID();

        InstalledExpansions = 0;
        unsafe
        {
            ReadOnlySpan<byte> noneVersionString = "none"u8;
            for (int i = 0; i < Framework.Instance()->ExVersions.Length; i++)
            {
                var versionSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_getExVersion(Framework.Instance(), i, false));

                if (versionSpan.Length == 0)
                {
                    break;
                }

                if (!versionSpan.StartsWith(noneVersionString))
                {
                    InstalledExpansions |= 1UL << i;
                }
            }
        }
    }

    public string GetWorldName(int worldId)
    {
        return WorldData.Value.TryGetValue((ushort)worldId, out var worldName) ? worldName : string.Empty;
    }

    public string GetTerritoryName(int terrirotyId)
    {
        return TerritoryData.Value.TryGetValue((ushort)terrirotyId, out var territoryName) ? territoryName : string.Empty;
    }

    /// <summary>
    /// Checks whether the given expansion is installed.
    /// </summary>
    /// <param name="index">The expansion to check, with 0 being the base game and 1 being Heavensward.</param>
    /// <returns>Whether the given expansion is installed.</returns>
    public bool IsExpansionInstalled(int index)
    {
        return (InstalledExpansions & (1UL << index)) != 0;
    }

    private Lazy<ulong> RebuildCID() => new(GetCID);

    public bool IsWine { get; init; }

    public string TargetName
    {
        get => _targetManager.Target?.Name.TextValue ?? string.Empty;
    }
    public unsafe nint TargetAddress
    {
        get => _targetManager.Target?.Address ?? nint.Zero;
    }
    public string SoftTargetName
    {
        get => _targetManager.SoftTarget?.Name.TextValue ?? string.Empty;
    }
    public unsafe nint SoftTargetAddress
    {
        get => _targetManager.SoftTarget?.Address ?? nint.Zero;
    }
    public string FocusTargetName
    {
        get => _targetManager.FocusTarget?.Name.TextValue ?? string.Empty;
    }
    public unsafe nint FocusTargetAddress
    {
        get => _targetManager.FocusTarget?.Address ?? nint.Zero;
    }
    public string MouseOverTargetName
    {
        get => _targetManager.MouseOverTarget?.Name.TextValue ?? string.Empty;
    }
    public unsafe nint MouseOverTargetAddress
    {
        get => _targetManager.MouseOverTarget?.Address ?? nint.Zero;
    }
    public unsafe GameObject* GposeTarget
    {
        get => TargetSystem.Instance()->GPoseTarget;
        set => TargetSystem.Instance()->GPoseTarget = value;
    }
    private unsafe bool HasGposeTarget => GposeTarget != null;
    private unsafe int GPoseTargetIdx => !HasGposeTarget ? -1 : GposeTarget->ObjectIndex;

    public bool IsInCutscene { get; private set; } = false;
    public bool IsOccupiedInCutSceneEvent => _condition[ConditionFlag.OccupiedInCutSceneEvent];
    public bool IsInGpose { get; private set; } = false;
    public bool IsLoggedIn { get; private set; }
    public bool IsOnFrameworkThread => _framework.IsInFrameworkUpdateThread;
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    public bool IsBoundByDuty { get; set; } = false;
    public bool IsBoundByPvP { get; set; } = false;
    public bool IsPvPExcludingDen => _clientState.IsPvPExcludingDen;
    public bool IsSyncPausedByDuty => IsBoundByDuty && _configService.Current.DisableSyncDuringDuty;
    public bool IsSyncPausedByPvP => IsBoundByPvP && _configService.Current.DisableSyncDuringPvP;
    public bool IsInCombat { get; private set; } = false;
    public bool IsPerforming { get; private set; } = false;
    public bool IsInCombatOrPerforming => IsInCombat || IsPerforming;
    public bool HasModifiedGameFiles => _gameData.HasModifiedGameDataFiles;
    public uint ClassJobId => _classJobId!.Value;
    public Lazy<Dictionary<uint, string>> JobData { get; private set; }
    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }
    public Lazy<Dictionary<ushort, string>> WorldToDataCenterData { get; private set; }
    public Lazy<Dictionary<ushort, uint>> WorldToDataCenterIdData { get; private set; }
    public Lazy<Dictionary<uint, string>> TerritoryData { get; private set; }
    public Lazy<Dictionary<uint, (Map Map, string MapName)>> MapData { get; private set; }
    public bool IsLodEnabled { get; private set; }
    public MareMediator Mediator { get; }
    public float FPSCounter { get; private set; } = 0f;
    public string PlayerName
    {
        get
        {
            if (_playerName == null)
            {
                _playerName = GetPlayerNameAsync().GetAwaiter().GetResult();
            }
            return _playerName;
        }
    }

    // basically just use this 90% of the time
    public unsafe bool TryGetPlayerAddressAndIndexOnFrameworkByContentId(ulong contentId, out nint address, out ushort index)
    {
        EnsureIsOnFramework();
        address = nint.Zero;
        index = ushort.MaxValue;
        if (contentId == 0)
        {
            return false;
        }
        for (int i = 0; i < 200; i += 2)
        {
            if (_objectTable[i] is not IBattleChara chara || chara.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc || !chara.IsValid())
            {
                continue;
            }
            nint currentAddress = chara.Address;
            if (currentAddress == nint.Zero || ((BattleChara*)currentAddress)->Character.ContentId != contentId)
            {
                continue;
            }
            address = currentAddress;
            index = (ushort)i;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Register a game object handler to be tracked/updated by the main Framework loop
    /// </summary>
    public void RegisterGameObjectHandler(GameObjectHandler handler)
    {
        _handlersTrackedOnFrameworkQueue.Enqueue((handler, true));
    }

    /// <summary>
    /// Remove a game object handler from being tracked on the main Framework loop
    /// </summary>
    public void UnregisterGameObjectHandler(GameObjectHandler handler)
    {
        _handlersTrackedOnFrameworkQueue.Enqueue((handler, false));
    }

    public CharacterVisibilityUpdate? GetFrameworkCharacterVisibilityByContentId(ulong contentId)
    {
        EnsureIsOnFramework();
        if (!_frameworkCharacters.TryGetValue(contentId, out var character))
        {
            return null;
        }
        bool visible = _playersSeenThisFrame.Contains(contentId);
        bool ready = visible && _pairedHandlersByContentId.TryGetValue(contentId, out var handler) && handler.ModelReady;

        return new(character.Name, contentId, visible, ready);
    }

    public CharacterVisibilityUpdate? GetFrameworkCharacterVisibilityByIdent(string ident)
    {
        EnsureIsOnFramework();
        var id = _frameworkCharacters.FirstOrDefault(chara => chara.Value.Ident == ident);
        if (id.Key == 0)
        {
            return null; // return null here so we don't do another dictionary lookup
        }

        return GetFrameworkCharacterVisibilityByContentId(id.Key);
    }

    /// <summary>
    /// Check if the player exists in the current frame and is able to perform an action before performing it.
    /// Must be called on the Framework thread
    /// </summary>
    public unsafe bool TryActOnFramework(ulong contentId, Action<IBattleChara, nint> action, out string? failureReason)
    {
        EnsureIsOnFramework();
        failureReason = null;

        if (!TryGetPlayerAddressAndIndexOnFrameworkByContentId(contentId, out var address, out var index))
        {
            failureReason = "Player lookup failed";
            return false;
        }

        if (_objectTable[index] is not IBattleChara player)
        {
            failureReason = "Character object not present";
            return false;
        }

        action(player, address);

        return true;
    }

    /// <summary>
    /// Gets the character's draw state after ensuring the provided address is still valid in the ObjectTable
    /// </summary>
    public CharacterDrawState GetCharacterDrawStateFromFramework(nint address, bool requireRenderable = true)
    {
        EnsureIsOnFramework();

        return GetCharacterDrawStateFromFrameworkInternal(address, requireRenderable);
    }

    /// <summary>
    /// Gets the character's draw state
    /// </summary>
    private static unsafe CharacterDrawState GetCharacterDrawStateFromFrameworkInternal(nint address, bool requireRenderable)
    {
        if (address == nint.Zero)
        {
            return new(DrawState.Missing, DrawCondition.ObjectZero);
        }
        var gameObject = (GameObject*)address;
        if (gameObject->DrawObject == null)
        {
            return new(DrawState.Loading, DrawCondition.DrawObjectZero);
        }
        var model = gameObject->GetCharacterBase();
        if (model == null)
        {
            return new(DrawState.Loading, DrawCondition.CharacterBaseZero);
        }
        if (model->HasModelInSlotLoaded != 0)
        {
            return new(DrawState.Loading, DrawCondition.ModelInSlotLoaded);
        }
        if (model->HasModelFilesInSlotLoaded != 0)
        {
            return new(DrawState.Loading, DrawCondition.ModelFilesInSlotLoaded);
        }
        if (requireRenderable && !(gameObject->Visibility == 0 && gameObject->RenderFlags == VisibilityFlags.None))
        {
            return new(DrawState.Loading, DrawCondition.NotRenderable);
        }

        return new(DrawState.Ready, DrawCondition.None);
    }

    public ICharacter? GetCharacterFromObjectTableByAddressWhenAlreadyOnFrameworkThread(nint address)
    {
        EnsureIsOnFramework();
        if (address == nint.Zero)
        {
            return null;
        }
        for (int i = 0; i < 200; i++)
        {
            if (_objectTable.GetObjectAddress(i) == address && _objectTable[i] is ICharacter character && character.IsValid())
            {
                return character;
            }
        }

        return null;
    }

    public ICharacter? GetCharacterFromObjectTableByIndex(int index)
    {
        EnsureIsOnFramework();

        return _objectTable[index] is ICharacter character && character.IsValid() ? character : null;
    }

    private const int GposeFirstObjectIndex = 201;
    // Brio can extend beyond 242 so we just scan to the end of the table for gpose 

    public ICharacter? GetCharacterFromObjectTableByName(string name)
    {
        EnsureIsOnFramework();
        for (int i = 0; i < 200; i++)
        {
            if (_objectTable[i] is ICharacter character && character.IsValid() && string.Equals(character.Name.ToString(), name, StringComparison.Ordinal))
            {
                return character;
            }
        }

        return null;
    }

    public ICharacter? GetGposeCharacterFromObjectTableByName(string name) // basically just a slight optimization to start earlier in the table
    {
        EnsureIsOnFramework();
        for (int i = GposeFirstObjectIndex; i < _objectTable.Length; i++)
        {
            if (_objectTable[i] is ICharacter character && character.IsValid() && string.Equals(character.Name.ToString(), name, StringComparison.Ordinal))
            {
                return character;
            }
        }

        return null;
    }

    public IEnumerable<ICharacter?> GetGposeCharactersFromObjectTable() // just a slight optimization to start earlier in the table
    {
        EnsureIsOnFramework();
        var characters = new List<ICharacter?>();
        for (int i = GposeFirstObjectIndex; i < _objectTable.Length; i++)
        {
            if (_objectTable[i] is ICharacter character && character.IsValid())
            {
                characters.Add(character);
            }
        }

        return characters;
    }

    public ICharacter? GetGposeTargetOnFramework()
    {
        EnsureIsOnFramework();
        if (!IsInGpose)
        {
            return null;
        }

        return GetCharacterFromObjectTableByIndex(GPoseTargetIdx);
    }

    ///////////////////////////
    ///  Player 'n pet stuff
    ///////////////////////////

    public bool GetIsPlayerPresent()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer != null && _objectTable.LocalPlayer.IsValid();
    }

    public async Task<bool> GetIsPlayerPresentAsync()
    {
        return await RunOnFrameworkThread(GetIsPlayerPresent).ConfigureAwait(false);
    }

    public IPlayerCharacter GetPlayerCharacter()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer!;
    }

    public string GetPlayerName()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer?.Name.ToString() ?? _playerName ?? "--";
    }

    public async Task<string> GetPlayerNameAsync()
    {
        return await RunOnFrameworkThread(GetPlayerName).ConfigureAwait(false);
    }

    public async Task<ulong> GetCIDAsync()
    {
        return await RunOnFrameworkThread(GetCID).ConfigureAwait(false);
    }

    public unsafe ulong GetCID()
    {
        EnsureIsOnFramework();
        var playerChar = GetPlayerCharacter();

        return playerChar == null ? 0 : ((BattleChara*)playerChar.Address)->Character.ContentId;
    }

    public unsafe string GetHashedCIDFromPlayerPointer(nint? ptr = null)
    {
        EnsureIsOnFramework();
        if (ptr == null)
        {
            ptr = GetPlayerCharacter().Address;
        }

        return ((BattleChara*)ptr)->Character.ContentId.ToString().GetHash256();
    }

    public IntPtr GetPlayerPtr()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer?.Address ?? IntPtr.Zero;
    }

    /// <summary>
    /// Defaults to the local player if no ptr is passed
    /// </summary>
    public unsafe IntPtr GetCompanionPtrFromFramework(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        playerPointer ??= _objectTable.LocalPlayer?.Address;
        if (playerPointer is null || playerPointer.Value == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var mgr = CharacterManager.Instance();
        if ((IntPtr)mgr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return (IntPtr)mgr->LookupBuddyByOwnerObject((BattleChara*)playerPointer.Value);
    }

    /// <summary>
    /// Defaults to the local player if no ptr is passed
    /// </summary>
    public IntPtr GetMinionOrMountPtrFromFramework(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        playerPointer ??= _objectTable.LocalPlayer?.Address;
        if (playerPointer is null || playerPointer.Value == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var player = GetCharacterFromObjectTableByAddressWhenAlreadyOnFrameworkThread(playerPointer.Value);
        if (player == null || player.ObjectIndex + 1 > 199)
        {
            return IntPtr.Zero;
        }

        var character = _objectTable[player.ObjectIndex + 1];

        return character != null && character.IsValid() ? character.Address : IntPtr.Zero;
    }

    /// <summary>
    /// Defaults to the local player if no ptr is passed
    /// </summary>
    public unsafe IntPtr GetPetPtrFromFramework(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        playerPointer ??= _objectTable.LocalPlayer?.Address;
        if (playerPointer is null || playerPointer.Value == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (_classJobIdsIgnoredForPets.Contains(((Character*)playerPointer.Value)->CharacterData.ClassJob))
        {
            return IntPtr.Zero;
        }

        var mgr = CharacterManager.Instance();
        if ((IntPtr)mgr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return (IntPtr)mgr->LookupPetByOwnerObject((BattleChara*)playerPointer.Value);
    }

    public unsafe nint GetObjectAddressByObjectKindAndAddress(API.Data.Enum.ObjectKind kind, nint address)
    {
        return kind switch
        {
            API.Data.Enum.ObjectKind.Player => address,
            API.Data.Enum.ObjectKind.Companion => GetCompanionPtrFromFramework(address),
            API.Data.Enum.ObjectKind.MinionOrMount => GetMinionOrMountPtrFromFramework(address),
            API.Data.Enum.ObjectKind.Pet => GetPetPtrFromFramework(address),
            _ => nint.Zero
        };
    }

    public uint GetHomeWorldId()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer?.HomeWorld.RowId ?? 0;
    }

    private uint GetWorldId()
    {
        EnsureIsOnFramework();
        return _objectTable.LocalPlayer!.CurrentWorld.RowId;
    }

    public unsafe bool TryGetCurrentPlotInfo(out int ward, out int plot)
    {
        var houseMan = HousingManager.Instance();
        if (houseMan != null)
        {
            ward = houseMan->GetCurrentWard();
            plot = houseMan->GetCurrentPlot();
            return true;
        }

        ward = default;
        plot = default;
        return false;
    }

    // borrowed from Lifestream
    public ResidentialAetheryteKind? GetResidentialAetheryteByTerritoryType(uint territoryType)
    {
        var t = _gameData.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
        if (t == null)
        {
            return null;
        }
        if (t.Value.PlaceNameRegion.RowId == 2402)
        {
            return ResidentialAetheryteKind.Kugane;
        }
        if (t.Value.PlaceNameRegion.RowId == 25)
        {
            return ResidentialAetheryteKind.Foundation;
        }
        if (t.Value.PlaceNameRegion.RowId == 23)
        {
            return ResidentialAetheryteKind.Gridania;
        }
        if (t.Value.PlaceNameRegion.RowId == 24)
        {
            return ResidentialAetheryteKind.Uldah;
        }
        if (t.Value.PlaceNameRegion.RowId == 22)
        {
            return ResidentialAetheryteKind.Limsa;
        }
        return null;
    }

    public unsafe LocationInfo GetMapData()
    {
        EnsureIsOnFramework();
        var agentMap = AgentMap.Instance();
        var houseMan = HousingManager.Instance();
        uint serverId = 0;
        if (_objectTable.LocalPlayer == null)
        {
            serverId = 0;
        }
        else serverId = _objectTable.LocalPlayer.CurrentWorld.RowId;
        uint mapId = agentMap == null ? 0 : agentMap->CurrentMapId;
        uint territoryId = agentMap == null ? 0 : agentMap->CurrentTerritoryId;
        uint divisionId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentDivision());
        uint wardId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentWard() + 1);
        uint houseId = 0;
        var tempHouseId = houseMan == null ? 0 : (houseMan->GetCurrentPlot());
        if (!houseMan->IsInside())
        {
            tempHouseId = 0;
        }
        if (tempHouseId < -1)
        {
            divisionId = tempHouseId == -127 ? 2 : (uint)1;
            tempHouseId = 100;
        }
        if (tempHouseId == -1)
        {
            tempHouseId = 0;
        }
        houseId = (uint)tempHouseId;
        if (houseId != 0)
        {
            territoryId = HousingManager.GetOriginalHouseTerritoryTypeId();
        }
        uint roomId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentRoom());

        return new LocationInfo()
        {
            ServerId = serverId,
            MapId = mapId,
            TerritoryId = territoryId,
            DivisionId = divisionId,
            WardId = wardId,
            HouseId = houseId,
            RoomId = roomId
        };
    }

    public unsafe void SetMarkerAndOpenMap(Vector3 position, Map map)
    {
        EnsureIsOnFramework();
        var agentMap = AgentMap.Instance();
        if (agentMap == null)
        {
            return;
        }
        agentMap->OpenMapByMapId(map.RowId);
        agentMap->SetFlagMapMarker(map.TerritoryType.RowId, map.RowId, position);
    }

    public async Task<LocationInfo> GetMapDataAsync()
    {
        return await RunOnFrameworkThread(GetMapData).ConfigureAwait(false);
    }

    public string? GetDataCenterNameForWorld(ushort worldId)
    {
        return WorldToDataCenterData.Value.TryGetValue(worldId, out var dataCenterName) ? dataCenterName : null;
    }

    public uint? GetDataCenterIdForWorld(ushort worldId)
    {

        return WorldToDataCenterIdData.Value.TryGetValue(worldId, out var dataCenterId) ? dataCenterId : null;
    }

    public unsafe ushort GetZoneId()
    {
        EnsureIsOnFramework();
        var readZoneId = ContentsReplayManager.Instance();
        return readZoneId != null ? readZoneId->ZoneInitPacket.Instance : (ushort)0;
    }

    public async Task<uint> GetWorldIdAsync()
    {
        return await RunOnFrameworkThread(GetWorldId).ConfigureAwait(false);
    }

    public async Task<uint> GetHomeWorldIdAsync()
    {
        return await RunOnFrameworkThread(GetHomeWorldId).ConfigureAwait(false);
    }

    public unsafe ulong GetContentIdForObjectAddressOnFramework(nint address)
    {
        EnsureIsOnFramework();
        if (address == nint.Zero || _pairedHandlersByContentId.Count == 0)
        {
            return 0;
        }
        for (int i = 0; i < 200; i += 2)
        {
            if (_objectTable[i] is not IBattleChara chara || chara.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc || !chara.IsValid())
            {
                continue;
            }

            nint playerAddress = chara.Address;
            if (playerAddress == nint.Zero)
            {
                continue;
            }

            ulong contentId = ((BattleChara*)playerAddress)->Character.ContentId;
            if (!_pairedHandlersByContentId.ContainsKey(contentId))
            {
                continue;
            }

            if (address == playerAddress)
            {
                return contentId;
            }

            var minionOrMount = _objectTable[chara.ObjectIndex + 1];
            if (minionOrMount != null && minionOrMount.IsValid() && minionOrMount.Address == address)
            {
                return contentId;
            }

            if (address == GetCompanionPtrFromFramework(playerAddress) || address == GetPetPtrFromFramework(playerAddress))
            {
                return contentId;
            }
        }

        return 0;
    }

    public void EnsureIsOnFramework()
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            throw new InvalidOperationException("Can only be run on Framework");
        }
    }

    public async Task RunOnFrameworkThread(System.Action act, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
        await _performanceCollector.LogPerformance(this, $"RunOnFramework:Act/{fileName}>{callerMember}:{callerLineNumber}", async () =>
        {
            if (_framework.IsInFrameworkUpdateThread)
            {
                act();
                return;
            }
            await _framework.RunOnFrameworkThread(act).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<T> RunOnFrameworkThread<T>(Func<T> func, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
        return await _performanceCollector.LogPerformance(this, $"RunOnFramework:Func<{typeof(T)}>/{fileName}>{callerMember}:{callerLineNumber}", async () =>
        {
            if (_framework.IsInFrameworkUpdateThread)
            {
                return func();
            }
            return await _framework.RunOnFrameworkThread(func).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting DalamudUtilService");
        _framework.Update += FrameworkOnUpdate;
        if (IsLoggedIn)
        {
            _classJobId = _objectTable.LocalPlayer!.ClassJob.RowId;
        }

        _logger.LogInformation("Started DalamudUtilService");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Stopping {type}", GetType());

        Mediator.UnsubscribeAll(this);
        _framework.Update -= FrameworkOnUpdate;
        return Task.CompletedTask;
    }

    public async Task<DrawWaitResult> WaitWhileCharacterIsDrawing(ILogger logger, GameObjectHandler handler, Guid redrawId, int timeOut = 5000, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = await RunOnFrameworkThread(() => handler.GetChareacterDrawStateFromFramework()).ConfigureAwait(false);
            if (state.State == DrawState.Ready)
            {
                return DrawWaitResult.Ready;
            }
            if (state.State == DrawState.Missing)
            {
                return DrawWaitResult.Missing;
            }

            TimeSpan remaining = TimeSpan.FromMilliseconds(timeOut) - timer.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                logger.LogWarning("[{redrawId}] Timed out waiting for {handler} condition: {condition}", redrawId, handler, state.Condition);

                return DrawWaitResult.TimedOut;
            }

            await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }
    }

    public async Task<DrawWaitResult> WaitWhileCharacterIsDrawing(ILogger logger, ulong contentId, API.Data.Enum.ObjectKind objectKind, Guid redrawId, int timeOut = 5000, CancellationToken ct = default, GameObjectHandler? charaHandler = null)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var drawState = await RunOnFrameworkThread(() =>
            {
                if (!TryGetPlayerAddressAndIndexOnFrameworkByContentId(contentId, out var ownerAddress, out _))
                {
                    return new CharacterDrawState(DrawState.Missing, DrawCondition.ObjectZero);
                }

                nint address = GetObjectAddressByObjectKindAndAddress(objectKind, ownerAddress);
                var state = GetCharacterDrawStateFromFramework(address, requireRenderable: objectKind == API.Data.Enum.ObjectKind.Player);
                _logger.LogTrace("[{guid}]- Draw state for {contentId}/{objectKind} is {state}", redrawId, contentId, objectKind, state);

                return state;
            }).ConfigureAwait(false);

            if (drawState.State == DrawState.Ready && charaHandler?.IsRedrawingViaIpcRequest != true)
            {
                return DrawWaitResult.Ready;
            }
            if (drawState.State == DrawState.Missing)
            {
                return DrawWaitResult.Missing;
            }

            TimeSpan remaining = TimeSpan.FromMilliseconds(timeOut) - timer.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                logger.LogWarning("[{redrawId}] Timed out waiting for {contentId}/{objectKind} condition: {condition}", redrawId, contentId, objectKind, drawState.Condition);

                return DrawWaitResult.TimedOut;
            }

            await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
        }
    }

    public API.Data.Enum.ObjectKind? GetOwnedObjectKindOnFramework(nint address)
    {
        if (!IsOnFrameworkThread || address == nint.Zero)
        {
            return null;
        }
        nint owner = GetPlayerPtr();
        if (owner == nint.Zero)
        {
            return null;
        }
        if (address == owner)
        {
            return API.Data.Enum.ObjectKind.Player;
        }
        if (address == GetPetPtrFromFramework(owner))
        {
            return API.Data.Enum.ObjectKind.Pet;
        }
        if (address == GetMinionOrMountPtrFromFramework(owner))
        {
            return API.Data.Enum.ObjectKind.MinionOrMount;
        }
        if (address == GetCompanionPtrFromFramework(owner))
        {
            return API.Data.Enum.ObjectKind.Companion;
        }

        return null;
    }

    public Vector2 WorldToScreen(IGameObject? obj)
    {
        if (obj == null)
        {
            return Vector2.Zero;
        }
        return _gameGui.WorldToScreen(obj.Position, out var screenPos) ? screenPos : Vector2.Zero;
    }

    public unsafe void OpenContextMenu(IntPtr agentPtr)
    {
        EnsureIsOnFramework();
        var agentContext = (AgentContext*)agentPtr;
        agentContext->OpenContextMenu();
    }

    public unsafe IGameObject? GetGameObjectByAddress(nint address)
    {
        EnsureIsOnFramework();
        if (address == nint.Zero)
        {
            return null;
        }

        int index = ((GameObject*)address)->ObjectIndex;
        var gameObject = _objectTable[index];

        return gameObject != null && gameObject.IsValid() ? gameObject : null;
    }

    public string? FindPlayerByNameHash(string ident)
    {
        return _frameworkCharacters.FirstOrDefault(p => p.Value.Ident == ident).Value.Name;
    }

    internal IEnumerable<string> GetVisiblePlayerIdents()
    {
        EnsureIsOnFramework();

        return _playersSeenThisFrame.Select(contentId => _frameworkCharacters[contentId].Ident).ToArray();
    }

    internal unsafe List<string> GetVisibleAllianceAndPartyMembers()
    {
        EnsureIsOnFramework();
        List<string> result = [];
        for (int i = 0; i < 200; i += 2)
        {
            if (_objectTable[i] is IBattleChara chara && chara.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc && chara.IsValid() && chara.Address != nint.Zero)
            {
                var pc = (BattleChara*)chara.Address;
                if (pc->IsAllianceMember || pc->IsPartyMember)
                {
                    result.Add(pc->Character.ContentId.ToString().GetHash256());
                }
            }
        }

        return result;
    }

    private void TargetPairByTargetType(Pair? pair, TargetType targetType)
    {
        if (_clientState.IsPvP)
        {
            return;
        }

        _ = RunOnFrameworkThread(() =>
        {
            IGameObject? objectToTarget = null;

            if (pair != null && TryGetPlayerAddressAndIndexOnFrameworkByContentId(pair.ContendId, out _, out var index))
            {
                objectToTarget = _objectTable[index];
            }

            switch (targetType)
            {
                case TargetType.Target:
                    _targetManager.Target = objectToTarget;
                    break;

                case TargetType.SoftTarget:
                    _targetManager.SoftTarget = objectToTarget;
                    break;

                case TargetType.FocusTarget:
                    _targetManager.FocusTarget = objectToTarget;
                    break;

                case TargetType.MouseOverTarget:
                    _targetManager.MouseOverTarget = objectToTarget;
                    break;
            }
        });
    }

    private unsafe void ScanFrameworkCharacters()
    {
        EnsureIsOnFramework();

        // swap the set before clearing so we don't have to recreate a new one
        (_playersSeenThisFrame, _playersSeenPreviousFrame) = (_playersSeenPreviousFrame, _playersSeenThisFrame);

        _playersSeenThisFrame.Clear();
        _frameworkCharacterChanges.Clear();

        var localPlayer = _objectTable.LocalPlayer;
        bool objectTableAvailable = localPlayer != null && !_condition[ConditionFlag.BetweenAreas] && !_condition[ConditionFlag.BetweenAreas51]
            && !(localPlayer.IsDead && _condition[ConditionFlag.BoundByDuty]);
        if (objectTableAvailable)
        {
            nint localPlayerAddress = localPlayer!.Address;
            for (int i = 0; i < 199; i += 2) // we only scan players here
            {
                if (_objectTable[i] is not IBattleChara chara || chara.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc || !chara.IsValid())
                {
                    continue;
                }

                nint address = chara.Address;
                if (address == nint.Zero || address == localPlayerAddress || _blockedCharacterHandler.IsCharacterBlocked(address, out _))
                {
                    continue;
                }

                ulong contentId = ((BattleChara*)address)->Character.ContentId;
                if (contentId == 0)
                {
                    continue;
                }

                _playersSeenThisFrame.Add(contentId);

                if (!_frameworkCharacters.ContainsKey(contentId))
                {
                    _frameworkCharacters.Add(contentId, (contentId.ToString().GetHash256(), chara.Name.ToString()));
                }
            }
        }

        // this is for the players we have not seen this frame and were in the previous (no longer visibile)
        foreach (var contentId in _playersSeenPreviousFrame)
        {
            if (!_playersSeenThisFrame.Contains(contentId) && _frameworkCharacters.TryGetValue(contentId, out var player))
            {
                _frameworkCharacterChanges.Add(new(contentId, player.Ident, player.Name, false));
            }
        }


        // this is for the players we have seen this frame and not the previous (now visibile)
        foreach (var contentId in _playersSeenThisFrame)
        {
            if (!_playersSeenPreviousFrame.Contains(contentId))
            {
                var player = _frameworkCharacters[contentId];
                _frameworkCharacterChanges.Add(new(contentId, player.Ident, player.Name, true));
            }
        }

        if (_frameworkCharacterChanges.Count > 0)
        {
            try
            {
                // send this to the PairManager to deal with
                FrameworkCharactersChanged?.Invoke(_frameworkCharacterChanges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Framework update failed");
            }
        }
    }

    private void UpdateGameObjectHandlers()
    {
        EnsureIsOnFramework();

        while (_handlersTrackedOnFrameworkQueue.TryDequeue(out var handlerUpdate))
        {
            var handler = handlerUpdate.Handler;

            if (handler.IsOwnedObject) // we add ourself, minion, mounts, etc.
            {
                if (handlerUpdate.TrackedOnFramework)
                {
                    if (!_ownedHandlers.Contains(handler))
                    {
                        _ownedHandlers.Add(handler);
                    }
                }
                else
                {
                    _ownedHandlers.Remove(handler);
                }

                continue;
            }

            if (handler.ContentId == 0)
            {
                if (handlerUpdate.TrackedOnFramework)
                {
                    _logger.LogWarning("Cannot track paired handler without a content ID: {handler}", handler);
                }

                continue;
            }

            if (handlerUpdate.TrackedOnFramework)
            {
                _pairedHandlersByContentId[handler.ContentId] = handler;
            }
            else if (_pairedHandlersByContentId.TryGetValue(handler.ContentId, out var registeredHandler) && ReferenceEquals(registeredHandler, handler))
            {
                _pairedHandlersByContentId.Remove(handler.ContentId);
            }
        }

        var localPlayer = _objectTable.LocalPlayer;
        bool canReadObjects = localPlayer != null && !_condition[ConditionFlag.BetweenAreas] && !_condition[ConditionFlag.BetweenAreas51]
            && !(localPlayer.IsDead && _condition[ConditionFlag.BoundByDuty]);

        foreach (var handler in _ownedHandlers)
        {
            handler.FrameworkUpdate(canReadObjects);
        }

        foreach (ulong contentId in _playersSeenPreviousFrame)
        {
            if (!_playersSeenThisFrame.Contains(contentId) && _pairedHandlersByContentId.TryGetValue(contentId, out var handler))
            {
                handler.Invalidate();
            }
        }

        if (canReadObjects)
        {
            foreach (ulong contentId in _playersSeenThisFrame)
            {
                if (_pairedHandlersByContentId.TryGetValue(contentId, out var handler))
                {
                    handler.FrameworkUpdate(true);
                }
            }
        }
    }

    private unsafe void FrameworkOnUpdate(IFramework framework)
    {
        bool isNormalFrameworkUpdate = DateTime.UtcNow < _delayedFrameworkUpdateCheck.AddSeconds(1);

        ScanFrameworkCharacters(); // check if we see/don't see players around us and let PairManager know
        UpdateGameObjectHandlers(); // update the game objects for the characters objects we are tracking

        // do all the other framework state transition stuff

        if (_clientState.IsGPosing && !IsInGpose)
        {
            _logger.LogDebug("Gpose start");
            IsInGpose = true;
            Mediator.Publish(new GposeStartMessage());
        }
        else if (!_clientState.IsGPosing && IsInGpose)
        {
            _logger.LogDebug("Gpose end");
            IsInGpose = false;
            Mediator.Publish(new GposeEndMessage());
        }

        if (_condition[ConditionFlag.InCombat] && !IsInCombat)
        {
            _logger.LogTrace("Combat start");
            IsInCombat = true;
            Mediator.Publish(new CombatOrPerformanceStartMessage());
            Mediator.Publish(new HaltScanMessage(nameof(IsInCombatOrPerforming)));
        }
        else if (!_condition[ConditionFlag.InCombat] && IsInCombat)
        {
            _logger.LogTrace("Combat end");
            IsInCombat = false;
            Mediator.Publish(new CombatOrPerformanceEndMessage());
            Mediator.Publish(new ResumeScanMessage(nameof(IsInCombatOrPerforming)));
        }

        if (_condition[ConditionFlag.Performing] && !IsPerforming)
        {
            _logger.LogTrace("Performance start");
            IsPerforming = true;
            Mediator.Publish(new CombatOrPerformanceStartMessage());
            Mediator.Publish(new HaltScanMessage(nameof(IsInCombatOrPerforming)));
        }
        else if (!_condition[ConditionFlag.Performing] && IsPerforming)
        {
            _logger.LogTrace("Performance end");
            IsPerforming = false;
            Mediator.Publish(new CombatOrPerformanceEndMessage());
            Mediator.Publish(new ResumeScanMessage(nameof(IsInCombatOrPerforming)));
        }

        if (_condition[ConditionFlag.BoundByDuty] && !IsBoundByDuty)
        {
            _logger.LogDebug("Duty start");
            IsBoundByDuty = true;
            if (_configService.Current.DisableSyncDuringDuty)
            {
                Mediator.Publish(new PauseSyncMessage());
                Mediator.Publish(new HaltScanMessage(nameof(IsBoundByDuty)));
            }
        }
        else if (!_condition[ConditionFlag.BoundByDuty] && IsBoundByDuty)
        {
            _logger.LogDebug("Duty end");
            IsBoundByDuty = false;
            if (_configService.Current.DisableSyncDuringDuty)
            {
                Mediator.Publish(new ResumeSyncMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(IsBoundByDuty)));
            }
        }

        if (_clientState.IsPvPExcludingDen && !IsBoundByPvP)
        {
            _logger.LogDebug("PvP start");
            IsBoundByPvP = true;
            if (_configService.Current.DisableSyncDuringPvP)
            {
                Mediator.Publish(new PauseSyncMessage());
                Mediator.Publish(new HaltScanMessage(nameof(IsBoundByPvP)));
            }
        }
        else if (!_clientState.IsPvPExcludingDen && IsBoundByPvP)
        {
            _logger.LogDebug("PvP end");
            IsBoundByPvP = false;
            if (_configService.Current.DisableSyncDuringPvP)
            {
                Mediator.Publish(new ResumeSyncMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(IsBoundByPvP)));
            }
        }

        if ((_condition[ConditionFlag.WatchingCutscene] || _condition[ConditionFlag.OccupiedInCutSceneEvent]) && !IsInCutscene)
        {
            _logger.LogDebug("Cutscene start");
            IsInCutscene = true;
            Mediator.Publish(new CutsceneStartMessage());
            Mediator.Publish(new HaltScanMessage(nameof(IsInCutscene)));
        }
        else if (!(_condition[ConditionFlag.WatchingCutscene] || _condition[ConditionFlag.OccupiedInCutSceneEvent]) && IsInCutscene)
        {
            _logger.LogDebug("Cutscene end");
            IsInCutscene = false;
            Mediator.Publish(new CutsceneEndMessage());
            Mediator.Publish(new ResumeScanMessage(nameof(IsInCutscene)));
        }

        if (IsInCutscene)
        {
            Mediator.Publish(new CutsceneFrameworkUpdateMessage());
            return;
        }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            var zone = _clientState.TerritoryType;
            var world = _objectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;

            if (_lastZone != zone || _lastWorld != world)
            {
                _lastZone = zone;
                _lastWorld = world;
                if (!_sentBetweenAreas)
                {
                    _logger.LogDebug("Zone switch start");
                    _sentBetweenAreas = true;
                    Mediator.Publish(new ZoneSwitchStartMessage());
                    Mediator.Publish(new HaltScanMessage(nameof(ConditionFlag.BetweenAreas)));
                }
            }

            return;
        }

        if (_sentBetweenAreas)
        {
            _logger.LogDebug("Zone switch end");
            _sentBetweenAreas = false;
            Mediator.Publish(new ZoneSwitchEndMessage());
            Mediator.Publish(new ResumeScanMessage(nameof(ConditionFlag.BetweenAreas)));
        }

        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer != null)
        {
            _classJobId = localPlayer.ClassJob.RowId;
            _playerName = localPlayer.Name.TextValue;
        }

        if (!((_configService.Current.AutoPauseDataApplicationWhenPerforming && IsPerforming) || IsInCombat))
        {
            Mediator.Publish(new FrameworkUpdateMessage());
        }

        Mediator.Publish(new PriorityFrameworkUpdateMessage());

        if (isNormalFrameworkUpdate)
        {
            return;
        }

        if (localPlayer != null && IsLoggedIn)
        {
            var frameworkInstance = Framework.Instance();
            try
            {
                FPSCounter = frameworkInstance->FrameRate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        if (localPlayer != null && !IsLoggedIn)
        {
            _logger.LogDebug("Logged in");
            IsLoggedIn = true;
            _lastZone = _clientState.TerritoryType;
            _cid = RebuildCID();
            Mediator.Publish(new DalamudLoginMessage());
        }
        else if (localPlayer == null && IsLoggedIn)
        {
            _logger.LogDebug("Logged out");
            _frameworkCharacters.Clear();
            IsLoggedIn = false;
            _playerName = null;
            Mediator.Publish(new DalamudLogoutMessage());
        }

        if (_gameConfig != null
            && _gameConfig.TryGet(Dalamud.Game.Config.SystemConfigOption.LodType_DX11, out bool lodEnabled))
        {
            IsLodEnabled = lodEnabled;
        }

        if ((_configService.Current.AutoPauseDataApplicationWhenPerforming && IsPerforming) || IsInCombat)
        {
            Mediator.Publish(new FrameworkUpdateMessage());
        }

        Mediator.Publish(new DelayedFrameworkUpdateMessage());

        _delayedFrameworkUpdateCheck = DateTime.UtcNow;

    }
}
