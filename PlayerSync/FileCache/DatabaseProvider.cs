using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PlayerSync.FileCache
{
    /// <summary>
    /// Provides the setup and teardown for a SQLite database including access to the DbContext for the given type
    /// </summary>
    /// <typeparam name="TContext">A type derived from DbContext</typeparam>
    public interface IDatabaseProvider<TContext> where TContext : DbContext
    {
        /// <summary>
        /// Starts the DatabaseProvider which creates the database instance and performs any migrations
        /// </summary>
        /// <returns></returns>
        Task<bool> StartAsync(CancellationToken cancellationToken, bool rebuildIfMissing = false);

        /// <summary>
        /// Stops the database, will attempt to close all open connections
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Creates a backup of the database to .GOOD.bak extension
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task BackupDatabaseAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to back up the database file before deleting it and creating a fresh database and migration
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task DeleteAndResetDatabase(CancellationToken cancellationToken);

        /// <summary>
        /// Gets a database context of the database provider's type
        /// </summary>
        /// <returns>A DbContext of the type the DatabaseProvider was created with</returns>
        TContext CreateDbContext();
    }

    internal class DatabaseProvider<TContext> : IDatabaseProvider<TContext> where TContext : DbContext
    {
        private readonly ILogger<DatabaseProvider<TContext>> _logger;

        private readonly DbContextOptions<TContext> _databaseOptions;
        private readonly Func<DbContextOptions<TContext>, TContext> _contextFactory;
        private readonly string _databasePath;

        public DatabaseProvider(ILogger<DatabaseProvider<TContext>> logger, string databaseDirectory, string databaseName, Func<DbContextOptions<TContext>, TContext> contextFactory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
            ArgumentNullException.ThrowIfNull(contextFactory);

            _logger = logger;
            _contextFactory = contextFactory;

            var dbFolder = Path.Combine(Path.GetFullPath(databaseDirectory), databaseName);
            Directory.CreateDirectory(dbFolder);
            _databasePath = Path.Combine(dbFolder, $"{databaseName}.db");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 30
            }
            .ToString();

            _databaseOptions = new DbContextOptionsBuilder<TContext>()
                .UseSqlite(connectionString, sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(TContext).Assembly.GetName().Name)).Options;
        }

        public string DatabaseFullFilePath => _databasePath;
        private string GoodDatabasePath => Path.ChangeExtension(_databasePath, ".GOOD.bak");
        private string RebuildPendingPath => _databasePath + ".rebuild";

        public async Task<bool> StartAsync(CancellationToken cancellationToken, bool rebuildIfMissing = false)
        {
            _logger.LogDebug("Starting SQLite using database {db}", _databasePath);

            if (!FileExists(_databasePath))
            {
                if (await TryRestoreDatabaseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                if (FileExists(RebuildPendingPath)) // this is a marker if the game crashes or the user turns off the plugin before completion
                {
                    await DeleteAndResetDatabase(cancellationToken).ConfigureAwait(false);

                    return true;
                }

                if (rebuildIfMissing)
                {
                    await File.WriteAllTextAsync(RebuildPendingPath, string.Empty, cancellationToken).ConfigureAwait(false);
                }
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var versionConnection = new SqliteConnection())
                    {
                        _logger.LogInformation("Using SQLite version: {version}", versionConnection.ServerVersion);
                    }

                    var databaseExists = FileExists(_databasePath);
                    _logger.LogInformation(databaseExists ? "Opening existing file cache database db {db}" : "Creating new file cache database db {db}", _databasePath);

                    await MigrateDatabaseAsync(cancellationToken).ConfigureAwait(false);

                    return FileExists(RebuildPendingPath);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        _logger.LogCritical(ex, "Cannot access file cache database {db}", _databasePath);

                        throw;
                    }

                    _logger.LogWarning(ex, "Failed to start database {db} after {attempt}/3 attempts", _databasePath, attempt);

                    if (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (await TryRestoreDatabaseAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await File.WriteAllTextAsync(RebuildPendingPath, string.Empty).ConfigureAwait(false);

            await DeleteAndResetDatabase(cancellationToken).ConfigureAwait(false);

            return true;
        }

        public async Task DeleteAndResetDatabase(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await StopAsync(cancellationToken).ConfigureAwait(false);

                if (FileExists(_databasePath))
                {
                    var backupPath = Path.ChangeExtension(_databasePath, ".OLD.bak");
                    File.Copy(_databasePath, backupPath, overwrite: true);
                    File.Delete(backupPath + "-shm");

                    // remove any existing backed up files before backing them up
                    // we should only keep the -wal file that corresponds with that specific .db
                    foreach (var suffix in new[] { "-wal", "-journal" })
                    {
                        File.Delete(backupPath + suffix);
                        if (FileExists(_databasePath + suffix))
                        {
                            File.Copy(_databasePath + suffix, backupPath + suffix);
                        }
                    }
                }

                // this should nuke the file cleanly
                File.Delete(_databasePath);
                // remove any of the other sql files just in case (they should be removed on db close)
                File.Delete(_databasePath + "-wal");
                File.Delete(_databasePath + "-shm");
                File.Delete(_databasePath + "-journal");

                await MigrateDatabaseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogCritical(ex, "Failed to reset database {db}!", _databasePath);

                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            SqliteConnection.ClearAllPools();

            return Task.CompletedTask;
        }

        public TContext CreateDbContext()
        {
            return _contextFactory(_databaseOptions);
        }

        public async Task BackupDatabaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            File.Delete(RebuildPendingPath);
            var tempPath = GoodDatabasePath + ".tmp";
            File.Delete(tempPath);

            await using (var context = CreateDbContext())
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                var connection = (SqliteConnection)context.Database.GetDbConnection();

                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path;"; // https://sqlite.org/lang_vacuum.html
                command.Parameters.AddWithValue("$path", tempPath);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            ValidateDbBackup(tempPath, cancellationToken);

            File.Move(tempPath, GoodDatabasePath, overwrite: true);

            _logger.LogInformation("Saved file cache backup to {path}", GoodDatabasePath);
        }

        private async Task<bool> TryRestoreDatabaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FileExists(GoodDatabasePath))
            {
                return false;
            }

            try
            {
                var tempPath = _databasePath + ".tmp";
                File.Copy(GoodDatabasePath, tempPath, overwrite: true);

                ValidateDbBackup(tempPath, cancellationToken);

                await StopAsync(cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                var badPath = Path.ChangeExtension(_databasePath, ".BAD.bak");
                var databaseExists = FileExists(_databasePath);
                if (databaseExists)
                {
                    File.Move(_databasePath, badPath, overwrite: true);
                }

                // these files must go with the same db file if they exist
                foreach (var suffix in new[] { "-wal", "-journal" })
                {
                    if (FileExists(_databasePath + suffix))
                    {
                        File.Move(_databasePath + suffix, badPath + suffix, overwrite: true);
                    }
                    else if (databaseExists)
                    {
                        File.Delete(badPath + suffix);
                    }
                }

                File.Delete(_databasePath + "-shm");
                File.Move(tempPath, _databasePath);

                _logger.LogWarning("Starting file cache database restore from {backup} Previous database: {bad}", GoodDatabasePath, badPath);
                await MigrateDatabaseAsync(cancellationToken).ConfigureAwait(false);

                File.Delete(RebuildPendingPath);

                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogCritical(ex, "Failed to restore file cache database {db} from {backup}!", _databasePath, GoodDatabasePath);

                throw;
            }
        }

        private async Task MigrateDatabaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            combinedCts.CancelAfter(TimeSpan.FromSeconds(10)); // don't let a stalled/locked migration prevent the startup
            await using var context = CreateDbContext();

            await context.Database.MigrateAsync(combinedCts.Token).ConfigureAwait(false);

            // this is a check for table validity, if it errors we'll catch it and restore the db
            if (context is FileCacheDbContext fileCacheContext)
            {
                _ = await fileCacheContext.FileCaches.AsNoTracking().OrderBy(fileCache => fileCache.Id).FirstOrDefaultAsync(combinedCts.Token).ConfigureAwait(false);
            }
        }

        private static void ValidateDbBackup(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(path).Length == 0)
            {
                throw new InvalidDataException($"File cache backup {path} is empty!");
            }

            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check(1);";
            var result = command.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"File cache backup {path} check fail: {result}");
            }
        }

        private static bool FileExists(string path)
        {
            // File.Exists also returns false when access is denied
            try
            {
                _ = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }
    }
}