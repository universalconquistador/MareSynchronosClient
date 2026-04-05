using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data.AdditionalTypes;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Validators;
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
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly MareConfigService _configurationService;
        private readonly PairManager _pairManager;
        private readonly ApiController _apiController;
        private readonly IpcManager _ipcManager;
        private readonly IChatGui _chatGui;

        DalamudLinkPayload? _lifestreamInvite = null;

        public JsonDataTypeHandlerService(ILogger<JsonDataTypeHandlerService> logger, MareMediator mediator, 
            MareConfigService mareConfigService, DalamudUtilService dalamudUtilService, PairManager pairManager,
            ApiController apiController, IpcManager ipcManager, IChatGui chatGui) : base(logger, mediator)
        {
            _dalamudUtilService = dalamudUtilService;
            _configurationService = mareConfigService;
            _pairManager = pairManager;
            _apiController = apiController;
            _ipcManager = ipcManager;
            _chatGui = chatGui;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("{name} started.", nameof(JsonDataTypeHandlerService));

            Mediator.Subscribe<JsonDataTypeMessage>(this, (msg) => ProcessJsonDataTypeDto(msg.Dto));

            _lifestreamInvite = _chatGui.AddChatLinkHandler(1, (_, a) => LifestreamInviteIpcHandler(a));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("{name} stopped.", nameof(JsonDataTypeHandlerService));

            _lifestreamInvite = null;

            return Task.CompletedTask;
        }

        public void ProcessJsonDataTypeDto(JsonDataTypeDto dto)
        {

            switch (dto.JsonDataType)
            {
                case JsonDataType.LifestreamLocationInvite:
                    LifestreamLocationInviteHandler(dto);
                    break;

                default:
                    break;
            }
        }

        private void LifestreamLocationInviteHandler(JsonDataTypeDto dto)
        {
            if (!_ipcManager.Lifestream.APIAvailable) return;

            if (!JsonDataTypeValidators.TryValidate<LifestreamParseableAddress>(dto.JsonDataType, dto.JsonData, out var address)) return;

            if (address == null) return;

            var pair = _pairManager.GetPairByUID(dto.UserData.UID);
            if (pair == null) return;

            string psync = "[PlayerSync] ";
            string invite = string.IsNullOrWhiteSpace(pair.PlayerName) ? $"UID/Alias {pair.UserData.AliasOrUID}" : $"Player {pair.PlayerName}";

            string destination = string.IsNullOrWhiteSpace(address.Plot) ? $"{address.World}" : $"{address.World}, {address.District}, W{address.Ward}, P{address.Plot}";

            Logger.LogTrace("Address parse: {1}", destination);

            SeStringBuilder se = new SeStringBuilder();

            se
            .AddText(psync + invite + " has sent you a Lifestream invite. Click to travel to: ")
            .Add(_lifestreamInvite!)
            .AddUiForeground(destination, 37)
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .Build();            

            _chatGui.Print(se.BuiltString);
        }

        private void LifestreamInviteIpcHandler(SeString address)
        {
            Logger.LogTrace("LIFESTREAM ADDRESS: {1}", address.TextValue);

            if (!_ipcManager.Lifestream.TryExecuteCommand(address.TextValue))
                Logger.LogWarning("Error in Lifestream command execution for {address}", address.TextValue);
        }
    }
}
