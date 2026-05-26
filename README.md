# MOsu

A custom osu! ruleset with unique mods and local PP tracking.

## Mods

- **No Sliding** — disables sliding on sliders
- **Random V2** — randomizes hit object positions with configurable patterns
- **Spacing Adjust** — adjust approach rate and circle size with extended limits

## Features

- **Local PP** — calculates and tracks performance points locally
- **Skip breaks** — button to skip break sections
- **ModRandomV2** — randomizes hit object positions with configurable patterns
- **Merged mods** — includes [NoSliding](https://github.com/ppy/osu/pull/22067) and [SpacingAdjust](https://github.com/ppy/osu/pull/18559)
- Mod presets with import/export
- Chat mod sharing — right-click a chat line to send current mods, right-click a message to apply them
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
