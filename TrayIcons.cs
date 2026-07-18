using System.Drawing;
using System.Drawing.Drawing2D;

namespace ImmichUploader;

public static class TrayIcons
{
    public static Icon CreateIdle()
    {
        return RenderIcon(Color.FromArgb(0, 120, 212), Color.White, "I", false);
    }

    public static Icon CreateRunning()
    {
        return RenderIcon(Color.FromArgb(0, 153, 76), Color.White, "I", true);
    }

    public static Icon CreatePaused()
    {
        return RenderIcon(Color.FromArgb(200, 130, 0), Color.White, "P", false);
    }

    public static Icon CreateError()
    {
        return RenderIcon(Color.FromArgb(200, 30, 30), Color.White, "!", false);
    }

    private static Icon RenderIcon(Color bg, Color fg, string letter, bool pulse)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var bgBrush = new SolidBrush(bg);
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var border = new Pen(Color.FromArgb(40, 0, 0, 0), 1f);
            g.DrawEllipse(border, 1, 1, 30, 30);

            using var fgBrush = new SolidBrush(fg);
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(letter, font);
            g.DrawString(letter, font, fgBrush, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);

            if (pulse)
            {
                using var dotBrush = new SolidBrush(Color.FromArgb(220, 255, 80, 0));
                g.FillEllipse(dotBrush, 22, 22, 8, 8);
                using var dotBorder = new Pen(Color.White, 1f);
                g.DrawEllipse(dotBorder, 22, 22, 8, 8);
            }
        }
        var hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }
}
