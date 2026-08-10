
# MOsu

osu! practice ruleset with unique mods and local PP tracking.

[![Watch the video](https://img.youtube.com/vi/nWBAsAGQIVM/maxresdefault.jpg)](https://youtu.be/nWBAsAGQIVM)


## Features

- **Local PP** — (with local profiles)
- **Skip breaks** — button to skip break sections
- **ModRandomV2** — like random but you can increase the distances between objects, separate for aim and stream (configured by divisor)
  - Make any map an aim practice map
  - Make any map a stream practice map
- **ModCircleGen** — generate a long stream practice map from any map (or you can do 32x 16x triples etc...)
- **Miss Challenge** — fail if your miss count exceeds a configurable limit (1–100). Option to auto-set from your best local score for the beatmap, matching by active mods (DT rate, AR/CS).
- **Merged mods** — includes [NoSliding](https://github.com/ppy/osu/pull/22067) and [SpacingAdjust](https://github.com/ppy/osu/pull/18559)
- **Live star rating** — HUD element showing cumulative star rating as you play, updating in real-time (like pp counter)
- Mod preset import/export — share presets via JSON files
- Collection import/export — back up and share beatmap collections
  



https://github.com/user-attachments/assets/862ee4f3-dc3b-48cd-9b4a-bce583774099




- Chat mod sharing — right-click a chat line to send current mods, right-click a message to apply them, or type /md or /mods to send and click the link to apply.
<img width="749" height="359" alt="image" src="https://github.com/user-attachments/assets/545c2144-1ad1-4958-ab46-42f9550991d6" />

- Suggested songs at the expanded score view.
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/ac1c1909-a4e9-49eb-84fc-a2f12ca4c02f" />


## Install

1. Navigate to osu!lazer's data directory: Settings → General → **Open osu! folder**
2. Might want to backup client.realm just in case
3. Copy `osu.Game.Rulesets.MOsu.dll` into the `rulesets` folder
4. Restart osu!lazer

### Download

Get the latest `.dll` from the [Releases](https://github.com/p2d0/mosu/releases) page.

## Build

```sh
dotnet build osu.Game.Rulesets.MOsu/osu.Game.Rulesets.MOsu.csproj -c Release
```

Requires .NET 8.0 SDK.
