using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace MareSynchronos.Services;

public class ChangelogService : MediatorSubscriberBase
{
    private readonly MareConfigService _configService;

    public ChangelogService(ILogger<ChangelogService> logger, MareMediator mediator, MareConfigService configService)
        : base(logger, mediator)
    {
        _configService = configService;

        // Subscribe to force show message
        Mediator.Subscribe<ForceShowChangelogMessage>(this, (msg) => CheckForNewVersion(forceShow: true));
    }

    public void CheckForNewVersion(bool forceShow = false)
    {
        var currentVersion = GetCurrentVersion();
        var lastSeenVersion = _configService.Current.LastSeenVersion;

        Logger.LogInformation("ChangelogService: Current version: '{CurrentVersion}', Last seen: '{LastSeenVersion}', Force: {ForceShow}", currentVersion, lastSeenVersion, forceShow);
        Logger.LogInformation("ChangelogService: IsNullOrEmpty(lastSeenVersion): {IsEmpty}, currentVersion != lastSeenVersion: {NotEqual}",
            string.IsNullOrEmpty(lastSeenVersion), currentVersion != lastSeenVersion);

        // If this is the first time or a new version, show changelog
        bool isFirstTime = string.IsNullOrEmpty(lastSeenVersion);
        bool isNewVersion = !string.IsNullOrEmpty(lastSeenVersion) && currentVersion != lastSeenVersion;

        Logger.LogInformation("ChangelogService: IsFirstTime: {IsFirstTime}, IsNewVersion: {IsNewVersion}", isFirstTime, isNewVersion);

        if (forceShow || isFirstTime || isNewVersion)
        {
            Logger.LogInformation("ChangelogService: Preparing to show changelog (FirstTime: {IsFirstTime})", isFirstTime);
            var changelogText = GetChangelogForVersion(currentVersion, isFirstTime);
            if (!string.IsNullOrEmpty(changelogText))
            {
                Logger.LogInformation("ChangelogService: Publishing changelog popup for version {Version}", currentVersion);
                Mediator.Publish(new OpenChangelogPopupMessage(currentVersion, changelogText));

                // Update the last seen version
                _configService.Current.LastSeenVersion = currentVersion;
                _configService.Save();
                Logger.LogInformation("ChangelogService: Updated LastSeenVersion to {Version}", currentVersion);
            }
            else
            {
                Logger.LogWarning("ChangelogService: No changelog text for version {Version}", currentVersion);
            }
        }
        else
        {
            Logger.LogInformation("ChangelogService: No version change detected, skipping changelog");
        }
    }

    private string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
    }

    private string GetChangelogForVersion(string version, bool isFirstTime = false)
    {
        Logger.LogInformation("ChangelogService: Getting changelog for version {Version}, FirstTime: {IsFirstTime}", version, isFirstTime);

        var changelogEntry = LoadChangelogEntry(version, isFirstTime);
        var changelog = changelogEntry?.Content ?? "No changelog available for this version.";

        Logger.LogInformation("ChangelogService: Changelog length: {Length} characters", changelog.Length);
        return changelog;
    }

    private ChangelogEntry? LoadChangelogEntry(string version, bool isFirstTime)
    {
        try
        {
            // For first-time users, always use welcome.json
            if (isFirstTime)
            {
                return LoadChangelogFromFile("welcome.json");
            }

            // Try to load specific version file (e.g., v1.13.7.json)
            var versionFile = $"{version}.json";
            var versionChangelog = LoadChangelogFromFile(versionFile);
            if (versionChangelog != null)
            {
                return versionChangelog;
            }

            // Try to load major version file (e.g., v1.13.json for v1.13.x)
            if (version.Contains('.') && version.LastIndexOf('.') > version.IndexOf('.'))
            {
                var majorVersion = version[..version.LastIndexOf('.')];
                var majorVersionFile = $"{majorVersion}.json";
                var majorVersionChangelog = LoadChangelogFromFile(majorVersionFile);
                if (majorVersionChangelog != null)
                {
                    return majorVersionChangelog;
                }
            }

            // Fall back to default.json
            return LoadChangelogFromFile("default.json");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load changelog for version {Version}", version);
            return new ChangelogEntry
            {
                Title = "PlayerSync Update",
                Content = "Thank you for using PlayerSync! Check our documentation for the latest changes."
            };
        }
    }

    private ChangelogEntry? LoadChangelogFromFile(string fileName)
    {
        try
        {
            var resourcePath = $"Resources/Changelogs/{fileName}";
            var assembly = Assembly.GetExecutingAssembly();
            var fullPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", resourcePath);

            if (!File.Exists(fullPath))
            {
                Logger.LogDebug("Changelog file not found: {Path}", fullPath);
                return null;
            }

            var jsonContent = File.ReadAllText(fullPath);
            var changelogEntry = JsonSerializer.Deserialize<ChangelogEntry>(jsonContent);

            Logger.LogDebug("Loaded changelog from {FileName}: {Title}", fileName, changelogEntry?.Title);
            return changelogEntry;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load changelog file {FileName}", fileName);
            return null;
        }
    }
}