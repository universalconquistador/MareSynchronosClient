using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller
{
    private readonly IDalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;
    private readonly RedrawManager _redrawManager;
    private bool _shownPenumbraUnavailable = false;
    private string? _penumbraModDirectory;
    public string? ModDirectory
    {
        get => _penumbraModDirectory;
        private set
        {
            if (!string.Equals(_penumbraModDirectory, value, StringComparison.Ordinal))
            {
                _penumbraModDirectory = value;
                _mareMediator.Publish(new PenumbraDirectoryChangedMessage(_penumbraModDirectory));
            }
        }
    }

    private readonly EventSubscriber _penumbraDispose;
    private readonly EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;
    private readonly EventSubscriber _penumbraInit;
    private readonly EventSubscriber<ModSettingChange, Guid, string, bool> _penumbraModSettingChanged;
    private readonly EventSubscriber<nint, int> _penumbraObjectIsRedrawn;
    private readonly AddTemporaryMod _penumbraAddTemporaryMod;
    private readonly AssignTemporaryCollection _penumbraAssignTemporaryCollection;
    private readonly ConvertTextureFile _penumbraConvertTextureFile;
    private readonly CreateTemporaryCollection _penumbraCreateNamedTemporaryCollection;
    private readonly GetEnabledState _penumbraEnabled;
    private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;
    private readonly RedrawObject _penumbraRedraw;
    private readonly DeleteTemporaryCollection _penumbraRemoveTemporaryCollection;
    private readonly RemoveTemporaryMod _penumbraRemoveTemporaryMod;
    private readonly GetModDirectory _penumbraResolveModDir;
    private readonly GetModPath _penumbraGetModPath;
    private readonly GetModList _penumbraGetModList;
    private readonly ResolvePlayerPathsAsync _penumbraResolvePaths;
    private readonly GetGameObjectResourcePaths _penumbraResourcePaths;

    public IpcCallerPenumbra(ILogger<IpcCallerPenumbra> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator, RedrawManager redrawManager) : base(logger, mareMediator)
    {
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;
        _redrawManager = redrawManager;
        _penumbraInit = Initialized.Subscriber(pi, PenumbraInit);
        _penumbraDispose = Disposed.Subscriber(pi, PenumbraDispose);
        _penumbraResolveModDir = new GetModDirectory(pi);
        _penumbraGetModList = new GetModList(pi);
        _penumbraGetModPath = new GetModPath(pi);
        _penumbraRedraw = new RedrawObject(pi);
        _penumbraObjectIsRedrawn = GameObjectRedrawn.Subscriber(pi, RedrawEvent);
        _penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pi);
        _penumbraRemoveTemporaryMod = new RemoveTemporaryMod(pi);
        _penumbraAddTemporaryMod = new AddTemporaryMod(pi);
        _penumbraCreateNamedTemporaryCollection = new CreateTemporaryCollection(pi);
        _penumbraRemoveTemporaryCollection = new DeleteTemporaryCollection(pi);
        _penumbraAssignTemporaryCollection = new AssignTemporaryCollection(pi);
        _penumbraResolvePaths = new ResolvePlayerPathsAsync(pi);
        _penumbraEnabled = new GetEnabledState(pi);
        _penumbraModSettingChanged = ModSettingChanged.Subscriber(pi, (change, arg1, arg, b) =>
        {
            if (change == ModSettingChange.EnableState)
                _mareMediator.Publish(new PenumbraModSettingChangedMessage());
        });
        _penumbraConvertTextureFile = new ConvertTextureFile(pi);
        _penumbraResourcePaths = new GetGameObjectResourcePaths(pi);
        _penumbraGameObjectResourcePathResolved = GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);

        CheckAPI();
        CheckModDirectory();

        Mediator.Subscribe<PenumbraRedrawCharacterMessage>(this, (msg) =>
        {
            _penumbraRedraw.Invoke(msg.Character.ObjectIndex, RedrawType.AfterGPose);
        });

        Mediator.Subscribe<DalamudLoginMessage>(this, (msg) => _shownPenumbraUnavailable = false);
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        bool penumbraAvailable = false;
        try
        {
            var penumbraVersion = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Penumbra", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0));
            penumbraAvailable = penumbraVersion >= new Version(1, 2, 0, 22);
            try
            {
                penumbraAvailable &= _penumbraEnabled.Invoke();
            }
            catch
            {
                penumbraAvailable = false;
            }
            _shownPenumbraUnavailable = _shownPenumbraUnavailable && !penumbraAvailable;
            APIAvailable = penumbraAvailable;
        }
        catch
        {
            APIAvailable = penumbraAvailable;
        }
        finally
        {
            if (!penumbraAvailable && !_shownPenumbraUnavailable)
            {
                _shownPenumbraUnavailable = true;
                _mareMediator.Publish(new NotificationMessage("Penumbra inactive",
                    "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use PlayerSync. If you just updated Penumbra, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetMods()
    {
        if (!APIAvailable) return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return _penumbraGetModList.Invoke();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not read the Penumbra mod list");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public void CheckModDirectory()
    {
        if (!APIAvailable)
        {
            ModDirectory = string.Empty;
        }
        else
        {
            ModDirectory = _penumbraResolveModDir!.Invoke().ToLowerInvariant();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        //_redrawManager.Cancel();

        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
    }

    public Guid? AssignTemporaryCollection(ILogger logger, Guid collName, int idx)
    {
        if (!APIAvailable)
        {
            return null;
        }

        var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx, forceAssignment: true);
        logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, retAssign);

        return collName;
    }

    public bool AssignTemporaryCollectionOnFramework(Guid collId, ushort index)
    {
        _dalamudUtil.EnsureIsOnFramework();

        return APIAvailable && _penumbraAssignTemporaryCollection.Invoke(collId, index, forceAssignment: true) == PenumbraApiEc.Success;
    }

    public async Task ConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
    {
        if (!APIAvailable) return;

        _mareMediator.Publish(new HaltScanMessage(nameof(ConvertTextureFiles)));
        int currentTexture = 0;
        foreach (var texture in textures)
        {
            if (token.IsCancellationRequested) break;

            progress.Report((texture.Key, ++currentTexture));

            logger.LogInformation("Converting Texture {path} to {type}", texture.Key, TextureType.Bc7Tex);
            var convertTask = _penumbraConvertTextureFile.Invoke(texture.Key, texture.Key, TextureType.Bc7Tex, mipMaps: true);
            await convertTask.ConfigureAwait(false);
            if (convertTask.IsCompletedSuccessfully && texture.Value.Any())
            {
                foreach (var duplicatedTexture in texture.Value)
                {
                    logger.LogInformation("Migrating duplicate {dup}", duplicatedTexture);
                    try
                    {
                        File.Copy(texture.Key, duplicatedTexture, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to copy duplicate {dup}", duplicatedTexture);
                    }
                }
            }
        }
        _mareMediator.Publish(new ResumeScanMessage(nameof(ConvertTextureFiles)));

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var player = _dalamudUtil.GetPlayerCharacter();
            if (player != null)
            {
                _penumbraRedraw.Invoke(player.ObjectIndex, setting: RedrawType.Redraw);
            }
        }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateTemporaryCollectionAsync(ILogger logger, string uid)
    {
        if (!APIAvailable) return Guid.Empty;

        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var collectionName = $"Mare_{uid}";

            // New requirement in API6: provide an identity string.
            var identity = "PlayerSync";

            var ec = _penumbraCreateNamedTemporaryCollection.Invoke(identity, collectionName, out var collId);

            if (ec != PenumbraApiEc.Success || collId == Guid.Empty)
            {
                logger.LogError("CreateTemporaryCollection failed: {Ec} (identity: {Identity}, name: {Name})",
                    ec, identity, collectionName);
                return Guid.Empty;
            }

            logger.LogTrace("Created Temp Collection {Name}, GUID: {Id}", collectionName, collId);
            return collId;
        }).ConfigureAwait(false);
    }

    public Guid CreateTemporaryCollectionOnFramework(ILogger logger, string uid)
    {
        _dalamudUtil.EnsureIsOnFramework();
        if (!APIAvailable)
        {
            return Guid.Empty;
        }
        var ec = _penumbraCreateNamedTemporaryCollection.Invoke("PlayerSync", $"Mare_{uid}", out var collId);
        if (ec != PenumbraApiEc.Success || collId == Guid.Empty)
        {
            logger.LogError("CreateTemporaryCollection failed: {Ec} for {Uid}", ec, uid);

            return Guid.Empty;
        }

        return collId;
    }

    public Dictionary<string, HashSet<string>>? GetCharacterDataOnFramework(ushort index)
    {
        _dalamudUtil.EnsureIsOnFramework();

        return APIAvailable ? _penumbraResourcePaths.Invoke(index)[0] : null;
    }


    public string GetMetaManipulationsOnFramework()
    {
        if (!APIAvailable)
        {
            return string.Empty;
        }

        try
        {
            return _penumbraGetMetaManipulations.Invoke();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Penumbra IPC failed!");

            return string.Empty;
        }
    }

    public unsafe bool RedrawOnFramework(ILogger logger, Guid applicationId, nint address)
    {
        _dalamudUtil.EnsureIsOnFramework();
        if (!APIAvailable || _dalamudUtil.IsZoning || address == nint.Zero)
        {
            return false;
        }

        var chara = (Character*)address;
        var idx = chara->ObjectIndex;
        _penumbraRedraw!.Invoke(idx, setting: RedrawType.Redraw);
        return true;
    }

    public void RemoveTemporaryCollectionOnFramework(ILogger logger, Guid applicationId, Guid collId)
    {
        _dalamudUtil.EnsureIsOnFramework();
        if (!APIAvailable || collId == Guid.Empty)
        {
            return;
        }
        var result = _penumbraRemoveTemporaryCollection.Invoke(collId);
        logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {result}", applicationId, result);
    }

    public void SetTemporaryModsOnFramework(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths, string? uid = null)
    {
        _dalamudUtil.EnsureIsOnFramework();
        if (!APIAvailable)
        {
            return;
        }
        string modKey = uid == null ? "MareChara_Files" : $"PS_{uid}_Files";
        _penumbraRemoveTemporaryMod.Invoke(modKey, collId, 0);
        var result = _penumbraAddTemporaryMod.Invoke(modKey, collId, modPaths, string.Empty, 0);
        logger.LogTrace("[{applicationId}] Setting temp files mod for {collId}, Success: {result}", applicationId, collId, result);
    }

    public async Task<(string[] forward, string[][] reverse)> ResolvePathsAsync(string[] forward, string[] reverse)
    {
        return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(false);
    }

    public void SetManipulationDataOnFramework(ILogger logger, Guid applicationId, Guid collId, string manipulationData, string? uid = null)
    {
        if (!APIAvailable)
        {
            return;
        }

        string modKey = uid == null ? "MareChara_Meta" : $"PS_{uid}_Meta";

        //logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
        var retAdd = _penumbraAddTemporaryMod.Invoke(modKey, collId, [], manipulationData, 0);
        logger.LogTrace("[{applicationId}] Setting temp meta mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
    }

    private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
    {
        _mareMediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, false));
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            _mareMediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
            if (_dalamudUtil.IsOnFrameworkThread && arg1.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                ulong contentId = _dalamudUtil.GetContentIdForObjectAddressOnFramework(ptr);
                if (contentId != 0)
                {
                    _mareMediator.Publish(new PenumbraSoundResourceLoadMessage(contentId, arg2));
                }
            }
        }
    }

    private void PenumbraDispose()
    {
        //_redrawManager.Cancel();
        _mareMediator.Publish(new PenumbraDisposedMessage());
    }

    private void PenumbraInit()
    {
        APIAvailable = true;
        ModDirectory = _penumbraResolveModDir.Invoke();
        _mareMediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke(0, setting: RedrawType.Redraw);
    }
}
