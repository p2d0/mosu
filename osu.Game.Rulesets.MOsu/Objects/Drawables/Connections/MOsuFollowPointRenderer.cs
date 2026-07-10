// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Pooling;

namespace osu.Game.Rulesets.MOsu.Objects.Drawables.Connections
{
    public partial class MOsuFollowPointRenderer : PooledDrawableWithLifetimeContainer<MOsuFollowPointLifetimeEntry, MOsuFollowPointConnection>
    {
        public new IReadOnlyList<MOsuFollowPointLifetimeEntry> Entries => lifetimeEntries;

        private DrawablePool<MOsuFollowPointConnection> connectionPool;
        private DrawablePool<FollowPoint> pointPool;

        private readonly List<MOsuFollowPointLifetimeEntry> lifetimeEntries = new List<MOsuFollowPointLifetimeEntry>();
        private readonly Dictionary<HitObject, IBindable> startTimeMap = new Dictionary<HitObject, IBindable>();

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                connectionPool = new DrawablePool<MOsuFollowPointConnection>(10, 200),
                pointPool = new DrawablePool<FollowPoint>(50, 1000)
            };
        }

        public void AddFollowPoints(OsuHitObject hitObject)
        {
            addEntry(hitObject);
            var startTimeBindable = hitObject.StartTimeBindable.GetBoundCopy();
            startTimeBindable.ValueChanged += _ => onStartTimeChanged(hitObject);
            startTimeMap[hitObject] = startTimeBindable;
        }

        public void RemoveFollowPoints(OsuHitObject hitObject)
        {
            removeEntry(hitObject);
            startTimeMap[hitObject].UnbindAll();
            startTimeMap.Remove(hitObject);
        }

        private void addEntry(OsuHitObject hitObject)
        {
            var newEntry = new MOsuFollowPointLifetimeEntry(hitObject);

            int index = lifetimeEntries.AddInPlace(newEntry, Comparer<MOsuFollowPointLifetimeEntry>.Create((e1, e2) =>
            {
                int comp = e1.Start.StartTime.CompareTo(e2.Start.StartTime);
                if (comp != 0) return comp;
                return -1;
            }));

            if (index < lifetimeEntries.Count - 1)
                newEntry.End = lifetimeEntries[index + 1].Start;
            else
                newEntry.End = null;

            if (index > 0)
                lifetimeEntries[index - 1].End = newEntry.Start;

            Add(newEntry);
        }

        private void removeEntry(OsuHitObject hitObject)
        {
            int index = lifetimeEntries.FindIndex(e => e.Start == hitObject);
            var entry = lifetimeEntries[index];
            entry.UnbindEvents();
            lifetimeEntries.RemoveAt(index);
            Remove(entry);

            if (index > 0)
                lifetimeEntries[index - 1].End = entry.End;
        }

        protected override MOsuFollowPointConnection GetDrawable(MOsuFollowPointLifetimeEntry entry)
        {
            var connection = connectionPool.Get();
            connection.Pool = pointPool;
            connection.Apply(entry);
            return connection;
        }

        private void onStartTimeChanged(OsuHitObject hitObject)
        {
            removeEntry(hitObject);
            addEntry(hitObject);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            foreach (var entry in lifetimeEntries)
                entry.UnbindEvents();
            lifetimeEntries.Clear();
        }


    }
}
