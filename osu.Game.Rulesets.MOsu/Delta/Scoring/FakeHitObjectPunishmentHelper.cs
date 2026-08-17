// Ported from delta fork (osu.Game.Rulesets.Osu/Scoring/FakeHitObjectPunishmentHelper.cs).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.MOsu.Delta.Scoring
{
    internal static class FakeHitObjectPunishmentHelper
    {
        public static bool IsFakeHitObject(HitObject hitObject)
            => hitObject is FakeHitCircle || hitObject is FakeSlider;

        public static bool IsPrimaryFakeHitObject(HitObject hitObject)
            => hitObject is FakeHitCircle || hitObject is FakeSlider;

        public static FakePunishMode ResolvePunishMode(HitObject hitObject)
            => hitObject switch
            {
                FakeHitCircle fakeCircle => fakeCircle.FakePunishMode,
                FakeSlider fakeSlider => fakeSlider.FakePunishMode,
                _ => FakePunishMode.None,
            };

        public static bool ShouldPunishAsMiss(FakePunishMode mode)
            => mode == FakePunishMode.Miss;
    }
}
