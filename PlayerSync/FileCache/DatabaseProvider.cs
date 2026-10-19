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

                await using var context = CreateDbContext();
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
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
