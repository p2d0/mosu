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
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.MOsu.Objects.Drawables;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
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

            if (SectionGimmickFunModsOverlay.HasAnyForcedFunMods(Beatmap))
            {
                Overlays.Add(new SectionGimmickFunModsOverlay(Beatmap, this, Mods)
                {
                    Depth = float.MinValue,
                });
            }

            // Attach dummy replay file to mosu scores that have no files,
            // so the delete button appears in the leaderboard context menu.
            // Custom rulesets don't get replay files from Player.PrepareScoreForResultsAsync (legacy-only).
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

                // Pass: the score has been recorded and results are being shown.
                player.OnShowingResults += () =>
                {
                    countPlay();

                    var scoreInfo = player.Score.ScoreInfo;
                    if (scoreInfo.Ruleset.ShortName == Ruleset.RulesetInfo.ShortName && scoreInfo.Files.Count == 0)
                    {
                        var stream = new MemoryStream(Array.Empty<byte>());
                        realm.Write(r =>
                        {
                            var managed = r.Find<ScoreInfo>(scoreInfo.ID);
                            if (managed != null)
                                scoreManager.AddFile(managed, stream, $"replay-{scoreInfo.ID}.osr", r);
                        });
                    }
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
            if (GameplayClockContainer != null && frameStablePlaybackProperty != null)
            {
                SkipOverlay skipOverlay;
                BreakTracker breakTracker = (BreakTracker)FrameStableComponents.First(p => p is BreakTracker);
                Overlays.Add(skipOverlay = new SkipOverlay {
                        Clock = FrameStableClock,
                        ProcessCustomClock = false,
                        BreakTracker = breakTracker,
                        Depth = float.NegativeInfinity
                    });

                skipOverlay.RequestSkip = () => {
                    if(breakTracker.CurrentPeriod.Value.HasValue)
                        SafeSeek(breakTracker.CurrentPeriod.Value.Value.End);
                };
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

            if (playableBeatmap is not MosuBeatmap mosuBeatmap)
                return null;

            var data = mosuBeatmap.Gimmicks;

            if (data.HitObjectGimmicks.Entries.Count == 0 && data.Sections.Sections.Count == 0)
                return null;

            var objectSettings = MosuGimmickApplier.GetObjectSettings(playableBeatmap, data, h);

            if (objectSettings?.IsFakeNote == true
                && MosuGimmickApplier.CreateFakeObject(playableBeatmap, data, h) is OsuHitObject fakeObject)
            {
                fakeObject.ApplyDefaults(playableBeatmap.ControlPointInfo, MosuGimmickApplier.ResolveDifficultyForObject(playableBeatmap, data, h));
                MosuGimmickApplier.ApplyForcedModsToObject(playableBeatmap, data, fakeObject);

                return fakeObject switch
                {
                    FakeHitCircle fakeCircle => new DrawableFakeHitCircle(fakeCircle),
                    FakeSlider fakeSlider => new DrawableFakeSlider(fakeSlider),
                    _ => null
                };
            }

            var section = data.Sections.FindSectionAt(h.StartTime);
            bool hidden = section?.Settings.ForceHidden == true || objectSettings?.ForceHidden == true;
            bool noApproach = section?.Settings.ForceNoApproachCircle == true || objectSettings?.ForceNoApproachCircle == true;
            bool traceable = section?.Settings.ForceTraceable == true || objectSettings?.ForceTraceable == true;

            if (hidden || noApproach || traceable)
            {
                return h switch
                {
                    HitCircle circle => new MosuDrawableHitCircle(circle, hidden, noApproach, traceable),
                    Slider slider => new MosuDrawableSlider(slider, hidden, noApproach, traceable),
                    Spinner spinner => new MosuDrawableSpinner(spinner, hidden, noApproach, traceable),
                    _ => null
                };
            }

            return null;
        }

        private bool gimmicksApplied;

        /// <summary>
        /// Ensures the delta gimmick sections (skipped by the stock decoder) are parsed and applied
        /// to the playable beatmap before any drawables are created. The .osu file is parsed once
        /// per map (cached), not per play.
        /// </summary>
        private void ensureGimmicksApplied()
        {
            if (gimmicksApplied)
                return;

            gimmicksApplied = true;

            try
            {
                if (playableBeatmap is not MosuBeatmap mosuBeatmap)
                    return;

                var data = mosuBeatmap.Gimmicks;

                if (!data.Parsed)
                {
                    data.Parsed = true;

                    string cacheKey = $"{mosuBeatmap.BeatmapInfo.OnlineID}:{mosuBeatmap.BeatmapInfo.MD5Hash}:{mosuBeatmap.BeatmapInfo.Path}";
                    var cached = MosuGimmickCache.TryGet(cacheKey);

                    if (cached != null)
                    {
                        data = mosuBeatmap.Gimmicks = cached;
                    }
                    else
                    {
                        var workingBeatmap = beatmap?.Value;
                        var path = workingBeatmap?.BeatmapInfo.Path;

                        if (workingBeatmap == null || string.IsNullOrEmpty(path))
                            return;

                        var storagePath = workingBeatmap.BeatmapInfo.BeatmapSet?.GetPathForFile(path);
                        if (storagePath == null)
                            return;

                        using var stream = workingBeatmap.GetStream(storagePath);
                        if (stream == null)
                            return;

                        using var reader = new StreamReader(stream);
                        (data.Sections, data.HitObjectGimmicks) = MosuGimmickParser.Parse(reader);
                        MosuGimmickCache.Set(cacheKey, data);
                    }
                }

                if (data.Sections.Sections.Count == 0 && data.HitObjectGimmicks.Entries.Count == 0)
                    return;

                Logger.Log($"[MOsu] Applying {data.Sections.Sections.Count} section gimmicks and {data.HitObjectGimmicks.Entries.Count} hitobject gimmicks");
                MosuGimmickApplier.Apply(mosuBeatmap, data);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu] Failed to apply gimmicks: {e}");
            }
        }

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
