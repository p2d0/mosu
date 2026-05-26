// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Break;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class SkipOverlay : BeatSyncedContainer, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>
        /// The duration of the break overlay fading.
        /// </summary>
        public const double BREAK_FADE_DURATION = BreakPeriod.MIN_BREAK_DURATION / 2;

        public required BreakTracker BreakTracker { get; init; }

        public Action RequestSkip;

        private readonly SkipButton skipButton;
        private readonly IBindable<Period?> currentPeriod = new Bindable<Period?>();

        public SkipOverlay()
        {
            RelativeSizeAxes = Axes.Both;

            Child = skipButton = new SkipButton
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                Y = 150, // Adjusted based on original vertical_margin * 10
                Size = new Vector2(1, 100),
                Action = () => RequestSkip?.Invoke(),
                Alpha = 0 // Hidden by default
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentPeriod.BindTo(BreakTracker.CurrentPeriod);
            currentPeriod.BindValueChanged(updateDisplay, true);
        }

        private void updateDisplay(ValueChangedEvent<Period?> period)
        {
            Scheduler.CancelDelayedTasks();

            if (period.NewValue == null)
            {
                skipButton.Hide();
                return;
            }

            var b = period.NewValue.Value;

            using (BeginAbsoluteSequence(b.Start))
            {
                skipButton.Show();

                using (BeginDelayedSequence(b.Duration))
                {
                    skipButton.Hide();
                }
            }
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat || currentPeriod.Value == null)
                return false;

            switch (e.Action)
            {
                case GlobalAction.SkipCutscene:
                    skipButton.TriggerClick();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }
    }
}
