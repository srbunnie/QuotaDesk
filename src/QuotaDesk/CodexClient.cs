using System.Diagnostics;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>Reads Codex rate limits through the local `codex app-server` (account/rateLimits/read). No prompts are sent.</summary>
internal static class CodexClient
{
    public static string? ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("AI_MONITOR_CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        // The Codex desktop app keeps its CLI in a versioned folder that changes on every update.
        var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try
        {
            return Directory.Exists(bin)
                ? Directory.EnumerateFiles(bin, "codex.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static async Task<ProviderSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        const string source = "codex app-server · account/rateLimits/read";
        var exe = ResolveExecutable();
        if (exe is null) return ProviderSnapshot.Unavailable("Codex", L.T("Codex CLI não encontrado.", "Codex CLI not found."), source);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "app-server --stdio",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardInputEncoding = new System.Text.UTF8Encoding(false)
                }
            };
            process.Start();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
            process.ErrorDataReceived += (_, _) => { }; // Drain stderr; never persist possible secrets.
            process.BeginErrorReadLine();
            try
            {
                await SendAsync(process, new
                {
                    method = "initialize", id = 1,
                    @params = new { clientInfo = new { name = "ai_usage_monitor", title = "QuotaDesk", version = "0.2.0" } }
                }, timeout.Token);
                (await ReadResultAsync(process, 1, timeout.Token)).Dispose();
                await SendAsync(process, new { method = "initialized" }, timeout.Token);
                await SendAsync(process, new { method = "account/rateLimits/read", id = 2 }, timeout.Token);
                using var response = await ReadResultAsync(process, 2, timeout.Token);
                return Parse(response.RootElement, source);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or
            JsonException or InvalidOperationException or OperationCanceledException)
        {
            return ProviderSnapshot.Unavailable("Codex", ex switch
            {
                System.ComponentModel.Win32Exception => L.T("Não foi possível iniciar o Codex CLI.", "Could not start the Codex CLI."),
                OperationCanceledException => L.T("Tempo de consulta esgotado.", "The check timed out."),
                _ => L.T("Não foi possível ler os limites do Codex.", "Could not read the Codex limits.")
            }, source);
        }
    }

    private static async Task SendAsync(Process process, object request, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private static async Task<JsonDocument> ReadResultAsync(Process process, int id, CancellationToken token)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(token) ?? throw new IOException("App server closed.");
            if (line.Length == 0 || line[0] != '{') continue;
            var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number ||
                !responseId.TryGetInt32(out var value) || value != id) { message.Dispose(); continue; }
            if (root.TryGetProperty("error", out _)) { message.Dispose(); throw new InvalidOperationException("App server returned an error."); }
            return message;
        }
    }

    internal static ProviderSnapshot Parse(JsonElement response, string source)
    {
        if (!response.TryGetProperty("result", out var result))
            return ProviderSnapshot.Unavailable("Codex", L.T("Resposta sem limites.", "Response without limits."), source);
        var windows = new List<LimitWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            // The general "codex" bucket first; model-specific buckets follow.
            foreach (var bucket in buckets.EnumerateObject().OrderBy(b => b.Name == "codex" ? 0 : 1))
                AddBucket(bucket.Value, bucket.Name, windows);
        }
        else if (result.TryGetProperty("rateLimits", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            AddBucket(single, "codex", windows);
        }
        return windows.Count == 0
            ? ProviderSnapshot.Unavailable("Codex", L.T("Nenhuma janela de cota disponível.", "No limit window available."), source)
            : new ProviderSnapshot("Codex", windows, DateTimeOffset.Now, null, source,
                Note: windows.Any(w => w.Kind == WindowKind.FiveHour) ? null : L.T("O Codex não informa janela de 5 horas para esta conta.", "Codex reports no 5-hour window for this account."))
            { Resets = ParseResets(result) };
    }

    /// <summary>
    /// "rateLimitResetCredits": how many limit resets the account holds and, when listed, when each one expires.
    /// The expiry field name is not documented, so any date-like property whose name mentions expiry/validity is used.
    /// </summary>
    internal static ResetCredits? ParseResets(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimitResetCredits", out var resets) || resets.ValueKind != JsonValueKind.Object) return null;
        var expirations = new List<DateTimeOffset>();
        var listed = 0;
        if (resets.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Array)
            foreach (var credit in credits.EnumerateArray())
            {
                listed++;
                if (credit.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in credit.EnumerateObject())
                {
                    var name = property.Name.ToLowerInvariant();
                    if (!name.Contains("expir") && !name.Contains("valid")) continue;
                    if (LimitParsing.Epoch(property.Value) is { } when && when.Year > 2020) { expirations.Add(when); break; }
                }
            }
        var available = resets.TryGetProperty("availableCount", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var n) ? n : listed;
        return new ResetCredits(Math.Max(0, available), expirations.OrderBy(x => x).ToList());
    }

    private static void AddBucket(JsonElement bucket, string id, List<LimitWindow> windows)
    {
        var general = id == "codex";
        var display = general ? "" : id;
        if (bucket.TryGetProperty("limitName", out var name) && name.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(name.GetString())) display = name.GetString()!;
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object) continue;
            var minutes = value.TryGetProperty("windowDurationMins", out var duration) && duration.TryGetInt32(out var n) ? n : 0;
            var kind = general ? LimitParsing.KindForMinutes(minutes) : WindowKind.Other;
            var label = LimitParsing.LabelForMinutes(minutes);
            if (display.Length > 0) label = $"{display} · {label}";
            var window = LimitParsing.ParseWindow(value, label, "usedPercent", kind);
            if (window is not null && (window.ResetsAt is null || window.ResetsAt > DateTimeOffset.Now)) windows.Add(window);
        }
    }
}
