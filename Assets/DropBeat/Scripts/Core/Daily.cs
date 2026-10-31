using System;
using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>How rare a Beat Box roll is (also the box's minimum when a reward guarantees one).</summary>
    public enum BoxRarity { Common, Rare, Epic, Legendary }

    /// <summary>What a Beat Box gave: XP always, plus progress toward (or the unlock of) one locked item.</summary>
    public struct BoxReward
    {
        public BoxRarity rarity;
        public int xp;
        /// <summary>Display name of the item that got progress ("" when everything is unlocked).</summary>
        public string item;
        public string itemKind; // "ball", "trail" or "song"
        public int progress;    // steps added toward its unlock
        public bool unlocked;   // the box finished (or gave) the unlock
        public int have, need;  // the item's progress after the box
    }

    /// <summary>
    /// The daily loop that brings players back: a 7-day login streak (with one forgiving freeze), a daily
    /// challenge with 3 attempts (extra ones for a rewarded ad), three daily quests, and Beat Boxes whose
    /// random rewards (common to legendary, with a pity guarantee) push locked cosmetics and songs toward
    /// their unlock. Boxes are only ever earned by playing; they are never sold. Everything resets at
    /// local midnight and is saved in PlayerPrefs ("db.daily.*").
    /// </summary>
    public static class Daily
    {
        private const string K = "db.daily.";
        private static readonly DateTime Epoch = new DateTime(2026, 1, 1);

        /// <summary>Test hook: shifts "today" by whole days.</summary>
        public static int DebugDayOffset;

        public static int Today => (int)(DateTime.Now.Date - Epoch).TotalDays + DebugDayOffset;

        /// <summary>Time left until the daily reset (local midnight).</summary>
        public static TimeSpan UntilReset => DateTime.Now.Date.AddDays(1) - DateTime.Now;

        // ================================================================ login streak

        public const int StreakLength = 7;
        private static int LastClaim { get => PlayerPrefs.GetInt(K + "lastClaim", int.MinValue / 2); set => PlayerPrefs.SetInt(K + "lastClaim", value); }
        public static int Streak { get => PlayerPrefs.GetInt(K + "streak", 0); private set => PlayerPrefs.SetInt(K + "streak", value); }
        /// <summary>A freeze saves the streak over one missed day; one is earned by finishing each 7-day week.</summary>
        public static int Freezes { get => PlayerPrefs.GetInt(K + "freezes", 1); private set => PlayerPrefs.SetInt(K + "freezes", Mathf.Clamp(value, 0, 1)); }

        public static bool CanClaimLogin => LastClaim != Today;

        /// <summary>The streak the player will have after claiming today (what the calendar highlights).</summary>
        public static int StreakIfClaimed
        {
            get
            {
                if (!CanClaimLogin) return Streak;
                int gap = Today - LastClaim;
                if (gap == 1) return Streak + 1;
                if (gap == 2 && Freezes > 0) return Streak + 1;
                return 1;
            }
        }

        /// <summary>Day of the week-long cycle, 0..6, for a streak value.</summary>
        public static int CycleDay(int streak) => (Mathf.Max(1, streak) - 1) % StreakLength;

        /// <summary>Boxes for each day of the cycle: the pull grows through the week to a big day 7.</summary>
        public static BoxRarity[] LoginBoxes(int cycleDay)
        {
            switch (cycleDay)
            {
                case 0: return new[] { BoxRarity.Common };
                case 1: return new[] { BoxRarity.Common, BoxRarity.Common };
                case 2: return new[] { BoxRarity.Rare };
                case 3: return new[] { BoxRarity.Common, BoxRarity.Rare };
                case 4: return new[] { BoxRarity.Rare, BoxRarity.Rare };
                case 5: return new[] { BoxRarity.Common, BoxRarity.Common, BoxRarity.Rare };
                default: return new[] { BoxRarity.Epic, BoxRarity.Rare, BoxRarity.Rare };
            }
        }

        /// <summary>Claims today's login reward; returns the boxes added and whether a freeze saved the streak.</summary>
        public static BoxRarity[] ClaimLogin(out bool usedFreeze)
        {
            usedFreeze = false;
            if (!CanClaimLogin) return new BoxRarity[0];
            int gap = Today - LastClaim;
            if (gap == 1) Streak++;
            else if (gap == 2 && Freezes > 0)
            {
                Freezes--;
                Streak++;
                usedFreeze = true;
            }
            else Streak = 1;
            LastClaim = Today;
            if (CycleDay(Streak) == StreakLength - 1) Freezes++; // a full week earns the freeze back
            BoxRarity[] boxes = LoginBoxes(CycleDay(Streak));
            foreach (BoxRarity b in boxes) AddBox(b);
            PlayerPrefs.Save();
            return boxes;
        }

        // ================================================================ daily challenge

        public const int FreeAttempts = 3, MaxAdAttempts = 2;

        public struct Challenge
        {
            public SongData song;
            public Difficulty difficulty;
            /// <summary>Accuracy needed (0..1) on a clear.</summary>
            public float goal;
        }

        /// <summary>Today's challenge: the same for everyone on a day; it can be a locked song (a free taste).</summary>
        public static Challenge TodaysChallenge(SongLibrary library)
        {
            var songs = new List<SongData>();
            if (library != null) foreach (SongData s in library.songs) if (s != null && s.clip != null && s.IsAnalyzed) songs.Add(s);
            int day = Today;
            uint h = (uint)(day * 2654435761u);
            var c = new Challenge();
            if (songs.Count == 0) return c;
            c.song = songs[(int)(h % (uint)songs.Count)];
            c.difficulty = (Difficulty)new[] { 1, 2, 1, 0, 1, 2, 1 }[((day % 7) + 7) % 7];
            c.goal = c.difficulty == Difficulty.Easy ? 0.95f : c.difficulty == Difficulty.Normal ? 0.9f : 0.85f;
            return c;
        }

        private static void Roll()
        {
            if (PlayerPrefs.GetInt(K + "day", int.MinValue) == Today) return;
            PlayerPrefs.SetInt(K + "day", Today);
            PlayerPrefs.SetInt(K + "attemptsUsed", 0);
            PlayerPrefs.SetInt(K + "adAttempts", 0);
            PlayerPrefs.SetInt(K + "challengeDone", 0);
            PlayerPrefs.SetFloat(K + "challengeBest", 0f);
            for (int i = 0; i < 3; i++)
            {
                PlayerPrefs.SetInt(K + "q" + i, 0);
                PlayerPrefs.SetInt(K + "qc" + i, 0);
            }
            PlayerPrefs.SetInt(K + "allQuests", 0);
            PlayerPrefs.Save();
        }

        public static int AttemptsLeft
        {
            get
            {
                Roll();
                return FreeAttempts + PlayerPrefs.GetInt(K + "adAttempts", 0) - PlayerPrefs.GetInt(K + "attemptsUsed", 0);
            }
        }

        public static bool CanBuyAttempt => AdAttempts < MaxAdAttempts;
        /// <summary>Extra attempts earned with rewarded ads today.</summary>
        public static int AdAttempts { get { Roll(); return PlayerPrefs.GetInt(K + "adAttempts", 0); } }
        public static bool ChallengeDone { get { Roll(); return PlayerPrefs.GetInt(K + "challengeDone", 0) == 1; } }
        public static float ChallengeBest { get { Roll(); return PlayerPrefs.GetFloat(K + "challengeBest", 0f); } }

        public static bool TryUseAttempt()
        {
            if (AttemptsLeft <= 0) return false;
            PlayerPrefs.SetInt(K + "attemptsUsed", PlayerPrefs.GetInt(K + "attemptsUsed", 0) + 1);
            PlayerPrefs.Save();
            return true;
        }

        public static void AddAdAttempt()
        {
            Roll();
            PlayerPrefs.SetInt(K + "adAttempts", Mathf.Min(MaxAdAttempts, PlayerPrefs.GetInt(K + "adAttempts", 0) + 1));
            PlayerPrefs.Save();
        }

        // ================================================================ quests

        public enum QuestKind { ClearSongs, Perfects, Combo, PlaySongs, ClearHard, Score, FullCombo, Challenge }

        public struct Quest
        {
            public QuestKind kind;
            public int target;
            public int progress;
            public bool claimed;
            public BoxRarity reward;
            public bool Done => progress >= target;
        }

        private static readonly QuestKind[] Pool =
        {
            QuestKind.ClearSongs, QuestKind.Perfects, QuestKind.Combo, QuestKind.PlaySongs,
            QuestKind.ClearHard, QuestKind.Score, QuestKind.FullCombo, QuestKind.Challenge
        };

        /// <summary>Today's three quests: an easy one, a medium one, a hard one (the hard one pays a rare box).</summary>
        public static Quest[] Quests()
        {
            Roll();
            int day = Today;
            var picked = new List<QuestKind>();
            uint h = (uint)(day * 2246822519u + 374761393u);
            while (picked.Count < 3)
            {
                h = h * 1664525u + 1013904223u;
                QuestKind k = Pool[(h >> 8) % (uint)Pool.Length];
                if (!picked.Contains(k)) picked.Add(k);
            }
            var q = new Quest[3];
            for (int i = 0; i < 3; i++)
            {
                q[i] = new Quest
                {
                    kind = picked[i],
                    target = Target(picked[i], i),
                    progress = PlayerPrefs.GetInt(K + "q" + i, 0),
                    claimed = PlayerPrefs.GetInt(K + "qc" + i, 0) == 1,
                    reward = i == 2 ? BoxRarity.Rare : BoxRarity.Common
                };
            }
            return q;
        }

        private static int Target(QuestKind k, int tier)
        {
            switch (k)
            {
                case QuestKind.ClearSongs: return 2 + tier;
                case QuestKind.Perfects: return 100 + 75 * tier;
                case QuestKind.Combo: return 40 + 30 * tier;
                case QuestKind.PlaySongs: return 3 + 2 * tier;
                case QuestKind.Score: return 100000 + 75000 * tier;
                default: return 1;
            }
        }

        public static string Describe(Quest q)
        {
            switch (q.kind)
            {
                case QuestKind.ClearSongs: return "Clear " + q.target + " songs";
                case QuestKind.Perfects: return "Hit " + q.target + " Perfects";
                case QuestKind.Combo: return "Reach a " + q.target + " combo";
                case QuestKind.PlaySongs: return "Play " + q.target + " songs";
                case QuestKind.ClearHard: return "Clear a song on Hard";
                case QuestKind.Score: return "Score " + q.target.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " points";
                case QuestKind.FullCombo: return "Full combo a song";
                default: return "Play the daily challenge";
            }
        }

        /// <summary>Claims a finished quest's box; finishing all three adds an epic bonus box.</summary>
        public static bool ClaimQuest(int i, out bool allDone)
        {
            allDone = false;
            Quest[] q = Quests();
            if (i < 0 || i >= q.Length || !q[i].Done || q[i].claimed) return false;
            PlayerPrefs.SetInt(K + "qc" + i, 1);
            AddBox(q[i].reward);
            bool all = true;
            for (int j = 0; j < q.Length; j++) all &= j == i || q[j].claimed;
            if (all && PlayerPrefs.GetInt(K + "allQuests", 0) == 0)
            {
                PlayerPrefs.SetInt(K + "allQuests", 1);
                AddBox(BoxRarity.Epic);
                allDone = true;
            }
            PlayerPrefs.Save();
            return true;
        }

        // ================================================================ runs

        /// <summary>Clears that fill the box meter; every <see cref="ClearsPerBox"/> clears drop a box.</summary>
        public const int ClearsPerBox = 4;
        public static int BoxMeter => PlayerPrefs.GetInt(K + "meter", 0);

        /// <summary>Feeds a finished real run into the quests, the challenge and the box meter; returns what it earned.</summary>
        public static List<string> OnRunFinished(ScoreState s, Difficulty d, bool cleared, bool challengeRun)
        {
            Roll();
            var news = new List<string>();
            Quest[] q = Quests();
            for (int i = 0; i < q.Length; i++)
            {
                if (q[i].claimed) continue;
                int add = 0;
                switch (q[i].kind)
                {
                    case QuestKind.ClearSongs: add = cleared ? 1 : 0; break;
                    case QuestKind.Perfects: add = s.Perfect; break;
                    case QuestKind.Combo: add = Mathf.Max(0, s.MaxCombo - q[i].progress); break;
                    case QuestKind.PlaySongs: add = 1; break;
                    case QuestKind.ClearHard: add = cleared && d == Difficulty.Hard ? 1 : 0; break;
                    case QuestKind.Score: add = s.Score; break;
                    case QuestKind.FullCombo: add = cleared && s.FullCombo ? 1 : 0; break;
                    case QuestKind.Challenge: add = challengeRun ? 1 : 0; break;
                }
                if (add <= 0) continue;
                bool was = q[i].Done;
                int p = Mathf.Min(q[i].target, q[i].progress + add);
                PlayerPrefs.SetInt(K + "q" + i, p);
                if (!was && p >= q[i].target) news.Add("QUEST DONE · " + Describe(q[i]).ToUpperInvariant());
            }

            if (challengeRun && cleared)
            {
                if (s.Accuracy > ChallengeBest) PlayerPrefs.SetFloat(K + "challengeBest", s.Accuracy);
                float goal = PlayerPrefs.GetFloat(K + "goal", 0.9f);
                if (!ChallengeDone && s.Accuracy >= goal)
                {
                    PlayerPrefs.SetInt(K + "challengeDone", 1);
                    AddBox(BoxRarity.Epic);
                    news.Insert(0, "DAILY CHALLENGE COMPLETE · EPIC BOX");
                }
            }

            if (cleared)
            {
                int m = BoxMeter + 1;
                if (m >= ClearsPerBox)
                {
                    m = 0;
                    AddBox(BoxRarity.Common);
                    news.Add("BEAT BOX EARNED");
                }
                PlayerPrefs.SetInt(K + "meter", m);
            }
            PlayerPrefs.Save();
            return news;
        }

        /// <summary>Remembers the goal of the challenge run being started (so results judge it the same way).</summary>
        public static void BeginChallenge(Challenge c) => PlayerPrefs.SetFloat(K + "goal", c.goal);

        // ================================================================ beat boxes

        /// <summary>Unopened boxes, each stored as its guaranteed minimum rarity.</summary>
        public static List<BoxRarity> Boxes()
        {
            var list = new List<BoxRarity>();
            string s = PlayerPrefs.GetString(K + "boxes", string.Empty);
            foreach (string part in s.Split(','))
            {
                if (int.TryParse(part, out int v)) list.Add((BoxRarity)Mathf.Clamp(v, 0, 3));
            }
            return list;
        }

        private static void SaveBoxes(List<BoxRarity> list)
        {
            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++) parts[i] = ((int)list[i]).ToString();
            PlayerPrefs.SetString(K + "boxes", string.Join(",", parts));
        }

        public static void AddBox(BoxRarity min)
        {
            var list = Boxes();
            list.Add(min);
            SaveBoxes(list);
        }

        /// <summary>Boxes opened since the last legendary; at <see cref="Pity"/> the next one is legendary.</summary>
        public const int Pity = 25;
        private static int SinceLegendary { get => PlayerPrefs.GetInt(K + "pity", 0); set => PlayerPrefs.SetInt(K + "pity", value); }
        public static int BoxesToGuaranteedLegendary => Mathf.Max(1, Pity - SinceLegendary);

        /// <summary>Opens the best box first: rolls its rarity (never below its minimum) and applies the reward.</summary>
        public static bool OpenBox(out BoxReward reward)
        {
            reward = default;
            var list = Boxes();
            if (list.Count == 0) return false;
            int best = 0;
            for (int i = 1; i < list.Count; i++) if (list[i] > list[best]) best = i;
            BoxRarity min = list[best];
            list.RemoveAt(best);
            SaveBoxes(list);

            float r = UnityEngine.Random.value;
            BoxRarity rarity = r < 0.03f ? BoxRarity.Legendary : r < 0.15f ? BoxRarity.Epic : r < 0.45f ? BoxRarity.Rare : BoxRarity.Common;
            if (rarity < min) rarity = min;
            if (SinceLegendary + 1 >= Pity) rarity = BoxRarity.Legendary;
            SinceLegendary = rarity == BoxRarity.Legendary ? 0 : SinceLegendary + 1;

            reward.rarity = rarity;
            reward.xp = new[] { 100, 250, 600, 1500 }[(int)rarity];
            GameSettings.AddBonusXp(reward.xp);
            int steps = new[] { 1, 2, 4, 999 }[(int)rarity];
            GameSettings.GrantRandomUnlockProgress(steps, ref reward);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Anything waiting for the player today (the menu's red dot).</summary>
        public static bool HasSomethingWaiting
        {
            get
            {
                if (CanClaimLogin || Boxes().Count > 0) return true;
                if (!ChallengeDone && AttemptsLeft > 0) return true;
                foreach (Quest q in Quests()) if (q.Done && !q.claimed) return true;
                return false;
            }
        }
    }
}
