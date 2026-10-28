using ModelContextProtocol.Protocol;
using System.Globalization;
using System.Text.Json;

namespace AIUsageMonitor;

internal sealed record MagnificCredits(decimal Available, decimal TotalPlan, decimal Spent, string? Plan, bool Unlimited)
{
    public double UsedPercent => TotalPlan <= 0 ? (Available > 0 ? 0 : 100) : (double)Math.Clamp(Spent / TotalPlan * 100m, 0m, 100m);
}

/// <summary>
/// Magnific plan credits read from Magnific's official remote MCP server (read-only account_balance tool).
/// Like OpenRouter, the browser login only opens from the tray or launcher action, never during background refresh.
/// </summary>
internal sealed class MagnificAgent : IAgentSource
{
    private static readonly Uri Endpoint = new("https://mcp.magnific.com");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private const string SourceName = "Magnific MCP · account_balance";
    private readonly OpenRouterTokenCache _tokenCache = new("magnific-mcp-token.dat", "QuotaDesk.Magnific.MCP.v1");
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly System.Threading.Timer _timer;
    private ProviderSnapshot _quota = ProviderSnapshot.Unavailable("Magnific",
        L.T("Conecte sua conta no menu da bandeja para ler os créditos.", "Connect your account from the tray menu to read the credits."), SourceName);
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private readonly CreditUsageTracker _usage = new("magnific");
    private bool _disposed;

    public MagnificAgent()
    {
        _timer = new System.Threading.Timer(_ => _ = RefreshQuotaAsync(false), null, RefreshInterval, RefreshInterval);
    }

    public string Id => "magnific";
    public event Action? Changed;

    public AgentStatus GetStatus(DateTimeOffset now)
    {
        var connected = _quota.UpdatedAt is not null;
        var detail = _quota.Stale ? L.T("créditos Magnific desatualizados", "Magnific credits outdated")
            : connected ? L.T("créditos Magnific", "Magnific credits") : L.T("Magnific desconectado", "Magnific disconnected");
        return new AgentStatus(Id, "Magnific", connected ? ActivityState.Idle : ActivityState.Unknown, connected, detail,
            SourceName + L.T(" · consulta a cada 15 min", " · checked every 15 min"), _quota);
    }

    public Task RefreshQuotaAsync(bool force) => RefreshCoreAsync(allowAuthorization: false, force, CancellationToken.None);

    /// <summary>Starts or resumes the browser OAuth flow only after the user chooses the tray action.</summary>
    public Task ConnectAsync() => RefreshCoreAsync(allowAuthorization: true, force: true, CancellationToken.None);

    /// <summary>Drops the saved login and signs in again, so the credits can come from another Magnific account.</summary>
    public async Task SwitchAccountAsync()
    {
        // Wait for a background refresh in flight: it could otherwise store the old account's refreshed tokens
        // right after the clear, and the new login would silently reuse them.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try { await _tokenCache.ClearAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _refreshGate.Release(); }
        _quota = ProviderSnapshot.Unavailable("Magnific", L.T("Trocando de conta: conclua o login no navegador.", "Switching account: finish signing in in the browser."), SourceName);
        _lastAttempt = DateTimeOffset.MinValue;
        Changed?.Invoke();
        await ConnectAsync().ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(bool allowAuthorization, bool force, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        if (allowAuthorization)
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        else if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;
        try
        {
            // Manual refreshes may run sooner, but never more than once a minute.
            var floor = force ? TimeSpan.FromMinutes(1) : RefreshInterval;
            if (!allowAuthorization && DateTimeOffset.UtcNow - _lastAttempt < floor)
                return;

            if (!allowAuthorization && await _tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                SetUnavailable(L.T("Conecte ou reautorize Magnific no menu da bandeja.", "Connect or reauthorize Magnific from the tray menu."));
                return;
            }

            _lastAttempt = DateTimeOffset.UtcNow;
            var result = await RemoteMcpTool.CallAsync(Endpoint, "Magnific", "account_balance", _tokenCache, allowAuthorization, cancellationToken)
                .ConfigureAwait(false);
            var credits = ParseResult(result);
            var hint = L.T($"{Credits(credits.Available)} créditos disponíveis de {Credits(credits.TotalPlan)} ({Credits(credits.Spent)} gastos)",
                           $"{Credits(credits.Available)} credits available of {Credits(credits.TotalPlan)} ({Credits(credits.Spent)} spent)") +
                       (credits.Plan is { } plan ? L.T($" · plano {plan}", $" · plan {plan}") : "");
            var window = new LimitWindow(L.T("Créditos", "Credits"), credits.UsedPercent, null, WindowKind.Other, ResetHint: hint)
            {
                Amount = credits.Unlimited ? L.T("ilimitado", "unlimited")
                    : L.T($"{Credits(credits.Available)} de {Credits(credits.TotalPlan)} cr", $"{Credits(credits.Available)} of {Credits(credits.TotalPlan)} cr"),
                Credits = credits.Unlimited ? null : _usage.Record(credits.Available, credits.TotalPlan, credits.Spent, "cr", DateTimeOffset.Now)
            };
            _quota = new ProviderSnapshot("Magnific", [window], DateTimeOffset.Now, null, SourceName);
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OpenRouterAuthorizationRequiredException)
        {
            SetUnavailableOrStale(L.T("Autorização expirada ou revogada. Escolha Conectar Magnific no menu da bandeja.",
                "Authorization expired or revoked. Choose Connect Magnific from the tray menu."));
        }
        catch (Exception)
        {
            SetUnavailableOrStale(L.T("Não foi possível consultar os créditos do Magnific; nova tentativa em 15 minutos.",
                "Could not read Magnific credits; retrying in 15 minutes."));
        }
        finally { _refreshGate.Release(); }
    }

    private static string Credits(decimal value) => value.ToString("N0", L.Culture);

    private static MagnificCredits ParseResult(CallToolResult result)
    {
        if (result.StructuredContent is { } structured && structured.ValueKind == JsonValueKind.Object &&
            TryParseBalance(structured, out var credits))
            return credits;
        var text = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(block => block.Text)).Trim();
        try
        {
            using var document = JsonDocument.Parse(text);
            if (TryParseBalance(document.RootElement, out credits)) return credits;
        }
        catch (JsonException) { }
        throw new InvalidDataException("Magnific returned an unexpected account_balance response shape.");
    }

    /// <summary>Reads {"plan":{productName,isUnlimitedMode},"credits":{available,totalPlan,spent}}; missing numbers never become zero.</summary>
    internal static bool TryParseBalance(JsonElement root, out MagnificCredits credits)
    {
        credits = null!;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("credits", out var c) || c.ValueKind != JsonValueKind.Object ||
            Number(c, "available") is not { } available || Number(c, "totalPlan") is not { } total || available < 0 || total < 0)
            return false;
        var spent = Number(c, "spent") ?? Math.Max(0, total - available);
        string? plan = null;
        var unlimited = false;
        if (root.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("productName", out var name) && name.ValueKind == JsonValueKind.String) plan = name.GetString();
            unlimited = p.TryGetProperty("isUnlimitedMode", out var u) && u.ValueKind == JsonValueKind.True;
        }
        credits = new MagnificCredits(available, total, spent, plan, unlimited);
        return true;
    }

    private static decimal? Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;

    private void SetUnavailable(string message)
    {
        if (_quota.Error == message) return;
        _quota = ProviderSnapshot.Unavailable("Magnific", message, SourceName);
        Changed?.Invoke();
    }

    private void SetUnavailableOrStale(string message)
    {
        _quota = _quota.Windows.Count > 0
            ? _quota with { Stale = true, Error = message, Note = message }
            : ProviderSnapshot.Unavailable("Magnific", message, SourceName);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
