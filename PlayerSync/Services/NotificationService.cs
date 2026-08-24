using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationType = MareSynchronos.MareConfiguration.Models.NotificationType;

namespace MareSynchronos.Services;

public class NotificationService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly INotificationManager _notificationManager;
    private readonly IChatGui _chatGui;
    private readonly MareConfigService _configurationService;
    private readonly StageConfigService _stageConfigService;
    DalamudLinkPayload? _inviteTogglePayload = null;
    DalamudLinkPayload? _stageUpdatePayload = null;

    string _savedStageFilename = "";
    string _savedStageId = "";

    public NotificationService(ILogger<NotificationService> logger, MareMediator mediator,
        DalamudUtilService dalamudUtilService,
        INotificationManager notificationManager,
        IChatGui chatGui, MareConfigService configurationService, StageConfigService stageConfigService) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
        _notificationManager = notificationManager;
        _chatGui = chatGui;
        _configurationService = configurationService;
        _stageConfigService = stageConfigService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);

        _inviteTogglePayload = _chatGui.AddChatLinkHandler(0, (_, _) => Mediator.Publish(new UiToggleMessage(typeof(PairingRequestsUi))));
        // LifeStreamHandler uses ID 1
        _stageUpdatePayload = _chatGui.AddChatLinkHandler(2, (_, _) => Mediator.Publish(new OpenStageUpdateWindow(_savedStageId, _savedStageFilename)));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _chatGui.RemoveChatLinkHandler();
        _inviteTogglePayload = null;

        return Task.CompletedTask;
    }

    private void PrintErrorChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[PlayerSync] Error: " + message);
        _chatGui.PrintError(se.BuiltString);
    }

    private void PrintInfoChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[PlayerSync] Info: ").AddItalics(message ?? string.Empty);
        _chatGui.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[PlayerSync] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    private void PrintPairRequestChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder();

        if (_inviteTogglePayload != null)
        {
            se
            .AddText("[PlayerSync] Info: ")
            .AddItalics(message ?? string.Empty)
            .AddText(" ")
            .Add(_inviteTogglePayload)
            .AddUiForeground("Click here to view invites.", 37)
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .Build();
        }
        else
        {
            se.AddText("[PlayerSync] Info: ").AddItalics(message ?? string.Empty);
        }
        
        _chatGui.Print(se.BuiltString);
    }

    private void PrintStageSaveChat(string message, string filename, string stageId)
    {
        _savedStageFilename = filename;
        _savedStageId = stageId;

        SeStringBuilder se = new();

        se.AddText($"[PlayerSync] Info: ");
        se.AddItalics(message);
        if (_stageUpdatePayload != null)
        {
            se.AddText(" ");
            se.Add(_stageUpdatePayload);
            se.AddUiForeground("Click here to upload the new version.", 37);
            se.AddUiForegroundOff();
            se.Add(RawPayload.LinkTerminator);
        }

        _chatGui.Print(se.Build());
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
                PrintInfoChat(msg.Message);
                break;

            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;

            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;

            case NotificationType.Invite:
                PrintPairRequestChat(msg.Message);
                break;

            case NotificationType.StageSaved:
                if (msg.UpdatedStageFilename != null && msg.UpdatedStageId != null)
                {
                    PrintStageSaveChat(msg.Message, msg.UpdatedStageFilename, msg.UpdatedStageId);
                }
                break;
        }
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.LogInformation("{msg}", msg.ToString());

        if (!_dalamudUtilService.IsLoggedIn) return;

        switch (msg.Type)
        {
            case NotificationType.Info:
                ShowNotificationLocationBased(msg, _configurationService.Current.InfoNotification);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(msg, _configurationService.Current.WarningNotification);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(msg, _configurationService.Current.ErrorNotification);
                break;

            case NotificationType.Invite:
                ShowNotificationLocationBased(msg, _configurationService.Current.PairRequestNotification);
                break;

            case NotificationType.StageSaved:
                ShowNotificationLocationBased(msg, _stageConfigService.Current.StageSavedNotificationLocation);
                break;
        }
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location)
    {
        switch (location)
        {
            case NotificationLocation.Toast:
                ShowToast(msg);
                break;

            case NotificationLocation.Chat:
                ShowChat(msg);
                break;

            case NotificationLocation.Both:
                ShowToast(msg);
                ShowChat(msg);
                break;

            case NotificationLocation.Nowhere:
                break;
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        Dalamud.Interface.ImGuiNotification.NotificationType dalamudType = msg.Type switch
        {
            NotificationType.Error => Dalamud.Interface.ImGuiNotification.NotificationType.Error,
            NotificationType.Warning => Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            NotificationType.Info => Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            NotificationType.Invite => Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            NotificationType.StageSaved => Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            _ => Dalamud.Interface.ImGuiNotification.NotificationType.Info
        };

        var notification = _notificationManager.AddNotification(new Notification()
        {
            Content = msg.Message ?? string.Empty,
            Title = msg.Title,
            Type = dalamudType,
            Minimized = false,
            InitialDuration = msg.TimeShownOnScreen ?? TimeSpan.FromSeconds(3)
        });

        if (msg.Type == NotificationType.StageSaved && msg.UpdatedStageFilename != null && msg.UpdatedStageId != null)
        {
            notification.DrawActions += drawContext =>
            {
                if (ImGui.Button("Upload new version", new System.Numerics.Vector2(drawContext.MaxCoord.X - drawContext.MinCoord.X, ImGui.GetFrameHeight())))
                {
                    Mediator.Publish(new OpenStageUpdateWindow(msg.UpdatedStageId, msg.UpdatedStageFilename));
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted($"Opens the stage details window for stage {msg.UpdatedStageId} so you can upload the new version of {Path.GetFileName(msg.UpdatedStageFilename)}.");
                    }
                }
            };
        }
    }
}