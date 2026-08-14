using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.UI.Components.Popup;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class UiService : DisposableMediatorSubscriberBase
{
    private readonly List<WindowMediatorSubscriberBase> _createdWindows = [];
    private readonly IUiBuilder _uiBuilder;
    private readonly FileDialogManager _fileDialogManager;
    private readonly ILogger<UiService> _logger;
    private readonly MareConfigService _mareConfigService;
    private readonly WindowSystem _windowSystem;
    private readonly UiFactory _uiFactory;

    public UiService(ILogger<UiService> logger, IUiBuilder uiBuilder,
        MareConfigService mareConfigService, WindowSystem windowSystem,
        IEnumerable<WindowMediatorSubscriberBase> windows,
        UiFactory uiFactory, FileDialogManager fileDialogManager,
        MareMediator mareMediator) : base(logger, mareMediator)
    {
        _logger = logger;
        _logger.LogTrace("Creating {type}", GetType().Name);
        _uiBuilder = uiBuilder;
        _mareConfigService = mareConfigService;
        _windowSystem = windowSystem;
        _uiFactory = uiFactory;
        _fileDialogManager = fileDialogManager;

        _uiBuilder.DisableGposeUiHide = true;
        _uiBuilder.Draw += Draw;
        _uiBuilder.OpenConfigUi += ToggleUi;
        _uiBuilder.OpenMainUi += ToggleMainUi;

        foreach (var window in windows)
        {
            _windowSystem.AddWindow(window);
        }

        Mediator.Subscribe<ProfileOpenStandaloneMessage>(this, msg =>
        {
            var existingWindow = _createdWindows.OfType<StandaloneProfileUi>().FirstOrDefault();

            if (existingWindow == null)
            {
                var window = _uiFactory.CreateStandaloneProfileUi(msg.Pair);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
            else if (string.Equals(existingWindow.Pair.UserData.UID, msg.Pair.UserData.UID, StringComparison.Ordinal))
            {
                existingWindow.IsOpen = false;
            }
            else
            {
                existingWindow.ResetProfileData();
                _windowSystem.RemoveWindow(existingWindow);
                _createdWindows.Remove(existingWindow);
                existingWindow.Dispose();

                var newWindow = _uiFactory.CreateStandaloneProfileUi(msg.Pair);
                _createdWindows.Add(newWindow);
                _windowSystem.AddWindow(newWindow);
            }               
        });

        Mediator.Subscribe<OpenSyncshellAdminPanel>(this, (msg) =>
        {
            if (!_createdWindows.Exists(p => p is SyncshellAdminUI ui
                && string.Equals(ui.GroupFullInfo.GID, msg.GroupInfo.GID, StringComparison.Ordinal)))
            {
                var window = _uiFactory.CreateSyncshellAdminUi(msg.GroupInfo);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
        });

        Mediator.Subscribe<OpenSyncshellProfilePanel>(this, (msg) =>
        {
            if (!_createdWindows.Exists(p => p is SyncshellProfileUi ui
                && string.Equals(ui.GroupFullInfo.GID, msg.GroupInfo.GID, StringComparison.Ordinal)))
            {
                var window = _uiFactory.CreateSyncshellProfileUi(msg.GroupInfo);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
        });

        Mediator.Subscribe<OpenPermissionWindow>(this, (msg) =>
        {
            if (!_createdWindows.Exists(p => p is PermissionWindowUI ui
                && msg.Pair == ui.Pair))
            {
                var window = _uiFactory.CreatePermissionPopupUi(msg.Pair);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
        });

        Mediator.Subscribe<OpenStageDetailsWindow>(this, msg =>
        {
            if (msg.StartingStageInfo != null)
            {
                // For viewing/editing an existing stage, find and activate that window if it already exists
                var existingWindow = _createdWindows.FirstOrDefault(window => window is StageDetailsUi stageWindow && stageWindow.StageInfo != null && stageWindow.StageInfo.SID == msg.StartingStageInfo.SID);
                if (existingWindow != null)
                {
                    existingWindow.IsOpen = true;
                    existingWindow.RequestFocus = true;
                    existingWindow.BringToFront();
                }
                else
                {
                    var window = _uiFactory.CreateStageDetailsUi(msg.StartingStageInfo, msg.OwningGroupId);
                    _createdWindows.Add(window);
                    _windowSystem.AddWindow(window);
                    window.IsOpen = true;
                }
            }
            else
            {
                // For creating a new stage, we always create a new window
                var window = _uiFactory.CreateStageDetailsUi(msg.StartingStageInfo, msg.OwningGroupId);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
                window.IsOpen = true;
            }
        });

        Mediator.Subscribe<RemoveWindowMessage>(this, (msg) =>
        {
            _windowSystem.RemoveWindow(msg.Window);
            _createdWindows.Remove(msg.Window);
            msg.Window.Dispose();
        });
    }

    public void ToggleMainUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    public void ToggleUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _logger.LogTrace("Disposing {type}", GetType().Name);

        _windowSystem.RemoveAllWindows();

        foreach (var window in _createdWindows)
        {
            window.Dispose();
        }

        _uiBuilder.Draw -= Draw;
        _uiBuilder.OpenConfigUi -= ToggleUi;
        _uiBuilder.OpenMainUi -= ToggleMainUi;
    }

    private void Draw()
    {
        _windowSystem.Draw();
        _fileDialogManager.Draw();
    }
}