using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, TransientConfigService transientConfigService,
    ServerConfigService serverConfigService, NotesConfigService notesConfigService, ServerTagConfigService serverTagConfigService) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;

    public void Migrate()
    {
        if (transientConfigService.Current.Version == 0)
        {
            _logger.LogInformation("Migrating Transient Config V0 => V1");
            transientConfigService.Current.TransientConfigs.Clear();
            transientConfigService.Current.Version = 1;
            transientConfigService.Save();
        }

        if (serverConfigService.Current.Version == 1)
        {
            _logger.LogInformation("Migrating Server Config V1 => V2");
            var centralServer = serverConfigService.Current.ServerStorage.Find(f => f.ServerName.Equals("Lunae Crescere Incipientis (Central Server EU)", StringComparison.Ordinal));
            if (centralServer != null)
            {
                centralServer.ServerName = ApiController.MainServer;
            }
            serverConfigService.Current.Version = 2;
            serverConfigService.Save();
        }

        // Floof left us a means to version the server.json to make updates to the client configs prior to services loading
        if (serverConfigService.Current.Version == 2)
        {
            _logger.LogInformation("Migrating Server Config V2 => V3");
            var centralServer = serverConfigService.Current.ServerStorage.Find(f => f.ServerName.Equals("Mare Sempiterne.io Server", StringComparison.Ordinal));
            var devServer = serverConfigService.Current.ServerStorage.Find(f => f.ServerUri.Equals("wss://dev.maresempiterne.io", StringComparison.Ordinal));

            // Migrate the main entry server
            if (centralServer != null)
            {
                centralServer.ServerName = ApiController.MainServer;
                centralServer.ServerUri = ApiController.MainServiceUri;
            }

            // Migrate dev, if we have one defined
            if (devServer != null)
            {
                _logger.LogInformation("Setting dev server information.");
                var mainUri = new Uri(ApiController.MainServiceUri);
                var newDev = $"{mainUri.Scheme}://dev.{mainUri.Host}";
                devServer.ServerName = ApiController.MainServer + " - Dev";
                devServer.ServerUri = newDev;
            }

            // The startup checks if the 1st entry matches the ServiceURI constants before we get here
            // Since it doesn't, until we migrate, we get a duplicate entry we must remove
            // For fresh installs we need to ensure we don't remove the default entry
            if (serverConfigService.Current.ServerStorage.Count > 1)
            {
                serverConfigService.Current.ServerStorage.RemoveAll(f => f.SecretKeys == null || !f.SecretKeys.Any());
            }

            // Reset us back to the first server in the list
            serverConfigService.Current.CurrentServer = 0;

            // Bump server.json for migration code flow
            serverConfigService.Current.Version = 3;
            serverConfigService.Save();
        }
        if (serverConfigService.Current.Version == 3)
        {
            _logger.LogInformation("Migrating Server Config V3 => V4");
            var centralServer = serverConfigService.Current.ServerStorage.Find(f => f.ServerUri.Equals("wss://playersync.io", StringComparison.Ordinal));

            // Migrate the main entry server
            if (centralServer != null)
                centralServer.ServerUri = ApiController.MainServiceUri;

            if (serverConfigService.Current.ServerStorage.Count > 1)
                serverConfigService.Current.ServerStorage.RemoveAll(f => f.SecretKeys == null || !f.SecretKeys.Any());

            serverConfigService.Current.CurrentServer = 0;

            // Bump server.json for migration code flow
            serverConfigService.Current.Version = 4;
            serverConfigService.Save();
        }

        // notes migrations
        if (notesConfigService.Current.Version == 0)
        {
            var oldKey = "wss://playersync.io";
            var newKey = "wss://sync.playersync.io";
            if (notesConfigService.Current.ServerNotes.TryGetValue(oldKey, out var notes))
            {
                notesConfigService.Current.ServerNotes[newKey] = notes;
                notesConfigService.Current.ServerNotes.Remove(oldKey);
            }
            notesConfigService.Current.Version = 1;
            notesConfigService.Save();

        }

        // server tags migration
        if (serverTagConfigService.Current.Version == 0)
        {
            var oldKey = "wss://playersync.io";
            var newKey = "wss://sync.playersync.io";
            if (serverTagConfigService.Current.ServerTagStorage.TryGetValue(oldKey, out var tags))
            {
                serverTagConfigService.Current.ServerTagStorage[newKey] = tags;
                serverTagConfigService.Current.ServerTagStorage.Remove(oldKey);
            }
            serverTagConfigService.Current.Version = 1;
            serverTagConfigService.Save();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
