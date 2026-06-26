using Dalamud.Game.Command;
using MareSynchronos.Interop.Ipc;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly IpcManager _ipcManager;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly DalamudUtilService _dalamudUtil;

    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    private sealed record PenumbraOption(
        [property: JsonPropertyName("Files")] Dictionary<string, string>? Files);
    private sealed record PenumbraGroup(
        [property: JsonPropertyName("Options")] List<PenumbraOption>? Options);

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
        ZoneSyncConfigService zoneSyncConfigService,
        FileDialogManager fileDialogManager,
        IpcManager ipcManager,
        FileCacheManager fileCacheManager,
        FileUploadManager fileUploadManager,
        DalamudUtilService dalamudUtil,
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
        _zoneSyncConfigService = zoneSyncConfigService;
        _fileDialogManager = fileDialogManager;
        _ipcManager = ipcManager;
        _fileCacheManager = fileCacheManager;
        _fileUploadManager = fileUploadManager;
        _dalamudUtil = dalamudUtil;
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
        $"\t {alias} diag - Opens the PlayerSync Diagnostics window" + Environment.NewLine +
        $"\t {alias} toggle - Disconnects from PlayerSync, if connected. Connects to PlayerSync, if disconnected" + Environment.NewLine +
        $"\t {alias} toggle on|off - Connects or disconnects to PlayerSync respectively" + Environment.NewLine +
        $"\t {alias} gpose - Opens the PlayerSync Character Data Hub window" + Environment.NewLine +
        $"\t {alias} analyze - Opens the PlayerSync Character Data Analysis window" + Environment.NewLine +
        $"\t {alias} broadcast - Toggles the Syncshell Broadcast feature" + Environment.NewLine +
        $"\t {alias} broadcast on|off - Enables or disables the Syncshell Broadcast feature" + Environment.NewLine +
        $"\t {alias} zonesync - Toggles the ZoneSync feature" + Environment.NewLine +
        $"\t {alias} zonesync on|off - Enables or disables the ZoneSync feature" + Environment.NewLine +
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

        if (string.Equals(splitArgs[0], "diag", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DiagnosticsUi)));
        }
        else if (string.Equals(splitArgs[0], "emote", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(EmoteSyncUi)));
        }
        else if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
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

        else if (string.Equals(splitArgs[0], "broadcast", StringComparison.OrdinalIgnoreCase))
        {
            bool originalStatus = _mareConfigService.Current.ListenForBroadcasts;
            _mareConfigService.Current.ListenForBroadcasts = !_mareConfigService.Current.ListenForBroadcasts;

            var setListening = splitArgs.Length > 1 ? splitArgs[1] : "";
            if (!string.IsNullOrWhiteSpace(setListening))
            {
                if (string.Equals(setListening, "on", StringComparison.OrdinalIgnoreCase))
                    _mareConfigService.Current.ListenForBroadcasts = true;
                else if (string.Equals(setListening, "off", StringComparison.OrdinalIgnoreCase))
                    _mareConfigService.Current.ListenForBroadcasts = false;
            }

            if (originalStatus != _mareConfigService.Current.ListenForBroadcasts)
            {
                _mareConfigService.Save();
                _mediator.Publish(new BroadcastListeningChanged(_mareConfigService.Current.ListenForBroadcasts));
            }
            string status = _mareConfigService.Current.ListenForBroadcasts ? "on" : "off";
            string chatMsg = $"[PlayerSync] Syncshell Broadcast feature is {status}.";
            _chat.Print(chatMsg);
        }

        else if (string.Equals(splitArgs[0], "zonesync", StringComparison.OrdinalIgnoreCase))
        {
            bool originalStatus = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
            _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = !_zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;

            var joinZoneSync = splitArgs.Length > 1 ? splitArgs[1] : "";
            if (!string.IsNullOrWhiteSpace(joinZoneSync))
            {
                if (string.Equals(joinZoneSync, "on", StringComparison.OrdinalIgnoreCase))
                    _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = true;
                else if (string.Equals(joinZoneSync, "off", StringComparison.OrdinalIgnoreCase))
                    _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = false;
            }

            string status = "";
            if (originalStatus != _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining)
            {
                var character = _serverConfigurationManager.CurrentPlayerName;
                _zoneSyncConfigService.Current.ZoneSyncEnabledPerCharacter[character] = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
                _zoneSyncConfigService.Save();
                _mediator.Publish(new GroupZoneSetEnableState(_zoneSyncConfigService.Current.EnableGroupZoneSyncJoining));
                status = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining ? "on (waiting a moment before sending a join...)" : "off (leaving Zone Syncshell...)";
            }
            else
            {
                status = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining ? "on." : "off.";
            }

            string chatMsg = $"[PlayerSync] ZoneSync feature is {status}";

            _chat.Print(chatMsg);
        }


        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
        else if (string.Equals(splitArgs[0], "preloadplaylist", StringComparison.OrdinalIgnoreCase))
        {
            if (!_apiController.IsConnected)
            {
                _chat.PrintError("[PlayerSync] Not connected to server.");
                return;
            }
            var savedDir = _mareConfigService.Current.LastPreloadPlaylistFolder;
            var startDir = !string.IsNullOrEmpty(savedDir) && Directory.Exists(savedDir)
                ? savedDir
                : _ipcManager.Penumbra.ModDirectory;
            _fileDialogManager.OpenFileDialog("Select Penumbra Group JSON", ".json", (ok, paths) =>
            {
                if (!ok || paths.FirstOrDefault() is not string path) return;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    _mareConfigService.Current.LastPreloadPlaylistFolder = dir;
                    _mareConfigService.Save();
                }
                _ = Task.Run(() => PreloadPlaylistAsync(path));
            }, 1, string.IsNullOrEmpty(startDir) ? null : startDir);
        }
    }

    private async Task PreloadPlaylistAsync(string jsonPath)
    {
        Task Print(string msg) => _dalamudUtil.RunOnFrameworkThread(() => _chat.Print(msg));
        Task PrintError(string msg) => _dalamudUtil.RunOnFrameworkThread(() => _chat.PrintError(msg));

        try
        {
            var json = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
            var group = JsonSerializer.Deserialize<PenumbraGroup>(json);

            var modDir = Path.GetDirectoryName(jsonPath)!;
            var scdPaths = (group?.Options ?? [])
                .SelectMany(o => o.Files ?? [])
                .Where(kv => kv.Value.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                .Select(kv => Path.Combine(modDir, kv.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scdPaths.Length == 0)
            {
                await Print("[PlayerSync] No SCD files found in that group.").ConfigureAwait(false);
                return;
            }

            await Print($"[PlayerSync] Found {scdPaths.Length} SCD file(s), uploading...").ConfigureAwait(false);

            var cacheEntries = _fileCacheManager.GetFileCachesByPaths(scdPaths);
            var hashes = cacheEntries.Values
                .Where(e => e != null)
                .Select(e => e!.Hash)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // SCDs that never resolved to a cache entry (missing on disk, outside the
            // Penumbra mod folder, etc.) can't be uploaded — count them as failures.
            var uncached = cacheEntries
                .Where(kv => kv.Value == null)
                .Select(kv => kv.Key)
                .ToList();

            var progress = new Progress<string>(msg => _log.Debug("[PreloadPlaylist] {msg}", msg));
            var failed = await _fileUploadManager.UploadFiles(hashes, progress).ConfigureAwait(false);

            var pushed = hashes.Count - failed.Count;

            // Upload failures come back as hashes; map them to file names.
            var uploadFailures = failed
                .Select(h => cacheEntries.FirstOrDefault(kv =>
                    kv.Value != null && string.Equals(kv.Value!.Hash, h, StringComparison.OrdinalIgnoreCase)).Key ?? h)
                .Select(p => $"{Path.GetFileName(p)} (upload failed)");

            // Uncached SCDs never resolved to a cache entry and are already paths.
            var uncachedFailures = uncached
                .Select(p => $"{Path.GetFileName(p)} (file missing)");

            var failedNames = uploadFailures.Concat(uncachedFailures).ToList();

            await Print($"[PlayerSync] SCD preload done — {pushed} uploaded, {failedNames.Count} failed.").ConfigureAwait(false);
            if (failedNames.Count > 0)
                await PrintError($"[PlayerSync] Failed files: {string.Join(", ", failedNames)}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "PreloadPlaylist failed");
            await PrintError($"[PlayerSync] SCD preload failed: {ex.Message}").ConfigureAwait(false);
        }
    }
}
