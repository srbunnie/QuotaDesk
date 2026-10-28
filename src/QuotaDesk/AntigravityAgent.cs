using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageMonitor;

/// <summary>Read-only adapter for the installed Antigravity loopback language server.
/// No OAuth files, prompts, browser scraping or account credentials are used.</summary>
internal sealed class AntigravityAgent : IAgentSource
{
    public string Id => "antigravity";
    public event Action? Changed;
    // Loopback replies are small; cap them so whatever holds the port cannot make us buffer gigabytes.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private ProviderSnapshot _quota = ProviderSnapshot.Unavailable("Antigravity", "Abra o Antigravity para consultar a cota.");
    private ActivityState _state = ActivityState.Unknown;
    private string _detail = "Antigravity fechado ou sem sinal local.";
    private DateTimeOffset _lastQuota;
    private int _port, _pid;
    private string? _csrf; // Ephemeral loopback session token, never persisted or logged.
    // Candidate PIDs of the last discovery that found nothing, and when: avoids re-running PowerShell every few seconds.
    private string _failedCandidates = "";
    private DateTimeOffset _failedAt;
    private bool _foreground;
    private static string CachePath => Path.Combine(AppPaths.DataDirectory, "antigravity-limits.json");

    public AntigravityAgent()
    {
        try
        {
            if (File.Exists(CachePath) && JsonSerializer.Deserialize<ProviderSnapshot>(File.ReadAllText(CachePath)) is { Name: "Antigravity", UpdatedAt: not null, Windows: { Count: > 0 } } cached &&
                cached.Windows.All(w => !string.IsNullOrWhiteSpace(w.Label) && double.IsFinite(w.UsedPercent) && w.UsedPercent is >= 0 and <= 100))
                _quota = cached with { Stale = true, Note = "Última leitura salva; aguardando o Antigravity atualizar." };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        _ = PollAsync();
    }
    public void SetForeground(bool visible) => _foreground = visible;
    public AgentStatus GetStatus(DateTimeOffset now)
    {
        // The card, dock and tray see only the chosen group, so its 5-hour and weekly windows behave like Claude's.
        lock (_sync) return new(Id, "Antigravity", _state, false, _detail,
            "Servidor local do Antigravity", _quota with { Windows = ModelChoice.Narrow(_quota.Windows) });
    }

    private async Task PollAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await RefreshQuotaAsync(false);
                // Slower while Antigravity is closed: there is nothing to read, only its start to notice.
                await Task.Delay(_port == 0 ? (_foreground ? 8000 : 30000) : (_foreground ? 3000 : 10000), _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshQuotaAsync(bool force)
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            if (_port == 0 || !Alive()) await DiscoverAsync();
            if (_port == 0) throw new IOException("Antigravity fechado. Abra o aplicativo para atualizar a cota.");
            if (force || DateTimeOffset.UtcNow - _lastQuota >= TimeSpan.FromSeconds(30))
            {
                // Groups with separate 5-hour and weekly windows (the Settings screen). Older versions only report
                // one fraction per model, so that stays as the fallback.
                var windows = new List<LimitWindow>();
                try { using var summary = await ReadAsync("RetrieveUserQuotaSummary"); windows = ParseQuotaSummary(summary.RootElement); }
                catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
                var grouped = windows.Count > 0;
                if (!grouped) { using var data = await ReadAsync("GetUserStatus"); windows = ParseQuota(data.RootElement); }
                if (windows.Count == 0) throw new IOException("O servidor local ainda não informou cotas.");
                lock (_sync) _quota = new("Antigravity", windows, DateTimeOffset.Now,
                    Source: "Antigravity · servidor local", Note: grouped
                        ? "Cota por grupo de modelos, com limite de 5 horas e semanal; não representa o chat Gemini."
                        : "Cota por modelo (rodinha do mouse para trocar); não representa o chat Gemini.");
                _lastQuota = DateTimeOffset.UtcNow;
                SaveCache();
            }
            // Activity is independent of quota changes. Only explicit server states activate the character.
            try
            {
                using var data = await ReadHubActivityAsync();
                var state = ParseActivity(data.RootElement);
                lock (_sync) { _state = state; _detail = state == ActivityState.Working ? "Execução ativa informada pelo Antigravity." : state == ActivityState.Idle ? "Nenhuma execução ativa informada pelo Antigravity." : "Atividade não informada por esta versão do Antigravity."; }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
            { lock (_sync) { _state = ActivityState.Unknown; _detail = "Cota disponível; atividade sem sinal local."; } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _port = 0; _csrf = null;
            lock (_sync)
            {
                _state = ActivityState.Unknown; _detail = "Antigravity fechado ou sem sinal local.";
                _quota = _quota with { Stale = _quota.Windows.Count > 0, Error = ex is IOException ? ex.Message : "Não foi possível consultar o servidor local do Antigravity." };
            }
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private bool Alive()
    {
        // A reused PID must still be a language server, not whatever process got the number afterwards.
        try { using var process = Process.GetProcessById(_pid); return !process.HasExited && process.ProcessName.StartsWith("language_server", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            ProviderSnapshot quota;
            lock (_sync) quota = _quota;
            // Only normalized percentages, labels and renewal times; no raw account response or session token.
            File.WriteAllText(CachePath + ".tmp", JsonSerializer.Serialize(quota));
            File.Move(CachePath + ".tmp", CachePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task DiscoverAsync()
    {
        _port = 0; _csrf = null;
        // Antigravity closed is the common case: a cheap process-list check avoids starting PowerShell every few seconds.
        // The binary is language_server.exe in older builds and language_server_windows_x64.exe in "Antigravity IDE".
        // Windsurf/Codeium ship a language_server too: skip processes whose path is clearly not Antigravity's.
        var running = Process.GetProcesses().Where(p => p.ProcessName.StartsWith("language_server", StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = new List<int>();
        foreach (var candidate in running)
        {
            using (candidate)
            {
                string? path = null;
                try { path = candidate.MainModule?.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                if (path is null || path.Contains("antigravity", StringComparison.OrdinalIgnoreCase)) candidates.Add(candidate.Id);
            }
        }
        if (candidates.Count == 0) return;
        // The same processes that yielded nothing a moment ago will not answer differently: retry every 2 minutes.
        var key = string.Join(",", candidates.Order());
        if (key == _failedCandidates && DateTimeOffset.UtcNow - _failedAt < TimeSpan.FromMinutes(2)) return;
        _failedCandidates = key;
        _failedAt = DateTimeOffset.UtcNow;
        // WMI supplies the existing process's transient local session token. It is not a Google credential.
        // The listening ports come from the OS by process id: current builds no longer log a plain-HTTP port.
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Get-CimInstance Win32_Process -Filter \"name like 'language_server%'\" | ForEach-Object { [pscustomobject]@{ ProcessId = $_.ProcessId; ExecutablePath = $_.ExecutablePath; CommandLine = $_.CommandLine; Ports = @(Get-NetTCPConnection -State Listen -OwningProcess $_.ProcessId -ErrorAction SilentlyContinue | ForEach-Object { $_.LocalPort }) } } | ConvertTo-Json -Compress");
        using var process = Process.Start(start) ?? throw new IOException("Servidor local indisponível.");
        var outputTask = process.StandardOutput.ReadToEndAsync(_stop.Token);
        var errorTask = process.StandardError.ReadToEndAsync(_stop.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(); } catch (InvalidOperationException) { } throw; }
        var output = await outputTask; await errorTask;
        if (string.IsNullOrWhiteSpace(output)) return;
        using var json = JsonDocument.Parse(output);
        var entries = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToArray() : [json.RootElement];
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") + Path.DirectorySeparatorChar;
        foreach (var entry in entries)
        {
            // Only Antigravity's own language server, installed per user.
            if (!entry.TryGetProperty("ExecutablePath", out var exe) || exe.ValueKind != JsonValueKind.String ||
                exe.GetString() is not { } path || !path.StartsWith(programs, StringComparison.OrdinalIgnoreCase) ||
                !path.Contains("antigravity", StringComparison.OrdinalIgnoreCase)) continue;
            var command = entry.TryGetProperty("CommandLine", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() ?? "" : "";
            var token = Regex.Match(command, "--csrf_token(?:=|\\s+)\"?([^\\s\"]+)");
            if (!token.Success || !entry.TryGetProperty("Ports", out var ports)) continue;
            var list = ports.ValueKind == JsonValueKind.Array
                ? ports.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out _)).Select(p => p.GetInt32()).ToArray()
                : ports.ValueKind == JsonValueKind.Number ? [ports.GetInt32()] : [];
            foreach (var port in list)
            {
                if (port is < 1 or > 65535 || !await ProbeAsync(port, token.Groups[1].Value)) continue;
                _pid = entry.GetProperty("ProcessId").GetInt32(); _port = port; _csrf = token.Groups[1].Value;
                _failedCandidates = "";
                return;
            }
        }
    }

    /// <summary>The server listens on several ports (HTTPS gRPC, extension server, plain HTTP): keep the one that answers.</summary>
    private async Task<bool> ProbeAsync(int port, string csrf)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary");
            request.Headers.Add("X-Codeium-Csrf-Token", csrf);
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, timeout.Token);
            // Older versions lack this method (404) but still answer GetUserStatus on the same port.
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return false; }
    }

    private async Task<JsonDocument> ReadAsync(string method)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/exa.language_server_pb.LanguageServerService/{method}");
        request.Headers.Add("X-Codeium-Csrf-Token", _csrf);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, _stop.Token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
    }

    private async Task<JsonDocument> ReadHubActivityAsync()
    {
        // The 2.18 Hub uses Connect streaming; its initial frame is the current summary snapshot.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/exa.language_server_pb.LanguageServerService/JetboxSubscribeToSummaries");
        request.Headers.Add("X-Codeium-Csrf-Token", _csrf);
        request.Headers.Add("Connect-Protocol-Version", "1");
        request.Content = new ByteArrayContent([0, 0, 0, 0, 2, 123, 125]);
        request.Content.Headers.ContentType = new("application/connect+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return await ReadAsync("GetAllCascadeTrajectories");
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var header = new byte[5];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        if (header[0] != 0 || size > 1024 * 1024) throw new HttpRequestException("Unsupported local state frame.");
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, timeout.Token);
        return JsonDocument.Parse(body);
    }

    internal static List<LimitWindow> ParseQuota(JsonElement root)
    {
        var windows = new List<LimitWindow>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("userStatus", out var status) || status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("cascadeModelConfigData", out var config) || config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("clientModelConfigs", out var models) || models.ValueKind != JsonValueKind.Array) return windows;
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String ||
                !model.TryGetProperty("quotaInfo", out var quota) || quota.ValueKind != JsonValueKind.Object || !quota.TryGetProperty("remainingFraction", out var fraction) ||
                fraction.ValueKind != JsonValueKind.Number || !fraction.TryGetDouble(out var remaining) || !double.IsFinite(remaining) || remaining is < 0 or > 1) continue;
            DateTimeOffset? reset = quota.TryGetProperty("resetTime", out var time) ? LimitParsing.Epoch(time) : null;
            windows.Add(new(label.GetString()!, (1 - remaining) * 100, reset));
        }
        return windows.OrderByDescending(w => w.Label.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)).ThenBy(w => w.Label, StringComparer.Ordinal).ToList();
    }

    internal static List<LimitWindow> ParseQuotaSummary(JsonElement root)
    {
        var windows = new List<LimitWindow>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return windows;
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("displayName", out var name) || name.ValueKind != JsonValueKind.String ||
                !group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
            var groupName = GroupName(name.GetString()!);
            foreach (var bucket in buckets.EnumerateArray())
            {
                // Buckets measured as an amount instead of a fraction are skipped rather than guessed.
                if (bucket.ValueKind != JsonValueKind.Object || !bucket.TryGetProperty("remainingFraction", out var fraction) ||
                    fraction.ValueKind != JsonValueKind.Number || !fraction.TryGetDouble(out var remaining) || !double.IsFinite(remaining) || remaining is < 0 or > 1) continue;
                var window = bucket.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                var kind = window switch { "5h" => WindowKind.FiveHour, "weekly" => WindowKind.Weekly, _ => WindowKind.Other };
                var label = kind switch
                {
                    WindowKind.FiveHour => "5 horas", WindowKind.Weekly => "Semanal",
                    _ => bucket.TryGetProperty("displayName", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : window ?? "Limite"
                };
                DateTimeOffset? reset = bucket.TryGetProperty("resetTime", out var time) ? LimitParsing.Epoch(time) : null;
                windows.Add(new LimitWindow(label, (1 - remaining) * 100, reset, kind) { Group = groupName });
            }
        }
        return windows;
    }

    /// <summary>"Gemini Models" → "Gemini", "Claude and GPT models" → "Claude e GPT"; unknown names pass through.</summary>
    private static string GroupName(string name) =>
        Regex.Replace(Regex.Replace(name.Trim(), @"\s+models$", "", RegexOptions.IgnoreCase), @"\band\b", "e");

    internal static ActivityState ParseActivity(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return ActivityState.Unknown;
        if (!root.TryGetProperty("updates", out var summaries) && !root.TryGetProperty("trajectorySummaries", out summaries) && !root.TryGetProperty("summaries", out summaries)) return root.EnumerateObject().Any() ? ActivityState.Unknown : ActivityState.Idle;
        if (summaries.ValueKind != JsonValueKind.Object) return ActivityState.Unknown;
        var unknown = false;
        var waiting = false;
        foreach (var item in summaries.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object) { unknown = true; continue; }
            if (item.Value.TryGetProperty("waitingSteps", out var steps) && steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() > 0)
            { waiting = true; continue; }
            if (!item.Value.TryGetProperty("status", out var status))
            {
                if (item.Value.TryGetProperty("notFullyIdle", out var active) && active.ValueKind == JsonValueKind.False) continue;
                unknown = true; continue;
            }
            var value = status.ValueKind == JsonValueKind.String ? status.GetString() : status.ToString();
            if (value is "RUNNING" or "BUSY" or "CANCELING" or "2" or "3" or "4" ||
                value is "CASCADE_RUN_STATUS_RUNNING" or "CASCADE_RUN_STATUS_BUSY" or "CASCADE_RUN_STATUS_CANCELING" or "CASCADE_STATUS_RUNNING") return ActivityState.Working;
            if (value is not ("IDLE" or "1" or "CASCADE_RUN_STATUS_IDLE" or "CASCADE_STATUS_IDLE" or "CASCADE_STATUS_DONE" or "CASCADE_STATUS_CANCELED")) unknown = true;
        }
        return waiting ? ActivityState.Waiting : unknown ? ActivityState.Unknown : ActivityState.Idle;
    }

    public void Dispose() { _stop.Cancel(); }
}
