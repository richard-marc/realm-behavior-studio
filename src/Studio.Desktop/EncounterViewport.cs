using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Studio.Raple;

namespace Studio.Desktop;
public sealed class EncounterViewport : FrameworkElement
{
    public EncounterSession? Session { get; set; }
    public ClientSprites? Sprites { get; set; }
    public string PreviewEntity { get; set; } = "Cave Pirate Brawler";
    public bool ShowHitboxes { get; set; }
    public bool ShowGrid { get; set; }
    public event Action<Guid>? Selected;
    double scale = 32; Vector pan; Point? drag;
    readonly HashSet<Key> keys = [];
    readonly Dictionary<int, (float X, float Y, long Moved, bool Left)> positions = [];
    static readonly Dictionary<string, SolidColorBrush> brushes = [];
    Point World(Point p) => new((p.X - ActualWidth / 2 - pan.X) / scale + 64.5, (p.Y - ActualHeight / 2 - pan.Y) / scale + 64.5);
    Point Screen(double x, double y) => new((x - 64.5) * scale + ActualWidth / 2 + pan.X, (y - 64.5) * scale + ActualHeight / 2 + pan.Y);
    internal static Point HitPlanePoint(Point ground, double tile) => ground - new Vector(0, tile * .5);
    Point HitScreen(double x, double y) => HitPlanePoint(Screen(x, y), scale);
    Point HitWorld(Point p) => World(p + new Vector(0, scale * .5));
    // Camera.as uses 50 px per tile; TextureRedrawer scales source pixels by 5 and
    // retains one bottom padding pixel. Characters and collision share this projection.
    internal static Rect CharacterBounds(Point ground, double tile, double size, BitmapSource image, double elevation = 0) =>
        new(ground.X - image.PixelWidth * tile * size / 20, ground.Y - (elevation + .02) * tile - image.PixelHeight * tile * size / 10,
            image.PixelWidth * tile * size / 10, image.PixelHeight * tile * size / 10);
    public void ResetCamera() { scale = 32; pan = new(); positions.Clear(); InvalidateVisual(); }
    public EncounterViewport()
    {
        ClipToBounds = true; Focusable = true; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        MouseWheel += (_, e) =>
        {
            var cursor = e.GetPosition(this); var world = World(cursor);
            scale = Math.Clamp(scale * (e.Delta > 0 ? 1.12 : 1 / 1.12), 8, 100);
            pan = new Vector(cursor.X - ActualWidth / 2 - (world.X - 64.5) * scale, cursor.Y - ActualHeight / 2 - (world.Y - 64.5) * scale); InvalidateVisual();
        };
        KeyDown += (_, e) => { keys.Add(e.Key); if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right) e.Handled = true; };
        KeyUp += (_, e) => keys.Remove(e.Key); LostKeyboardFocus += (_, _) => keys.Clear();
        MouseDown += (_, e) =>
        {
            Focus(); if (e.ChangedButton == MouseButton.Middle) { drag = e.GetPosition(this); CaptureMouse(); return; }
            if (e.ChangedButton == MouseButton.Right && Session != null)
            {
                var w = HitWorld(e.GetPosition(this)); var bullet = Session.Projectiles.OrderBy(b => Math.Pow(b.X - w.X, 2) + Math.Pow(b.Y - w.Y, 2)).FirstOrDefault();
                if (bullet?.NodeId is { } id && Math.Abs(bullet.X - w.X) < 1 && Math.Abs(bullet.Y - w.Y) < 1) Selected?.Invoke(id);
            }
        };
        MouseMove += (_, e) => { if (drag is { } previous) { var next = e.GetPosition(this); pan += next - previous; drag = next; InvalidateVisual(); } };
        MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { drag = null; ReleaseMouseCapture(); } };
    }
    public EncounterInput Input()
    {
        bool Held(params Key[] values) => IsKeyboardFocusWithin && values.Any(keys.Contains);
        var aim = HitWorld(Mouse.GetPosition(this));
        return new((Held(Key.D, Key.Right) ? 1 : 0) - (Held(Key.A, Key.Left) ? 1 : 0), (Held(Key.S, Key.Down) ? 1 : 0) - (Held(Key.W, Key.Up) ? 1 : 0), IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed, aim.X, aim.Y);
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brush("#101215"), null, new(0, 0, ActualWidth, ActualHeight));
        if (ShowGrid)
        {
            var grid = new Pen(Brush("#22262C"), .5);
            for (var i = 0; i <= 128; i++) { dc.DrawLine(grid, Screen(i, 0), Screen(i, 128)); dc.DrawLine(grid, Screen(0, i), Screen(128, i)); }
        }
        // Quiet reference axes remain useful when panning without a dense tile grid.
        var origin = Screen(64.5, 64.5);
        var guide = new Pen(Brush("#1B1E23"), 1) { DashStyle = DashStyles.Dot };
        dc.DrawLine(guide, new(origin.X, 0), new(origin.X, ActualHeight)); dc.DrawLine(guide, new(0, origin.Y), new(ActualWidth, origin.Y));
        if (Session is not { } session)
        {
            DrawCharacter(dc, PreviewEntity, origin, SpriteAction.Stand, 0, 0);
            Text(dc, "Ready to simulate", new(18, 18), 13, Brush("#A4AAB3"));
            Text(dc, "Press Play  ·  WASD to move  ·  mouse to aim", new(18, ActualHeight - 32), 12, Brush("#686F7B")); return;
        }
        foreach (var e in session.Entities.OrderBy(e => e.Y))
        {
            var p = Screen(e.X, e.Y); var previous = positions.GetValueOrDefault(e.Id, (e.X, e.Y, -1000, false));
            var moved = Math.Abs(e.X - previous.X) + Math.Abs(e.Y - previous.Y) > .0001;
            var left = moved && Math.Abs(e.X - previous.X) > .0001 ? e.X < previous.X : previous.Left;
            positions[e.Id] = (e.X, e.Y, moved ? session.Ms : previous.Moved, left);
            var frame = session.Ms - positions[e.Id].Moved < 200 ? 1 + (int)(session.Ms / 180 % 2) : 0;
            DrawCharacter(dc, e.Name, p, frame == 0 ? SpriteAction.Stand : SpriteAction.Walk, session.Ms / 360d % 1, left ? Math.PI : 0);
            var width = Math.Clamp(scale * .8 * Sprites!.Size(e.Name), 26, 90);
            var image = Sprites.Get(e.Name); var bounds = image == null ? new Rect(p.X - 12, p.Y - 24, 24, 24) : CharacterBounds(p, scale, Sprites.Size(e.Name), image, Sprites.Elevation(e.Name));
            var top = bounds.Top - 10;
            dc.DrawRoundedRectangle(Brush("#303039"), null, new(p.X - width / 2, top, width, 3), 1.5, 1.5);
            var hpWidth = width * Math.Clamp(e.Hp / (double)Math.Max(1, e.MaxHp), 0, 1);
            if (hpWidth > 0) dc.DrawRoundedRectangle(Brush("#C1B2E9"), null, new(p.X - width / 2, top, hpWidth, 3), 1.5, 1.5);
            if (ShowHitboxes) DrawHitbox(dc, HitScreen(e.X, e.Y), e.Hitbox, "#B3A1D6");
        }
        var projectiles = session.Projectiles;
        foreach (var b in projectiles)
        {
            var point = HitScreen(b.X, b.Y); var image = Sprites?.Get(b.TextureId);
            if (image != null)
            {
                var size = scale * .65 * Math.Clamp(Sprites!.Size(b.TextureId), .5, 3);
                dc.PushTransform(new RotateTransform((b.Angle + Sprites.Rotation(b.TextureId, session.Ms)) * 180 / Math.PI, point.X, point.Y));
                dc.DrawImage(image, new(point.X - size * image.PixelWidth / 16, point.Y - size * image.PixelHeight / 16, size * image.PixelWidth / 8, size * image.PixelHeight / 8)); dc.Pop();
            }
            else dc.DrawRectangle(Brush("#F1C783"), null, new(point.X - 2, point.Y - 2, 4, 4));
            if (ShowHitboxes)
            {
                var pen = new Pen(Brush(b.Friendly ? "#98E8CB" : "#E9B774"), 1);
                dc.DrawLine(pen, point - new Vector(3, 0), point + new Vector(3, 0));
                dc.DrawLine(pen, point - new Vector(0, 3), point + new Vector(0, 3));
            }
        }
        var player = Screen(session.PlayerX, session.PlayerY);
        var attacking = session.Ms < session.PlayerAttackStart + session.PlayerAttackPeriod;
        var action = attacking ? SpriteAction.Attack : session.PlayerMoveSpeed > 0 ? SpriteAction.Walk : SpriteAction.Stand;
        var phase = attacking ? (session.Ms - session.PlayerAttackStart) / session.PlayerAttackPeriod : session.PlayerMoveSpeed > 0 ? session.Ms / (int)(3.5 / session.PlayerMoveSpeed) % 1 : 0;
        if (session.Dead) dc.PushOpacity(.3);
        DrawCharacter(dc, session.PlayerClass, player, action, phase, attacking ? session.PlayerAttackAngle : session.PlayerFacing);
        if (session.Dead) dc.Pop();
        if (ShowHitboxes) DrawHitbox(dc, HitScreen(session.PlayerX, session.PlayerY), new HitGeometry(.5), "#98E8CB");
        Text(dc, $"{session.Ms / 1000d:0.0}s     HP {session.PlayerHp}     {projectiles.Count} projectiles", new(18, 16), 12, Brush("#9A9FAA"));
        Text(dc, ShowHitboxes ? "Client hit plane  ·  target boundaries + projectile points" : session.Weapon.Name + "  ·  scroll to zoom  ·  middle drag to pan", new(18, ActualHeight - 28), 11, Brush("#838B98"));
    }
    void DrawHitbox(DrawingContext dc, Point point, HitGeometry hitbox, string color)
    {
        var pen = new Pen(Brush(color), 1); var radius = scale * hitbox.Radius;
        if (hitbox.Circular) dc.DrawEllipse(null, pen, point, radius, radius);
        else dc.DrawRectangle(null, pen, new(point.X - radius, point.Y - radius, radius * 2, radius * 2));
        dc.DrawEllipse(Brush(color), null, point, 1.5, 1.5);
    }
    void DrawCharacter(DrawingContext dc, string id, Point point, SpriteAction action, double phase, double facing)
    {
        var frame = Sprites?.Character(id, action, phase, facing) ?? default;
        var image = frame.Image;
        dc.DrawEllipse(Brush("#090A0C"), null, point + new Vector(0, 1), scale * .4, scale * .12);
        if (image == null)
        {
            dc.DrawRoundedRectangle(null, new Pen(Brush("#626975"), 1), new(point.X - 12, point.Y - 24, 24, 24), 4, 4);
            Text(dc, "?", new(point.X - 4, point.Y - 22), 14, Brush("#92969F")); return;
        }
        var rect = CharacterBounds(point, scale, Sprites!.Size(id), image, Sprites.Elevation(id));
        if (frame.Flip) dc.PushTransform(new ScaleTransform(-1, 1, point.X, point.Y));
        dc.DrawImage(image, rect); if (frame.Flip) dc.Pop();
    }
    internal static SolidColorBrush Brush(string hex) { if (brushes.TryGetValue(hex, out var cached)) return cached; var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brushes[hex] = brush; }
    internal static void Text(DrawingContext dc, string text, Point p, double size, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1), p);
}
