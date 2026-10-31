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
                if (!TestMode || value == BallSkin.Chrome || AdsWatched(SkinKey(value)) >= CosmeticAdsToUnlock) PlayerPrefs.SetInt("db.ballSkin", (int)value);
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
                if (!TestMode || value == TrailEffect.Classic || AdsWatched(TrailKey(value)) >= CosmeticAdsToUnlock) PlayerPrefs.SetInt("db.trail", (int)value);
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
        public static bool IsUnlocked(SongData song) => song == null || !song.locked || TestMode || AdsWatched(SongKey(song)) >= SongAdsToUnlock;
        public static int AdsWatched(SongData song) => song == null ? 0 : Mathf.Min(SongAdsToUnlock, AdsWatched(SongKey(song)));
        public static int WatchedAdFor(SongData song) => song == null ? 0 : AddAdWatched(SongKey(song));

        public static bool IsUnlocked(BallSkin s) => s == BallSkin.Chrome || TestMode || AdsWatched(SkinKey(s)) >= CosmeticAdsToUnlock;
        public static int AdsWatched(BallSkin s) => Mathf.Min(CosmeticAdsToUnlock, AdsWatched(SkinKey(s)));
        public static int WatchedAdFor(BallSkin s) => AddAdWatched(SkinKey(s));

        public static bool IsUnlocked(TrailEffect t) => t == TrailEffect.Classic || TestMode || AdsWatched(TrailKey(t)) >= CosmeticAdsToUnlock;
        public static int AdsWatched(TrailEffect t) => Mathf.Min(CosmeticAdsToUnlock, AdsWatched(TrailKey(t)));
        public static int WatchedAdFor(TrailEffect t) => AddAdWatched(TrailKey(t));

        public static void Save() => PlayerPrefs.Save();
    }
}
