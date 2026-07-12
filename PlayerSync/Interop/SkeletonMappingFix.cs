using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.Havok.Animation.Mapper;
using FFXIVClientStructs.Havok.Animation.Animation;
using FFXIVClientStructs.Havok.Common.Base.Container.Array;
using Dalamud.Utility.Signatures;

namespace PlayerSync.Interop;

internal unsafe class SkeletonMappingFix : IHostedService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IGameInteropProvider _gameInteropProvider;

    private delegate void SetupSkeletonMappingDelegate(hkaSkeletonMapper* skeletonMapper, hkaAnimationBinding* animationBinding, hkArray<short>* srcBoneToTrackIndices, hkArray<short>* dstBoneToTrackIndices, hkArray<short>* dstTrackToBoneIndices);
    [Signature("4C 89 4C 24 ?? 4C 89 44 24 ?? 55 53 56", DetourName = nameof(SetupSkeletonMappingDetour))]
    private readonly Hook<SetupSkeletonMappingDelegate>? _setupSkeletonMappingHook;

    [Signature("48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 41 56 48 83 EC 20 8B 74 24 50 49 8B F8 45 8B 40 0C")]
    private readonly delegate* unmanaged<int*, void*, void*, int, int, void> _hkArrayUtilReserve;

    private const string _globalHavokAllocatorSig = "48 8D 15 ?? ?? ?? ?? 45 8B CF 48 8D 4D 68";
    private readonly nint _globalHavokAllocator; // hkMemoryAllocator*

    public SkeletonMappingFix(ILogger<SkeletonMappingFix> logger, IGameInteropProvider gameInteropProvider, ISigScanner sigScanner)
    {
        _logger = logger;
        _gameInteropProvider = gameInteropProvider;

        _gameInteropProvider.InitializeFromAttributes(this);
        _globalHavokAllocator = sigScanner.GetStaticAddressFromSig(_globalHavokAllocatorSig);
    }

    private void SetupSkeletonMappingDetour(hkaSkeletonMapper* skeletonMapper, hkaAnimationBinding* animationBinding, hkArray<short>* srcBoneToTrackIndices, hkArray<short>* dstBoneToTrackIndices, hkArray<short>* dstTrackToBoneIndices)
    {
        if (ResizeIfNecessary(skeletonMapper, animationBinding, srcBoneToTrackIndices))
        {
            _setupSkeletonMappingHook!.Original!.Invoke(skeletonMapper, animationBinding, srcBoneToTrackIndices, dstBoneToTrackIndices, dstTrackToBoneIndices);
        }
    }

    // Returns false (and skips the original call) only for a malformed binding with a negative bone index.
    private bool ResizeIfNecessary(hkaSkeletonMapper* skeletonMapper, hkaAnimationBinding* animationBinding, hkArray<short>* srcBoneToTrackIndices)
    {
        if (skeletonMapper == null || animationBinding == null || srcBoneToTrackIndices == null)
        {
            return true;
        }

        var skeleton = skeletonMapper->Mapping.SkeletonA.ptr;
        if (skeleton == null)
        {
            return true;
        }

        // The game writes each track index to srcBoneToTrackIndices[boneIndex], indexing by the VALUE from the
        // binding's TransformTrackToBoneIndices -- so the buffer must fit max(boneIndex) + 1, not the track count
        // (independent, both untrusted). A negative value writes before the buffer and no growth can make it safe.
        int skeletonBoneCount = skeleton->Bones.Length;
        var trackToBone = animationBinding->TransformTrackToBoneIndices;
        int maxBoneIndex = -1;
        for (int i = 0; i < trackToBone.Length; i++)
        {
            int boneIndex = trackToBone[i];
            if (boneIndex < 0)
            {
                _logger.LogWarning("Skipping malformed animation binding with negative bone index {index}.", boneIndex);
                return false;
            }
            if (boneIndex > maxBoneIndex) maxBoneIndex = boneIndex;
        }

        if (maxBoneIndex >= skeletonBoneCount)
        {
            _logger.LogWarning("Fixing binding issue!");
            int successFlag = 0;
            _hkArrayUtilReserve(&successFlag, (void*)_globalHavokAllocator, srcBoneToTrackIndices, maxBoneIndex + 1, sizeof(short));
        }

        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _setupSkeletonMappingHook?.Enable();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _setupSkeletonMappingHook?.Disable();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _setupSkeletonMappingHook?.Dispose();
    }
}
