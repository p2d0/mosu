// Selection handler that resolves fake-note clones to their real source objects on delete.
//
// Fake-note drawables bind a clone (see DeltaGimmickApplier.CreateFakeObject) that is not
// present in the beatmap, so a plain delete silently no-ops (EditorBeatmap.Remove finds no
// matching index). Mapping each deleted item to the beatmap object with the same ObjectId
// makes the delete fire HitObjectRemoved, which removes the gimmick entry too.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Delta.Edit
{
    public partial class DeltaSelectionHandler : OsuSelectionHandler
    {
        protected override void DeleteItems(IEnumerable<HitObject> items)
        {
            var list = items.ToList();
            Logger.Log($"[MOsu-Select] DeleteItems called with {list.Count} items: {string.Join(", ", list.Select(i => $"{i.GetType().Name}@{i.StartTime} id={DeltaGimmickApplier.GetObjectId(i as OsuHitObject)}"))}");

            var resolved = list.Select(i => ResolveToSource(i, EditorBeatmap)).ToList();
            Logger.Log($"[MOsu-Select] resolved to: {string.Join(", ", resolved.Select(i => $"{i.GetType().Name}@{i.StartTime} id={DeltaGimmickApplier.GetObjectId(i as OsuHitObject)}"))}");

            base.DeleteItems(resolved);
        }

        public static HitObject ResolveToSource(HitObject item, EditorBeatmap editorBeatmap)
        {
            if (item is not OsuHitObject osuItem)
                return item;

            long id = DeltaGimmickApplier.GetObjectId(osuItem);

            foreach (var h in editorBeatmap.HitObjects.OfType<OsuHitObject>())
            {
                if (DeltaGimmickApplier.GetObjectId(h) == id)
                {
                    Logger.Log($"[MOsu-Select] item {item.GetType().Name}@{item.StartTime} id={id} -> source {h.GetType().Name}@{h.StartTime}");
                    return h;
                }
            }

            Logger.Log($"[MOsu-Select] item {item.GetType().Name}@{item.StartTime} id={id} has no matching source; deleting as-is");
            return item;
        }
    }
}
