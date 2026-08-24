using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace MareSynchronos.WebAPI;

public class HttpClientProvider : IDisposable
{
    private HttpClient? _currentClient;

    // Called by whoever is currently directly accepting a HttpClient and using it
    public HttpClient GetHttpClient()
    {
        if (_currentClient == null)
        {
            RecreateHttpClient();
        }

        if (_currentClient == null)
        {
            throw new ObjectDisposedException(nameof(HttpClientProvider));
        }

        return _currentClient;
    }

    public void RecreateHttpClient(string? proxyUri = null)
    {
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,

            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };

        if (!string.IsNullOrWhiteSpace(proxyUri))
        {
            // this doesn't need to be encrypted as the traffic passing through the proxy itself is https
            Uri proxyServerUri = new Uri($"http://{proxyUri}:8080", UriKind.Absolute);
            var proxyServer = new WebProxy(proxyServerUri)
            {
                // setup credentials
                UseDefaultCredentials = false
            };

            handler.UseProxy = true;
            handler.Proxy = proxyServer;
        }

        var newClient = new HttpClient(handler, disposeHandler: true);
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        newClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlayerSync", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
        newClient.Timeout = Timeout.InfiniteTimeSpan; // we set CancellationToken timeouts for various requests

        var oldClient = Interlocked.Exchange(ref _currentClient, newClient); // Atomically swap in the new client and get out the previous client in a thread-safe manner

        oldClient?.CancelPendingRequests();
        oldClient?.Dispose();
    }

    public void Dispose()
    {
        HttpClient? currentClient;

        currentClient = Interlocked.Exchange(ref _currentClient, null);

        currentClient?.CancelPendingRequests();
        currentClient?.Dispose();
    }
}
