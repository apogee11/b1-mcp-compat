using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace B1.Mcp.Compat.Host;

/// <summary>Wires the log file (Compat__Log__Path, Compat__Log__Level). Two levels only: info (everything) and error.</summary>
public static class LogSetup
{
    public static void Configure(ILoggingBuilder logging, CompatOptions.LogOptions o)
    {
        var level = o.Level.Trim().ToLowerInvariant() switch
        {
            "info" => LogLevel.Information,
            "error" => LogLevel.Error,
            _ => throw new InvalidOperationException($"Compat__Log__Level '{o.Level}' is not valid: use 'info' or 'error'."),
        };
        var dir = string.IsNullOrWhiteSpace(o.Path) ? AppContext.BaseDirectory : CompatOptions.Resolve(o.Path);
        try
        {
            Directory.CreateDirectory(dir);
            using (File.Create(Path.Combine(dir, ".write-test"), 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException($"Compat__Log__Path '{dir}' cannot be written: {e.Message}");
        }
        logging.SetMinimumLevel(level);
        logging.AddProvider(new FileLoggerProvider(dir));
    }
}

/// <summary>
/// One JSON object per line, shaped like the OpenTelemetry log data model (timestamp, severity_text, severity_number, body, trace_id, span_id, resource, attributes),
/// in a file per day that is kept for 14 days. Structured state (the wide event of a request) becomes the attributes.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 14;
    private const string Prefix = "b1-mcp-compat-";
    private readonly string _dir;
    private readonly string _version = typeof(CompatOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateOnly _day;

    public FileLoggerProvider(string dir) => _dir = dir;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_lock) { _writer?.Dispose(); _writer = null; }
    }

    private sealed class FileLogger(FileLoggerProvider p, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            p.Write(category, logLevel, formatter(state, exception), state as IEnumerable<KeyValuePair<string, object?>>, exception);
        }
    }

    private void Write(string category, LogLevel level, string message, IEnumerable<KeyValuePair<string, object?>>? attributes, Exception? ex)
    {
        var error = level >= LogLevel.Error;
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            j.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O"));
            j.WriteString("severity_text", error ? "ERROR" : "INFO");
            j.WriteNumber("severity_number", error ? 17 : 9);
            j.WriteString("body", message);
            if (Activity.Current is { } a)
            {
                j.WriteString("trace_id", a.TraceId.ToString());
                j.WriteString("span_id", a.SpanId.ToString());
            }
            j.WriteStartObject("resource");
            j.WriteString("service.name", "b1-mcp-compat");
            j.WriteString("service.version", _version);
            j.WriteString("host.name", Environment.MachineName);
            j.WriteNumber("process.pid", Environment.ProcessId);
            j.WriteEndObject();
            j.WriteStartObject("attributes");
            j.WriteString("log.category", category);
            foreach (var (k, v) in attributes ?? [])
            {
                if (k == "{OriginalFormat}") continue;
                switch (v)
                {
                    case null: j.WriteNull(k); break;
                    case string s: j.WriteString(k, s); break;
                    case bool b: j.WriteBoolean(k, b); break;
                    case int i: j.WriteNumber(k, i); break;
                    case long l: j.WriteNumber(k, l); break;
                    case double d: j.WriteNumber(k, d); break;
                    default: j.WriteString(k, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)); break;
                }
            }
            if (ex != null)
            {
                j.WriteString("exception.type", ex.GetType().FullName);
                j.WriteString("exception.message", ex.Message);
                j.WriteString("exception.stacktrace", ex.ToString());
            }
            j.WriteEndObject();
            j.WriteEndObject();
        }
        ms.WriteByte((byte)'\n');
        lock (_lock)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_writer == null || today != _day) Rotate(today);
                _writer!.BaseStream.Write(ms.GetBuffer(), 0, (int)ms.Length);
                _writer.BaseStream.Flush();
            }
            catch (IOException) { /* a full disk must not take the proxy down */ }
        }
    }

    private void Rotate(DateOnly today)
    {
        _writer?.Dispose();
        _day = today;
        _writer = new StreamWriter(new FileStream(Path.Combine(_dir, $"{Prefix}{today:yyyy-MM-dd}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, Prefix + "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-RetainDays)) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* retention is best effort */ }
    }
}
