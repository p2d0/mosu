// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Replays;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class RandomV2Settings : PlayerSettingsGroup
    {
        private readonly OsuModRandomV2 mod;
        private readonly IBeatmap beatmap;
        private readonly IReadOnlyList<Mod> mods;
        private readonly Func<Replay?> replayFunc;
        private readonly Bindable<IReadOnlyList<Mod>> songSelectMods;
        private double lastReprocessTime;

        [Resolved]
        private GameHost host { get; set; } = null!;

        public RandomV2Settings(OsuModRandomV2 mod, IBeatmap beatmap, IReadOnlyList<Mod> mods, Func<Replay?> replayFunc, Bindable<IReadOnlyList<Mod>> songSelectMods)
            : base("RandomV2 Settings")
        {
            this.mod = mod;
            this.beatmap = beatmap;
            this.mods = mods;
            this.replayFunc = replayFunc;
            this.songSelectMods = songSelectMods;

            AddRange(mod.CreateSettingsControls().Where(c => c.GetType().Name != "PlayAutoplayButton"));

            foreach (var (attr, prop) in mod.GetSettingsSourceProperties())
            {
                var bindable = prop.GetValue(mod);
                if (bindable == null)
                    continue;

                var bindableType = bindable.GetType();
                var bindMethod = bindableType.GetMethod("BindValueChanged");
                if (bindMethod == null)
                    continue;

                var eventType = bindMethod.GetParameters()[0].ParameterType;
                var eventArgType = eventType.GetGenericArguments()[0];

                var action = (Delegate)CreateHandler(eventType, eventArgType);
                bindMethod.Invoke(bindable, new object[] { action, false });
            }
        }

        private Delegate CreateHandler(Type actionType, Type eventType)
        {
            var param = Expression.Parameter(eventType, "e");
            var reprocessCall = Expression.Call(
                Expression.Constant(this),
                typeof(RandomV2Settings).GetMethod(nameof(reprocess), BindingFlags.NonPublic | BindingFlags.Instance)!);
            var lambda = Expression.Lambda(actionType, reprocessCall, param);
            return lambda.Compile();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (!isDisposing) return;

            host.UpdateThread.Scheduler.Add(() =>
            {
                var currentMods = songSelectMods.Value;
                var targetMod = currentMods.OfType<OsuModRandomV2>().FirstOrDefault();
                if (targetMod != null && targetMod != mod)
                {
                    targetMod.AimDistanceMultiplier.Value = mod.AimDistanceMultiplier.Value;
                    targetMod.PowerJumps.Value = mod.PowerJumps.Value;
                    targetMod.ExpoJumps.Value = mod.ExpoJumps.Value;
                    targetMod.RemoveStacks.Value = mod.RemoveStacks.Value;
                    targetMod.StreamDistanceMultiplier.Value = mod.StreamDistanceMultiplier.Value;
                    targetMod.PowerStreams.Value = mod.PowerStreams.Value;
                }
            });
        }

        private void reprocess()
        {
            var now = Environment.TickCount64;
            if (now - lastReprocessTime < 100)
                return;
            lastReprocessTime = now;

            mod.ApplyToBeatmap(beatmap);

            var replay = replayFunc();
            if (replay == null) return;

            var autoplay = mods.OfType<ModAutoplay>().FirstOrDefault();
            if (autoplay == null) return;

            var newReplay = autoplay.CreateReplayData(beatmap, mods).Replay;
            replay.Frames = newReplay.Frames;
        }
    }
}
