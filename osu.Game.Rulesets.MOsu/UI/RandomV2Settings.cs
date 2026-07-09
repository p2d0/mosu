// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Framework.Threading;
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
        private ScheduledDelegate? pendingReprocess;
        private readonly List<(object bindable, Delegate handler)> boundHandlers = new List<(object, Delegate)>();

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

                BindToReprocess(bindable);
            }
        }

        private void BindToReprocess(object bindable)
        {
            var bindableType = bindable.GetType();
            var bindMethod = bindableType.GetMethod("BindValueChanged");
            if (bindMethod == null)
                return;

            var eventType = bindMethod.GetParameters()[0].ParameterType;
            var eventArgType = eventType.GetGenericArguments()[0];

            var action = CreateReprocessHandler(eventType, eventArgType);
            bindMethod.Invoke(bindable, new object[] { action, false });

            boundHandlers.Add((bindable, action));
        }

        private Delegate CreateReprocessHandler(Type actionType, Type eventType)
        {
            var param = Expression.Parameter(eventType, "e");
            var reprocessCall = Expression.Call(
                Expression.Constant(this),
                typeof(RandomV2Settings).GetMethod(nameof(reprocess), BindingFlags.NonPublic | BindingFlags.Instance)!);
            return Expression.Lambda(actionType, reprocessCall, param).Compile();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (!isDisposing) return;

            foreach (var (bindable, handler) in boundHandlers)
            {
                var eventInfo = bindable.GetType().GetEvent("ValueChanged");
                eventInfo?.RemoveEventHandler(bindable, handler);
            }
            boundHandlers.Clear();

            GC.Collect();

            host.UpdateThread.Scheduler.Add(() =>
            {
                var currentMods = songSelectMods.Value;
                var targetMod = currentMods.OfType<OsuModRandomV2>().FirstOrDefault();
                if (targetMod != null && targetMod != mod)
                {
                    foreach (var (_, prop) in mod.GetSettingsSourceProperties())
                    {
                        if (prop.Name == nameof(ModRandom.Seed))
                            continue;

                        var sourceBindable = prop.GetValue(mod);
                        var targetBindable = prop.GetValue(targetMod);
                        if (sourceBindable == null || targetBindable == null)
                            continue;

                        var valueProp = sourceBindable.GetType().GetProperty("Value");
                        if (valueProp == null)
                            continue;

                        var sourceValue = valueProp.GetValue(sourceBindable);
                        valueProp.SetValue(targetBindable, sourceValue);
                    }
                }
            });
        }

        private void reprocess()
        {
            if (pendingReprocess?.Completed != true)
            {
                pendingReprocess?.Cancel();
                pendingReprocess = null;
            }

            pendingReprocess = Scheduler.AddDelayed(() =>
            {
                mod.ApplyToBeatmap(beatmap);

                var replay = replayFunc();
                if (replay == null)
                    return;

                var autoplay = mods.OfType<ModAutoplay>().FirstOrDefault();
                if (autoplay == null)
                    return;

                var newReplay = autoplay.CreateReplayData(beatmap, mods).Replay;
                replay.Frames = newReplay.Frames;

                GC.Collect();
            }, 10);
        }
    }
}
