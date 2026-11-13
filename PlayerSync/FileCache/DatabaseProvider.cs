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
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            _logger.LogDebug("Starting SQLite using database {db}", _databasePath);

            bool databaseExisted = FileExists(_databasePath);
            if (!databaseExisted)
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

            if (databaseExisted && await TryRestoreDatabaseAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await File.WriteAllTextAsync(RebuildPendingPath, string.Empty, cancellationToken).ConfigureAwait(false);
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
            File.Delete(RebuildPendingPath);

            _logger.LogInformation("Saved file cache backup to {path}", GoodDatabasePath);
        }

        private async Task<bool> TryRestoreDatabaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FileExists(GoodDatabasePath))
            {
                return false;
            }

            var tempPath = $"{_databasePath}.restore-{Guid.NewGuid():N}.tmp";
            string? badPath = null;
            try
            {
                try
                {
                    File.Copy(GoodDatabasePath, tempPath);
                    ValidateDbBackup(tempPath, cancellationToken);
                    await MigrateDatabaseAsync(cancellationToken, tempPath).ConfigureAwait(false);
                    if (FileExists(tempPath + "-wal") || FileExists(tempPath + "-journal"))
                    {
                        throw new InvalidDataException($"Restored database {tempPath} still has an active journal");
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Cannot prepare file cache backup {backup} for restore", GoodDatabasePath);
                    return false;
                }

                await StopAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                badPath = Path.ChangeExtension(_databasePath, $".BAD-{Guid.NewGuid():N}.bak");
                var databaseExists = FileExists(_databasePath);
                if (databaseExists)
                {
                    File.Move(_databasePath, badPath);
                }

                foreach (var suffix in new[] { "-wal", "-journal" })
                {
                    if (FileExists(_databasePath + suffix))
                    {
                        File.Move(_databasePath + suffix, badPath + suffix);
                    }
                }

                File.Delete(_databasePath + "-shm");
                File.Move(tempPath, _databasePath);

                _logger.LogWarning("Restored file cache database from {backup}. Previous database: {bad}", GoodDatabasePath, badPath);
                File.Delete(RebuildPendingPath);

                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogCritical(ex, "Failed to restore file cache database {db} from {backup}!", _databasePath, GoodDatabasePath);

                if (badPath != null && FileExists(badPath) && !FileExists(_databasePath))
                {
                    // these files are specific to the specific .db
                    foreach (var suffix in new[] { "-wal", "-journal" })
                    {
                        if (FileExists(badPath + suffix))
                        {
                            File.Move(badPath + suffix, _databasePath + suffix);
                        }
                    }

                    File.Move(badPath, _databasePath);
                }

                throw;
            }
            finally
            {
                try
                {
                    // these files are specific to the specific .db
                    foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                    {
                        File.Delete(tempPath + suffix);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Cannot delete temporary database {db}", tempPath);
                }
            }
        }

        private async Task MigrateDatabaseAsync(CancellationToken cancellationToken, string? restorePath = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = CreateDbContext();
            var database = context.Database;

            if (restorePath != null)
            {
                var connectionString = new SqliteConnectionStringBuilder(database.GetConnectionString())
                {
                    DataSource = restorePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                };
                database.SetConnectionString(connectionString.ToString());

                // Remove any migration locks that got stuck
                await database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "__EFMigrationsLock";""", cancellationToken).ConfigureAwait(false);
            }

            var pendingMigrations = await database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (pendingMigrations.Any())
            {
                using var migrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (restorePath == null && FileExists(_databasePath))
                {
                    migrationCts.CancelAfter(TimeSpan.FromSeconds(10)); // don't let a stalled/locked migration prevent the startup
                }

                await database.MigrateAsync(migrationCts.Token).ConfigureAwait(false);
            }

            // this is a check for table validity, if it errors we'll catch it and restore the db
            if (context is FileCacheDbContext fileCacheContext)
            {
                _ = await fileCacheContext.FileCaches.AsNoTracking().OrderBy(fileCache => fileCache.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
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