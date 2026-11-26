using GamePlatform.LoadBalancer.Health;
using GamePlatform.LoadBalancer.Routing;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace GamePlatform.LoadBalancer.Proxy;

[ApiController]
[Route("{**path}")]
public sealed class ProxyController : ControllerBase
{
    private static readonly HashSet<string> HopByHopHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Keep-Alive",
            "Proxy-Authenticate",
            "Proxy-Authorization",
            "TE",
            "Trailer",
            "Transfer-Encoding",
            "Upgrade"
        };

    private readonly ServerHealthMonitor _healthMonitor;
    private readonly IServerSelector _selector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(
        ServerHealthMonitor healthMonitor,
        IServerSelector selector,
        IHttpClientFactory httpClientFactory,
        ILogger<ProxyController> logger)
    {
        _healthMonitor = healthMonitor;
        _selector = selector;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [AcceptVerbs(
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS")]
    public async Task Proxy(
        string? path,
        CancellationToken cancellationToken)
    {
        var servers = _healthMonitor.GetHealthyServers();

        if (servers.Count == 0)
        {
            Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;

            await Response.WriteAsJsonAsync(
                new
                {
                    error = "No healthy game servers available."
                },
                cancellationToken);

            return;
        }

        var server = _selector.Select(servers);

        var requestId =
            Request.Headers["X-Request-Id"]
                .FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        var targetUrl =
            $"{server.BaseUrl.TrimEnd('/')}/{path}";

        if (Request.QueryString.HasValue)
        {
            targetUrl += Request.QueryString.Value;
        }

        using var client =
            _httpClientFactory.CreateClient("proxy");

        using var message = new HttpRequestMessage(
            new HttpMethod(Request.Method),
            targetUrl);

        /*
         * Forward request body.
         */
        if (Request.ContentLength is > 0 ||
            Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            message.Content =
                new StreamContent(Request.Body);

            /*
             * Content headers belong on HttpContent,
             * not HttpRequestMessage.Headers.
             */
            foreach (var header in Request.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                {
                    continue;
                }

                if (header.Key.Equals(
                        "Content-Length",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (header.Key.Equals(
                        "Host",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (header.Key.Equals(
                        "X-Request-Id",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (header.Key.StartsWith(
                        "Content-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    message.Content.Headers.TryAddWithoutValidation(
                        header.Key,
                        header.Value.ToArray());
                }
            }
        }

        /*
         * Forward normal request headers.
         */
        foreach (var header in Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            if (header.Key.Equals(
                    "Host",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Key.Equals(
                    "Content-Length",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Key.Equals(
                    "X-Request-Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Key.StartsWith(
                    "Content-",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value.ToArray());
        }

        /*
         * Preserve the request ID across the proxy.
         */
        message.Headers.TryAddWithoutValidation(
            "X-Request-Id",
            requestId);

        var started = Stopwatch.GetTimestamp();

        try
        {
            _logger.LogInformation(
                "FORWARD {Method} {Path} RequestId={RequestId} Server={Server} Target={TargetUrl}",
                Request.Method,
                path,
                requestId,
                server.Name,
                targetUrl);

            _logger.LogWarning(
                    "PROXY OUT: Method={Method}, URL={Url}",
                    message.Method,
                    message.RequestUri);

            using var downstreamResponse =
                await client.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            Response.StatusCode =
                (int)downstreamResponse.StatusCode;

            /*
             * Copy response headers, but never copy
             * hop-by-hop transport headers.
             *
             * Kestrel owns Transfer-Encoding,
             * Connection, etc.
             */
            foreach (var header in downstreamResponse.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                {
                    continue;
                }

                Response.Headers[header.Key] =
                    header.Value.ToArray();
            }

            foreach (var header in
                     downstreamResponse.Content.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                {
                    continue;
                }

                if (header.Key.Equals(
                        "Content-Length",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Response.Headers[header.Key] =
                    header.Value.ToArray();
            }

            await downstreamResponse.Content.CopyToAsync(
                Response.Body,
                cancellationToken);

            var elapsed =
                Stopwatch.GetElapsedTime(started);

            _logger.LogInformation(
                "COMPLETED RequestId={RequestId} Server={Server} Status={StatusCode} DurationMs={DurationMs}",
                requestId,
                server.Name,
                Response.StatusCode,
                elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var elapsed =
                Stopwatch.GetElapsedTime(started);

            _logger.LogWarning(
                "CANCELLED RequestId={RequestId} Server={Server} DurationMs={DurationMs}",
                requestId,
                server.Name,
                elapsed.TotalMilliseconds);

            throw;
        }
        catch (HttpRequestException ex)
        {
            var elapsed =
                Stopwatch.GetElapsedTime(started);

            _logger.LogWarning(
                ex,
                "DOWNSTREAM FAILURE RequestId={RequestId} Server={Server} DurationMs={DurationMs}",
                requestId,
                server.Name,
                elapsed.TotalMilliseconds);

            Response.StatusCode =
                StatusCodes.Status502BadGateway;

            await Response.WriteAsJsonAsync(
                new
                {
                    error = "Game server unavailable.",
                    requestId,
                    server = server.Name
                },
                cancellationToken);
        }
    }
}
