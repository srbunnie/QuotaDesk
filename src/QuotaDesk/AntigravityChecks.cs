using System.Text.Json;

namespace AIUsageMonitor;

internal static class AntigravityChecks
{
    public static string Run()
    {
        var checks = new List<string>();
        void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException(label); checks.Add("OK: " + label); }
        using var data = JsonDocument.Parse("""
            {"userStatus":{"cascadeModelConfigData":{"clientModelConfigs":[
              {"label":"Gemini teste","quotaInfo":{"remainingFraction":0.75,"resetTime":"2026-10-01T02:00:00Z"}},
              {"label":"Nulo","quotaInfo":{"remainingFraction":null}},
              {"label":"Inválido","quotaInfo":{"remainingFraction":1.1}},null]}}}
            """);
        var windows = AntigravityAgent.ParseQuota(data.RootElement);
        Check(windows.Count == 1 && windows[0].UsedPercent == 25 && windows[0].RemainingPercent == 75, "fração restante convertida em uso; amostras inválidas descartadas");
        Check(windows[0].ResetsAt == DateTimeOffset.Parse("2026-10-01T02:00:00Z"), "renovação UTC preservada");
        using var missing = JsonDocument.Parse("{\"userStatus\":null}");
        Check(AntigravityAgent.ParseQuota(missing.RootElement).Count == 0, "ausência de cota não vira zero artificial");
        ActivityState State(string json) { using var document = JsonDocument.Parse(json); return AntigravityAgent.ParseActivity(document.RootElement); }
        Check(State("{}") == ActivityState.Idle, "snapshot vazio do Hub fica ocioso");
        Check(State("{\"updates\":{\"a\":{\"status\":\"CASCADE_RUN_STATUS_RUNNING\"}}}") == ActivityState.Working, "execução explícita ativa o personagem");
        Check(State("{\"updates\":{\"a\":{\"status\":2,\"waitingSteps\":[{}]}}}") == ActivityState.Waiting, "pedido de interação permanece aguardando");
        Check(State("{\"updates\":{\"a\":{\"status\":1}}}") == ActivityState.Idle, "estado numérico ocioso do Hub");
        Check(State("{\"updates\":{\"a\":{\"status\":\"NOVO_ESTADO\"}}}") == ActivityState.Unknown, "estado desconhecido não simula trabalho");
        var old = new ProviderSnapshot("Antigravity", windows, DateTimeOffset.Parse("2026-09-30T02:00:00Z"));
        var expired = old.WithRenewals(DateTimeOffset.Parse("2026-10-02T02:00:00Z"));
        Check(expired.Stale && expired.Windows[0].UsedPercent == 25, "renovação expirada conserva a última porcentagem até confirmação");

        using var summary = JsonDocument.Parse("""
            {"response":{"groups":[
              {"displayName":"Gemini Models","buckets":[
                {"window":"weekly","remainingFraction":0.97,"resetTime":"2026-10-24T05:08:35Z"},
                {"window":"5h","remainingFraction":1,"resetTime":"2026-10-19T07:42:00Z"}]},
              {"displayName":"Claude and GPT models","buckets":[
                {"window":"weekly","remainingFraction":0.49},{"window":"5h","remainingAmount":"12"}]},
              {"displayName":"Gemini 4 Argon","buckets":[{"window":"weekly","remainingFraction":0.8}]},
              {"displayName":"Quebrado","buckets":[{"window":"5h","remainingFraction":2}]}]}}
            """);
        var grouped = AntigravityAgent.ParseQuotaSummary(summary.RootElement);
        Check(grouped.Count == 4 && grouped.Select(w => w.Group).Distinct().SequenceEqual(["Gemini", "Claude e GPT", "Gemini 4 Argon"]),
            "grupos lidos e nomeados; fração inválida e quantidade sem fração descartadas");
        var gemini = grouped.Where(w => w.Group == "Gemini").ToList();
        Check(gemini.Any(w => w.Kind == WindowKind.FiveHour && w.UsedRounded == 0) && gemini.Any(w => w.Kind == WindowKind.Weekly && w.UsedRounded == 3),
            "grupo traz janela de 5 horas e semanal separadas");
        ModelChoice.Selected = "Claude e GPT";
        var narrowed = ModelChoice.Narrow(grouped);
        Check(narrowed.Count == 1 && narrowed[0].Kind == WindowKind.Weekly && narrowed[0].UsedRounded == 51 && ModelChoice.Options.Count == 3,
            "card mostra só o grupo escolhido");
        ModelChoice.Selected = "Grupo que sumiu";
        Check(ModelChoice.Narrow(grouped).All(w => w.Group == "Gemini"), "grupo salvo que não existe mais volta ao primeiro");
        ModelChoice.Selected = null;
        using var none = JsonDocument.Parse("{\"response\":{}}");
        Check(AntigravityAgent.ParseQuotaSummary(none.RootElement).Count == 0, "versão sem grupos cai na leitura por modelo");
        return string.Join(Environment.NewLine, checks);
    }
}
