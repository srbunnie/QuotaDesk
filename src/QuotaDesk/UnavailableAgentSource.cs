namespace AIUsageMonitor;

/// <summary>A catalog entry with no live adapter. It deliberately supplies no quota or activity claim.</summary>
internal sealed class UnavailableAgentSource(string id, string name, string? reason = null) : IAgentSource
{
    public string Id { get; } = id;
    public event Action? Changed { add { } remove { } }

    public AgentStatus GetStatus(DateTimeOffset now) => new(Id, name, ActivityState.Unknown, false,
        reason is null ? L.T("Nenhum adaptador de atividade conectado.", "No activity adapter connected.") : L.T("Adaptador indisponível.", "Adapter unavailable."),
        reason is null ? L.T("Sem conexão ou adaptador", "No connection or adapter") : L.T("Adaptador indisponível", "Adapter unavailable"),
        ProviderSnapshot.Unavailable(name, reason is null
            ? L.T("Sem adaptador de cota conectado.", "No limit adapter connected.")
            : L.T($"Adaptador indisponível: {reason}", $"Adapter unavailable: {reason}")));

    public Task RefreshQuotaAsync(bool force) => Task.CompletedTask;
    public void Dispose() { }
}
