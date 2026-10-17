# Snapview app icon

The icon returns to the original simple picture-frame symbol, recolored with a
blue/violet tile, cyan sky, teal mountains and yellow sun. It is deterministic
native vector artwork, rather than a detailed generated landscape/photo.

- `Snapview.drawing.xml`: editable WPF `DrawingImage` vector source.
- `Snapview.png`: 512px render of that source.
- `Snapview.ico`: transparent Windows frames at 16, 20, 24, 32, 40, 48, 64, 128 and 256px.
- `Snapview-mark.png`: 96px title-bar derivative.

After editing the vector source, regenerate PNG/ICO files on Windows:

```powershell
.\scripts\build-icon.ps1
dotnet build -c Release
```

The script renders the vector with WPF, then resamples/encodes icon frames. The app
embeds only the small title mark and ICO. The earlier photo-style icon is replaced.
