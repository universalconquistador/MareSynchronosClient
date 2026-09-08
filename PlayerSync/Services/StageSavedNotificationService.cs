using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace MareSynchronos.Services;

public class StageSavedNotificationService : IHostedService
{
    private readonly ILogger _logger;
    private readonly IpcCallerStagehand _ipcCallerStagehand;
    private readonly StageConfigService _stageConfigService;
    private readonly MareMediator _mareMediator;

    public StageSavedNotificationService(ILogger<StageSavedNotificationService> logger, IpcCallerStagehand ipcCallerStagehand, StageConfigService stageConfigService, MareMediator mareMediator)
    {
        _logger = logger;
        _ipcCallerStagehand = ipcCallerStagehand;
        _stageConfigService = stageConfigService;
        _mareMediator = mareMediator;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ipcCallerStagehand.StagehandApi.LocalStageDefinitionEdited += OnLocalStageDefinitionEdited;

        return Task.CompletedTask;
    }

    private void OnLocalStageDefinitionEdited(string definitionFilename)
    {
        foreach (var pair in _stageConfigService.Current.StageIdUploadedDefinitionPath)
        {
            if (pair.Value == definitionFilename)
            {
                _mareMediator.Publish(new NotificationMessage(
                    "Stage Saved",
                    $"Stage {Path.GetFileName(definitionFilename)} was just saved, which you have uploaded to PlayerSync as {pair.Key}.",
                    MareConfiguration.Models.NotificationType.StageSaved,
                    UpdatedStageFilename: definitionFilename,
                    UpdatedStageId: pair.Key));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ipcCallerStagehand.StagehandApi.LocalStageDefinitionEdited -= OnLocalStageDefinitionEdited;

        return Task.CompletedTask;
    }
}
