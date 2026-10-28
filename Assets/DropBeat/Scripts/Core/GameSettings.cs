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
            get => PlayerPrefs.GetFloat(KeyMusic, 0.85f);
            set => PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value));
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfx, 0.6f);
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

        public struct Best
        {
            public int score;
            public float accuracy;
            public string rank;
            public bool fullCombo;
            public bool Exists => score > 0;
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
                fullCombo = PlayerPrefs.GetInt(k + ".fc", 0) == 1
            };
        }

        /// <summary>Stores the result if it beats the previous best. Returns true for a new record.</summary>
        public static bool SubmitBest(SongData song, Difficulty d, int score, float accuracy, string rank, bool fullCombo)
        {
            Best old = GetBest(song, d);
            if (score <= old.score) return false;
            string k = BestKey(song, d);
            PlayerPrefs.SetInt(k + ".score", score);
            PlayerPrefs.SetFloat(k + ".acc", accuracy);
            PlayerPrefs.SetString(k + ".rank", rank);
            PlayerPrefs.SetInt(k + ".fc", fullCombo ? 1 : 0);
            PlayerPrefs.Save();
            return true;
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
