using System.Text.Json;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;

namespace DisputePortal.Platform.Infrastructure.Logging; 
public sealed class RotatingApplicationLog : ILogEventSink, Serilog.Formatting.ITextFormatter, IDisposable, IAsyncDisposable
{
    private readonly PortalLogOptions options;
    private readonly string application;
    private readonly string environment;
    private readonly string filePrefix;
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly string directory;
    private readonly Channel<byte[]> queue;
    private readonly Task writer;
    private long dropped;
    public long DroppedEvents => Interlocked.Read(ref dropped);
    public string DirectoryPath => directory;

    public RotatingApplicationLog(
        PortalLogOptions options,
        string contentRoot,
        string applicationName = "DisputePortal.Api",
        string environmentName = "Development")
    {
        options.ValidateLogRetentionOptions();
        this.options = options;
        static string Label(string value) => value.Length is > 0 and <= 120 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? value : throw new ArgumentException("Invalid application logging label.");
        application = Label(applicationName);
        environment = Label(environmentName);
        filePrefix = "portal-" + application + "-";
        directory = Path.GetFullPath(options.Directory, contentRoot);
        queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        writer = WriteRotatedLogEntry();
    }

    private byte[]? Serialize(LogEvent value)
    {
        var fields = new Dictionary<string, object?>
        {
            ["timestamp"] = value.Timestamp.ToUniversalTime(),
            ["application"] = application,
            ["environment"] = environment,
            ["level"] = value.Level.ToString()
        };
        // Do not render arbitrary messages, exceptions, scopes, URLs or payloads.
        // Persist only the schema of operational properties below.
        foreach (var property in value.Properties)
        {
            if (property.Value is not ScalarValue scalar)
            {
                continue;
            }

            if (property.Key is "StatusCode" or "ElapsedMilliseconds" or "Rows" or "BufferedSeries" or "Dropped" or "ProcessorCount" or "ServerGC" or "Group")
            {
                if (scalar.Value is bool or byte or short or int or long or float or double or decimal)
                {
                    fields[property.Key] = scalar.Value;
                }
            }
            else if (scalar.Value is Guid diagnosticId && property.Key is "EventId" or "CorrelationId")
            {
                fields[property.Key == "EventId" ? "eventId" : "correlationId"] = diagnosticId.ToString("D");
            }
            else if (scalar.Value is string text)
            {
                if (property.Key == "SourceContext"
                    && (text.StartsWith("DisputePortal.", StringComparison.Ordinal)
                    || text == application)
                    && text.Length <= 180)
                {
                    fields["category"] = text;
                }
                else if (property.Key == "Method")
                {
                    fields["method"] = text is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "CONNECT" or "TRACE" ? text : "OTHER";
                }
                else if (property.Key == "Route" && text.Length <= 256 && !text.Contains('?') && !text.Contains('@'))
                {
                    fields["route"] = text;
                }
                else if (property.Key == "TraceId" && text.Length == 32 && text.All(Uri.IsHexDigit))
                {
                    fields["traceId"] = text;
                }
                else if (property.Key == "CorrelationId" && Guid.TryParseExact(text, "D", out var correlation))
                {
                    fields["correlationId"] = correlation.ToString("D");
                }
                else if (property.Key is "ExceptionType" or "FailureType" or "ErrorType"
                    && text.Length <= 100
                    && text.All(c => char.IsAsciiLetterOrDigit(c)
                    || c is '.' or '_'))
                {
                    fields["exceptionType"] = text;
                }
                else if (property.Key == "Operation"
                    && text.Length <= 100
                    && text.All(c => char.IsAsciiLetterOrDigit(c)
                    || c is '.' or '_' or '-'))
                {
                    fields["operation"] = text;
                }
            }
        }

        if (value.Properties.TryGetValue("SourceContext", out var source)
            && source is ScalarValue { Value: "Serilog.AspNetCore.RequestLoggingMiddleware" })
        {
            fields["category"] = "DisputePortal.Api.Http";
        }

        if (!fields.ContainsKey("category"))
        {
            return null;
        }

        if (value.Exception is { } error)
        {
            fields["exceptionType"] = error.GetType().Name;
            fields["stackMethods"] = new System.Diagnostics.StackTrace(error, false).GetFrames().Take(32).Select(frame => frame.GetMethod()).Where(method => method is not null).Select(method => ((method!.DeclaringType?.FullName ?? "") + "." + method.Name)[..Math.Min(
                256,
                (method.DeclaringType?.FullName?.Length ?? 0) + 1 + method.Name.Length)]).ToArray();
        }

        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (Serialize(logEvent) is { } line)
        {
            output.WriteLine(System.Text.Encoding.UTF8.GetString(line));
        }
    }

    public void Emit(LogEvent value)
    {
        try
        {
            if (Serialize(value) is { } line && !queue.Writer.TryWrite(line))
            {
                Interlocked.Increment(ref dropped);
            }
        }
        catch
        {
            Interlocked.Increment(ref dropped);
        }
    }

    private async Task WriteRotatedLogEntry()
    {
        FileStream? stream = null;
        DateOnly day = default;
        var number = 0;
        try
        {
            await foreach (var line in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    if (stream is null
                        || day != DateOnly.FromDateTime(now.UtcDateTime)
                        || stream.Length + line.Length + 1 > options.FileSizeBytes)
                    {
                        if (stream is not null)
                        {
                            await stream.DisposeAsync().ConfigureAwait(false);
                        }

                        stream = null;
                        System.IO.Directory.CreateDirectory(directory);
                        day = DateOnly.FromDateTime(now.UtcDateTime);
                        var path = Path.Combine(
                            directory,
                            $"{filePrefix}{now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{instance}-{++number:D4}.jsonl");
                        stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
                        Prune(path, now);
                    }

                    await stream.WriteAsync(line).ConfigureAwait(false);
                    await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Interlocked.Increment(ref dropped);
                    if (stream is not null)
                    {
                        try
                        {
                            await stream.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (IOException)
                        {
                        }

                        stream = null;
                    }
                }
            }
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void Prune(string current, DateTimeOffset now)
    {
        var files = new DirectoryInfo(directory).GetFiles(filePrefix + "*.jsonl").OrderByDescending(f => string.Equals(
            f.FullName,
            current,
            StringComparison.OrdinalIgnoreCase)).ThenByDescending(f => f.CreationTimeUtc).ThenByDescending(
            f => f.Name,
            StringComparer.Ordinal).ToArray();
        foreach (var file in files.Skip(options.RetainedFiles).Concat(files.Where(f => f.CreationTimeUtc < now.UtcDateTime.AddDays(-options.RetentionDays))).DistinctBy(f => f.FullName))
            if (!string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        try
        {
            await writer.WaitAsync(TimeSpan.FromSeconds(options.ShutdownTimeoutSeconds)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Interlocked.Increment(ref dropped);
        }
    }

    public void Dispose()
    {
        queue.Writer.TryComplete();
        try
        {
            if (!writer.Wait(TimeSpan.FromSeconds(options.ShutdownTimeoutSeconds)))
            {
                Interlocked.Increment(ref dropped);
            }
        }
        catch (AggregateException)
        {
        }
    }
}
