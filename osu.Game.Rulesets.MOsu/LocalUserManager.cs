using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Scoring;
using osu.Framework.Allocation;
using osu.Game.Database;
using osu.Game.Online;
using Realms;
using osu.Game.Users;
using osu.Framework.Graphics;

namespace osu.Game.Rulesets.MOsu
{
    [Cached]
    public partial class LocalUserManager : Component
    {
        private readonly RealmAccess realm;
        private readonly osu.Game.Rulesets.MOsu.Database.MOsuRealmAccess mosuRealm;
        private readonly IAPIProvider api;
        // NOTE all in one for now
        private readonly Dictionary<string, UserStatistics> statisticsCache = new Dictionary<string, UserStatistics>();

        private string cacheKey(RulesetInfo ruleset)
        {
            if (ruleset == null)
                return null;
            return $"{ruleset.ShortName ?? "unknown"}:{ActiveProfile.Value ?? "default"}";
        }
        private readonly OsuRuleset ruleset;
        public RulesetInfo RulesetInfo => ruleset.RulesetInfo;

        private readonly Bindable<string> activeProfileBindable = new Bindable<string>();
        public Bindable<string> ActiveProfile => activeProfileBindable;

        public event Action<string>? ProfileChanged;
        public event Action? ProfilesChanged;

        public void SetActiveProfile(string name)
        {

            if (activeProfileBindable.Value == name) return;
            activeProfileBindable.Value = name;
            mosuRealm.Write(r =>
            {
                foreach (var p in r.All<LocalProfile>())
                    p.IsActive = p.Name == name;
            });
            ProfileChanged?.Invoke(name);
            RefreshStatisticsAsync(ruleset.RulesetInfo);
        }

        public List<LocalProfile> GetProfiles()
        {
            return mosuRealm.Run(r => r.All<LocalProfile>().OrderBy(p => p.Name).ToList().Detach());
        }

        public void EnsureDefaultProfile()
        {
            mosuRealm.Write(r =>
            {
                if (r.All<LocalProfile>().Count() == 0 && api.LocalUser.Value?.Username is string username && !string.IsNullOrEmpty(username))
                {
                    r.Add(new LocalProfile { Name = username, IsActive = true });
                    SetActiveProfile(username);
                }
                else
                {
                    var active = r.All<LocalProfile>().FirstOrDefault(p => p.IsActive);
                    if (active != null)
                        SetActiveProfile(active.Name);
                    else
                    {
                        var first = r.All<LocalProfile>().FirstOrDefault();
                        if (first != null)
                        {
                            first.IsActive = true;
                            SetActiveProfile(first.Name);
                        }
                    }
                }
            });
            // Initialize statistics after profiles are ensured
            if (statisticsInitialised == null || statisticsInitialised.IsCompleted)
                statisticsInitialised = InitialiseStatisticsAsync();
        }

        public void AddProfile(string name)
        {
            mosuRealm.Write(r =>
            {
                if (r.All<LocalProfile>().Any(p => p.Name == name))
                    return;
                r.Add(new LocalProfile { Name = name, IsActive = false });
            });
            ProfilesChanged?.Invoke();
        }

        public void RemoveProfile(string name)
        {
            string? fallbackName = null;
            mosuRealm.Write(r =>
            {
                var profile = r.All<LocalProfile>().FirstOrDefault(p => p.Name == name);
                if (profile != null && r.All<LocalProfile>().Count() > 1)
                {
                    if (ActiveProfile.Value == name)
                    {
                        fallbackName = r.All<LocalProfile>().FirstOrDefault(p => p.Name != name)?.Name;
                    }
                    r.Remove(profile);

                    // Remove all scores under this profile for mosusu ruleset only
                    realm.Write(rm =>
                    {
                        rm.RemoveRange(rm.All<ScoreInfo>().Filter("RealmUser.Username == $0 && Ruleset.ShortName == $1", name, ruleset.RulesetInfo.ShortName));
                    });
                }
            });
            if (fallbackName != null)
                SetActiveProfile(fallbackName);
            ProfilesChanged?.Invoke();
        }

        /// <summary>
        /// Returns the <see cref="UserStatistics"/> currently available for the given ruleset.
        /// This may return null if the requested statistics has not been fetched before yet.
        /// </summary>
        /// <param name="ruleset">The ruleset to return the corresponding <see cref="UserStatistics"/> for.</param>
        public UserStatistics? GetStatisticsFor(RulesetInfo ruleset)
        {
            var key = cacheKey(ruleset);
            if (key == null)
                return null;
            return statisticsCache.GetValueOrDefault(key);
        }

        public UserStatistics? GetStatisticsForProfile(string profileName, RulesetInfo ruleset)
        {
            var key = $"{ruleset.ShortName}:{profileName}";
            return statisticsCache.GetValueOrDefault(key);
        }

        private Task? statisticsInitialised;

        private async Task InitialiseStatisticsAsync()
        {
            statisticsCache.Clear();

            if (api.LocalUser.Value == null)
                return;

            var profileNames = GetProfileNames();
            foreach (var name in profileNames)
            {
                var user = await GetLocalUserWithStatisticsForUsernameAsync(name, ruleset.RulesetInfo).ConfigureAwait(false);
                if (user.Statistics != null)
                {
                    var key = $"{ruleset.RulesetInfo.ShortName}:{name}";
                    statisticsCache[key] = user.Statistics;
                }
            }

            // Fire update for active profile so toolbar picks it up
            var activeKey = cacheKey(ruleset.RulesetInfo);
            if (activeKey != null && statisticsCache.TryGetValue(activeKey, out var activeStats))
            {
                var update = new UserStatisticsUpdate(ruleset.RulesetInfo, null, activeStats);
                StatisticsUpdated?.Invoke(update);
            }
        }

        public Task EnsureStatisticsLoadedAsync()
        {
            return statisticsInitialised ?? Task.CompletedTask;
        }

        public async Task UpdateUserStatisticsAsync(RulesetInfo ruleset, Action<UserStatisticsUpdate>? callback = null)
        {
            var user = await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);
            UpdateStatistics(user.Statistics, ruleset, callback);
        }

        public async Task RefreshStatisticsAsync(RulesetInfo ruleset)
        {
            await UpdateUserStatisticsAsync(ruleset).ConfigureAwait(false);
        }

        public List<string> GetProfileNames()
        {
            return mosuRealm.Run(r => r.All<LocalProfile>().OrderBy(p => p.Name).ToList().Detach().Select(p => p.Name).ToList());
        }

        private void CacheStatistics(UserStatistics stats, string profileName, RulesetInfo ruleset)
        {
            var key = $"{ruleset.ShortName}:{profileName}";
            var oldStatistics = statisticsCache.GetValueOrDefault(key);
            statisticsCache[key] = stats;
            var update = new UserStatisticsUpdate(ruleset, oldStatistics, stats);
            StatisticsUpdated?.Invoke(update);
        }

        public event Action<UserStatisticsUpdate>? StatisticsUpdated;

        public void UpdateStatistics(UserStatistics newStatistics, RulesetInfo ruleset, Action<UserStatisticsUpdate>? callback = null)
        {
            var key = cacheKey(ruleset);
            var oldStatistics = statisticsCache.GetValueOrDefault(key);
            statisticsCache[key] = newStatistics;

            var update = new UserStatisticsUpdate(ruleset, oldStatistics, newStatistics);
            callback?.Invoke(update);
            StatisticsUpdated?.Invoke(update);
        }



        public LocalUserManager(OsuRuleset ruleset, RealmAccess realm, osu.Game.Rulesets.MOsu.Database.MOsuRealmAccess mosuRealm, IAPIProvider api)
        {
            this.ruleset = ruleset;
            this.realm = realm;
            this.mosuRealm = mosuRealm;
            this.api = api;

            api.LocalUser.BindValueChanged(_ =>
            {
                // queuing up requests directly on user change is unsafe, as the API status may have not been updated yet.
                // schedule a frame to allow the API to be in its correct state sending requests.
                EnsureDefaultProfile();
            }, true);


        }

        public async Task<APIUser> GetLocalUserWithStatisticsAsync(RulesetInfo ruleset)
        {
            if (api.LocalUser.Value == null)
                return await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);

            var cacheK = cacheKey(ruleset);
            if (GetStatisticsFor(ruleset) is UserStatistics stats)
            {
                return new APIUser
                {
                    Id = api.LocalUser.Value.Id,
                    Username = ActiveProfile.Value ?? api.LocalUser.Value.Username,
                    CountryCode = api.LocalUser.Value.CountryCode,
                    CoverUrl = api.LocalUser.Value.CoverUrl,
                    Statistics = stats
                };
            }
            return await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);
        }

        public (float totalPp, float totalAccuracy) CalculateUserTotalPerformanceAggregates(IEnumerable<ScoreInfo> scores)
        {
            ScoreInfo[] groupedScores = scores.ToArray();

            // Build the diminishing sum
            double factor = 1;
            double totalPp = 0;
            double totalAccuracy = 0;

            foreach (var score in groupedScores)
            {
                totalPp += score.PP!.Value * factor;
                totalAccuracy += score.Accuracy * factor;
                factor *= 0.95;
            }

            // Legacy compatibility factor
            totalPp += (417.0 - 1.0 / 3.0) * (1.0 - Math.Pow(0.995, groupedScores.Length));

            // Normalize accuracy
            if (groupedScores.Length > 0)
            {
                totalAccuracy *= 100.0 / (20 * (1 - Math.Pow(0.95, groupedScores.Length)));
            }

            if (double.IsNegative(totalPp) || double.IsNaN(totalPp) || double.IsInfinity(totalPp))
                throw new InvalidOperationException($"Calculating total PP resulted in invalid value ({totalPp})");

            if (double.IsNaN(totalAccuracy) || double.IsInfinity(totalAccuracy))
                throw new InvalidOperationException($"Calculating total accuracy resulted in invalid value ({totalAccuracy})");

            totalAccuracy = Math.Clamp(totalAccuracy, 0, 100);

            return ((float)totalPp, (float)totalAccuracy);
        }

        public async Task<APIUser> GetLocalUserWithStatisticsUncached(RulesetInfo ruleset)
        {
            var username = ActiveProfile.Value ?? api.LocalUser.Value?.Username ?? "unknown";
            return await GetLocalUserWithStatisticsForUsernameAsync(username, ruleset).ConfigureAwait(false);
        }

        public async Task<APIUser> GetLocalUserWithStatisticsForUsernameAsync(string username, RulesetInfo ruleset)
        {
            return await Task.Run(() =>
            {
                var allScores = GetBestScores(username, ruleset);


                var scoresWithPP = allScores.Where(s => s.PP.HasValue).ToList();

                // --- Rank History Calculation (Last 90 Days) ---
                var rankHistoryData = new int[90];
                var today = DateTimeOffset.UtcNow.Date;

                for (int i = 0; i < 90; i++)
                {
                    var historyDate = today.AddDays(-89 + i);

                    // Note: This logic assumes 'allScores' is already sorted by PP Desc (which the extensions do).
                    // However, for history, we strictly need to filter by date first, then recalculate "Best" for that specific day.
                    // The current logic approximates this by taking the *current* best list and filtering by date.
                    var scoresUpToHistoryDate = scoresWithPP.Where(s => s.Date.Date <= historyDate);

                    (var pp, var acc) = CalculateUserTotalPerformanceAggregates(scoresUpToHistoryDate);
                    rankHistoryData[i] = (int)pp;
                }

                (float currentTotalPP, float accuracy) = CalculateUserTotalPerformanceAggregates(allScores);


                var localUser = api.LocalUser.Value;
                return new APIUser
                {
                    Id = localUser?.Id ?? 0,
                    Username = username,
                    CountryCode = localUser != null ? localUser.CountryCode : default,
                    CoverUrl = localUser?.CoverUrl ?? "",
                    Statistics = new UserStatistics
                    {
                        IsRanked = true,
                        PP = (decimal)currentTotalPP,
                        Accuracy = accuracy,
                        GlobalRank = (int)currentTotalPP,
                        RankHistory = new APIRankHistory
                        {
                            Data = rankHistoryData,
                            Mode = ruleset.ShortName
                        },
                        PlayCount = allScores.Count,
                        TotalScore = allScores.Sum(s => s.TotalScore),
                        PlayTime = (int)allScores.Sum(s => s.BeatmapInfo.Length) / 3600,
                        MaxCombo = allScores.Any() ? allScores.Max(s => s.MaxCombo) : 0,
                        GradesCount = new UserStatistics.Grades
                        {
                            SSPlus = allScores.Count(s => s.Rank == ScoreRank.XH),
                            SS = allScores.Count(s => s.Rank == ScoreRank.X),
                            SPlus = allScores.Count(s => s.Rank == ScoreRank.SH),
                            S = allScores.Count(s => s.Rank == ScoreRank.S),
                            A = allScores.Count(s => s.Rank == ScoreRank.A)
                        },
                    }
                };
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets best performance scores for a specific user (Top Ranks).
        /// Replaces: ByUsername
        /// </summary>
        public List<ScoreInfo> GetBestScores(string username, RulesetInfo ruleset)
        {
            return realm.Run(r => r.All<ScoreInfo>()
                .Filter("RealmUser.Username == $0 && PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $1 SORT(PP DESC) DISTINCT(BeatmapInfo.ID,BeatmapInfo.DifficultyName)", username, ruleset.ShortName)
                .ToList()
                .Detach());
        }

        /// <summary>
        /// Gets all scores that have a replay file available (Local scores), sorted by PP.
        /// Replaces: All
        /// </summary>
        public List<ScoreInfo> GetLocalScores(RulesetInfo ruleset)
        {
            return realm.Run(r => r.All<ScoreInfo>()
                .Filter("PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $0 SORT(PP DESC) DISTINCT(BeatmapInfo.ID,BeatmapInfo.DifficultyName)", ruleset.ShortName)
                .ToList()
                .Detach());
        }

        /// <summary>
        /// Gets recent local scores (with replay files).
        /// Replaces: Recent(ruleset)
        /// </summary>
        public List<ScoreInfo> GetRecentScores(RulesetInfo ruleset)
        {
            return realm.Run(r => r.All<ScoreInfo>()
                .Filter("PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $0 SORT(Date DESC) LIMIT(50)", ruleset.ShortName)
                .ToList()
                .Detach());
        }

        /// <summary>
        /// Gets recent scores for a specific user.
        /// Replaces: Recent(ruleset, username)
        /// </summary>
        public List<ScoreInfo> GetRecentScores(string username, RulesetInfo ruleset)
        {
            return realm.Run(r => r.All<ScoreInfo>()
                .Filter("RealmUser.Username == $0 && PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $1 SORT(Date DESC) LIMIT(50)", username, ruleset.ShortName)
                .ToList()
                .Detach());
        }


    }
}
