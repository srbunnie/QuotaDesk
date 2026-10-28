using System.Globalization;

namespace AIUsageMonitor;

/// <summary>Hourly OpenRouter account-credit source. It never launches OAuth during background refresh.</summary>
internal sealed class OpenRouterAgent : IAgentSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private const string SourceName = "OpenRouter MCP · get-credits";
    private readonly OpenRouterTokenCache _tokenCache = new();
    private readonly CreditUsageTracker _usage = new("openrouter");
    private readonly OpenRouterMcpClient _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly System.Threading.Timer _timer;
    private ProviderSnapshot _quota = ProviderSnapshot.Unavailable("OpenRouter",
        "Conecte sua conta no menu da bandeja para ler o saldo.", SourceName);
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private bool _disposed;

    public OpenRouterAgent()
    {
        _client = new OpenRouterMcpClient(_tokenCache);
        _timer = new System.Threading.Timer(_ => _ = RefreshQuotaAsync(false), null, RefreshInterval, RefreshInterval);
    }

    public string Id => "openrouter";
    public event Action? Changed;

    public AgentStatus GetStatus(DateTimeOffset now)
    {
        var connected = _quota.UpdatedAt is not null;
        var detail = _quota.Stale ? "saldo OpenRouter desatualizado" : connected ? "saldo OpenRouter" : "OpenRouter desconectado";
        return new AgentStatus(Id, "OpenRouter", connected ? ActivityState.Idle : ActivityState.Unknown, connected, detail,
            SourceName + " · consulta horária", _quota);
    }

    // The remote account-credit endpoint has a deliberate one-hour floor, including explicit refresh requests.
    public Task RefreshQuotaAsync(bool _) => RefreshCoreAsync(allowAuthorization: false, CancellationToken.None);

    /// <summary>Starts or resumes the browser OAuth flow only after the user chooses the tray action.</summary>
    public Task ConnectAsync() => RefreshCoreAsync(allowAuthorization: true, CancellationToken.None);

    /// <summary>Drops the saved login and signs in again, so the balance can come from another OpenRouter account.</summary>
    public async Task SwitchAccountAsync()
    {
        // Wait for a background refresh in flight: it could otherwise store the old account's refreshed tokens
        // right after the clear, and the new login would silently reuse them.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try { await _tokenCache.ClearAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _refreshGate.Release(); }
        _quota = ProviderSnapshot.Unavailable("OpenRouter", "Trocando de conta: conclua o login no navegador.", SourceName);
        _lastAttempt = DateTimeOffset.MinValue;
        Changed?.Invoke();
        await ConnectAsync().ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(bool allowAuthorization, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        if (allowAuthorization)
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        else if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;
        try
        {
            if (!allowAuthorization && _lastAttempt != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - _lastAttempt < RefreshInterval)
                return;

            if (!allowAuthorization && await _tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                SetUnavailable("Conecte ou reautorize OpenRouter no menu da bandeja.");
                return;
            }

            _lastAttempt = DateTimeOffset.UtcNow;
            var credits = await _client.GetCreditsAsync(allowAuthorization, cancellationToken).ConfigureAwait(false);
            var hint = $"US$ {Usd(credits.Remaining)} disponíveis de US$ {Usd(credits.TotalCredits)} (US$ {Usd(credits.TotalUsage)} gastos)";
            var window = new LimitWindow("Gasto", credits.UsedPercent, null, WindowKind.Other, ResetHint: hint)
            {
                Amount = $"US$ {Usd(credits.Remaining)} de {Usd(credits.TotalCredits)}",
                Credits = _usage.Record(credits.Remaining, credits.TotalCredits, credits.TotalUsage, "US$", DateTimeOffset.Now)
            };
            _quota = new ProviderSnapshot("OpenRouter", [window], DateTimeOffset.Now, null, SourceName);
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OpenRouterAuthorizationRequiredException)
        {
            SetUnavailableOrStale("Autorização expirada ou revogada. Escolha Conectar OpenRouter no menu da bandeja.");
        }
        catch (Exception)
        {
            SetUnavailableOrStale("Não foi possível consultar o saldo do OpenRouter; uma nova tentativa ocorrerá após 1 hora.");
        }
        finally { _refreshGate.Release(); }
    }

    private static string Usd(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));

    private void SetUnavailable(string message)
    {
        if (_quota.Error == message) return;
        _quota = ProviderSnapshot.Unavailable("OpenRouter", message, SourceName);
        Changed?.Invoke();
    }

    private void SetUnavailableOrStale(string message)
    {
        _quota = _quota.Windows.Count > 0
            ? _quota with { Stale = true, Error = message, Note = message }
            : ProviderSnapshot.Unavailable("OpenRouter", message, SourceName);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
