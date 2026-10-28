using System.Text.Json;

namespace AIUsageMonitor;

internal static class ClaudeQuotaChecks
{
    public static string Run()
    {
        var checks = new List<string>();
        void Check(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException(label);
            checks.Add("OK: " + label);
        }

        var now = DateTimeOffset.Parse("2026-10-15T15:00:00-03:00");
        var confirmed = DateTimeOffset.Parse("2026-10-16T05:00:00-03:00");
        var inaccurate = DateTimeOffset.Parse("2026-10-16T14:29:00-03:00");
        ProviderSnapshot Snapshot(double used, DateTimeOffset? reset, DateTimeOffset captured, bool estimated) =>
            new("Claude Code", [new LimitWindow("Semanal", used, reset, WindowKind.Weekly, estimated)],
                captured, Source: estimated ? "histórico local" : "statusLine do Claude Code (exato)");

        var desktop = Snapshot(78, inaccurate, now.AddMinutes(-1), true);
        var statusLine = Snapshot(72, confirmed, now.AddHours(-2), false);
        var official = ClaudeQuota.MergeSources(statusLine, desktop, now, null);
        var officialWindow = official.Find(WindowKind.Weekly);
        Check(officialWindow is { UsedPercent: 78, ResetEstimated: false } &&
              officialWindow.ResetsAt == confirmed && official.UpdatedAt == desktop.UpdatedAt,
            "horário oficial distante da estimativa prevalece, preservando o percentual mais recente");

        var manual = ClaudeQuota.MergeSources(null, desktop, now, confirmed);
        Check(manual.Find(WindowKind.Weekly)?.ResetsAt == confirmed &&
              manual.Note?.Contains("informado", StringComparison.OrdinalIgnoreCase) == true,
            "renovação às 05:00 informada pelo usuário tem origem explícita");

        var newerOfficialReset = confirmed.AddHours(1);
        var preferred = ClaudeQuota.MergeSources(
            Snapshot(72, newerOfficialReset, now.AddHours(-2), false), desktop, now, confirmed);
        Check(preferred.Find(WindowKind.Weekly) is { ResetEstimated: false } preferredWindow &&
              preferredWindow.ResetsAt == newerOfficialReset,
            "horário oficial tem prioridade sobre a confirmação manual");

        var afterReset = confirmed.AddMinutes(1);
        var oldDesktop = Snapshot(78, inaccurate, confirmed.AddMinutes(-1), true);
        var waiting = ClaudeQuota.MergeSources(null, oldDesktop, afterReset, confirmed).WithRenewals(afterReset);
        Check(waiting.Stale && waiting.Find(WindowKind.Weekly)?.UsedPercent == 78 &&
              waiting.Find(WindowKind.Weekly)?.ResetsAt is null,
            "renovação manual vencida aguarda nova leitura sem inventar uso zero");

        var freshDesktop = Snapshot(4, null, confirmed.AddMinutes(5), true);
        var newCycle = ClaudeQuota.MergeSources(null, freshDesktop, confirmed.AddMinutes(6), confirmed);
        Check(newCycle.Find(WindowKind.Weekly) is { UsedPercent: 4, ResetsAt: null } && !newCycle.Stale,
            "nova amostra após a renovação não reaproveita confirmação do ciclo anterior");

        var friday = DateTimeOffset.Parse("2026-10-16T13:20:00-03:00");
        var nextFriday = DateTimeOffset.Parse("2026-10-23T05:00:00-03:00");
        var current = Snapshot(2, null, friday.AddMinutes(-1), true);
        var recurring = ClaudeQuota.MergeSources(null, current, friday, confirmed, true);
        Check(recurring.Find(WindowKind.Weekly) is { UsedPercent: 2 } recurringWindow &&
              recurringWindow.ResetsAt == nextFriday && recurringWindow.PacePercent(friday) is > 0 &&
              recurringWindow.PaceDelta(friday) is not null,
            "novo ciclo mantém uso real de 2%, renova na próxima sexta às 05:00 e calcula a folga");
        var delayed = ClaudeQuota.MergeSources(null, oldDesktop, afterReset, confirmed, true).WithRenewals(afterReset);
        Check(delayed.Stale && delayed.Find(WindowKind.Weekly) is { UsedPercent: 78, ResetsAt: null },
            "recorrência não transporta percentual antigo para um ciclo novo sem leitura");
        var exactRecurring = ClaudeQuota.MergeSources(statusLine, desktop, now, confirmed, true);
        Check(exactRecurring.Find(WindowKind.Weekly)?.ResetsAt == confirmed,
            "horário oficial continua prioritário com recorrência habilitada");
        Check(ClaudeQuota.NextConfirmedWeeklyReset(confirmed, confirmed) == nextFriday &&
              ClaudeQuota.NextConfirmedWeeklyReset(confirmed, nextFriday.AddDays(21).AddMinutes(1)) == nextFriday.AddDays(28),
            "limite exato e várias semanas sem abrir o app avançam para a sexta correta");
        ProviderSnapshot? History(params (string At, int Used)[] samples)
        {
            var path = Path.Combine(Path.GetTempPath(), "quotadesk-claude-history-check-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    version = 2,
                    samples = samples.Select(sample => new
                    {
                        t = DateTimeOffset.Parse(sample.At).ToUnixTimeMilliseconds(),
                        u = new { fh = 0, sd = sample.Used }
                    }).ToArray()
                }));
                return ClaudeQuota.ReadDesktopHistory(now, path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        var wideBoundary = History(
            ("2026-10-08T20:00:00-03:00", 90),
            ("2026-10-09T09:00:00-03:00", 10),
            ("2026-10-15T13:00:00-03:00", 78));
        Check(wideBoundary?.Find(WindowKind.Weekly) is { UsedPercent: 78, ResetsAt: null },
            "histórico com queda em intervalo de 13 horas não inventa horário de renovação");

        var narrowBoundary = History(
            ("2026-10-09T04:55:00-03:00", 90),
            ("2026-10-09T05:05:00-03:00", 10),
            ("2026-10-15T13:00:00-03:00", 78));
        Check(narrowBoundary?.Find(WindowKind.Weekly) is { UsedPercent: 78, ResetsAt: null },
            "mesmo um histórico semanal com amostras próximas exige horário oficial ou informado");

        return string.Join(Environment.NewLine, checks);
    }
}
