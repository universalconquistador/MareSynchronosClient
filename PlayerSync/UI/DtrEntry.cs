using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace MareSynchronos.UI;

public sealed class DtrEntry : IDisposable, IHostedService
{
    private readonly ApiController _apiController;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly IDtrBar _dtrBar;
    private readonly Lazy<IDtrBarEntry> _entry;
    private readonly ILogger<DtrEntry> _logger;
    private readonly MareMediator _mareMediator;
    private readonly PairManager _pairManager;
    private readonly IBroadcastManager _broadcastManager;
    private Task? _runTask;

    public DtrEntry(ILogger<DtrEntry> logger, IDtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService, MareMediator mareMediator, PairManager pairManager, IBroadcastManager broadcastManager, ApiController apiController)
    {
        _logger = logger;
        _dtrBar = dtrBar;
        _entry = new(CreateEntry);
        _configService = configService;
        _mareMediator = mareMediator;
        _pairManager = pairManager;
        _broadcastManager = broadcastManager;
        _apiController = apiController;
    }

    public void Dispose()
    {
        if (_entry.IsValueCreated)
        {
            _logger.LogDebug("Disposing DtrEntry");
            Clear();
            _entry.Value.Remove();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting DtrEntry");
        _runTask = Task.Run(RunAsync, _cancellationTokenSource.Token);
        _logger.LogInformation("Started DtrEntry");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        try
        {
            await _runTask!.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore cancelled
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private void Clear()
    {
        if (!_entry.IsValueCreated) return;
        _logger.LogInformation("Clearing entry");

        _entry.Value.Shown = false;
    }

    private IDtrBarEntry CreateEntry()
    {
        _logger.LogTrace("Creating new DtrBar entry");
        var entry = _dtrBar.Get("PlayerSync");
        entry.OnClick = _ => _mareMediator.Publish(new UiToggleMessage(typeof(CompactUi)));

        return entry;
    }

    private async Task RunAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);

            Update();
        }
    }

    private void Update()
    {
        if (!_configService.Current.EnableDtrEntry || !_configService.Current.HasValidSetup())
        {
            if (_entry.IsValueCreated && _entry.Value.Shown)
            {
                _logger.LogInformation("Disabling entry");

                Clear();
            }
            return;
        }

        if (!_entry.Value.Shown)
        {
            _logger.LogInformation("Showing entry");
            _entry.Value.Shown = true;
        }

        string tooltip;
        SeStringBuilder textBuilder = new SeStringBuilder();
        if (_apiController.IsConnected)
        {
            tooltip = "PlayerSync: Connected";

            // Add the broadcast info
            if (_broadcastManager.IsListening)
            {
                var color = _broadcastManager.IsBroadcasting()
                    ? _configService.Current.DtrColorsBroadcasting
                    : default;
                textBuilder.AddColoredText($"\uE038 {_broadcastManager.AvailableBroadcastGroups.Count} ",
                    _configService.Current.UseColorsInDtr
                    ? color
                    : default);

                if (_broadcastManager.IsBroadcasting())
                {
                    tooltip += $"{Environment.NewLine}Broadcasting {_broadcastManager.BroadcastingGroupId}";
                }

                tooltip += $"{Environment.NewLine}{_broadcastManager.AvailableBroadcastGroups.Count} Broadcasts Nearby";
            }

            var pairCount = _pairManager.GetVisibleUserCount();
            var pairColor = _configService.Current.DtrColorsDefault;
            if (pairCount > 0)
            {
                IEnumerable<string> visiblePairs;
                if (_configService.Current.ShowUidInDtrTooltip)
                {
                    visiblePairs = _pairManager.GetOnlineUserPairs()
                        .Where(x => x.IsVisible)
                        .Select(x => string.Format("{0} ({1})", _configService.Current.PreferNoteInDtrTooltip ? x.GetNote() ?? x.PlayerName : x.PlayerName, x.UserData.AliasOrUID));
                }
                else
                {
                    visiblePairs = _pairManager.GetOnlineUserPairs()
                        .Where(x => x.IsVisible)
                        .Select(x => string.Format("{0}", _configService.Current.PreferNoteInDtrTooltip ? x.GetNote() ?? x.PlayerName : x.PlayerName));
                }

                tooltip += $"{Environment.NewLine}----------{Environment.NewLine}{string.Join(Environment.NewLine, visiblePairs)}";
                pairColor = _configService.Current.DtrColorsPairsInRange;
            }

            textBuilder.AddColoredText($"\uE044 {pairCount}", _configService.Current.UseColorsInDtr ? pairColor : default);
        }
        else
        {
            textBuilder.AddColoredText("\uE044 \uE04C", _configService.Current.UseColorsInDtr ? _configService.Current.DtrColorsNotConnected : default);
            tooltip = "PlayerSync: Not Connected";
        }

        _entry.Value.Text = textBuilder.Build();
        _entry.Value.Tooltip = tooltip;
    }
}

/// <summary>
/// The colors that can be applied to the text inside a <see cref="SeString"/>.
/// </summary>
/// <param name="Foreground"></param>
/// <param name="Glow"></param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SeStringTextColors(uint Foreground = default, uint Glow = default);

public static class SeStringBuilderExtensions
{
    // The built-in color & glow functions in SeStringBuilder operate based on UIColors, which have a different value per UI theme.
    // These functions allow literal RGBA colors and glows to be used.

    private const byte _colorTypeForeground = 0x13;
    private const byte _colorTypeGlow = 0x14;

    private static RawPayload BuildColorStartPayload(byte colorType, uint color)
        => new(unchecked([0x02, colorType, 0x05, 0xF6, byte.Max((byte)color, 0x01), byte.Max((byte)(color >> 8), 0x01), byte.Max((byte)(color >> 16), 0x01), 0x03]));

    private static RawPayload BuildColorEndPayload(byte colorType)
        => new([0x02, colorType, 0x02, 0xEC, 0x03]);

    public static void BeginForegroundColor(this SeStringBuilder sb, uint color)
    {
        sb.Add(BuildColorStartPayload(_colorTypeForeground, color));
    }

    public static void EndForegroundColor(this SeStringBuilder sb)
    {
        sb.Add(BuildColorEndPayload(_colorTypeForeground));
    }

    public static void BeginGlowColor(this SeStringBuilder sb, uint color)
    {
        sb.Add(BuildColorStartPayload(_colorTypeGlow, color));
    }

    public static void EndGlowColor(this SeStringBuilder sb)
    {
        sb.Add(BuildColorEndPayload(_colorTypeGlow));
    }

    public static void AddColoredText(this SeStringBuilder builder, string text, SeStringTextColors colors)
    {
        if (colors.Foreground != default)
            builder.BeginForegroundColor(colors.Foreground);
        if (colors.Glow != default)
            builder.BeginGlowColor(colors.Glow);
        builder.AddText(text);
        if (colors.Glow != default)
            builder.EndGlowColor();
        if (colors.Foreground != default)
            builder.EndForegroundColor();
    }
}