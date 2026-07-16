// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.MOsu.Screens.Play.HUD
{
    public abstract partial class PerformancePointsCounter : RollingCounter<int>
    {
        public bool UsesFixedAnchor { get; set; }

        [Resolved]
        private ScoreProcessor scoreProcessor { get; set; } = null!;

        [Resolved]
        private GameplayState gameplayState { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        [CanBeNull]
        private List<TimedDifficultyAttributes> timedAttributes = null!;

        private readonly CancellationTokenSource loadCancellationSource = new CancellationTokenSource();
        private ModSettingChangeTracker? modSettingChangeTracker;
        private ScheduledDelegate? debouncedRefresh;

        private JudgementResult lastJudgement = null!;
        private PerformanceCalculator performanceCalculator = null!;
        private ScoreInfo scoreInfo = null!;

        private Mod[] clonedMods = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (gameplayState != null)
            {
                performanceCalculator = gameplayState.Ruleset.CreatePerformanceCalculator()!;
                clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();

                scoreInfo = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset) { Mods = clonedMods };

                modSettingChangeTracker = new ModSettingChangeTracker(gameplayState.Mods);
                modSettingChangeTracker.SettingChanged += _ => refreshTimedAttributes();

                refreshTimedAttributes();
            }
        }

        private int refreshGeneration;

        private void refreshTimedAttributes()
        {
            if (gameplayState == null) return;

            debouncedRefresh?.Cancel();
            debouncedRefresh = Scheduler.AddDelayed(() =>
            {
                int generation = ++refreshGeneration;
                clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();

                difficultyCache.GetTimedDifficultyAttributesAsync(beatmap.Value, gameplayState.Ruleset, clonedMods, loadCancellationSource.Token)
                               .ContinueWith(task => Schedule(() =>
                               {
                                   if (generation != refreshGeneration)
                                       return;

                                   timedAttributes = task.GetResultSafely();

                                   IsValid = true;

                                   if (lastJudgement != null)
                                       onJudgementChanged(lastJudgement);
                               }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }, 100);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (scoreProcessor != null)
            {
                scoreProcessor.NewJudgement += onJudgementChanged;
                scoreProcessor.JudgementReverted += onJudgementChanged;
            }

            if (gameplayState?.LastJudgementResult.Value != null)
                onJudgementChanged(gameplayState.LastJudgementResult.Value);
        }

        public virtual bool IsValid { get; set; }

        private void onJudgementChanged(JudgementResult judgement)
        {
            lastJudgement = judgement;

            var attrib = getAttributeAtTime(judgement);

            if (gameplayState == null || attrib == null || scoreProcessor == null)
            {
                IsValid = false;
                return;
            }

            scoreInfo.Mods = clonedMods;
            scoreProcessor.PopulateScore(scoreInfo);
            Current.Value = (int)Math.Round(performanceCalculator?.Calculate(scoreInfo, attrib).Total ?? 0, MidpointRounding.AwayFromZero);
            IsValid = true;
        }

        [CanBeNull]
        private DifficultyAttributes? getAttributeAtTime(JudgementResult judgement)
        {
            if (timedAttributes == null || timedAttributes.Count == 0)
                return null;

            int attribIndex = timedAttributes.BinarySearch(new TimedDifficultyAttributes(judgement.HitObject.GetEndTime(), null));
            if (attribIndex < 0)
                attribIndex = ~attribIndex - 1;

            return timedAttributes[Math.Clamp(attribIndex, 0, timedAttributes.Count - 1)].Attributes;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (scoreProcessor != null)
            {
                scoreProcessor.NewJudgement -= onJudgementChanged;
                scoreProcessor.JudgementReverted -= onJudgementChanged;
            }

            modSettingChangeTracker?.Dispose();
            debouncedRefresh?.Cancel();
            loadCancellationSource?.Cancel();
        }
    }
}
