
namespace MareSynchronos.Services.Models;

public enum DiagnosticsTestState
{
    Pending,
    Testing,
    Passed,
    Failed,
    Skipped,
}

public enum DiagnosticsTestKind
{
    DnsResolution,
    Ping,
    CloudflareDownloadSpeed,
    CloudflareUploadSpeed,
    VolumeFreeSpace,
}

public abstract record DiagnosticResult(
    DiagnosticsTestState State,
    string TargetValue,
    string? ErrorMessage = null,
    string? Details = null)
{

    public bool WasSuccessful => State == DiagnosticsTestState.Passed;
}

public sealed record DnsDiagnosticResult(
    DiagnosticsTestState State,
    string TargetValue,
    string[]? ResolvedIpAddresses = null,
    string? ErrorMessage = null,
    string? Details = null)
    : DiagnosticResult(State, TargetValue, ErrorMessage, Details
        );

public sealed record PingDiagnosticResult(
    DiagnosticsTestState State,
    string TargetValue,
    int? PingAttemptCount = null,
    int? PingSuccessfulReplyCount = null,
    double? PingMinimumMilliseconds = null,
    double? PingAverageMilliseconds = null,
    double? PingMaximumMilliseconds = null,
    string? ErrorMessage = null,
    string? Details = null)
    : DiagnosticResult(State, TargetValue, ErrorMessage, Details
        );

public sealed record SpeedTestDiagnosticResult(
    DiagnosticsTestState State,
    DiagnosticsTestKind SpeedTestKind,
    string TargetValue,
    long? TransferBytes = null,
    double? TransferSeconds = null,
    double? TransferMegabitsPerSecond = null,
    string? ErrorMessage = null,
    string? Details = null)
    : DiagnosticResult(State, TargetValue, ErrorMessage, Details
        );

public sealed record StorageDirectoryDiagnosticResult(
    DiagnosticsTestState State,
    string TargetValue,
    string? VolumeRootPath = null,
    long? VolumeTotalBytes = null,
    long? VolumeFreeBytes = null,
    long? VolumeAvailableBytes = null,
    string? ErrorMessage = null,
    string? Details = null)
    : DiagnosticResult(State, TargetValue, ErrorMessage, Details
        );

public sealed record FinalDiagnosticResults(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    List<DiagnosticResult>? Results
    );