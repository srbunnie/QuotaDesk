using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageMonitor;

/// <summary>
/// Local HTTP receiver for Claude Code hooks (127.0.0.1 only). Hook bodies are parsed in memory for
/// session_id, tool_name, notification_type and the cwd folder name, then discarded; prompts and outputs are never read.
/// </summary>
internal sealed partial class ClaudeIntegration : IAgentSource
{
    public const int Port = 32871;

    private static readonly TimeSpan WorkingTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WaitingTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionForget = TimeSpan.FromHours(2);

    private sealed record SessionState(ActivityState State, DateTimeOffset At, string Detail, string? PendingKind = null, string? Project = null,
        string? Action = null);

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Last)> _eventCounts = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    private readonly FileSystemWatcher? _cacheWatcher;
    private FileSystemWatcher? _desktopWatcher;
    private readonly object _watchGate = new();
    // Hook payloads are small; a few parallel connections at most, each with a bounded body.
    private const int MaxBody = 512 * 1024;
    private readonly SemaphoreSlim _connections = new(8);
    public event Action? Changed;
    public string Id => "claude";
    public Func<object?>? ProviderDiagnostics { get; set; }
    public string ListenerError { get; private set; } = "";

    public ClaudeIntegration()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.ClaudeStatusLineDirectory);
        _cacheWatcher = Watch(AppPaths.ClaudeStatusLineDirectory, Path.GetFileName(ClaudeQuota.StatusLineCachePath));
        var desktopDir = Path.GetDirectoryName(ClaudeQuota.DesktopHistoryPath)!;
        if (Directory.Exists(desktopDir)) _desktopWatcher = Watch(desktopDir, Path.GetFileName(ClaudeQuota.DesktopHistoryPath));
        try
        {
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }
        catch (SocketException)
        {
            ListenerError = L.T($"Porta local {Port} ocupada: hooks do Claude indisponíveis.", $"Local port {Port} in use: Claude hooks unavailable.");
        }
    }

    private FileSystemWatcher Watch(string directory, string file)
    {
        var watcher = new FileSystemWatcher(directory, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += (_, _) => Changed?.Invoke();
        watcher.Created += (_, _) => Changed?.Invoke();
        watcher.Renamed += (_, _) => Changed?.Invoke();
        return watcher;
    }

    public Task RefreshQuotaAsync(bool force) { Changed?.Invoke(); return Task.CompletedTask; }

    public AgentStatus GetStatus(DateTimeOffset now)
    {
        var directory = Path.GetDirectoryName(ClaudeQuota.DesktopHistoryPath)!;
        // Called from the UI thread and from the listener (diagnostics): swap the watcher under a lock so none leaks.
        lock (_watchGate)
        {
            if (!_stop.IsCancellationRequested && Directory.Exists(directory) && _desktopWatcher?.Path != directory)
            {
                _desktopWatcher?.Dispose();
                _desktopWatcher = Watch(directory, "plan-usage-history.json");
            }
        }
        var quota = ClaudeQuota.Read(now);
        var (state, detail) = Aggregate(now);
        var source = ListenerError.Length > 0 ? ListenerError
            : _eventCounts.IsEmpty ? L.T("hooks do Claude Code · nenhum evento recebido ainda", "Claude Code hooks · no event received yet")
            : L.T($"hooks do Claude Code · último evento {LimitParsing.Ago(now - _eventCounts.Values.Max(x => x.Last))}",
                $"Claude Code hooks · last event {LimitParsing.Ago(now - _eventCounts.Values.Max(x => x.Last))}");
        // Chat and Cowork do not run Claude Code hooks and leave no real-time local signal. The desktop usage samples
        // (~every 15 min) only say usage rose at some point in the last interval, so they are shown as information,
        // never as the live state.
        if (ClaudeQuota.LastUsageRise is { } rise)
            source += L.T($" · último uso da cota detectado às {rise.LocalDateTime:HH:mm} (chat, Cowork ou Code)",
                $" · last limit usage seen at {rise.LocalDateTime:HH:mm} (chat, Cowork or Code)");
        // The bubble follows the session that is working (or, if none, the one waiting for you) and changed last.
        var latest = _sessions.Values.Where(s => s.Action is not null && s.State != ActivityState.Idle && now - s.At <= WaitingTimeout)
            .OrderByDescending(s => s.State == ActivityState.Working).ThenByDescending(s => s.At).FirstOrDefault();
        return new AgentStatus(Id, "Claude Code", state, false, detail, source, quota)
        {
            Pending = PendingSessions(now),
            SessionOpen = !_sessions.IsEmpty,
            Activity = latest is null ? null : new AgentActivity(latest.Project, latest.Action!, latest.At)
        };
    }

    /// <summary>
    /// Sessions blocked on a permission or a question. A finished answer ("Stop") or the idle reminder is not a pending
    /// question, and a pending one is dropped as soon as the session moves on or ages out.
    /// </summary>
    private List<PendingItem> PendingSessions(DateTimeOffset now) =>
        _sessions.Where(p => p.Value is { State: ActivityState.Waiting, PendingKind: not null } s && now - s.At <= WaitingTimeout)
            .OrderBy(p => p.Value.At)
            .Select(p => new PendingItem(p.Key + "|" + p.Value.At.ToUnixTimeSeconds(), "Claude Code", p.Value.Project ?? L.T("sessão", "session"), p.Value.PendingKind!, p.Value.At))
            .ToList();

    private (ActivityState, string) Aggregate(DateTimeOffset now)
    {
        foreach (var pair in _sessions)
            if (now - pair.Value.At > SessionForget) _sessions.TryRemove(pair.Key, out _);
        var effective = _sessions.Values.Select(s => s.State switch
        {
            ActivityState.Working when now - s.At > WorkingTimeout =>
                s with { State = ActivityState.Waiting, Detail = L.T("sem eventos há mais de 10 min", "no events for over 10 min") },
            ActivityState.Waiting when now - s.At > WaitingTimeout => s with { State = ActivityState.Idle, Detail = L.T("sessão parada", "session stopped") },
            _ => s
        }).ToList();
        var working = effective.Where(s => s.State == ActivityState.Working).OrderByDescending(s => s.At).FirstOrDefault();
        if (working is not null)
            return (ActivityState.Working, effective.Count(s => s.State == ActivityState.Working) > 1
                ? L.T($"{effective.Count(s => s.State == ActivityState.Working)} sessões ativas", $"{effective.Count(s => s.State == ActivityState.Working)} active sessions") : working.Detail);
        var waiting = effective.Where(s => s.State == ActivityState.Waiting).OrderByDescending(s => s.At).FirstOrDefault();
        if (waiting is not null) return (ActivityState.Waiting, waiting.Detail);
        return (ActivityState.Idle, effective.Count > 0 ? L.T("sessão aberta, sem pedido em andamento", "session open, no request running") : L.T("nenhuma sessão ativa", "no active session"));
    }

    public string DiagnosticsJson(DateTimeOffset now)
    {
        var (state, detail) = Aggregate(now);
        var quota = ClaudeQuota.Read(now).WithRenewals(now);
        return JsonSerializer.Serialize(new
        {
            state = state.ToString(), detail, sessions = _sessions.Count,
            quotaConfiguration = new { directory = AppPaths.DataDirectory,
                userResetFileExists = File.Exists(Path.Combine(AppPaths.DataDirectory, "claude-renewal.json")),
                portableResetFileExists = File.Exists(Path.Combine(AppContext.BaseDirectory, "claude-renewal.json")) },
            providers = ProviderDiagnostics?.Invoke(),
            quota = new { quota.UpdatedAt, quota.Stale, quota.Error, quota.Note,
                windows = quota.Windows.Select(w => new { kind = w.Kind.ToString(), w.UsedPercent, w.ResetsAt, w.ResetEstimated, w.ResetHint }) },
            events = _eventCounts.ToDictionary(x => x.Key, x => new { x.Value.Count, secondsAgo = (int)(now - x.Value.Last).TotalSeconds })
        });
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_connections.Wait(0)) { client.Dispose(); continue; } // flood: drop instead of queueing
                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(client); }
                    finally { try { _connections.Release(); } catch (ObjectDisposedException) { } }
                });
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
            catch (SocketException) { await Task.Delay(200); }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var header = await ReadHeaderAsync(stream, timeout.Token);
                if (header is null) return;
                var lines = header.Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length < 2) { await RespondAsync(stream, 400); return; }
                string? Header(string name) => lines.FirstOrDefault(x => x.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim();
                // Only Claude Code (a local, non-browser client) talks to this port. Browsers always send Origin on
                // cross-site POSTs, and a DNS-rebinding page arrives with a foreign Host: refuse both, plus the
                // "simple request" content types a page could post without a preflight.
                var host = Header("Host");
                if (Header("Origin") is not null || (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}"))
                {
                    await RespondAsync(stream, 404);
                    return;
                }
                var contentType = Header("Content-Type") ?? "";
                if (contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) ||
                    contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                    contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    await RespondAsync(stream, 400);
                    return;
                }
                if (request[0] == "GET" && request[1] == "/status")
                {
                    await RespondAsync(stream, 200, DiagnosticsJson(DateTimeOffset.Now));
                    return;
                }
                if (request[0] != "POST" || !request[1].StartsWith("/claude/", StringComparison.Ordinal)) { await RespondAsync(stream, 404); return; }
                byte[] body;
                if (string.Equals(Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
                    body = await ReadChunkedAsync(stream, timeout.Token);
                else
                {
                    var length = int.TryParse(Header("Content-Length"), out var n) ? n : 0;
                    if (length is < 0 or > MaxBody) { await RespondAsync(stream, 413); return; }
                    body = new byte[length];
                    await stream.ReadExactlyAsync(body, timeout.Token);
                }
                var eventName = request[1]["/claude/".Length..];
                var status = Apply(eventName, body);
                await RespondAsync(stream, status);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException or FormatException)
            {
                // Hooks are observational: malformed payloads cannot affect Claude's work.
            }
        }
    }

    private int Apply(string eventName, byte[] body)
    {
        if (body.Length == 0) return 400;
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var session = Str("session_id");
        if (string.IsNullOrWhiteSpace(session) || session.Length > 100) return 400;
        var tool = SafeName(Str("tool_name"));
        var kind = AgentActions.ForClaudeTool(tool);
        SessionState? next = eventName switch
        {
            "SessionStart" => new(ActivityState.Idle, DateTimeOffset.Now, L.T("sessão iniciada", "session started")),
            "UserPromptSubmit" => new(ActivityState.Working, DateTimeOffset.Now, L.T("processando pedido", "working on your request"), Action: AgentActions.Describe(ActionKind.Thinking, false)),
            "PreToolUse" => new(ActivityState.Working, DateTimeOffset.Now, tool is null ? L.T("usando ferramenta", "using a tool") : L.T($"usando {tool}", $"using {tool}"),
                Action: AgentActions.Describe(kind, false, tool)),
            "PostToolUse" => new(ActivityState.Working, DateTimeOffset.Now, L.T("processando resultado", "processing the result"), Action: AgentActions.Describe(kind, true, tool)),
            "PermissionRequest" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("aguardando sua permissão", "waiting for your permission"), L.T("Permissão", "Permission"), Action: L.T("Precisa da sua permissão", "Needs your permission")),
            "Notification" => Str("notification_type") switch
            {
                "permission_prompt" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("aguardando sua permissão", "waiting for your permission"), L.T("Permissão", "Permission"), Action: L.T("Precisa da sua permissão", "Needs your permission")),
                "elicitation_dialog" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("aguardando sua resposta", "waiting for your answer"), L.T("Pergunta", "Question"), Action: L.T("Tem uma pergunta para você", "Has a question for you")),
                "idle_prompt" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("aguardando sua resposta", "waiting for your answer"), Action: L.T("Esperando sua resposta", "Waiting for your answer")),
                _ => new SessionState(ActivityState.Waiting, DateTimeOffset.Now, L.T("aguardando você", "waiting for you"), Action: L.T("Esperando você", "Waiting for you"))
            },
            "Stop" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("resposta concluída · aguardando você", "answer done · waiting for you"), Action: L.T("Terminou · sua vez", "Done · your turn")),
            "StopFailure" => new(ActivityState.Waiting, DateTimeOffset.Now, L.T("turno interrompido por erro", "turn stopped by an error"), Action: L.T("Parou por um erro", "Stopped by an error")),
            _ => null
        };
        if (eventName is not ("SessionEnd" or "SessionStart" or "UserPromptSubmit" or "PreToolUse" or "PostToolUse" or
            "PermissionRequest" or "Notification" or "Stop" or "StopFailure")) return 404;
        _eventCounts.AddOrUpdate(eventName, _ => (1, DateTimeOffset.Now), (_, v) => (v.Count + 1, DateTimeOffset.Now));
        if (eventName == "SessionEnd") _sessions.TryRemove(session, out _);
        else if (next is not null)
        {
            // Only the folder name of the working directory is kept (to tell sessions apart), never prompts or paths.
            var project = ProjectName(Str("cwd")) ?? (_sessions.TryGetValue(session, out var known) ? known.Project : null);
            next = next with { Project = project };
            // Notification (idle reminder) must not downgrade a session that is actively working again.
            if (!(eventName == "Notification" && _sessions.TryGetValue(session, out var current) &&
                  current.State == ActivityState.Working && DateTimeOffset.Now - current.At < TimeSpan.FromSeconds(5)))
                _sessions[session] = next;
        }
        Changed?.Invoke();
        return 200;
    }

    private static string? ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        var name = Path.GetFileName(cwd.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return name.Length == 0 ? null : name[..Math.Min(32, name.Length)];
    }

    private static string? SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = ToolNameFilter().Replace(value, "");
        if (clean.StartsWith("mcp__", StringComparison.Ordinal)) clean = "MCP";
        return clean.Length == 0 ? null : clean[..Math.Min(24, clean.Length)];
    }

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex ToolNameFilter();

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new List<byte>(1024);
        var one = new byte[1];
        while (header.Count < 16384)
        {
            if (await stream.ReadAsync(one, token) == 0) return null;
            header.Add(one[0]);
            var n = header.Count;
            if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10)
                return Encoding.ASCII.GetString(header.ToArray(), 0, n - 4);
        }
        return null;
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 1024)
        {
            if (await stream.ReadAsync(one, token) == 0) throw new IOException("closed");
            if (one[0] == 10) break;
            if (one[0] != 13) bytes.Add(one[0]);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static async Task<byte[]> ReadChunkedAsync(NetworkStream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        while (true)
        {
            var sizeLine = (await ReadLineAsync(stream, token)).Split(';')[0].Trim();
            if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var size) || size < 0)
                throw new FormatException("invalid chunk size");
            if (size == 0) { await ReadLineAsync(stream, token); break; }
            if (buffer.Length + size > MaxBody) throw new InvalidOperationException("too large");
            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk, token);
            buffer.Write(chunk);
            await ReadLineAsync(stream, token);
        }
        return buffer.ToArray();
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string body = "{}")
    {
        var phrase = status switch { 200 => "OK", 404 => "Not Found", 413 => "Payload Too Large", _ => "Bad Request" };
        var content = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {phrase}\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(content);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _cacheWatcher?.Dispose();
        lock (_watchGate) _desktopWatcher?.Dispose();
        _stop.Dispose();
    }
}
