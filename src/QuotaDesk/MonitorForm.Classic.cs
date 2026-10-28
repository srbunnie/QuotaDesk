using System.Drawing.Drawing2D;
using System.Globalization;

namespace AIUsageMonitor;

/// <summary>
/// Quota templates and the title bar. "loft" is the voxel Loft with stacked quota rows; "classic" brings back the
/// original panel: the three-desk voxel office and one big quota per card (click toggles 5 hours / weekly).
/// The title (logo, weekday, date and clock) is shown in both.
/// </summary>
internal sealed partial class MonitorForm
{
    private const int TitleHeight = 90;
    private const int ClassicCardHeight = 296;
    private readonly ClassicOffice _classic = new();
    // Classic cards: the window each card shows when it has both (picked automatically until the user clicks).
    private readonly Dictionary<string, WindowKind> _view = new(StringComparer.OrdinalIgnoreCase);
    private int _clockMinute = -1;

    private bool Classic => Settings.QuotaTemplate == "classic";
    private bool ClassicScene => Classic && ClassicOffice.Supports(_agents);
    /// <summary>Loft-style card geometry (compact header, stacked rows); the classic template keeps the original one.</summary>
    private bool LoftCards => _sharedOffice.IsAvailable && !Classic;
    /// <summary>The pixel-art office: fewer than three tools in the classic template, or no voxel art at all.</summary>
    private bool PixelScene => Classic ? !ClassicScene : !_sharedOffice.IsAvailable;
    private RectangleF TitleRect => new(0, 0, LogicalWidth, HeaderLayoutHeight);

    internal void SetTemplate(string template)
    {
        if (Settings.QuotaTemplate == template) return;
        Settings.QuotaTemplate = template;
        _hover = null;
        _tips.Hide(this);
        PreferencesChanged();
    }

    /// <summary>Repaints the title when the minute turns (the scene ticks never cover it).</summary>
    private void TickClock()
    {
        var minute = DateTime.Now.Minute;
        if (minute == _clockMinute) return;
        _clockMinute = minute;
        Invalidate(Scale(TitleRect));
    }

    // ---------------------------------------------------------------- title

    private void PaintTitle(Graphics g)
    {
        var now = DateTime.Now;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var date = now.ToString("ddd, dd/MM", pt).Replace(".", "").ToUpperInvariant();
        var time = now.ToString("HH:mm");
        // Header actions sit on the title row, left of the menu button; anything they would cover is left out.
        var free = Settings.ShowPanelHeader ? HeaderButtonRect(0).X - 8 : PanelMenuRect.X - 8;
        if (_officeViewport.HasNavigation && Compact) free = OfficeNavigationRect(-1).X - 8;

        if (Compact)
        {
            // One line: no room for the wordmark, but the day and the time stay visible.
            var font = Theme.Font("mono", 13f, FontStyle.Bold);
            var line = $"{date}  {time}";
            if (Margin + 4 + g.MeasureString(line, font).Width <= free)
            {
                TextOut(g, date, font, Color.FromArgb(180, 190, 218), Margin + 4, 17);
                TextOut(g, time, font, Theme.Orange, Margin + 4 + g.MeasureString(date + "  ", font).Width, 17);
            }
            return;
        }

        // "QUOTA" white + "DESK" orange, chunky pixel type with a hard shadow.
        var scale = LogicalWidth >= 600 ? 3f : 2f;
        var x = Margin + 68f;
        var quotaWidth = PixelFont.Measure("QUOTA", scale, 4, bold: true);
        var titleWidth = quotaWidth + 5 + PixelFont.Measure("DESK", scale, 4, bold: true);
        if (x + titleWidth <= free)
        {
            using (var logo = TrayIconRenderer.Render(48, TrayIconRenderer.Logo)) g.DrawImage(logo, Margin + 4, 20, 48, 48);
            PixelFont.Draw(g, "QUOTADESK", x + 2, 26, scale, Color.FromArgb(0, 0, 0), 4, bold: true);
            PixelFont.Draw(g, "QUOTA", x, 24, scale, Color.FromArgb(240, 242, 248), 4, bold: true);
            PixelFont.Draw(g, "DESK", x + quotaWidth + 4 + 1, 24, scale, Theme.Orange, 4, bold: true);
            if (LogicalWidth >= 600)
            {
                const string subtitle = "MONITOR DE IA // LOCAL";
                var subtitleFont = Theme.Font("mono", 12.5f);
                var gap = Math.Max(0, (titleWidth - g.MeasureString(subtitle, subtitleFont, 1000, Typographic).Width) / (subtitle.Length - 1));
                Tracked(g, subtitle, subtitleFont, Color.FromArgb(180, 190, 218), x, 54, gap);
            }
        }

        var timeWidth = PixelFont.Measure(time, 2, 3, bold: true);
        var timeX = LogicalWidth - Margin - 10 - timeWidth;
        PixelFont.Draw(g, time, timeX + 1, 60, 2, Color.FromArgb(90, 40, 10), 3, bold: true);
        PixelFont.Draw(g, time, timeX, 59, 2, Theme.Orange, 3, bold: true);
        RightText(g, date, Theme.Font("mono", 12.5f), Color.FromArgb(180, 190, 218), timeX - 12, 62);

        using var divider = new Pen(Line, 2f);
        g.DrawLine(divider, Margin, TitleHeight - 8, LogicalWidth - Margin, TitleHeight - 8);
    }

    // ---------------------------------------------------------------- classic card

    /// <summary>The window a classic card shows: the user's choice, a just-renewed one, otherwise the tightest.</summary>
    private (LimitWindow? Main, WindowKind Kind, bool HasBoth) Pick(AgentStatus agent)
    {
        // Balance-only tools (Magnific, OpenRouter) show their first balance or quota.
        if (!UsesTimeSlots(agent)) return (QuotaRows(agent).FirstOrDefault(), WindowKind.Other, false);
        var five = agent.Quota.Find(WindowKind.FiveHour);
        var week = agent.Quota.Find(WindowKind.Weekly);
        var both = five is not null && week is not null;
        if (_view.TryGetValue(agent.Id, out var chosen) && both) return (chosen == WindowKind.Weekly ? week : five, chosen, true);
        // A renewed window stays on the card until a newer reading confirms it (or at least one hour), so the renewal
        // is always visible instead of an old percentage.
        var justRenewed = new[] { five, week }.FirstOrDefault(w => w is { Renewed: true, RenewedAt: { } at } &&
            (agent.Quota.UpdatedAt is not { } read || at > read || DateTimeOffset.Now - at < TimeSpan.FromHours(1)));
        if (justRenewed is not null) return (justRenewed, justRenewed.Kind, both);
        if (five is null && week is null) return (QuotaRows(agent).FirstOrDefault(), WindowKind.Other, false);
        if (five is null) return (week, WindowKind.Weekly, false);
        if (week is null) return (five, WindowKind.FiveHour, false);
        return week.RemainingPercent < five.RemainingPercent ? (week, WindowKind.Weekly, true) : (five, WindowKind.FiveHour, true);
    }

    /// <summary>Click on a classic card with both windows: switch between 5 hours and weekly.</summary>
    private bool ClassicCardClick(int index)
    {
        var agent = _agents[index];
        var (_, kind, both) = Pick(agent);
        if (!both) return false;
        _view[agent.Id] = kind == WindowKind.Weekly ? WindowKind.FiveHour : WindowKind.Weekly;
        _lastTip = null;
        Invalidate();
        return true;
    }

    /// <summary>
    /// Body of the original card under the shared header: big usage number, bar with the even-pace marker, the other
    /// window as a thin bar, reading time and the renewal box.
    /// </summary>
    private void PaintClassicBody(Graphics g, AgentStatus agent, RectangleF r, Theme.AgentLook look, DateTimeOffset now)
    {
        var (main, kind, hasBoth) = Pick(agent);
        var x = r.X + 20;
        var right = r.Right - 20;
        var stale = agent.Quota.Stale;
        var noData = agent.Quota.Windows.Count == 0;
        var labelColor = Theme.Label;
        var balance = main?.Credits is not null || main?.Amount is not null;

        var label = kind switch
        {
            WindowKind.Weekly => "USO SEMANAL",
            WindowKind.FiveHour => "USO 5 HORAS",
            _ when balance => "SALDO",
            _ when agent.ServiceHealth is not null && noData => "SERVIÇO",
            _ => "USO POR MODELO"
        };
        PixelFont.Draw(g, AsciiUpper(label, 16), x, r.Y + 108, 2, labelColor);
        if (agent.Id == "antigravity" && ModelChoice.Options.Count > 1)
            RightText(g, $"‹ {ModelChoice.Index + 1}/{ModelChoice.Options.Count} ›", Theme.Font("mono", 11), look.Accent, right - (hasBoth ? 28 : 0), r.Y + 110);
        if (main?.Renewed == true)
            PixelFont.Draw(g, "RENOVADA", right - PixelFont.Measure("RENOVADA", 2) - (hasBoth ? 28 : 0), r.Y + 108, 2, Theme.Green);
        if (main?.Renewed == true) stale = false; // a renewal is certain from the clock, not an old reading
        if (hasBoth)
        {
            // Two dots: this card can switch between the 5-hour and weekly windows (click).
            for (var i = 0; i < 2; i++)
            {
                var active = (i == 0 ? WindowKind.FiveHour : WindowKind.Weekly) == kind;
                var dot = new RectangleF(right - 20 + i * 12, r.Y + 111, 8, 8);
                if (active) using (var b = new SolidBrush(look.Accent)) g.FillEllipse(b, dot);
                else using (var p = new Pen(Color.FromArgb(120, labelColor), 1.5f)) g.DrawEllipse(p, dot);
            }
        }

        var mainExpired = main?.ResetsAt is { } at && at <= now && !main.ResetEstimated;
        var bar = new RectangleF(x, r.Y + 176, right - x, 14);
        using (var trackPath = Theme.Rounded(bar, 7))
        using (var track = new SolidBrush(Color.FromArgb(30, 36, 54)))
            g.FillPath(track, trackPath);

        if (main is null || mainExpired)
        {
            PixelFont.Draw(g, "--", x, r.Y + 132, 5, Color.FromArgb(70, 78, 104), 5, bold: true);
            var why = agent.ServiceHealth is { } health && noData ? StateText.Label(agent).ToLowerInvariant()
                : noData ? "sem dados" : mainExpired ? "aguardando" : "não informado";
            RightText(g, why, Theme.Font("mono", 12.5f), noData && agent.ServiceHealth is null ? Theme.Amber : labelColor, right, r.Y + 146);
        }
        else
        {
            var used = main.UsedRounded;
            var critical = used >= Settings.CriticalAt;
            var numberColor = stale ? Theme.TextDim : critical ? Theme.Red : Color.FromArgb(246, 248, 252);
            PixelFont.Draw(g, $"{used}%", x + 2, r.Y + 134, 5, Color.FromArgb(0, 0, 0), 5, bold: true);
            PixelFont.Draw(g, $"{used}%", x, r.Y + 132, 5, numberColor, 5, bold: true);
            // A balance names its amount in the tool's own unit; a quota says how much is left.
            RightText(g, main.Amount ?? $"{main.RemainingPercent}%", Theme.Font("mono", 14f, FontStyle.Bold), labelColor, right, r.Y + 134);
            RightText(g, main.Amount is null ? "restante" : "disponível", Theme.Font("mono", 13f), labelColor, right, r.Y + 152);

            if (used > 0)
            {
                var color = stale ? Theme.Mix(look.Accent, Theme.TextFaint, 0.55f) : critical ? Theme.Red : look.Accent;
                var fillRect = new RectangleF(bar.X, bar.Y, Math.Max(14, bar.Width * used / 100f), bar.Height);
                using var fillPath = Theme.Rounded(fillRect, 7);
                using var brush = new LinearGradientBrush(fillRect, Theme.Mix(color, Color.White, 0.25f), color, 90f);
                g.FillPath(brush, fillPath);
            }
        }

        // Pace marker: where the bar would be if the quota were spent evenly until the reset.
        var paceDelta = main is null || mainExpired || stale ? null : main.PaceDelta(now);
        if (paceDelta is { } delta && main!.PacePercent(now) is { } pace)
        {
            var px = (float)Math.Round(Math.Clamp(bar.X + bar.Width * (float)pace / 100f, bar.X + 3, bar.Right - 3));
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var outline = new SolidBrush(Color.FromArgb(5, 8, 14))) g.FillRectangle(outline, px - 4, bar.Y - 6, 8, bar.Height + 12);
            using (var flag = new SolidBrush(Theme.PaceColor(delta))) g.FillRectangle(flag, px - 2, bar.Y - 4, 4, bar.Height + 8);
            g.SmoothingMode = old;
            RightText(g, Theme.PaceLabel(delta), Theme.Font("mono", 12.5f, FontStyle.Bold), Theme.PaceColor(delta), right, r.Y + 195);
        }

        // The other window (weekly under 5 hours, or the reverse) as a thin bar, so both are visible at once.
        if (hasBoth && agent.Quota.Find(kind == WindowKind.FiveHour ? WindowKind.Weekly : WindowKind.FiveHour) is { } other)
        {
            // Its own row, with its own pace (+N above / -N of slack), so nothing here reads as belonging to the main bar.
            var secondDelta = stale ? null : other.PaceDelta(now);
            var otherLabel = $"{(other.Kind == WindowKind.Weekly ? "SEM" : "5H")} {other.UsedRounded}%" +
                             (secondDelta is { } sd && Math.Abs(sd) > 3 ? $" {(sd > 0 ? "+" : "-")}{Math.Abs(sd):0}" : "");
            if (secondDelta is { } tint) labelColor = Theme.PaceColor(tint);
            var labelFont = Theme.Font("mono", 11.5f, FontStyle.Bold);
            var labelWidth = g.MeasureString(otherLabel, labelFont, 1000, Typographic).Width + 2;
            var thin = new RectangleF(x, r.Y + 217, right - x - labelWidth - 8, 6);
            var otherColor = stale ? Theme.Mix(look.Accent, Theme.TextFaint, 0.55f) : other.UsedRounded >= Settings.CriticalAt ? Theme.Red : Theme.Mix(look.Accent, Color.White, 0.3f);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var track = new SolidBrush(Color.FromArgb(30, 36, 54))) g.FillRectangle(track, thin);
            using (var fill = new SolidBrush(otherColor)) g.FillRectangle(fill, thin.X, thin.Y, thin.Width * other.UsedRounded / 100f, thin.Height);
            if (!stale && other.PacePercent(now) is { } otherPace && other.PaceDelta(now) is { } otherDelta)
            {
                var px = (float)Math.Round(Math.Clamp(thin.X + thin.Width * (float)otherPace / 100f, thin.X + 1, thin.Right - 1));
                using var outline = new SolidBrush(Color.FromArgb(5, 8, 14));
                g.FillRectangle(outline, px - 3, thin.Y - 3, 6, thin.Height + 6);
                using var flag = new SolidBrush(Theme.PaceColor(otherDelta));
                g.FillRectangle(flag, px - 1.5f, thin.Y - 2, 3, thin.Height + 4);
            }
            g.SmoothingMode = old;
            RightText(g, otherLabel, labelFont, labelColor, right, thin.Y - 5);
        }
        // A credit balance shows its burn rate on the row the second bar would use.
        else if (main?.Credits is { } credits && credits.PerDay is { } perDay)
        {
            var text = $"GASTO ~{credits.Format(perDay)}/DIA" +(credits.EmptyAt is { } empty ? $" · ACABA {empty.LocalDateTime:dd/MM}" : "");
            using var rate = new SolidBrush(stale ? Theme.TextFaint : Theme.Label);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(text, Theme.Font("mono", 11.5f, FontStyle.Bold), rate, new RectangleF(x, r.Y + 211, right - x, 18), format);
        }
        // Limit resets the account can still spend (only tools that report them), on the row the second bar would use.
        else if (agent.Quota.Resets is { } resets)
        {
            var resetFont = Theme.Font("mono", 11.5f, FontStyle.Bold);
            var expiry = resets.NextExpiry(now);
            var text = resets.Available == 0 ? "RESETS 0"
                : $"RESETS {resets.Available}" + (expiry is { } e ? $" · VENCE {e.LocalDateTime:dd/MM}" : "");
            using var resetBrush = new SolidBrush(resets.Available > 0 ? Theme.Green : Theme.TextFaint);
            g.DrawString(text, resetFont, resetBrush, x, r.Y + 211);
        }

        var freshness = agent.Quota.UpdatedAt is { } updated
            ? $"{(stale ? "DESATUALIZADO" : "LEITURA")} {updated.LocalDateTime:HH:mm}" : noData && agent.ServiceHealth is not null ? "STATUS DO SERVIÇO" : "AGUARDANDO DADOS";
        if (agent.Id == "antigravity" && main is not null)
        {
            var name = main.Group ?? main.Label;
            freshness = name.Length > 35 ? name[..32] + "..." : name;
        }
        using (var brush = new SolidBrush(stale ? Theme.Amber : Theme.Label))
        using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(freshness, Theme.Font("mono", 12.5f), brush, new RectangleF(x, r.Y + 194, right - x - (paceDelta is null ? 0 : 88), 18), format);
        PaintResetBox(g, new RectangleF(r.X + 16, r.Y + 230, r.Width - 32, 50), main, noData, mainExpired, stale, look, now);
    }
}
