using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Brio.API;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerBrio : IIpcCaller
{
    private readonly ILogger<IpcCallerBrio> _logger;
    private readonly DalamudUtilService _dalamudUtilService;

    private readonly ApiVersion _apiVersion;

    private readonly SpawnActor _spawnActor;
    private readonly DespawnActor _despawnActor;
    private readonly SetModelTransform _setModelTransform;
    private readonly GetModelTransform _getModelTransform;

    private readonly GetPoseAsJson _getPoseAsJson;
    private readonly LoadPoseFromJson _setPoseFromJson;

    private readonly FreezeActor _freezeActor;
    private readonly FreezePhysics _freezePhysics;

    public bool APIAvailable { get; private set; }

    public IpcCallerBrio(ILogger<IpcCallerBrio> logger, IDalamudPluginInterface dalamudPluginInterface, DalamudUtilService dalamudUtilService)
    {
        _logger = logger;
        _dalamudUtilService = dalamudUtilService;

        _apiVersion = new ApiVersion(dalamudPluginInterface);
        _spawnActor = new SpawnActor(dalamudPluginInterface);
        _despawnActor = new DespawnActor(dalamudPluginInterface);

        _setModelTransform = new SetModelTransform(dalamudPluginInterface);
        _getModelTransform = new GetModelTransform(dalamudPluginInterface);

        _getPoseAsJson = new GetPoseAsJson(dalamudPluginInterface);
        _setPoseFromJson = new LoadPoseFromJson(dalamudPluginInterface);

        _freezeActor = new FreezeActor(dalamudPluginInterface);
        _freezePhysics = new FreezePhysics(dalamudPluginInterface);

        CheckAPI();
    }

    public void CheckAPI()
    {
        try
        {
            var version = _apiVersion.Invoke();
            APIAvailable = (version.Item1 == 3 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public ICharacter? SpawnActor()
    {
        _dalamudUtilService.EnsureIsOnFramework();
        if (!APIAvailable)
        {
            return null;
        }
        _logger.LogDebug("Spawning Brio Actor");

        return _spawnActor.Invoke(Brio.API.Enums.SpawnFlags.Default, true) as ICharacter;
    }

    public bool DespawnActor(ICharacter actor)
    {
        _dalamudUtilService.EnsureIsOnFramework();

        return APIAvailable && _despawnActor.Invoke(actor);
    }

    public bool ApplyTransform(ICharacter actor, WorldData data)
    {
        _dalamudUtilService.EnsureIsOnFramework();

        return APIAvailable && _setModelTransform.Invoke(actor,
            new(data.PositionX, data.PositionY, data.PositionZ),
            new(data.RotationX, data.RotationY, data.RotationZ, data.RotationW),
            new(data.ScaleX, data.ScaleY, data.ScaleZ), false);
    }

    public WorldData GetTransform(ICharacter actor)
    {
        _dalamudUtilService.EnsureIsOnFramework();
        if (!APIAvailable)
        {
            return default;
        }
        var data = _getModelTransform.Invoke(actor);
        if (data.Item1 == null || data.Item2 == null || data.Item3 == null)
        {
            return default;
        }

        return new WorldData()
        {
            PositionX = data.Item1.Value.X,
            PositionY = data.Item1.Value.Y,
            PositionZ = data.Item1.Value.Z,
            RotationX = data.Item2.Value.X,
            RotationY = data.Item2.Value.Y,
            RotationZ = data.Item2.Value.Z,
            RotationW = data.Item2.Value.W,
            ScaleX = data.Item3.Value.X,
            ScaleY = data.Item3.Value.Y,
            ScaleZ = data.Item3.Value.Z
        };
    }

    public string? GetPose(ICharacter actor)
    {
        _dalamudUtilService.EnsureIsOnFramework();

        return APIAvailable ? _getPoseAsJson.Invoke(actor) : null;
    }

    public bool SetPose(ICharacter actor, string pose)
    {
        _dalamudUtilService.EnsureIsOnFramework();
        if (!APIAvailable)
        {
            return false;
        }
        var applicablePose = JsonNode.Parse(pose);
        if (applicablePose == null)
        {
            return false;
        }
        var currentPose = GetPose(actor);
        if (string.IsNullOrEmpty(currentPose))
        {
            return false;
        }
        applicablePose["ModelDifference"] = JsonNode.Parse(JsonNode.Parse(currentPose)!["ModelDifference"]!.ToJsonString());
        string poseJson = applicablePose.ToJsonString();
        _freezeActor.Invoke(actor);
        _freezePhysics.Invoke();

        return _setPoseFromJson.Invoke(actor, poseJson, false);
    }

    public void Dispose()
    {
    }
}
