using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Windows.Forms;

namespace AIUsageMonitor;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        AppPaths.Configure(args);
        if (args.Length == 2 && args[0] == "--selftest-integrations")
        {
            try { IntegrationChecks.Run(args[1]); return 0; }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                File.WriteAllText(args[1], "FAIL: " + ex);
                return 1;
            }
        }
        // Runs one provider's browser login without the tray (the running instance picks up the saved login on its next refresh).
        if (args.Length == 1 && args[0] == "--connect-magnific")
        {
            using var magnific = new MagnificAgent();
            magnific.ConnectAsync().GetAwaiter().GetResult();
            var quota = magnific.GetStatus(DateTimeOffset.Now).Quota;
            Console.WriteLine(quota.Windows.Count > 0 ? "Magnific conectado: " + quota.Windows[0].ResetHint : "Magnific não conectado: " + quota.Error);
            return quota.Windows.Count > 0 ? 0 : 1;
        }
        // Lists a remote MCP server's tools with the saved login (no tool is called, no browser is opened).
        if (args.Length == 2 && args[0] == "--list-mcp-tools" && args[1] is "openrouter" or "magnific")
        {
            var (endpoint, cache) = args[1] == "openrouter"
                ? (new Uri("https://mcp.openrouter.ai/mcp"), new OpenRouterTokenCache())
                : (new Uri("https://mcp.magnific.com"), new OpenRouterTokenCache("magnific-mcp-token.dat", "QuotaDesk.Magnific.MCP.v1"));
            try
            {
                var tools = RemoteMcpTool.ListToolsAsync(endpoint, args[1], cache, CancellationToken.None).GetAwaiter().GetResult();
                foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
                {
                    var p = tool.ProtocolTool;
                    var readOnly = p.Annotations?.ReadOnlyHint is { } ro ? (ro ? "somente leitura" : "ESCREVE") : "sem indicação";
                    var inputs = p.InputSchema.TryGetProperty("properties", out var props) && props.ValueKind == System.Text.Json.JsonValueKind.Object
                        ? string.Join(", ", props.EnumerateObject().Select(x => x.Name)) : "";
                    Console.WriteLine($"## {p.Name}  [{readOnly}]");
                    Console.WriteLine($"   parâmetros: {(inputs.Length == 0 ? "(nenhum)" : inputs)}");
                    Console.WriteLine($"   {(p.Description ?? "").Replace("\n", " ")}");
                }
                return 0;
            }
            catch (OpenRouterAuthorizationRequiredException)
            {
                Console.WriteLine("Sem login salvo válido; conecte pelo menu da bandeja primeiro.");
                return 2;
            }
        }
        if (args.Length == 1 && args[0] == "--review-mini-office")
        {
            ApplicationConfiguration.Initialize();
            return MiniOfficePreview.Open();
        }
        if (args.Length == 2 && args[0] == "--preview-mini-office")
        {
            ApplicationConfiguration.Initialize();
            return MiniOfficePreview.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--selftest-providers")
        {
            try { ProviderAvailabilityChecks.Run(args[1]); return 0; }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                File.WriteAllText(args[1], "FAIL: " + ex);
                return 1;
            }
        }
        if (args.Length == 2 && args[0] == "--selftest-office")
        {
            ApplicationConfiguration.Initialize();
            try { SharedOfficeChecks.Run(args[1]); return 0; }
            catch (Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "shared-office-checks.txt"), "FAIL: " + ex); return 1; }
        }
        if (args.Length == 2 && args[0] == "--selftest-office-navigation")
        {
            ApplicationConfiguration.Initialize();
            return OfficeNavigationChecks.Run(args[1]);
        }
        if (args.Length == 1 && args[0] == "--review-loft")
        {
            ApplicationConfiguration.Initialize();
            return LoftReview.Run();
        }
        if (args.Length == 1 && args[0] == "--visual-preview")
        {
            ApplicationConfiguration.Initialize();
            return LoftReview.Run();
        }
        // Developer utilities (no tray, no single-instance lock):
        //   --export-icon <file.ico>   writes the application icon
        //   --preview <folder>         renders every visual state with SIMULATED data (watermarked) to PNG
        //   --snapshot <file.png>      renders the panel with the REAL local data to PNG
        if (args.Length == 2 && args[0] == "--selftest-stability")
        {
            ApplicationConfiguration.Initialize();
            return StabilityChecks.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--selftest-ui")
        {
            ApplicationConfiguration.Initialize();
            return UiChecks.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--settings-preview")
        {
            ApplicationConfiguration.Initialize();
            return SettingsForm.RenderPreviews(args[1]);
        }
        if (args.Length == 2 && args[0] == "--export-icon")
        {
            TrayIconRenderer.WriteIco(args[1], TrayIconRenderer.Logo);
            using var tray = TrayIconRenderer.Render(16, TrayIconRenderer.Logo);
            tray.Save(Path.ChangeExtension(args[1], ".16.png"), ImageFormat.Png);
            using var big = TrayIconRenderer.Render(256, TrayIconRenderer.Logo);
            big.Save(Path.ChangeExtension(args[1], ".256.png"), ImageFormat.Png);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--preview")
        {
            ApplicationConfiguration.Initialize();
            Preview.RenderAll(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--mini")
        {
            ApplicationConfiguration.Initialize();
            MiniScreenForm.RenderPreviews(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--selftest-renewal")
        {
            File.WriteAllText(args[1], Preview.RenewalSelfTest());
            return 0;
        }
        if (args.Length == 2 && args[0] == "--selftest-claude")
        {
            File.WriteAllText(args[1], ClaudeQuotaChecks.Run());
            return 0;
        }        if (args.Length == 2 && args[0] == "--selftest-antigravity")
        {
            File.WriteAllText(args[1], AntigravityChecks.Run());
            return 0;
        }
        if (args.Length == 1 && args[0] == "--notify-test")
        {
            ApplicationConfiguration.Initialize();
            Preview.NotificationTest();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--snapshot")
        {
            ApplicationConfiguration.Initialize();
            return Preview.RenderReal(args[1]).GetAwaiter().GetResult();
        }

        // Started from inside another app's process group (e.g. a terminal in Claude, Codex or an IDE)? Then that app
        // closing or updating would take QuotaDesk down with it. Relaunch outside the group when Windows allows it.
        if (!args.Contains("--detached") && Detach.TryRelaunchOutsideJob(args)) return 0;

        // --test-instance: a review build can run next to the installed one (own lock, show signal and data folder;
        // the hook port stays with the installed instance, so the test one reports Claude's state as unavailable).
        // Each named environment (QUOTADESK_ENV: dev, homolog) gets its own lock, so dev and staging can run together.
        var test = args.Contains("--test-instance") || AppPaths.Isolated;
        var suffix = !test ? "" : AppPaths.EnvironmentName is { } env ? ".Test." + env : ".Test";
        using var mutex = new Mutex(true, "Local\\AIUsageMonitor.SingleInstance" + suffix, out var created);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, MonitorContext.ShowSignalName + suffix);
        using var launcherSignal = new EventWaitHandle(false, EventResetMode.AutoReset, MonitorContext.ShowLauncherSignalName + suffix);
        if (!created)
        {
            // A second launch routes to the existing process and preserves the requested destination.
            AllowSetForegroundWindow(-1);
            if (args.Contains("--launcher")) launcherSignal.Set();
            else showSignal.Set();
            return 0;
        }

        // Unexpected errors are written to a local log instead of silently killing the tray app.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorLog.Write("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write("fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write("task", e.Exception); e.SetObserved(); };
        // If Windows closes the app for an update or restart, ask it to start QuotaDesk again afterwards.
        if (!test) _ = RegisterApplicationRestart("--restarted", 0);

        ApplicationConfiguration.Initialize();
        var context = new MonitorContext(showSignal, launcherSignal, args.Contains("--open"), args.Contains("--launcher"), args.Contains("--voxel"));
        Application.Run(context);
        if (context.RestartRequested)
        {
            // Release the single-instance mutex before the replacement process claims it.
            launcherSignal.Dispose();
            showSignal.Dispose();
            mutex.ReleaseMutex();
            mutex.Dispose();
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, test ? "--launcher --test-instance" : "--launcher")
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                ErrorLog.Write("restart", ex);
            }
        }
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, int flags);
}

/// <summary>Relaunches the app with CREATE_BREAKAWAY_FROM_JOB so it does not live inside its launcher's job object.</summary>
internal static partial class Detach
{
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CreateProcessW(string? app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string? dir, ref StartupInfo si, out ProcessInformation pi);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    public static bool TryRelaunchOutsideJob(string[] args)
    {
        try
        {
            if (!IsProcessInJob(System.Diagnostics.Process.GetCurrentProcess().Handle, IntPtr.Zero, out var inJob) || !inJob) return false;
            var exe = Environment.ProcessPath;
            if (exe is null) return false;
            var cmd = new System.Text.StringBuilder($"\"{exe}\" {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))} --detached");
            var si = new StartupInfo { cb = System.Runtime.InteropServices.Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, CreateBreakawayFromJob | CreateUnicodeEnvironment,
                    IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out var pi))
                return false; // the job does not allow breakaway: just run where we are
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Appends exception type, message and stack to error.log in the data folder (no data, no secrets).</summary>
internal static partial class ErrorLog
{
    public static void Write(string kind, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppPaths.DataDirectory, "error.log");
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {Redact(ex?.ToString() ?? "")}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    /// <summary>
    /// The log is what users attach to bug reports: an exception from an HTTP or OAuth library can carry a URL or body
    /// with a token, code or key, and paths carry the Windows user name. Mask both before anything reaches the disk.
    /// </summary>
    internal static string Redact(string text)
    {
        text = SecretValue().Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + "[redigido]");
        text = BearerValue().Replace(text, "Bearer [redigido]");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 3 ? text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"(?<key>[""']?(?:access_token|refresh_token|id_token|client_secret|code_verifier|code|state|token|api[_-]?key|apikey|password|secret|csrf[_-]?token|x-codeium-csrf-token)[""']?)(?<sep>\s*[:=]\s*[""']?)[^\s""'&,;}]+",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SecretValue();

    [System.Text.RegularExpressions.GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BearerValue();
}

/// <summary>Offscreen rendering used to verify the layout. Simulated data is always watermarked.</summary>
internal static class Preview
{
    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.Now;
        LimitWindow W(WindowKind k, double used, double hours, bool est = false) =>
            new(k == WindowKind.FiveHour ? "5 horas" : "Semanal", used, now.AddHours(hours), k, est, est ? "estimativa ±8 min" : null);
        ProviderSnapshot Codex(bool five) => new("Codex",
            five ? [W(WindowKind.FiveHour, 34, 2.3), W(WindowKind.Weekly, 77, 90)] : [W(WindowKind.Weekly, 77, 90)],
            now.AddMinutes(-1), null, "codex app-server · account/rateLimits/read",
            Note: five ? null : "O Codex não informa janela de 5 horas para esta conta.");
        var claudeOk = new ProviderSnapshot("Claude Code", [W(WindowKind.FiveHour, 21, 3.8, true), W(WindowKind.Weekly, 5, 150, true)],
            now.AddMinutes(-6), null, "App Claude · histórico local de uso do plano");
        var claudeHot = claudeOk with { Windows = [W(WindowKind.FiveHour, 93, 0.6, true), W(WindowKind.Weekly, 81, 20, true)] };
        var claudeStale = claudeOk with { UpdatedAt = now.AddHours(-3), Stale = true, Note = "Dados desatualizados: última amostra há 3h 00min (o app Claude registra enquanto está aberto)." };
        var claudeMissing = ProviderSnapshot.Unavailable("Claude Code", "Sem dados de cota. Abra o app Claude (desktop) ou use o Claude Code no terminal.");
        var codexMissing = ProviderSnapshot.Unavailable("Codex", "Codex CLI não encontrado.", "codex app-server · account/rateLimits/read");

        AgentStatus Cx(ActivityState s, ProviderSnapshot q) => new("codex", "ChatGPT Codex", s, true,
            s == ActivityState.Working ? "executando tarefa" : s == ActivityState.Waiting ? "tarefa concluída · aguardando você" : "nenhuma tarefa recente",
            "inferido do log local · há 12 s", q);
        AgentStatus Cl(ActivityState s, ProviderSnapshot q) => new("claude", "Claude Code", s, false,
            s == ActivityState.Working ? "usando Bash" : s == ActivityState.Waiting ? "resposta concluída · aguardando você" : "nenhuma sessão ativa",
            "hooks do Claude Code · último evento há 4 s", q);

        var cases = new (string Name, AgentStatus[] Agents)[]
        {
            ("1-working-both", [Cx(ActivityState.Working, Codex(false)), Cl(ActivityState.Working, claudeOk)]),
            ("2-waiting-idle", [Cx(ActivityState.Idle, Codex(true) with { Windows = [W(WindowKind.FiveHour, 100, 2.8), W(WindowKind.Weekly, 42, 98)] }),
                Cl(ActivityState.Waiting, claudeHot) with
                {
                    SessionOpen = true,
                    Pending = [new("a", "Claude Code", "quotadesk", "Permissão", now.AddMinutes(-3)), new("b", "Claude Code", "meu-site", "Pergunta", now.AddMinutes(-1))]
                }]),
            ("3-idle-stale", [Cx(ActivityState.Idle, Codex(false)), Cl(ActivityState.Waiting, claudeStale)]),
            ("4-missing", [Cx(ActivityState.Unknown, codexMissing), Cl(ActivityState.Idle, claudeMissing)]),
        };
        AgentStatus Ag(ActivityState state) => new("antigravity", "Antigravity", state, false, "Estado de teste", "Simulação visual",
            new ProviderSnapshot("Antigravity", [new LimitWindow("Gemini 3.1 Pro (High)", 24, now.AddHours(3))], now));
        for (var i = 0; i < cases.Length; i++) cases[i].Agents = [..cases[i].Agents, Ag(i == 3 ? ActivityState.Unknown : cases[i].Agents[1].State)];
        var settings = new AppSettings { City = "Cidade de exemplo", Latitude = -23.55, Longitude = -46.63 };
        (int Hour, int Minute, Weather Weather)[] skies = [(14, 30, Weather.Clear), (18, 5, Weather.PartlyCloudy), (22, 0, Weather.Clear), (12, 0, Weather.Rain)];
        using var form = new MonitorForm();
        for (var c = 0; c < cases.Length; c++)
        {
            var (name, agents) = cases[c];
            var sky = skies[c % skies.Length];
            form.SkyProvider = _ => SkyService.Compute(DateTime.Today.AddHours(sky.Hour).AddMinutes(sky.Minute), settings, null, sky.Weather);
            form.ShowData(agents);
            for (var frame = 0; frame < 2; frame++)
            {
                using var bmp = form.Snapshot(frame == 0 ? 3 : 51);
                Watermark(bmp, "PRÉ-VISUALIZAÇÃO · DADOS SIMULADOS");
                bmp.Save(Path.Combine(folder, $"{name}-f{frame}.png"), ImageFormat.Png);
            }
        }
        // Compact mode (cards only), plus the refresh animation and the pending badge
        form.SkyProvider = _ => SkyService.Compute(DateTime.Today.AddHours(14), settings, null, Weather.Clear);
        form.Settings.Compact = true;
        foreach (var c in new[] { 0, 1, 3 })
        {
            form.ShowData(cases[c].Agents);
            using var bmp = form.Snapshot(3);
            Watermark(bmp, "PRÉ-VISUALIZAÇÃO · DADOS SIMULADOS");
            bmp.Save(Path.Combine(folder, $"compact-{cases[c].Name}.png"), ImageFormat.Png);
        }
        form.Settings.Compact = false;
        // The window at different times of day and weather (same agents)
        (int Hour, int Minute, Weather Weather, string Tag)[] timeline =
        [
            (5, 50, Weather.Clear, "05h50-amanhecer"), (9, 0, Weather.PartlyCloudy, "09h00-parcial"), (13, 0, Weather.Clear, "13h00-sol"),
            (17, 55, Weather.Clear, "17h55-por-do-sol"), (18, 35, Weather.Clear, "18h35-crepusculo"), (23, 0, Weather.Clear, "23h00-noite"),
            (15, 0, Weather.Rain, "15h00-chuva"), (21, 0, Weather.Storm, "21h00-tempestade"), (7, 30, Weather.Fog, "07h30-neblina"),
            (11, 0, Weather.Cloudy, "11h00-nublado")
        ];
        foreach (var (hour, minute, weather, tag) in timeline)
        {
            form.SkyProvider = _ => SkyService.Compute(DateTime.Today.AddHours(hour).AddMinutes(minute), settings, null, weather);
            form.ShowData(cases[0].Agents);
            using var bmp = form.Snapshot(40);
            Watermark(bmp, "PRÉ-VISUALIZAÇÃO · DADOS SIMULADOS");
            bmp.Save(Path.Combine(folder, $"sky-{tag}.png"), ImageFormat.Png);
        }
        // Idle activities (sleep / read / game) for both desks
        form.SkyProvider = _ => SkyService.Compute(DateTime.Today.AddHours(21), settings, null, Weather.Clear);
        for (var cycle = 0; cycle < 5; cycle++)
        {
            PixelOffice.ForcedIdleActivity = cycle;
            ClassicOffice.ForcedIdlePose = new[] { "coffee", "sip", "sleep", "stretch", "coffee" }[cycle];
            form.ShowData([Cx(ActivityState.Idle, Codex(false)), Cl(ActivityState.Idle, claudeOk), Ag(ActivityState.Idle)]);
            using var bmp = form.Snapshot(5);
            Watermark(bmp, "PRÉ-VISUALIZAÇÃO · DADOS SIMULADOS");
            bmp.Save(Path.Combine(folder, $"idle-{cycle}.png"), ImageFormat.Png);
        }
        PixelOffice.ForcedIdleActivity = null;
        ClassicOffice.ForcedIdlePose = null;
        foreach (var catFrame in new[] { 500, 560, 610 })
        {
            using var catPreview = form.Snapshot(catFrame);
            Watermark(catPreview, "PRÉ-VISUALIZAÇÃO · DADOS SIMULADOS");
            catPreview.Save(Path.Combine(folder, $"cat-sleep-{catFrame}.png"), ImageFormat.Png);
        }
        // The edge widget (notch), open with a tool hovered and collapsed, on a transparent background.
        using (var notch = new NotchForm())
        {
            notch.ShowData(cases[1].Agents);
            notch.Place(Screen.PrimaryScreen!, WidgetEdge.Right);
            notch.Preview(wide: true, hover: 1);
            using (var open = notch.Snapshot()) open.Save(Path.Combine(folder, "notch-open.png"), ImageFormat.Png);
            notch.Preview(wide: false, hover: -1);
            using (var slim = notch.Snapshot()) slim.Save(Path.Combine(folder, "notch-slim.png"), ImageFormat.Png);
        }
        for (var size = 16; size <= 32; size += 8)
        {
            using var icon = TrayIconRenderer.Render(size, [new(Theme.Blue, 23, false), new(Theme.Orange, 79, true)]);
            using var zoom = new Bitmap(size * 8, size * 8);
            using (var g = Graphics.FromImage(zoom))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(icon, 0, 0, size * 8, size * 8);
            }
            zoom.Save(Path.Combine(folder, $"tray-{size}px-x8.png"), ImageFormat.Png);
        }
    }

    public static async Task<int> RenderReal(string file)
    {
        var sources = ProviderModuleCatalog.CreateSources();
        try
        {
            await Task.WhenAll(sources.Select(s => s.RefreshQuotaAsync(true)));
            using var sky = new SkyService(AppSettings.Load());
            await Task.Delay(12000); // let the session-log scan and the first weather reading finish
            var agents = MonitorContext.Collect(sources);
            using var form = new MonitorForm { SkyProvider = sky.Current };
            form.ShowData(agents);
            using var bmp = form.Snapshot(0);
            bmp.Save(file, ImageFormat.Png);
            // The office shows three seats at a time; save the last three as well so every provider is visible.
            while (form.NavigateOffice(1)) { }
            using (var last = form.Snapshot(0)) last.Save(Path.ChangeExtension(file, null) + "-2.png", ImageFormat.Png);
            // The same readings in the classic template (original office and cards).
            while (form.NavigateOffice(-1)) { }
            form.Settings.QuotaTemplate = "classic";
            form.ShowData(agents);
            using (var classic = form.Snapshot(0)) classic.Save(Path.ChangeExtension(file, null) + "-classic.png", ImageFormat.Png);
            form.Settings.Compact = true;
            using (var compact = form.Snapshot(0)) compact.Save(Path.ChangeExtension(file, null) + "-classic-compact.png", ImageFormat.Png);
            var report = string.Join(Environment.NewLine, agents.Select(a =>
                $"{a.Name}: state={a.State} est={a.StateEstimated} detail='{a.StateDetail}' source='{a.StateSource}' | quota source='{a.Quota.Source}' " +
                $"updated={a.Quota.UpdatedAt:O} stale={a.Quota.Stale} error='{a.Quota.Error}' note='{a.Quota.Note}' | " +
                string.Join("; ", a.Quota.Windows.Select(w => $"{w.Label}[{w.Kind}] used={w.UsedPercent} resets={w.ResetsAt:O} est={w.ResetEstimated} hint='{w.ResetHint}'"))));
            File.WriteAllText(Path.ChangeExtension(file, ".txt"), report + Environment.NewLine + "Céu: " + sky.Current(DateTime.Now).Describe().Replace('\n', '|') + (sky.LastError is { } err ? " | erro: " + err : ""));
            return 0;
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
    }

    /// <summary>Feeds scripted readings into RenewalWatcher and reports what it would notify.</summary>
    public static string RenewalSelfTest()
    {
        var t0 = DateTimeOffset.Now;
        var watcher = new RenewalWatcher();
        var log = new List<string>();
        AgentStatus Agent(string id, string name, params LimitWindow[] windows) =>
            new(id, name, ActivityState.Idle, false, "", "", new ProviderSnapshot(name, windows, t0));
        LimitWindow Win(WindowKind k, double used, DateTimeOffset? reset, bool est) => new(k.ToString(), used, reset, k, est);
        void Step(string label, DateTimeOffset now, params AgentStatus[] agents)
        {
            var found = watcher.Check(agents, now);
            log.Add($"{label}: {(found.Count == 0 ? "nenhum aviso" : string.Join(" | ", found.Select(f => $"AVISO {f.AgentName} {f.Kind} exato={f.AtExactTime}")))}");
        }
        var codexReset = t0.AddMinutes(1);
        Step("1 primeira leitura (Codex semanal 77%, Claude 5h 28% est.)", t0,
            Agent("codex", "Codex", Win(WindowKind.Weekly, 77, codexReset, false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 28, t0.AddHours(3), true), Win(WindowKind.Weekly, 6, t0.AddDays(6), true)));
        Step("2 uso sobe (Claude 5h 35%, semanal 7%)", t0.AddSeconds(30),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 77, codexReset, false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 35, t0.AddHours(3), true), Win(WindowKind.Weekly, 7, t0.AddDays(6), true)));
        Step("3 horário exato do Codex passou", t0.AddMinutes(2),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 77, codexReset, false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 35, t0.AddHours(3), true), Win(WindowKind.Weekly, 7, t0.AddDays(6), true)));
        Step("4 nova leitura do Codex (0%, nova janela) — sem duplicar", t0.AddMinutes(3),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 0, t0.AddDays(7), false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 35, t0.AddHours(3), true), Win(WindowKind.Weekly, 7, t0.AddDays(6), true)));
        Step("5 Claude 5h cai de 35% para 0% (renovou)", t0.AddHours(3),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 1, t0.AddDays(7), false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 0, null, true), Win(WindowKind.Weekly, 7, t0.AddDays(6), true)));
        Step("6 Claude 5h volta a subir (4%) — sem aviso", t0.AddHours(3.5),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 1, t0.AddDays(7), false)),
            Agent("claude", "Claude Code", Win(WindowKind.FiveHour, 4, t0.AddHours(8), true), Win(WindowKind.Weekly, 8, t0.AddDays(6), true)));
        Step("7 sem dados do Claude (leitura ausente) — sem aviso", t0.AddHours(4),
            Agent("codex", "Codex", Win(WindowKind.Weekly, 1, t0.AddDays(7), false)),
            Agent("claude", "Claude Code"));
        var (title, text) = RenewalWatcher.Describe([new RenewalWatcher.Renewal("codex", "Codex", WindowKind.Weekly, true, codexReset)]);
        log.Add($"Texto do aviso: {title} — {text}");
        return string.Join(Environment.NewLine, log);
    }

    /// <summary>Shows one real Windows notification from a temporary tray icon, then exits.</summary>
    public static void NotificationTest()
    {
        using var icon = new NotifyIcon { Icon = TrayIconRenderer.CreateIcon(16, TrayIconRenderer.Logo), Text = "QuotaDesk (teste)", Visible = true };
        var (title, text) = RenewalWatcher.Describe([new RenewalWatcher.Renewal("codex", "Codex", WindowKind.Weekly, true, DateTimeOffset.Now)]);
        icon.ShowBalloonTip(10_000, title + " (teste)", text, ToolTipIcon.Info);
        using var timer = new System.Windows.Forms.Timer { Interval = 12_000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
        icon.Visible = false;
    }

    private static void Watermark(Bitmap bmp, string text)
    {
        using var g = Graphics.FromImage(bmp);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(230, 255, 190, 70));
        var size = g.MeasureString(text, font);
        using var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillRectangle(bg, bmp.Width - size.Width - 18, 4, size.Width + 8, size.Height);
        g.DrawString(text, font, brush, bmp.Width - size.Width - 14, 4);
    }
}
