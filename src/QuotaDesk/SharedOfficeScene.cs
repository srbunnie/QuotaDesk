using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>
/// The voxel Loft as a row of modular provider rooms with independently animated personas. Only the rooms of the
/// providers on the panel are drawn, side by side in office order: hiding a provider removes its room and persona.
/// The plate has transparent window panes and monitor glass: the live sky and each tool's screen are painted
/// underneath it every frame. Geometry is in world units from the manifest; the plate may be higher resolution.
/// Callers work in "strip" coordinates: the visible rooms laid end to end from x = 0.
/// </summary>
internal sealed class SharedOfficeScene : IDisposable
{
    private const string ResourcePrefix = "AIUsageMonitor.assets.";
    private static readonly string[] Order = ["codex", "claude", "antigravity", "magnific", "figma", "openrouter"];
    private static readonly string[] PoseNames = ["type-a", "type-b", "blink", "look", "wait", "coffee", "sip", "sleep", "stretch", "nosig-a", "nosig-b", "chair"];
    // Optional art for tools without live activity; when a provider ships these files they replace the fallback loop.
    private static readonly string[] OptionalPoseNames = ["ambient-a", "ambient-b"];
    // Balance/status tools: never "working", so a coffee-and-sleep idle would be all they ever show.
    private static readonly HashSet<string> AmbientProviders = new(["magnific", "figma", "openrouter"], StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderGeometry> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _poses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _screens = new(StringComparer.OrdinalIgnoreCase);
    private readonly float _worldWidth, _worldHeight, _viewportWidth, _floorY;
    private Bitmap? _background;
    private Bitmap? _layer;           // plate pre-scaled to the current device size, camera and room strip
    private RectangleF _layerSource;
    private string _layerStrip = "";
    private Strip? _strip;            // last composed strip; the room set changes rarely
    private SkyState? _sky;
    private DateTime _skyAt;
    private bool _disposed;

    /// <summary>Room is the provider's slice of the plate [left, left + width); in a strip, its place in the row.</summary>
    private sealed record ProviderGeometry(string Id, float Seat, PointF Head, float SpriteScale, RectangleF? Screen,
        float RoomLeft, float RoomWidth, RectangleF[] Windows)
    {
        public ProviderGeometry MovedBy(float dx) => this with
        {
            Seat = Seat + dx, Head = new(Head.X + dx, Head.Y), Screen = Screen is { } s ? s with { X = s.X + dx } : null,
            RoomLeft = RoomLeft + dx, Windows = Windows.Select(w => w with { X = w.X + dx }).ToArray(),
        };
    }

    /// <summary>The visible rooms laid end to end: geometry in strip coordinates and where each plate slice goes.</summary>
    private sealed record Strip(string Key, float Width, Dictionary<string, ProviderGeometry> Geometry,
        RectangleF[] Windows, (RectangleF Source, float X)[] Slices);

    public static IReadOnlyList<string> ProviderOrder { get; } = Array.AsReadOnly(Order);

    /// <summary>Live day/night and weather for every scene that is not handed a sky explicitly (mini monitor, previews).</summary>
    public static Func<DateTime, SkyState>? SkySource { get; set; }

    public bool IsAvailable { get; }

    public SharedOfficeScene()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = FindResource(assembly, ResourcePrefix + "office.manifest.json");
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 2)
                throw new InvalidDataException("Unsupported shared-office manifest version.");
            _worldWidth = root.GetProperty("width").GetSingle();
            _worldHeight = root.GetProperty("height").GetSingle();
            _viewportWidth = root.GetProperty("viewportWidth").GetSingle();
            _floorY = root.GetProperty("floorY").GetSingle();
            _background = ReadBitmap(assembly, ResourcePrefix + "office." + root.GetProperty("asset").GetString());
            if (Math.Abs(_background.Width / _worldWidth - _background.Height / _worldHeight) > 0.01f)
                throw new InvalidDataException("Shared-office plate does not match the manifest's world proportions.");
            var allWindows = root.GetProperty("windows").EnumerateArray().Select(ReadRect).ToArray();

            foreach (var item in root.GetProperty("providers").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                RectangleF? screen = item.TryGetProperty("screen", out var s) && s.ValueKind == JsonValueKind.Array ? ReadRect(s) : null;
                var room = item.GetProperty("room");
                var (left, width) = (room[0].GetSingle(), room[1].GetSingle());
                var windows = allWindows.Where(w => w.X + w.Width / 2 >= left && w.X + w.Width / 2 < left + width).ToArray();
                _geometry.Add(id, new(id, item.GetProperty("seat").GetSingle(), ReadPoint(item.GetProperty("head")),
                    item.GetProperty("spriteScale").GetSingle(), screen, left, width, windows));
            }

            if (_geometry.Count != Order.Length || !Order.All(_geometry.ContainsKey))
                throw new InvalidDataException("Shared-office provider geometry is incomplete or out of order.");
            foreach (var id in Order)
            foreach (var pose in PoseNames)
                _poses.Add(id + ":" + pose, ReadBitmap(assembly, ResourcePrefix + $"providers.{id}.poses.{pose}.png"));
            foreach (var id in Order)
            foreach (var pose in OptionalPoseNames)
                if (TryReadBitmap(assembly, ResourcePrefix + $"providers.{id}.poses.{pose}.png") is { } optional)
                    _poses.Add(id + ":" + pose, optional);
            IsAvailable = true;
        }
        catch (Exception)
        {
            DisposeAssets();
            IsAvailable = false;
        }
    }

    /// <summary>The seat art for a provider: its own, or its category's while it has none.</summary>
    private string SeatKey(string id) => _geometry.ContainsKey(id) ? id : ProviderCategories.SeatOf(id);

    /// <summary>
    /// The visible providers' rooms in office order, end to end from x = 0. A category member uses its category's room;
    /// with nobody visible the whole Loft is the strip.
    /// </summary>
    private Strip StripFor(IEnumerable<string>? roomIds)
    {
        var seats = (roomIds ?? []).Select(SeatKey).Where(_geometry.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Without art (IsAvailable false) there is no geometry: an empty strip instead of a KeyNotFoundException.
        var ids = Order.Where(id => _geometry.ContainsKey(id) && (seats.Count == 0 || seats.Contains(id))).ToArray();
        var key = string.Join(",", ids);
        if (_strip?.Key == key) return _strip;
        var geometry = new Dictionary<string, ProviderGeometry>(StringComparer.OrdinalIgnoreCase);
        var slices = new List<(RectangleF, float)>();
        var x = 0f;
        foreach (var id in ids)
        {
            var room = _geometry[id];
            geometry.Add(id, room.MovedBy(x - room.RoomLeft));
            slices.Add((new RectangleF(room.RoomLeft, 0, room.RoomWidth, _worldHeight), x));
            x += room.RoomWidth;
        }
        return _strip = new(key, x, geometry, geometry.Values.SelectMany(g => g.Windows).ToArray(), slices.ToArray());
    }

    /// <summary>
    /// The camera over the strip of <paramref name="roomIds"/> (the providers on the panel; defaults to the visible
    /// ones) that frames the rooms of <paramref name="visibleIds"/>: a three-room view, never wider than the strip.
    /// </summary>
    public RectangleF GetViewport(IReadOnlyList<string> visibleIds, IReadOnlyList<string>? roomIds = null)
    {
        var strip = StripFor(roomIds ?? visibleIds);
        var rooms = (visibleIds ?? []).Select(SeatKey).Where(strip.Geometry.ContainsKey).Select(id => strip.Geometry[id]).ToArray();
        var width = Math.Min(strip.Width, _viewportWidth);
        if (rooms.Length == 0) return new(0, 0, width, _worldHeight);
        var left = rooms.Min(r => r.RoomLeft);
        var right = rooms.Max(r => r.RoomLeft + r.RoomWidth);
        width = Math.Min(strip.Width, Math.Max(width, right - left));
        var x = Math.Clamp((left + right) / 2f - width / 2f, 0, strip.Width - width);
        return new(x, 0, width, _worldHeight);
    }

    public PointF GetHeadAnchor(string providerId, RectangleF sourceViewport, RectangleF destination, IReadOnlyList<string>? roomIds = null)
    {
        if (!StripFor(roomIds ?? [providerId]).Geometry.TryGetValue(SeatKey(providerId), out var item) ||
            sourceViewport.Width <= 0 || sourceViewport.Height <= 0)
            return new(destination.Left, destination.Top);
        return new(destination.Left + (item.Head.X - sourceViewport.X) * destination.Width / sourceViewport.Width,
            destination.Top + (item.Head.Y - sourceViewport.Y) * destination.Height / sourceViewport.Height);
    }

    public void Render(Graphics graphics, RectangleF destination,
        IReadOnlyList<AgentStatus> activeAgents, RectangleF sourceViewport, int frame, SkyState? sky = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable || _background is null || destination.Width <= 0 || destination.Height <= 0 ||
            sourceViewport.Width <= 0 || sourceViewport.Height <= 0) return;

        var now = DateTimeOffset.Now;
        sky ??= CurrentSky(now.LocalDateTime);
        // A category member without its own art takes the category's seat (and its room, persona and screen).
        // One persona per seat: if two members of a category arrive, the first keeps the chair.
        activeAgents = activeAgents.Select(a => SeatKey(a.Id) is var seat && seat != a.Id ? a with { Id = seat } : a)
            .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var strip = StripFor(activeAgents.Select(a => a.Id));
        var geometry = strip.Geometry;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(destination, CombineMode.Intersect); // keep the caller's rounded clip
            var device = DeviceRect(graphics, destination);
            using (var night = new SolidBrush(Color.FromArgb(10, 12, 20))) graphics.FillRectangle(night, destination);

            // World units from here on.
            graphics.TranslateTransform(destination.X, destination.Y);
            graphics.ScaleTransform(destination.Width / sourceViewport.Width, destination.Height / sourceViewport.Height);
            graphics.TranslateTransform(-sourceViewport.X, -sourceViewport.Y);
            graphics.SmoothingMode = SmoothingMode.None;

            var byId = activeAgents.Where(a => geometry.ContainsKey(a.Id)).ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
            var pauses = byId.Values.ToDictionary(a => a.Id, a => QuotaPause.For(a, now), StringComparer.OrdinalIgnoreCase);
            PaintSky(graphics, sky, frame, sourceViewport, strip);
            foreach (var item in geometry.Values)
                if (item.Screen is { } screen && screen.IntersectsWith(sourceViewport))
                    PaintScreen(graphics, item, screen, byId.GetValueOrDefault(item.Id),
                        byId.TryGetValue(item.Id, out var owner) ? pauses[owner.Id].Kind : PauseKind.None, frame);

            DrawPlate(graphics, device, sourceViewport, strip);

            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            var signs = new List<(ProviderGeometry Item, QuotaPause Pause, string Id)>();
            foreach (var agent in activeAgents)
            {
                if (!geometry.TryGetValue(agent.Id, out var item)) continue;
                var pose = PoseFor(agent, pauses[agent.Id].Kind, frame, Array.IndexOf(Order, agent.Id));
                var sprite = _poses[agent.Id + ":" + pose];
                var height = sprite.Height * item.SpriteScale;
                var width = sprite.Width * item.SpriteScale;
                var world = new RectangleF(item.Seat - width / 2, _floorY - height, width, height);
                if (!sourceViewport.IntersectsWith(world)) continue;
                // Only seats whose head is inside the view get a sign; clamping an off-screen one would put it on a neighbor.
                if (pauses[agent.Id] is { Kind: not PauseKind.None } pause && item.Head.X >= sourceViewport.Left && item.Head.X <= sourceViewport.Right)
                    signs.Add((item, pause, agent.Id));
                graphics.DrawImage(sprite, world, new Rectangle(0, 0, sprite.Width, sprite.Height), GraphicsUnit.Pixel);
                var slot = Array.IndexOf(Order, agent.Id);
                if (pose == "sleep") DrawSleep(graphics, item.Head.X + 24, item.Head.Y + 10, frame + slot * 7, 2.6f);
                if (pose == "wait") DrawThinking(graphics, item.Head.X + 26, item.Head.Y - 8, frame + slot * 2, Theme.Look(agent.Id).Accent);
            }
            if (sky.Weather == Weather.Storm && Lightning(frame))
                using (var flash = new SolidBrush(Color.FromArgb(28, 220, 228, 255))) graphics.FillRectangle(flash, sourceViewport);

            // Signs are laid out in destination units, outside the world transform.
            graphics.Restore(state);
            state = graphics.Save();
            graphics.SetClip(destination, CombineMode.Intersect); // keep the caller's rounded clip
            foreach (var (item, pause, id) in signs)
                PaintPauseSign(graphics, item, pause, id, now, sourceViewport, destination);
        }
        finally { graphics.Restore(state); }
    }

    private SkyState CurrentSky(DateTime now)
    {
        if (_sky is null || (now - _skyAt).Duration().TotalSeconds >= 30)
        {
            _sky = SkySource?.Invoke(now) ?? SkyService.Compute(now, new AppSettings(), null);
            _skyAt = now;
        }
        return _sky;
    }

    private static RectangleF DeviceRect(Graphics g, RectangleF r)
    {
        var points = new[] { r.Location, new PointF(r.Right, r.Bottom) };
        g.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, points);
        return RectangleF.FromLTRB(points[0].X, points[0].Y, points[1].X, points[1].Y);
    }

    /// <summary>
    /// The plate is resampled once per camera position, device size and room strip, then blitted every frame. Each
    /// visible room's slice of the plate is copied to its place in the strip.
    /// </summary>
    private void DrawPlate(Graphics g, RectangleF device, RectangleF viewport, Strip strip)
    {
        var size = new Size(Math.Max(1, (int)Math.Round(device.Width)), Math.Max(1, (int)Math.Round(device.Height)));
        if (_layer is null || _layer.Size != size || _layerSource != viewport || _layerStrip != strip.Key)
        {
            _layer?.Dispose();
            _layer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            var k = _background!.Width / _worldWidth;
            var (sx, sy) = (size.Width / viewport.Width, size.Height / viewport.Height);
            using var lg = Graphics.FromImage(_layer);
            lg.CompositingMode = CompositingMode.SourceCopy;
            lg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            lg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var wrap = new ImageAttributes();
            wrap.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe on the borders
            foreach (var (source, x) in strip.Slices)
            {
                // Whole device columns per room, so neighbouring rooms meet without a seam or an overlap.
                var left = (int)Math.Round((x - viewport.X) * sx);
                var right = (int)Math.Round((x + source.Width - viewport.X) * sx);
                if (right <= 0 || left >= size.Width) continue;
                lg.DrawImage(_background, new Rectangle(left, (int)Math.Round(-viewport.Y * sy), right - left, (int)Math.Round(_worldHeight * sy)),
                    source.X * k, source.Y * k, source.Width * k, source.Height * k, GraphicsUnit.Pixel, wrap);
            }
            _layerSource = viewport;
            _layerStrip = strip.Key;
        }
        var state = g.Save();
        g.ResetTransform();
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImageUnscaled(_layer, (int)Math.Round(device.X), (int)Math.Round(device.Y));
        g.Restore(state);
    }

    // ---------------------------------------------------------------- sky behind the window panes

    /// <summary>
    /// One continuous sky behind every window: gradient, stars, the sun or the moon (real phase), drifting voxel clouds,
    /// a city skyline whose windows light up at night, and the live weather (rain, snow, fog, lightning).
    /// </summary>
    private void PaintSky(Graphics g, SkyState sky, int frame, RectangleF viewport, Strip strip)
    {
        var visible = strip.Windows.Where(w => w.IntersectsWith(viewport)).OrderBy(w => w.X).ToArray();
        if (visible.Length == 0) return;
        var state = g.Save();
        using (var region = new Region())
        {
            region.MakeEmpty();
            foreach (var w in visible) region.Union(w);
            g.SetClip(region, CombineMode.Intersect);
        }
        var top = visible.Min(w => w.Top);
        var bottom = visible.Max(w => w.Bottom);
        var band = new RectangleF(viewport.X, top - 1, viewport.Width, bottom - top + 2);
        using (var gradient = new LinearGradientBrush(band, sky.Top, sky.Bottom, LinearGradientMode.Vertical))
            g.FillRectangle(gradient, band);

        var clear = 1 - Math.Clamp(sky.CloudCover / 100f, 0, 1);
        if (sky.Night > 0.35f)
            for (var i = 0; i < 160; i++)
            {
                var x = Hash(i * 7 + 3) % (int)_worldWidth;
                var y = top + Hash(i * 13 + 1) % (int)((bottom - top) * 0.6f);
                var twinkle = (i + frame / 9) % 5 == 0 ? 0.35f : 1f;
                var alpha = (int)(255 * Math.Clamp((sky.Night - 0.35f) / 0.5f, 0, 1) * (0.2f + 0.8f * clear) * twinkle);
                Fill(g, Color.FromArgb(alpha, 235, 240, 255), x, y, i % 4 == 0 ? 1.6f : 1f, i % 4 == 0 ? 1.6f : 1f);
            }

        PaintCelestial(g, sky, visible, clear);
        // Stars and clouds keep the full Loft's spread, so the sky stays put when rooms come and go.
        PaintClouds(g, sky, frame, top, _worldWidth);
        PaintSkyline(g, sky, frame, visible);

        var drops = sky.Weather switch { Weather.Drizzle => 10, Weather.Rain => 22, Weather.Storm => 34, _ => 0 };
        var snow = sky.Weather == Weather.Snow;
        foreach (var w in visible)
        {
            var seed = (int)w.X;
            using var rain = new Pen(Color.FromArgb(sky.Weather == Weather.Drizzle ? 120 : 170, 180, 205, 240), 0.7f);
            for (var i = 0; i < drops; i++)
            {
                var y = w.Top + (Hash(i * 7 + seed) % (int)w.Height + frame * 5) % (int)w.Height;
                var x = w.Left + (Hash(i * 13 + 5 + seed) % (int)w.Width - (y - w.Top) / 3 + (int)w.Width) % (int)w.Width;
                g.DrawLine(rain, x, y, x - 1.2f, y + 4.5f);
            }
            if (snow)
                for (var i = 0; i < 16; i++)
                {
                    var y = w.Top + (Hash(i * 5 + seed) % (int)w.Height + frame) % (int)w.Height;
                    var x = w.Left + (Hash(i * 11 + seed) % (int)w.Width + (int)(Math.Sin((frame + i * 9) / 6.0) * 3) + (int)w.Width) % (int)w.Width;
                    Fill(g, Color.FromArgb(230, 245, 248, 255), x, y, 1.6f, 1.6f);
                }
        }
        if (sky.Weather == Weather.Fog)
            Fill(g, Color.FromArgb(110, Theme.Mix(Color.FromArgb(190, 196, 206), Color.FromArgb(60, 64, 80), sky.Night)), band.X, band.Y, band.Width, band.Height);
        if (sky.Weather == Weather.Storm && Lightning(frame))
        {
            Fill(g, Color.FromArgb(130, 235, 240, 255), band.X, band.Y, band.Width, band.Height);
            var w = visible[Hash(frame / 2) % visible.Length];
            var bx = w.Left + 10 + Hash(frame) % (int)(w.Width - 20);
            for (var step = 0; step < 10; step++)
            {
                bx += (Hash(frame + step) % 3 - 1) * 2.5f;
                Fill(g, Color.White, bx, w.Top + step * 4, 1.6f, 4);
            }
        }
        g.Restore(state);
    }

    private static bool Lightning(int frame) => Hash(frame / 2 * 31 + 7) % 40 == 0;

    /// <summary>
    /// The sun by day, the moon by night. Its path is spread over the windows on camera, so it always crosses one of
    /// them instead of hiding behind the wall for most of the day.
    /// </summary>
    private static void PaintCelestial(Graphics g, SkyState sky, RectangleF[] windows, float clear)
    {
        float t;
        var moon = sky.Night > 0.5f;
        if (!moon)
        {
            if (sky.SunT is < -0.03f or > 1.03f) return;
            t = Math.Clamp(sky.SunT, 0, 0.999f);
        }
        else
        {
            // Night progress from the sky's own clock: SunT runs past 1 after sunset and below 0 before sunrise.
            var day = Math.Max(60, (sky.Sunset - sky.Sunrise).TotalMinutes);
            var ratio = (float)(day / Math.Max(60, 1440 - day));
            t = Math.Clamp(sky.SunT > 1 ? (sky.SunT - 1) * ratio : 1 + sky.SunT * ratio, 0, 0.999f);
        }
        var p = t * windows.Length;
        var w = windows[(int)p];
        var local = p - (int)p;
        var size = moon ? 7f : 8f;
        var cx = w.Left + 6 + local * (w.Width - 12);
        var cy = w.Bottom - 12 - (float)Math.Sin(Math.PI * t) * (w.Height - 26);
        if (!moon)
        {
            var alpha = sky.CloudCover > 85 ? 0.35f : 1f;
            var color = sky.SunT is < 0.08f or > 0.92f ? Color.FromArgb(255, 170, 90) : Color.FromArgb(255, 236, 150);
            Fill(g, Color.FromArgb((int)(40 * alpha), color), cx - size, cy - size, size * 2, size * 2);
            Fill(g, Color.FromArgb((int)(80 * alpha), color), cx - size * 0.75f, cy - size * 0.75f, size * 1.5f, size * 1.5f);
            Fill(g, Color.FromArgb((int)(255 * alpha), color), cx - size / 2, cy - size / 2, size, size);
            return;
        }
        // Square voxel moon; the dark side follows the real phase and hemisphere.
        var a = (int)(255 * Math.Clamp((sky.Night - 0.3f) / 0.4f, 0, 1) * (0.35f + 0.65f * clear));
        var mx = cx - size / 2;
        var my = cy - size / 2;
        Fill(g, Color.FromArgb(a / 7, 255, 244, 200), mx - 4, my - 4, size + 8, size + 8);
        Fill(g, Color.FromArgb(a, 254, 252, 214), mx, my, size, size);
        var lit = (1 - Math.Cos(2 * Math.PI * sky.MoonPhase)) / 2;
        var dark = (float)Math.Round(size * (1 - lit) * 2) / 2;
        var darkOnLeft = (sky.MoonPhase < 0.5) != sky.SouthernHemisphere;
        if (dark > 0)
            Fill(g, Color.FromArgb(a, Theme.Mix(sky.Top, Color.FromArgb(254, 252, 214), 0.14f)), darkOnLeft ? mx : mx + size - dark, my, dark, size);
    }

    private static void PaintClouds(Graphics g, SkyState sky, int frame, float top, float width)
    {
        var wet = sky.Weather is Weather.Rain or Weather.Storm or Weather.Drizzle;
        var count = sky.CloudCover switch { < 15 => 3, < 40 => 7, < 70 => 11, _ => 16 };
        if (sky.Weather == Weather.Clear && sky.CloudCover < 8) count = 0;
        var baseCloud = wet || sky.Weather == Weather.Cloudy
            ? Theme.Mix(Color.FromArgb(150, 156, 170), Color.FromArgb(40, 44, 58), sky.Night)
            : Theme.Mix(Color.FromArgb(242, 245, 252), Color.FromArgb(62, 68, 92), sky.Night);
        var color = Theme.Mix(baseCloud, sky.Bottom, 0.18f);
        if (sky.CloudCover >= 80)
            for (var band = 0; band < 3; band++)
                Fill(g, Color.FromArgb(200 - band * 55, color), 0, top + band * 4, width, 4);
        string[] shape = ["...####....", ".########..", "###########", ".#########."];
        const float cell = 3f;
        var span = width + 80;
        for (var i = 0; i < count; i++)
        {
            var x = (Hash(i * 29 + 3) % (int)span + frame * (0.18f + i % 4 * 0.05f)) % span - 40;
            var y = top + 3 + Hash(i * 7 + 3) % 22;
            for (var row = 0; row < shape.Length; row++)
            for (var col = 0; col < shape[row].Length; col++)
                if (shape[row][col] == '#')
                    Fill(g, Color.FromArgb(row == shape.Length - 1 ? 190 : 235, row == 0 ? Theme.Mix(color, Color.White, 0.2f) : color),
                        (float)Math.Floor(x / cell) * cell + col * cell, y + row * cell, cell, cell);
        }
    }

    /// <summary>Two layers of blocky towers; lit windows grow with the night and switch slowly, a few rooftops blink red.</summary>
    private static void PaintSkyline(Graphics g, SkyState sky, int frame, RectangleF[] windows)
    {
        var far = sky.Night > 0.5f
            ? Theme.Mix(sky.Bottom, Color.FromArgb(22, 28, 52), 0.55f)
            : Theme.Mix(sky.Bottom, Color.FromArgb(120, 136, 162), 0.45f);
        var near = sky.Night > 0.5f
            ? Theme.Mix(sky.Bottom, Color.FromArgb(10, 13, 26), 0.82f)
            : Theme.Mix(sky.Bottom, Color.FromArgb(70, 82, 106), 0.7f);
        var lit = 8 + (int)(50 * Math.Clamp(sky.Night, 0, 1));
        foreach (var (layer, color, tall, seed) in new[] { (0, far, 30, 101), (1, near, 22, 557) })
            foreach (var w in windows)
            {
                // Each window sees the city from its own sill, scaled to its height (tall is for a 64-unit pane).
                var horizon = w.Bottom;
                var reach = Math.Max(4, (int)(tall * w.Height / 64f));
                var x = w.Left - 12 + Hash(seed + (int)w.X) % 6;
                var b = 0;
                while (x < w.Right)
                {
                    var hash = Hash(seed * 17 + (int)w.X * 3 + b++);
                    var width = 7 + hash % 9;
                    var height = 6 + hash / 9 % reach;
                    var y = horizon - height;
                    Fill(g, color, x, y, width, height);
                    if (hash % 5 == 0) Fill(g, color, x + width / 2f - 2, y - 3, 4, 3);   // stepped roof
                    if (layer == 1 && hash % 7 == 0 && height > 16)
                    {
                        Fill(g, color, x + width / 2f - 0.5f, y - 6, 1, 6);              // antenna
                        if ((frame / 6 + hash) % 3 == 0) Fill(g, Color.FromArgb(240, 255, 70, 70), x + width / 2f - 0.8f, y - 7, 1.6f, 1.6f);
                    }
                    for (var wy = y + 2; wy < horizon - 2; wy += 3.2f)
                    for (var wx = x + 1.5f; wx < x + width - 2; wx += 2.6f)
                    {
                        var cell = Hash((int)(wx * 13 + wy * 7) + seed + (int)(frame / 240));
                        if (sky.Night > 0.25f && cell % 100 < lit)
                            Fill(g, Color.FromArgb(layer == 0 ? 150 : 225, 255, 212 - cell % 40, 130), wx, wy, 1.3f, 1.5f);
                        else if (sky.Night <= 0.25f && cell % 3 == 0)
                            Fill(g, Color.FromArgb(70, 230, 240, 255), wx, wy, 1.3f, 1.5f);
                    }
                    x += width + (hash / 7 % 3);
                }
            }
    }

    // ---------------------------------------------------------------- monitors

    /// <summary>
    /// Each tool's own screen: code scrolling while it works, a blinking prompt while it waits, its own screensaver
    /// when idle, TV static without signal and a dark glass for a hidden seat. Drawn as small LCD pixels.
    /// </summary>
    private void PaintScreen(Graphics g, ProviderGeometry item, RectangleF screen, AgentStatus? agent, PauseKind pause, int frame)
    {
        var (w, h) = (Math.Max(8, (int)Math.Round(screen.Width)), Math.Max(8, (int)Math.Round(screen.Height)));
        if (!_screens.TryGetValue(item.Id, out var bmp) || bmp.Width != w || bmp.Height != h)
        {
            bmp?.Dispose();
            _screens[item.Id] = bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }
        var look = Theme.Look(item.Id);
        var slot = Array.IndexOf(Order, item.Id);
        frame += slot * 5;
        using (var sg = Graphics.FromImage(bmp))
        {
            sg.SmoothingMode = SmoothingMode.None;
            var ambient = agent is not null && AmbientProviders.Contains(item.Id) &&
                (agent.State == ActivityState.Idle || agent.ServiceHealth?.State == ServiceAvailability.Operational);
            if (agent is null) sg.Clear(Color.FromArgb(8, 10, 16));
            else if (pause == PauseKind.Weekly) PaintScreensaver(sg, item.Id, w, h, frame, look, dim: true);
            else if (ambient || agent.State == ActivityState.Idle || pause == PauseKind.FiveHour) PaintScreensaver(sg, item.Id, w, h, frame, look, dim: false);
            else if (agent.State == ActivityState.Working) PaintCode(sg, w, h, frame, slot, look);
            else if (agent.State == ActivityState.Waiting) PaintPrompt(sg, w, h, frame, look);
            else PaintStatic(sg, w, h, frame, slot);
        }
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, screen, new RectangleF(0, 0, w, h), GraphicsUnit.Pixel);
        g.Restore(state);
    }

    private static void PaintCode(Graphics sg, int w, int h, int frame, int slot, Theme.AgentLook look)
    {
        sg.Clear(Color.FromArgb(10, 12, 20));
        Fill(sg, Color.FromArgb(26, 30, 44), 0, 0, w, 4);                      // editor tab bar
        Fill(sg, look.Accent, 3, 1, 10, 2);
        var rows = (h - 7) / 4;
        for (var row = 0; row < rows; row++)
        {
            var hash = Hash((row + frame / 3) * 31 + slot * 7);
            var indent = hash % 4 * 3;
            var length = 4 + hash / 3 % (w / 2);
            if (row == rows - 1) length = Math.Max(2, length * (frame % 4 + 1) / 4); // the newest line is still being typed
            var color = (hash / 7 % 3) switch { 0 => look.Accent, 1 => look.ScreenText, _ => Color.FromArgb(206, 214, 236) };
            Fill(sg, Color.FromArgb(60, 70, 96), 2, 6 + row * 4, 2, 2);          // line numbers
            Fill(sg, color, 6 + indent, 6 + row * 4, Math.Min(length, w - 8 - indent), 2);
            var extra = hash / 11 % (w / 4);
            if (row != rows - 1 && extra > 2 && 8 + indent + length + extra < w - 2)
                Fill(sg, Color.FromArgb(92, 112, 152), 8 + indent + length, 6 + row * 4, extra, 2);
        }
    }

    private static void PaintPrompt(Graphics sg, int w, int h, int frame, Theme.AgentLook look)
    {
        sg.Clear(Color.FromArgb(10, 12, 20));
        Fill(sg, Color.FromArgb(70, 84, 116), 3, 3, w / 2, 2);
        Fill(sg, Color.FromArgb(54, 64, 92), 3, 7, w / 3, 2);
        Fill(sg, Color.FromArgb(54, 64, 92), 3, 11, w / 4, 2);
        var py = h - 10;
        for (var step = 0; step < 3; step++) // ">" prompt
        {
            Fill(sg, look.ScreenText, 3 + step, py + step, 2, 1);
            Fill(sg, look.ScreenText, 3 + step, py + 4 - step, 2, 1);
        }
        if (frame / 4 % 2 == 0) Fill(sg, look.Accent, 10, py, 4, 5);
    }

    private static void PaintStatic(Graphics sg, int w, int h, int frame, int slot)
    {
        sg.Clear(Color.FromArgb(16, 18, 26));
        for (var speck = 0; speck < w * h / 18; speck++)
        {
            var hash = Hash(speck * 17 + frame / 2 * 131 + slot);
            Fill(sg, Color.FromArgb(40 + hash % 90, 200, 206, 220), hash / 7 % w, hash / 131 % h, 2, 1);
        }
        var ink = frame / 5 % 2 == 0 ? Color.FromArgb(240, 242, 248) : Color.FromArgb(170, 176, 196);
        Fill(sg, Color.FromArgb(210, 16, 18, 26), 2, h / 2 - 11, w - 4, 21);
        PixelFont.Draw(sg, "SEM", (w - PixelFont.Measure("SEM", 1, 1)) / 2f, h / 2 - 9, 1, ink, 1);
        PixelFont.Draw(sg, "SINAL", (w - PixelFont.Measure("SINAL", 1, 1)) / 2f, h / 2, 1, ink, 1);
    }

    /// <summary>Per-workspace screensavers: what each tool shows on its monitor while nobody is typing.</summary>
    private static void PaintScreensaver(Graphics sg, string id, int w, int h, int frame, Theme.AgentLook look, bool dim)
    {
        sg.Clear(Color.FromArgb(6, 8, 14));
        var accent = dim ? Color.FromArgb(90, look.Accent) : look.Accent;
        switch (id)
        {
            case "claude":
            {
                // Spark: eight rays breathing in and out.
                var (cx, cy) = (w / 2, h / 2);
                var r = 6 + (int)(Math.Sin(frame / 4.0) * 2.5);
                for (var a = 0; a < 8; a++)
                {
                    var ang = a * Math.PI / 4 + frame / 30.0;
                    for (var s = 2; s <= r; s++)
                        Fill(sg, accent, cx + (int)Math.Round(Math.Cos(ang) * s) - 1, cy + (int)Math.Round(Math.Sin(ang) * s) - 1, 2, 2);
                }
                Fill(sg, accent, cx - 2, cy - 2, 4, 4);
                break;
            }
            case "antigravity":
            {
                // Four-point star cycling through the rainbow, with orbiting sparks.
                Color[] hues = [Color.FromArgb(66, 133, 244), Color.FromArgb(234, 67, 53), Color.FromArgb(251, 188, 5), Color.FromArgb(52, 168, 83)];
                var color = dim ? Color.FromArgb(90, hues[frame / 6 % 4]) : hues[frame / 6 % 4];
                var (cx, cy) = (w / 2, h / 2);
                var r = 9 + (int)(Math.Sin(frame / 3.0) * 2);
                for (var s = 0; s <= r; s++)
                {
                    var t = Math.Max(1, (r - s) / 3);
                    Fill(sg, color, cx - t, cy - s, t * 2, 1); Fill(sg, color, cx - t, cy + s, t * 2, 1);
                    Fill(sg, color, cx - s, cy - t, 1, t * 2); Fill(sg, color, cx + s, cy - t, 1, t * 2);
                }
                for (var i = 0; i < 3; i++)
                {
                    var ang = frame / 5.0 + i * 2.1;
                    Fill(sg, Color.FromArgb(dim ? 80 : 220, 255, 255, 255), cx + (int)(Math.Cos(ang) * 17), cy + (int)(Math.Sin(ang) * 11), 2, 2);
                }
                break;
            }
            case "figma":
            {
                // Design canvas: three frames and a cursor gliding between them.
                Color[] fills = [Color.FromArgb(242, 78, 30), Color.FromArgb(162, 89, 255), Color.FromArgb(26, 188, 254), Color.FromArgb(10, 207, 131)];
                Fill(sg, Color.FromArgb(30, 32, 40), 0, 0, w, 4);
                for (var i = 0; i < 3; i++)
                {
                    var fx = 6 + i * (w - 12) / 3;
                    var fw = (w - 12) / 3 - 4;
                    Fill(sg, Color.FromArgb(dim ? 30 : 52, 56, 70), fx, 9, fw, h - 16);
                    Fill(sg, dim ? Color.FromArgb(80, fills[i]) : fills[i], fx + 2, 11, fw - 4, 5);
                    Fill(sg, Color.FromArgb(dim ? 60 : 140, 200, 206, 220), fx + 2, 19, fw - 8, 2);
                    Fill(sg, Color.FromArgb(dim ? 60 : 140, 200, 206, 220), fx + 2, 23, fw - 12, 2);
                }
                var t = frame % 60 / 60.0 * Math.PI * 2;
                var px = (int)(w / 2 + Math.Cos(t) * (w / 2 - 12));
                var py = (int)(h / 2 + Math.Sin(t * 2) * (h / 4));
                for (var row = 0; row < 5; row++) Fill(sg, Color.White, px, py + row, Math.Max(1, 5 - row), 1);
                Fill(sg, dim ? Color.FromArgb(80, fills[3]) : fills[3], px + 4, py + 5, 8, 3);
                break;
            }
            case "openrouter":
            {
                // Routing graph: a pulse leaves the hub and visits each model in turn.
                var hub = new Point(w / 2, h / 2);
                Point[] nodes = [new(8, 8), new(w - 9, 8), new(8, h - 9), new(w - 9, h - 9)];
                using var edge = new Pen(Color.FromArgb(dim ? 40 : 90, look.Accent), 1);
                foreach (var n in nodes) sg.DrawLine(edge, hub, n);
                foreach (var n in nodes) Fill(sg, Color.FromArgb(dim ? 70 : 200, 220, 214, 255), n.X - 2, n.Y - 2, 4, 4);
                Fill(sg, accent, hub.X - 3, hub.Y - 3, 6, 6);
                var target = nodes[frame / 12 % 4];
                var t = frame % 12 / 12f;
                Fill(sg, Color.White, hub.X + (target.X - hub.X) * t - 1, hub.Y + (target.Y - hub.Y) * t - 1, 3, 3);
                break;
            }
            default:
            {
                // Classic bouncing block with a trail.
                Fill(sg, Color.FromArgb(70, accent), Bounce(frame - 2, w - 10) + 1, Bounce((frame - 2) * 7 / 10, h - 7) + 1, 8, 5);
                Fill(sg, accent, Bounce(frame, w - 10) + 1, Bounce(frame * 7 / 10, h - 7) + 1, 8, 5);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- bubbles over the personas

    /// <summary>A few "z" drifting up beside the head.</summary>
    private static void DrawSleep(Graphics g, float x, float y, int frame, float size)
    {
        var count = frame / 6 % 4;
        string[] glyph = ["####", "..#.", ".#..", "####"];
        for (var i = 0; i < count; i++)
        {
            var cell = size + i * size * 0.35f;
            var ox = x + i * size * 4.5f;
            var oy = y - i * size * 5.5f;
            for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
                if (glyph[row][col] == '#')
                {
                    Fill(g, Color.FromArgb(150, 10, 12, 24), ox + col * cell + cell * 0.35f, oy + row * cell + cell * 0.35f, cell, cell);
                    Fill(g, Color.FromArgb(226, 236, 255), ox + col * cell, oy + row * cell, cell, cell);
                }
        }
    }

    /// <summary>Waiting: a small thought bubble with three dots taking turns.</summary>
    private static void DrawThinking(Graphics g, float x, float y, int frame, Color accent)
    {
        var ink = Color.FromArgb(12, 13, 22);
        var paper = Color.FromArgb(242, 244, 250);
        Fill(g, ink, x + 2, y, 32, 18);
        Fill(g, ink, x, y + 2, 36, 14);
        Fill(g, paper, x + 2, y + 2, 32, 14);
        Fill(g, ink, x + 1, y + 19, 6, 5);
        Fill(g, paper, x + 2.5f, y + 20.5f, 3, 2);
        for (var dot = 0; dot < 3; dot++)
            Fill(g, dot == frame / 3 % 3 ? accent : Color.FromArgb(120, 126, 146), x + 6.5f + dot * 8.5f, y + 7, 5, 5);
    }

    /// <summary>
    /// Pixel plaque over the seat. Weekly: the chair is empty and the sign says when the persona returns.
    /// 5 hours: the persona stays on a break and a smaller tag says until when. Countdown in the last hour.
    /// </summary>
    private static void PaintPauseSign(Graphics g, ProviderGeometry item, QuotaPause pause, string id, DateTimeOffset now,
        RectangleF source, RectangleF destination)
    {
        var factor = destination.Width / source.Width;
        var weekly = pause.Kind == PauseKind.Weekly;
        var scale = Math.Max(1f, MathF.Round((weekly ? 2.5f : 2f) * factor * 2) / 2);
        var lines = pause.SignLines(now);
        var widths = lines.Select(l => PixelFont.Measure(l, scale, bold: true)).ToArray();
        var pad = 3 * scale;
        var w = widths.Max() + pad * 2;
        var lineH = PixelFont.Height(scale) + 2 * scale;
        var h = lineH * lines.Length + pad * 2 - 2 * scale;
        var anchor = new PointF(destination.Left + (item.Head.X - source.X) * factor,
            destination.Top + (item.Head.Y - source.Y) * destination.Height / source.Height);
        // Weekly sits where the head would be (the seat is empty); the 5-hour tag floats just above the head.
        var x = (float)Math.Round(Math.Clamp(anchor.X - w / 2, destination.Left + 2, destination.Right - w - 2));
        var y = (float)Math.Round(Math.Clamp(weekly ? anchor.Y - h / 2 : anchor.Y - h - 6 * factor, destination.Top + 2, destination.Bottom - h - 2));
        var accent = Theme.Look(id).Accent;
        var urgent = pause.Until is { } until && until - now <= TimeSpan.FromHours(1);
        using (var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(shadow, x + scale, y + scale, w, h);
        using (var fill = new SolidBrush(Color.FromArgb(235, 10, 13, 24))) g.FillRectangle(fill, x, y, w, h);
        using (var edge = new Pen(weekly ? accent : Theme.Amber, Math.Max(1, scale))) g.DrawRectangle(edge, x, y, w, h);
        for (var i = 0; i < lines.Length; i++)
        {
            var color = i == 0 ? Theme.Label : urgent ? Theme.Amber : Color.FromArgb(244, 246, 250);
            PixelFont.Draw(g, lines[i], x + (w - widths[i]) / 2, y + pad + i * lineH, scale, color, bold: true);
        }
    }

    private string PoseFor(AgentStatus agent, PauseKind pause, int frame, int slot)
    {
        var tick = frame + slot * 17;
        if (pause == PauseKind.Weekly) return "chair";
        // 5-hour limit: on a break at the desk (coffee, a sip, a stretch) until the window renews; never typing.
        if (pause == PauseKind.FiveHour) return tick % 120 < 90 ? (tick % 37 < 7 ? "sip" : "coffee") : "stretch";
        var ambient = AmbientProviders.Contains(agent.Id) &&
            (agent.State == ActivityState.Idle || agent.ServiceHealth?.State == ServiceAvailability.Operational);
        if (ambient)
        {
            if (_poses.ContainsKey(agent.Id + ":ambient-a"))
                return tick / 6 % 2 == 0 ? "ambient-a" : _poses.ContainsKey(agent.Id + ":ambient-b") ? "ambient-b" : "ambient-a";
            // Fallback with the existing art: watching the screen and blinking, a sip now and then, a stretch; no sleeping.
            var t = tick % 200;
            return t < 6 ? "blink" : t < 90 ? "look" : t < 96 ? "blink" : t < 150 ? (tick % 29 < 6 ? "sip" : "coffee") : t < 162 ? "stretch" : "look";
        }
        return agent.State switch
        {
            ActivityState.Working when tick % 170 < 8 => "look",
            ActivityState.Working when tick % 31 < 3 => "blink",
            ActivityState.Working => tick / 4 % 2 == 0 ? "type-a" : "type-b",
            ActivityState.Waiting when tick % 79 < 5 => "blink",
            ActivityState.Waiting => "wait",
            ActivityState.Idle when tick % 220 < 160 => tick % 37 < 7 ? "sip" : "coffee",
            ActivityState.Idle when tick % 220 < 173 => "stretch",
            ActivityState.Idle => "sleep",
            _ => tick / 11 % 2 == 0 ? "nosig-a" : "nosig-b"
        };
    }

    private static void Fill(Graphics g, Color color, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0 || color.A == 0) return;
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

    private static PointF ReadPoint(JsonElement element) => new(element[0].GetSingle(), element[1].GetSingle());

    private static RectangleF ReadRect(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

    private static Bitmap ReadBitmap(Assembly assembly, string suffix)
    {
        using var stream = FindResource(assembly, suffix);
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static Bitmap? TryReadBitmap(Assembly assembly, string suffix) =>
        assembly.GetManifestResourceNames().Any(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ? ReadBitmap(assembly, suffix) : null;

    private static Stream FindResource(Assembly assembly, string suffix)
    {
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Missing embedded office art resource: " + suffix);
        return assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException("Cannot open embedded office art resource: " + suffix);
    }

    private void DisposeAssets()
    {
        _background?.Dispose();
        _background = null;
        _layer?.Dispose();
        _layer = null;
        _strip = null;
        foreach (var image in _poses.Values) image.Dispose();
        _poses.Clear();
        foreach (var image in _screens.Values) image.Dispose();
        _screens.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeAssets();
        _disposed = true;
    }
}
