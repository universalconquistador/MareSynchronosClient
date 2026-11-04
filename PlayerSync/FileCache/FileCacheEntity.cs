#nullable disable

namespace MareSynchronos.FileCache;

public enum FileCacheStore
{
    Penumbra = 0, //less volitile, makes sense to preserve cache space for other user's data
    Cache = 1
}

public class FileCacheEntity
{
    private FileCacheEntity()
    {
        // used for EF
    }

    public FileCacheEntity(string hash, string path, FileCacheStore store, long lastAccessedDateTicks, long lastModifiedDateTicks, long? size = null, long? compressedSize = null)
    {
        Size = size ?? -1;
        CompressedSize = compressedSize ?? -1;
        Hash = hash.ToUpperInvariant();
        FilePathWithinFileStore = string.Join("\\", path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        LastModifiedDateTicks = lastModifiedDateTicks;
        LastAccessedDateTicks = lastAccessedDateTicks;
        Store = store;
    }

    // db properties
    public long Id { get; private set; }
    public string Hash { get; set; }
    public long LastAccessedDateTicks { get; set; }
    public long LastModifiedDateTicks { get; set; }
    public string FilePathWithinFileStore { get; set; }
    public FileCacheStore Store { get; private set; }
    public long Size { get; set; } = -1;
    public long CompressedSize { get; set; } = -1;
    // not in the db
    public bool IsCacheEntry => Store == FileCacheStore.Cache;
    public string ResolvedFilepath { get; private set; } = string.Empty;

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = Path.GetFullPath(filePath).Replace('/', '\\').ToLowerInvariant();
    }
}