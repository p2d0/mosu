// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Pooling;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Objects.Drawables.Connections
{
    public partial class MOsuFollowPointConnection : PoolableDrawableWithLifetime<MOsuFollowPointLifetimeEntry>
    {
        public const int SPACING = 32;
        public const double PREEMPT = 800;

        public DrawablePool<FollowPoint>? Pool { private get; set; }

        // Incremented on apply/free to cancel stale scheduled refreshes.
        private int refreshVersion;

        protected override void OnApply(MOsuFollowPointLifetimeEntry entry)
        {
            base.OnApply(entry);
            refreshVersion++;
            entry.Invalidated += scheduleRefresh;
            scheduleRefresh();
        }

        protected override void OnFree(MOsuFollowPointLifetimeEntry entry)
        {
            refreshVersion++;
            base.OnFree(entry);
            entry.Invalidated -= scheduleRefresh;
            returnFollowPoints();
        }

        private void scheduleRefresh()
        {
            int version = refreshVersion;

            Scheduler.AddOnce(() =>
            {
                if (version != refreshVersion || !IsInUse)
                    return;

            Debug.Assert(Pool != null);
            returnFollowPoints();

            var entry = Entry;
            if (entry == null) return;

            if (entry.End == null)
            {
                entry.LifetimeEnd = double.MinValue;
                return;
            }

            OsuHitObject start = entry.Start;
            OsuHitObject end = entry.End;
            double startTime = start.GetEndTime();

            Vector2 startPosition = start.StackedEndPosition;
            Vector2 endPosition = end.StackedPosition;
            Vector2 distanceVector = endPosition - startPosition;
            int distance = (int)distanceVector.Length;
            float rotation = (float)(Math.Atan2(distanceVector.Y, distanceVector.X) * (180 / Math.PI));

            double finalTransformEndTime = startTime;

            for (int d = (int)(SPACING * 1.5); d < distance - SPACING; d += SPACING)
            {
                float fraction = (float)d / distance;
                Vector2 pointStartPosition = startPosition + (fraction - 0.1f) * distanceVector;
                Vector2 pointEndPosition = startPosition + fraction * distanceVector;

                GetFadeTimes(start, end, (float)d / distance, out double fadeInTime, out double fadeOutTime);

                FollowPoint fp;
                AddInternal(fp = Pool.Get());

                fp.ClearTransforms();
                fp.Position = pointStartPosition;
                fp.Rotation = rotation;
                fp.Alpha = 0;
                fp.Scale = new Vector2(1.5f * end.Scale);
                fp.AnimationStartTime.Value = fadeInTime;

                using (fp.BeginAbsoluteSequence(fadeInTime))
                {
                    fp.FadeIn(end.TimeFadeIn);
                    fp.ScaleTo(end.Scale, end.TimeFadeIn, Easing.Out);
                    fp.MoveTo(pointEndPosition, end.TimeFadeIn, Easing.Out);
                    fp.Delay(fadeOutTime - fadeInTime).FadeOut(end.TimeFadeIn).Expire();
                    finalTransformEndTime = fp.LifetimeEnd;
                }
            }

            entry.LifetimeEnd = finalTransformEndTime;
            });
        }

        /// <summary>
        /// Detach follow points, disposing any excess (poolless) ones to prevent
        /// SkinnableDrawable SourceChanged event leaks.
        /// </summary>
        private void returnFollowPoints()
        {
            foreach (FollowPoint fp in InternalChildren.OfType<FollowPoint>().ToArray())
            {
                bool hadPool = fp.IsInPool;
                // Detach first — Return() expects Parent == null
                RemoveInternal(fp, false);
                // Excess points have no pool, so they'd be orphaned and leak via
                // SkinnableDrawable.CurrentSkin.SourceChanged subscription.
                if (!hadPool)
                    fp.Dispose();
            }
        }

        public static void GetFadeTimes(OsuHitObject start, OsuHitObject end, float fraction, out double fadeInTime, out double fadeOutTime)
        {
            double startTime = start.GetEndTime();
            double duration = end.StartTime - startTime;
            double preempt = PREEMPT * Math.Min(1, start.TimePreempt / OsuHitObject.PREEMPT_MIN);
            fadeOutTime = startTime + fraction * duration;
            fadeInTime = fadeOutTime - preempt;
        }
    }
}
