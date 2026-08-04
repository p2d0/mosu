using NUnit.Framework;
using osu.Game.Rulesets.MOsu.Utils;

namespace osu.Game.Rulesets.MOsu.Tests
{
    /// <summary>
    /// Verifies <see cref="SuggestionQueryBuilder"/> maps tags to genre keywords and builds osu!web
    /// search query strings exactly as the suggested-songs panel consumed them before extraction.
    /// </summary>
    [TestFixture]
    public class SuggestionQueryBuilderTest
    {
        [Test]
        public void ExtractGenreTagsMatchesMultiWordAndNormalisedGenres()
        {
            var tags = SuggestionQueryBuilder.ExtractGenreTags("kpop j-rock drum and bass vocaloid");

            Assert.That(tags, Does.Contain("kpop"));
            Assert.That(tags, Does.Contain("j rock"), "hyphen should normalise to space for multi-word genres");
            Assert.That(tags, Does.Contain("drum and bass"), "multi-word genre should match across tag tokens");
            Assert.That(tags, Does.Contain("vocaloid"));
        }

        [Test]
        public void BuildMainQueryAppliesBpmStarAndGenreFilters()
        {
            var query = SuggestionQueryBuilder.BuildMainQuery(160, 180, 4, 8, maxStarsIsDefault: false, tags: "kpop");

            Assert.That(query, Is.EqualTo("favourites>1 bpm>=160 bpm<=180 stars>=4 stars<=8 \"kpop\""));
        }

        [Test]
        public void BuildMainQueryOmitsStarCapWhenUpperBoundIsDefault()
        {
            var query = SuggestionQueryBuilder.BuildMainQuery(160, 180, 4, 10.1, maxStarsIsDefault: true, tags: "");

            Assert.That(query, Does.Contain("stars>=4"));
            Assert.That(query, Does.Not.Contain("stars<="));
        }

        [Test]
        public void BuildArtistQueryQuotesArtistAndAppliesStars()
        {
            var query = SuggestionQueryBuilder.BuildArtistQuery("Camellia", 4, 8, maxStarsIsDefault: false);

            Assert.That(query, Is.EqualTo("artist:\"Camellia\" favourites>1 stars>=4 stars<=8"));
        }
    }
}
