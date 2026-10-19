# Tarkov Overlay

**English** | [Русский](README.ru.md)

A lightweight Windows overlay for **Escape from Tarkov** that shows item prices right under the game's own item tooltip. Hover an item in your stash, and a small panel appears below the tooltip. It shows the flea market price, the best trader offer and the price per slot.

> Unofficial fan project. Not affiliated with or endorsed by Battlestate Games or tarkov.dev.

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

The mode hotkey tries `Ctrl+Alt+M` first. If another app already uses that, it falls back to `Ctrl+Shift+M`, then `Ctrl+Alt+P`. The log shows which one it registered.

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
- **Writes:** only `%LOCALAPPDATA%\TarkovOverlay\`:
  - `settings.json`
  - `tarkov_overlay.log`
  - the price caches (`items_cache_*.json`)
- **Desktop:** a `Tarkov Overlay.lnk` shortcut, only when you ask for it (via `install.ps1` or the tray item).
- **Network:**
  - `json.tarkov.dev` for prices
  - `github.com/tesseract-ocr` for the one-time OCR data download (`download-tessdata.ps1`)
- **Never** reads or modifies game files or game memory, never injects anything, and sends no input to the game. It doesn't touch account data or credentials.

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

## Data source

Prices come from the public [tarkov.dev](https://tarkov.dev) JSON API (`regular` and `pve` datasets) and are cached locally. Thanks to the tarkov.dev team. This project is not affiliated with them.

## Disclaimer

This tool only **reads the screen** and the game's own log files. It does not inject into, modify or automate the game, and it sends no input to it. Even so, third-party tools may conflict with the Battlestate Games EULA, and the rules can change. **Use at your own risk.** The authors take no responsibility for account actions or any other consequences.

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
- **"tessdata missing".** Run `scripts\download-tessdata.ps1`.
- **Different screen scale or resolution.** The detector scales with screen height (tuned at 1080p and up). Please open an issue with a screenshot.

## License

[MIT](LICENSE) © 2026 ArKhamDC
