using System.Net.Http;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>
/// Reads the public Figma Agent component from Figma's Statuspage API. It reports service-wide availability,
/// not whether this user's in-product agent is currently executing a prompt.
/// </summary>
internal sealed class FigmaAgent : IAgentSource
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Uri SummaryUri = new("https://status.figma.com/api/v2/summary.json");
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private static string CreditLimitMessage => L.T(
        "Saldo individual não está disponível por API pública. Consulte AI balance no Figma; a API de consumo exige Enterprise e acesso de administrador.",
        "Individual balance is not available through a public API. Check AI balance in Figma; the usage API needs Enterprise and admin access.");

    private readonly object _sync = new();
    private AgentStatus _status = CreateUnavailable(DateTimeOffset.UtcNow);
    private DateTimeOffset? _lastAttempt;
    private bool _lastAttemptFailed;
    private Task? _refreshTask;
    private bool _disposed;
    private readonly System.Threading.Timer _timer;

    public FigmaAgent()
    {
        _timer = new System.Threading.Timer(_ => _ = RefreshQuotaAsync(false), null, RetryInterval, RetryInterval);
    }

    public string Id => "figma";
    public event Action? Changed;

    public AgentStatus GetStatus(DateTimeOffset now)
    {
        lock (_sync) return _status;
    }

    public Task RefreshQuotaAsync(bool force)
    {
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            if (_refreshTask is { IsCompleted: false }) return _refreshTask;
            var now = DateTimeOffset.UtcNow;
            // The hourly ceiling also applies to manual refreshes so repeated clicks cannot poll the public endpoint rapidly.
            var interval = _lastAttemptFailed ? RetryInterval : PollInterval;
            if (_lastAttempt is { } previous && now - previous < interval) return Task.CompletedTask;

            _lastAttempt = now;
            _refreshTask = RefreshCoreAsync();
            return _refreshTask;
        }
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            using var response = await Client.GetAsync(SummaryUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var health = ParseHealth(document.RootElement, DateTimeOffset.UtcNow);
            var snapshot = new AgentStatus(Id, "Figma Agent",
                ActivityState.Unknown,
                false, health.Detail, L.T("Figma Status API (estado global)", "Figma Status API (global status)"), CreateQuotaSnapshot())
            {
                ServiceHealth = health
            };
            lock (_sync)
            {
                _status = snapshot;
                _lastAttemptFailed = health.State == ServiceAvailability.Unknown;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or IOException)
        {
            lock (_sync)
            {
                var checkedAt = DateTimeOffset.UtcNow;
                var previousHealth = _status.ServiceHealth;
                var health = previousHealth is null
                    ? new ServiceHealthSnapshot(ServiceAvailability.Unknown, L.T("Status indisponível", "Status unavailable"),
                        L.T("Não foi possível consultar a página de status do Figma.", "Could not reach the Figma status page."), checkedAt)
                    : previousHealth with { Stale = true, Detail = L.T("Não foi possível atualizar o status global do Figma Agent; exibindo a última leitura.",
                        "Could not update the Figma Agent global status; showing the last reading.") };
                _status = new AgentStatus(Id, "Figma Agent",
                    ActivityState.Unknown,
                    false, health.Detail, L.T("Figma Status API (estado global)", "Figma Status API (global status)"), CreateQuotaSnapshot())
                {
                    ServiceHealth = health
                };
                _lastAttemptFailed = true;
            }
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    internal static ServiceHealthSnapshot ParseHealth(JsonElement root, DateTimeOffset checkedAt)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array)
            return new(ServiceAvailability.Unknown, L.T("Status indisponível", "Status unavailable"),
                L.T("A resposta da página de status do Figma não contém os componentes esperados.", "The Figma status page response lacks the expected components."), checkedAt);

        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object ||
                !component.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), "Figma agent", StringComparison.OrdinalIgnoreCase)) continue;

            var raw = component.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
                ? status.GetString()
                : null;
            var (state, label) = raw?.ToLowerInvariant() switch
            {
                "operational" => (ServiceAvailability.Operational, L.T("Serviço OK", "Service OK")),
                "degraded_performance" => (ServiceAvailability.Degraded, L.T("Degradado", "Degraded")),
                "partial_outage" => (ServiceAvailability.PartialOutage, L.T("Falha parcial", "Partial outage")),
                "major_outage" => (ServiceAvailability.MajorOutage, L.T("Fora do ar", "Down")),
                "under_maintenance" => (ServiceAvailability.Maintenance, L.T("Em manutenção", "Maintenance")),
                _ => (ServiceAvailability.Unknown, L.T("Sem sinal", "No signal"))
            };
            return new(state, label,
                L.T($"Status global do Figma Agent: {label.ToLowerInvariant()}. Isso não verifica créditos ou tarefas da sua conta.",
                    $"Figma Agent global status: {label.ToLowerInvariant()}. This does not check your account's credits or tasks."), checkedAt);
        }

        return new(ServiceAvailability.Unknown, L.T("Sem sinal", "No signal"),
            L.T("O componente Figma Agent não foi encontrado na página de status oficial.", "The Figma Agent component was not found on the official status page."), checkedAt);
    }

    private static ProviderSnapshot CreateQuotaSnapshot() =>
        new(L.T("Créditos de IA do Figma", "Figma AI credits"), [], null, CreditLimitMessage, L.T("Menu AI balance do Figma", "Figma AI balance menu"))
        {
            Note = L.T("A API de uso do Figma é exclusiva do plano Enterprise e retorna consumo diário atrasado; não informa o saldo atual desta conta.",
                "Figma's usage API is Enterprise-only and returns delayed daily usage; it does not report this account's current balance.")
        };

    private static AgentStatus CreateUnavailable(DateTimeOffset now)
    {
        var health = new ServiceHealthSnapshot(ServiceAvailability.Unknown, L.T("Consultando status", "Checking status"),
            L.T("Aguardando a primeira consulta à página de status do Figma.", "Waiting for the first Figma status page check."), now);
        return new AgentStatus("figma", "Figma Agent", ActivityState.Unknown, false, health.Detail,
            L.T("Figma Status API (estado global)", "Figma Status API (global status)"), CreateQuotaSnapshot())
        {
            ServiceHealth = health
        };
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
        _timer.Dispose();
    }
}
