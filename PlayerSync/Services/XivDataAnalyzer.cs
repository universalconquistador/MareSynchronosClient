using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation;
using FFXIVClientStructs.Havok.Common.Base.Container.Array;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.GameModel;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace MareSynchronos.Services;

public sealed class XivDataAnalyzer
{
    private readonly ILogger<XivDataAnalyzer> _logger;
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataStorageService _configService;
    private readonly List<string> _failedCalculatedTris = [];

    public XivDataAnalyzer(ILogger<XivDataAnalyzer> logger, FileCacheManager fileCacheManager,
        XivDataStorageService configService)
    {
        _logger = logger;
        _fileCacheManager = fileCacheManager;
        _configService = configService;
    }

    public unsafe Dictionary<string, List<ushort>>? GetSkeletonBoneIndices(GameObjectHandler handler)
    {
        if (handler.Address == nint.Zero) return null;
        var chara = (CharacterBase*)(((Character*)handler.Address)->GameObject.DrawObject);
        if (chara->GetModelType() != CharacterBase.ModelType.Human) return null;
        var resHandles = chara->Skeleton->SkeletonResourceHandles;
        Dictionary<string, List<ushort>> outputIndices = [];
        try
        {
            for (int i = 0; i < chara->Skeleton->PartialSkeletonCount; i++)
            {
                var handle = *(resHandles + i);
                _logger.LogTrace("Iterating over SkeletonResourceHandle #{i}:{x}", i, ((nint)handle).ToString("X"));
                if ((nint)handle == nint.Zero) continue;
                var curBones = handle->BoneCount;
                // this is unrealistic, the filename shouldn't ever be that long
                if (handle->FileName.Length > 1024) continue;
                var skeletonName = handle->FileName.ToString();
                if (string.IsNullOrEmpty(skeletonName)) continue;
                outputIndices[skeletonName] = new();
                for (ushort boneIdx = 0; boneIdx < curBones; boneIdx++)
                {
                    var boneName = handle->HavokSkeleton->Bones[boneIdx].Name.String;
                    if (boneName == null) continue;
                    outputIndices[skeletonName].Add((ushort)(boneIdx + 1));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not process skeleton data");
        }

        return (outputIndices.Count != 0 && outputIndices.Values.All(u => u.Count > 0)) ? outputIndices : null;
    }

    public unsafe Dictionary<string, List<ushort>>? GetBoneIndicesFromPap(string hash)
    {
        if (_configService.Current.BonesDictionary.TryGetValue(hash, out var bones))
            return bones;

        var cacheEntity = _fileCacheManager.GetFileCacheByHash(hash);
        if (cacheEntity == null)
            return null;

        using BinaryReader reader = new BinaryReader(File.Open(cacheEntity.ResolvedFilepath, FileMode.Open, FileAccess.Read, FileShare.Read));

        // most of this shit is from vfxeditor, surely nothing will change in the pap format :copium:
        reader.ReadInt32(); // ignore
        reader.ReadInt32(); // ignore
        reader.ReadInt16(); // num animations
        reader.ReadInt16(); // model id
        var type = reader.ReadByte(); // type
        if (type != 0)
            return null; // it's not human, ignore

        reader.ReadByte(); // variant
        reader.ReadInt32(); // ignore
        var havokPosition = reader.ReadInt32();
        var footerPosition = reader.ReadInt32();
        var havokDataSize = footerPosition - havokPosition;
        reader.BaseStream.Position = havokPosition;
        var havokData = reader.ReadBytes(havokDataSize);
        if (havokData.Length <= 8)
            return null; // havok data isnt there. 

        var output = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var tempHavokDataPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + ".hkx";
        var tempHavokDataPathAnsi = Marshal.StringToHGlobalAnsi(tempHavokDataPath);

        try
        {
            File.WriteAllBytes(tempHavokDataPath, havokData);

            var loadoptions = stackalloc hkSerializeUtil.LoadOptions[1];
            loadoptions->TypeInfoRegistry = hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry();
            loadoptions->ClassNameRegistry = hkBuiltinTypeRegistry.Instance()->GetClassNameRegistry();
            loadoptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
            {
                Storage = (int)(hkSerializeUtil.LoadOptionBits.Default)
            };

            var resource = hkSerializeUtil.LoadFromFile((byte*)tempHavokDataPathAnsi, null, loadoptions);
            if (resource == null)
                throw new InvalidOperationException("Resource was null after loading");

            var rootLevelName = @"hkRootLevelContainer"u8;
            fixed (byte* n1 = rootLevelName)
            {
                var container = (hkRootLevelContainer*)resource->GetContentsPointer(n1, hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry());
                if (container == null)
                {
                    _logger.LogWarning("RootLevelContainer was null in {path}", tempHavokDataPath);
                    return null;
                }

                var animationName = @"hkaAnimationContainer"u8;
                fixed (byte* n2 = animationName)
                {
                    var animContainer = (hkaAnimationContainer*)container->findObjectByName(n2, prevObject: null);
                    if (animContainer == null)
                    {
                        _logger.LogWarning("Animation container was null in {path}", tempHavokDataPath);
                        return null;
                    }

                    for (int i = 0; i < animContainer->Bindings.Length; i++)
                    {
                        var bindingPtr = animContainer->Bindings[i].ptr;
                        if (bindingPtr == null)
                        {
                            _logger.LogWarning("BindingPointer {i} was null in {path}", i, tempHavokDataPath);
                            continue;
                        }

                        var binding = *bindingPtr;

                        // FIXED: use hkArray<short>, cast to ushort when adding
                        hkArray<short>* boneTransformPtr = &binding.TransformTrackToBoneIndices;
                        if (boneTransformPtr == null || boneTransformPtr->Length <= 0)
                        {
                            _logger.LogWarning("Invalid,Empty,or NULL bones in Binding Pointer {i}", i);
                            continue;
                        }

                        var boneTransform = *boneTransformPtr;

                        string name = binding.OriginalSkeletonName.String ?? $"Unknown_{i}";
                        if (!output.ContainsKey(name))
                            output[name] = new List<ushort>();

                        for (int boneIdx = 0; boneIdx < boneTransform.Length; boneIdx++)
                        {
                            try
                            {
                                output[name].Add((ushort)boneTransform[boneIdx]);
                            }
                            catch
                            {
                                _logger.LogWarning("Bone index is invalid: {boneIdx} in binding {i}", boneIdx, i);
                                break;
                            }
                        }

                        output[name].Sort();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Havok file not loadable in {path}", tempHavokDataPath);
        }
        finally
        {
            Marshal.FreeHGlobal(tempHavokDataPathAnsi);
            File.Delete(tempHavokDataPath);
        }

        _configService.Current.BonesDictionary[hash] = output;
        _configService.Save();

        return output;
    }


    public async Task<long> GetTrianglesByHash(string hash)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(hash, out var cachedTris) && cachedTris > 0)
            return cachedTris;

        if (_failedCalculatedTris.Contains(hash, StringComparer.Ordinal))
            return 0;

        var path = _fileCacheManager.GetFileCacheByHash(hash);
        if (path == null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            return 0;

        var filePath = path.ResolvedFilepath;

        try
        {
            _logger.LogDebug("Detected Model File {path}, calculating Tris", filePath);
            var file = new MdlFile(filePath);
            if (file.LodCount <= 0)
            {
                _failedCalculatedTris.Add(hash);
                _configService.Current.TriangleDictionary[hash] = 0;
                _configService.Save();
                return 0;
            }

            long tris = 0;
            for (int i = 0; i < file.LodCount; i++)
            {
                try
                {
                    var meshIdx = file.Lods[i].MeshIndex;
                    var meshCnt = file.Lods[i].MeshCount;
                    tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not load lod mesh {mesh} from path {path}", i, filePath);
                    continue;
                }

                if (tris > 0)
                {
                    _logger.LogDebug("TriAnalysis: {filePath} => {tris} triangles", filePath, tris);
                    _configService.Current.TriangleDictionary[hash] = tris;
                    _configService.Save();
                    break;
                }
            }

            return tris;
        }
        catch (Exception e)
        {
            _failedCalculatedTris.Add(hash);
            _configService.Current.TriangleDictionary[hash] = 0;
            _configService.Save();
            _logger.LogWarning(e, "Could not parse file {file}", filePath);
            return 0;
        }
    }
}
