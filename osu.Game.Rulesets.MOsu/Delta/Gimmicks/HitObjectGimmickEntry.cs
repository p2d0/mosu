// Ported from delta fork (osu.Game/Beatmaps/HitObjectGimmicks/HitObjectGimmickEntry.cs).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.MOsu.Delta.Gimmicks
{
    public class HitObjectGimmickEntry
    {
        public long? ObjectId { get; set; }

        public double StartTime { get; set; }

        public int ComboIndexWithOffsets { get; set; }

        /// <summary>
        /// Index of the bound object in the playable beatmap's HitObjects list at save/bind time.
        /// Disambiguates objects that share the same legacy key when list order is unstable.
        /// </summary>
        public int? HitObjectIndex { get; set; }

        public HitObjectGimmickSettings Settings { get; set; } = new HitObjectGimmickSettings();
    }
}
