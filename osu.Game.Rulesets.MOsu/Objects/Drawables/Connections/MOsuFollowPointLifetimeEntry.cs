// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Performance;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Objects.Drawables.Connections
{
    public class MOsuFollowPointLifetimeEntry : LifetimeEntry
    {
        public event Action? Invalidated;
        public readonly OsuHitObject Start;

        public MOsuFollowPointLifetimeEntry(OsuHitObject start)
        {
            Start = start;
            LifetimeStart = Start.StartTime;
        }

        private OsuHitObject? end;

        public OsuHitObject? End
        {
            get => end;
            set
            {
                UnbindEvents();

                end = value;

                bindEvents();

                refreshLifetimes();
            }
        }

        private bool wasBound;

        private void bindEvents()
        {
            UnbindEvents();

            if (End == null)
                return;

            Start.DefaultsApplied += onDefaultsApplied;
            Start.PositionBindable.ValueChanged += onPositionChanged;

            End.DefaultsApplied += onDefaultsApplied;
            End.PositionBindable.ValueChanged += onPositionChanged;

            wasBound = true;
        }

        public void UnbindEvents()
        {
            if (!wasBound)
                return;

            Debug.Assert(End != null);

            Start.DefaultsApplied -= onDefaultsApplied;
            Start.PositionBindable.ValueChanged -= onPositionChanged;

            End.DefaultsApplied -= onDefaultsApplied;
            End.PositionBindable.ValueChanged -= onPositionChanged;

            wasBound = false;
        }

        private void onDefaultsApplied(HitObject obj) => refreshLifetimes();

        private void onPositionChanged(ValueChangedEvent<Vector2> obj) => refreshLifetimes();

        private void refreshLifetimes()
        {
            if (End == null || End.NewCombo || Start is Spinner || End is Spinner)
            {
                LifetimeEnd = LifetimeStart;
                return;
            }

            Vector2 startPosition = Start.StackedEndPosition;
            Vector2 endPosition = End.StackedPosition;
            Vector2 distanceVector = endPosition - startPosition;

            float fraction = (int)(MOsuFollowPointConnection.SPACING * 1.5f) / distanceVector.Length;
            MOsuFollowPointConnection.GetFadeTimes(Start, End, fraction, out double fadeInTime, out _);

            LifetimeStart = fadeInTime;

            // Don't reset LifetimeEnd to double.MaxValue if it's already finite.
            // The connection sets a proper LifetimeEnd once follow points are spawned.
            // Resetting it on every position change (e.g. RandomV2 reprocess) resurrects
            // past entries in the LifetimeEntryManager, causing overspawning.
            if (LifetimeEnd == double.MaxValue)
                LifetimeEnd = End.StartTime + End.TimeFadeIn;

            Invalidated?.Invoke();
        }
    }
}
