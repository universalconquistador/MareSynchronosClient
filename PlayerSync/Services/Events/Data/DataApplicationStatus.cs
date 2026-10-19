namespace MareSynchronos.Services.Events;

public enum DataApplicationStatus
{
    Init,
    Waiting,
    Queued,
    Deferred,
    Starting,
    Processing,
    Downloading,
    Applying,
    Cancelled,
    Completed,
    Failed
}