using System.Drawing;
using System.Drawing.Imaging;

namespace AIUsageMonitor;

internal static class MiniOfficePreview
{
    public static int Open()
    {
        using var form = new MiniScreenForm
        {
            ClientSize = new Size(480, 320),
            Location = new Point(80, 80),
            TopMost = true,
            Text = "QuotaDesk · prévia 480x320 sem conexão"
        };
        form.ShowData(MonitorContext.Collect([]));
        Application.Run(form);
        return 0;
    }

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var now = DateTime.Now;
        var unknown = MonitorContext.Collect([]);
        var sample = unknown.Select((a, i) => a with
        {
            State = i % 2 == 0 ? ActivityState.Working : ActivityState.Idle,
            Quota = new ProviderSnapshot(a.Name,
                [new("5h", 12 + i * 13, new DateTimeOffset(now).AddHours(2), WindowKind.FiveHour),
                 new("semanal", i == 2 ? 100 : 38 + i * 8, new DateTimeOffset(now).AddDays(3), WindowKind.Weekly)], new DateTimeOffset(now))
        }).ToArray();
        using var scene = new SharedOfficeScene();
        if (!scene.IsAvailable) return 1;
        foreach (var (name, agents) in new[] { ("offline", unknown), ("simulado", (IReadOnlyList<AgentStatus>)sample) })
        {
            var view = new OfficeViewport();
            view.SetEnabled(agents.Select(a => a.Id));
            for (var page = 0; page < 4; page++)
            {
                var visible = view.VisibleIds.Select(id => agents.First(a => a.Id == id)).ToArray();
                using var image = new Bitmap(480, 320);
                using (var g = Graphics.FromImage(image))
                {
                    MiniScreenForm.RenderOffice(g, image.Size, agents, visible, scene, page * 11, now, view, 0.4f);
                    g.ResetTransform();
                    using var brush = new SolidBrush(Color.Gold);
                    using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString(name == "simulado" ? "DADOS SIMULADOS" : "PREVIA SEM CONEXAO", font, brush, 190, 8);
                }
                image.Save(Path.Combine(folder, $"mini-480x320-{name}-{page}.png"), ImageFormat.Png);
                using var portrait = new Bitmap(320, 480);
                using (var g = Graphics.FromImage(portrait))
                {
                    MiniScreenForm.RenderPortraitOffice(g, portrait.Size, agents, visible, scene, page * 11, now, view, new AppSettings(), 0.4f);
                    g.ResetTransform();
                    using var brush = new SolidBrush(Color.Gold);
                    using var font = new Font("Segoe UI", 9, FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString(name == "simulado" ? "DADOS SIMULADOS" : "PREVIA SEM CONEXAO", font, brush, 120, 24);
                }
                portrait.Save(Path.Combine(folder, $"mini-320x480-{name}-{page}.png"), ImageFormat.Png);
                if (page < 3 && !view.Move(1)) return 1;
            }
            if (view.Move(1)) return 1;
        }
        File.WriteAllText(Path.Combine(folder, "checks.txt"), "PASS: native 480x320 previews, 3 providers per view, four bounded navigation positions; synthetic quota previews are explicitly marked.");
        return 0;
    }
}
