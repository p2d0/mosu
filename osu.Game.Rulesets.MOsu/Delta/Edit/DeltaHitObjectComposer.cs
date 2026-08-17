// MOsu's editor composer: uses the gimmick-aware editor ruleset for the compose
// playfield and adds the section/hitobject gimmick toolbox groups.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Utils;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Game.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.IO.Serialization;using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.OSD;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.MOsu.Delta.Edit.Timeline;
using osu.Game.Rulesets.MOsu.Extensions;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Extensions;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit;using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose;
using osu.Game.Screens.Edit.Compose.Components;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osu.Framework.Screens;

namespace osu.Game.Rulesets.MOsu.Delta.Edit
{
    public partial class DeltaHitObjectComposer : OsuHitObjectComposer, IKeyBindingHandler<PlatformAction>, IKeyBindingHandler<GlobalAction>
    {
        private IReadOnlyDependencyContainer parentDependencies = null!;
        private DependencyContainer dependencies = null!;

        private RealmAccess realm = null!;
        private Storage storage = null!;

        private DeltaEditorDrawableRuleset editorDrawableRuleset = null!;
        private DeltaEditorChangeHandler? editorChangeHandler;

        private DeltaSectionGimmickEditorModel sectionModel = null!;
        private HitObjectGimmickEditorModel hitObjectModel = null!;
        private BeatmapHitObjectGimmicks hitObjectGimmicks = null!;

        [Resolved]
        private EditorScreenWithTimeline? screenWithTimeline { get; set; }

        private bool timelineGimmickDisplaysInjected;
        private osu.Framework.Graphics.Containers.Container<Drawable>? timelineContentContainer;

        private bool sliderVelocityControlUncapped;
        private readonly HashSet<Drawable> uncappedSliderVelocityControls = new HashSet<Drawable>();
        private osu.Framework.Graphics.Drawable? sliderVelocityScanRoot;
        private int sliderVelocityScanFrame;

        /// <summary>
        /// The stock slider velocity control (right-toolbox <c>SliderVelocityControl</c> and the
        /// timeline difficulty-point popover's <c>SliderVelocityAdjustmentControl</c>) caps its
        /// bindable at 10x. Delta removes the cap in core; widen every instance in place so SV > 10
        /// can be authored. The popover instance is created on demand and renders at the game
        /// root, so scan periodically from the top ancestor.
        /// </summary>
        private void uncapSliderVelocityControl()
        {
            sliderVelocityScanRoot ??= findRoot(this);

            // Scan at ~1Hz; toolbox/popover instances appear after load / on demand.
            if (++sliderVelocityScanFrame % 60 != 0)
                return;

            foreach (var control in sliderVelocityScanRoot.FindDescendants<Drawable>())
            {
                string name = control.GetType().Name;

                if (name != "SliderVelocityControl" && name != "SliderVelocityAdjustmentControl")
                    continue;

                if (!uncappedSliderVelocityControls.Add(control))
                    continue;

                if (control.GetType().GetProperty("Current")?.GetValue(control) is BindableNumber<double> bindable)
                {
                    bindable.MinValue = 0;
                    bindable.MaxValue = 1000;
                }

                sliderVelocityControlUncapped = true;
            }
        }

        /// <summary>
        /// Whether any slider velocity control has been uncapped (used by the tests).
        /// </summary>
        internal bool SliderVelocityControlUncapped => sliderVelocityControlUncapped;

        private static osu.Framework.Graphics.Drawable? findRoot(osu.Framework.Graphics.Drawable drawable)
        {
            while (drawable.Parent != null)
                drawable = drawable.Parent;

            return drawable;
        }

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private IWorkingBeatmapCache workingBeatmapCache { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private OnScreenDisplay onScreenDisplay { get; set; }

        [Resolved]
        private EditorClipboard editorClipboard { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnapProvider { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        private string? savedStateHash;

        [Cached]
        protected readonly SectionGimmickToolboxGroup SectionGimmickToolboxGroup = new SectionGimmickToolboxGroup();

        [Cached]
        protected readonly HitObjectGimmickToolboxGroup HitObjectGimmickToolboxGroup = new HitObjectGimmickToolboxGroup();

        public DeltaHitObjectComposer(Ruleset ruleset)
            : base(ruleset)
        {
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // [Resolved] fields come back null in the editor context, so capture the parent chain
            // and resolve dependencies manually (same pattern the drawable ruleset uses).
            parentDependencies = parent;
            return dependencies = (DependencyContainer)base.CreateChildDependencies(parent);
        }

        [BackgroundDependencyLoader]
        private void load(EditorBeatmap editorBeatmap)
        {
            // Ensure gimmicks are parsed + applied before the toolbox models snapshot them.
            WorkingBeatmap? working = null;

            try
            {
                working = parentDependencies.Get<IBindable<WorkingBeatmap>>()?.Value;
            }
            catch
            {
            }

            DeltaGimmickRuntime.EnsureApplied(editorBeatmap.PlayableBeatmap, working, mutateList: true);

            // The list now holds fake clones in place of their sources; re-key the editor's
            // startTimeBindables so deleting a fake finds its bindable (RemoveAt keys by instance).
            rekeyStartTimeBindables(editorBeatmap);

            // The core only builds a change handler for savable rulesets; provide our own so
            // undo/redo works (states include the gimmick sections).
            editorChangeHandler = new DeltaEditorChangeHandler(editorBeatmap);
            dependencies.CacheAs<IEditorChangeHandler>(editorChangeHandler);
            AddInternal(editorChangeHandler);

            var sectionModel = new DeltaSectionGimmickEditorModel(editorBeatmap);
            var hitObjectModel = new HitObjectGimmickEditorModel(editorBeatmap);

            this.sectionModel = sectionModel;
            this.hitObjectModel = hitObjectModel;
            this.hitObjectGimmicks = (editorBeatmap.PlayableBeatmap as DeltaBeatmap)?.Gimmicks?.HitObjectGimmicks ?? new BeatmapHitObjectGimmicks();

            dependencies.CacheAs(sectionModel);
            dependencies.CacheAs(hitObjectModel);

            // delta-style placement: commit a plain add so objects can be placed on top of each
            // other (the stock CommitPlacement removes same-time objects first).
            dependencies.CacheAs<IPlacementHandler>(new MosuPlacementHandler(this));

            // DI in the editor context is unreliable for the drawable ruleset, so wire the
            // drawable refresh directly through the composer.
            if (editorDrawableRuleset != null)
            {
                sectionModel.Changed += editorDrawableRuleset.RefreshDrawables;
                hitObjectModel.Changed += editorDrawableRuleset.RefreshDrawables;
            }
            else
                osu.Framework.Logging.Logger.Log("[MOsu-Composer] editorDrawableRuleset null at wiring time");

            try
            {
                realm = parentDependencies.Get<RealmAccess>();
                storage = parentDependencies.Get<Storage>();
            }
            catch
            {
            }

            RightToolbox.AddRange(new Drawable[]
            {
                HitObjectGimmickToolboxGroup,
                SectionGimmickToolboxGroup,
            });

            // baseline: the state as loaded (after the initial gimmick application), so only
            // real user edits count as unsaved changes.
            savedStateHash = computeStateHash();
        }

        protected override ComposeBlueprintContainer CreateBlueprintContainer()
            => new DeltaBlueprintContainer(this);

        protected override DrawableRuleset<OsuHitObject> CreateDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => editorDrawableRuleset = new DeltaEditorDrawableRuleset(ruleset, beatmap, mods);

        protected override void Update()
        {
            base.Update();
            injectTimelineGimmickDisplays();
            uncapSliderVelocityControl();
        }

        /// <summary>
        /// The stock Slider Velocity Adjustment toolbox control caps its bindable at 10x (core
        /// SliderVelocityAdjustmentControl). Widen it in place so SV > 10 can be authored, matching
        /// <summary>
        /// Adds the delta-style section/hitobject-gimmick displays to the compose timeline.
        /// The timeline is built asynchronously by the core after the composer loads, so poll
        /// until it exists (same pattern mania uses to read timeline zoom).
        /// Visible copies are inserted below the blueprint container (proxy + Depth), with
        /// non-rendering originals on top for input, mirroring delta's ComposeScreen layering.
        /// </summary>
        private void injectTimelineGimmickDisplays()
        {
            if (timelineGimmickDisplaysInjected)
                return;

            var timeline = screenWithTimeline?.TimelineArea.Timeline;

            if (timeline == null)
                return;

            if (timelineContentContainer == null)
            {
                // TimelineBlueprintContainer is internal in the packaged game; anchor on the
                // public break display, which sits in the same content container as the blueprints.
                var breakDisplay = timeline.FindDescendant<TimelineBreakDisplay>();

                if (breakDisplay?.Parent is Container<Drawable> container)
                    timelineContentContainer = container;
                else
                    return;
            }

            var sectionDisplay = new TimelineSectionGimmickDisplay(sectionModel)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Height = 0.45f,
            };

            var boundaryDisplay = new TimelineSectionGimmickBoundaryLineDisplay(sectionModel)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            var gimmickDisplay = new TimelineHitObjectGimmickLineDisplay(hitObjectGimmicks)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            // Visible proxies render below the blueprints; the originals stop rendering once
            // proxied, so they serve purely as the input layer on top.
            var sectionProxy = sectionDisplay.CreateProxy();
            var boundaryProxy = boundaryDisplay.CreateProxy();
            var gimmickProxy = gimmickDisplay.CreateProxy();

            sectionProxy.Depth = -1;
            boundaryProxy.Depth = -1;
            gimmickProxy.Depth = -1;

            timelineContentContainer.AddRange(new Drawable[]
            {
                sectionProxy,
                boundaryProxy,
                gimmickProxy,
                sectionDisplay,
                boundaryDisplay,
                gimmickDisplay,
            });

            timelineGimmickDisplaysInjected = true;
        }

        internal void save()
        {
            if (realm == null || storage == null)
                return;            if (!DeltaEditorSaver.Save(EditorBeatmap, realm, storage))
                return;

            savedStateHash = computeStateHash();

            onScreenDisplay?.Display(new DeltaBeatmapEditorToast(ToastStrings.BeatmapSaved, EditorBeatmap.BeatmapInfo.GetDisplayTitle()));

            // Defer the recalc a frame so the freshly-written file is fully flushed to storage
            // before a fresh working beatmap reads it (first save otherwise reads stale data).
            Scheduler.AddOnce(recalculateDifficulty);
        }

        private void recalculateDifficulty()
        {
            // The editor's BeatmapInfo is a copy and lags the realm after save; re-fetch the
            // live model so the caches invalidate against the file just written.
            var info = realm.Run(r => r.Find<BeatmapInfo>(EditorBeatmap.BeatmapInfo.ID));

            if (info == null)
                return;

            workingBeatmapCache.Invalidate(info);
            var working = workingBeatmapCache.GetWorkingBeatmap(info);
            difficultyCache.Invalidate(info, working.BeatmapInfo);

            workingBeatmapCache.Invalidate(info);
        }

        private partial class DeltaBeatmapEditorToast : Toast
        {
            public DeltaBeatmapEditorToast(LocalisableString value, string beatmapDisplayName)
                : base(InputSettingsStrings.EditorSection, value)
            {
                ExtraText = beatmapDisplayName;
            }
        }

        private static readonly System.Reflection.FieldInfo? start_time_bindables_field =
            typeof(EditorBeatmap).GetField("startTimeBindables", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static void rekeyStartTimeBindables(EditorBeatmap editorBeatmap)
        {
            if (start_time_bindables_field?.GetValue(editorBeatmap) is not System.Collections.Generic.Dictionary<HitObject, Bindable<double>> dict)
                return;

            var fakes = new System.Collections.Generic.Dictionary<long, OsuHitObject>();

            foreach (var h in editorBeatmap.PlayableBeatmap.HitObjects.OfType<OsuHitObject>())
            {
                if (h is FakeHitCircle or FakeSlider)
                    fakes[DeltaGimmickApplier.GetObjectId(h)] = h;
            }

            foreach (var (source, bindable) in dict.ToList())
            {
                if (source is OsuHitObject osuSource
                    && fakes.TryGetValue(DeltaGimmickApplier.GetObjectId(osuSource), out var fake)
                    && !ReferenceEquals(source, fake))
                {
                    dict.Remove(source);
                    dict[fake] = bindable;
                }
            }
        }

        private partial class MosuPlacementHandler : IPlacementHandler
        {
            private readonly DeltaHitObjectComposer composer;

            public MosuPlacementHandler(DeltaHitObjectComposer composer)
            {
                this.composer = composer;
            }

            public void ShowPlacement(HitObject hitObject) => composer.EditorBeatmap.PlacementObject.Value = hitObject;

            public void HidePlacement() => composer.EditorBeatmap.PlacementObject.Value = null;

            public void CommitPlacement(HitObject hitObject)
            {
                // delta-style: allow stacking — just add, don't replace same-time objects.
                composer.EditorBeatmap.PlacementObject.Value = null;

                // Core placement tools create stock Sliders with the 10x SV cap; widen the
                // bindable so SV > 10 can be authored and round-trips through save/reload.
                if (hitObject is Slider slider)
                    DeltaSlider.UncapVelocityBindable(slider);

                composer.EditorBeatmap.Add(hitObject);
            }

            public void Delete(HitObject hitObject) => composer.EditorBeatmap.Remove(hitObject);
        }

        /// <summary>
        /// The editor session's section model (used by the timeline displays and the tests).
        /// </summary>
        internal DeltaSectionGimmickEditorModel SectionGimmickModel => sectionModel;

        private bool hasUnsavedChanges => savedStateHash != null && computeStateHash() != savedStateHash;

        /// <summary>
        /// Whether the beatmap differs from its last-saved state (used by the tests).
        /// </summary>
        internal bool HasUnsavedChanges => hasUnsavedChanges;

        private string computeStateHash()
        {
            var sb = new StringBuilder();

            foreach (var o in EditorBeatmap.HitObjects)
            {
                sb.Append(o.GetType().Name).Append('@').Append(o.StartTime).Append(';');

                if (o is IHasPosition p)
                    sb.Append(p.Position.X).Append(',').Append(p.Position.Y).Append(';');
            }

            if (EditorBeatmap.PlayableBeatmap is DeltaBeatmap mosu && mosu.Gimmicks != null)
                sb.Append(DeltaGimmickSerializer.Serialize(mosu.Gimmicks.Sections, mosu.Gimmicks.HitObjectGimmicks));

            return sb.ToString();
        }

        private void exitEditor()
        {
            for (Drawable? d = this; d != null; d = d.Parent)
            {
                if (d is Editor editor)
                {
                    editor.Exit();
                    return;
                }
            }
        }

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            switch (e.Action)
            {
                case PlatformAction.Save:
                    save();
                    return true;

                case PlatformAction.Copy:
                    copyWithGimmicks();
                    return true;

                case PlatformAction.Cut:
                    copyWithGimmicks();
                    EditorBeatmap.RemoveRange(EditorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().Select(i => DeltaSelectionHandler.ResolveToSource(i, EditorBeatmap)).ToArray());
                    return true;

                case PlatformAction.Paste:
                    pasteWithGimmicks();
                    return true;

                case PlatformAction.Undo:
                    editorChangeHandler?.RestoreState(-1);
                    return true;

                case PlatformAction.Redo:
                    editorChangeHandler?.RestoreState(1);
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        private void copyWithGimmicks()
        {
            if (!EditorBeatmap.SelectedHitObjects.Any())
                return;

            editorClipboard.Content.Value = new DeltaClipboardContent(EditorBeatmap).Serialize();
        }

        private void pasteWithGimmicks()
        {
            var content = editorClipboard.Content.Value.Deserialize<DeltaClipboardContent>();
            if (content?.HitObjects == null || content.HitObjects.Count == 0)
                return;

            double timeOffset = beatSnapProvider.SnapTime(clock.CurrentTime) - content.HitObjects.Min(o => o.StartTime);

            foreach (var h in content.HitObjects)
                h.StartTime += timeOffset;

            EditorBeatmap.BeginChange();

            EditorBeatmap.SelectedHitObjects.Clear();

            EditorBeatmap.AddRange(content.HitObjects);
            EditorBeatmap.SelectedHitObjects.AddRange(content.HitObjects);

            rebindPastedGimmicks(content, timeOffset);

            EditorBeatmap.EndChange();
        }

        private void rebindPastedGimmicks(DeltaClipboardContent content, double timeOffset)
        {
            if (content.Gimmicks == null || content.Gimmicks.Count == 0)
                return;

            if (EditorBeatmap.PlayableBeatmap is not DeltaBeatmap mosu)
                return;

            var pasted = content.HitObjects.OfType<OsuHitObject>().ToList();
            bool any = false;

            foreach (var entry in content.Gimmicks)
            {
                var target = pasted.FirstOrDefault(o =>
                    Precision.AlmostEquals(o.StartTime, entry.StartTime + timeOffset) && o.ComboIndexWithOffsets == entry.ComboIndexWithOffsets);

                if (target == null)
                    continue;

                var newEntry = DeltaClipboardContent.CloneEntry(entry);
                newEntry.ObjectId = DeltaGimmickApplier.GetObjectId(target);
                newEntry.StartTime = target.StartTime;
                newEntry.HitObjectIndex = mosu.HitObjects.IndexOf(target);

                mosu.Gimmicks.HitObjectGimmicks.Entries.Add(newEntry);
                any = true;
            }

            if (any)
            {
                mosu.Gimmicks.Applied = false;
                DeltaGimmickApplier.Apply(mosu, mosu.Gimmicks, mutateList: false);
            }
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Action != GlobalAction.Back)
                return false;

            if (!hasUnsavedChanges)
                return false;

            // a dialog is already displayed: consume so the editor doesn't exit, but don't
            // re-push (pushing would dismiss the currently displayed dialog).
            if (dialogOverlay?.CurrentDialog != null)
                return true;

            if (dialogOverlay == null)
                return false; // no dialog overlay (e.g. visual tests): let the editor handle the exit.

            dialogOverlay.Push(new PromptForSaveDialog(exitEditor, () =>
            {
                save();
                exitEditor();
            }, () => { }));

            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }
    }
}
