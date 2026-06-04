# AGENTS.md

This is a standalone ruleset project using NuGet package references to `ppy.osu.Game` and `ppy.osu.Game.Rulesets.Osu`.

## Source reference

See [LOCAL.md](LOCAL.md) for paths to the full osu! source tree on this machine.

## Build

```sh
dotnet build osu.Game.Rulesets.MOsu/osu.Game.Rulesets.MOsu.csproj -c Release
```

## Key conventions

- Global type aliases in `osu.Game.Rulesets.MOsu/GlobalTypeAliases.cs` redirect to osu! core types — check there before adding new files
- Realm models in `osu.Game.Rulesets.MOsu/Models/` use `[assembly: Explicit]` registration

## UI caveats

- **No `RelativeSizeAxes = Axes.Both` inside auto-sizing FillFlowContainer** — `FillFlowContainer` with `AutoSizeAxes = Axes.Y` (or `Axes.Both`) will crash with `InvalidOperationException` if any child uses `RelativeSizeAxes` on the axis the container is auto-sizing. Use `AutoSizeAxes` or fixed dimensions instead.
- **Realm objects on game thread** — Realm model properties (e.g. `LocalProfile.Name`) must not be accessed from callbacks running on the game thread after `await`. Cache the value as a plain string in the constructor.

## Tagging releases

Versioned tags use semver `X.Y.Z` format (e.g. `1.0.18`).

```sh
# Check latest tag
git tag -l | tail -1

# Create annotated version tag
git tag -a 1.0.18 -m "release notes here"

# Push commit + tag
git push origin HEAD --tags
```
