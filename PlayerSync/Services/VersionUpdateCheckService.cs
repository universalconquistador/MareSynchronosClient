using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace MareSynchronos.Services;

public class VersionUpdateCheckService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly ILogger<VersionUpdateCheckService> _logger;
    private readonly HttpClient _httpClient;
    private Version _latestVersion = new();

    private readonly CancellationTokenSource _periodicVersionUpdateCheckCts = new();

    public VersionUpdateCheckService(ILogger<VersionUpdateCheckService> logger, HttpClient httpClient, MareMediator mediator) : base(logger, mediator)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(5);
    private const string RepositoryUrl = "https://playersync.io/download/plugin/repo.json";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _latestVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        _logger.LogInformation("Starting VersionUpdateCheckService");
        _ = Task.Run(PeriodicCheckVersionTask, _periodicVersionUpdateCheckCts.Token);
        _logger.LogInformation("Started VersionUpdateCheckService");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _periodicVersionUpdateCheckCts.Cancel();
        _periodicVersionUpdateCheckCts.Dispose();
        return Task.CompletedTask;
    }

    private async Task PeriodicCheckVersionTask()
    {
        // We need to give the plugin a moment to fully start all the services
        await Task.Delay(TimeSpan.FromSeconds(10), _periodicVersionUpdateCheckCts.Token).ConfigureAwait(false);

        while (!_periodicVersionUpdateCheckCts.Token.IsCancellationRequested)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RepositoryUrl);
            req.Headers.Accept.ParseAdd("application/json");

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _periodicVersionUpdateCheckCts.Token).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(_periodicVersionUpdateCheckCts.Token).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PlayerSync was unable to check for version update from {url}", RepositoryUrl);
                continue;
            }

            try
            {
                Version version = ParseAssemblyVersion(json);
                if (_latestVersion < version)
                {
                    _latestVersion = version;
                    SendVersionUpdateNotice(version.ToString());
                }
            }
            catch
            {
                _logger.LogWarning("There was an issue parsing the repo.json for {url}", RepositoryUrl);
            }

            await Task.Delay(UpdateInterval, _periodicVersionUpdateCheckCts.Token).ConfigureAwait(false);
        }
    }

    private static Version ParseAssemblyVersion(string json)
    {
        Version version;
        using var repoJson = JsonDocument.Parse(json);
        var firstElement = repoJson.RootElement[0];
        var rawVersion = firstElement.TryGetProperty("AssemblyVersion", out var v) ? v.ToString() : null;
        if (rawVersion != null)
        {
            if (!Version.TryParse(rawVersion, out var outVersion))
                version = new(0, 0, 0, 0);
            else
                version = outVersion;
        }
        else
        {
            version = new(0, 0, 0, 0);
        }

        return version;
    }

    private void SendVersionUpdateNotice(string version)
    {
        string msg = $"A new version ({version}) of PlayerSync is available. Please update when possible.";
        _logger.LogInformation(msg);
        Mediator.Publish(new NotificationMessage("PlayerSync Update Available", msg, NotificationType.Warning));
    }
}