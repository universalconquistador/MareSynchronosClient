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
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stops the database, will attempt to close all open connections
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);

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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting SQLite using database {db}", _databasePath);

            try
            {
                var databaseExists = File.Exists(_databasePath);
                _logger.LogInformation(databaseExists ? "Opening existing file cache database db {db}" : "Creating new file cache database db {db}", _databasePath);

                bool recreateDatabase = false;
                if (databaseExists)
                {
                    await using (var context = CreateDbContext())
                    {
                        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                        var connection = (SqliteConnection)context.Database.GetDbConnection();
                        bool fileCachesExists;

                        // there was an experimental version I abandoned but left some users with an invalid db migration history, so this takes care of that
                        await using (var command = connection.CreateCommand())
                        {
                            command.CommandText = """
                                SELECT 1 FROM sqlite_master
                                WHERE type = 'table' AND name = 'FileCaches' COLLATE NOCASE
                                LIMIT 1;
                                """;
                            fileCachesExists = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
                        }

                        if (fileCachesExists)
                        {
                            var initialMigration = context.Database.GetMigrations().FirstOrDefault() ?? throw new InvalidOperationException("No FileCache database migrations were found.");
                            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);

                            if (!appliedMigrations.Contains(initialMigration, StringComparer.Ordinal))
                            {
                                var backupPath = $"{_databasePath}.incompatible-{Guid.NewGuid():N}.bak";
                                var backupConnectionString = new SqliteConnectionStringBuilder
                                {
                                    DataSource = backupPath,
                                    Pooling = false
                                }.ToString();

                                await using (var backup = new SqliteConnection(backupConnectionString))
                                {
                                    await backup.OpenAsync(cancellationToken).ConfigureAwait(false);
                                    connection.BackupDatabase(backup);
                                }

                                _logger.LogError("[DATABASE] Missing migration {migration}! Applied migrations: {applied}. Backup saved to {backup} and the database will be recreated!",
                                    initialMigration, string.Join(", ", appliedMigrations), backupPath);

                                recreateDatabase = true;
                            }
                        }
                    }
                }

                if (recreateDatabase)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using (var context = CreateDbContext())
                    {
                        // this should nuke the file cleanly
                        await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // remove any of the other sql files just in case (they should be removed on db close)
                    File.Delete(_databasePath + "-wal");
                    File.Delete(_databasePath + "-shm");
                    File.Delete(_databasePath + "-journal");
                }

                await using (var context = CreateDbContext())
                {
                    await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to start/create database {db}!", _databasePath);

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
    }
}
