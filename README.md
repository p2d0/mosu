# MOsu

A custom osu! ruleset with unique mods and local PP tracking.

## Mods

- **Autopilot** — cursor movement is automated, only clicking required
- **Classic** — osu! classic mod with old-style mechanics
- **Freeze Frame** — freeze on miss for recovery practice
- **Hidden** — fading approach circles
- **No Sliding** — disables sliding on sliders
- **Random V2** — randomizes hit object positions
- **Relax** — aim assist, only tapping required
- **Spacing Adjust** — adjust approach rate and circle size
- **Square** — replaces patterns with square-based formations
- **Strict Tracking** — enforces strict cursor tracking
- **Target Practice** — generated target practice maps
- **Traceable** — visible cursor trails and path indicators

## Features

- Local PP calculation and statistics
- Mod presets with import/export
- Per-beatmap mod presets (BeatmapModPreset)
- Chat mod sharing via encoded links (`osu://preset/...`)
- Built-in preset collection
- Local user profile with performance graphs and ranked scores

## Install

1. Navigate to osu!lazer's data directory: Settings → General → **Open osu! folder**
2. Copy `osu.Game.Rulesets.MOsu.dll` into the `rulesets` folder
3. Restart osu!lazer

### Download

Get the latest `.dll` from the [Releases](https://github.com/p2d0/mosusu/releases) page.

## Build

```sh
dotnet build osu.Game.Rulesets.MOsu/osu.Game.Rulesets.MOsu.csproj -c Release
```

Requires .NET 8.0 SDK.
