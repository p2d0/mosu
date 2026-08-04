namespace osu.Game.Rulesets.MOsu.Models
{
    public class LocalProfile
    {
        public string Name { get; set; } = "";

        public bool IsActive { get; set; }

        /// <summary>
        /// Number of recorded plays, persisted in the profile JSON.
        /// </summary>
        public int PlayCount { get; set; }
    }
}
