using System.Drawing;
using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

/// <summary>
/// Guards the app/tray artwork: the exe icon and tray glyphs are loaded from
/// embedded resources, and the tray frames must stay colour-coded per state.
/// </summary>
public class TrayIconTests
{
    /// <summary>Average RGB of the top edge of the state frame (the state colour).</summary>
    private static (int R, int G, int B) FrameColour(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        long r = 0, g = 0, b = 0;
        var count = 0;
        for (var y = 1; y <= 3; y++)
        {
            for (var x = 8; x < 24; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }
        }

        return ((int)(r / count), (int)(g / count), (int)(b / count));
    }

    [Fact]
    public void TrayIcons_RenderAtTraySize()
    {
        using var idle = TrayIcons.CreateIdle();
        using var running = TrayIcons.CreateRunning();
        using var runningDim = TrayIcons.CreateRunningDim();
        using var paused = TrayIcons.CreatePaused();
        using var error = TrayIcons.CreateError();

        foreach (var icon in new[] { idle, running, runningDim, paused, error })
        {
            Assert.Equal(32, icon.Width);
            Assert.Equal(32, icon.Height);
            Assert.NotEqual(IntPtr.Zero, icon.Handle);
        }
    }

    [Fact]
    public void TrayIcons_StateFramesAreColourCoded()
    {
        using var idle = TrayIcons.CreateIdle();
        using var running = TrayIcons.CreateRunning();
        using var paused = TrayIcons.CreatePaused();
        using var error = TrayIcons.CreateError();

        var idleColour = FrameColour(idle);
        var runningColour = FrameColour(running);
        var pausedColour = FrameColour(paused);
        var errorColour = FrameColour(error);

        // Idle is blue-dominant, running green-dominant, paused amber, error red.
        Assert.True(idleColour.B > idleColour.R + 60, $"idle was {idleColour}");
        Assert.True(runningColour.G > runningColour.R + 60 && runningColour.G > runningColour.B, $"running was {runningColour}");
        Assert.True(pausedColour.R > 150 && pausedColour.G > 80 && pausedColour.B < 60, $"paused was {pausedColour}");
        Assert.True(errorColour.R > 150 && errorColour.G < 90 && errorColour.B < 90, $"error was {errorColour}");
    }

    [Fact]
    public void TrayIcons_RunningBlinkFrameIsDimmerButStillGreen()
    {
        using var bright = TrayIcons.CreateRunning();
        using var dim = TrayIcons.CreateRunningDim();

        var brightColour = FrameColour(bright);
        var dimColour = FrameColour(dim);

        Assert.True(dimColour.G > dimColour.R + 40 && dimColour.G > dimColour.B, $"dim was {dimColour}");
        Assert.True(dimColour.G < brightColour.G, $"dim {dimColour} should be darker than bright {brightColour}");
    }

    [Fact]
    public void TrayIcons_ExposeWindowIcon()
    {
        var icon = TrayIcons.App;
        using var bitmap = icon.ToBitmap();

        Assert.True(bitmap.Width >= 32);
        Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A);
    }

    [Fact]
    public void TrayIcons_ArtworkIsEmbedded()
    {
        var resources = typeof(TrayIcons).Assembly.GetManifestResourceNames();

        Assert.Contains("ImmichUploader.Assets.app.ico", resources);
        Assert.Contains("ImmichUploader.Assets.app-small.png", resources);
    }
}
