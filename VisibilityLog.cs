using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexBar;

internal sealed class VisibilityLog
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBar", "visibility.log");
    private readonly string path;
    private readonly long maxBytes;
    private readonly object gate = new();
    private readonly string session = Guid.NewGuid().ToString("N");
    private long sequence;
    public string? LastError { get; private set; }

    public VisibilityLog(string? path = null, long maxBytes = 256 * 1024)
    {
        this.path = path ?? DefaultPath;
        this.maxBytes = maxBytes;
    }

    public bool Write(string eventName, string details)
    {
        lock (gate)
        {
            try
            {
                var line = JsonSerializer.Serialize(new
                {
                    time = DateTimeOffset.Now, session, sequence = ++sequence,
                    pid = Environment.ProcessId, eventName, details
                }) + Environment.NewLine;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > maxBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, line, new UTF8Encoding(false));
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                LastError = $"{ex.GetType().Name} (0x{ex.HResult:X8})";
                Trace.WriteLine($"CodexBar: visibility diagnostic write failed: {LastError}");
                return false;
            }
        }
    }
}
