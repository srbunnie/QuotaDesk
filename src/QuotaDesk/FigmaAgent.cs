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
    private static readonly string CreditLimitMessage =
        "Saldo individual não está disponível por API pública. Consulte AI balance no Figma; a API de consumo exige Enterprise e acesso de administrador.";

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
                false, health.Detail, "Figma Status API (estado global)", CreateQuotaSnapshot())
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
                    ? new ServiceHealthSnapshot(ServiceAvailability.Unknown, "Status indisponível",
                        "Não foi possível consultar a página de status do Figma.", checkedAt)
                    : previousHealth with { Stale = true, Detail = "Não foi possível atualizar o status global do Figma Agent; exibindo a última leitura." };
                _status = new AgentStatus(Id, "Figma Agent",
                    ActivityState.Unknown,
                    false, health.Detail, "Figma Status API (estado global)", CreateQuotaSnapshot())
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
            return new(ServiceAvailability.Unknown, "Status indisponível",
                "A resposta da página de status do Figma não contém os componentes esperados.", checkedAt);

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
                "operational" => (ServiceAvailability.Operational, "Serviço OK"),
                "degraded_performance" => (ServiceAvailability.Degraded, "Degradado"),
                "partial_outage" => (ServiceAvailability.PartialOutage, "Falha parcial"),
                "major_outage" => (ServiceAvailability.MajorOutage, "Fora do ar"),
                "under_maintenance" => (ServiceAvailability.Maintenance, "Em manutenção"),
                _ => (ServiceAvailability.Unknown, "Sem sinal")
            };
            return new(state, label,
                $"Status global do Figma Agent: {label.ToLowerInvariant()}. Isso não verifica créditos ou tarefas da sua conta.", checkedAt);
        }

        return new(ServiceAvailability.Unknown, "Sem sinal",
            "O componente Figma Agent não foi encontrado na página de status oficial.", checkedAt);
    }

    private static ProviderSnapshot CreateQuotaSnapshot() =>
        new("Créditos de IA do Figma", [], null, CreditLimitMessage, "Menu AI balance do Figma")
        {
            Note = "A API de uso do Figma é exclusiva do plano Enterprise e retorna consumo diário atrasado; não informa o saldo atual desta conta."
        };

    private static AgentStatus CreateUnavailable(DateTimeOffset now)
    {
        var health = new ServiceHealthSnapshot(ServiceAvailability.Unknown, "Consultando status",
            "Aguardando a primeira consulta à página de status do Figma.", now);
        return new AgentStatus("figma", "Figma Agent", ActivityState.Unknown, false, health.Detail,
            "Figma Status API (estado global)", CreateQuotaSnapshot())
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
