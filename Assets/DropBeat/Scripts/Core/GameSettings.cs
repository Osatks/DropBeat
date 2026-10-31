using UnityEngine;

namespace DropBeat
{
    /// <summary>Persistent player settings and best scores (PlayerPrefs).</summary>
    public static class GameSettings
    {
        private const string KeyOffset = "db.offsetMs";
        private const string KeyAssist = "db.assistTick";
        private const string KeyHitSound = "db.hitSound";
        private const string KeyMusic = "db.musicVol";
        private const string KeySfx = "db.sfxVol";
        private const string KeyNoFail = "db.noFail";
        private const string KeyAuto = "db.autoplay";
        private const string KeyDiff = "db.difficulty";
        private const string KeySong = "db.song";
        private const string KeyCalibrated = "db.calibrated";

        /// <summary>Default output latency guess: one DSP buffer.</summary>
        public static float DefaultOffsetMs
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out _);
                int sr = Mathf.Max(8000, AudioSettings.outputSampleRate);
                return Mathf.Round(1000f * length / sr);
            }
        }

        public static float OffsetMs
        {
            get => PlayerPrefs.GetFloat(KeyOffset, DefaultOffsetMs);
            set => PlayerPrefs.SetFloat(KeyOffset, Mathf.Clamp(value, -200f, 300f));
        }

        public static bool Calibrated
        {
            get => PlayerPrefs.GetInt(KeyCalibrated, 0) == 1;
            set => PlayerPrefs.SetInt(KeyCalibrated, value ? 1 : 0);
        }

        public static bool AssistTick
        {
            get => PlayerPrefs.GetInt(KeyAssist, 0) == 1;
            set => PlayerPrefs.SetInt(KeyAssist, value ? 1 : 0);
        }

        public static bool HitSound
        {
            get => PlayerPrefs.GetInt(KeyHitSound, 1) == 1;
            set => PlayerPrefs.SetInt(KeyHitSound, value ? 1 : 0);
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(KeyMusic, 0.8f);
            set => PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value));
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfx, 0.8f);
            set => PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value));
        }

        public static bool NoFail
        {
            get => PlayerPrefs.GetInt(KeyNoFail, 0) == 1;
            set => PlayerPrefs.SetInt(KeyNoFail, value ? 1 : 0);
        }

        public static bool Autoplay
        {
            get => PlayerPrefs.GetInt(KeyAuto, 0) == 1;
            set => PlayerPrefs.SetInt(KeyAuto, value ? 1 : 0);
        }

        public static Difficulty Difficulty
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(KeyDiff, 1), 0, 2);
            set => PlayerPrefs.SetInt(KeyDiff, (int)value);
        }

        public static int SongIndex
        {
            get => PlayerPrefs.GetInt(KeySong, 0);
            set => PlayerPrefs.SetInt(KeySong, value);
        }

        /// <summary>Stars per chart (bitmask): clear, full combo, rank A or better.</summary>
        public const int StarClear = 1, StarFullCombo = 2, StarRankA = 4;

        /// <summary>A chart's records. Every field only ever improves (except the fail streak).</summary>
        public struct Best
        {
            public int score;
            public float accuracy;
            public string rank;
            public bool fullCombo;
            public int maxCombo, plays, clears, stars, failRun;
            public bool allPerfect;
            /// <summary>Furthest a failed run got (0..1); 1 once cleared.</summary>
            public float progress;
            public bool Exists => score > 0;
            public int StarCount => (stars & 1) + ((stars >> 1) & 1) + ((stars >> 2) & 1);
        }

        /// <summary>What a run changed (for the results screen).</summary>
        public struct RunRecord
        {
            public Best before, after;
            public bool firstClear, newScore, newAccuracy, newRank, newCombo, newFullCombo, newProgress;
            public int newStars;
            /// <summary>This run unlocked a clear reward: the Champion ball (first Hard clear), the Aurora trail (first Normal or Hard clear).</summary>
            public bool unlockedSkin, unlockedTrail;
        }

        private static string BestKey(SongData song, Difficulty d) => "db.best." + song.name + "." + (int)d;

        public static Best GetBest(SongData song, Difficulty d)
        {
            string k = BestKey(song, d);
            return new Best
            {
                score = PlayerPrefs.GetInt(k + ".score", 0),
                accuracy = PlayerPrefs.GetFloat(k + ".acc", 0f),
                rank = PlayerPrefs.GetString(k + ".rank", string.Empty),
                fullCombo = PlayerPrefs.GetInt(k + ".fc", 0) == 1,
                maxCombo = PlayerPrefs.GetInt(k + ".combo", 0),
                plays = PlayerPrefs.GetInt(k + ".plays", 0),
                clears = PlayerPrefs.GetInt(k + ".clears", 0),
                stars = PlayerPrefs.GetInt(k + ".stars", 0),
                failRun = PlayerPrefs.GetInt(k + ".failRun", 0),
                allPerfect = PlayerPrefs.GetInt(k + ".ap", 0) == 1,
                progress = PlayerPrefs.GetFloat(k + ".prog", 0f)
            };
        }

        /// <summary>0 = best rank (SS) .. 6 = F; empty counts as worst.</summary>
        public static int RankIndex(string r)
        {
            int i = string.IsNullOrEmpty(r) ? -1 : System.Array.IndexOf(new[] { "SS", "S", "A", "B", "C", "D", "F" }, r);
            return i < 0 ? 7 : i;
        }

        /// <summary>Records a finished run (fails too). Records never go backwards; returns what changed.</summary>
        public static RunRecord SubmitRun(SongData song, Difficulty d, ScoreState s, string rank, bool cleared, float progress)
        {
            Best before = GetBest(song, d);
            Best b = before;
            b.plays++;
            var rec = new RunRecord { before = before };
            if (cleared)
            {
                b.clears++;
                b.failRun = 0;
                rec.firstClear = before.clears == 0;
                rec.newScore = s.Score > before.score;
                rec.newAccuracy = s.Accuracy > before.accuracy + 1e-5f;
                rec.newCombo = s.MaxCombo > before.maxCombo;
                rec.newRank = RankIndex(rank) < RankIndex(before.rank);
                rec.newFullCombo = s.FullCombo && !before.fullCombo;
                b.score = Mathf.Max(b.score, s.Score);
                b.accuracy = Mathf.Max(b.accuracy, s.Accuracy);
                b.maxCombo = Mathf.Max(b.maxCombo, s.MaxCombo);
                if (rec.newRank) b.rank = rank;
                b.fullCombo |= s.FullCombo;
                b.allPerfect |= s.TotalNotes > 0 && s.Perfect == s.TotalNotes;
                rec.newProgress = before.progress < 1f;
                b.progress = 1f;
            }
            else
            {
                b.failRun++;
                rec.newProgress = progress > before.progress + 1e-4f;
                b.progress = Mathf.Max(b.progress, progress);
            }
            if (cleared && d == Difficulty.Hard && !HardClearReward)
            {
                HardClearReward = true;
                rec.unlockedSkin = true;
            }
            if (cleared && d != Difficulty.Easy && !NormalClearReward)
            {
                NormalClearReward = true;
                rec.unlockedTrail = true;
            }
            int earned = cleared ? StarClear | (s.FullCombo ? StarFullCombo : 0) | (s.Accuracy >= 0.9f ? StarRankA : 0) : 0;
            b.stars |= earned;
            rec.newStars = earned & ~before.stars;

            string k = BestKey(song, d);
            PlayerPrefs.SetInt(k + ".score", b.score);
            PlayerPrefs.SetFloat(k + ".acc", b.accuracy);
            PlayerPrefs.SetString(k + ".rank", b.rank ?? string.Empty);
            PlayerPrefs.SetInt(k + ".fc", b.fullCombo ? 1 : 0);
            PlayerPrefs.SetInt(k + ".combo", b.maxCombo);
            PlayerPrefs.SetInt(k + ".plays", b.plays);
            PlayerPrefs.SetInt(k + ".clears", b.clears);
            PlayerPrefs.SetInt(k + ".stars", b.stars);
            PlayerPrefs.SetInt(k + ".failRun", b.failRun);
            PlayerPrefs.SetInt(k + ".ap", b.allPerfect ? 1 : 0);
            PlayerPrefs.SetFloat(k + ".prog", b.progress);
            PlayerPrefs.Save();
            rec.after = b;
            return rec;
        }

        public static int Stars(SongData song, Difficulty d) => GetBest(song, d).StarCount;

        /// <summary>Stars over every song and difficulty (menu header).</summary>
        public static int TotalStars(SongLibrary library)
        {
            int n = 0;
            if (library == null) return 0;
            foreach (SongData s in library.songs)
            {
                if (s == null) continue;
                for (int d = 0; d < 3; d++) n += Stars(s, (Difficulty)d);
            }
            return n;
        }

        /// <summary>The equipped ball skin; falls back to Chrome if the saved one is (still) locked.</summary>
        public static BallSkin BallSkin
        {
            get
            {
                // Test mode keeps its own choice, so turning it off returns to the player's real one.
                int saved = TestMode && PlayerPrefs.HasKey("db.ballSkin.test") ? PlayerPrefs.GetInt("db.ballSkin.test") : PlayerPrefs.GetInt("db.ballSkin", 0);
                var s = (BallSkin)Mathf.Clamp(saved, 0, BallStyles.SkinCount - 1);
                return IsUnlocked(s) ? s : BallSkin.Chrome;
            }
            set
            {
                if (TestMode) PlayerPrefs.SetInt("db.ballSkin.test", (int)value);
                if (!TestMode || EarnedUnlock(value)) PlayerPrefs.SetInt("db.ballSkin", (int)value);
            }
        }

        /// <summary>The equipped trail; falls back to Classic if the saved one is (still) locked.</summary>
        public static TrailEffect Trail
        {
            get
            {
                int saved = TestMode && PlayerPrefs.HasKey("db.trail.test") ? PlayerPrefs.GetInt("db.trail.test") : PlayerPrefs.GetInt("db.trail", 0);
                var t = (TrailEffect)Mathf.Clamp(saved, 0, BallStyles.TrailCount - 1);
                return IsUnlocked(t) ? t : TrailEffect.Classic;
            }
            set
            {
                if (TestMode) PlayerPrefs.SetInt("db.trail.test", (int)value);
                if (!TestMode || EarnedUnlock(value)) PlayerPrefs.SetInt("db.trail", (int)value);
            }
        }

        // ------------------------------------------------------------------ unlocks (rewarded ads)

        /// <summary>
        /// Test mode: everything unlocked and no between-song ads. Only available while
        /// <see cref="AdsConfig.allowTestMode"/> is on (turn that off before publishing).
        /// </summary>
        public static bool TestMode
        {
            get => AdsConfig.Instance.allowTestMode && PlayerPrefs.GetInt("db.testMode", 0) == 1;
            set
            {
                PlayerPrefs.SetInt("db.testMode", value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Rewarded ads needed to unlock a locked song.</summary>
        public const int SongAdsToUnlock = 5;
        /// <summary>Rewarded ads needed to unlock a ball skin or a trail effect (Chrome and Classic are free).</summary>
        public const int CosmeticAdsToUnlock = 10;

        private static string SongKey(SongData song) => "song." + song.name;
        private static string SkinKey(BallSkin s) => "skin." + s;
        private static string TrailKey(TrailEffect t) => "trail." + t;

        private static int AdsWatched(string key) => PlayerPrefs.GetInt("db.ads." + key, 0);

        /// <summary>Counts one watched rewarded ad toward an unlock and saves it; returns the new count.</summary>
        private static int AddAdWatched(string key)
        {
            int n = AdsWatched(key) + 1;
            PlayerPrefs.SetInt("db.ads." + key, n);
            PlayerPrefs.Save();
            return n;
        }

        /// <summary>False for a locked song the player has not yet watched enough rewarded ads for.</summary>
        public static bool IsUnlocked(SongData song) => song == null || !song.locked || TestMode || Owns(Purchases.AllSongs) || AdsWatched(SongKey(song)) >= SongAdsToUnlock;
        public static int AdsWatched(SongData song) => song == null ? 0 : Mathf.Min(SongAdsToUnlock, AdsWatched(SongKey(song)));
        public static int WatchedAdFor(SongData song) => song == null ? 0 : AddAdWatched(SongKey(song));

        public static bool IsUnlocked(BallSkin s) => TestMode || EarnedUnlock(s);
        /// <summary>Unlocked for real (test mode aside): free, earned by a clear, or paid for with ads.</summary>
        private static bool EarnedUnlock(BallSkin s) =>
            s == BallSkin.Chrome || (IsClearReward(s) ? HardClearReward : Owns(Purchases.AllStyles) || AdsWatched(SkinKey(s)) >= CosmeticAdsToUnlock);
        public static int AdsWatched(BallSkin s) => Mathf.Min(CosmeticAdsToUnlock, AdsWatched(SkinKey(s)));
        public static int WatchedAdFor(BallSkin s) => AddAdWatched(SkinKey(s));

        public static bool IsUnlocked(TrailEffect t) => TestMode || EarnedUnlock(t);
        private static bool EarnedUnlock(TrailEffect t) =>
            t == TrailEffect.Classic || (IsClearReward(t) ? NormalClearReward : Owns(Purchases.AllStyles) || AdsWatched(TrailKey(t)) >= CosmeticAdsToUnlock);
        public static int AdsWatched(TrailEffect t) => Mathf.Min(CosmeticAdsToUnlock, AdsWatched(TrailKey(t)));
        public static int WatchedAdFor(TrailEffect t) => AddAdWatched(TrailKey(t));

        // ------------------------------------------------------------------ purchases (Unity IAP)

        public static bool Owns(string productId) => PlayerPrefs.GetInt("db.iap." + productId, 0) == 1;

        public static void SetOwned(string productId)
        {
            PlayerPrefs.SetInt("db.iap." + productId, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Remove Ads: no more full-screen ads between songs (rewarded ads stay, the player picks them).</summary>
        public static bool NoAds => Owns(Purchases.RemoveAds);

        public static bool PaidAnything
        {
            get
            {
                foreach (string id in Purchases.All) if (Owns(id)) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ ad frequency test

        /// <summary>
        /// Songs between full-screen ads for this player. When the test is on, each new install is put in
        /// one group (every 2, 3 or 4 songs) for good; the group is sent to LevelPlay as the user segment
        /// ("every2" / "every3" / "every4"), so its reports compare revenue and retention per group.
        /// </summary>
        public static int InterstitialEvery
        {
            get
            {
                AdsConfig c = AdsConfig.Instance;
                if (!c.abTestInterstitialEvery || c.interstitialEveryOptions == null || c.interstitialEveryOptions.Length == 0)
                    return c.interstitialEvery;
                if (!PlayerPrefs.HasKey("db.ab.every"))
                {
                    PlayerPrefs.SetInt("db.ab.every", c.interstitialEveryOptions[UnityEngine.Random.Range(0, c.interstitialEveryOptions.Length)]);
                    PlayerPrefs.Save();
                }
                return Mathf.Max(1, PlayerPrefs.GetInt("db.ab.every", c.interstitialEvery));
            }
        }

        // ------------------------------------------------------------------ clear rewards (no ads)

        /// <summary>The Champion ball is won by clearing any song on Hard; ads can't buy it.</summary>
        public static bool IsClearReward(BallSkin s) => s == BallSkin.Champion;
        /// <summary>The Aurora trail is won by clearing any song on Normal (or Hard).</summary>
        public static bool IsClearReward(TrailEffect t) => t == TrailEffect.Aurora;

        public static bool HardClearReward
        {
            get => PlayerPrefs.GetInt("db.reward.hard", 0) == 1;
            private set => PlayerPrefs.SetInt("db.reward.hard", value ? 1 : 0);
        }

        public static bool NormalClearReward
        {
            get => PlayerPrefs.GetInt("db.reward.normal", 0) == 1;
            private set => PlayerPrefs.SetInt("db.reward.normal", value ? 1 : 0);
        }

        /// <summary>Grants the clear rewards to players who cleared songs before the rewards existed.</summary>
        public static void SyncClearRewards(SongLibrary library)
        {
            if (library == null) return;
            foreach (SongData s in library.songs)
            {
                if (s == null) continue;
                if (GetBest(s, Difficulty.Hard).clears > 0) HardClearReward = NormalClearReward = true;
                if (GetBest(s, Difficulty.Normal).clears > 0) NormalClearReward = true;
            }
        }

        // ------------------------------------------------------------------ lifetime stats (profile)

        /// <summary>Lifetime totals over every real run (autoplay never counts).</summary>
        public struct Stats
        {
            public int runs, clears, fails, fullCombos, allPerfects, bestCombo, bossesDefeated;
            public int perfect, great, good, miss;
            public long totalScore;
            public float playSeconds;
            /// <summary>XP won from Beat Boxes.</summary>
            public long bonusXp;

            public int Hits => perfect + great + good;
            public float Accuracy => Hits + miss > 0 ? (perfect + great * 0.7f + good * 0.4f) / (Hits + miss) : 0f;
            /// <summary>Experience: 1 per 100 points scored, plus 50 per clear, plus XP from Beat Boxes.</summary>
            public long Xp => totalScore / 100 + clears * 50L + bonusXp;
        }

        private const string StatKey = "db.stat.";

        public static Stats GetStats()
        {
            return new Stats
            {
                runs = PlayerPrefs.GetInt(StatKey + "runs", 0),
                clears = PlayerPrefs.GetInt(StatKey + "clears", 0),
                fails = PlayerPrefs.GetInt(StatKey + "fails", 0),
                fullCombos = PlayerPrefs.GetInt(StatKey + "fc", 0),
                allPerfects = PlayerPrefs.GetInt(StatKey + "ap", 0),
                bestCombo = PlayerPrefs.GetInt(StatKey + "bestCombo", 0),
                bossesDefeated = PlayerPrefs.GetInt(StatKey + "boss", 0),
                perfect = PlayerPrefs.GetInt(StatKey + "perfect", 0),
                great = PlayerPrefs.GetInt(StatKey + "great", 0),
                good = PlayerPrefs.GetInt(StatKey + "good", 0),
                miss = PlayerPrefs.GetInt(StatKey + "miss", 0),
                // PlayerPrefs has no long: the lifetime score is stored as a string.
                totalScore = long.TryParse(PlayerPrefs.GetString(StatKey + "score", "0"), out long ts) ? ts : 0L,
                playSeconds = PlayerPrefs.GetFloat(StatKey + "time", 0f),
                bonusXp = PlayerPrefs.GetInt(StatKey + "bonusXp", 0)
            };
        }

        /// <summary>Adds a finished run (cleared or failed) to the lifetime stats.</summary>
        public static void RecordRun(ScoreState s, bool cleared, float seconds, bool bossDefeated)
        {
            Stats st = GetStats();
            st.runs++;
            if (cleared) st.clears++;
            else st.fails++;
            if (cleared && s.FullCombo) st.fullCombos++;
            if (cleared && s.TotalNotes > 0 && s.Perfect == s.TotalNotes) st.allPerfects++;
            if (bossDefeated) st.bossesDefeated++;
            st.bestCombo = Mathf.Max(st.bestCombo, s.MaxCombo);
            st.perfect += s.Perfect;
            st.great += s.Great;
            st.good += s.Good;
            st.miss += s.Miss;
            st.totalScore += s.Score;
            st.playSeconds += Mathf.Max(0f, seconds);

            PlayerPrefs.SetInt(StatKey + "runs", st.runs);
            PlayerPrefs.SetInt(StatKey + "clears", st.clears);
            PlayerPrefs.SetInt(StatKey + "fails", st.fails);
            PlayerPrefs.SetInt(StatKey + "fc", st.fullCombos);
            PlayerPrefs.SetInt(StatKey + "ap", st.allPerfects);
            PlayerPrefs.SetInt(StatKey + "bestCombo", st.bestCombo);
            PlayerPrefs.SetInt(StatKey + "boss", st.bossesDefeated);
            PlayerPrefs.SetInt(StatKey + "perfect", st.perfect);
            PlayerPrefs.SetInt(StatKey + "great", st.great);
            PlayerPrefs.SetInt(StatKey + "good", st.good);
            PlayerPrefs.SetInt(StatKey + "miss", st.miss);
            PlayerPrefs.SetString(StatKey + "score", st.totalScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.SetFloat(StatKey + "time", st.playSeconds);
            PlayerPrefs.Save();
        }

        public static void AddBonusXp(int xp) => PlayerPrefs.SetInt(StatKey + "bonusXp", PlayerPrefs.GetInt(StatKey + "bonusXp", 0) + Mathf.Max(0, xp));

        /// <summary>
        /// Beat Box reward: <paramref name="steps"/> unlock steps (the same steps a rewarded ad gives) toward
        /// one random locked ball, trail or song that ads can unlock; a big enough box unlocks it outright.
        /// Clear rewards (Champion, Aurora) are never in boxes: those are won by playing.
        /// </summary>
        public static void GrantRandomUnlockProgress(int steps, ref BoxReward reward)
        {
            var keys = new System.Collections.Generic.List<string>();
            var names = new System.Collections.Generic.List<string>();
            var kinds = new System.Collections.Generic.List<string>();
            var needs = new System.Collections.Generic.List<int>();
            for (int i = 0; i < BallStyles.SkinCount; i++)
            {
                var s = (BallSkin)i;
                if (EarnedUnlock(s) || IsClearReward(s)) continue;
                keys.Add(SkinKey(s)); names.Add(BallStyles.Name(s)); kinds.Add("ball"); needs.Add(CosmeticAdsToUnlock);
            }
            for (int i = 0; i < BallStyles.TrailCount; i++)
            {
                var t = (TrailEffect)i;
                if (EarnedUnlock(t) || IsClearReward(t)) continue;
                keys.Add(TrailKey(t)); names.Add(BallStyles.Name(t)); kinds.Add("trail"); needs.Add(CosmeticAdsToUnlock);
            }
            SongLibrary lib = Resources.Load<SongLibrary>("SongLibrary");
            if (lib != null)
            {
                foreach (SongData song in lib.songs)
                {
                    if (song == null || !song.locked || AdsWatched(SongKey(song)) >= SongAdsToUnlock) continue;
                    keys.Add(SongKey(song)); names.Add(song.title.ToUpperInvariant()); kinds.Add("song"); needs.Add(SongAdsToUnlock);
                }
            }
            if (keys.Count == 0)
            {
                reward.item = string.Empty;
                return;
            }
            int pick = UnityEngine.Random.Range(0, keys.Count);
            int have = AdsWatched(keys[pick]);
            int after = Mathf.Min(needs[pick], have + steps);
            PlayerPrefs.SetInt("db.ads." + keys[pick], after);
            reward.item = names[pick];
            reward.itemKind = kinds[pick];
            reward.progress = after - have;
            reward.have = after;
            reward.need = needs[pick];
            reward.unlocked = after >= needs[pick];
        }

        /// <summary>Player level from experience: each level needs 250 XP more than the one before.</summary>
        public static int Level(long xp, out float progress)
        {
            int level = 1;
            long need = 250, left = xp;
            while (left >= need && level < 99)
            {
                left -= need;
                level++;
                need += 250;
            }
            progress = level >= 99 ? 1f : left / (float)need;
            return level;
        }

        public static string LevelTitle(int level)
        {
            if (level >= 30) return "LEGEND";
            if (level >= 20) return "BEAT MASTER";
            if (level >= 12) return "RHYTHM ACE";
            if (level >= 6) return "GROOVER";
            return "ROOKIE";
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
