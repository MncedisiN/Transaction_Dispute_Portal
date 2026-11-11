using System.Diagnostics.Metrics;

namespace DisputePortal.Api.Observability;

public sealed class HttpMetricListener(MetricAggregator aggregation, EndpointMetricCatalog routes, ILogger<HttpMetricListener> logger) : IHostedService, IDisposable
{
    private MeterListener? listener;

    public Task StartAsync(CancellationToken ct)
    {
        listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "Microsoft.AspNetCore.Hosting" && instrument.Name == "http.server.request.duration")
            {
                owner.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
        {
            try
            {
                string? method = null, route = null;
                var status = 0;
                var error = false;
                foreach (var tag in tags)
                    switch (tag.Key)
                    {
                        case "http.request.method":
                            method = tag.Value as string;
                            break;
                        case "http.route":
                            route = tag.Value as string;
                            break;
                        case "http.response.status_code":
                            if (tag.Value is int code)
                            {
                                status = code;
                            }

                            break;
                        case "error.type":
                            error = tag.Value is not null;
                            break;
                    }

                aggregation.Observe(EndpointMetricCatalog.Method(method), routes.Normalize(route), status, measurement, error);
            }
            catch
            { /* A diagnostics subscriber must never interrupt a request. */
            }
        });
        listener.Start();
        logger.LogInformation("Built-in HTTP metric listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref listener, null)?.Dispose();
    }
}
