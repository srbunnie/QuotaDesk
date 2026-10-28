using System.Globalization;
using System.Text.Json;

namespace AIUsageMonitor;

internal enum ActivityState { Unknown, Idle, Waiting, Working }

internal enum ServiceAvailability { Unknown, Operational, Degraded, PartialOutage, MajorOutage, Maintenance }

internal sealed record ServiceHealthSnapshot(ServiceAvailability State, string Label, string Detail,
    DateTimeOffset CheckedAt, bool Stale = false);

internal enum WindowKind { FiveHour, Weekly, Other }

/// <summary>A quota window. UsedPercent comes from the tool; ResetsAt may be exact or estimated.</summary>
internal sealed record LimitWindow(string Label, double UsedPercent, DateTimeOffset? ResetsAt,
    WindowKind Kind = WindowKind.Other, bool ResetEstimated = false, string? ResetHint = null)
{
    /// <summary>The reset time of this window has passed: the quota renewed (a new window starts on the next use).</summary>
    public bool Renewed { get; init; }
    public DateTimeOffset? RenewedAt { get; init; }
    /// <summary>Quota group that shares this window (Antigravity: "Gemini", "Claude e GPT"...); null for single-pool tools.</summary>
    public string? Group { get; init; }
    /// <summary>Balance in the tool's own unit (OpenRouter US$, Magnific credits), shown in place of the bare percentage.</summary>
    public string? Amount { get; init; }
    /// <summary>Credit balance and measured burn rate (Magnific, OpenRouter); null for percentage-only quotas.</summary>
    public CreditBalance? Credits { get; init; }

    public int UsedRounded => (int)Math.Round(Math.Clamp(UsedPercent, 0, 100));
    public int RemainingPercent => 100 - UsedRounded;
    public string RemainingText => L.T($"{RemainingPercent}% restante", $"{RemainingPercent}% left");

    /// <summary>Length of the window when the tool defines it (5 hours / 7 days); unknown for per-model quotas.</summary>
    public TimeSpan? Duration => Kind switch
    {
        WindowKind.FiveHour => TimeSpan.FromHours(5),
        WindowKind.Weekly => TimeSpan.FromDays(7),
        _ => null
    };

    /// <summary>
    /// Even-pace reference: the percentage that would be used by now if the quota were spent evenly across the window.
    /// Null when the window's start cannot be derived (no reset time or unknown duration).
    /// </summary>
    public double? PacePercent(DateTimeOffset now)
    {
        if (ResetsAt is not { } reset || Duration is not { } duration) return null;
        var left = reset - now;
        if (left <= TimeSpan.Zero || left > duration) return null;
        return (1 - left / duration) * 100;
    }

    /// <summary>Usage minus the even pace, in points: positive = spending faster than the window allows.</summary>
    public double? PaceDelta(DateTimeOffset now) => PacePercent(now) is { } pace ? UsedPercent - pace : null;

    /// <summary>What the current rate leads to: when the quota would run out, or how much would be left at the reset.</summary>
    public string? PaceForecast(DateTimeOffset now)
    {
        if (PacePercent(now) is not { } pace || ResetsAt is not { } reset || Duration is not { } duration || pace < 2 || UsedPercent < 1) return null;
        var start = reset - duration;
        var emptyAt = start + (now - start) * (100 / UsedPercent);
        if (emptyAt < reset)
        {
            var local = emptyAt.ToLocalTime();
            var when = local.Date == now.LocalDateTime.Date ? L.T($"hoje {local:HH:mm}", $"today {local:HH:mm}") : local.ToString(L.T("ddd dd/MM HH:mm", "ddd MM/dd HH:mm"), L.Culture);
            return L.T($"nesse ritmo a cota acaba ~{when}, antes de renovar", $"at this pace the limit runs out ~{when}, before it resets");
        }
        var left = Math.Clamp(100 - UsedPercent * 100 / pace, 0, 100);
        return L.T($"nesse ritmo sobram ~{left:0}% na renovação", $"at this pace ~{left:0}% will be left at the reset");
    }

    public string? Countdown(DateTimeOffset now)
    {
        if (ResetsAt is null) return null;
        var time = ResetsAt.Value - now;
        return time <= TimeSpan.Zero ? null : Span(time);
    }

    public static string Span(TimeSpan time)
    {
        if (time.TotalDays >= 1) return $"{(int)time.TotalDays}d {time.Hours}h";
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours}h {time.Minutes:00}min";
        return $"{Math.Max(1, (int)Math.Ceiling(time.TotalMinutes))}min";
    }

    /// <summary>
    /// When the quota runs out if the average rate since the window started continues (linear: used / elapsed).
    /// BeforeReset says whether that happens before the renewal. Null when the window has no known start, too little
    /// time has passed (under 2% of it) or nothing was used yet.
    /// </summary>
    public (DateTimeOffset At, bool BeforeReset)? Depletion(DateTimeOffset now)
    {
        if (PacePercent(now) is not { } pace || ResetsAt is not { } reset || Duration is not { } duration || pace < 2 || UsedPercent < 1 || UsedPercent >= 100)
            return null;
        var start = reset - duration;
        var emptyAt = start + (now - start) * (100 / UsedPercent);
        return (emptyAt, emptyAt < reset);
    }

    public string ResetText(DateTimeOffset now)
    {
        if (ResetsAt is null) return ResetHint ?? L.T("renovação indisponível", "reset time unavailable");
        var countdown = Countdown(now);
        if (countdown is null) return ResetEstimated ? L.T("renovação estimada já passou", "estimated reset has passed") : L.T("renovação pendente", "reset pending");
        return ResetEstimated ? L.T($"renova em ~{countdown}", $"resets in ~{countdown}") : L.T($"renova em {countdown}", $"resets in {countdown}");
    }

    public string? ResetClock(DateTimeOffset now)
    {
        if (ResetsAt is null) return null;
        var local = ResetsAt.Value.ToLocalTime();
        var text = local.Date == now.LocalDateTime.Date ? L.T($"hoje {local:HH:mm}", $"today {local:HH:mm}")
            : local.Date == now.LocalDateTime.Date.AddDays(1) ? L.T($"amanhã {local:HH:mm}", $"tomorrow {local:HH:mm}")
            : local.ToString(L.T("ddd dd/MM HH:mm", "ddd MM/dd HH:mm"), L.Culture);
        return ResetEstimated ? "~" + text : text;
    }
}

/// <summary>Limit resets the account can spend (reported by the tool): how many and when the listed ones expire.</summary>
internal sealed record ResetCredits(int Available, IReadOnlyList<DateTimeOffset> Expirations)
{
    /// <summary>Soonest expiry still in the future, if the tool listed one.</summary>
    public DateTimeOffset? NextExpiry(DateTimeOffset now) => Expirations.Where(x => x > now).Cast<DateTimeOffset?>().FirstOrDefault();
}

/// <summary>Quota data from one tool. Stale data is kept but flagged, never presented as current.</summary>
internal sealed record ProviderSnapshot(string Name, IReadOnlyList<LimitWindow> Windows,
    DateTimeOffset? UpdatedAt, string? Error = null, string? Source = null, bool Stale = false, string? Note = null)
{
    /// <summary>Null when the tool does not report limit resets at all.</summary>
    public ResetCredits? Resets { get; init; }

    public static ProviderSnapshot Unavailable(string name, string message, string? source = null) =>
        new(name, [], null, message, source);

    public LimitWindow? Find(WindowKind kind) => Windows.FirstOrDefault(x => x.Kind == kind);

    /// <summary>
    /// Windows whose reset time already passed are shown as renewed (0% used) instead of their last, stale reading.
    /// Applied once, before the panel, the tray icon and the renewal notifications read the data, so they agree.
    /// </summary>
    public ProviderSnapshot WithRenewals(DateTimeOffset now)
    {
        if (!Windows.Any(w => w.ResetsAt is { } r && r <= now)) return this;
        var windows = Windows.Select(w => w.ResetsAt is { } reset && reset <= now
            ? w with { ResetsAt = null, ResetHint = L.T("aguardando nova leitura", "waiting for a new reading"), Renewed = false, RenewedAt = null }
            : w).ToList();
        return this with { Windows = windows, Stale = true,
            Note = L.T("Horário de renovação passou; percentual preservado até uma nova leitura confirmar o uso. ",
                "Reset time passed; the percentage is kept until a new reading confirms the usage. ") + Note };
    }
}

/// <summary>Everything the UI needs about one tool. New tools only have to produce this record.</summary>
internal sealed record AgentStatus(string Id, string Name, ActivityState State, bool StateEstimated,
    string StateDetail, string StateSource, ProviderSnapshot Quota)
{
    /// <summary>Global health reported by the provider's public status page; this is not user activity.</summary>
    public ServiceHealthSnapshot? ServiceHealth { get; init; }
    /// <summary>Sessions blocked on the user (permission, question). Only tools that report them fill this; empty otherwise.</summary>
    public IReadOnlyList<PendingItem> Pending { get; init; } = [];
    /// <summary>A session is open even though nothing is running (shown as "Ativo" instead of "Ocioso").</summary>
    public bool SessionOpen { get; init; }
    /// <summary>What the agent is doing right now, for the character's speech bubble; null when the tool does not say.</summary>
    public AgentActivity? Activity { get; init; }
}

/// <summary>
/// The latest step of a session in a few words: the task title (Codex thread name, or the project folder) and the action
/// ("Executou o comando"). Built from event and tool names only; prompts, answers and file contents are never read.
/// </summary>
internal sealed record AgentActivity(string? Title, string Action, DateTimeOffset At);

internal enum ActionKind { Thinking, Command, Edit, Read, Search, Web, Delegate, Plan, Connector, Other }

internal static class AgentActions
{
    public static string Describe(ActionKind kind, bool done, string? other = null) => (kind, done) switch
    {
        (ActionKind.Thinking, _) => L.T("Pensando…", "Thinking…"),
        (ActionKind.Command, false) => L.T("Executando um comando", "Running a command"),
        (ActionKind.Command, true) => L.T("Executou o comando", "Ran the command"),
        (ActionKind.Edit, false) => L.T("Editando um arquivo", "Editing a file"),
        (ActionKind.Edit, true) => L.T("Editou um arquivo", "Edited a file"),
        (ActionKind.Read, false) => L.T("Lendo um arquivo", "Reading a file"),
        (ActionKind.Read, true) => L.T("Leu um arquivo", "Read a file"),
        (ActionKind.Search, false) => L.T("Procurando no código", "Searching the code"),
        (ActionKind.Search, true) => L.T("Procurou no código", "Searched the code"),
        (ActionKind.Web, false) => L.T("Pesquisando na web", "Searching the web"),
        (ActionKind.Web, true) => L.T("Pesquisou na web", "Searched the web"),
        (ActionKind.Delegate, false) => L.T("Delegando a um subagente", "Delegating to a subagent"),
        (ActionKind.Delegate, true) => L.T("Subagente terminou", "Subagent finished"),
        (ActionKind.Plan, _) => L.T("Atualizou o plano", "Updated the plan"),
        (ActionKind.Connector, false) => L.T("Usando um conector", "Using a connector"),
        (ActionKind.Connector, true) => L.T("Usou um conector", "Used a connector"),
        (_, false) => other is null ? L.T("Usando uma ferramenta", "Using a tool") : L.T($"Usando {other}", $"Using {other}"),
        _ => other is null ? L.T("Usou uma ferramenta", "Used a tool") : L.T($"Usou {other}", $"Used {other}")
    };

    /// <summary>Claude Code tool names (already reduced to [A-Za-z0-9_]; MCP tools arrive as "MCP").</summary>
    public static ActionKind ForClaudeTool(string? tool) => tool switch
    {
        "Bash" or "PowerShell" or "BashOutput" or "KillShell" => ActionKind.Command,
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ActionKind.Edit,
        "Read" or "NotebookRead" => ActionKind.Read,
        "Grep" or "Glob" or "LS" => ActionKind.Search,
        "WebFetch" or "WebSearch" => ActionKind.Web,
        "Task" or "Agent" => ActionKind.Delegate,
        "TodoWrite" => ActionKind.Plan,
        "MCP" => ActionKind.Connector,
        _ => ActionKind.Other
    };

    /// <summary>A title for the bubble: control characters removed and cut to a length that fits two short lines.</summary>
    public static string? CleanTitle(string? text, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? null : clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }
}

/// <summary>One session that cannot continue until the user answers. Key identifies it so it is announced and dismissed once.</summary>
internal sealed record PendingItem(string Key, string Provider, string Project, string Kind, DateTimeOffset Since);

internal interface IAgentSource : IDisposable
{
    string Id { get; }
    event Action? Changed;
    AgentStatus GetStatus(DateTimeOffset now);
    Task RefreshQuotaAsync(bool force);
    void SetForeground(bool visible) { }
}

internal static class StateText
{
    public static string Label(ActivityState state) => state switch
    {
        ActivityState.Working => L.T("Trabalhando", "Working"),
        ActivityState.Waiting => L.T("Aguardando", "Waiting"),
        ActivityState.Idle => L.T("Ocioso", "Idle"),
        _ => L.T("Sem sinal", "No signal")
    };

    public static string Label(AgentStatus agent)
    {
        if (agent.ServiceHealth is not { } health) return Label(agent.State) + (agent.StateEstimated ? " ~" : "");
        return health.Label + (health.Stale ? " ~" : "");
    }
}

internal static class LimitParsing
{
    public static LimitWindow? ParseWindow(JsonElement source, string label, string percentName, WindowKind kind)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(percentName, out var percent) ||
            percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used) || !double.IsFinite(used))
            return null;
        DateTimeOffset? reset = null;
        if (source.TryGetProperty("resetsAt", out var camel)) reset = Epoch(camel);
        if (reset is null && source.TryGetProperty("resets_at", out var snake)) reset = Epoch(snake);
        return new(label, Math.Clamp(used, 0, 100), reset, kind);
    }

    public static DateTimeOffset? Epoch(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
                return seconds > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(seconds)
                    : DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(),
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;
        }
        catch (ArgumentOutOfRangeException) { }
        return null;
    }

    public static WindowKind KindForMinutes(int minutes) => minutes switch
    {
        300 => WindowKind.FiveHour, 10080 => WindowKind.Weekly, _ => WindowKind.Other
    };

    public static string LabelForMinutes(int minutes) => minutes switch
    {
        300 => L.T("5 horas", "5 hours"), 10080 => L.T("Semanal", "Weekly"), 1440 => L.T("24 horas", "24 hours"),
        _ when minutes > 0 => $"{minutes.ToString(CultureInfo.InvariantCulture)} min",
        _ => L.T("Limite", "Limit")
    };

    public static string Ago(TimeSpan span) =>
        span.TotalMinutes < 1 ? L.T("agora", "now") :
        span.TotalHours < 1 ? L.T($"há {(int)span.TotalMinutes} min", $"{(int)span.TotalMinutes} min ago") :
        span.TotalDays < 1 ? L.T($"há {(int)span.TotalHours}h {span.Minutes:00}min", $"{(int)span.TotalHours}h {span.Minutes:00}min ago")
            : L.T($"há {(int)span.TotalDays}d", $"{(int)span.TotalDays}d ago");
}

/// <summary>
/// The Antigravity quota group (or, on versions without groups, the model) picked on its card. Kept by name (the list can
/// change order or gain entries) and shared, so the side dock and the tray show the same choice as the panel.
/// </summary>
internal static class ModelChoice
{
    public static string? Selected { get; set; }
    public static event Action? Changed;
    /// <summary>Names offered by the last reading, in display order.</summary>
    public static IReadOnlyList<string> Options { get; private set; } = [];
    public static int Index => Math.Max(0, Options.ToList().IndexOf(Selected ?? ""));

    private static string Key(LimitWindow w) => w.Group ?? w.Label;

    /// <summary>Only the windows of the chosen group (both its 5-hour and weekly windows), or the chosen model.</summary>
    public static IReadOnlyList<LimitWindow> Narrow(IReadOnlyList<LimitWindow> all)
    {
        Options = all.Select(Key).Distinct().ToList();
        if (Options.Count == 0) return all;
        var chosen = Options[Index];
        return all.Where(w => Key(w) == chosen).ToList();
    }

    /// <summary>The window that limits the chosen group first (for a single model, that model).</summary>
    public static LimitWindow? Current(AgentStatus agent) =>
        agent.Quota.Windows.OrderBy(w => w.RemainingPercent).FirstOrDefault();

    public static void Step(int step)
    {
        if (Options.Count < 2) return;
        Selected = Options[((Index + step) % Options.Count + Options.Count) % Options.Count];
        Changed?.Invoke();
    }
}

internal enum PauseKind { None, FiveHour, Weekly }

/// <summary>
/// Why a tool is out of quota and until when. Weekly (or credits) wins over 5 hours: renewing the short window does not
/// bring back a tool whose week is spent, so the return time is the latest reset among the exhausted windows.
/// Stale or already-expired readings never count.
/// </summary>
internal sealed record QuotaPause(PauseKind Kind, DateTimeOffset? Until, bool Estimated)
{
    public static readonly QuotaPause None = new(PauseKind.None, null, false);

    public static QuotaPause For(AgentStatus agent, DateTimeOffset now)
    {
        if (agent.Quota.Stale) return None;
        var spent = agent.Quota.Windows.Where(w => w.UsedRounded >= 100 && w.Renewed != true &&
            !(w.ResetsAt is { } reset && reset <= now && !w.ResetEstimated)).ToList();
        if (spent.Count == 0) return None;
        var kind = spent.All(w => w.Kind == WindowKind.FiveHour) ? PauseKind.FiveHour : PauseKind.Weekly;
        var until = spent.Any(w => w.ResetsAt is null) ? null : spent.Max(w => w.ResetsAt);
        return new(kind, until, spent.Any(w => w.ResetEstimated));
    }

    /// <summary>Two short ASCII lines for the pixel sign: the return day and time, a countdown in the last hour.</summary>
    public string[] SignLines(DateTimeOffset now)
    {
        var head = Kind == PauseKind.FiveHour ? L.T("PAUSA", "PAUSE") : L.T("VOLTO", "BACK");
        if (Until is not { } until) return Kind == PauseKind.Weekly ? [L.T("SEM", "NO"), L.T("COTA", "LIMIT")] : [head, "--:--"];
        var tilde = Estimated ? "~" : "";
        var left = until - now;
        if (left <= TimeSpan.FromHours(1))
        {
            var clamp = left < TimeSpan.Zero ? TimeSpan.Zero : left;
            return [head + L.T(" EM", " IN"), $"{tilde}{(int)clamp.TotalMinutes:00}:{clamp.Seconds:00}"];
        }
        var local = until.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var isToday = local.Date == today;
        var day = isToday ? L.T("HOJE", "TODAY") : local.Date == today.AddDays(1) ? L.T("AMANHA", "TMRW")
            : (L.En ? new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" } : new[] { "DOM", "SEG", "TER", "QUA", "QUI", "SEX", "SAB" })[(int)local.DayOfWeek];
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture); // the pixel sign has no glyph for other separators
        return [Kind == PauseKind.FiveHour ? L.T("PAUSA ATE", "PAUSE UNTIL") : head + " " + day, $"{tilde}{(Kind == PauseKind.FiveHour && !isToday ? day + " " : "")}{clock}"];
    }
}
