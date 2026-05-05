using Dalamud.Plugin.Services;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace PlayerSync.Services
{
    public class JsonDataTypeHandlerService : DisposableMediatorSubscriberBase, IHostedService
    {
        private readonly LifeStreamHandler _lifeStreamHandler;

        public JsonDataTypeHandlerService(ILogger<JsonDataTypeHandlerService> logger, MareMediator mediator, 
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController, IpcManager ipcManager, IChatGui chatGui) : base(logger, mediator)
        {
            _lifeStreamHandler = new(logger, mediator, mareConfigService, dalamudUtilService, pairManager, apiController, ipcManager, chatGui);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("{name} started.", nameof(JsonDataTypeHandlerService));

            Mediator.Subscribe<JsonDataTypeMessage>(this, (msg) => ProcessJsonDataTypeDto(msg.Dto));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("{name} stopped.", nameof(JsonDataTypeHandlerService));

            return Task.CompletedTask;
        }

        public void ProcessJsonDataTypeDto(JsonDataTypeDto dto)
        {

            switch (dto.JsonDataType)
            {
                case JsonDataType.LifestreamLocationInvite:
                    _lifeStreamHandler.LifestreamLocationInviteHandler(dto);
                    break;

                default:
                    break;
            }
        }
    }
}
