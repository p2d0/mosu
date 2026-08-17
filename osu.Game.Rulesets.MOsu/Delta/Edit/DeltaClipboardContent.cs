// Clipboard content that also carries the gimmick entries for the copied objects, so
// copy/paste/cut/duplicate preserve hitobject gimmicks (fake notes, overrides, ...).

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Delta.Edit
{
    public class DeltaClipboardContent : ClipboardContent
    {
        public List<HitObjectGimmickEntry> Gimmicks { get; set; } = new List<HitObjectGimmickEntry>();

        public DeltaClipboardContent()
        {
        }

        public DeltaClipboardContent(EditorBeatmap editorBeatmap)
            : base(editorBeatmap)
        {
            if (editorBeatmap.PlayableBeatmap is not DeltaBeatmap mosu)
                return;

            var selectedIds = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>()
                .Select(DeltaGimmickApplier.GetObjectId)
                .ToHashSet();

            Gimmicks = mosu.Gimmicks.HitObjectGimmicks.Entries
                .Where(e => e.ObjectId.HasValue && selectedIds.Contains(e.ObjectId.Value))
                .Select(CloneEntry)
                .ToList();
        }

        public static HitObjectGimmickEntry CloneEntry(HitObjectGimmickEntry entry)
        {
            var settings = entry.Settings ?? new HitObjectGimmickSettings();

            return new HitObjectGimmickEntry
            {
                ObjectId = entry.ObjectId,
                StartTime = entry.StartTime,
                ComboIndexWithOffsets = entry.ComboIndexWithOffsets,
                HitObjectIndex = entry.HitObjectIndex,
                Settings = HitObjectGimmickBindingUtils.CloneSettings(settings),
            };
        }
    }
}
