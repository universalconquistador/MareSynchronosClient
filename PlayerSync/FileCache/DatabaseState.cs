namespace MareSynchronos.FileCache;

public enum DatabaseState
{
    Stopped,
    Starting,
    Stopping,
    Ready,
    Failed,
    Validating,
    CacheInit,
    Migrating
}