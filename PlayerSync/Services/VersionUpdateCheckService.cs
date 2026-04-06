using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace MareSynchronos.Services;

public class VersionUpdateCheckService : DisposableMediatorSubscriberBase
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private const string RepositoryUrl = "https://playersync.io/download/plugin/repo.json";

    private readonly ILogger<VersionUpdateCheckService> _logger;
    private readonly HttpClient _httpClient;

    private Version _latestVersion;
    private readonly object _sync = new();

    private CancellationTokenSource? _periodicCts;
    private Task? _periodicTask;

    public VersionUpdateCheckService(ILogger<VersionUpdateCheckService> logger, HttpClient httpClient, MareMediator mediator)
        : base(logger, mediator)
    {
        _logger = logger;
        _httpClient = httpClient;
        _latestVersion = Assembly.GetExecutingAssembly().GetName().Version!;

        Mediator.Subscribe<ConnectedMessage>(this, _ => Start());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => Stop());
    }

    private void Start()
    {
        lock (_sync)
        {
            if (_periodicTask is { IsCompleted: false })
                return;

            _periodicCts?.Dispose();
            _periodicCts = new CancellationTokenSource();
            _periodicTask = PeriodicCheckVersionTask(_periodicCts.Token);
        }
    }

    private void Stop()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _periodicCts;
            _periodicCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task PeriodicCheckVersionTask(CancellationToken ct)
    {
        try
        {
            // give plugin time to finish spinning up
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, RepositoryUrl);
                    req.Headers.Accept.ParseAdd("application/json");

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("PlayerSync was unable to check for version update from {url} (HTTP {code})", RepositoryUrl, (int)resp.StatusCode);

                        await Task.Delay(UpdateInterval, ct).ConfigureAwait(false);
                        continue;
                    }

                    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    Version version;
                    try
                    {
                        version = ParseAssemblyVersion(json);
                    }
                    catch
                    {
                        _logger.LogWarning("There was an issue parsing the repo.json for {url}", RepositoryUrl);
                        await Task.Delay(UpdateInterval, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (_latestVersion < version)
                    {
                        _latestVersion = version;
                        SendVersionUpdateNotice(version.ToString());
                    }

                    await Task.Delay(UpdateInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Version update check failed");
                    // backoff so we don't spin if something is broken
                    await Task.Delay(UpdateInterval, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (_sync)
                _periodicTask = null;
        }
    }

    private static Version ParseAssemblyVersion(string json)
    {
        using var repoJson = JsonDocument.Parse(json);
        var firstElement = repoJson.RootElement[0];
        var rawVersion = firstElement.TryGetProperty("AssemblyVersion", out var v) ? v.ToString() : null;

        return rawVersion != null && Version.TryParse(rawVersion, out var outVersion)
            ? outVersion
            : new Version(0, 0, 0, 0);
    }

    private void SendVersionUpdateNotice(string version)
    {
        string msg = $"A new version ({version}) of PlayerSync is available. Please update when possible.";
        _logger.LogInformation(msg);
        Mediator.Publish(new NotificationMessage("PlayerSync Update Available", msg, NotificationType.Warning));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Stop();
        base.Dispose(disposing);
    }
}