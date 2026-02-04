using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayerSync.PlayerData.Pairs;
using PlayerSync.Services;
using System.Reflection;

namespace MareSynchronos;

#pragma warning disable S125 // Sections of code should not be commented out
/*                                                                
                                                       RRRRRRRRRRRRRRRRRRRRRRRR                                                        
                                                  RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR                                                   
                                             RRRRRRRRRRRRTZZZZRRRRRRRRRRRRZZZVRRRRRRRRRR          RR                                   
                                          RRRRRRRRRZZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRZZRRRRRRR      HRRR                                  
                                       RRRRRRRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRTZRRRRRR   RRRRR                                 
                                     RRRRRRRZZRRRRRRRRRRRRQRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRZRRRRRRRRRRR                                 
                                   RRRRRRZZRRRRRRRRDHRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRVRRRRZRRRR                                
                                 RRRRRTZRRRRRRRKMRRRRRR                     RRRRRRRRRRRRRRRRRRRRRRRRRRRR                               
                               RRRRRRZRRRRRRDQRRRR                               RRRRRRRRRRRRRRRRRRRRRRRH                              
                             RRRRRRZRRRRRIQRRRR                                     RRRRRRRRRRRRRRRRRRRRR                              
                            RRRRRZRRRRRDRRRR                                          BRRRRRZRRRRRRRRRRRRR                             
                          RRRRRZRRRRRFRRRR                                         RRRRRRZZRRRRRRRRRRRRRRRR                            
                         RRRRRZRRRRFRRRR                                         RRRRRRRRRRRRRRRRRRRRRRRRRR                            
                        RRRRRTRRRDRRRR                                                     DRRRRRRRRRRRRRRRR                           
                       RRRRRRHDDRRRRRRRRRRRRRRRRRRRRRRRRRRR                    ORRRRRRRRRRRRRM   RRRRRRRRRRRQ                          
                      RRRRRRRXZZZZZZZZZZZZZZZZZZZZZZZXVRRRRRRRR            ORRRRRRRRRRRRRRRRRRRRRRO  RRRRRRRR                          
                     RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRVZZRRRRR         RRRRRZRRRRRRRRRRRRRRRRRRRRR   RRRRRR                         
                     RRRRZRRRRRRZRRRRRRRRRRRRRRRRRRRRRRRRRRRRXRRRRR     KRRRZRRRRRRRRRRRRRRRRRRRRRRRRRRR   RRRR                        
                    RRRRZRRRRRRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRTRRRR   RRRRXRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR   RR                        
                   RRRRZRRRRRRRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR  RRRZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR                              
                   RRRRZRRRRRRRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRORRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR                                
                   RRRZRRRRR RRRZZRRRRRRRRRRR          RRRRVRRRRRRRRRRRRRZRRRRRRRRRR       RRRRRRRRRR                                  
                  RRRRZIRRR  RRRZZRRRRRRRRRRR           RRRRRRRRRRRRRRRRRZRRRRRRRRRRR         RRRRRR              RR                   
                  RRRRRRRR   RRRZZRRRRRRRRRRR            RRRXRRRRRRRRRRRRRRRRRRRRRRRRRR         QR                RR                   
                  RRRRRRR    RRRZZRRRRRRRRRRR            RRRZRRRRRRRRR RRRRRRRRRRRRXRRRRRRR                       RRR                  
                  RRRRRRR    RRRZZRRRRRRRRRRR           RRRRRRRRRRRRRR RRRRRRRRRRRRRRRZRRRRRRR                    RRR                  
                  RRRRRR     RRRZZRRRRRRRRRRR         RRRRRZRRRRRRRRRR   RRRRRRRRRRRRRRRRZVRRRRRRQ                RRR                  
                  RRRRR      RRRZZRRRRRRRRRRRRRRRRRRRRRRRZZRRRRRRRRRR     RRRRRRRRRRRRRRRRRRRZRRRRRR              RRRR                 
                  RRRRR      RRRZZRRRRRRRRRRRRRRRRRRRVZZRRRRRRRRRRRRR       RRRRRRRRRRRRRRRRRRRRZRRRRR           RRRRR                 
                  RRRR       RRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR          RRRRRRRRRRRRRRRRRRRRZRRRRR         RRRRR                 
                  RRRR       RRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR               RRRRRRRRRRRRRRRRRRRZRRRR        RRRRR                 
                  RRR        RRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR                   IRRRRRRRRRRRRRRRRRRRRR      RRRRRR                 
                  RRR        RRRZZRRRRRRRRRRRRRRRRRRRRRRRRRRRR                          RRRRRRRRRRRRRRRRRRR     RRRRRR                 
                   RR        RRRZZRRRRRRRRRRRRRRRRRRRRRRRRRR              RR               RRRRRRRRRRRRVRRR    RRRRRRR                 
                   RR        RRRZZRRRRRRRRRRR                           RRRRRR               RRRRRRRRRRRRRR   RRRRRRRR                 
                   RR        RRRZZRRRRRRRRRRR                         RRRRRRRRRR             RRRRRRRRRRRRRR  RRRRRRRRR                 
                             RRRZZRRRRRRRRRRR                        RRRRZRRZRRRRR         BRRRRRRRRRRRRRRR KRRRRRRRR                  
                             RRRZZRRRRRRRRRRR                      RRRRZRRRRRRZRRRRRRRRRRRRRRRRRRRRRRRRRRR RRRRZRRRRR                  
                             RRRZZRRRRRRRRRRR                     RRRRZRRRRRRRRRRZRRRRRRRRRTRRRRRRRRRRRRR RRRRRRRRRR                   
                        R    RRRZZRRRRRRRRRRR                      RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRHRRRRRRRRRRR                   
                        RRRR RRRZZRRRRRRRRRRR                        RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR RRRRRRRRRRR                    
                         RRRRRRRZZRRRRRRRRRRR                          RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR RRRRRRRRRRR                     
                          RRRRRRRRRRRRRRRRRRR                             RRRRRRRRRRRRRRRRRRRRRRRRRR  RRRRVRRRRRRR                     
                          RRRRZRRRRRRRRRRRRR                                   RRRRRRRRRRRRRRRRR    RRRRRZRRRRRRR                      
                           RRRRRZZTRRRRRRRRRR                                                     RRRRRRZRRRRRRR                       
                            RRRVRRRRZZZRRRRRRRRRRRRRR                                           QRRRRRZRRRRRRR                         
                            RRRRRRRRRRRRRRRZZZRRRRRR                                          RRRRRRRZRRRRRRR                          
                             RRRRRRRRRRRRRRRRIRRR                                           RRRRRRRZRRRRRRRR                           
                              RRRVRRRRRRRRZRRRRRRR                                       RRRRRRRTVRRRRRRRRR                            
                              RRRRZRRRRRRRRRZRRRRRRRR                                 RRRRRRORZRRRRRRRRRR                              
                               RRRRRRRRRRRRRRRRRRRRRRRRRRRR                       RRRRRRRRRZTRRRRRQRRRR                                
                                RRRVRRRRRRRRRRRRRRZRRRRRRRRRRRRRRRRRD   DRRRRRRRRRRRRRRVZRRRRRRORRRRR                                  
                                 RRRRRRRRRRRRRRRRRRRRRZRRRRRRRRRRRRRRRRRRRRRRRRRRRRZZRRRRRRRRHRRRRR                                    
                                 RRRRR  RRRRRRRRRRRRRRRRRRRZZRRRRRRRRRRRRRRRXZZZRRRRRRRRRRHRRRRRR                                      
                                  RRRR     RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRIRRRRRRR                                        
                                   RR         RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRQKRRRRRRRR                                           
                                                  RRRRRRRRRRRQRRRRRRRRRRRRRKHORRRRRRRRRR                                               
                                                       RRRRRRRRRRRRRRRRRRRRRRRRRRRR                                                    
                                                                   DDDD                                                                
*/
#pragma warning restore S125 // Sections of code should not be commented out

public class MarePlugin : MediatorSubscriberBase, IHostedService
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareConfigService _mareConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly GroupZoneSyncManager _groupZoneSyncManager;
    private readonly NamePlateManagerService _namePlateManagerService;
    private IServiceScope? _runtimeServiceScope;
    private Task? _launchTask = null;

    public MarePlugin(ILogger<MarePlugin> logger, MareConfigService mareConfigService,
        ServerConfigurationManager serverConfigurationManager,
        DalamudUtilService dalamudUtil,
        IServiceScopeFactory serviceScopeFactory, MareMediator mediator,
        GroupZoneSyncManager groupZoneSyncManager, NamePlateManagerService namePlateManagerService) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtil = dalamudUtil;
        _serviceScopeFactory = serviceScopeFactory;
        _groupZoneSyncManager = groupZoneSyncManager;
        _namePlateManagerService = namePlateManagerService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        Logger.LogInformation("Launching {name} {major}.{minor}.{build}", "PlayerSync", version.Major, version.Minor, version.Build);
        Mediator.Publish(new EventMessage(new Services.Events.Event(nameof(MarePlugin), Services.Events.EventSeverity.Informational,
            $"Starting PlayerSync {version.Major}.{version.Minor}.{version.Build}")));

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (msg) => {
            if (_launchTask == null || _launchTask.IsCompleted) _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
            if (!_mareConfigService.Current.HasValidSetup())
            {
                Logger.LogInformation("Setup not valid: AcceptedAgreement={Agreement}, InitialScanComplete={Scan}, CacheFolder='{Cache}'",
                    _mareConfigService.Current.AcceptedAgreement,
                    _mareConfigService.Current.InitialScanComplete,
                    _mareConfigService.Current.CacheFolder);
            }
        });
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());

        Mediator.StartQueueProcessing();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeAll();

        DalamudUtilOnLogOut();

        Logger.LogDebug("Halting PlayerSync Plugin");

        return Task.CompletedTask;
    }

    private void DalamudUtilOnLogIn()
    {
        Logger?.LogDebug("Client login");
        if (_launchTask == null || _launchTask.IsCompleted) _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    private void DalamudUtilOnLogOut()
    {
        Logger?.LogDebug("Client logout");

        _runtimeServiceScope?.Dispose();
    }

    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        while (!await _dalamudUtil.GetIsPlayerPresentAsync().ConfigureAwait(false))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        try
        {
            Logger?.LogDebug("Launching Managers");

            _runtimeServiceScope?.Dispose();
            _runtimeServiceScope = _serviceScopeFactory.CreateScope();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<UiService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CommandManagerService>();
            if (!_mareConfigService.Current.HasValidSetup() || !_serverConfigurationManager.HasValidConfig())
            {
                Mediator.Publish(new SwitchToIntroUiMessage());
                return;
            }
            else if (!_mareConfigService.Current.FirstTimeSetupComplete)
            {
                _mareConfigService.Current.FirstTimeSetupComplete = true;
                _mareConfigService.Save();
            }
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CacheCreationService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<VisibleUserDataDistributor>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<NotificationService>();

#if !DEBUG
            if (_mareConfigService.Current.LogLevel != LogLevel.Information)
            {
                Mediator.Publish(new NotificationMessage("Abnormal Log Level",
                    $"Your log level is set to '{_mareConfigService.Current.LogLevel}' which is not recommended for normal usage. Set it to '{LogLevel.Information}' in \"PlayerSync Settings -> Debug\" unless instructed otherwise.",
                    MareConfiguration.Models.NotificationType.Error, TimeSpan.FromSeconds(15000)));
            }
#endif
        }
        catch (Exception ex)
        {
            Logger?.LogCritical(ex, "Error during launch of managers");
        }
    }
}
