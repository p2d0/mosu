// One module owning per-object section-scoped fun-mod application for delta gimmicks.
//
// Consolidates what used to live in three places:
//  - the hidden / no-approach / traceable drawable-state re-application that every
//    MosuDrawable* in MosuGimmickDrawables reimplemented (6 near-identical copies),
//  - the per-object visibility-mod / approach / synesthesia / freeze-frame hooks that
//    SectionGimmickFunModsOverlay registered on each alive drawable,
//  - the per-object settings resolution (the overlay kept a duplicate legacy-key lookup;
//    this module uses the applier's single id-based path).
//
// All per-object hooks resolve the section for the object held by the drawable at fire time,
// so pooled drawables (reused across many objects) never leak a mod onto out-of-section
// objects.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Rulesets.Osu.Objects.Drawables;

using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Delta.UI
{
    public partial class SectionModApplicator
    {
        // ---- shared queries ----

        /// <summary>
        /// Whether any section forces the given fun mod flag.
        /// </summary>
        public static bool HasAnyForced(IBeatmap beatmap, Func<SectionGimmickSettings, bool> predicate)
            => (beatmap as DeltaBeatmap)?.Gimmicks?.Sections.Sections.Any(s => predicate(s.Settings)) == true;

        /// <summary>
        /// Whether the beatmap forces any fun mod at all (used to skip creating the overlay).
        /// </summary>
        public static bool HasAnyForcedFunMods(IBeatmap beatmap)
            => HasAnyForced(beatmap, s =>
                s.ForceTransform
                || s.ForceWiggle
                || s.ForceSpinIn
                || s.ForceGrow
                || s.ForceDeflate
                || s.ForceBarrelRoll
                || s.ForceApproachDifferent
                || s.ForceMuted
                || s.ForceNoScope
                || s.ForceTraceable
                || s.ForceMagnetised
                || s.ForceRepel
                || s.ForceFreezeFrame
                || s.ForceBubbles
                || s.ForceSynesthesia
                || s.ForceDepth
                || s.ForceBloom);

        /// <summary>
        /// Resolves the effective section settings for a hitobject: the section covering its start
        /// time, with the object's per-object gimmick flags OR'd in when it has an entry (matching
        /// the applier / drawable-creation semantics — either can force a visibility flag). The
        /// fun-mod flags (transform, wiggle, ...) are not expressible per-object, so the section's
        /// values always remain effective. Single resolution path for all drawable-level reads.
        /// </summary>
        public static SectionGimmickSettings? ResolveSettingsForHitObject(IBeatmap beatmap, HitObject hitObject)
        {
            if (hitObject is not OsuHitObject osuHitObject)
                return null;

            var data = (beatmap as DeltaBeatmap)?.Gimmicks;
            if (data == null)
                return null;

            var sectionSettings = data.Sections.FindSectionAt(osuHitObject.StartTime)?.Settings;

            var objectSettings = DeltaGimmickApplier.GetObjectSettings(beatmap, data, osuHitObject);
            if (objectSettings == null)
                return sectionSettings;

            var effective = new SectionGimmickSettings();

            if (sectionSettings != null)
            {
                // carry over every fun-mod / visibility field the overlay reads
                effective.ForceHidden = sectionSettings.ForceHidden;
                effective.ForceNoApproachCircle = sectionSettings.ForceNoApproachCircle;
                effective.ForceHardRock = sectionSettings.ForceHardRock;
                effective.ForceFlashlight = sectionSettings.ForceFlashlight;
                effective.ForceTraceable = sectionSettings.ForceTraceable;
                effective.FlashlightRadius = sectionSettings.FlashlightRadius;

                effective.ForceTransform = sectionSettings.ForceTransform;
                effective.ForceWiggle = sectionSettings.ForceWiggle;
                effective.WiggleStrength = sectionSettings.WiggleStrength;
                effective.ForceSpinIn = sectionSettings.ForceSpinIn;
                effective.ForceGrow = sectionSettings.ForceGrow;
                effective.GrowStartScale = sectionSettings.GrowStartScale;
                effective.ForceDeflate = sectionSettings.ForceDeflate;
                effective.DeflateStartScale = sectionSettings.DeflateStartScale;
                effective.ForceApproachDifferent = sectionSettings.ForceApproachDifferent;
                effective.ApproachDifferentScale = sectionSettings.ApproachDifferentScale;
                effective.ForceFreezeFrame = sectionSettings.ForceFreezeFrame;
                effective.ForceSynesthesia = sectionSettings.ForceSynesthesia;
                effective.ForceBubbles = sectionSettings.ForceBubbles;
                effective.ForceMuted = sectionSettings.ForceMuted;
                effective.MutedMuteComboCount = sectionSettings.MutedMuteComboCount;
                effective.ForceNoScope = sectionSettings.ForceNoScope;
                effective.NoScopeHiddenComboCount = sectionSettings.NoScopeHiddenComboCount;
                effective.ForceBloom = sectionSettings.ForceBloom;
                effective.BloomMaxCursorSize = sectionSettings.BloomMaxCursorSize;
                effective.BloomMaxSizeComboCount = sectionSettings.BloomMaxSizeComboCount;
                effective.ForceMagnetised = sectionSettings.ForceMagnetised;
                effective.MagnetisedAttractionStrength = sectionSettings.MagnetisedAttractionStrength;
                effective.ForceRepel = sectionSettings.ForceRepel;
                effective.RepelRepulsionStrength = sectionSettings.RepelRepulsionStrength;
                effective.ForceDepth = sectionSettings.ForceDepth;
                effective.DepthMaxDepth = sectionSettings.DepthMaxDepth;
                effective.ForceBarrelRoll = sectionSettings.ForceBarrelRoll;
                effective.BarrelRollSpinSpeed = sectionSettings.BarrelRollSpinSpeed;
            }

            effective.ForceHidden = effective.ForceHidden || objectSettings.ForceHidden;
            effective.ForceNoApproachCircle = effective.ForceNoApproachCircle || objectSettings.ForceNoApproachCircle;
            effective.ForceHardRock = effective.ForceHardRock || objectSettings.ForceHardRock;
            effective.ForceFlashlight = effective.ForceFlashlight || objectSettings.ForceFlashlight;
            effective.ForceTraceable = effective.ForceTraceable || objectSettings.ForceTraceable;

            return effective;
        }

        // ---- drawable state (collapsed from MosuGimmickDrawables) ----

        /// <summary>
        /// (Re)subscribes the hidden / no-approach-circle / traceable state application for a
        /// drawable and applies it immediately. The stock state-update flow clears and re-shows
        /// the approach circle / body transforms at start time, so a one-shot apply would be
        /// overwritten — matching delta, the effect is re-applied on every state update.
        /// </summary>
        public static void HookSectionScopedDrawableState(DrawableHitObject drawable, bool hidden, bool noApproachCircle, bool traceable)
        {
            drawable.ApplyCustomUpdateState -= applySectionScopedDrawableState;
            drawable.ApplyCustomUpdateState += applySectionScopedDrawableState;
            applySectionScopedDrawableState(drawable, drawable.State.Value);

            void applySectionScopedDrawableState(DrawableHitObject d, ArmedState state)
                => ApplySectionScopedDrawableState(d, hidden, noApproachCircle, traceable);
        }

        public static void ApplySectionScopedDrawableState(DrawableHitObject drawable, bool hidden, bool noApproachCircle, bool traceable)
        {
            if (noApproachCircle)
                SectionGimmickHiddenVisuals.ApplyHiddenState(drawable, onlyFadeApproachCircles: true);

            if (hidden)
                SectionGimmickHiddenVisuals.ApplyHiddenState(drawable);

            if (traceable)
                SectionGimmickTraceableVisuals.ApplyTraceableState(drawable);
        }

        // ---- per-object fun-mod hooks (moved from SectionGimmickFunModsOverlay) ----

        private static readonly MethodInfo apply_normal_visibility_state =
            typeof(ModWithVisibilityAdjustment).GetMethod(
                "ApplyNormalVisibilityState",
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMemberException(nameof(ModWithVisibilityAdjustment), "ApplyNormalVisibilityState");

        private readonly IBeatmap beatmap;
        private readonly IReadOnlyList<Mod> selectedMods;
        private readonly OsuColour colours;
        private readonly OsuModSynesthesia? synesthesiaMod;

        private readonly bool hasForcedTransform;
        private readonly bool hasForcedWiggle;
        private readonly bool hasForcedSpinIn;
        private readonly bool hasForcedGrow;
        private readonly bool hasForcedDeflate;
        private readonly bool hasForcedApproachDifferent;
        private readonly bool hasForcedSynesthesia;
        private readonly bool hasForcedFreezeFrame;

        public SectionModApplicator(IBeatmap beatmap, IReadOnlyList<Mod> selectedMods, OsuColour colours)
        {
            this.beatmap = beatmap;
            this.selectedMods = selectedMods;
            this.colours = colours;

            hasForcedTransform = forced(s => s.ForceTransform) && !selected<OsuModTransform>();
            hasForcedWiggle = forced(s => s.ForceWiggle) && !selected<OsuModWiggle>();
            hasForcedSpinIn = forced(s => s.ForceSpinIn) && !selected<OsuModSpinIn>();
            hasForcedGrow = forced(s => s.ForceGrow) && !selected<OsuModGrow>();
            hasForcedDeflate = forced(s => s.ForceDeflate) && !selected<OsuModDeflate>();
            hasForcedApproachDifferent = forced(s => s.ForceApproachDifferent) && !selected<OsuModApproachDifferent>();
            hasForcedSynesthesia = forced(s => s.ForceSynesthesia) && !selected<OsuModSynesthesia>();
            hasForcedFreezeFrame = forced(s => s.ForceFreezeFrame) && !selected<OsuModFreezeFrame>();

            if (forced(s => s.ForceSynesthesia) && !selected<OsuModSynesthesia>())
            {
                synesthesiaMod = new OsuModSynesthesia();
                synesthesiaMod.ApplyToBeatmap(beatmap);
            }
        }

        private bool selected<TMod>()
            where TMod : Mod
            => selectedMods.Any(m => m is TMod);

        private bool forced(Func<SectionGimmickSettings, bool> predicate) => HasAnyForced(beatmap, predicate);

        /// <summary>
        /// Registers all per-object section-scoped mod effects on a (possibly pooled) drawable.
        /// Called once per drawable instance; each hook resolves the section for the object the
        /// drawable currently holds at fire time.
        /// </summary>
        public void HookSectionScopedMods(DrawableHitObject drawable)
        {
            Logger.Log($"[MOsu-FunMods] hooking drawable {drawable.GetType().Name} (transform={hasForcedTransform} wiggle={hasForcedWiggle})");

            if (hasForcedTransform)
                applySectionScopedVisibilityMod(new OsuModTransform(), drawable, s => s.ForceTransform, null);

            if (hasForcedWiggle)
                applySectionScopedVisibilityMod(new OsuModWiggle(), drawable, s => s.ForceWiggle,
                    (m, s) => m.Strength.Value = Math.Clamp(s.WiggleStrength, 0.1f, 2f));

            if (hasForcedSpinIn)
                applySectionScopedVisibilityMod(new OsuModSpinIn(), drawable, s => s.ForceSpinIn, null);

            if (hasForcedGrow)
                applySectionScopedVisibilityMod(new OsuModGrow(), drawable, s => s.ForceGrow,
                    (m, s) => m.StartScale.Value = Math.Clamp(s.GrowStartScale, 0f, 0.99f));

            if (hasForcedDeflate)
                applySectionScopedVisibilityMod(new OsuModDeflate(), drawable, s => s.ForceDeflate,
                    (m, s) => m.StartScale.Value = Math.Clamp(s.DeflateStartScale, 1f, 25f));

            if (hasForcedApproachDifferent)
                applySectionScopedApproachDifferent(drawable);

            if (hasForcedSynesthesia)
                applySectionScopedSynesthesia(drawable);

            if (hasForcedFreezeFrame)
                applyCustomFreezeFrame(drawable);
        }

        /// <summary>
        /// Applies a <see cref="ModWithVisibilityAdjustment"/> to a drawable such that its effect
        /// is only applied to objects whose section forces the corresponding fun mod. The hook is
        /// added once per (pooled) drawable but resolves the section per object at fire time, and
        /// fired immediately for the current state — the drawable's initial Idle state application
        /// already happened before the overlay could subscribe, and circles change state again only
        /// on judgement, so a pure subscription would never fire in time.
        /// </summary>
        internal void applySectionScopedVisibilityMod<TMod>(TMod mod, DrawableHitObject drawable, Func<SectionGimmickSettings, bool> isForced, Action<TMod, SectionGimmickSettings>? configure)
            where TMod : ModWithVisibilityAdjustment
        {
            void fire(DrawableHitObject o, ArmedState state)
            {
                var settings = ResolveSettingsForHitObject(beatmap, o.HitObject);
                bool forced = settings != null && isForced(settings);

                Logger.Log($"[MOsu-FunMods] {mod.Name} fire: drawable={o.GetType().Name} start={o.HitObject.StartTime:0} state={state} settings={settings != null} forced={forced}");

                if (!forced)
                    return;

                configure?.Invoke(mod, settings!);
                apply_normal_visibility_state.Invoke(mod, new object[] { o, state });

                Logger.Log($"[MOsu-FunMods] {mod.Name} applied: start={o.HitObject.StartTime:0} posTransforms={o.Transforms.Count(t => t.TargetMember == "Position")}");
            }

            drawable.ApplyCustomUpdateState += fire;
            fire(drawable, drawable.State.Value);
        }

        private void applySectionScopedApproachDifferent(DrawableHitObject drawable)
        {
            void fire(DrawableHitObject o, ArmedState state)
            {
                if (o is not DrawableHitCircle drawableHitCircle)
                    return;

                var settings = ResolveSettingsForHitObject(beatmap, o.HitObject);
                if (settings?.ForceApproachDifferent != true)
                    return;

                var hitCircle = drawableHitCircle.HitObject;
                float scale = Math.Clamp(settings.ApproachDifferentScale, 1.5f, 10f);

                drawableHitCircle.ApproachCircle.ClearTransforms(targetMember: nameof(drawableHitCircle.ApproachCircle.Scale));

                using (drawableHitCircle.BeginAbsoluteSequence(hitCircle.StartTime - hitCircle.TimePreempt))
                    drawableHitCircle.ApproachCircle.ScaleTo(scale).ScaleTo(1f, hitCircle.TimePreempt);
            }

            drawable.ApplyCustomUpdateState += fire;
            fire(drawable, drawable.State.Value);
        }

        private void applySectionScopedSynesthesia(DrawableHitObject drawable)
        {
            if (synesthesiaMod == null)
                return;

            Color4? timingBasedColour = null;

            drawable.HitObjectApplied += _ =>
            {
                var settings = ResolveSettingsForHitObject(beatmap, drawable.HitObject);

                if (settings?.ForceSynesthesia != true)
                {
                    timingBasedColour = null;
                    return;
                }

                // Slider tails are an edge case: their start time is offset 36ms back (see LastTick),
                // so use the parenting slider's end time instead to ensure proper snap.
                double snapTime = drawable is DrawableSliderTail tail
                    ? tail.Slider.GetEndTime()
                    : drawable.HitObject.StartTime;

                timingBasedColour = BindableBeatDivisor.GetColourFor(beatmap.ControlPointInfo.GetClosestBeatDivisor(snapTime), colours);
            };

            // Set every update so it isn't overwritten by DrawableHitObject.OnApply() -> UpdateComboColour().
            drawable.OnUpdate += _ =>
            {
                if (timingBasedColour != null)
                    drawable.AccentColour.Value = timingBasedColour.Value;
            };
        }

        private void applyCustomFreezeFrame(DrawableHitObject drawable)
        {
            if (drawable is not DrawableHitCircle drawableHitCircle)
                return;

            var hitCircle = drawableHitCircle.HitObject;
            float originalPreempt = (float)(beatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault()?.TimePreempt ?? hitCircle.TimePreempt);

            void fire(DrawableHitObject o, ArmedState state)
            {
                if (o is not DrawableHitCircle circle)
                    return;

                if (ResolveSettingsForHitObject(beatmap, circle.HitObject)?.ForceFreezeFrame != true)
                    return;

                var approachCircle = circle.ApproachCircle;
                approachCircle.ClearTransforms(targetMember: nameof(approachCircle.Scale));
                approachCircle.ScaleTo(4 * (float)(circle.HitObject.TimePreempt / originalPreempt));

                using (approachCircle.BeginAbsoluteSequence(circle.HitObject.StartTime - circle.HitObject.TimePreempt))
                    approachCircle.ScaleTo(1, circle.HitObject.TimePreempt).Then().Expire();
            }

            drawable.ApplyCustomUpdateState += fire;
            fire(drawable, drawable.State.Value);
        }

    }
}
