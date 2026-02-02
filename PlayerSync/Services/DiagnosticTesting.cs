using MareSynchronos.Services.Models;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace MareSynchronos.Services
{
    public static class DiagnosticTesting
    {
        private const int PingAttemptCount = 4;
        private const int PingTimeoutMilliseconds = 3000;
        private const long CloudflareDownloadBytes = 25_000_000;
        private const long CloudflareUploadBytes = 10_000_000;
        private static readonly Uri CloudflareDownloadUriBase = new("https://speed.cloudflare.com/__down", UriKind.Absolute);
        private static readonly Uri CloudflareUploadUriBase = new("https://speed.cloudflare.com/__up", UriKind.Absolute);

        private static readonly IReadOnlyList<string> TestEndpoints = new[]
        {
            "sync.playersync.io",
            "playersync.io",
            "fs-na-01.playersync.io",
            "54cfada9aee2c44d7e67f5769a0a9301.r2.cloudflarestorage.com",
        };

        private static async Task<DnsDiagnosticResult> ExecuteDnsTestAsync(string targetValue, CancellationToken cancellationToken)
        {
            try
            {
                IPAddress[] resolvedIpAddresses = await Dns.GetHostAddressesAsync(targetValue, cancellationToken).ConfigureAwait(false);
                var resolvedIpAddressesValues = resolvedIpAddresses.Select(ipAddress => ipAddress.ToString()).ToArray();

                var resultText = resolvedIpAddressesValues.Length == 0 ? "No addresses returned" : $"Resolved: {string.Join(", ", resolvedIpAddressesValues)}";

                return new DnsDiagnosticResult(
                    State: DiagnosticsTestState.Passed,
                    ResolvedIpAddresses: resolvedIpAddressesValues,
                    TargetValue: targetValue,
                    Details: resultText
                );
            }
            catch (Exception ex)
            {
                return new DnsDiagnosticResult(
                    State: DiagnosticsTestState.Failed,
                    TargetValue: targetValue,
                    ErrorMessage: ex.ToString()
                );
            }
            
        }

        private static async Task<PingDiagnosticResult> ExecutePingTestAsync(string targetValue, CancellationToken cancellationToken)
        {
            List<long> roundTripTimes = new();
            int pingReplyCount = 0;

            try
            {
                using Ping pingSender = new();

                for (int pingAttempt = 0; pingAttempt < PingAttemptCount; pingAttempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    PingReply pingReply = await pingSender.SendPingAsync(targetValue, PingTimeoutMilliseconds).ConfigureAwait(false);

                    if (pingReply.Status == IPStatus.Success)
                    {
                        pingReplyCount++;
                        roundTripTimes.Add(pingReply.RoundtripTime);
                    }

                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }

                double? pingMinMs = roundTripTimes.Count > 0 ? roundTripTimes.Min() : null;
                double? pingMaxMs = roundTripTimes.Count > 0 ? roundTripTimes.Max() : null;
                double? pingAvgMs = roundTripTimes.Count > 0 ? roundTripTimes.Average() : null;

                var detailsText = roundTripTimes.Count == 0 ? "Ping timed out." : $"min={pingMinMs:0}ms avg={pingAvgMs:0}ms max={pingMaxMs:0}ms replies={pingReplyCount}/{PingAttemptCount}";

                return new PingDiagnosticResult(
                    State: roundTripTimes.Count == 0 ? DiagnosticsTestState.Failed : DiagnosticsTestState.Passed,
                    TargetValue: targetValue,
                    PingAttemptCount: PingAttemptCount,
                    PingSuccessfulReplyCount: pingReplyCount,
                    PingMinimumMilliseconds: pingMinMs,
                    PingAverageMilliseconds: pingAvgMs,
                    PingMaximumMilliseconds: pingMaxMs,
                    Details: detailsText
                );

            }
            catch (Exception ex)
            {
                return new PingDiagnosticResult(
                    State: DiagnosticsTestState.Failed,
                    TargetValue: targetValue,
                    ErrorMessage: ex.ToString()
                );
            }

        }

        private static async Task<SpeedTestDiagnosticResult> ExecuteDownloadSpeedTestAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            long measurementIdentifier = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            UriBuilder uriBuilder = new(CloudflareDownloadUriBase);
            string queryString = $"measId={measurementIdentifier}&bytes={CloudflareDownloadBytes}";
            uriBuilder.Query = queryString;
            Uri cloudflareDownloadRequestUri = uriBuilder.Uri;

            Stopwatch stopwatch = Stopwatch.StartNew();

            using HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(cloudflareDownloadRequestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            httpResponseMessage.EnsureSuccessStatusCode();

            long totalBytesRead = 0;
            byte[] readBuffer = ArrayPool<byte>.Shared.Rent(128 * 1024);

            try
            {
                using Stream responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    int bytesReadThisIteration = await responseStream.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), cancellationToken).ConfigureAwait(false);
                    if (bytesReadThisIteration <= 0)
                        break;

                    totalBytesRead += bytesReadThisIteration;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
            }

            stopwatch.Stop();

            double elapsedSeconds = Math.Max(0.0001, stopwatch.Elapsed.TotalSeconds);
            double megabitsPerSecond = (totalBytesRead * 8d) / elapsedSeconds / 1_000_000d;

            string detailsText = $"{megabitsPerSecond:0.0} Mbps ({totalBytesRead:N0} bytes in {elapsedSeconds:0.00}s)";

            return new SpeedTestDiagnosticResult(
                State: DiagnosticsTestState.Passed,
                SpeedTestKind: DiagnosticsTestKind.CloudflareDownloadSpeed,
                TargetValue: "Cloudflare Speed Test Download",
                TransferBytes: totalBytesRead,
                TransferSeconds: elapsedSeconds,
                TransferMegabitsPerSecond: megabitsPerSecond,
                Details: detailsText
                );
        }

        private static async Task<SpeedTestDiagnosticResult> ExecuteUploadSpeedTestAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            long measurementIdentifier = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            UriBuilder uriBuilder = new(CloudflareUploadUriBase);
            string queryString = $"measId={measurementIdentifier}";
            uriBuilder.Query = queryString;
            Uri cloudflareUploadRequestUri = uriBuilder.Uri;

            using GeneratedByteStreamContent uploadContent = new(CloudflareUploadBytes);

            Stopwatch stopwatch = Stopwatch.StartNew();
            using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(cloudflareUploadRequestUri, uploadContent, cancellationToken).ConfigureAwait(false);
            httpResponseMessage.EnsureSuccessStatusCode();
            stopwatch.Stop();

            double elapsedSeconds = Math.Max(0.0001, stopwatch.Elapsed.TotalSeconds);
            double megabitsPerSecond = (CloudflareUploadBytes * 8d) / elapsedSeconds / 1_000_000d;

            string detailsText = $"{megabitsPerSecond:0.0} Mbps ({CloudflareUploadBytes:N0} bytes in {elapsedSeconds:0.00}s)";

            return new SpeedTestDiagnosticResult(
                State: DiagnosticsTestState.Passed,
                SpeedTestKind: DiagnosticsTestKind.CloudflareUploadSpeed,
                TargetValue: "Cloudflare Speed Test Upload",
                TransferBytes: CloudflareUploadBytes,
                TransferSeconds: elapsedSeconds,
                TransferMegabitsPerSecond: megabitsPerSecond,
                Details: detailsText
                );
        }

        private sealed class GeneratedByteStreamContent : HttpContent
        {
            private readonly long contentLengthBytes;
            private readonly int writeBufferSizeBytes;

            public GeneratedByteStreamContent(long contentLengthBytes, int writeBufferSizeBytes = 128 * 1024)
            {
                this.contentLengthBytes = contentLengthBytes;
                this.writeBufferSizeBytes = writeBufferSizeBytes;

                Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                Headers.ContentLength = contentLengthBytes;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return SerializeToStreamAsync(stream, context, CancellationToken.None);
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                byte[] writeBuffer = ArrayPool<byte>.Shared.Rent(writeBufferSizeBytes);
                try
                {
                    long remainingBytesToWrite = contentLengthBytes;
                    while (remainingBytesToWrite > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int bytesToWriteThisIteration = (int)Math.Min(remainingBytesToWrite, writeBufferSizeBytes);
                        await stream.WriteAsync(writeBuffer.AsMemory(0, bytesToWriteThisIteration), cancellationToken).ConfigureAwait(false);
                        remainingBytesToWrite -= bytesToWriteThisIteration;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(writeBuffer);
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = contentLengthBytes;
                return true;
            }
        }

        private static string CreateDiagnosticsJson(FinalDiagnosticResults results)
        {
            JsonSerializerOptions jsonSerializerOptions = new()
            {
                WriteIndented = true,
            };

            if (results.Results == null || results.Results.Count == 0)
            {
                return "No tests run.";
            }

            return JsonSerializer.Serialize(results, jsonSerializerOptions);
        }

        /// <summary>
        /// Run all the tests and return the results as a json string
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="httpClient"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="OperationCanceledException"></exception>
        public static async Task<string> RunAllDiagnosticTests(IProgress<string> progress, HttpClient httpClient, CancellationToken cancellationToken)
        {
            List<DiagnosticResult> diagnosticResults = new();
            var runStart = DateTimeOffset.UtcNow;

            // DNS testing
            foreach (var endpoint in TestEndpoints)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                progress.Report($"DNS Test: {endpoint}");
                var result = await ExecuteDnsTestAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (result != null)
                    diagnosticResults.Add(result);
            }

            // Ping testing
            foreach (var endpoint in TestEndpoints)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                progress.Report($"Ping Test: {endpoint}");
                var result = await ExecutePingTestAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (result != null)
                    diagnosticResults.Add(result);
            }

            // download test
            progress.Report("Cloudflare Download Speed Test");
            var downloadResult = await ExecuteDownloadSpeedTestAsync(httpClient, cancellationToken).ConfigureAwait(false);
            if (downloadResult != null)
                diagnosticResults.Add(downloadResult);

            // upload test
            progress.Report("Cloudflare Upload Speed Test");
            var uploadResult = await ExecuteUploadSpeedTestAsync(httpClient, cancellationToken).ConfigureAwait(false);
            if (uploadResult != null)
                diagnosticResults.Add(uploadResult);

            var runEnd = DateTimeOffset.UtcNow;

            var results = new FinalDiagnosticResults(
                StartTime: runStart,
                EndTime: runEnd,
                Results: diagnosticResults
                );

            return CreateDiagnosticsJson(results);
        }
    }
}
