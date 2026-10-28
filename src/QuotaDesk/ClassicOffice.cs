using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace AIUsageMonitor;

/// <summary>
/// Classic template scene: the original three-desk voxel office (assets\voxel-office.png, sky cut out of the windows)
/// with light layers drawn on top at runtime: the live sky behind the glass, each monitor's content and small
/// per-state effects. The three tools on camera sit at the desks in card order, each with its own persona poses.
/// Coordinates below are in plate pixels.
/// </summary>
internal sealed class ClassicOffice : IDisposable
{
    public const int SrcW = 1494, SrcH = 381;
    public const float LogicalW = 855f, LogicalH = 218f;

    /// <summary>
    /// Screen = top-left, top-right, bottom-left of the monitor glass. Head = top of the hair.
    /// Anchor = where the chair's leftmost wheel touches the floor; AnchorX is that same point inside the sprite canvas.
    /// </summary>
    private sealed record Station(string Id, PointF[] Screen, Size ScreenPx, PointF Head, PointF Anchor, int AnchorX);

    private static readonly Station[] Stations =
    [
        new("codex", [new(256, 171), new(349, 171), new(256, 242)], new(40, 30), new(150, 122), new(74, 366), 32),
        new("claude", [new(799, 172), new(910, 172), new(799, 243)], new(46, 30), new(665, 132), new(610, 366), 33),
        new("antigravity", [new(1259, 173), new(1380, 173), new(1259, 243)], new(50, 30), new(1140, 133), new(1094, 368), 37),
    ];
    private static readonly string[] Poses = ["type-a", "type-b", "blink", "look", "wait", "coffee", "sip", "sleep", "stretch", "nosig-a", "nosig-b", "chair"];
    private static readonly PointF[] Mugs = [new(345, 250), new(891, 252), new(1381, 255)];   // top centre of each desk mug
    private static readonly RectangleF[] Windows = [new(389, 27, 238, 188), new(1010, 26, 216, 189)];
    private static readonly (float X, float Y)[] Stars =
        [(0.06f, 0.08f), (0.17f, 0.30f), (0.29f, 0.12f), (0.38f, 0.42f), (0.50f, 0.20f), (0.61f, 0.06f), (0.70f, 0.33f), (0.81f, 0.13f), (0.90f, 0.45f), (0.12f, 0.52f), (0.57f, 0.50f), (0.95f, 0.22f)];

    private Bitmap? _plate;   // plate scaled to the device size of the scene
    private Bitmap? _frame;   // composed frame, same size
    private readonly Bitmap?[] _screens = new Bitmap?[Stations.Length];
    private readonly Dictionary<string, Bitmap> _sprites = [];   // "<id>-<pose>", scaled to the device size
    private readonly (string? Pose, DateTime Until)[] _breaks = new (string?, DateTime)[Stations.Length];
    private readonly DateTime[] _stretchUntil = new DateTime[Stations.Length];
    private Bitmap? _base;    // sky + plate, rebuilt only when the sky changes
    private string _baseKey = "";

    /// <summary>Previews only: pins the idle pose ("coffee", "sip", "sleep" or "stretch").</summary>
    internal static string? ForcedIdlePose { get; set; }

    /// <summary>Allows opening the voxel plate before personal character sprites have been added.</summary>
    internal static bool ForceEnabled { get; set; }

    /// <summary>
    /// Idle characters alternate between a coffee break (holding the mug, a sip now and then) and a nap,
    /// 35–85 seconds each; waking up starts with a stretch.
    /// </summary>
    private string IdlePose(int slot, int frame)
    {
        if (ForcedIdlePose is { } forced) return forced;
        var now = DateTime.Now;
        if (_breaks[slot].Pose is null || now >= _breaks[slot].Until)
        {
            var next = _breaks[slot].Pose switch { "coffee" => "sleep", "sleep" => "coffee", _ => Random.Shared.Next(2) == 0 ? "coffee" : "sleep" };
            if (_breaks[slot].Pose == "sleep") _stretchUntil[slot] = now.AddSeconds(2.2);
            _breaks[slot] = (next, now.AddSeconds(Random.Shared.Next(35, 85)));
        }
        if (now < _stretchUntil[slot]) return "stretch";
        if (_breaks[slot].Pose == "sleep") return "sleep";
        return (frame + slot * 13) % 45 < 7 ? "sip" : "coffee";
    }

    /// <summary>One pre-rendered pose per real state, with small variations so nobody sits frozen.</summary>
    private string PoseFor(ActivityState state, int slot, int frame)
    {
        var t = frame + slot * 23;
        return state switch
        {
            ActivityState.Working when t % 170 < 10 => "look",        // a glance at you every ~20 s
            ActivityState.Working when t % 37 < 2 => "blink",
            ActivityState.Working => t / 3 % 2 == 0 ? "type-a" : "type-b",
            ActivityState.Waiting => "wait",
            ActivityState.Idle => IdlePose(slot, frame),
            _ => t / 14 % 2 == 0 ? "nosig-a" : "nosig-b"                 // "sem sinal": hunting for signal with the phone
        };
    }

    private static readonly Lazy<bool> HasArt = new(() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(n => n.EndsWith("voxel-office.png", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The plate has exactly three desks; fewer tools use the pixel-art office, as the classic panel did.</summary>
    public static bool Supports(IReadOnlyList<AgentStatus> agents) =>
        agents.Count == Stations.Length && (HasArt.Value || ForceEnabled);

    // Desks follow the cards, left to right, whichever tools are on camera.
    private static Station StationFor(AgentStatus agent, int index) => Stations[index % Stations.Length];

    /// <summary>The persona art for a tool: its own, or its category's while it has none.</summary>
    private static string ArtId(string id) => ProviderModuleCatalog.Find(id) is not null ? id : ProviderCategories.SeatOf(id);

    /// <summary>Where the chair's leftmost wheel sits inside the provider's pose canvas.</summary>
    private static int AnchorXFor(string id) => ProviderModuleCatalog.Find(ArtId(id))?.Station.CharacterAnchorX ?? 32;

    /// <summary>Top of the character's head, in logical units relative to the scene origin (for speech bubbles).</summary>
    public static PointF HeadAnchor(AgentStatus agent, int index)
    {
        var head = StationFor(agent, index).Head;
        return new PointF(head.X * LogicalW / SrcW, head.Y * LogicalH / SrcH);
    }

    public void Render(Graphics g, RectangleF dest, IReadOnlyList<AgentStatus> agents, int frame, SkyState sky)
    {
        float scale, offsetX, offsetY;
        using (var m = g.Transform) { scale = m.Elements[0]; offsetX = m.OffsetX; offsetY = m.OffsetY; }
        var width = Math.Max(1, (int)Math.Round(dest.Width * scale));
        var height = Math.Max(1, (int)Math.Round(dest.Height * scale));
        EnsureBuffers(width, height);
        var kx = width / (float)SrcW;
        var ky = height / (float)SrcH;

        // Sky + plate change slowly (clouds drift once a second); rain, snow and lightning need every frame.
        var lively = sky.Weather is Weather.Rain or Weather.Drizzle or Weather.Storm or Weather.Snow;
        var skyFrame = lively ? frame : frame / 8 * 8;
        var baseKey = sky.Key + "|" + skyFrame;
        if (baseKey != _baseKey)
        {
            using var gb = Graphics.FromImage(_base!);
            gb.Clear(Color.FromArgb(8, 10, 18));
            gb.SmoothingMode = SmoothingMode.None;
            gb.ScaleTransform(kx, ky);
            for (var w = 0; w < Windows.Length; w++) DrawSky(gb, Windows[w], sky, skyFrame, celestial: w == 0, seed: w);
            gb.ResetTransform();
            gb.DrawImageUnscaled(_plate!, 0, 0);
            _baseKey = baseKey;
        }

        using (var gf = Graphics.FromImage(_frame!))
        {
            gf.CompositingMode = CompositingMode.SourceCopy;
            gf.DrawImageUnscaled(_base!, 0, 0);
            gf.CompositingMode = CompositingMode.SourceOver;
            gf.SmoothingMode = SmoothingMode.None;

            gf.ScaleTransform(kx, ky);
            DrawServerLeds(gf, frame, agents.Any(a => a.State == ActivityState.Working));
            DrawSleep(gf, 598, 286, frame + 9, 3f);   // the cat
            for (var i = 0; i < Mugs.Length; i++) DrawSteam(gf, Mugs[i], frame + i * 5);
            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                var station = StationFor(agent, i);
                var slot = Array.IndexOf(Stations, station);
                var look = Theme.Look(agent.Id);
                DrawScreen(gf, station, slot, agent.State, frame + i * 5, look);

                // Exhausted quota: an empty chair for the weekly limit, a coffee break for the 5-hour one.
                var pause = QuotaPause.For(agent, DateTimeOffset.Now).Kind;
                var pose = pause == PauseKind.Weekly ? "chair"
                    : pause == PauseKind.FiveHour ? ((frame + slot * 13) % 45 < 7 ? "sip" : "coffee")
                    : PoseFor(agent.State, slot, frame);
                if (_sprites.TryGetValue(ArtId(agent.Id) + "-" + pose, out var sprite))
                {
                    gf.ResetTransform();
                    gf.DrawImageUnscaled(sprite, (int)Math.Round((station.Anchor.X - AnchorXFor(agent.Id)) * kx), (int)Math.Round(station.Anchor.Y * ky) - sprite.Height);
                    gf.ScaleTransform(kx, ky);

                    if (pose == "sleep") DrawSleep(gf, station.Head.X + 48, station.Head.Y + 6, frame + i * 7);
                    if (pose == "wait") DrawThinking(gf, station.Head.X + 46, station.Head.Y - 22, frame + i * 2, look.Accent);
                    if (pose.StartsWith("nosig", StringComparison.Ordinal)) DrawNoSignal(gf, station.Head.X - 78, station.Head.Y - 14, frame + i * 3);
                }
            }
        }

        var state = g.Save();
        g.ResetTransform();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImageUnscaled(_frame!, (int)Math.Round(dest.X * scale + offsetX), (int)Math.Round(dest.Y * scale + offsetY));
        g.Restore(state);
    }

    private void EnsureBuffers(int width, int height)
    {
        if (_plate is not null && _plate.Width == width && _plate.Height == height) return;
        _plate?.Dispose();
        _frame?.Dispose();
        _base?.Dispose();
        _base = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        _baseKey = "";
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().First(n => n.EndsWith("voxel-office.png", StringComparison.OrdinalIgnoreCase));
        using (var stream = assembly.GetManifestResourceStream(name)!)
        using (var full = new Bitmap(stream))
            _plate = Scaled(full, width, height);
        _frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

        foreach (var sprite in _sprites.Values) sprite.Dispose();
        _sprites.Clear();
        var resources = assembly.GetManifestResourceNames();
        foreach (var module in ProviderModuleCatalog.All)
        foreach (var pose in Poses)
        {
            var key = module.Id + "-" + pose;
            var resource = resources.FirstOrDefault(n => n.EndsWith($"providers.{module.Id}.poses.{pose}.png", StringComparison.OrdinalIgnoreCase));
            if (resource is null) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var full = new Bitmap(stream);
            _sprites[key] = Scaled(full, Math.Max(1, (int)Math.Round(full.Width * width / (float)SrcW)), Math.Max(1, (int)Math.Round(full.Height * height / (float)SrcH)));
        }
    }

    private static Bitmap Scaled(Bitmap source, int width, int height)
    {
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe on the borders
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
        return scaled;
    }

    // ---------------------------------------------------------------- sky behind the glass

    private static void DrawSky(Graphics g, RectangleF r, SkyState sky, int frame, bool celestial, int seed)
    {
        var state = g.Save();
        g.SetClip(r);
        using (var gradient = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), sky.Top, sky.Bottom, LinearGradientMode.Vertical))
            g.FillRectangle(gradient, r);

        var clear = 1 - Math.Clamp(sky.CloudCover / 100f, 0, 1);
        if (sky.Night > 0.35f)
            for (var i = 0; i < Stars.Length; i++)
            {
                var alpha = (int)(255 * Math.Clamp((sky.Night - 0.35f) / 0.5f, 0, 1) * (0.25f + 0.75f * clear) * ((i + frame / 12 + seed) % 4 == 0 ? 0.4f : 1f));
                Fill(g, Color.FromArgb(alpha, 235, 240, 255), r.X + Stars[(i + seed * 5) % Stars.Length].X * r.Width, r.Y + Stars[i].Y * r.Height, 3, 3);
            }

        if (celestial && sky.SunT is > -0.03f and < 1.03f)
        {
            var t = Math.Clamp(sky.SunT, 0, 1);
            var cx = r.X + 20 + t * (r.Width - 40);
            var cy = r.Bottom - 50 - (float)Math.Sin(Math.PI * t) * (r.Height - 80);
            var alpha = sky.CloudCover > 85 ? 0.35f : 1f;
            var color = sky.SunT is < 0.08f or > 0.92f ? Color.FromArgb(255, 170, 90) : Color.FromArgb(255, 236, 150);
            Fill(g, Color.FromArgb((int)(40 * alpha), color), cx - 30, cy - 30, 60, 60);
            Fill(g, Color.FromArgb((int)(70 * alpha), color), cx - 22, cy - 22, 44, 44);
            Fill(g, Color.FromArgb((int)(255 * alpha), color), cx - 15, cy - 15, 30, 30);
        }
        if (celestial && sky.Night > 0.3f)
        {
            // Square voxel moon, where the concept art has it; the dark side follows the real phase
            var alpha = (int)(255 * Math.Clamp((sky.Night - 0.3f) / 0.4f, 0, 1) * (0.35f + 0.65f * clear));
            const float size = 32;
            var mx = r.X + 44;
            var my = r.Y + 35;
            Fill(g, Color.FromArgb(alpha / 8, 255, 244, 200), mx - 10, my - 10, size + 20, size + 20);
            Fill(g, Color.FromArgb(alpha, 254, 252, 214), mx, my, size, size);
            var lit = (1 - Math.Cos(2 * Math.PI * sky.MoonPhase)) / 2;
            var dark = (float)Math.Round(size * (1 - lit) / 4) * 4;
            var darkOnLeft = (sky.MoonPhase < 0.5) != sky.SouthernHemisphere;
            if (dark > 0)
                Fill(g, Color.FromArgb(alpha, Theme.Mix(sky.Top, Color.FromArgb(254, 252, 214), 0.14f)), darkOnLeft ? mx : mx + size - dark, my, dark, size);
        }

        var wet = sky.Weather is Weather.Rain or Weather.Storm or Weather.Drizzle;
        var clouds = sky.CloudCover switch { < 15 => 1, < 40 => 2, < 70 => 3, _ => 5 };
        if (sky.Weather == Weather.Clear) clouds = sky.CloudCover < 8 ? 0 : 1;
        var baseCloud = wet || sky.Weather == Weather.Cloudy
            ? Theme.Mix(Color.FromArgb(150, 156, 170), Color.FromArgb(40, 44, 58), sky.Night)
            : Theme.Mix(Color.FromArgb(242, 245, 252), Color.FromArgb(62, 68, 92), sky.Night);
        var cloudColor = Theme.Mix(baseCloud, sky.Bottom, 0.18f);
        if (sky.CloudCover >= 80)
            for (var band = 0; band < 4; band++)
                Fill(g, Color.FromArgb(210 - band * 45, cloudColor), r.X, r.Y + band * 8, r.Width, 8);
        string[] shape = ["...####....", ".########..", "###########", ".#########."];
        const float cell = 8;
        for (var i = 0; i < clouds; i++)
        {
            var span = r.Width + 180;
            var cx = r.X - 90 + (Hash(i * 29 + seed * 11) % (int)span + frame * (0.6f + i * 0.15f)) % span;
            var cy = r.Y + 8 + Hash(i * 7 + 3 + seed) % 60;
            for (var row = 0; row < shape.Length; row++)
            for (var col = 0; col < shape[row].Length; col++)
                if (shape[row][col] == '#')
                    Fill(g, Color.FromArgb(row == shape.Length - 1 ? 190 : 232, row == 0 ? Theme.Mix(cloudColor, Color.White, 0.2f) : cloudColor),
                        (float)Math.Floor(cx / cell) * cell + col * cell, cy + row * cell, cell, cell);
        }

        var drops = sky.Weather switch { Weather.Drizzle => 14, Weather.Rain => 30, Weather.Storm => 46, _ => 0 };
        var rain = Color.FromArgb(sky.Weather == Weather.Drizzle ? 120 : 175, 170, 200, 240);
        for (var i = 0; i < drops; i++)
        {
            var y = (Hash(i * 7 + 1 + seed) % (int)r.Height + frame * 11) % (int)r.Height;
            var x = (Hash(i * 13 + 5 + seed) % (int)r.Width - y / 3 + (int)r.Width) % (int)r.Width;
            Fill(g, rain, r.X + x, r.Y + y, 2.5f, 9);
        }
        if (sky.Weather == Weather.Snow)
            for (var i = 0; i < 22; i++)
            {
                var y = (Hash(i * 5 + seed) % (int)r.Height + frame * 2) % (int)r.Height;
                var x = (Hash(i * 11 + seed) % (int)r.Width + (int)(Math.Sin((frame + i * 9) / 6.0) * 6) + (int)r.Width) % (int)r.Width;
                Fill(g, Color.FromArgb(230, 245, 248, 255), r.X + x, r.Y + y, 4, 4);
            }
        if (sky.Weather == Weather.Fog)
            Fill(g, Color.FromArgb(96, Theme.Mix(Color.FromArgb(190, 196, 206), Color.FromArgb(60, 64, 80), sky.Night)), r.X, r.Y, r.Width, r.Height);
        if (sky.Weather == Weather.Storm && Hash(frame / 2 * 31 + 7) % 36 == 0)
        {
            Fill(g, Color.FromArgb(120, 235, 240, 255), r.X, r.Y, r.Width, r.Height);
            var bx = r.X + 30 + Hash(frame) % (int)(r.Width - 60);
            for (var step = 0; step < 12; step++)
            {
                bx += (Hash(frame + step) % 3 - 1) * 5;
                Fill(g, Color.White, bx, r.Y + step * 8, 4, 8);
            }
        }
        g.Restore(state);
    }

    // ---------------------------------------------------------------- stations

    private void DrawScreen(Graphics g, Station s, int slot, ActivityState state, int frame, Theme.AgentLook look)
    {
        var (w, h) = (s.ScreenPx.Width, s.ScreenPx.Height);
        var bmp = _screens[slot] ??= new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var sg = Graphics.FromImage(bmp))
        {
            sg.SmoothingMode = SmoothingMode.None;
            switch (state)
            {
                case ActivityState.Working:
                    sg.Clear(Color.FromArgb(10, 12, 20));
                    var rows = (h - 3) / 4;
                    for (var row = 0; row < rows; row++)
                    {
                        var hash = Hash((row + frame / 4) * 31 + slot * 7);
                        var indent = hash % 3 * 2;
                        var length = 4 + hash / 3 % (w / 2);
                        if (row == rows - 1) length = Math.Max(2, length * (frame % 4 + 1) / 4); // the newest line is still being typed
                        var color = (hash / 7 % 3) switch { 0 => look.Accent, 1 => look.ScreenText, _ => Color.FromArgb(206, 214, 236) };
                        Fill(sg, color, 2 + indent, 2 + row * 4, Math.Min(length, w - 4 - indent), 2);
                        var extra = hash / 11 % (w / 4);
                        if (row != rows - 1 && extra > 2 && 4 + indent + length + extra < w - 2)
                            Fill(sg, Color.FromArgb(92, 112, 152), 4 + indent + length, 2 + row * 4, extra, 2);
                    }
                    break;
                case ActivityState.Waiting:
                    sg.Clear(Color.FromArgb(10, 12, 20));
                    Fill(sg, Color.FromArgb(70, 84, 116), 3, 3, w / 2, 2);
                    Fill(sg, Color.FromArgb(54, 64, 92), 3, 7, w / 3, 2);
                    var py = h - 10;
                    for (var step = 0; step < 3; step++) // ">" prompt
                    {
                        Fill(sg, look.ScreenText, 3 + step, py + step, 2, 1);
                        Fill(sg, look.ScreenText, 3 + step, py + 4 - step, 2, 1);
                    }
                    if (frame / 4 % 2 == 0) Fill(sg, look.Accent, 10, py, 4, 5);
                    break;
                case ActivityState.Idle:
                    sg.Clear(Color.FromArgb(6, 8, 14));
                    var bx = Bounce(frame, w - 8);
                    var by = Bounce(frame * 7 / 10, h - 6);
                    Fill(sg, Color.FromArgb(70, look.Accent), Bounce(frame - 2, w - 8) + 1, Bounce((frame - 2) * 7 / 10, h - 6) + 1, 6, 4);
                    Fill(sg, look.Accent, bx + 1, by + 1, 6, 4);
                    break;
                default:
                    // No signal: TV static with the message on top
                    sg.Clear(Color.FromArgb(16, 18, 26));
                    for (var speck = 0; speck < 40; speck++)
                    {
                        var hash = Hash(speck * 17 + frame / 2 * 131 + slot);
                        Fill(sg, Color.FromArgb(40 + hash % 90, 200, 206, 220), hash / 7 % w, hash / 131 % h, 2, 1);
                    }
                    var ink = frame / 5 % 2 == 0 ? Color.FromArgb(240, 242, 248) : Color.FromArgb(170, 176, 196);
                    Fill(sg, Color.FromArgb(200, 16, 18, 26), 2, h / 2 - 11, w - 4, 21);
                    PixelFont.Draw(sg, L.T("SEM", "NO"), (w - PixelFont.Measure(L.T("SEM", "NO"), 1, 1)) / 2f, h / 2 - 9, 1, ink, 1);
                    PixelFont.Draw(sg, L.T("SINAL", "SIGNAL"), (w - PixelFont.Measure(L.T("SINAL", "SIGNAL"), 1, 1)) / 2f, h / 2, 1, ink, 1);
                    break;
            }
        }
        var oldInterpolation = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, s.Screen);
        g.InterpolationMode = oldInterpolation;
        g.PixelOffsetMode = oldOffset;
    }

    /// <summary>Idle: a few "z" drifting up beside the head.</summary>
    private static void DrawSleep(Graphics g, float x, float y, int frame, float size = 4f)
    {
        var count = frame / 6 % 4;
        string[] glyph = ["####", "..#.", ".#..", "####"];
        for (var i = 0; i < count; i++)
        {
            var cell = size + i * 1.5f;
            var ox = x + i * 20;
            var oy = y - i * 22;
            for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
                if (glyph[row][col] == '#')
                {
                    Fill(g, Color.FromArgb(150, 10, 12, 24), ox + col * cell + 1.5f, oy + row * cell + 1.5f, cell, cell);
                    Fill(g, Color.FromArgb(226, 236, 255), ox + col * cell, oy + row * cell, cell, cell);
                }
        }
    }

    /// <summary>Waiting: a small thought bubble with three dots taking turns.</summary>
    private static void DrawThinking(Graphics g, float x, float y, int frame, Color accent)
    {
        var ink = Color.FromArgb(12, 13, 22);
        Fill(g, ink, x + 3, y, 50, 28);
        Fill(g, ink, x, y + 3, 56, 22);
        Fill(g, Color.FromArgb(242, 244, 250), x + 3, y + 3, 50, 22);
        Fill(g, ink, x + 2, y + 30, 9, 8);
        Fill(g, Color.FromArgb(242, 244, 250), x + 4, y + 32, 5, 4);
        for (var dot = 0; dot < 3; dot++)
            Fill(g, dot == frame / 3 % 3 ? accent : Color.FromArgb(120, 126, 146), x + 10 + dot * 13, y + 11, 7, 7);
    }

    /// <summary>"Sem sinal": a bubble with a crossed-out signal icon, blinking.</summary>
    private static void DrawNoSignal(Graphics g, float x, float y, int frame)
    {
        var ink = Color.FromArgb(12, 13, 22);
        Fill(g, ink, x + 3, y, 58, 44);
        Fill(g, ink, x, y + 3, 64, 38);
        Fill(g, Color.FromArgb(242, 244, 250), x + 3, y + 3, 58, 38);
        Fill(g, ink, x + 52, y + 46, 9, 8);
        Fill(g, Color.FromArgb(242, 244, 250), x + 54, y + 44, 5, 8);
        string[] icon = [".#######.", "#.......#", "..#####..", ".#.....#.", "...###...", "....#...."];
        var bars = frame / 5 % 2 == 0 ? Color.FromArgb(150, 156, 176) : Color.FromArgb(196, 200, 214);
        for (var row = 0; row < icon.Length; row++)
        for (var col = 0; col < icon[row].Length; col++)
            if (icon[row][col] == '#') Fill(g, bars, x + 14 + col * 4, y + 10 + row * 4, 4, 4);
        if (frame / 5 % 2 == 0)
            for (var step = 0; step < 7; step++) // red slash
                Fill(g, Color.FromArgb(232, 60, 70), x + 16 + step * 5, y + 32 - step * 4, 6, 5);
    }

    /// <summary>A wisp of steam over a desk mug.</summary>
    private static void DrawSteam(Graphics g, PointF mug, int frame)
    {
        for (var puff = 0; puff < 3; puff++)
        {
            var phase = (frame + puff * 5) % 15;
            var alpha = (int)(120 * (1 - phase / 15f));
            Fill(g, Color.FromArgb(alpha, 236, 240, 250), mug.X - 2 + (float)Math.Sin((phase + puff * 3) / 2.5) * 3, mug.Y - 6 - phase * 1.6f, 4, 4);
        }
    }

    private static void DrawServerLeds(Graphics g, int frame, bool busy)
    {
        for (var row = 0; row < 5; row++)
            if (Hash(frame / (busy ? 2 : 7) * 13 + row * 5) % 4 == 0)
                Fill(g, Color.FromArgb(14, 20, 34), 486, 185 + row * 24, 10, 9);
    }

    private static void Fill(Graphics g, Color color, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y, width, height);
    }

    private static int Bounce(int t, int max) => max <= 0 ? 0 : Math.Abs((t % (2 * max) + 2 * max) % (2 * max) - max);

    private static int Hash(int v)
    {
        unchecked
        {
            var x = (uint)v * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return (int)(x & 0x7FFFFFFF);
        }
    }

    public void Dispose()
    {
        _plate?.Dispose();
        _frame?.Dispose();
        _base?.Dispose();
        foreach (var screen in _screens) screen?.Dispose();
        foreach (var sprite in _sprites.Values) sprite.Dispose();
    }
}
