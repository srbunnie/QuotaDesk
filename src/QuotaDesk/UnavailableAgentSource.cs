namespace AIUsageMonitor;

/// <summary>A catalog entry with no live adapter. It deliberately supplies no quota or activity claim.</summary>
internal sealed class UnavailableAgentSource(string id, string name, string? reason = null) : IAgentSource
{
    public string Id { get; } = id;
    public event Action? Changed { add { } remove { } }

    public AgentStatus GetStatus(DateTimeOffset now) => new(Id, name, ActivityState.Unknown, false,
        reason is null ? "Nenhum adaptador de atividade conectado." : "Adaptador indisponível.",
        reason is null ? "Sem conexão ou adaptador" : "Adaptador indisponível",
        ProviderSnapshot.Unavailable(name, reason is null
            ? "Sem adaptador de cota conectado."
            : $"Adaptador indisponível: {reason}"));

    public Task RefreshQuotaAsync(bool force) => Task.CompletedTask;
    public void Dispose() { }
}
