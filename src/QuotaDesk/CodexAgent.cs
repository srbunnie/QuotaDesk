using System.Text;

namespace AIUsageMonitor;

/// <summary>
/// Codex: quota via app-server, activity inferred from the local session logs (~/.codex/sessions/*.jsonl).
/// Only the event type markers near the end of the changed file are inspected; message content is not kept.
/// </summary>
internal sealed class CodexAgent : IAgentSource
{
    private static readonly TimeSpan PollVisible = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollHidden = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinRefreshGap = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WorkingSilence = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WaitingTimeout = TimeSpan.FromMinutes(15);

    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _pollTimer;
    private readonly System.Threading.Timer _debounce;
    private readonly object _gate = new();
    private readonly HashSet<string> _dirtyFiles = [];
    private readonly Dictionary<string, (ActivityState State, DateTimeOffset At, string Detail, string? Action)> _files = [];
    private Dictionary<string, string> _titles = [];
    private DateTime _titlesRead;
    private ProviderSnapshot _quota = ProviderSnapshot.Unavailable("Codex", L.T("Consultando...", "Checking..."), "codex app-server");
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private int _refreshing;
    private bool _visible;
    public event Action? Changed;
    public string Id => "codex";

    public static string SessionsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public CodexAgent()
    {
        _debounce = new System.Threading.Timer(_ => ProcessDirty());
        _pollTimer = new System.Threading.Timer(_ => _ = RefreshQuotaAsync(false), null, PollHidden, PollHidden);
        if (Directory.Exists(SessionsDirectory))
        {
            _watcher = new FileSystemWatcher(SessionsDirectory, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 16384,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, e) => MarkDirty(e.FullPath);
            _watcher.Created += (_, e) => MarkDirty(e.FullPath);
            // Pick up a session that was already running when the monitor started.
            try
            {
                // Windows may not refresh the modified time while Codex keeps the log open, so look at a
                // few recent files and take the time from the events themselves.
                foreach (var file in new DirectoryInfo(SessionsDirectory).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                             .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromDays(3))
                             .OrderByDescending(f => f.LastWriteTimeUtc).Take(12))
                    MarkDirty(file.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void SetForeground(bool visible)
    {
        _visible = visible;
        var period = visible ? PollVisible : PollHidden;
        try { _pollTimer.Change(period, period); } catch (ObjectDisposedException) { }
    }

    private void MarkDirty(string path)
    {
        lock (_gate) _dirtyFiles.Add(path);
        // Watcher events already in flight can arrive after Dispose; on a pool thread that exception would end the app.
        try { _debounce.Change(700, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }

    private void ProcessDirty()
    {
        string[] files;
        lock (_gate) { files = [.. _dirtyFiles]; _dirtyFiles.Clear(); }
        var completed = false;
        foreach (var file in files)
        {
            var result = InspectTail(file);
            if (result is null) continue;
            lock (_gate)
            {
                var had = _files.TryGetValue(file, out var previous);
                _files[file] = result.Value;
                if (result.Value.State == ActivityState.Waiting && (!had || previous.State == ActivityState.Working)) completed = true;
            }
        }
        Changed?.Invoke();
        // A finished task is the moment usage changes: refresh quota (rate limited).
        if (completed) _ = RefreshQuotaAsync(false);
    }

    // Completed items in the log, newest wins; only the type name is matched, never the item's content.
    private static readonly (string Marker, ActionKind Kind)[] ActionMarkers =
    [
        ("\"type\":\"CommandExecution\"", ActionKind.Command),
        ("\"type\":\"FileChange\"", ActionKind.Edit),
        ("\"type\":\"McpToolCall\"", ActionKind.Connector),
        ("\"type\":\"web_search_call\"", ActionKind.Web),
        ("\"type\":\"Reasoning\"", ActionKind.Thinking)
    ];

    private static (ActivityState State, DateTimeOffset At, string Detail, string? Action)? InspectTail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var take = (int)Math.Min(length, 96 * 1024);
            stream.Seek(length - take, SeekOrigin.Begin);
            var buffer = new byte[take];
            stream.ReadExactly(buffer);
            var text = Encoding.UTF8.GetString(buffer);
            var lastWrite = LastEventTime(text) ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            int Last(string marker) => text.LastIndexOf(marker, StringComparison.Ordinal);
            var started = Last("\"type\":\"task_started\"");
            var complete = Last("\"type\":\"task_complete\"");
            var aborted = Last("\"type\":\"turn_aborted\"");
            var approval = Math.Max(Last("_approval_request\""), Last("\"type\":\"request_user_input\""));
            var end = Math.Max(complete, aborted);
            var step = ActionMarkers.Select(m => (At: Last(m.Marker), m.Kind)).MaxBy(m => m.At);
            var action = step.At >= 0 ? AgentActions.Describe(step.Kind, true) : L.T("Trabalhando…", "Working…");
            if (approval > started && approval > end) return (ActivityState.Waiting, lastWrite, L.T("aguardando sua aprovação", "waiting for your approval"), L.T("Precisa da sua aprovação", "Needs your approval"));
            if (started > end) return (ActivityState.Working, lastWrite, L.T("executando tarefa", "running a task"), action);
            if (end >= 0) return complete > aborted
                ? (ActivityState.Waiting, lastWrite, L.T("tarefa concluída · aguardando você", "task done · waiting for you"), L.T("Terminou · sua vez", "Done · your turn"))
                : (ActivityState.Waiting, lastWrite, L.T("tarefa interrompida", "task interrupted"), L.T("Tarefa interrompida", "Task interrupted"));
            // Tail without markers (very long turn): recent writes still mean work in progress.
            return (ActivityState.Working, lastWrite, L.T("executando tarefa", "running a task"), action);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static DateTimeOffset? LastEventTime(string text)
    {
        const string key = "\"timestamp\":\"";
        var at = text.LastIndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + key.Length;
        var end = text.IndexOf('"', start);
        return end > start && end - start < 40 && DateTimeOffset.TryParse(text.AsSpan(start, end - start),
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;
    }

    public AgentStatus GetStatus(DateTimeOffset now)
    {
        (ActivityState State, DateTimeOffset At, string Detail, string? Action)? best = null;
        string? bestFile = null;
        lock (_gate)
        {
            foreach (var key in _files.Keys.ToList())
            {
                var entry = _files[key];
                if (entry.State == ActivityState.Working && now - entry.At > WorkingSilence)
                    entry = (ActivityState.Idle, entry.At, L.T("sem atividade recente", "no recent activity"), null);
                else if (entry.State == ActivityState.Waiting && now - entry.At > WaitingTimeout)
                    entry = (ActivityState.Idle, entry.At, L.T("sessão parada", "session stopped"), null);
                if (entry.State == ActivityState.Idle && now - entry.At > TimeSpan.FromHours(2)) { _files.Remove(key); continue; }
                if (best is null || entry.State > best.Value.State || (entry.State == best.Value.State && entry.At > best.Value.At))
                    (best, bestFile) = (entry, key);
            }
        }
        var activity = best is { Action: { } action } && bestFile is not null
            ? new AgentActivity(ThreadTitle(bestFile), action, best.Value.At) : null;
        var state = best?.State ?? ActivityState.Idle;
        var detail = best?.Detail ?? L.T("nenhuma tarefa recente", "no recent task");
        var source = _watcher is null ? L.T("pasta de sessões do Codex não encontrada", "Codex sessions folder not found")
            : best is null ? L.T("inferido do log local de sessões", "inferred from the local session log")
            : L.T($"inferido do log local · {LimitParsing.Ago(now - best.Value.At)}", $"inferred from the local log · {LimitParsing.Ago(now - best.Value.At)}");
        // The same quota is spent by Codex and by the Work space (only plain chat does not count).
        return new AgentStatus(Id, "ChatGPT Codex", state, true, detail, source, _quota) { Activity = activity };
    }

    /// <summary>
    /// The conversation title Codex shows in its own list (session_index.jsonl: id → thread_name), matched by the id at the
    /// end of the log file name. Re-read only when the index changes; the title is shown, never stored.
    /// </summary>
    private string? ThreadTitle(string logPath)
    {
        var name = Path.GetFileNameWithoutExtension(logPath);
        if (name.Length < 36) return null;
        var id = name[^36..];
        var index = Path.Combine(Path.GetDirectoryName(SessionsDirectory)!, "session_index.jsonl");
        try
        {
            var stamp = File.GetLastWriteTimeUtc(index);
            if (stamp != _titlesRead && File.Exists(index))
            {
                var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var stream = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 8 * 1024 * 1024) return null;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    try
                    {
                        using var json = System.Text.Json.JsonDocument.Parse(line);
                        var root = json.RootElement;
                        if (root.TryGetProperty("id", out var i) && i.ValueKind == System.Text.Json.JsonValueKind.String &&
                            root.TryGetProperty("thread_name", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String &&
                            AgentActions.CleanTitle(t.GetString()) is { } title)
                            titles[i.GetString()!] = title; // later lines are renames: the last one wins
                    }
                    catch (System.Text.Json.JsonException) { }
                }
                (_titles, _titlesRead) = (titles, stamp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return _titles.GetValueOrDefault(id);
    }

    public async Task RefreshQuotaAsync(bool force)
    {
        if (!force && DateTimeOffset.Now - _lastRefresh < MinRefreshGap) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            _lastRefresh = DateTimeOffset.Now;
            var result = await CodexClient.ReadAsync(CancellationToken.None);
            // Keep the last good reading on a transient failure, but flag it.
            _quota = result.Windows.Count == 0 && _quota.Windows.Count > 0
                ? _quota with { Stale = true, Note = L.T($"{result.Error} Mostrando a última leitura.", $"{result.Error} Showing the last reading.") }
                : result;
            Changed?.Invoke();
        }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _pollTimer.Dispose();
        _debounce.Dispose();
    }
}
