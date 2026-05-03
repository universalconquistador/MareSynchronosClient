using DnsClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace PlayerSync.WebAPI.SignalR
{
    public class GatewayResult
    {
        public required Uri GatewayUri { get; init; }
        public required Uri StatusUri { get; init; }
        public required int StatusMs { get; init; }
        public required int RequestMs { get; init; }
        public required int TotalMs { get; init; }
        public required int Priority { get; init; }
    }

    public class GatewayManager
    {
        private const string GatewaySubDomain = "gateways";
        private const string GatewayStatus = "gateway-status";
        private const int DelayVariance = 100;

        private ILogger Logger { get; }

        public GatewayManager(ILogger logger) 
        {
            Logger = logger;
        }

        public async Task<Uri?> GetServiceGatewayUri(Uri serviceUri, CancellationToken ct = default)
        {
            string host = serviceUri.Host;
            string domain = string.Join('.', host.Split('.').Skip(1));

            Logger.LogTrace("{service} Host: {host} Domain: {domain}", nameof(GatewayManager), host, domain);

            var hosts = await GetTxtRecordPartsAsync($"{GatewaySubDomain}.{domain}", ct).ConfigureAwait(false);

            Logger.LogTrace("{service} Record Entries: {entries}", nameof(GatewayManager), string.Join(',', hosts));

            var serviceGateways = MakeServiceGatewaysFromHosts(hosts, domain);

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(2000)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PlayerSync");

            Logger.LogTrace("{service} Checking gateways for availability", nameof(GatewayManager));

            var tasks = serviceGateways.Select(uri => CheckGatewayAsync(httpClient, uri, ct)).ToList();

            GatewayResult?[] results = await Task.WhenAll(tasks).ConfigureAwait(false); // can take up to 2000ms

            var validResults = results.Where(result => result is not null).ToList();

            Logger.LogTrace("{service} Number of valid gateways: {number}", nameof(GatewayManager), validResults.Count);

            if (validResults.Count == 0)
                return null;

            // get the lowest trip time
            int lowestTotalMs = validResults.Min(result => result!.TotalMs);

            // get the lowest trip time unless there is a higher priority (lower priority value) within 30ms of the lowest
            var bestGateway = validResults.Where(result => result!.TotalMs <= lowestTotalMs + DelayVariance).OrderBy(result => result!.Priority).ThenBy(result => result!.TotalMs).First();
            if (bestGateway == null) return null;

            Logger.LogDebug("{service} Resolved best gateway: {gateway}", nameof(GatewayManager), bestGateway.GatewayUri.Host);

            return new($"wss://{bestGateway.GatewayUri.Host}");
        }

        public static async Task<List<string>> GetListOfServiceGateways(Uri serviceUri, CancellationToken ct = default)
        {
            var gatewayList = new List<string>();

            string host = serviceUri.Host;
            string domain = string.Join('.', host.Split('.').Skip(1));

            var hosts = await GetTxtRecordPartsAsync($"{GatewaySubDomain}.{domain}", ct).ConfigureAwait(false);
            var serviceGateways = MakeServiceGatewaysFromHosts(hosts, domain);

            foreach (var gateway in serviceGateways)
            {
                gatewayList.Add(gateway.Host.Split('.')[0]);
            }

            return gatewayList;
        }

        private static async Task<List<string>> GetTxtRecordPartsAsync(string hostName, CancellationToken ct = default)
        {
            var dnsClient = new LookupClient();

            var queryResult = await dnsClient.QueryAsync(hostName, QueryType.TXT, cancellationToken: ct).ConfigureAwait(false);

            return queryResult.Answers
                .TxtRecords()
                .SelectMany(txtRecord => txtRecord.Text)
                .SelectMany(txtValue => txtValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();
        }

        private static List<Uri> MakeServiceGatewaysFromHosts(List<string> hosts, string domain)
        {
            var uris = new List<Uri>();
            foreach (var host in hosts)
            {
                Uri uri = new Uri($"https://{host}.{domain}");
                uris.Add(uri);
            }

            return uris;
        }

        private async Task<GatewayResult?> CheckGatewayAsync(HttpClient httpClient, Uri gatewayUri, CancellationToken ct)
        {
            Uri statusUri = new Uri(gatewayUri, $"/{GatewayStatus}");

            Logger.LogTrace("{service} Checking gateway: {gateway}", nameof(GatewayManager), statusUri.ToString());

            using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestTimeoutCts.CancelAfter(TimeSpan.FromMilliseconds(2000)); // we don't care about anything taking > 2seconds to reply

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(statusUri, requestTimeoutCts.Token).ConfigureAwait(false);

                stopwatch.Stop(); // grab the time it took for the GET request, basically the web RTT

                if (response.StatusCode != HttpStatusCode.OK)
                    return null;

                string json = await response.Content.ReadAsStringAsync(requestTimeoutCts.Token).ConfigureAwait(false);

                Logger.LogTrace("{service} Status code: {status} Body: {body}", nameof(GatewayManager), response.StatusCode, json.ToString());

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("status", out JsonElement statusElement))
                    return null;

                if (!root.TryGetProperty("priority", out JsonElement priorityElement))
                    return null;

                int statusMs = statusElement.GetInt32();
                int priority = priorityElement.GetInt32();

                if (priority <= 0)
                    return null;

                if (statusMs == -1 || statusMs == 9999) // -1 and 9999 mean the gateway can't accept requests or it can't get to the sync service
                    return null;

                if (statusMs < 0 || statusMs > 2000) // should never happen with ct but we check anyway
                    return null;

                int requestMs = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);

                Logger.LogTrace("{service} Gateway: {gateway}, Request: {time}ms", nameof(GatewayManager), statusUri.ToString(), requestMs.ToString());

                return new GatewayResult
                {
                    GatewayUri = gatewayUri,
                    StatusUri = statusUri,
                    StatusMs = statusMs,
                    RequestMs = requestMs,
                    TotalMs = statusMs + requestMs,
                    Priority = priority
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to get gateway status for {gateway}!", statusUri.ToString());
                return null;
            }
        }
    }
}
