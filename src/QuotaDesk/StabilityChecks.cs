using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIUsageMonitor;

/// <summary>Regression fixtures are isolated from the user's settings and provider services.</summary>
internal static class StabilityChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new List<string>();
        void Check(bool condition, string label) => results.Add((condition ? "OK: " : "FAIL: ") + label);
        void Case(string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { results.Add($"FAIL: {name}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        }
        var now = DateTimeOffset.Now;
        var agents = new[] { "codex", "claude", "antigravity" }.Select(id => new AgentStatus(id, id,
            ActivityState.Working, false, "simulado", "teste", new ProviderSnapshot(id, [], now))).ToArray();

        Case("log sem segredos", () =>
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var raw = "GET https://x.test/cb?code=abc123&state=xyz789 failed; {\"access_token\":\"tok-1\",\"refresh_token\": \"tok-2\"} " +
                "Authorization: Bearer eyJhbGciOi.payload.sig X-Codeium-Csrf-Token: csrf-9 at " + Path.Combine(profile, "src", "A.cs");
            var clean = ErrorLog.Redact(raw);
            Check(!new[] { "abc123", "xyz789", "tok-1", "tok-2", "eyJhbGciOi", "csrf-9", profile }.Any(s => clean.Contains(s, StringComparison.OrdinalIgnoreCase)),
                "o error.log mascara tokens, códigos OAuth, CSRF e a pasta do usuário");
        });

        Case("preferências", () =>
        {
            var save = typeof(AppSettings).GetMethod("SaveTo", Private | BindingFlags.Public);
            var load = typeof(AppSettings).GetMethod("LoadFrom", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Check(save is not null && load is not null, "persistência pode ser verificada em diretório isolado");
            if (save is null || load is null) return;
            var fixture = Path.Combine(Path.GetFullPath(folder), "settings-fixture-" + Guid.NewGuid().ToString("N"), "settings.json");
            bool Save(AppSettings settings) => (bool)save.Invoke(settings, [fixture])!;
            AppSettings Load() => (AppSettings)load.Invoke(null, [fixture])!;
            Check(Save(new AppSettings { WidgetPinned = true, Scale = 1.2f }), "primeira gravação é concluída");
            Check(Load() is { WidgetPinned: true, Scale: 1.2f }, "preferências gravadas são preservadas");
            Check(Save(new AppSettings { WidgetPinned = false, Scale = 0.9f }), "substituição de preferências é concluída");
            var backup = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(fixture + ".bak"));
            Check(backup is { WidgetPinned: true, Scale: 1.2f }, "backup contém o estado anterior válido");
            File.WriteAllText(fixture + ".interrupted.tmp", "{");
            Check(Load() is { WidgetPinned: false, Scale: 0.9f }, "temporário incompleto não altera a leitura principal");
            File.WriteAllText(fixture, "{");
            Check(Load() is { WidgetPinned: true, Scale: 1.2f }, "JSON principal inválido recupera o backup");
            Check(Save(new AppSettings { Scale = 1.1f }), "gravação após recuperação é concluída");
            backup = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(fixture + ".bak"));
            Check(backup is { WidgetPinned: true, Scale: 1.2f }, "principal corrompido não substitui o backup válido");
            var before = File.ReadAllText(fixture);
            using (var held = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.None))
                Check(!Save(new AppSettings { Scale = 1.5f }), "arquivo bloqueado informa falha de gravação");
            Check(File.ReadAllText(fixture) == before, "falha de gravação preserva o arquivo anterior");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(fixture)!, "*.tmp").Any(p => !p.EndsWith(".interrupted.tmp")),
                "temporários da gravação falha são removidos");
        });

        Case("captura", () =>
        {
            using var dock = new DockForm();
            _ = dock.Handle;
            dock.ShowData(agents);
            Call(dock, "SetWide", true);
            Field(dock, "_mouseDown", true);
            Field(dock, "_dragging", true);
            Field(dock, "_pinPressed", true);
            // Exercise the same cleanup event raised by Windows when capture is stolen/cancelled.
            Call(dock, "OnMouseCaptureChanged", EventArgs.Empty);
            Check(!Field<bool>(dock, "_dragging") && !Field<bool>(dock, "_mouseDown") && !Field<bool>(dock, "_pinPressed"),
                "perda de captura limpa arraste e botão pressionado da barra");
            Call(dock, "SetWide", false);
            Check(!Field<bool>(dock, "_wide"), "barra pode recolher após captura interrompida");
        });

        Case("DPI", () =>
        {
            using var panel = new MonitorForm { Opacity = 0, TopMost = true,
                Settings = new AppSettings { WindowX = 20, WindowY = 30 } };
            panel.ShowData(agents);
            panel.Show();
            panel.Location = new Point(100, 100);
            Field(panel, "_movingWindow", true);
            Field(panel, "_userMoved", true);
            var rect = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(rect, 0, 110); Marshal.WriteInt32(rect, 4, 120);
                Marshal.WriteInt32(rect, 8, 1176); Marshal.WriteInt32(rect, 12, 800);
                SendMessage(panel.Handle, 0x02E0, (nint)(144 | (144 << 16)), rect);
                Check(panel.Location != new Point(20, 30), "WM_DPICHANGED durante arraste não restaura coordenadas antigas");
                Check(Math.Abs(Field<float>(panel, "_scale") - 1.2f) < 0.001f, "mudança de DPI atualiza a escala de desenho");
            }
            finally { Marshal.FreeHGlobal(rect); }
            panel.Hide();
        });

        Case("texto", () =>
        {
            using var panel = new MonitorForm();
            panel.ShowData(agents);
            var scaleFor = typeof(MonitorForm).GetMethod("NameScaleFor", Private);
            Check(scaleFor is not null, "escala do nome é calculada para o card correspondente");
            if (scaleFor is null) return;
            var before = (float)scaleFor.Invoke(panel, [0])!;
            panel.Settings.ProviderDensityById["claude"] = ProviderDensity.IconOnly;
            panel.ShowData(agents);
            var after = (float)scaleFor.Invoke(panel, [0])!;
            Check(after >= before && after > 2f, "card minimizado não reduz a fonte dos cards completos");
            using var image = panel.Snapshot(3);
            image.Save(Path.Combine(folder, "names-mixed-density.png"));
        });
        Case("tela USB serial", () =>
        {
            var hello = UsbScreenProtocol.HelloPacket();
            Check(hello.Length == 6 && hello.All(b => b == 0x45), "handshake são seis bytes 0x45");
            Check(UsbScreenProtocol.BitmapCommand(0, 0, 320, 480).SequenceEqual(new byte[] { 0, 0, 4, 253, 223, 197 }),
                "comando de bitmap em tela cheia empacota os campos de 10 bits");
            var packed = UsbScreenProtocol.Command(1, 1023, 700, 513, 6);
            Check(((packed[0] << 2) | (packed[1] >> 6)) == 1023 && (((packed[1] & 63) << 4) | (packed[2] >> 4)) == 700 &&
                (((packed[2] & 15) << 6) | (packed[3] >> 2)) == 513 && (((packed[3] & 3) << 8) | packed[4]) == 6 && packed[5] == 1,
                "os quatro campos de 10 bits voltam iguais ao desempacotar");
            using (var pixels = new Bitmap(4, 1))
            {
                pixels.SetPixel(0, 0, Color.FromArgb(255, 0, 0));
                pixels.SetPixel(1, 0, Color.FromArgb(0, 255, 0));
                pixels.SetPixel(2, 0, Color.FromArgb(0, 0, 255));
                pixels.SetPixel(3, 0, Color.White);
                Check(UsbScreenProtocol.ToRgb565(pixels).SequenceEqual(new byte[] { 0x00, 0xF8, 0xE0, 0x07, 0x1F, 0x00, 0xFF, 0xFF }),
                    "RGB565 little-endian: vermelho, verde, azul e branco");
            }
            using (var full = new Bitmap(UsbScreenProtocol.Width, UsbScreenProtocol.Height))
                Check(UsbScreenProtocol.ToRgb565(full).Length == 320 * 480 * 2, "quadro cheio tem 2 bytes por pixel");
            Check(307200 % UsbScreenProtocol.ChunkBytes == 0, "o quadro cheio é dividido em blocos inteiros");
            UsbSerialScreen.FindPortName();
            Check(true, "procurar a porta sem o aparelho não lança exceção");
            using var serial = new SerialMiniScreen();
            serial.ShowData(agents);
            var frame = (byte[])Call(serial, "Compose", 0)!;
            Check(frame.Length == 320 * 480 * 2 && frame.Any(b => b != 0), "o quadro 320×480 é desenhado com conteúdo");
            Check(!serial.Running && !serial.Connected, "sem iniciar, a tela serial não abre nenhuma porta");
        });
        File.WriteAllLines(Path.Combine(folder, "stability-checks.txt"), results);
        return results.Any(r => r.StartsWith("FAIL:")) ? 1 : 0;
    }
}
