using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using System.Collections.ObjectModel;
using System.Globalization;
using MareSynchronos.Utils;

namespace MareSynchronos.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/sync";
    private const string _secondaryCommandName = "/psync";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly MareMediator _mediator;
    private readonly MareConfigService _mareConfigService;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    /// <summary>
    /// The alias to reference in user-facing text. "/sync" if available, otherwise "/psync".
    /// </summary>
    public string ActiveAlias { get; private set; } = _secondaryCommandName;

    public CommandManagerService(
        ICommandManager commandManager,
        PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager,
        CacheMonitor periodicFileScanner,
        ApiController apiController,
        MareMediator mediator,
        MareConfigService mareConfigService,
        IChatGui chat,
        IPluginLog log)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _mareConfigService = mareConfigService;
        _chat = chat;
        _log = log;

        // 1) Try to register /sync first (primary).
        var syncHandler = new CommandInfo(OnCommand)
        {
            HelpMessage = BuildFullHelpForAlias(_commandName),
            ShowInHelp = true
        };

        var syncOk = TryAddHandler_NoThrow(_commandName, syncHandler);
        if (!syncOk)
        {
            _log.Information("{Alias} is taken; will fall back to {Fallback}", _commandName, _secondaryCommandName);
            if (_mareConfigService.Current.ShowSyncConflictNotifications)
            {
                _chat.PrintError("[PlayerSync] Another plugin conflicts with /sync, using /psync as a fallback.");
            }
        }

        var psyncHandler = new CommandInfo(OnCommand)
        {
            HelpMessage = syncOk ? BuildMinimalHelpForAlias(_secondaryCommandName)
                                 : BuildFullHelpForAlias(_secondaryCommandName),
            ShowInHelp = true
        };

        var psyncOk = TryAddHandler_NoThrow(_secondaryCommandName, psyncHandler);

        ActiveAlias = syncOk ? _commandName : _secondaryCommandName;

        // So we can use this in a couple other chat printerrors 
        CommandAlias.Active = ActiveAlias;

        if (!psyncOk && !syncOk)
        {
            _log.Error("Failed to register both {Primary} and {Fallback}.", _commandName, _secondaryCommandName);
            _chat.PrintError("[PlayerSync] Failed to register commands (/sync, /psync). Another plugin may conflict.");
        }
    }

    public void Dispose()
    {
        SafeRemove(_commandName);
        SafeRemove(_secondaryCommandName);
    }

    // Build the full help string
    private string BuildFullHelpForAlias(string alias) =>
        "Opens the PlayerSync UI" + Environment.NewLine + Environment.NewLine +
        "Additionally possible commands:" + Environment.NewLine +
        $"\t {alias} toggle - Disconnects from PlayerSync, if connected. Connects to PlayerSync, if disconnected" + Environment.NewLine +
        $"\t {alias} toggle on|off - Connects or disconnects to PlayerSync respectively" + Environment.NewLine +
        $"\t {alias} gpose - Opens the PlayerSync Character Data Hub window" + Environment.NewLine +
        $"\t {alias} analyze - Opens the PlayerSync Character Data Analysis window" + Environment.NewLine +
        $"\t {alias} settings - Opens the PlayerSync Settings window";

    // Build the help string for the secondary if both commands are available
    private string BuildMinimalHelpForAlias(string alias) =>
        "Opens the PlayerSync UI";

    // Dalamud won't raise an error, we need to check if the command is registered
    private bool IsAliasTaken(string alias)
    {
        ReadOnlyDictionary<string, IReadOnlyCommandInfo> map = _commandManager.Commands;
        foreach (var key in map.Keys)
            if (string.Equals(key, alias, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool TryAddHandler_NoThrow(string alias, CommandInfo handler)
    {
        if (IsAliasTaken(alias))
        {
            _log.Information("Command {Alias} already present; not adding.", alias);
            return false;
        }

        try
        {
            _commandManager.AddHandler(alias, handler);
            _log.Information("Registered command {Alias}", alias);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not register {Alias}", alias);
            return false;
        }
    }

    private void SafeRemove(string alias)
    {
        if (!IsAliasTaken(alias)) return;
        try { _commandManager.RemoveHandler(alias); } catch { /* ignore */ }
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            if (_mareConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (!_mareConfigService.Current.HasValidSetup())
            return;

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage(
                    "PlayerSync disconnecting",
                    "Cannot use /toggle while PlayerSync is still disconnecting",
                    NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;

            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnectionsAsync();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
    }
}
