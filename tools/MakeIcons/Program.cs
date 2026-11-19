using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

// Turns the Coodsz banner SVGs in assets\icons into the .ico files the exes use (ApplicationIcon).
//
//   dotnet run --project tools\MakeIcons -- <icons folder> [preview folder]
//
// For every name.svg it writes name.ico beside it with 16, 32, 48 and 256 px images as PNG entries.
// Only the few SVG parts the banners use are read: rect (with rx), circle and path, with fill, stroke,
// stroke-width and stroke-linecap. Path data goes to WPF's Geometry.Parse, which knows M H V L C A Z.
// At 16 px the rod and its end caps are left out and the banner fills the image, with thinner outlines,
// so the C on the shield stays readable. With a preview folder each size is also saved as a PNG.
var folder = args.Length > 0 ? args[0] : Path.Combine("assets", "icons");
var preview = args.Length > 1 ? args[1] : null;
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine("The icons folder does not exist: " + folder);
    return 1;
}

var failed = 0;
// WPF draws only on an STA thread.
var thread = new Thread(() =>
{
    foreach (var svg in Directory.GetFiles(folder, "*.svg").OrderBy(path => path, StringComparer.Ordinal))
    {
        try
        {
            var icon = IconMaker.Load(svg);
            var images = IconMaker.Sizes.Select(size => (size, png: IconMaker.Png(icon, size))).ToList();
            var ico = Path.ChangeExtension(svg, ".ico");
            File.WriteAllBytes(ico, IconMaker.Ico(images));
            Console.WriteLine(ico + "  " + string.Join(", ", images.Select(image => image.size + " px")));
            if (preview != null)
            {
                Directory.CreateDirectory(preview);
                foreach (var (size, png) in images)
                    File.WriteAllBytes(Path.Combine(preview, Path.GetFileNameWithoutExtension(svg) + "-" + size + ".png"), png);
                // The 16 px image ten times bigger with hard pixels, to judge it by eye.
                File.WriteAllBytes(Path.Combine(preview, Path.GetFileNameWithoutExtension(svg) + "-16-zoom.png"), IconMaker.Zoom(images[0].png, 10));
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException or System.Xml.XmlException)
        {
            Console.Error.WriteLine(svg + ": " + ex.Message);
            failed++;
        }
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return failed == 0 ? 0 : 1;

internal sealed record IconShape(Geometry Geometry, Brush? Fill, Brush? Stroke, double StrokeWidth, PenLineCap Cap, bool RodPart);

internal sealed record SvgIcon(Rect ViewBox, IReadOnlyList<IconShape> Shapes);

internal static class IconMaker
{
    public static readonly int[] Sizes = [16, 32, 48, 256];

    // The 16 px image shows only the banner (x 24 to 104, y 15 to 108 in the 128 viewBox), with room for its outline.
    private static readonly Rect SmallView = new(16, 13.5, 96, 96);

    public static SvgIcon Load(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new FormatException("The SVG is empty.");
        var box = Numbers((string?)root.Attribute("viewBox") ?? "0 0 128 128");
        if (box.Length != 4)
            throw new FormatException("The viewBox needs four numbers.");
        var shapes = new List<IconShape>();
        foreach (var element in root.Elements())
        {
            Geometry geometry;
            switch (element.Name.LocalName)
            {
                case "rect":
                    var rx = Number(element, "rx");
                    geometry = new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height")), rx, rx);
                    break;
                case "circle":
                    var r = Number(element, "r");
                    geometry = new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), r, r);
                    break;
                case "path":
                    // F1 is SVG's default nonzero fill rule.
                    geometry = Geometry.Parse("F1 " + ((string?)element.Attribute("d") ?? throw new FormatException("A path has no d.")));
                    break;
                default:
                    continue;
            }

            geometry.Freeze();
            var cap = (string?)element.Attribute("stroke-linecap") switch
            {
                "round" => PenLineCap.Round,
                "square" => PenLineCap.Square,
                _ => PenLineCap.Flat
            };
            // The rod across the top is a rect with its two round end caps.
            var rodPart = element.Name.LocalName is "rect" or "circle";
            shapes.Add(new IconShape(geometry, Paint(element, "fill", "#000000"), Paint(element, "stroke", null),
                element.Attribute("stroke-width") == null ? 1 : Number(element, "stroke-width"), cap, rodPart));
        }

        return new SvgIcon(new Rect(box[0], box[1], box[2], box[3]), shapes);
    }

    public static byte[] Png(SvgIcon icon, int size)
    {
        var small = size <= 16;
        var view = small ? SmallView : icon.ViewBox;
        var scale = size / Math.Max(view.Width, view.Height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(scale, 0, 0, scale, -view.X * scale, -view.Y * scale));
            foreach (var shape in icon.Shapes)
            {
                if (small && shape.RodPart)
                    continue;
                Pen? pen = null;
                if (shape.Stroke != null && shape.StrokeWidth > 0)
                {
                    // At 16 px the gold outlines (3 wide) go to 1.5, so the shield keeps room for the
                    // C, and the C (7 wide) goes to 9, about one and a half pixels.
                    var width = !small ? shape.StrokeWidth : shape.StrokeWidth < 5 ? 1.5 : 9;
                    pen = new Pen(shape.Stroke, width) { StartLineCap = shape.Cap, EndLineCap = shape.Cap, LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                }

                context.DrawGeometry(shape.Fill, pen, shape.Geometry);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static byte[] Zoom(byte[] png, int factor)
    {
        var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)), null, new Rect(0, 0, source.PixelWidth * factor, source.PixelHeight * factor));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth * factor, source.PixelHeight * factor));
        }

        var bitmap = new RenderTargetBitmap(source.PixelWidth * factor, source.PixelHeight * factor, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico file: ICONDIR, one ICONDIRENTRY per image, then each PNG as it is.</summary>
    public static byte[] Ico(IReadOnlyList<(int size, byte[] png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            // 256 is written as 0.
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
            writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    private static double Number(XElement element, string name) =>
        double.Parse((string?)element.Attribute(name) ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double[] Numbers(string text) =>
        text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(part => double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    private static Brush? Paint(XElement element, string name, string? fallback)
    {
        var value = (string?)element.Attribute(name) ?? fallback;
        if (value == null || value == "none")
            return null;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
