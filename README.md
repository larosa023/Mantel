# Mantel

Clean, lightweight desktop widgets for Windows. A Rainmeter alternative that looks good out of the box: drag widgets where you want them, toggle the ones you use, no skin files or scripting.

## Widgets

| Widget | What it shows |
| --- | --- |
| Clock | Time and date. Add more for world clocks, each with its own time zone and label. |
| Now Playing | Track, art and progress with previous/play/next. With [Amplite](https://github.com/larosa023/amplite) running it also switches **EQ presets**, changes volume on scroll and seeks on click. Otherwise it works with Spotify, browsers and most players. |
| Scores | Live NBA, WNBA and college games from [Fullcourt](https://github.com/larosa023/fullcourt). Click a game to open it there. |
| Weather | Current conditions, high/low, feels-like, wind and a 3-day forecast (Open-Meteo, no API key). |
| PC stats | CPU and memory usage. |
| Countdown | Days, hours and minutes to a date. |
| Disk space | Free space on each drive. |
| Network | Live download and upload speed, with a history graph. |
| Calendar | This month with today highlighted; flip months, optional week numbers. |
| Notes | A sticky note you type on right on the desktop; saves itself. |
| App launcher | Lettered tiles for your favorite apps, folders and files. |

Any widget can be added more than once (a scores widget per league, a clock per time zone), turned on or off from the tray menu or Settings, and configured in Settings. A widget that's off is never created, so it costs no CPU, memory or network. Widgets pause and hide while a full-screen game or video runs.

## Flyouts

Panels of widgets that stay out of the way until you want them:

- **Screen edge:** push the mouse against the left, right or top edge and the flyout slides out. It slides away when you move off it.
- **Taskbar icon:** a flyout can have its own icon by the clock. Click it and the panel pops up above the taskbar, like Windows' quick settings.

Mantel starts you with three, off until you turn them on in Settings: **Music** (Now Playing with Amplite's EQ, taskbar icon), **Scores** (tonight's games, taskbar icon) and **Glance** (clock, weather and PC stats, right edge). Each flyout has its own animation (slide, fade, zoom, spring or none) and speed, placement (start, center or end of its edge, full height or fitted, gap from the edge, which screen), edge delay and an optional **Ctrl + Alt** keyboard shortcut. Make your own with **+ New flyout**, and move any widget between the desktop and a flyout with its **Show on** setting. Widgets in a flyout only run while it's open.

## Themes

Nineteen themes, from small color changes to a completely different look: **Glass** (default), **Smoke**, **Light**, **Paper**, **AMOLED**, **Nord**, **Neon**, **Bare** (no cards), **Terminal**, **Catppuccin**, **Dracula**, **Tokyo Night**, **Gruvbox**, **Rosé Pine**, **Frost**, **Synthwave**, **Mono**, **Pill** and **Solarized Light**. Widget size and card opacity are adjustable for all of them. Pick the theme's own accent color, any custom color, or **Match Amplite's accent** to follow Amplite's theme live.

## Using it

- **Tray icon:** click to open Settings; right-click for widget toggles, lock, updates and quit.
- **Start menu:** opening Mantel while it's already running brings up Settings.
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
