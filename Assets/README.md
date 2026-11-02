# Branding assets

| File | Role |
|------|------|
| `app.png` | Large master artwork (source for the big `.ico` frames and the installer/`README` artwork). |
| `app-small.png` | Simplified small master; embedded at runtime and drawn inside every tray glyph. |
| `app.ico` | Windows icon: 16/20/24/32 px frames from `app-small.png`, 40/48/64/128 px + a 256 px PNG frame from `app.png`. |

`app.ico` is referenced by `<ApplicationIcon>` in `ImmichUploader.csproj` (exe, shortcut,
taskbar, Add/Remove Programs) and `SetupIconFile` in `ImmichUploader.iss`. The `.ico` and
`app-small.png` are also embedded as resources (`ImmichUploader.Assets.*`) so
`TrayIcons.cs` can build the window icon and the state-coloured tray glyphs at runtime
without shipping loose files.

When the artwork changes, regenerate `app.ico` from the two PNGs (small master for the
16-32 px frames, large master for the rest) and keep every listed size: Windows picks a
frame per DPI/surface, and a missing small frame is what makes a tray/taskbar icon look
blurry.
