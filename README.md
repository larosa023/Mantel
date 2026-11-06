# Mantel

Clean, lightweight desktop widgets for Windows. A Rainmeter alternative that looks good out of the box: drag widgets where you want them, toggle the ones you use, no skin files or scripting.

## Widgets

| Widget | Status |
| --- | --- |
| Clock (with 24h and world-clock options) | ✅ |
| PC stats (CPU, memory) | ✅ |
| Now playing + EQ preset switcher | Planned |
| Weather (Open-Meteo) | Planned |
| Fullcourt scores | Planned |
| Countdown timers | Planned |

Every widget can be turned on or off from the tray menu or Settings. A widget that's off is never created, so it costs no CPU, memory or network.

## Using it

- **Tray icon:** click to open Settings; right-click for widget toggles, lock, updates and quit.
- **Move widgets:** unlock (tray menu or right-click a widget), drag, then lock again.
- **Settings and positions** are saved in `%AppData%\Mantel\settings.json`.

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
