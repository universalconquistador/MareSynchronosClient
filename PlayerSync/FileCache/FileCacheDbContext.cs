using MareSynchronos.FileCache;
using Microsoft.EntityFrameworkCore;

namespace PlayerSync.FileCache;

public class FileCacheDbContext : DbContext
{
    public FileCacheDbContext(DbContextOptions<FileCacheDbContext> options) : base(options)
    {
    }

    public DbSet<FileCacheEntity> FileCaches => Set<FileCacheEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileCacheEntity>(entity =>
        {
            entity.ToTable("FileCaches");

            entity.HasKey(fileCache => fileCache.Id);

            entity.Property(fileCache => fileCache.Id).ValueGeneratedOnAdd();
            entity.Property(fileCache => fileCache.Hash).IsRequired().HasMaxLength(40);
            entity.Property(fileCache => fileCache.Store).IsRequired().HasConversion<int>();
            entity.Property(fileCache => fileCache.FilePathWithinFileStore).IsRequired();
            entity.Property(fileCache => fileCache.LastAccessedDateTicks).IsRequired();
            entity.Property(fileCache => fileCache.LastModifiedDateTicks).IsRequired();
            entity.Property(fileCache => fileCache.Size).HasDefaultValue(-1L);
            entity.Property(fileCache => fileCache.CompressedSize).HasDefaultValue(-1L);

            entity.HasIndex(fileCache => new { fileCache.Hash, fileCache.Store }).HasDatabaseName("FileCaches_Hash_Store");
            entity.HasIndex(fileCache => new { fileCache.Store, fileCache.FilePathWithinFileStore }).IsUnique().HasDatabaseName("FileCaches_Store_FilePathWithinFileStore");
            entity.HasIndex(fileCache => new { fileCache.Store, fileCache.LastAccessedDateTicks }).HasDatabaseName("FileCaches_Store_LastAccessedDateTicks");

            entity.Ignore(fileCache => fileCache.IsCacheEntry);
            entity.Ignore(fileCache => fileCache.ResolvedFilepath);
        });
    }
}
