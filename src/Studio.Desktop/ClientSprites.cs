using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Studio.Raple;

namespace Studio.Desktop;

public enum SpriteAction { Stand, Walk, Attack }
public readonly record struct CharacterFrame(BitmapSource? Image, bool Flip);

// Reads the supplied client's own sheet registrations. No game art is copied into Studio.
public sealed class ClientSprites
{
    record Sheet(string Path, int Width, int Height, int CellWidth, int CellHeight, bool Animated, bool FirstDown);
    readonly Dictionary<string, Sheet> sheets = new(StringComparer.Ordinal);
    readonly Dictionary<string, XElement> objects = new(StringComparer.Ordinal);
    readonly Dictionary<string, BitmapSource?> frames = new(StringComparer.Ordinal);
    readonly Dictionary<string, BitmapSource> bitmaps = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<BitmapSource, bool> empty = [];
    readonly Dictionary<BitmapSource, BitmapSource> symmetricWalks = [];
    readonly Dictionary<string, BitmapSource> extendedAttacks = [];
    static readonly int[][] FacingDirections = [[1, 3, 2], [3, 1, 2], [3, 0, 2], [0, 3, 2], [0, 2], [2, 0], [2, 1], [1, 2]];
    public static ClientSprites? Current { get; private set; }
    public int SheetCount => sheets.Count;
    public int LoadedFrames => frames.Values.Count(v => v != null);
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
    public string ClientRoot { get; }

    public ClientSprites(string serverRoot, string? clientRoot = null)
    {
        ClientRoot = Path.GetFullPath(clientRoot ?? Path.Combine(serverRoot, "../raple-realms-7-client"));
        var src = Path.Combine(ClientRoot, "src");
        if (Directory.Exists(src))
        {
            var files = Directory.EnumerateFiles(src, "*.as", SearchOption.AllDirectories).ToArray();
            var classes = files.GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
            var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files.Where(f => Path.GetFileName(f) is "EmbeddedAssets.as" or "Oryx4Assets.as" or "OryxAscendedAssets.as"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"(?:var|const)\s+(\w+)\s*:\s*Class\s*=\s*(\w+)"))
                    symbols[Path.GetFileNameWithoutExtension(file) + "." + m.Groups[1].Value] = m.Groups[2].Value;
            string? Resolve(string name)
            {
                if (symbols.TryGetValue(name, out var actual)) name = actual;
                if (!classes.TryGetValue(name, out var file)) return null;
                var embed = Regex.Match(File.ReadAllText(file), "Embed\\s*\\(\\s*source\\s*=\\s*\"([^\"]+\\.png)\"");
                if (!embed.Success) return null;
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, embed.Groups[1].Value));
                return path.StartsWith(ClientRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
            }
            var loader = Path.Combine(src, "com/company/assembleegameclient/util/AssetLoader.as");
            if (File.Exists(loader))
            {
                var code = File.ReadAllText(loader);
                foreach (Match m in Regex.Matches(code, "AssetLibrary\\.addImageSet\\(\\s*\"([^\"]+)\"\\s*,\\s*new\\s+([\\w.]+)\\(\\)\\.bitmapData\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\)"))
                    if (Resolve(m.Groups[2].Value) is { } path)
                        sheets[m.Groups[1].Value] = new(path, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), false, false);
                foreach (Match m in Regex.Matches(code, "AnimatedChars\\.add\\(\\s*\"([^\"]+)\"\\s*,\\s*new\\s+([\\w.]+)\\(\\)\\.bitmapData\\s*,.*?\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*AnimatedChar\\.(RIGHT|DOWN)\\s*\\)"))
                    if (Resolve(m.Groups[2].Value) is { } path)
                        sheets[m.Groups[1].Value] = new(path, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), true, m.Groups[7].Value == "DOWN");
            }
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(serverRoot, "TKR.Shared/resources/xml"), "*.xml", SearchOption.AllDirectories))
        {
            var xml=XDocument.Load(file);
            foreach (var element in xml.Descendants("Object"))
                if (element.Attribute("id") is { } id) objects[id.Value] = element;
            foreach(var element in xml.Descendants("Ground"))
                if(element.Attribute("type") is {} type)
                { var text=type.Value;var number=text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToUInt16(text[2..],16):ushort.Parse(text,CultureInfo.InvariantCulture);objects["@ground:"+number]=element; }
        }
        Current = this;
    }
    public BitmapSource? Get(string id, int frame = 0, bool down = false) => GetRaw(id, frame, down ? 2 : 0);
    public BitmapSource? GetGround(ushort type)=>Get("@ground:"+type);
    BitmapSource? GetRaw(string id, int frame, int direction)
    {
        var key = id + ":" + frame + ":" + direction;
        if (frames.TryGetValue(key, out var cached)) return cached;
        BitmapSource? result = null;
        if (objects.TryGetValue(id, out var xml))
        {
            var texture = xml.Element("Texture") ?? xml.Element("AnimatedTexture") ?? xml.Element("RandomTexture")?.Elements().FirstOrDefault();
            if (texture?.Element("File") is { } file && sheets.TryGetValue(file.Value, out var sheet))
            {
                try
                {
                    if (!bitmaps.TryGetValue(sheet.Path, out var bitmap))
                    {
                        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(sheet.Path); image.EndInit(); image.Freeze();
                        bitmaps[sheet.Path] = bitmap = image;
                    }
                    var value = texture.Element("Index")?.Value ?? "0";
                    var index = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(value[2..], 16) : int.Parse(value, CultureInfo.InvariantCulture);
                    var cols = bitmap.PixelWidth / sheet.CellWidth;
                    var x = index % cols * sheet.CellWidth + (sheet.Animated ? Math.Clamp(frame, 0, 6) * sheet.Width : 0);
                    var row = direction == 3 ? 2 : direction == 2 ? (sheet.FirstDown ? 0 : 1) : (sheet.FirstDown ? 1 : 0);
                    if (!sheet.Animated || row >= sheet.CellHeight / sheet.Height) row = 0;
                    var y = index / cols * sheet.CellHeight + row * sheet.Height;
                    if (x + sheet.Width <= bitmap.PixelWidth && y + sheet.Height <= bitmap.PixelHeight)
                    { result = new CroppedBitmap(bitmap, new System.Windows.Int32Rect(x, y, sheet.Width, sheet.Height)); result.Freeze(); }
                }
                catch (Exception e) when (e is IOException or FormatException or ArgumentException or NotSupportedException) { }
            }
        }
        if (result == null) Missing.Add(id);
        return frames[key] = result;
    }
    Sheet? ObjectSheet(string id)
    {
        if (!objects.TryGetValue(id, out var xml)) return null;
        var texture = xml.Element("Texture") ?? xml.Element("AnimatedTexture") ?? xml.Element("RandomTexture")?.Elements().FirstOrDefault();
        return texture?.Element("File") is { } file ? sheets.GetValueOrDefault(file.Value) : null;
    }
    bool Empty(BitmapSource? frame)
    {
        if (frame == null) return true;
        if (empty.TryGetValue(frame, out var cached)) return cached;
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[frame.PixelWidth * frame.PixelHeight * 4]; bitmap.CopyPixels(bytes, frame.PixelWidth * 4, 0);
        return empty[frame] = !Enumerable.Range(0, bytes.Length / 4).Any(i => bytes[i * 4 + 3] != 0);
    }
    BitmapSource SymmetricWalk(BitmapSource image)
    {
        if (symmetricWalks.TryGetValue(image, out var cached)) return cached;
        // AnimatedChar.loadDir mirrors the first seven columns for its missing up/down step.
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = image.PixelWidth * 4; var pixels = new byte[stride * image.PixelHeight]; source.CopyPixels(pixels, stride, 0);
        var mirrored = new byte[pixels.Length]; var width = Math.Min(7, image.PixelWidth);
        for (var y = 0; y < image.PixelHeight; y++)
            for (var x = 0; x < width; x++) Array.Copy(pixels, y * stride + x * 4, mirrored, y * stride + (width - x - 1) * 4, 4);
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, mirrored, stride); result.Freeze();
        return symmetricWalks[image] = result;
    }
    public CharacterFrame Character(string id, SpriteAction action, double phase, double facing)
    {
        var sheet = ObjectSheet(id);
        if (sheet is not { Animated: true }) return new(Get(id), false);
        bool Has(int dir) => dir == 3 ? sheet.CellHeight >= sheet.Height * 3 :
            dir == 2 ? sheet.FirstDown || sheet.CellHeight >= sheet.Height * 2 : !sheet.FirstDown || sheet.CellHeight >= sheet.Height * 2;
        var sector = Math.Clamp((int)(Math.IEEERemainder(facing, Math.PI * 2) / (Math.PI / 4) + 4) % 8, 0, 7);
        var dir = FacingDirections[sector].First(Has); var flip = dir == 1; var rawDir = flip ? 0 : dir;
        var second = phase % 1 >= .5;
        var frame = action == SpriteAction.Stand ? 0 : action == SpriteAction.Walk ? (second ? 2 : 1) : (second ? 5 : 4);
        var image = GetRaw(id, frame, rawDir);
        if (action == SpriteAction.Attack)
        {
            var first = GetRaw(id, 4, rawDir); var last = GetRaw(id, 5, rawDir);
            if (Empty(first) && Empty(last)) { frame = second ? 2 : 1; image = GetRaw(id, frame, rawDir); }
            else if (Empty(image)) { frame = Empty(first) ? 5 : 4; image = GetRaw(id, frame, rawDir); }
            if (frame == 5 && !Empty(GetRaw(id, 6, rawDir)) && image != null)
            {
                var key = id + ":" + rawDir;
                if (!extendedAttacks.TryGetValue(key, out var composite))
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawImage(image, new Rect(sheet.Width, 0, sheet.Width, sheet.Height));
                        dc.DrawImage(GetRaw(id, 6, rawDir), new Rect(sheet.Width * 2, 0, sheet.Width, sheet.Height));
                    }
                    var bitmap = new RenderTargetBitmap(sheet.Width * 3, sheet.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
                    extendedAttacks[key] = composite = bitmap;
                }
                image = composite;
            }
        }
        if (Empty(image))
        {
            image = GetRaw(id, frame == 2 && dir is 2 or 3 ? 1 : 0, rawDir);
            if (frame == 2 && dir is 2 or 3 && image != null) image = SymmetricWalk(image);
        }
        return new(image, flip);
    }
    public double Size(string id) => objects.TryGetValue(id, out var xml) && double.TryParse(xml.Element("Size")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size / 100 : 1;
    public double Elevation(string id) => objects.TryGetValue(id, out var xml) ? Number(xml, "Z") : 0;
    public double Rotation(string id, long ms) => objects.TryGetValue(id, out var xml)
        ? Number(xml, "AngleCorrection") * Math.PI / 4 + (Number(xml, "Rotation") is var rotation && rotation != 0 ? ms / rotation : 0) : 0;
    static double Number(XElement xml, string key) => double.TryParse(xml.Element(key)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
}

public sealed class SpriteConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => ClientSprites.Current?.Get(value is CatalogEntry entry ? entry.Id : value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
