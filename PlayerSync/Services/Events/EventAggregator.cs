using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Events;

public class EventAggregator : MediatorSubscriberBase, IHostedService
{
    private sealed class AggregatedEvent<TEvent> where TEvent : Event
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly RollingList<TEvent> _events;

        public AggregatedEvent(int eventCount)
        {
            _events = new RollingList<TEvent>(eventCount);
            EventList = CreateEventLazy();
        }

        public Lazy<List<TEvent>> EventList { get; private set; }
        public bool NewEventsAvailable => !EventList.IsValueCreated;

        public void Add(TEvent eventData)
        {
            _lock.Wait();

            try
            {
                _events.Add(eventData);

                if (EventList.IsValueCreated)
                {
                    EventList = CreateEventLazy();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public void RecreateLazy()
        {
            _lock.Wait();

            try
            {
                if (EventList.IsValueCreated)
                {
                    EventList = CreateEventLazy();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private Lazy<List<TEvent>> CreateEventLazy()
        {
            return new Lazy<List<TEvent>>(() =>
            {
                _lock.Wait();

                try
                {
                    return [.. _events];
                }
                finally
                {
                    _lock.Release();
                }
            });
        }
    }

    private readonly AggregatedEvent<Event> _events = new(500);
    private readonly Dictionary<string, AggregatedEvent<DataApplicationEvent>> _dataApplicationEvents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1);
    private readonly string _configDirectory;
    private readonly ILogger<EventAggregator> _logger;
    private DateTime _currentTime;

    private string CurrentLogName => $"{DateTime.Now:yyyy-MM-dd}-events.log";

    public Lazy<List<Event>> EventList => _events.EventList;
    public bool NewEventsAvailable => _events.NewEventsAvailable;
    public string EventLogFolder => Path.Combine(_configDirectory, "eventlog");
    public bool WriteDataEventsToFile { get; set; } = false;

    public EventAggregator(string configDirectory, ILogger<EventAggregator> logger, MareMediator mareMediator) : base(logger, mareMediator)
    {
        _configDirectory = configDirectory;
        _logger = logger;
        _currentTime = DateTime.Now - TimeSpan.FromDays(1);

        Mediator.Subscribe<EventMessage>(this, ProcessEventMessage);
    }

    public List<DataApplicationEvent> GetAllDataEventsByUid(string uid)
    {
        AggregatedEvent<DataApplicationEvent>? events;

        _lock.Wait();

        try
        {
            _dataApplicationEvents.TryGetValue(uid, out events);
        }
        finally
        {
            _lock.Release();
        }

        return events?.EventList.Value ?? [];
    }

    public DataApplicationEvent? GetLatestDataEventByUid(string uid)
    {
        AggregatedEvent<DataApplicationEvent>? events;

        _lock.Wait();

        try
        {
            _dataApplicationEvents.TryGetValue(uid, out events);
        }
        finally
        {
            _lock.Release();
        }

        var eventList = events?.EventList.Value ?? [];

        return events == null ? null : eventList.Count > 0 ? eventList[^1] : null;
    }

    private void ProcessEventMessage(EventMessage msg)
    {
        var writeToFile = false;

        _lock.Wait();

        try
        {
            Logger.LogTrace("Received event message: {event}", msg.Event);

            if (msg.Event is DataApplicationEvent dataApplicationEvent)
            {
                var uid = dataApplicationEvent.UserUID;

                if (!_dataApplicationEvents.TryGetValue(uid, out var dataEvents))
                {
                    dataEvents = new AggregatedEvent<DataApplicationEvent>(500);
                    _dataApplicationEvents.Add(uid, dataEvents);
                }

                dataEvents.Add(dataApplicationEvent);
                writeToFile = WriteDataEventsToFile;
            }
            else
            {
                _events.Add(msg.Event);
                writeToFile = true;
            }
        }
        finally
        {
            _lock.Release();
        }

        if (writeToFile)
        {
            WriteToFile(msg.Event);
        }
    }

    private void WriteToFile(Event receivedEvent)
    {
        if (DateTime.Now.Day != _currentTime.Day)
        {
            try
            {
                _currentTime = DateTime.Now;
                var filesInDirectory = Directory.EnumerateFiles(EventLogFolder, "*.log");
                if (filesInDirectory.Skip(10).Any())
                {
                    File.Delete(filesInDirectory.OrderBy(f => new FileInfo(f).LastWriteTimeUtc).First());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete last events");
            }
        }

        var eventLogFile = Path.Combine(EventLogFolder, CurrentLogName);
        try
        {
            if (!Directory.Exists(EventLogFolder)) Directory.CreateDirectory(EventLogFolder);
            File.AppendAllLines(eventLogFile, [receivedEvent.ToString()]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Could not write to event file {eventLogFile}");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting EventAggregatorService");
        Logger.LogInformation("Started EventAggregatorService");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeAll();
        return Task.CompletedTask;
    }
}
