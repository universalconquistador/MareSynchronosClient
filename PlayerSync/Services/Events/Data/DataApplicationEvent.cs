using MareSynchronos.API.Data;
using System.Globalization;

namespace MareSynchronos.Services.Events;

public record DataApplicationEvent : Event
{
    public string UserUID { get; }
    public Guid ApplicationId { get; }
    public DataApplicationStatus ApplicationStatus { get; }

    public DataApplicationEvent(Guid applicationId, DataApplicationStatus applicationStatus, string? character, UserData userData, string eventSource, EventSeverity eventSeverity, string message)
        : base(character, userData, eventSource, eventSeverity, message)
    {
        UserUID = userData.UID;
        ApplicationId = applicationId;
        ApplicationStatus = applicationStatus;
    }

    public override string ToString()
    {
        return $"ApplicationId:{ApplicationId}\tApplicationStatus:{ApplicationStatus}\t{base.ToString()}";
    }

    public string ToLogFormat()
    {
        return $"[{this.EventTime.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)}] {this.EventSeverity} - {this.Message}";
    }
}
