// Clipboard content that also carries the gimmick entries for the copied objects, so
// copy/paste/cut/duplicate preserve hitobject gimmicks (fake notes, overrides, ...).

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public class MosuClipboardContent : ClipboardContent
    {
        public List<HitObjectGimmickEntry> Gimmicks { get; set; } = new List<HitObjectGimmickEntry>();

        public MosuClipboardContent()
        {
        }

        public MosuClipboardContent(EditorBeatmap editorBeatmap)
            : base(editorBeatmap)
        {
            if (editorBeatmap.PlayableBeatmap is not MosuBeatmap mosu)
                return;

            var selectedIds = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>()
                .Select(MosuGimmickApplier.GetObjectId)
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
