using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class PlayerIdleStatusService : DisposableMediatorSubscriberBase, IHostedService
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(60); // timer value before you are marked as idle

    private readonly DalamudUtilService _dalamudUtilService;

    private bool _isPlayerIdle = false;
    private bool _playerIdleFailedErrorNotice = false;

    public PlayerIdleStatusService(ILogger<PlayerIdleStatusService> logger, MareMediator mediator, DalamudUtilService dalamudUtilService) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
    }

    public bool IsPlayerIdle => _isPlayerIdle;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("{name} started.", nameof(PlayerIdleStatusService));

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => CheckForPlayerIdle());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("{name} stopped.", nameof(PlayerIdleStatusService));

        return Task.CompletedTask;
    }

    private void CheckForPlayerIdle()
    {
        if (_dalamudUtilService.IsWine) return;

        bool wasPlayerIdle = _isPlayerIdle;
        try
        {
            _isPlayerIdle = IdleCheck.IsIdleFor(IdleThreshold); // Windows only
        }
        catch (Exception ex)
        {
            if (!_playerIdleFailedErrorNotice)
                Logger.LogError(ex, "Idle check failed.");

            _playerIdleFailedErrorNotice = true;
        }

        if (!wasPlayerIdle && _isPlayerIdle)
        {
            Logger.LogInformation("PlayerSync online status changed to IDLE.");
            //Mediator.Publish(new NotificationMessage("Idle Timer", "Your online status is now set to IDLE.", MareConfiguration.Models.NotificationType.Info));
            Mediator.Publish(new PlayerIdleStartMessage());
        }


        if (wasPlayerIdle && !_isPlayerIdle)
        {
            Logger.LogInformation("PlayerSync online status changed to ACTIVE.");
            //Mediator.Publish(new NotificationMessage("Idle Timer", "Your online status is now set to ACTIVE.", MareConfiguration.Models.NotificationType.Info));
            Mediator.Publish(new PlayerIdleEndMessage());
        }
    }

}
