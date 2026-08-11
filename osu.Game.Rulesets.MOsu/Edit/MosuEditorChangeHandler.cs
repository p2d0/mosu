// Undo/redo change handler for the mosu editor. The core only creates a change handler
// when the ruleset can save (ILegacyRuleset), which excludes mosu, so undo/redo were
// no-ops. This mirrors BeatmapEditorChangeHandler but also serialises the gimmick sections
// into each state, so gimmick edits are part of the undo history too.

using System.IO;
using System.Text;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public partial class MosuEditorChangeHandler : EditorChangeHandler
    {
        private readonly EditorBeatmap editorBeatmap;
        private readonly LegacyEditorBeatmapPatcher patcher;
        private readonly MosuBeatmap? playable;

        public MosuEditorChangeHandler(EditorBeatmap editorBeatmap)
        {
            this.editorBeatmap = editorBeatmap;
            playable = editorBeatmap.PlayableBeatmap as MosuBeatmap;
            patcher = new LegacyEditorBeatmapPatcher(editorBeatmap);

            editorBeatmap.TransactionBegan += BeginChange;
            editorBeatmap.TransactionEnded += EndChange;
            editorBeatmap.SaveStateTriggered += SaveState;
        }

        protected override void WriteCurrentStateToStream(MemoryStream stream)
        {
            var originalRuleset = editorBeatmap.BeatmapInfo.Ruleset;

            using (var sw = new StreamWriter(stream, Encoding.UTF8, 1024, true))
            {
                // The legacy encoder only accepts legacy rulesets; present as osu-mode like the saver does.
                try
                {
                    editorBeatmap.BeatmapInfo.Ruleset = new RulesetInfo { OnlineID = 0, ShortName = "osu" };
                    new LegacyBeatmapEncoder(editorBeatmap, editorBeatmap.BeatmapSkin, editorBeatmap.Storyboard).Encode(sw);
                }
                finally
                {
                    editorBeatmap.BeatmapInfo.Ruleset = originalRuleset;
                }

                if (playable != null)
                    sw.Write(MosuGimmickSerializer.Serialize(playable.Gimmicks.Sections, playable.Gimmicks.HitObjectGimmicks));
            }
        }

        protected override void ApplyStateChange(byte[] previousState, byte[] newState)
        {
            patcher.Patch(previousState, newState);

            if (playable == null)
                return;

            using var reader = new StreamReader(new MemoryStream(newState));
            (playable.Gimmicks.Sections, playable.Gimmicks.HitObjectGimmicks) = MosuGimmickParser.Parse(reader);
            playable.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playable, playable.Gimmicks, mutateList: false);
        }
    }
}
