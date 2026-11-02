using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ImmichUploader;

/// <summary>
/// Owns the application artwork: the window/taskbar icon (embedded
/// <c>Assets\app.ico</c>) plus the generated tray glyphs. Tray icons keep the
/// logo and wrap it in a state-coloured rounded frame, so idle / running /
/// paused / error stay distinguishable at 16-32 px without losing the mark.
/// </summary>
public static class TrayIcons
{
    private const int Size = 32;
    private const float FrameWidth = 3.5f;
    private const float FrameRadius = 7f;

    private static readonly Lazy<Icon> WindowIcon = new(LoadWindowIcon);
    private static readonly Lazy<Bitmap?> Artwork = new(LoadArtwork);

    /// <summary>Window and taskbar icon; multi-size, from the embedded .ico.</summary>
    public static Icon App => WindowIcon.Value;

    public static Icon CreateIdle() => Render(Color.FromArgb(0, 132, 224), Color.FromArgb(0, 92, 168));

    public static Icon CreateRunning() => Render(Color.FromArgb(0, 210, 108), Color.FromArgb(0, 148, 70));

    /// <summary>Dimmed running frame; alternated with <see cref="CreateRunning"/> to blink.</summary>
    public static Icon CreateRunningDim() => Render(Color.FromArgb(0, 146, 74), Color.FromArgb(0, 100, 48));

    public static Icon CreatePaused() => Render(Color.FromArgb(240, 162, 0), Color.FromArgb(188, 118, 0));

    public static Icon CreateError() => Render(Color.FromArgb(230, 54, 54), Color.FromArgb(176, 30, 30));

    private static Icon Render(Color top, Color bottom)
    {
        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // State frame: a rounded-square outline that still reads after Windows
            // scales the glyph down to the 16 px tray slot.
            var frame = new RectangleF(
                FrameWidth / 2f,
                FrameWidth / 2f,
                Size - 1f - FrameWidth,
                Size - 1f - FrameWidth);
            using (var path = RoundedRect(frame, FrameRadius))
            using (var brush = new LinearGradientBrush(
                       new RectangleF(0, 0, Size, Size), top, bottom, LinearGradientMode.Vertical))
            using (var pen = new Pen(brush, FrameWidth) { LineJoin = LineJoin.Round })
            {
                g.DrawPath(pen, path);
            }

            var inner = Size - 2 * (FrameWidth + 1f);
            var artRect = new RectangleF((Size - inner) / 2f, (Size - inner) / 2f, inner, inner);
            if (Artwork.Value is { } art)
            {
                g.DrawImage(art, artRect);
            }
            else
            {
                // A missing asset must not break the tray: fall back to a plain badge.
                using var fallback = RoundedRect(artRect, FrameRadius - 2f);
                using var solid = new SolidBrush(bottom);
                g.FillPath(solid, fallback);
            }
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon LoadWindowIcon()
    {
        using var stream = OpenResource("ImmichUploader.Assets.app.ico");
        if (stream is not null)
        {
            using var icon = new Icon(stream);
            return (Icon)icon.Clone();
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static Bitmap? LoadArtwork()
    {
        using var stream = OpenResource("ImmichUploader.Assets.app-small.png");
        if (stream is null) return null;

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Stream? OpenResource(string name)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
