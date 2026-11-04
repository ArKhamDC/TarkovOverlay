# Tarkov Overlay

**English** | [Русский](README.ru.md)

A lightweight Windows overlay for **Escape from Tarkov** that shows item prices right under the game's own item tooltip. Hover an item in your stash, and a small panel appears below the tooltip. It shows the flea market price, the best trader offer and the price per slot.

> Unofficial fan project. Not affiliated with or endorsed by Battlestate Games or tarkov.dev.

**[⬇ Download the latest release](https://github.com/ArKhamDC/TarkovOverlay/releases/latest)** — ready-to-run Windows build, no .NET install needed.

## Features

- **Hover price panel.** The overlay detects the game's dark item tooltip near the cursor and reads the item name with OCR (Tesseract). It then shows a panel aligned to the tooltip's left edge with:
  - the flea market price: lowest offer and 24h average
  - the best trader and their price
  - the price per inventory slot (with the item size)
  - Missing lines are left out. `X` means no price data at all.
- **Fast.** Each tooltip is read once and cached. The panel appears roughly 100–150 ms after the tooltip and disappears as soon as the tooltip does.
- **Game modes:** `auto`, `pvp`, `pve`, `season`.
  - `auto` reads the game's own log files (read-only) to detect PvE or PvP. If it finds nothing, it uses your last manual choice.
  - Season: tarkov.dev has no seasonal flea data, so Season mode shows trader prices only (panel header `[Сезон · только торговцы]`; the UI is in Russian).
  - The current mode appears in the panel header (`[PvE]`, `[PvP]`) and as a short toast when you switch.
- **Tray icon** with a mode switcher, a price refresh, a "Create desktop shortcut" item, and buttons to open the log and settings folder or exit.
- **Single instance.** Launching the overlay a second time does nothing.
- **Optional full-screen tile labels** (experimental, off by default).

### Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+O` | Show / hide the overlay |
| `Ctrl+Alt+R` | Refresh prices now |
| `Ctrl+Shift+M` | Cycle price mode: auto → PvP → PvE → Season |
| `Ctrl+Alt+X` | Exit |
| `Ctrl+Alt+G` | GPS arrow on / off |
| `Ctrl+Alt+N` | Map window show / hide (falls back to `Ctrl+Alt+K` if N is taken) |

The mode hotkey tries `Ctrl+Alt+M` first. If another app already uses that, it falls back to `Ctrl+Shift+M`, then `Ctrl+Alt+P`. The log shows which one it registered. GPS and map hotkeys are skipped if another app already owns them; the tray items still work.

## GPS and map

In a raid the overlay can press the in-game screenshot key (Print Screen by default, every 10 seconds) and read your position and facing from the **file name** Escape from Tarkov already writes. It does not open the image, read game memory, or inject code.

- An arrow on the price overlay points at the nearest extract and shows the distance in meters.
- A separate map window draws a top-down underlay for the current raid: Customs, Reserve, Woods, Shoreline, Interchange, Factory (day, night, and the infected variant), Lighthouse, Streets of Tarkov, Ground Zero, The Lab, Labyrinth, and Terminal. Your marker, heading, and extracts are drawn on top of that image. The picture keeps its aspect ratio when you resize the window (empty bands on the sides are fine). Drag the title bar to move it, drag the edges or corners to resize it, and set opacity from the tray slider. Position, size, and opacity are saved in `settings.json`. Icebreaker, and any map that does not have a calibrated image yet, stays on the simple dot schematic.
- Hiding the map does not turn off the arrow. Hiding the arrow does not forget the map window. `Ctrl+Alt+O` still hides the whole overlay.
- After a file name is parsed, that screenshot is deleted (`GpsDeleteAfterParse`, default on) so the folder does not fill up. Turn auto-capture off in the tray if you only want manual screenshots.
- The current map comes from the game's own log files (the same logs used for PvP/PvE/Season). Outside a raid the arrow and the map hide.

The underlay sits on the public [tarkov.dev](https://tarkov.dev) map frames (bounds and `coordinateRotation` from their MIT-licensed map data, checked against that API's spawn and extract coordinates). Roads and paths are drawn between those published landmarks, with water and building footprints, so the picture shows the road network. The tarkov.dev SVG maps, and the tiles made from them, are CC BY-NC-SA and their authors forbid using them in an in-raid overlay, so they are not included. MapGenie tiles and the game's own map textures are not licensed for redistribution. This is not an official game map. Terminal and Labyrinth have no published road geometry, so those two stay a plain frame. The window title and the arrow line add the floor when your height is inside a published floor band (for example Dorms 2nd floor, or Reserve bunkers). Interior floor plans of every building are not in the data we can ship. Pressing the screenshot key is ordinary input. It does not move your character, buy or sell items, or read account data.

## How it works

1. **Tooltip detection.** About every 60 ms, while the game window is active, the overlay captures a small area around the mouse cursor. It looks for the game's tooltip, a dark box with light text, using a simple pixel threshold and connected-component search.
2. **OCR.** It crops only that tooltip and reads it with Tesseract (the `tessdata` best models, 2× upscale). Results are cached by a hash of the tooltip pixels, so the same tooltip is only read once.
3. **Name match.** The text is normalized and matched against full item names: an exact match first, then a fuzzy one, then the short name or a prefix.
4. **Price lookup.** Prices come from a local copy of the [json.tarkov.dev](https://json.tarkov.dev) item list (`pve` and `regular`), refreshed every 20 minutes.
5. **Panel.** A transparent, click-through, topmost WPF window draws the panel just under the tooltip. The panel is hidden as soon as the tooltip disappears or changes.
6. **Mode detection** (`auto`). The newest game log folder (`<game>\Logs\log_*`) is read every 5 s, read-only. The game server host in `backend_*.log` (`gw-pve…` = PvE, `gw-pvp…`/`prod…` = PvP) or the `mode:` line in `application_*.log` decides the mode. Only these patterns are extracted; nothing else from the logs is stored.

## What files it touches

- **Reads:**
  - the game's `Logs` folder (install path from the registry key `HKLM\...\Uninstall\EscapeFromTarkov`), read-only
  - screen pixels near the cursor
  - screenshot **file names** in `Documents\Escape from Tarkov\Screenshots` (the image is not opened)
- **Writes:** only `%LOCALAPPDATA%\TarkovOverlay\`:
  - `settings.json`
  - `tarkov_overlay.log`
  - the price caches (`items_cache_*.json`) and the map cache (`maps_cache.json`)
- **Desktop:** a `Tarkov Overlay.lnk` shortcut, only when you ask for it (via `install.ps1` or the tray item).
- **Network:**
  - `json.tarkov.dev` for prices and map extracts
  - `github.com/tesseract-ocr` for the one-time OCR data download (`download-tessdata.ps1`)
- **May delete** in-raid screenshots after their file names have been parsed, when GPS delete is on.
- **May press** the in-game screenshot key while you are in a raid, the game window is focused, and GPS auto-capture is on. It does not click, move, buy, or sell.
- **Never** reads game memory, never injects code, and does not touch account data or credentials.

## Requirements

- Windows 10/11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0); building from source needs the .NET 8 SDK
- The game running in **Windowed** or **Borderless** mode. Exclusive full-screen hides overlays.
- Game client language: Russian or English. By default (`auto`) item names are matched in both languages, so either client works without changes.
- Interface language: Russian or English, chosen automatically from the Windows display language. Change it in the tray menu under **Language / Язык** (the overlay restarts to apply it).

## Install

```powershell
git clone https://github.com/ArKhamDC/TarkovOverlay
cd TarkovOverlay\TarkovOverlay
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Run
```

`install.ps1` does the following:
1. Downloads the Tesseract language data (`scripts\download-tessdata.ps1`) if it's missing.
2. Builds the Release version.
3. Creates a **Tarkov Overlay** shortcut on your Desktop.

Add `-NoShortcut` to skip the shortcut, or `-Run` to start the overlay afterwards.

To build manually instead:

```powershell
cd TarkovOverlay
powershell -ExecutionPolicy Bypass -File scripts\download-tessdata.ps1
dotnet build -c Release
.\bin\Release\net8.0-windows\TarkovOverlay.exe
```

## Settings

Settings live in `%LOCALAPPDATA%\TarkovOverlay\settings.json`, which is created on first run. The log (`tarkov_overlay.log`) and the price cache are in the same folder.

| Key | Default | Meaning |
|---|---|---|
| `ModeChoice` | `auto` | `auto`, `pvp`, `pve` or `season` |
| `LastManualMode` | `pve` | Fallback for `auto` when the game logs give no signal |
| `Language` | `auto` | Game client language for item names: `auto` (Russian + English), `ru` or `en` |
| `UiLanguage` | `auto` | Overlay interface language: `auto` (from Windows), `ru` or `en` |
| `TooltipIntervalMs` | `60` | How often to look for a tooltip |
| `RefreshMinutes` | `20` | Price refresh interval |
| `RequireGameForeground` | `true` | Only work while the game window is active |
| `ExcludeOverlayFromCapture` | `true` | Hide the overlay from screen capture so OCR doesn't read its own panels |
| `AutoFullScan` | `false` | Experimental labels on every stash tile |
| `ScanIntervalMs`, `OcrThreads` | `350`, `0` | Full-scan tuning (`0` = auto) |
| `GpsEnabled` | `true` | Arrow to the nearest extract |
| `GpsAutoScreenshot` | `true` | Press the screenshot key during a raid |
| `GpsAutoScreenshotSeconds` | `10` | How often (2–600) |
| `GpsDeleteAfterParse` | `true` | Delete a screenshot after its file name is parsed |
| `GpsScreenshotsPath` | empty | Blank uses `Documents\Escape from Tarkov\Screenshots` |
| `GpsScreenshotKey` | `PrintScreen` | `PrintScreen`, `F12`, a letter, or a virtual-key code |
| `GpsMapVisible` | `true` | Map window, separate from the arrow |
| `GpsMapLeft`, `GpsMapTop` | `24`, `80` | Map window position (also saved when you drag it) |
| `GpsMapWidth`, `GpsMapHeight` | `420`, `320` | Map window size (also saved when you resize it) |
| `GpsMapOpacity` | `0.85` | Map window opacity, 0.3–1 (tray slider) |

## Data source

Prices come from the public [tarkov.dev](https://tarkov.dev) JSON API (`regular` and `pve` datasets) and are cached locally. Thanks to the tarkov.dev team. This project is not affiliated with them.

## Disclaimer

The price overlay **reads the screen** and the game's own log files. GPS reads screenshot **file names** and those same logs, and it can press the screenshot key you already use in game. It does not inject into the game, read game memory, or play the game for you. Even so, third-party tools may conflict with the Battlestate Games EULA, and the rules can change. **Use at your own risk.** The authors take no responsibility for account actions or any other consequences.

## Troubleshooting

- **No panel appears.**
  - Make sure the game is in Windowed or Borderless mode and is the active window.
  - Check `tarkov_overlay.log` for `[Tooltip]` lines.
- **Wrong or no match.**
  - Make sure `Language` is `auto` or matches the game language.
  - Press `Ctrl+Alt+R` to refresh prices.
- **Wrong prices for your mode.**
  - Switch the mode from the tray or with the hotkey.
  - On `auto`, the log shows the detected mode in `[Mode]` lines.
- **Hotkey doesn't work.** Another app has it registered. See the `[Hotkeys]` line in the log.
- **GPS arrow never appears.** Be in a raid (the log should show `[GPS] рейд начался`), with the game window focused if auto-screenshot is on. A manual in-raid screenshot updates it too. If the game runs as administrator, start the overlay as administrator so the screenshot key is delivered.
- **Map is in the way.** Drag its title bar, resize from the edges, or hide it from the tray / `Ctrl+Alt+N`. Opacity is the tray slider and `GpsMapOpacity`. The picture scales with the window and keeps its aspect ratio.
- **Map is only dots.** That raid has no calibrated underlay (Icebreaker, or a map added after this build). Extracts and the player arrow still draw.
- **"tessdata missing".** Run `scripts\download-tessdata.ps1`.
- **Different screen scale or resolution.** The detector scales with screen height (tuned at 1080p and up). Please open an issue with a screenshot.

## License

[MIT](LICENSE) © 2026 ArKhamDC
