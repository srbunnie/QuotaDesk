using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AIUsageMonitor;

/// <summary>
/// A 189x66 pixel-art office scaled 3x with nearest-neighbour, modelled on the QuotaDesk reference art:
/// Codex's desk on the left, Claude's on the right, a big night/day window, server tower and a sleeping cat in the
/// middle, shelves, framed pictures and posters on the walls. The window sky follows the local clock and the real
/// weather. Character animations map to real states: Working = typing + scrolling code, Waiting = speech bubble +
/// blinking prompt, Idle = asleep with "z", Unknown = empty chair and monitor off.
/// </summary>
internal sealed class PixelOffice : IDisposable
{
    public const int W = 285, H = 66, FloorY = 54, Zoom = 3;
    private const int GX0 = 74, GX1 = 116, GY0 = 4, GY1 = 36, MullionX = 94;
    public static readonly Rectangle LeftPoster = new(2, 3, 18, 27);
    public static readonly Rectangle RightPoster = new(167, 3, 18, 27);

    private readonly Bitmap _canvas = new(W, H, PixelFormat.Format32bppArgb);
    private readonly int[] _bgPixels = new int[W * H];
    private readonly int[] _px = new int[W * H];
    private readonly bool[] _skyMask = new bool[W * H];
    private string _bgKey = "";
    /// <summary>Last known weekly return time per tool, to notice when it arrives.</summary>
    private readonly Dictionary<string, DateTimeOffset> _returnDue = new();

    // ---------------------------------------------------------------- sprites (facing right, 24 wide)
    // Transcribed from the reference art. k outline, h/H/d hair, s/S skin, e eye, j/J/L shirt, p/P pants,
    // b/B shoes, a/A/q headphones. Heads sit on top of the shared body; the chair is drawn separately.
    private static readonly string[] CodexHead =
    [
        ".........qAAAAq.........",
        ".......qAAq.kkkkk.......",
        "......qAa.khhhhhhk......",
        "......aA.khhhhhhhk......",
        ".....aA.khhhhhhhhhk.....",
        ".....aa.khhhhHHHhk......",
        ".....aa.khhhSsssSk......",
        "...qaAAAqhhSsssssk......",
        "...qaAAAaqSsssessk......",
        "...qaAAAaqSsssesssk.....",
        "....qaAAaqSssssssk......",
        ".....qaaqkSssssssk......",
        ".........jjSsssSk.......",
    ];
    private static readonly string[] ClaudeHead =
    [
        "........................",
        ".........kkkkkkk........",
        ".......kkhhhHHhhhk......",
        "......khhhhhHHhhhhhk....",
        ".....khhhhhhhhhHhhhhk...",
        "....kdhhhhhhhhhhhhhhhk..",
        "....kddhhhhhhhhsshhhhk..",
        "....kddhhhhhhhssssshhk..",
        "....kdddhhhhSsssssssk...",
        ".....kddhhhSSsssesssk...",
        ".....kddhhhhSsssessssk..",
        "......kddhhSSssssssssk..",
        "......kddhhSssssssssk...",
        ".......kddhSsssssk......",
        "........kkkSSsssk.......",
    ];
    // Short swept hair, cropped sides and a light bomber jacket distinguish the third character.
    private static readonly string[] AntigravityHead =
    [
        ".........kk.k...........",
        "........khhkhk..........",
        ".......khhHHhhkk........",
        "......khhHHhhhhhkk......",
        ".....khhHHhhhhhhhk......",
        ".....kdhhhhhhhhhhk......",
        ".....kddhhhhSSssSk......",
        ".....kdddSSssssssk......",
        "......kddSsssesssk......",
        "......kddSsssessssk.....",
        ".......kdSsssssssk......",
        "........kSSssssSk.......",
        ".........jjSsssSk.......",
    ];
    private static readonly string[] BodyRows =
    [
        ".......kjjjjjJk.........",
        "......kLjjjjjjJk........",
        "......kLjjjjjjJk........",
        "......kLjjjjJjjk........",
        "......kjjjjjJJjk........",
        "......kjjjjJjjjJk.......",
        "......kjjjjJjjjJssssssSk",
        "......kjjjJjjjjkssssssSk",
        "......kjjjjJjjjkkkkkkkk.",
        ".......kJjjjJk..........",
        ".......kJjjjjJk.........",
        ".......kpppppppppk......",
        ".......kPPPPPPPPPPk.....",
        ".......kkkkkkkkkPpk.....",
        "...............kPpk.....",
        "...............kPpk.....",
        "...............kPpk.....",
        "...............kPpk.....",
        "..............kbbbbk....",
        "..............kBbbbbk...",
    ];
    private static readonly (int Row, string Line)[] TypeB = [(19, "......kjjjjJjjjJ.sssssSk"), (20, "......kjjjJjjjjkssssssSk")];
    // Relaxed pose (waiting / idle): upright torso, arm hanging by the side, hand resting on the thigh.
    private static readonly (int Row, string Line)[] Rest =
    [
        (16, "......kLjjjjJjjjjk......"), (17, "......kjjjjjJjjjjk......"), (18, "......kjjjjjJjjjjk......"),
        (19, "......kjjjjjJjjjk......."), (20, "......kjjjjjJjjjk......."), (21, "......kjjjjjJjjjk......."),
        (22, ".......kJjjjjJjk........"), (23, ".......kJjjjjSsk........"), (24, ".......kppppSssppk......"),
    ];

    // Holding something in front of the chest (reading / playing): forearm bent up, hands at row 20.
    private static readonly (int Row, string Line)[] Hold =
    [
        (16, "......kLjjjjJjjk........"), (17, "......kjjjjjJjjk........"), (18, "......kjjjjjJjjk........"),
        (19, "......kjjjjJJjjJssk....."), (20, "......kjjjJjjjjJssssk..."), (21, "......kjjjjjjjjkkkkkk..."),
        (22, ".......kJjjjjJjk........"), (23, ".......kJjjjjJjk........"),
    ];

    private enum IdleActivity { Sleep, Read, Ships, Snake, Coffee }
    private readonly Dictionary<int, (IdleActivity Activity, DateTime Until)> _activities = new();

    /// <summary>Idle characters play a computer game for 35–85 seconds before switching games.</summary>
    private IdleActivity ActivityFor(int index)
    {
        if (ForcedIdleActivity is { } forced) return (forced + index) % 2 == 0 ? IdleActivity.Ships : IdleActivity.Snake;
        if (_activities.TryGetValue(index, out var current) && DateTime.Now < current.Until) return current.Activity;
        var next = Random.Shared.Next(2) == 0 ? IdleActivity.Ships : IdleActivity.Snake;
        if (_activities.ContainsKey(index)) next = current.Activity == IdleActivity.Ships ? IdleActivity.Snake : IdleActivity.Ships;
        _activities[index] = (next, DateTime.Now.AddSeconds(Random.Shared.Next(35, 85)));
        return next;
    }

    /// <summary>Previews only: pins the idle activity cycle.</summary>
    internal static long? ForcedIdleActivity { get; set; }

    public static readonly string[] Cat =
    [
        ".kk...kk...............",
        "kcck.kcck..............",
        "kccckccck...kkkkkkkk...",
        "kcdcccdcck.kccccxcccck.",
        "kwwwnwwwcckccxcccccxcck",
        ".kwwwwwcccccccccccccck.",
        "..kkkkkkkkkkkkkkkkkkk..",
    ];

    public static Color? CatPalette(char c) => c switch
    {
        'k' => Color.FromArgb(12, 12, 20),
        'c' => Color.FromArgb(120, 124, 142),
        'x' => Color.FromArgb(82, 86, 104),
        'd' => Color.FromArgb(30, 30, 44),
        'w' => Color.FromArgb(228, 230, 238),
        'n' => Color.FromArgb(255, 140, 170),
        _ => null
    };

    private static readonly Color Outline = Color.FromArgb(10, 11, 18);
    private static readonly Color Leaf = Color.FromArgb(70, 170, 70);
    private static readonly Color LeafDark = Color.FromArgb(34, 112, 52);
    private static readonly Color LeafLight = Color.FromArgb(130, 210, 100);

    /// <summary>Left edge (canvas px) of the character of tool <paramref name="index"/>.</summary>
    public static int StationX(int index, int count)
    {
        if (count == 2) return index == 0 ? 17 : 122;
        if (count == 3) return index switch { 0 => 17, 1 => 122, _ => 218 };
        var slot = W / Math.Max(1, count);
        return index * slot + Math.Max(4, (slot - 56) / 2);
    }

    public static float StationCenter(int index, int count) => StationX(index, count) + 28f;

    public void Render(Graphics g, Rectangle dest, IReadOnlyList<AgentStatus> agents, int frame, SkyState sky)
    {
        var key = sky.Key + "|" + frame / 12;
        if (key != _bgKey)
        {
            BuildBackground(sky, frame / 12);
            _bgKey = key;
        }
        Array.Copy(_bgPixels, _px, _px.Length);
        DrawWeather(sky, frame);
        if (agents.Count > 2) DrawWeather(sky, frame, 190);
        DrawServerLeds(frame, agents.Any(a => a.State == ActivityState.Working));
        for (var i = 0; i < agents.Count; i++) DrawStation(agents[i], i, StationX(i, agents.Count), frame + i * 5);
        DrawCat(frame);

        var data = _canvas.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < H; row++)
                System.Runtime.InteropServices.Marshal.Copy(_px, row * W, data.Scan0 + row * data.Stride, W);
        }
        finally { _canvas.UnlockBits(data); }
        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_canvas, dest, new Rectangle(0, 0, agents.Count > 2 ? W : 189, H), GraphicsUnit.Pixel);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;
    }

    // ================================================================ workstations

    private void DrawCat(int frame)
    {
        DrawSprite(Cat, 93, FloorY + 1, CatPalette);
        DrawZ(104, FloorY - 1, frame + 3, Color.FromArgb(232, 234, 246));
    }
    /// <summary>When the weekly quota is spent: the date it comes back. Null while weekly quota remains or no reset time is known.</summary>
    private static DateTimeOffset? WeeklyReturn(AgentStatus agent)
    {
        var now = DateTimeOffset.Now;
        return agent.Quota.Windows.FirstOrDefault(w => w.Kind == WindowKind.Weekly && w.UsedRounded >= 100 && w.ResetsAt is { } r && r > now)?.ResetsAt;
    }

    /// <summary>Sign shown once the return time has passed: a wifi icon whose arcs light up one by one while a new reading is requested.</summary>
    private void DrawConnectingSign(int x, int y, Color accent, int frame)
    {
        Rect(x, y, 32, 17, Outline);
        Rect(x + 1, y + 1, 30, 15, Color.FromArgb(232, 236, 222));
        Rect(x + 2, y + 14, 28, 1, accent);
        var step = frame / 3 % 5;   // 0 = dot only ... 3 = all arcs, 4 = blink
        var on = Color.FromArgb(38, 48, 42);
        var off = Color.FromArgb(196, 202, 190);
        var cx = x + 16;
        var by = y + 12;
        Rect(cx - 1, by, 2, 2, step == 4 ? off : on);
        for (var arc = 1; arc <= 3; arc++)
        {
            var r = arc * 3 + 1;
            var color = step >= arc && step < 4 ? on : off;
            for (var dy = -r; dy < 0; dy++)
            for (var dx = -r; dx <= r; dx++)
                if (-dy >= Math.Abs(dx) && (int)Math.Round(Math.Sqrt(dx * dx + dy * dy)) == r) Px(cx + dx, by + dy, color);
        }
    }

    private void DrawAwaySign(int x, int y, Color accent, DateTimeOffset? returnsAt = null)
    {
        var extra = returnsAt is null ? 0 : 6;   // third line: the time
        y -= extra;
        Rect(x, y, 32, 17 + extra, Outline);
        Rect(x + 1, y + 1, 30, 15 + extra, Color.FromArgb(232, 236, 222));
        Rect(x + 2, y + 14 + extra, 28, 1, accent);
        string[] words = returnsAt is { } back
            // Invariant: in a custom format '/' and ':' become the culture's separators ('.' or '-'), which have no glyph.
            ? [L.T("VOLTO", "BACK"), back.ToLocalTime().ToString(L.DayMonth, System.Globalization.CultureInfo.InvariantCulture),
                back.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)]
            : [L.T("JA", "BACK"), L.T("VOLTO", "SOON")];
        var letters = new Dictionary<char, string[]>
        {
            ['J'] = ["..###", "...#.", "...#.", "#..#.", ".##.."],
            ['A'] = [".###.", "#...#", "#####", "#...#", "#...#"],
            ['V'] = ["#...#", "#...#", "#...#", ".#.#.", "..#.."],
            ['O'] = [".###.", "#...#", "#...#", "#...#", ".###."],
            ['L'] = ["#....", "#....", "#....", "#....", "#####"],
            ['T'] = ["#####", "..#..", "..#..", "..#..", "..#.."],
            [':'] = [".....", "..#..", ".....", "..#..", "....."],
            ['/'] = ["....#", "...#.", "..#..", ".#...", "#...."],
            ['0'] = [".###.", "#..##", "#.#.#", "##..#", ".###."],
            ['1'] = ["..#..", ".##..", "..#..", "..#..", ".###."],
            ['2'] = [".###.", "#...#", "..##.", ".#...", "#####"],
            ['3'] = ["####.", "....#", ".###.", "....#", "####."],
            ['4'] = ["#..#.", "#..#.", "#####", "...#.", "...#."],
            ['5'] = ["#####", "#....", "####.", "....#", "####."],
            ['6'] = [".###.", "#....", "####.", "#...#", ".###."],
            ['7'] = ["#####", "....#", "...#.", "..#..", "..#.."],
            ['8'] = [".###.", "#...#", ".###.", "#...#", ".###."],
            ['9'] = [".###.", "#...#", ".####", "....#", ".###."]
        };
        for (var line = 0; line < words.Length; line++)
        {
            var start = x + (32 - (words[line].Length * 6 - 1)) / 2;
            for (var i = 0; i < words[line].Length; i++)
                if (letters.TryGetValue(words[line][i], out var glyph))
                    DrawSprite(glyph, start + i * 6, y + 2 + line * 6,
                    c => c == '#' ? Color.FromArgb(38, 48, 42) : (Color?)null);
        }
    }
    private void DrawStation(AgentStatus agent, int index, int ox, int frame)
    {
        // Weekly quota spent: the desk is empty with a "VOLTO dd/MM" sign until the quota renews.
        var returnsAt = WeeklyReturn(agent);
        var connecting = false;
        if (returnsAt is { } due) _returnDue[agent.Id] = due;
        else if (_returnDue.TryGetValue(agent.Id, out var passed))
        {
            // The return time arrived: keep the chair empty and "request connection" until a new reading shows quota again.
            if (agent.Quota.Find(WindowKind.Weekly) is { UsedRounded: < 100 }) _returnDue.Remove(agent.Id);
            else connecting = DateTimeOffset.Now >= passed;
        }
        if (returnsAt is not null || connecting) agent = agent with { State = ActivityState.Unknown };
        var resting = agent.State is ActivityState.Idle or ActivityState.Waiting;
        var activity = resting ? ActivityFor(index) : IdleActivity.Sleep;
        if (!resting) _activities.Remove(index);
        var look = Theme.Look(agent.Id);
        var state = agent.State;
        var ground = FloorY + 7;
        var top = ground - 34;          // chair/character origin: wheels rest on the ground row
        var deskX = ox + 17;
        var deskY = top + 22;           // desk top just under the hands
        var claude = agent.Id == "claude";

        // Chair: tall backrest curving back towards the seat, bright seat, five-star base
        var chair = look.Chair;
        var chairLight = Theme.Mix(chair, Color.White, 0.3f);
        var chairDark = Theme.Mix(chair, Outline, 0.45f);
        for (var r = 8; r < 27; r++)
        {
            var off = r < 16 ? 0 : r < 21 ? 1 : 2;
            Px(ox + off, top + r, chairLight);
            Px(ox + off + 1, top + r, chair);
            Px(ox + off + 2, top + r, chairDark);
        }
        Rect(ox + 3, top + 27, 13, 1, chair);
        Rect(ox + 3, top + 28, 13, 1, chairDark);
        Rect(ox + 9, top + 29, 2, 4, Color.FromArgb(44, 48, 62));
        Rect(ox + 4, top + 33, 12, 1, Color.FromArgb(44, 48, 62));
        foreach (var wx in new[] { ox + 4, ox + 10, ox + 15 }) Px(wx, top + 34, Outline);

        // Desk with a drawer pedestal on the right
        var desk = Color.FromArgb(58, 64, 86);
        var deskTop = Color.FromArgb(96, 104, 132);
        var deskDark = Color.FromArgb(36, 40, 56);
        Rect(deskX, deskY, 40, 3, desk);
        Rect(deskX, deskY, 40, 1, deskTop);
        Rect(deskX + 1, deskY + 3, 2, ground - deskY - 3, deskDark);
        Rect(deskX + 28, deskY + 3, 11, ground - deskY - 3, desk);
        Rect(deskX + 28, deskY + 3, 1, ground - deskY - 3, deskTop);
        for (var dy = deskY + 6; dy < ground; dy += 4)
        {
            Rect(deskX + 29, dy, 10, 1, deskDark);
            Rect(deskX + 32, dy - 2, 4, 1, claude ? look.Accent : Color.FromArgb(128, 136, 160));
        }

        // Monitor (dark bezel, black screen) on a stand
        var mx = deskX + 10;
        var my = top + 1;
        Rect(mx, my, 24, 15, Color.FromArgb(40, 44, 58));
        Rect(mx, my, 24, 1, Color.FromArgb(74, 80, 102));
        Rect(mx, my + 14, 24, 1, Color.FromArgb(28, 30, 42));
        Rect(mx + 10, my + 15, 4, deskY - my - 16, Color.FromArgb(36, 40, 54));
        Rect(mx + 7, deskY - 1, 10, 1, Color.FromArgb(62, 68, 88));
        DrawScreen(agent, mx + 2, my + 2, 20, 11, frame, look);
        if (resting && activity is IdleActivity.Ships or IdleActivity.Snake)
            DrawGame(mx + 2, my + 2, frame, activity, look.Accent);

        // Keyboard
        var keyFlash = state == ActivityState.Working && frame % 2 == 0;
        Rect(deskX + 1, deskY - 1, 8, 1, Color.FromArgb(84, 90, 116));
        if (keyFlash) Px(deskX + 2 + frame / 2 % 6, deskY - 1, Theme.Mix(Color.FromArgb(84, 90, 116), look.Accent, 0.7f));

        // Mug; Claude also has the desk lamp, Codex a small plant by the window
        if (claude)
        {
            Rect(deskX + 26, deskY - 4, 3, 4, look.Accent);
            Rect(deskX + 26, deskY - 4, 3, 1, Theme.Mix(look.Accent, Color.White, 0.35f));
            Px(deskX + 29, deskY - 3, look.Accent); Px(deskX + 29, deskY - 2, look.Accent);
            DrawLamp(deskX + 35, deskY);
        }
        else
        {
            Rect(deskX + 30, deskY - 4, 3, 4, Color.FromArgb(232, 236, 244));
            Rect(deskX + 30, deskY - 3, 3, 1, look.Accent);
            Px(deskX + 33, deskY - 3, Color.FromArgb(232, 236, 244)); Px(deskX + 33, deskY - 2, Color.FromArgb(232, 236, 244));
        }
        if (state == ActivityState.Idle && (frame / 3) % 4 != 0)
        {
            var sx = claude ? deskX + 27 : deskX + 31;
            Px(sx + (frame / 6) % 2, deskY - 5 - (frame / 3) % 2, Color.FromArgb(90, 220, 230, 255));
        }

        if (agent.Id == "antigravity")
        {
            Rect(ox + 5, top + 23, 2, 1, Color.FromArgb(66, 133, 244));
            Rect(ox + 7, top + 23, 2, 1, Color.FromArgb(234, 67, 53));
            Rect(ox + 9, top + 23, 2, 1, Color.FromArgb(251, 188, 5));
        }
        if (state == ActivityState.Unknown)
        {
            if (connecting) DrawConnectingSign(deskX + 1, deskY - 17, look.Accent, frame);
            else if (returnsAt is not null || agent.Id == "antigravity") DrawAwaySign(deskX + 1, deskY - 17, look.Accent, returnsAt);
            // Keep the provider present even when activity is unavailable.
        }

        // Character
        var head = agent.Id == "antigravity" ? AntigravityHead : look.Headphones ? CodexHead : ClaudeHead;
        var rows = head.Concat(BodyRows.Skip(head.Length - 13)).ToArray();
        if (state == ActivityState.Working && (frame / 2) % 2 == 1) Apply(rows, TypeB);
        if (resting && activity == IdleActivity.Sleep) Apply(rows, Rest);
        if (resting && activity is IdleActivity.Read or IdleActivity.Coffee) Apply(rows, Hold);
        if (resting && activity is IdleActivity.Ships or IdleActivity.Snake && frame % 4 < 2) Apply(rows, TypeB);
        if (resting && activity == IdleActivity.Sleep) rows = Asleep(rows, head.Length);
        var blink = state == ActivityState.Waiting && frame % 22 == 0;
        DrawSprite(rows, ox, top + 1, c => c switch
        {
            'k' => Outline,
            'h' => look.Hair,
            'H' => look.HairLight,
            'd' => Theme.Mix(look.Hair, Outline, 0.3f),
            's' => look.Skin,
            'S' => look.SkinShade,
            'e' => blink ? look.SkinShade : Outline,
            'j' => look.Jacket,
            'J' => look.JacketDark,
            'L' => Theme.Mix(look.Jacket, Color.White, 0.3f),
            'p' => Color.FromArgb(34, 38, 56),
            'P' => Color.FromArgb(26, 28, 42),
            'b' => Color.FromArgb(16, 16, 24),
            'B' => Color.FromArgb(56, 58, 76),
            'a' => look.Accent,
            'A' => Theme.Mix(look.Accent, Color.White, 0.55f),
            'q' => Theme.Mix(look.Accent, Outline, 0.5f),
            _ => null
        });

        if (agent.Id == "antigravity")
        {
            // Google color badge on the jacket, and red sneaker trim.
            Px(ox + 8, top + 18, Color.FromArgb(66, 133, 244));
            Px(ox + 9, top + 18, Color.FromArgb(234, 67, 53));
            Px(ox + 8, top + 19, Color.FromArgb(251, 188, 5));
            Px(ox + 9, top + 19, look.Accent);
            Rect(ox + 15, top + 32, 3, 1, Color.FromArgb(234, 67, 53));
        }

        switch (state)
        {
            case ActivityState.Working:
                if (frame % 3 == 0) Px(deskX + 2 + frame % 5, deskY - 3 - frame % 2, Color.FromArgb(200, look.Accent));
                break;
            case ActivityState.Waiting:
            case ActivityState.Idle:
                if (activity == IdleActivity.Sleep) DrawZ(ox + 15, top + 4, frame, Color.FromArgb(232, 234, 246));
                if (activity == IdleActivity.Read) DrawBook(ox + 16, top + 15, frame, look);
                if (activity == IdleActivity.Coffee)
                {
                    var cy = top + (frame % 48 < 18 ? 11 : 17);
                    Rect(ox + 17, cy + 2, 4, 2, look.Skin);
                    Rect(ox + 19, cy, 4, 4, Color.FromArgb(240, 236, 222));
                    Rect(ox + 19, cy + 1, 3, 2, look.Accent);
                    Px(ox + 23, cy + 1, Color.White); Px(ox + 23, cy + 2, Color.White);
                    Px(ox + 20 + frame / 4 % 2, cy - 2 - frame / 5 % 2, Color.FromArgb(156, 166, 184));
                }
                break;
        }
    }

    private void DrawGame(int x, int y, int frame, IdleActivity activity, Color accent)
    {
        Rect(x, y, 20, 11, Color.FromArgb(4, 10, 20));
        if (activity == IdleActivity.Ships)
        {
            for (var i = 0; i < 8; i++) Px(x + (i * 7) % 20, y + (i * 3 + frame / 3) % 11, Color.FromArgb(72, 86, 118));
            var ship = 3 + frame / 3 % 13;
            Rect(x + ship - 1, y + 9, 3, 1, accent); Px(x + ship, y + 8, Color.White);
            Px(x + ship, y + 7 - frame % 7, Color.FromArgb(255, 228, 120));
            for (var i = 0; i < 3; i++) { var ex = x + 2 + i * 6 + frame / 8 % 2; Rect(ex, y + 2, 3, 1, Theme.Orange); Px(ex + 1, y + 3, Theme.Orange); }
        }
        else
        {
            var path = new List<(int X, int Y)>();
            for (var i = 2; i <= 17; i++) path.Add((i, 2));
            for (var i = 3; i <= 8; i++) path.Add((17, i));
            for (var i = 16; i >= 2; i--) path.Add((i, 8));
            for (var i = 7; i >= 3; i--) path.Add((2, i));
            for (var i = 0; i < 13; i++) { var p = path[(frame / 2 + i) % path.Count]; Px(x + p.X, y + p.Y, i == 12 ? Color.White : Color.FromArgb(90, 230, 142)); }
            Px(x + 10, y + 5, Theme.Orange);
        }
    }

    private static void Apply(string[] rows, (int Row, string Line)[] changes)
    {
        foreach (var (row, line) in changes) rows[row] = line;
    }

    /// <summary>An open book held up in front of the character; now and then a page turns.</summary>
    private void DrawBook(int x, int y, int frame, Theme.AgentLook look)
    {
        var page = Color.FromArgb(240, 236, 222);
        var shade = Color.FromArgb(206, 200, 184);
        var line = Color.FromArgb(150, 150, 150);
        var cover = look.Headphones ? Color.FromArgb(200, 60, 50) : Color.FromArgb(40, 110, 210);
        Rect(x, y, 8, 5, page);
        Rect(x + 4, y, 1, 5, shade);                         // spine
        for (var r = 1; r < 4; r++) { Rect(x + 1, y + r, 2, 1, line); Rect(x + 5, y + r, 2, 1, line); }
        Rect(x - 1, y + 5, 10, 1, cover);                    // cover edge
        Px(x - 1, y + 4, cover); Px(x + 8, y + 4, cover);
        if (frame % 48 < 3)                                  // page turning
        {
            Rect(x + 5, y - 1, 3, 1, page);
            Rect(x + 5, y, 3, 4, Color.FromArgb(252, 250, 240));
        }
        Rect(x - 1, y + 5, 2, 1, look.Skin);                 // hands holding the covers
        Rect(x + 7, y + 5, 2, 1, look.Skin);
    }

    /// <summary>A handheld game console with a flickering screen and thumbs on the buttons.</summary>
    private void DrawHandheld(int x, int y, int frame, Theme.AgentLook look)
    {
        var body = Color.FromArgb(70, 76, 100);
        var bodyLight = Color.FromArgb(110, 118, 146);
        Rect(x, y, 9, 4, body);
        Rect(x, y, 9, 1, bodyLight);
        Rect(x + 3, y + 1, 3, 2, Color.FromArgb(10, 20, 20));
        var hue = (frame / 3) % 3;
        var glow = hue switch { 0 => Color.FromArgb(80, 240, 160), 1 => Color.FromArgb(90, 200, 255), _ => Color.FromArgb(255, 210, 90) };
        Px(x + 3 + (frame / 2) % 3, y + 1 + (frame / 4) % 2, glow);
        Px(x + 3 + (frame / 3 + 1) % 3, y + 2, Theme.Mix(glow, Color.White, 0.3f));
        Px(x + 1, y + 2, Color.FromArgb(40, 44, 60));        // d-pad
        Px(x + 7, y + 1, Color.FromArgb(230, 70, 60));       // buttons
        Px(x + 7, y + 2, look.Accent);
        var press = frame % 4 < 2;
        Rect(x - 1, y + 2, 2, 2, look.Skin);                 // thumbs
        Rect(x + 8, y + (press ? 1 : 2), 2, 2, look.Skin);
        // Screen light on the face
        Px(x + 2, y - 2, Color.FromArgb(40, glow));
        Px(x + 3, y - 3, Color.FromArgb(30, glow));
    }

    /// <summary>Head drops two rows and the eyes close.</summary>
    private static string[] Asleep(string[] rows, int headRows)
    {
        var result = (string[])rows.Clone();
        var blank = new string('.', rows[0].Length);
        result[0] = result[1] = blank;
        for (var i = 0; i < headRows - 2; i++) result[i + 2] = rows[i];
        var firstEye = true;
        for (var i = 0; i < headRows; i++)
        {
            if (result[i].IndexOf('e') < 0) continue;
            result[i] = result[i].Replace('e', firstEye ? 'k' : 's');
            firstEye = false;
        }
        return result;
    }

    /// <summary>Black desk lamp: round base, jointed arm and a dome shade pointing at the desk, with a warm glow.</summary>
    private void DrawLamp(int x, int deskY)
    {
        var body = Color.FromArgb(46, 48, 60);
        var edge = Color.FromArgb(112, 116, 134);
        var dark = Color.FromArgb(22, 22, 30);
        Rect(x - 2, deskY - 1, 6, 1, dark);                               // base
        Rect(x - 1, deskY - 2, 4, 1, body);
        for (var i = 0; i < 6; i++) Px(x + 1 + i / 3, deskY - 3 - i, edge);   // lower arm
        Px(x + 3, deskY - 9, Color.FromArgb(150, 154, 170));             // elbow joint
        for (var i = 0; i < 3; i++) Px(x + 3 - i / 2, deskY - 10 - i, edge); // upper arm towards the shade
        string[] shade =
        [
            "..eee...",
            ".ekkkk..",
            "ekkkkkk.",
            "kkkkkkkk",
            ".yyyyyy.",
        ];
        for (var r = 0; r < shade.Length; r++)
        for (var c = 0; c < shade[r].Length; c++)
        {
            var color = shade[r][c] switch { 'k' => body, 'e' => edge, 'y' => Color.FromArgb(255, 214, 130), _ => Color.Empty };
            if (color != Color.Empty) Px(x - 2 + c, deskY - 16 + r, color);
        }
        // Short warm glow under the shade and a lit patch on the desk top
        for (var row = 0; row < 3; row++)
            for (var col = -row; col <= 5 + row; col++)
                Px(x - 2 + col, deskY - 10 + row, Color.FromArgb(70 - row * 22, 255, 200, 120));
        for (var col = -6; col <= 6; col++)
            Px(x + col, deskY, Color.FromArgb(150 - Math.Abs(col) * 18, 255, 206, 140));
    }

    private void DrawScreen(AgentStatus agent, int x, int y, int w, int h, int frame, Theme.AgentLook look)
    {
        Rect(x, y, w, h, Color.FromArgb(5, 7, 13));
        switch (agent.State)
        {
            case ActivityState.Working:
                for (var row = 1; row < h - 1; row++)
                {
                    var seed = Hash(row + frame / 2);
                    var indent = seed % 3 * 2;
                    var len = 4 + seed / 3 % (w - 8);
                    var color = (seed / 7 % 3) switch { 0 => look.ScreenText, 1 => Color.FromArgb(232, 234, 242), _ => Color.FromArgb(120, 128, 150) };
                    Rect(x + 2 + indent, y + row, Math.Min(len, w - 4 - indent), 1, color);
                }
                if (frame % 2 == 0) Rect(x + w - 3, y + h - 2, 2, 1, Color.White);
                break;
            case ActivityState.Waiting:
                DrawPrompt(x + 3, y + 3, look.ScreenText, (frame / 4) % 2 == 0);
                break;
            case ActivityState.Idle:
                if (agent.Id == "claude")
                    for (var row = 2; row < h - 2; row += 2)
                        Rect(x + 3, y + row, 4 + Hash(row * 7) % 9, 1, row == 2 ? look.ScreenText : Color.FromArgb(90, 96, 118));
                else DrawPrompt(x + 3, y + 3, look.ScreenText, false);
                break;
            default:
                Px(x + w - 1, y + h, Color.FromArgb(200, Theme.Red));
                break;
        }
    }

    /// <summary>A big ">_" terminal prompt.</summary>
    private void DrawPrompt(int x, int y, Color color, bool cursorBright)
    {
        for (var i = 0; i < 3; i++) { Rect(x + i, y + i, 2, 1, color); Rect(x + i, y + 4 - i, 2, 1, color); }
        Rect(x + 6, y + 4, 5, 1, cursorBright ? Color.White : color);
    }

    private void DrawBubble(int x, int y, int frame, Theme.AgentLook look)
    {
        var fill = Color.FromArgb(234, 238, 246);
        Rect(x + 1, y, 11, 1, Outline);
        Rect(x + 1, y + 6, 11, 1, Outline);
        Rect(x, y + 1, 1, 5, Outline);
        Rect(x + 12, y + 1, 1, 5, Outline);
        Rect(x + 1, y + 1, 11, 5, fill);
        Px(x + 2, y + 7, Outline); Px(x + 3, y + 7, Outline); Px(x + 2, y + 6, fill);
        var dots = 1 + (frame / 4) % 3;
        for (var i = 0; i < dots; i++) Rect(x + 3 + i * 3, y + 3, 1, 1, i == dots - 1 ? look.Accent : Color.FromArgb(70, 74, 96));
    }

    private void DrawZ(int x, int y, int frame, Color tint)
    {
        for (var i = 0; i < 3; i++)
        {
            var t = (frame / 2 + i * 6) % 18;
            var color = Color.FromArgb((int)(255 * (1 - t / 18.0)), tint);
            var size = 4 + i % 2;
            var zx = x + t / 4 + i * 3;
            var zy = y - 3 - t / 3 - i;
            Rect(zx, zy, size, 1, color);
            for (var d = 1; d < size - 1; d++) Px(zx + size - 1 - d, zy + d, color);
            Rect(zx, zy + size - 1, size, 1, color);
        }
    }

    private void DrawServerLeds(int frame, bool busy)
    {
        for (var row = 0; row < 7; row++)
        {
            var on = Hash(row * 13 + frame / (busy ? 3 : 10)) % 3 != 0;
            var y = 40 + row * 3;
            Px(89, y, on ? Color.FromArgb(50, 220, 255) : Color.FromArgb(20, 60, 74));
            if (row % 2 == 0) Px(87, y, on ? Color.FromArgb(60, 255, 170) : Color.FromArgb(20, 60, 50));
        }
    }

    private void DrawSprite(string[] rows, int x, int y, Func<char, Color?> palette)
    {
        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
            if (palette(rows[r][c]) is { } color) Px(x + c, y + r, color);
    }

    // ================================================================ sky & weather (per frame)

    private static bool IsGlass(int x, int y) => y >= GY0 && y <= GY1 && ((x >= GX0 && x <= GX1 && (x < MullionX || x > MullionX + 2)) || (x >= 190 && x <= 232 && (x < 210 || x > 212)));

    private void DrawWeather(SkyState sky, int frame, int left = 74)
    {
        var GX0 = left; var GX1 = left + 42;
        var gw = GX1 - GX0 + 1;
        var gh = GY1 - GY0 + 1;
        var wet = sky.Weather is Weather.Rain or Weather.Storm or Weather.Drizzle;

        var clouds = sky.CloudCover switch { < 15 => 1, < 40 => 2, < 70 => 3, _ => 5 };
        if (sky.Weather == Weather.Clear) clouds = sky.CloudCover < 8 ? 0 : 1;
        var baseCloud = wet || sky.Weather == Weather.Cloudy
            ? Theme.Mix(Color.FromArgb(150, 156, 170), Color.FromArgb(40, 44, 58), sky.Night)
            : Theme.Mix(Color.FromArgb(242, 245, 252), Color.FromArgb(62, 68, 92), sky.Night);
        var cloudColor = Theme.Mix(baseCloud, sky.Bottom, 0.18f);
        if (sky.CloudCover >= 80)
            for (var y = GY0; y < GY0 + 5; y++)
                for (var x = GX0; x <= GX1; x++)
                    if (_skyMask[y * W + x]) Px(x, y, Color.FromArgb(210 - (y - GY0) * 36, cloudColor));
        string[] shape = ["....#####......", "..###########..", ".#############.", "###############", ".#############."];
        for (var i = 0; i < clouds; i++)
        {
            var span = gw + 30;
            var cx = GX0 - 15 + (Hash(i * 29) % span + frame / (8 + i * 2)) % span;
            var cy = GY0 + 1 + (Hash(i * 7 + 3) % 10);
            for (var r = 0; r < shape.Length; r++)
            for (var c = 0; c < shape[r].Length; c++)
            {
                if (shape[r][c] != '#') continue;
                var x = cx + c - (i % 2 == 0 ? 0 : 3);
                var y = cy + r;
                if (x >= 0 && x < W && y >= 0 && y < H && _skyMask[y * W + x])
                    Px(x, y, Color.FromArgb(r == shape.Length - 1 ? 190 : 232, r == 0 ? Theme.Mix(cloudColor, Color.White, 0.2f) : cloudColor));
            }
        }

        var drops = sky.Weather switch { Weather.Drizzle => 16, Weather.Rain => 36, Weather.Storm => 56, _ => 0 };
        for (var i = 0; i < drops; i++)
        {
            var y = GY0 + (Hash(i * 7 + 1) % gh + frame * 2) % gh;
            var x = GX0 + ((Hash(i * 13 + 5) % gw) - (y - GY0) / 3 + gw) % gw;
            var c = Color.FromArgb(sky.Weather == Weather.Drizzle ? 120 : 175, 170, 200, 240);
            if (IsGlass(x, y)) Px(x, y, c);
            if (IsGlass(x, y + 1)) Px(x, y + 1, c);
        }
        if (sky.Weather == Weather.Snow)
            for (var i = 0; i < 26; i++)
            {
                var y = GY0 + (Hash(i * 5) % gh + frame / 2) % gh;
                var x = GX0 + (Hash(i * 11) % gw + (int)(Math.Sin((frame + i * 9) / 6.0) * 1.5) + gw) % gw;
                if (IsGlass(x, y)) Px(x, y, Color.FromArgb(230, 245, 248, 255));
            }
        if (sky.Weather == Weather.Fog)
            for (var y = GY0; y <= GY1; y++)
                for (var x = GX0; x <= GX1; x++)
                    if (IsGlass(x, y)) Px(x, y, Color.FromArgb(70 + ((x + y + frame / 6) % 7) * 6, Theme.Mix(Color.FromArgb(190, 196, 206), Color.FromArgb(60, 64, 80), sky.Night)));
        if (sky.Weather == Weather.Storm && Hash(frame / 2 * 31 + 7) % 36 == 0)
        {
            for (var y = GY0; y <= GY1; y++)
                for (var x = GX0; x <= GX1; x++)
                    if (IsGlass(x, y)) Px(x, y, Color.FromArgb(120, 235, 240, 255));
            var bx = GX0 + 8 + Hash(frame) % (gw - 16);
            for (var y = GY0; y < GY0 + 18; y++)
            {
                bx += (Hash(frame + y) % 3) - 1;
                if (IsGlass(bx, y)) Px(bx, y, Color.White);
            }
        }
    }

    // ================================================================ static background (cached)

    private void BuildBackground(SkyState sky, int twinkle)
    {
        Array.Clear(_skyMask);
        // Wall with vertical panels
        for (var y = 0; y < FloorY; y++)
        {
            var wall = Theme.Mix(Color.FromArgb(14, 18, 34), Color.FromArgb(24, 30, 50), y / (float)FloorY);
            for (var x = 0; x < W; x++) Bg(x, y, wall);
        }
        for (var x = 0; x < W; x++) { Bg(x, 0, Color.FromArgb(8, 10, 18)); Bg(x, 1, Color.FromArgb(18, 22, 38)); }
        foreach (var seam in new[] { 23, 69, 121, 164 })
            for (var y = 2; y < FloorY - 2; y++) { Bg(seam, y, Color.FromArgb(10, 13, 24)); Bg(seam + 1, y, Color.FromArgb(30, 36, 58)); }
        for (var x = 0; x < W; x++) { Bg(x, FloorY - 2, Color.FromArgb(30, 36, 54)); Bg(x, FloorY - 1, Color.FromArgb(40, 46, 66)); }

        // Checkered floor
        for (var y = FloorY; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var dark = (x / 8 + (y - FloorY) / 4) % 2 == 0;
            Bg(x, y, dark ? Color.FromArgb(14, 17, 29) : Color.FromArgb(22, 26, 42));
        }

        DrawWindow(sky, twinkle);
        DrawWindow(sky, twinkle, 190, celestial: false);

        // Daylight spilling onto the floor
        var spill = (1 - sky.Night) * 0.14f;
        if (spill > 0.01f)
            for (var y = FloorY; y < FloorY + 8; y++)
                for (var x = GX0 - 2 - (y - FloorY); x <= GX1 + 2 + (y - FloorY); x++)
                    Bg(x, y, Color.FromArgb((int)(255 * spill * (1 - (y - FloorY) / 9f)), sky.Bottom));

        // Left wall: poster, shelf, framed picture, big plant
        DrawPoster(LeftPoster, Theme.Blue);
        DrawShelf(26, 17, 18, [Color.FromArgb(232, 180, 60), Theme.Blue, Color.FromArgb(40, 90, 200)], plantAt: 27, seed: 1);
        DrawShelf(26, 26, 18, [Theme.Orange, Color.FromArgb(232, 180, 60), Color.FromArgb(120, 130, 160)], plantAt: -1, seed: 4);
        DrawFrame(47, 8, 17, 12);
        DrawFloorPlant(3, FloorY + 6, Color.FromArgb(78, 86, 106), tall: true);

        // Right wall: framed picture, shelf, poster, orange plant
        DrawFrame(125, 9, 15, 11);
        DrawShelf(143, 16, 18, [Theme.Orange, Theme.Blue, Color.FromArgb(232, 180, 60)], plantAt: 144, seed: 7);
        DrawShelf(143, 25, 18, [Color.FromArgb(214, 60, 40), Theme.Blue, Color.FromArgb(120, 130, 160)], plantAt: -1, seed: 9);
        DrawPoster(RightPoster, Theme.Orange);
        DrawFloorPlant(180, FloorY + 6, Theme.Orange, tall: false);

        // Extra station keeps the original two desks and window in place.

        DrawShelf(241, 17, 18, [Theme.Blue, Color.FromArgb(234, 67, 53), Color.FromArgb(251, 188, 5)], plantAt: 242, seed: 12);
        DrawShelf(241, 26, 18, [Color.FromArgb(52, 168, 83), Theme.Blue, Color.FromArgb(251, 188, 5)], plantAt: -1, seed: 15);
        DrawFloorPlant(277, FloorY + 6, Color.FromArgb(52, 168, 83), tall: false);
        for (var y = 2; y < FloorY - 2; y++) Bg(208, y, Color.FromArgb(30, 36, 58));
        // Server tower in front of the window
        const int sx = 79, sy = 36;
        for (var y = sy; y <= FloorY + 6; y++)
            for (var x = sx; x <= sx + 13; x++)
            {
                var edge = x == sx || x == sx + 13 || y == sy;
                Bg(x, y, edge ? Color.FromArgb(66, 74, 98) : x == sx + 1 ? Color.FromArgb(44, 50, 70) : Color.FromArgb(26, 30, 44));
            }
        for (var row = 0; row < 7; row++)
            for (var x = sx + 3; x <= sx + 7; x++) Bg(x, 40 + row * 3, Color.FromArgb(44, 50, 70));
        for (var y = sy + 2; y < FloorY + 4; y++) Bg(sx + 12, y, Color.FromArgb(34, 38, 54));

        // Small plant on Codex's desk, next to the window
        DrawPottedPlant(70, 49, Color.FromArgb(40, 96, 210));
    }

    private void DrawWindow(SkyState sky, int twinkle, int left = 74, bool celestial = true)
    {
        var GX0 = left; var GX1 = left + 42; var MullionX = left + 20;
        for (var y = GY0; y <= GY1; y++)
        {
            var c = Theme.Mix(sky.Top, sky.Bottom, (y - GY0) / (float)(GY1 - GY0));
            for (var x = GX0; x <= GX1; x++) { Bg(x, y, c); _skyMask[y * W + x] = true; }
        }
        var clear = 1 - Math.Clamp(sky.CloudCover / 100f, 0, 1);
        if (sky.Night > 0.35f)
            foreach (var (sx, sy) in new[] { (3, 2), (8, 8), (14, 3), (18, 11), (24, 5), (29, 1), (33, 8), (38, 3), (41, 12), (6, 14), (27, 13), (12, 17) })
            {
                var a = (int)(255 * Math.Clamp((sky.Night - 0.35f) / 0.5f, 0, 1) * (0.25f + 0.75f * clear) * ((sx + twinkle) % 4 == 0 ? 0.45f : 1f));
                Bg(GX0 + sx, GY0 + sy, Color.FromArgb(a, 235, 240, 255), glassOnly: true);
            }
        if (celestial && sky.SunT is > -0.03f and < 1.03f)
        {
            var t = Math.Clamp(sky.SunT, 0, 1);
            var sx = GX0 + (int)(t * (GX1 - GX0));
            var sy = GY1 - 7 - (int)(Math.Sin(Math.PI * t) * (GY1 - GY0 - 12));
            var alpha = sky.CloudCover > 85 ? 0.35f : 1f;
            var sunColor = sky.SunT is < 0.08f or > 0.92f ? Color.FromArgb(255, 170, 90) : Color.FromArgb(255, 236, 150);
            for (var dx = -6; dx <= 6; dx++)
            for (var dy = -6; dy <= 6; dy++)
            {
                var d = dx * dx + dy * dy;
                if (d <= 8) Bg(sx + dx, sy + dy, Color.FromArgb((int)(255 * alpha), sunColor), glassOnly: true);
                else if (d <= 30) Bg(sx + dx, sy + dy, Color.FromArgb((int)(45 * alpha), sunColor), glassOnly: true);
            }
        }
        if (celestial && sky.Night > 0.3f)
        {
            var (mx, my) = (GX1 - 9, GY0 + 6);
            var p = sky.MoonPhase;
            var alpha = (int)(255 * Math.Clamp((sky.Night - 0.3f) / 0.4f, 0, 1) * (0.35f + 0.65f * clear));
            for (var dx = -8; dx <= 8; dx++)
            for (var dy = -8; dy <= 8; dy++)
            {
                var d = dx * dx + dy * dy;
                if (d > 20 && d <= 64) Bg(mx + dx, my + dy, Color.FromArgb(alpha / 9, 255, 240, 200), glassOnly: true);
            }
            for (var dx = -4; dx <= 4; dx++)
            for (var dy = -4; dy <= 4; dy++)
            {
                var nx = dx / 4.3; var ny = dy / 4.3;
                if (nx * nx + ny * ny > 1) continue;
                var s = Math.Sqrt(1 - ny * ny);
                var c = Math.Cos(2 * Math.PI * p);
                var ex = sky.SouthernHemisphere ? -nx : nx;
                var lit = p < 0.5 ? ex > c * s : ex < -c * s;
                var crater = (dx == -1 && dy == -1) || (dx == 1 && dy == 2);
                Bg(mx + dx, my + dy, lit ? Color.FromArgb(alpha, crater ? 236 : 255, crater ? 220 : 240, crater ? 180 : 200) : Color.FromArgb(alpha / 4, 200, 210, 230), glassOnly: true);
            }
        }
        // Skyline: far layer, near layer with a spire tower, lit windows
        var rng = new Random(left == 74 ? 11 : 29);
        var bodyNear = Theme.Mix(Color.FromArgb(78, 94, 122), Color.FromArgb(10, 14, 28), sky.Night);
        var bodyFar = Theme.Mix(Color.FromArgb(112, 132, 160), Color.FromArgb(24, 32, 58), sky.Night);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var bx = GX0 - 1; bx <= GX1;)
            {
                var bw = rng.Next(4, 9);
                var bh = pass == 0 ? rng.Next(10, 22) : rng.Next(5, 15);
                var body = pass == 0 ? bodyFar : bodyNear;
                for (var x = bx; x < bx + bw; x++)
                for (var y = GY1 - bh + 1; y <= GY1; y++)
                {
                    if (x < GX0 || x > GX1) continue;
                    Bg(x, y, body);
                    _skyMask[y * W + x] = false;
                }
                for (var ly = GY1 - bh + 2; ly < GY1; ly += 2)
                for (var lx = bx + 1; lx < bx + bw - 1; lx += 2)
                {
                    if (lx < GX0 || lx > GX1) continue;
                    var h = Hash(lx * 31 + ly * 17 + pass * 7);
                    var chance = (pass == 0 ? 0.04 : 0.08) + (pass == 0 ? 0.35 : 0.5) * sky.Night;
                    if (h % 100 >= chance * 100) continue;
                    if (h % 11 == 0 && twinkle % 3 == 0) continue;
                    var color = (h / 7 % 5) switch { 0 => Color.FromArgb(90, 170, 255), 1 or 2 => Color.FromArgb(255, 170, 60), _ => Color.FromArgb(255, 212, 110) };
                    Bg(lx, ly, Color.FromArgb((int)((pass == 0 ? 90 : 140) + 110 * sky.Night), color));
                }
                bx += bw + (pass == 0 ? rng.Next(1, 3) : rng.Next(0, 2));
            }
        }
        // Tall tower with a spire in the right pane
        var tx = left + 24;
        for (var y = GY1 - 24; y <= GY1; y++)
            for (var x = tx; x < tx + 6; x++) { Bg(x, y, bodyNear); _skyMask[y * W + x] = false; }
        for (var y = GY1 - 29; y < GY1 - 24; y++) { Bg(tx + 2, y, bodyNear); Bg(tx + 3, y, bodyNear); _skyMask[y * W + tx + 2] = _skyMask[y * W + tx + 3] = false; }
        Bg(tx + 2, GY1 - 30, Color.FromArgb(255, 70, 60));
        for (var y = GY1 - 22; y < GY1; y += 2)
            for (var x = tx + 1; x < tx + 5; x += 2)
                if (Hash(x * 5 + y) % 3 != 0) Bg(x, y, Color.FromArgb((int)(120 + 120 * sky.Night), 255, 190, 90));
        // Frame, mullion and sill
        var frame = Color.FromArgb(52, 60, 86);
        var frameDark = Color.FromArgb(32, 38, 58);
        for (var x = GX0 - 2; x <= GX1 + 2; x++)
        {
            Bg(x, GY0 - 2, frameDark); Bg(x, GY0 - 1, frame);
            Bg(x, GY1 + 1, frame); Bg(x, GY1 + 2, Color.FromArgb(66, 74, 100)); Bg(x, GY1 + 3, frameDark);
        }
        for (var y = GY0 - 2; y <= GY1 + 1; y++)
        {
            Bg(GX0 - 2, y, frameDark); Bg(GX0 - 1, y, frame);
            Bg(GX1 + 1, y, frame); Bg(GX1 + 2, y, frameDark);
            Bg(MullionX, y, frame); Bg(MullionX + 1, y, Color.FromArgb(66, 74, 100)); Bg(MullionX + 2, y, frameDark);
        }
    }

    private void DrawPoster(Rectangle r, Color accent)
    {
        for (var y = r.Top; y < r.Bottom; y++)
            for (var x = r.Left; x < r.Right; x++)
            {
                var edge = x == r.Left || x == r.Right - 1 || y == r.Top || y == r.Bottom - 1;
                Bg(x, y, edge ? Color.FromArgb(160, 164, 158) : Color.FromArgb(218, 220, 210));
            }
        for (var y = r.Top + 1; y <= r.Bottom; y++) Bg(r.Right, y, Color.FromArgb(10, 12, 20));
        for (var x = r.Left + 1; x <= r.Right; x++) Bg(x, r.Bottom, Color.FromArgb(10, 12, 20));
        for (var x = r.Left + r.Width / 2 - 2; x < r.Left + r.Width / 2 + 2; x++) Bg(x, r.Top, Color.FromArgb(150, 230, 230, 220)); // tape
        for (var x = r.Left + 3; x < r.Left + 8; x++) Bg(x, r.Bottom - 4, accent);
    }

    private void DrawShelf(int x, int y, int w, Color[] books, int plantAt, int seed)
    {
        for (var i = 0; i < w; i++) { Bg(x + i, y, Color.FromArgb(70, 78, 104)); Bg(x + i, y + 1, Color.FromArgb(40, 46, 64)); }
        Bg(x + 2, y + 2, Color.FromArgb(40, 46, 64)); Bg(x + w - 3, y + 2, Color.FromArgb(40, 46, 64));
        var bx = plantAt >= 0 ? x + 7 : x + 2;
        for (var i = 0; bx < x + w - 2; i++)
        {
            var h = 4 + Hash(seed * 7 + i) % 3;
            var c = books[i % books.Length];
            var bw = 1 + Hash(seed + i * 5) % 2;
            for (var yy = y - h; yy < y; yy++)
                for (var xx = bx; xx < bx + bw; xx++) Bg(xx, yy, xx == bx ? Theme.Mix(c, Color.White, 0.2f) : c);
            bx += bw + (i % 3 == 2 ? 1 : 0);
            if (i == 3 && plantAt < 0) { DrawFigurine(bx + 1, y); bx += 4; }
        }
        if (plantAt >= 0) DrawPottedPlant(plantAt, y - 1, Color.FromArgb(40, 96, 210));
    }

    private void DrawFigurine(int x, int y)
    {
        var c = Color.FromArgb(120, 128, 156);
        Bg(x, y - 1, c); Bg(x + 1, y - 1, c); Bg(x + 2, y - 1, c); Bg(x + 1, y - 2, c); Bg(x + 1, y - 3, Color.FromArgb(160, 168, 196));
    }

    private void DrawFrame(int x, int y, int w, int h)
    {
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                var border = xx == x || xx == x + w - 1 || yy == y || yy == y + h - 1;
                Bg(xx, yy, border ? Color.FromArgb(64, 72, 98) : Color.FromArgb(30, 38, 62));
            }
        // Mountain landscape inside
        for (var xx = x + 1; xx < x + w - 1; xx++)
        {
            var d = Math.Abs(xx - (x + w / 2 - 1));
            var peak = y + h - 2 - Math.Max(0, (h - 4) - d);
            for (var yy = Math.Max(y + 2, peak); yy < y + h - 1; yy++) Bg(xx, yy, yy == peak ? Color.FromArgb(120, 134, 170) : Color.FromArgb(70, 84, 118));
        }
        Bg(x + w - 4, y + 2, Color.FromArgb(200, 210, 230));
    }

    private void DrawPottedPlant(int x, int baseY, Color pot)
    {
        for (var yy = baseY - 2; yy <= baseY; yy++)
            for (var xx = x; xx < x + 4; xx++) Bg(xx, yy, xx == x ? Theme.Mix(pot, Outline, 0.35f) : pot);
        foreach (var (dx, dy, c) in new[] { (1, -3, Leaf), (2, -4, LeafLight), (0, -5, LeafDark), (3, -5, Leaf), (1, -6, Leaf), (2, -7, LeafDark), (-1, -4, Leaf), (4, -3, LeafDark) })
            Bg(x + dx, baseY + dy, c);
    }

    private void DrawFloorPlant(int x, int baseY, Color pot, bool tall)
    {
        var potH = 8;
        for (var yy = baseY - potH + 1; yy <= baseY; yy++)
        {
            var inset = (yy - (baseY - potH + 1)) / 4;
            for (var xx = x + inset; xx < x + 9 - inset; xx++)
                Bg(xx, yy, xx <= x + inset ? Theme.Mix(pot, Outline, 0.35f) : yy == baseY - potH + 1 ? Theme.Mix(pot, Color.White, 0.2f) : pot);
        }
        var top = baseY - potH;
        var blades = tall ? 9 : 6;
        var height = tall ? 20 : 13;
        for (var i = 0; i < blades; i++)
        {
            var dir = (i - blades / 2.0) / (blades / 2.0); // -1..1
            var len = height - Math.Abs(i - blades / 2) * 2 + Hash(i * 3) % 3;
            for (var s = 0; s < len; s++)
            {
                var px = x + 4 + (int)Math.Round(dir * s * 0.55);
                var py = top - s;
                Bg(px, py, s > len - 3 ? LeafLight : (i % 2 == 0 ? Leaf : LeafDark));
                if (s < len / 2) Bg(px + (dir > 0 ? 1 : -1), py, LeafDark);
            }
        }
    }

    // ================================================================ pixel helpers

    private void Bg(int x, int y, Color c, bool glassOnly = false)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        if (glassOnly && !IsGlass(x, y)) return;
        var i = y * W + x;
        _bgPixels[i] = c.A == 255 ? c.ToArgb() : Theme.Mix(Color.FromArgb(_bgPixels[i]), Color.FromArgb(255, c), c.A / 255f).ToArgb();
    }

    private void Px(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var i = y * W + x;
        _px[i] = c.A == 255 ? c.ToArgb() : Theme.Mix(Color.FromArgb(_px[i]), Color.FromArgb(255, c), c.A / 255f).ToArgb();
    }

    private void Rect(int x, int y, int w, int h, Color c)
    {
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++) Px(x + i, y + j, c);
    }

    private static int Hash(int v)
    {
        unchecked
        {
            var h = (uint)v * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)(h & 0x7fffffff);
        }
    }

    public void Dispose() => _canvas.Dispose();
}
