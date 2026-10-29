using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace AIUsageMonitor;

/// <summary>Offline renderer checks and reproducible visual artifacts for the shared office.</summary>
internal static class SharedOfficeChecks
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new List<string>();
        using var scene = new SharedOfficeScene();
        Require(scene.IsAvailable, "embedded backdrop, manifest and all independent poses load");
        Require(SharedOfficeScene.ProviderOrder.SequenceEqual(new[] { "codex", "claude", "antigravity", "magnific", "figma", "openrouter" }),
            "provider order follows six fixed physical seats");

        var all = CreateStatuses(ActivityState.Working);
        var unknown = CreateStatuses(ActivityState.Unknown);
        var viewport = scene.GetViewport(SharedOfficeScene.ProviderOrder.Take(3).ToArray());
        Require(Math.Abs(viewport.Width - 1089) < 1 && viewport.Height == 224, "three rooms fill the short panoramic view");
        Require(Math.Abs(scene.GetViewport(["codex", "magnific"]).Width - 726) < 1, "hidden providers' rooms collapse: two providers, two rooms");

        var individual = new List<Bitmap>();
        foreach (var id in SharedOfficeScene.ProviderOrder)
        {
            var singleViewport = scene.GetViewport([id]);
            using var work = Render(scene, all.Where(a => a.Id == id).ToArray(), singleViewport, 17);
            work.Save(Path.Combine(folder, $"office-{id}.png"), ImageFormat.Png);
            individual.Add((Bitmap)work.Clone());

            using var signalA = Render(scene, unknown.Where(a => a.Id == id).ToArray(), singleViewport, 1);
            using var signalB = Render(scene, unknown.Where(a => a.Id == id).ToArray(), singleViewport, 12);
            Require(DifferentPixels(signalA, signalB), $"{id} no-signal frames visibly alternate");
            signalA.Save(Path.Combine(folder, $"no-signal-{id}-a.png"), ImageFormat.Png);
            signalB.Save(Path.Combine(folder, $"no-signal-{id}-b.png"), ImageFormat.Png);
        }

        using var groupA = Render(scene, unknown, viewport, 1);
        using var groupB = Render(scene, unknown, viewport, 12);
        Require(DifferentPixels(groupA, groupB), "three-seat no-signal scene changes between frames");
        groupA.Save(Path.Combine(folder, "no-signal-three-seat-a.png"), ImageFormat.Png);
        groupB.Save(Path.Combine(folder, "no-signal-three-seat-b.png"), ImageFormat.Png);

        // Every enabled agent is offered to the renderer. A one-seat crop must remain pixel-identical
        // whether off-camera agents are present or omitted, proving sourceViewport clipping is global.
        var oneViewport = viewport;
        using var withAll = Render(scene, all, oneViewport, 36);
        using var oneOnly = Render(scene, all.Take(3).ToArray(), oneViewport, 36);
        Require(!DifferentPixels(withAll, oneOnly), "enabled-only camera crop never draws off-screen providers");

        using (var contact = new Bitmap(1632, 448, PixelFormat.Format32bppPArgb))
        using (var g = Graphics.FromImage(contact))
        {
            g.Clear(Color.FromArgb(31, 35, 46));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.SmoothingMode = SmoothingMode.None;
            for (var i = 0; i < individual.Count; i++)
            {
                var x = (i % 3) * 544;
                var y = (i / 3) * 224;
                g.DrawImage(individual[i], new Rectangle(x, y, 544, 112), new Rectangle(0, 0, individual[i].Width, individual[i].Height), GraphicsUnit.Pixel);
                using var a = Render(scene, unknown, scene.GetViewport([SharedOfficeScene.ProviderOrder[i]]), 1);
                using var b = Render(scene, unknown, scene.GetViewport([SharedOfficeScene.ProviderOrder[i]]), 12);
                g.DrawImage(a, new Rectangle(x, y + 112, 272, 56), new Rectangle(0, 0, a.Width, a.Height), GraphicsUnit.Pixel);
                g.DrawImage(b, new Rectangle(x + 272, y + 112, 272, 56), new Rectangle(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
                using var font = new Font("Segoe UI", 12, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                g.DrawString(SharedOfficeScene.ProviderOrder[i], font, brush, x + 6, y + 4);
            }
            contact.Save(Path.Combine(folder, "shared-office-contact-sheet.png"), ImageFormat.Png);
        }
        foreach (var image in individual) image.Dispose();

        // Live window sky and per-state screens: every seat in a different state, under fixed skies.
        var mixed = SharedOfficeScene.ProviderOrder.Select((id, i) => new AgentStatus(id, id,
            new[] { ActivityState.Working, ActivityState.Waiting, ActivityState.Idle, ActivityState.Unknown, ActivityState.Idle, ActivityState.Working }[i],
            false, "offline visual check", "check", ProviderSnapshot.Unavailable(id, "offline visual check"))).ToArray();
        var skies = new (string Name, DateTime At, Weather Weather)[]
        {
            ("night-clear", new DateTime(2026, 10, 26, 22, 30, 0), Weather.Clear),
            ("night-rain", new DateTime(2026, 10, 26, 23, 10, 0), Weather.Rain),
            ("dusk-partly", new DateTime(2026, 10, 26, 18, 20, 0), Weather.PartlyCloudy),
            ("day-clear", new DateTime(2026, 10, 26, 11, 0, 0), Weather.Clear),
            ("day-storm", new DateTime(2026, 10, 26, 15, 0, 0), Weather.Storm),
            ("night-snow", new DateTime(2026, 10, 26, 2, 0, 0), Weather.Snow),
        };
        foreach (var (name, at, weather) in skies)
        {
            var sky = SkyService.Compute(at, new AppSettings(), null, weather);
            for (var page = 0; page < 2; page++)
            {
                var camera = scene.GetViewport(SharedOfficeScene.ProviderOrder.Skip(page * 3).Take(3).ToArray(), SharedOfficeScene.ProviderOrder);
                using var frameA = Render(scene, mixed, camera, 40, 2f, sky);
                using var frameB = Render(scene, mixed, camera, 44, 2f, sky);
                Require(DifferentPixels(frameA, frameB), $"{name} scene animates (screens or sky)");
                frameA.Save(Path.Combine(folder, $"sky-{name}-{page}.png"), ImageFormat.Png);
            }
        }

        // Hiding providers removes their rooms: three scattered providers sit side by side.
        var trio = mixed.Where(a => a.Id is "codex" or "magnific" or "openrouter").ToArray();
        using (var collapsed = Render(scene, trio, scene.GetViewport(trio.Select(a => a.Id).ToArray()), 40, 2f))
            collapsed.Save(Path.Combine(folder, "rooms-collapsed.png"), ImageFormat.Png);

        // A tool without art gets a placeholder room and persona of its own, after the built-in rooms.
        var newcomer = new AgentStatus("exemplo", "Exemplo", ActivityState.Working, false, "offline visual check", "check",
            ProviderSnapshot.Unavailable("Exemplo", "offline visual check"));
        var withNewcomer = trio.Append(newcomer).ToArray();
        var rooms = withNewcomer.Select(a => a.Id).ToArray();
        var world = new RectangleF(0, 0, 4 * 363, 224);
        var head = scene.GetHeadAnchor("exemplo", world, world, rooms).X - scene.GetHeadAnchor("openrouter", world, world, rooms).X;
        Require(Math.Abs(head - 363) < 2, "a tool without art gets its own room after the others");
        using (var placeholder = Render(scene, withNewcomer, scene.GetViewport(["openrouter", "exemplo"], rooms), 40))
        using (var without = Render(scene, trio, scene.GetViewport(["openrouter", "exemplo"], rooms), 40))
        {
            Require(DifferentPixels(placeholder, without), "the placeholder room has a persona at its desk");
            placeholder.Save(Path.Combine(folder, "placeholder-room.png"), ImageFormat.Png);
        }
        var seats = new OfficeViewport();
        seats.SetEnabled(["exemplo", "codex"]);
        Require(seats.EnabledIds.SequenceEqual(["codex", "exemplo"]), "office navigation includes the placeholder seat, after the built-in ones");

        report.Add("PASS " + "embedded backdrop, manifest and all independent poses load");
        report.Add("PASS " + "modular rooms: three-room view, hidden providers' rooms collapse");
        report.Add("PASS " + "six separate provider renders and 12 individual unknown-state frames");
        report.Add("PASS " + "three-seat no-signal frames alternate");
        report.Add("PASS " + "camera clipping excludes off-screen providers");
        report.Add("PASS " + "a tool without art gets a placeholder room and persona");
        File.WriteAllLines(Path.Combine(folder, "shared-office-checks.txt"), report, Encoding.UTF8);
    }

    private static AgentStatus[] CreateStatuses(ActivityState state) =>
        SharedOfficeScene.ProviderOrder.Select(id => new AgentStatus(id, id, state, false, "offline visual check", "check",
            ProviderSnapshot.Unavailable(id, "offline visual check"))).ToArray();

    private static Bitmap Render(SharedOfficeScene scene, IReadOnlyList<AgentStatus> agents, RectangleF viewport, int frame,
        float zoom = 1, SkyState? sky = null)
    {
        var height = (int)Math.Round(viewport.Height * zoom);
        var width = (int)Math.Round(viewport.Width * zoom);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        scene.Render(g, new RectangleF(0, 0, width, height), agents, viewport, frame, sky);
        return bitmap;
    }

    private static bool DifferentPixels(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return true;
        for (var y = 0; y < left.Height; y += 3)
        for (var x = 0; x < left.Width; x += 3)
            if (left.GetPixel(x, y) != right.GetPixel(x, y)) return true;
        return false;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Shared office check failed: " + message);
    }
}
