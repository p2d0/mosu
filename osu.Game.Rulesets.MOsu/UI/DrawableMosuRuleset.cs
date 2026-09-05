// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.MOsu.Delta.Objects.Drawables;
using osu.Game.Rulesets.MOsu.Delta.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.MOsu.Delta.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Screens.Play;
using osuTK;
using osu.Framework.Threading;
using System.Reflection;
using System.Threading.Tasks;
using osu.Game.Database;
using osu.Framework.Screens;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class DrawableMosuRuleset : DrawableRuleset<OsuHitObject>
    {
        private Bindable<bool>? cursorHideEnabled;

        public new MosuInputManager KeyBindingInputManager => (MosuInputManager)base.KeyBindingInputManager;

        public new MOsuPlayfield Playfield => (MOsuPlayfield)base.Playfield;

        protected new OsuRulesetConfigManager Config => (OsuRulesetConfigManager)base.Config;

        public DrawableMosuRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
            this.playableBeatmap = beatmap!;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // [Resolved] fields come back null in the ruleset context (gameplay and editor alike),
            // so capture the parent chain and resolve dependencies manually (same pattern the editor ruleset uses).
            parentDependencies = parent;

            // Apply gimmicks (including the in-place fake-object replacement) before loadObjects
            // enumerates the playable's HitObjects: mutating the list mid-enumeration throws.
            DeltaGimmickRuntime.TryEnsureApplied(playableBeatmap!, parent, mods: Mods);
            notifyScoreProcessorGimmicksApplied();

            var osuConfig = (OsuRulesetConfigManager?)parent.Get<IRulesetConfigCache>().GetConfigFor(new osu.Game.Rulesets.Osu.OsuRuleset());
            var dependencies = base.CreateChildDependencies(parent);
            if (osuConfig != null)
            {
                var wrapper = new DependencyContainer(dependencies);
                wrapper.Cache(osuConfig);
                return wrapper;
            }
            return dependencies;
        }

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        [Resolved]
        private GameplayClockContainer GameplayClockContainer { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;


        // public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        // {
        //     if (e.Repeat)
        //         return false;

        //     switch (e.Action)
        //     {
        //         case GlobalAction.SkipCutscene:
        //             BreakTracker breakTracker = (BreakTracker)FrameStableComponents.First(p => p is BreakTracker);
        //             //     beatmap.Value.Track.Seek(breakTracker.CurrentPeriod.Value.Value.End);
        //             // Schedule(() => {
        //             if(beatmap.Value.Track.IsLoaded && breakTracker.CurrentPeriod.Value.HasValue){
        //                 // samplePlaybackDisabled.Value = true;
        //                 Player.Seek(breakTracker.CurrentPeriod.Value.Value.End);
        //                 // (GameplayClockContainer as MasterGameplayClockContainer)?.Start();
        //                 // samplePlaybackDisabled.Value = FrameStableClock.IsCatchingUp.Value || GameplayClockContainer.IsPaused.Value;
        //             }
        //             // });
        //             return true;
        //     }

        //     return false;
        // }

        // public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        // {
        // }

        // protected override bool OnKeyDown(KeyDownEvent e)
        // {
        //     if (e.Key == Key.Space)
        //     {
        //         BreakTracker breakTracker = (BreakTracker)FrameStableComponents.First(p => p is BreakTracker);
        //         if(breakTracker.CurrentPeriod.Value.HasValue)
        //             beatmap.Value.Track.Seek(breakTracker.CurrentPeriod.Value.Value.End);
        //     }

        //     return base.OnKeyDown(e);
        // }

        private ScheduledDelegate? frameStablePlaybackResetDelegate;
        private IBeatmap playableBeatmap = null!;
        private static readonly PropertyInfo? frameStablePlaybackProperty =
            typeof(DrawableRuleset).GetProperty("FrameStablePlayback", BindingFlags.Instance | BindingFlags.NonPublic);

        private void SafeSeek(double time)
        {
            if (GameplayClockContainer == null || frameStablePlaybackProperty == null)
                return;

            // Cancel any pending frame-stable restore
            if (frameStablePlaybackResetDelegate?.Cancelled == false && !frameStablePlaybackResetDelegate.Completed)
                frameStablePlaybackResetDelegate.RunTask();

            // Read current FrameStablePlayback state via reflection
            bool wasFrameStable = (bool)frameStablePlaybackProperty.GetValue(this)!;

            // Disable frame-stable playback
            frameStablePlaybackProperty.SetValue(this, false);

            // Perform the seek
            GameplayClockContainer.Seek(time);

            // Schedule restore of frame-stable playback after children process
            frameStablePlaybackResetDelegate = ScheduleAfterChildren(() =>
                                                                     frameStablePlaybackProperty.SetValue(this, wasFrameStable));
        }


                private ScoreManager scoreManager = null!;
                private LocalUserManager? localUserManager;
                private IDisposable? replayFileSubscription;

        [BackgroundDependencyLoader]
        private void load(ReplayPlayer? replayPlayer, Player? player, RealmAccess realm, LocalUserManager? localUserManager, ScoreManager? scoreManager)
        {
            this.scoreManager = scoreManager!;
            this.localUserManager = localUserManager;

            // Section gimmick overlays (gimmicks are parsed/applied before drawables are created).
            if (!Mods.Any(m => m is ModFlashlight) && SectionGimmickFlashlightOverlay.HasAnyForcedFlashlightSection(Beatmap))
            {
                Overlays.Add(new SectionGimmickFlashlightOverlay(Beatmap, this)
                {
                    Depth = float.MinValue,
                });
            }

            if (!Mods.Any(m => m is InputBlockingMod) && SectionGimmickInputBlockingOverlay.HasAnyForcedInputBlockingSection(Beatmap))
            {
                Overlays.Add(new SectionGimmickInputBlockingOverlay(Beatmap, this)
                {
                    Depth = float.MinValue,
                });
            }

            if (SectionModApplicator.HasAnyForcedFunMods(Beatmap))
            {
                Overlays.Add(new SectionGimmickFunModsOverlay(Beatmap, this, Mods)
                {
                    Depth = float.MinValue,
                });
            }

            // Attach the recorded replay file to every mosu score that gets saved — pass, the save-on-fail
            // button, or any other import path. Custom rulesets never get replay files from Player.ImportScore
            // (legacy-only), so watching or exporting a replay later would otherwise be impossible.
            if (scoreManager != null && player != null)
            {
                // Count the play exactly like the main game counts one (pass / fail / quit, gated on hits).
                // A play is only counted once per session, and only if the profile's play count should
                // increase (at least one successful hit and a non-zero score).
                bool playCounted = false;

                void countPlay()
                {
                    var scoreInfo = player.Score.ScoreInfo;
                    // Count against the active profile, not the score's RealmUser: osu core attributes scores to the
                    // logged-in API user (p720 here), but MOsu profiles are the local abstraction (Guest). The score's
                    // RealmUser is rewritten to the active profile on results anyway.
                    string profileName = localUserManager?.ActiveProfile.Value ?? scoreInfo.RealmUser.Username;
                    if (playCounted || !LocalUserManager.ShouldCountPlay(scoreInfo))
                        return;

                    playCounted = true;
                    localUserManager?.IncrementPlayCount(profileName);
                }

                void attachReplayFile(Guid scoreId)
                {
                    if (player.Score.Replay.Frames.Count == 0)
                        return;

                    using var stream = new MemoryStream();

                    // LegacyScoreEncoder refuses non-legacy rulesets. The ruleset byte it writes is only used
                    // when the replay is parsed back (databased scores use the stored ScoreInfo, not the file's),
                    // so encode under the osu! ruleset identity to get standard OsuReplayFrame playback.
                    var encodeScore = player.Score.DeepClone();
                    encodeScore.ScoreInfo.Ruleset = new osu.Game.Rulesets.Osu.OsuRuleset().RulesetInfo;
                    new LegacyScoreEncoder(encodeScore, playableBeatmap).Encode(stream, leaveOpen: true);

                    stream.Position = 0;
                    realm.Write(r =>
                    {
                        var managed = r.Find<ScoreInfo>(scoreId);
                        if (managed != null && managed.Files.Count == 0)
                            scoreManager.AddFile(managed, stream, "replay.osr", r);
                    });
                }

                // Fire whenever the score row lands in the database — pass (auto-import before results) and
                // the save-on-fail button (forced import) both pass through here; Sticks uses the same trigger.
                //
                // Also mirror SticksReplayStore.EnsureLocalIdentity: imports of scores with no files keep the
                // pre-set hash, and ScoreDownloadTracker (drives the results screen replay button) only matches
                // rows with a non-empty ScoreInfo.Hash, keyed on the score's stable ID here.
                if (string.IsNullOrEmpty(player.Score.ScoreInfo.Hash))
                    player.Score.ScoreInfo.Hash = $"mosu-replay-{player.Score.ScoreInfo.ID:N}";

                replayFileSubscription = realm.RegisterForNotifications(
                    r => r.All<ScoreInfo>().Where(s => s.ID == player.Score.ScoreInfo.ID && !s.DeletePending),
                    (scores, _) =>
                    {
                        if (!scores.Any() || IsDisposed)
                            return;

                        Schedule(() => attachReplayFile(player.Score.ScoreInfo.ID));
                    });

                // Pass: the score has been recorded and results are being shown.
                player.OnShowingResults += () =>
                {
                    countPlay();
                };

                // Fail: mirrors upstream's submitFromFailOrQuit on fail.
                player.GameplayState.HealthProcessor.Failed += () =>
                {
                    countPlay();
                    return true;
                };

                // Quit mid-play: the player exits without showing results.
                // The player is only parented to the ScreenStack after its load completes (push loads it
                // under the source screen first), so defer the hookup until the stack is actually the parent.
                void hookQuitHandler()
                {
                    if (player.Parent is ScreenStack screenStack)
                    {
                        screenStack.ScreenExited += (exited, _) =>
                        {
                            if (exited == player)
                                countPlay();
                        };
                    }
                    else
                        Schedule(hookQuitHandler);
                }

                Schedule(hookQuitHandler);
            }
            if (replayPlayer != null)
            {
                replayPlayer.AddSettings(new SongProgressSettings());
                // Add RandomV2 settings panel when the mod is active
                if (Mods.OfType<OsuModRandomV2>().FirstOrDefault() is OsuModRandomV2 randomV2){
                    replayPlayer.AddSettings(new RandomV2Settings(randomV2, Beatmap, Mods, () => ReplayScore?.Replay, replayPlayer.Mods));
                }

                // Add SpacingAdjust settings panel when the mod is active
                if (Mods.OfType<OsuModSpacingAdjust>().FirstOrDefault() is OsuModSpacingAdjust spacingAdjust){
                    replayPlayer.AddSettings(new SpacingAdjustSettings(spacingAdjust, Beatmap, Mods, () => ReplayScore?.Replay, replayPlayer.Mods));
                }

                


                ReplayAnalysisOverlay analysisOverlay;
                PlayfieldAdjustmentContainer.Add(analysisOverlay = new ReplayAnalysisOverlay(replayPlayer.Score.Replay));
                Overlays.Add(analysisOverlay.CreateProxy().With(p => p.Depth = float.NegativeInfinity));
                var osuConfig = (OsuRulesetConfigManager?)Dependencies.Get<IRulesetConfigCache>().GetConfigFor(new osu.Game.Rulesets.Osu.OsuRuleset());
                replayPlayer.AddSettings(new ReplayAnalysisSettings(osuConfig!));

                cursorHideEnabled = osuConfig!.GetBindable<bool>(OsuRulesetSetting.ReplayCursorHideEnabled);

                // I have little faith in this working (other things touch cursor visibility) but haven't broken it yet.
                // Let's wait for someone to report an issue before spending too much time on it.
                cursorHideEnabled.BindValueChanged(enabled => Playfield.Cursor.FadeTo(enabled.NewValue ? 0 : 1), true);
            }

            // beatmap.Value.Beatmap.Br
            if (GameplayClockContainer != null && frameStablePlaybackProperty != null && player != null)
            {
                // Added to the player's BreakOverlay (outside the ruleset input manager), like the intro skip
                // overlay — touch-sourced mouse clicks are suppressed inside gameplay, so an overlay under
                // DrawableRuleset would never receive taps. BreakOverlay is only present during breaks, so this
                // also removes the need for the overlay's own presence logic.
                //
                // BreakOverlay is assigned near the end of Player.load, which may run after this ruleset load —
                // poll until it exists.
                Schedule(() => {
                BreakTracker breakTracker = (BreakTracker)FrameStableComponents.First(p => p is BreakTracker);
                    var skipOverlay = new SkipOverlay
                    {
                        Clock = FrameStableClock,
                        ProcessCustomClock = false,
                        BreakTracker = breakTracker,
                    };
                    skipOverlay.RequestSkip = () =>
                    {
                    if(breakTracker.CurrentPeriod.Value.HasValue)
                        SafeSeek(breakTracker.CurrentPeriod.Value.Value.End);
                };

                    player.BreakOverlay.Add(skipOverlay);
                });
            }



            if (player != null && localUserManager != null){
                player.OnShowingResults += async () => {
                    var scoreInfo = player.Score.ScoreInfo;
                    var pp = await calculatePP(scoreInfo).ConfigureAwait(false);
                    realm.Write(r => {
                        var score = r.Find<ScoreInfo>(scoreInfo.ID);
                        if (score != null)
                        {
                            score.PP = pp;
                            // Set username to active profile for profile-based score tracking.
                            string activeProfile = localUserManager.ActiveProfile.Value;
                            if (!string.IsNullOrEmpty(activeProfile))
                                score.RealmUser.Username = activeProfile;
                        }
                    });

                    await localUserManager.UpdateUserStatisticsAsync(Ruleset.RulesetInfo).ConfigureAwait(false);
                };
                BreakTracker breakTracker = (BreakTracker)FrameStableComponents.First(p => p is BreakTracker);
                breakTracker.Breaks = playableBeatmap.Breaks;
                // player.OnLoadComplete += (player) => {
                //     breakTracker.Breaks = beatmap.Value.Beatmap.Breaks;
                // };
            }

        }

        private async Task<double?> calculatePP(ScoreInfo scoreInfo){
            var attributes = await difficultyCache.GetDifficultyAsync(scoreInfo.BeatmapInfo!, scoreInfo.Ruleset, scoreInfo.Mods).ConfigureAwait(false);
            var performanceCalculator = scoreInfo.Ruleset.CreateInstance().CreatePerformanceCalculator();

            // Performance calculation requires the beatmap and ruleset to be locally available. If not, return a default value.
            if (attributes?.DifficultyAttributes == null || performanceCalculator == null)
                return null;

            var result = await performanceCalculator.CalculateAsync(scoreInfo, attributes.Value.DifficultyAttributes, default).ConfigureAwait(false);

            // scoreInfo.PP = result.Total;

            return result.Total;
        }

        public override DrawableHitObject<OsuHitObject>? CreateDrawableRepresentation(OsuHitObject h)
        {
            ensureGimmicksApplied();

            return DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(playableBeatmap!, h, Mods);
        }

        private bool gimmicksApplied;

        private void ensureGimmicksApplied()
        {
            if (gimmicksApplied)
                return;

            gimmicksApplied = true;
            DeltaGimmickRuntime.TryEnsureApplied(playableBeatmap!, parentDependencies, mods: Mods);
            notifyScoreProcessorGimmicksApplied();
        }

        /// <summary>
        /// The score processor may have simulated max stats before the gimmicks were parsed
        /// (fake sources still counted as normal objects); give it the chance to re-simulate.
        /// </summary>
        private void notifyScoreProcessorGimmicksApplied()
        {
            try
            {
                if (parentDependencies.Get<ScoreProcessor>() is DeltaScoreProcessor processor)
                    processor.OnGimmicksApplied();
            }
            catch
            {
            }
        }

        private IReadOnlyDependencyContainer parentDependencies = null!;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true; // always show the gameplay cursor

        protected override Playfield CreatePlayfield() => new MOsuPlayfield();

        protected override PassThroughInputManager CreateInputManager() => new MosuInputManager(Ruleset.RulesetInfo);

        public override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer() => new OsuPlayfieldAdjustmentContainer { AlignWithStoryboard = true };
        protected override ResumeOverlay CreateResumeOverlay()
        {
            if (Mods.Any(m => m is OsuModAutopilot or OsuModTouchDevice))
                return new DelayedResumeOverlay { Scale = new Vector2(0.65f) };

            return new MosuResumeOverlay();
        }

        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new OsuFramedReplayInputHandler(replay);

        protected override ReplayRecorder CreateReplayRecorder(Score score) => new OsuReplayRecorder(score);

        protected override void Dispose(bool isDisposing)
        {
            replayFileSubscription?.Dispose();
            base.Dispose(isDisposing);
        }

        public override double GameplayStartTime
        {
            get
            {
                if (Objects.FirstOrDefault() is OsuHitObject first)
                    return first.StartTime - Math.Max(2000, first.TimePreempt);

                return 0;
            }
        }
    }
}
