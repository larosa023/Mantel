# Mantel

Clean, lightweight desktop widgets for Windows. A Rainmeter alternative that looks good out of the box: drag widgets where you want them, toggle the ones you use, no skin files or scripting.

## Widgets

| Widget | What it shows |
| --- | --- |
| Clock | Time and date. Add more for world clocks, each with its own time zone and label. |
| Now Playing | Track, artist, album art and progress from Spotify, browsers and most players, with previous/play/next. Hides when nothing's playing. |
| Weather | Current conditions, high/low, feels-like, wind and a 3-day forecast (Open-Meteo, no API key). |
| PC stats | CPU and memory usage. |
| Countdown | Days, hours and minutes to a date. Add as many as you like. |
| Disk space | Free space on each drive. |
| Network | Live download and upload speed. |
| Fullcourt scores | Planned |

Every widget can be turned on or off from the tray menu or Settings, and configurable widgets have their options in Settings. A widget that's off is never created, so it costs no CPU, memory or network. Widgets also pause and hide while a full-screen game or video is running.

## Using it

- **Tray icon:** click to open Settings; right-click for widget toggles, lock, updates and quit.
- **Move widgets:** unlock (tray menu or right-click a widget), drag, then lock again.
- **Snapping:** dragged widgets snap to an 8px grid and to screen edges (can be turned off).
- **Settings and positions** are saved in `%AppData%\Mantel\settings.json`; a log is kept next to it.

## Updates

Mantel updates itself with [Velopack](https://velopack.io) from this repo's GitHub Releases:

1. Checks 30 seconds after launch and every 4 hours after that.
2. Downloads in the background. Updates are delta packages, usually a few hundred KB.
3. Applies the update when you've been away for 10 minutes, when you quit Mantel, or on next launch. Widgets come back where they were.

It installs per-user, so neither installing nor updating ever asks for admin rights. Settings has toggles for automatic updates and the beta channel, plus a "Check now" button.

## Development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/Mantel
```

Dev builds run normally but skip update checks, since they aren't installed through Velopack.

### Adding a widget

1. Create a class under `src/Mantel/Widgets/` that extends `TimedWidget` (or implements `IWidget`).
2. Register it with one line in `Core/WidgetRegistry.cs`.

### Releasing

Bump `<Version>` in `src/Mantel/Mantel.csproj` and push to `main`. GitHub Actions tags, builds, packages and publishes the release. Pushing a `v*` tag or creating a release on GitHub also works.

A version with a suffix, such as `0.2.0-beta.1`, publishes to the beta channel as a prerelease. Only people with "Beta updates" turned on receive it.

The first release produces `Mantel-win-Setup.exe` on the Releases page. Install from that once, and every later release arrives on its own.
