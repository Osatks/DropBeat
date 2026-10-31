using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// The DAILY sheet (login streak, daily challenge, quests, Beat Boxes), the menu button's red dot, and
    /// the Beat Box opening: the box shakes faster and faster while its glow flickers through every rarity,
    /// then bursts open on the one it rolled.
    /// </summary>
    public sealed partial class GameUI
    {
        private static readonly Color[] RarityColor =
        {
            new Color(0.55f, 0.85f, 1f, 1f),  // common
            new Color(0.3f, 0.55f, 1f, 1f),   // rare
            new Color(0.85f, 0.35f, 1f, 1f),  // epic
            new Color(1f, 0.8f, 0.25f, 1f)    // legendary
        };
        private static readonly string[] RarityName = { "common", "rare", "epic", "legendary" };

        private RectTransform _daily, _dailyCard, _dailyDot;
        private float _dailyOpened;
        private float _dailyTick;

        // streak
        private readonly List<Image> _streakCells = new List<Image>();
        private readonly List<Text> _streakTexts = new List<Text>();
        private Text _streakTitle, _streakNote, _claimText;
        private Button _claimBtn;

        // challenge
        private Text _chSong, _chInfo, _chBest, _chBtnText;
        private Button _chBtn;
        private readonly List<Image> _chPips = new List<Image>();

        // quests
        private readonly List<Text> _qText = new List<Text>();
        private readonly List<Text> _qProg = new List<Text>();
        private readonly List<Image> _qFill = new List<Image>();
        private readonly List<Button> _qBtn = new List<Button>();
        private readonly List<Text> _qBtnText = new List<Text>();

        // boxes
        private Text _boxCount, _boxMeter, _boxPity;
        private Image _boxMeterFill;
        private Button _openBtn;

        // opening overlay
        private RectTransform _opening, _openBox, _openLid, _openRays;
        private Image _openBoxImg, _openHalo;
        private Text _openRarity, _openItem, _openXp, _openHint;
        private float _openT0 = -100f;
        private BoxReward _openReward;
        private int _openTicks;
        private bool _openRevealed;

        private void BuildDaily()
        {
            _daily = UIFactory.Stretch(_menu, "Daily");
            UIFactory.Image(_daily, new Color(0.01f, 0f, 0.04f, 0.95f), null, true);
            _daily.offsetMin = new Vector2(0, -300);
            _daily.offsetMax = new Vector2(0, 300);

            _dailyCard = UIFactory.Rect(_daily, "Card", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _dailyCard.offsetMin = new Vector2(-500, 330);
            _dailyCard.offsetMax = new Vector2(500, -330);
            UIFactory.Card(_dailyCard).raycastTarget = true;

            var header = UIFactory.Rect(_dailyCard, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(-64, 84));
            var title = UIFactory.Label(UIFactory.Rect(header, "Title", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(12, 0), Vector2.zero),
                UIFactory.Spaced("daily"), 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Gold, UIFactory.Pink, true);
            var done = UIFactory.Button(header, "Done", UIFactory.Spaced("done"), 24, () => ShowDaily(false), out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)done.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(190, 76), new Vector2(1f, 0.5f));

            var view = UIFactory.Rect(_dailyCard, "View", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.offsetMin = new Vector2(30, 30);
            view.offsetMax = new Vector2(-30, -130);
            UIFactory.Image(view, new Color(0f, 0f, 0f, 0.001f), null, true);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = view;
            var content = UIFactory.Rect(view, "Content", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            UIFactory.VStack(content, 22, 6);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            BuildStreakCard(content);
            BuildChallengeCard(content);
            BuildQuests(content);
            BuildBoxCard(content);
            BuildOpening();
            _daily.gameObject.SetActive(false);
        }

        private static RectTransform Panel(RectTransform parent, string name, float height)
        {
            var rt = UIFactory.Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Layout(rt, height);
            UIFactory.Image(rt, UIFactory.GlassTint, UIFactory.Glass);
            return rt;
        }

        private static Text Eyebrow(RectTransform parent, string text, float y)
        {
            return UIFactory.Label(UIFactory.Rect(parent, "Eyebrow", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(-56, 34)),
                UIFactory.Spaced(text), 20, UIFactory.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        private void BuildStreakCard(RectTransform content)
        {
            var card = Panel(content, "Streak", 330);
            _streakTitle = Eyebrow(card, "login streak", -18);
            var row = UIFactory.Rect(card, "Days", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(-56, 130));
            UIFactory.HStack(row, 10);
            for (int i = 0; i < Daily.StreakLength; i++)
            {
                var cell = UIFactory.Rect(row, "Day" + i, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                _streakCells.Add(UIFactory.Image(cell, new Color(1f, 1f, 1f, 0.08f), UIFactory.Rounded));
                var t = UIFactory.Label(cell, "Text", string.Empty, 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.supportRichText = true;
                _streakTexts.Add(t);
            }
            _claimBtn = UIFactory.Button(card, "Claim", UIFactory.Spaced("claim"), 28, ClaimLogin, out _claimText, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)_claimBtn.transform, new Vector2(0.5f, 0f), new Vector2(0, 64), new Vector2(560, 84));
            _streakNote = UIFactory.Label(UIFactory.Rect(card, "Note", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(-56, 30)),
                string.Empty, 18, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void BuildChallengeCard(RectTransform content)
        {
            var card = Panel(content, "Challenge", 300);
            Eyebrow(card, "daily challenge", -18);
            _chSong = UIFactory.Label(UIFactory.Rect(card, "Song", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -54), new Vector2(-56, 60)),
                string.Empty, 46, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _chSong.resizeTextForBestFit = true;
            _chSong.resizeTextMinSize = 26;
            _chSong.resizeTextMaxSize = 46;
            _chInfo = UIFactory.Label(UIFactory.Rect(card, "Info", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -116), new Vector2(-56, 32)),
                string.Empty, 22, UIFactory.TextColor, TextAnchor.MiddleLeft);
            _chBest = UIFactory.Label(UIFactory.Rect(card, "Best", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(-56, 30)),
                string.Empty, 19, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            var pips = UIFactory.Rect(card, "Pips", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -24), new Vector2(5 * 34, 26));
            for (int i = 0; i < Daily.FreeAttempts + Daily.MaxAdAttempts; i++)
            {
                var p = UIFactory.Rect(pips, "Pip" + i, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 34, 0), new Vector2(26, 26));
                _chPips.Add(UIFactory.Image(p, Color.white, UIFactory.Circle));
            }
            _chBtn = UIFactory.Button(card, "Play", UIFactory.Spaced("play"), 28, ChallengeButton, out _chBtnText, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)_chBtn.transform, new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(560, 84));
        }

        private void BuildQuests(RectTransform content)
        {
            var card = Panel(content, "Quests", 470);
            Eyebrow(card, "daily quests  ·  finish all 3 for an epic box", -18);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var row = UIFactory.Rect(card, "Quest" + i, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -66 - i * 132), new Vector2(-56, 118));
                UIFactory.Image(row, new Color(0.16f, 0.11f, 0.32f, 0.92f), UIFactory.Rounded);
                _qText.Add(UIFactory.Label(UIFactory.Rect(row, "Text", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(24, -14), new Vector2(-260, 40)),
                    string.Empty, 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold));
                var bar = UIFactory.Rect(row, "Bar", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(24, 26), new Vector2(-290, 16));
                UIFactory.Image(bar, new Color(1f, 1f, 1f, 0.12f), UIFactory.Pill);
                var fill = UIFactory.Image(UIFactory.Stretch(bar, "Fill"), UIFactory.Cyan, UIFactory.Pill);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                _qFill.Add(fill);
                _qProg.Add(UIFactory.Label(UIFactory.Rect(row, "Prog", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(24, 46), new Vector2(-290, 28)),
                    string.Empty, 18, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold));
                var b = UIFactory.Button(row, "Claim", UIFactory.Spaced("claim"), 22, () => ClaimQuest(idx), out Text bt, UIFactory.ButtonStyle.Primary);
                Place((RectTransform)b.transform, new Vector2(1f, 0.5f), new Vector2(-18, 0), new Vector2(210, 76), new Vector2(1f, 0.5f));
                _qBtn.Add(b);
                _qBtnText.Add(bt);
            }
        }

        private void BuildBoxCard(RectTransform content)
        {
            var card = Panel(content, "Boxes", 340);
            Eyebrow(card, "beat boxes", -18);
            var icon = UIFactory.At(card, "Icon", new Vector2(0f, 1f), new Vector2(120, -130), new Vector2(150, 150));
            DrawBox(icon, RarityColor[2]);
            _boxCount = UIFactory.Label(UIFactory.Rect(card, "Count", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(230, -60), new Vector2(-260, 56)),
                string.Empty, 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _boxMeter = UIFactory.Label(UIFactory.Rect(card, "Meter", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(230, -116), new Vector2(-260, 30)),
                string.Empty, 19, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            var bar = UIFactory.Rect(card, "MeterBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(230, -152), new Vector2(-290, 14));
            UIFactory.Image(bar, new Color(1f, 1f, 1f, 0.12f), UIFactory.Pill);
            _boxMeterFill = UIFactory.Image(UIFactory.Stretch(bar, "Fill"), UIFactory.Gold, UIFactory.Pill);
            _boxMeterFill.type = Image.Type.Filled;
            _boxMeterFill.fillMethod = Image.FillMethod.Horizontal;
            _boxPity = UIFactory.Label(UIFactory.Rect(card, "Pity", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(230, -178), new Vector2(-260, 28)),
                string.Empty, 17, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _openBtn = UIFactory.Button(card, "Open", UIFactory.Spaced("open"), 28, OpenNextBox, out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)_openBtn.transform, new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(560, 84));
        }

        /// <summary>A gift-box drawing: body, lid and ribbon in the rarity colour.</summary>
        private static void DrawBox(RectTransform area, Color c)
        {
            var body = UIFactory.Rect(area, "Body", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(118, 92));
            UIFactory.Gradient(UIFactory.Image(body, Color.white, UIFactory.Rounded), c, Color.Lerp(c, Color.black, 0.45f), false);
            var lid = UIFactory.Rect(area, "Lid", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 96), new Vector2(136, 34));
            UIFactory.Gradient(UIFactory.Image(lid, Color.white, UIFactory.Rounded), Color.Lerp(c, Color.white, 0.35f), c, false);
            var ribbon = UIFactory.Rect(area, "Ribbon", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(18, 124));
            UIFactory.Image(ribbon, new Color(1f, 1f, 1f, 0.85f), UIFactory.Rounded);
        }

        private void BuildOpening()
        {
            _opening = UIFactory.Stretch(_daily, "Opening");
            UIFactory.Image(_opening, new Color(0.01f, 0f, 0.05f, 0.94f), null, true);
            var tap = _opening.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(OpeningTapped);

            _openRays = UIFactory.At(_opening, "Rays", new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 900));
            for (int i = 0; i < 12; i++)
            {
                var ray = UIFactory.Rect(_openRays, "Ray" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(46, 450));
                ray.localRotation = Quaternion.Euler(0f, 0f, i * 30f);
                UIFactory.Gradient(UIFactory.Image(ray, Color.white, UIFactory.Pill), new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0f), false);
            }
            _openHalo = UIFactory.Image(UIFactory.At(_opening, "Halo", new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(520, 520)), Color.white, UIFactory.GlowSprite);

            _openBox = UIFactory.At(_opening, "Box", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(300, 240));
            _openBoxImg = UIFactory.Image(UIFactory.Rect(_openBox, "Body", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(240, 180)), Color.white, UIFactory.Rounded);
            _openLid = UIFactory.Rect(_openBox, "Lid", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 176), new Vector2(276, 62));
            UIFactory.Image(_openLid, new Color(1f, 1f, 1f, 0.92f), UIFactory.Rounded);
            UIFactory.Image(UIFactory.Rect(_openBox, "Ribbon", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(34, 238)), new Color(1f, 1f, 1f, 0.9f), UIFactory.Rounded);

            _openRarity = UIFactory.Label(UIFactory.At(_opening, "Rarity", new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(900, 110)), string.Empty, 86, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _openItem = UIFactory.Label(UIFactory.At(_opening, "Item", new Vector2(0.5f, 0.5f), new Vector2(0, -240), new Vector2(900, 60)), string.Empty, 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _openXp = UIFactory.Label(UIFactory.At(_opening, "Xp", new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(900, 44)), string.Empty, 28, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _openHint = UIFactory.Label(UIFactory.At(_opening, "Hint", new Vector2(0.5f, 0.5f), new Vector2(0, -400), new Vector2(900, 40)), string.Empty, 22, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            _opening.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- actions

        private void ShowDaily(bool on)
        {
            if (_daily == null || _daily.gameObject.activeSelf == on) return;
            _daily.gameObject.SetActive(on);
            _sheet.gameObject.SetActive(!on);
            _dailyOpened = Time.unscaledTime;
            if (on)
            {
                _opening.gameObject.SetActive(false);
                RefreshDaily();
            }
            RefreshDailyDot();
        }

        private void ClaimLogin()
        {
            if (!Daily.CanClaimLogin) return;
            BoxRarity[] got = Daily.ClaimLogin(out bool froze);
            SfxPlayer sfx = _game.Sfx;
            if (sfx != null) sfx.PlayNow(got.Length >= 3 ? sfx.Fanfare : sfx.PowerUp, 0.6f);
            ShowToast((froze ? "STREAK SAVED BY YOUR FREEZE · " : string.Empty) + "+" + got.Length + (got.Length == 1 ? " BEAT BOX" : " BEAT BOXES"));
            RefreshDaily();
            RefreshDailyDot();
        }

        private void ChallengeButton()
        {
            if (Daily.ChallengeDone) return;
            if (Daily.AttemptsLeft > 0)
            {
                ShowDaily(false);
                _game.PlayDailyChallenge();
            }
            else if (Daily.CanBuyAttempt) _game.BuyChallengeAttempt();
        }

        private void ClaimQuest(int i)
        {
            if (!Daily.ClaimQuest(i, out bool all)) return;
            SfxPlayer sfx = _game.Sfx;
            if (sfx != null) sfx.PlayNow(all ? sfx.Fanfare : sfx.PowerUp, 0.55f);
            ShowToast(all ? "ALL QUESTS DONE · +1 EPIC BOX" : "+1 BEAT BOX");
            RefreshDaily();
            RefreshDailyDot();
        }

        private void OpenNextBox()
        {
            if (!Daily.OpenBox(out _openReward)) return;
            _openT0 = Time.unscaledTime;
            _openTicks = 0;
            _openRevealed = false;
            _opening.gameObject.SetActive(true);
            _openRarity.text = _openItem.text = _openXp.text = _openHint.text = string.Empty;
            RefreshDaily();
        }

        private void OpeningTapped()
        {
            float age = Time.unscaledTime - _openT0;
            if (!_openRevealed)
            {
                _openT0 -= Mathf.Max(0f, ShakeTime - age); // tap skips straight to the reveal
                return;
            }
            if (age < ShakeTime + 0.5f) return;
            if (Daily.Boxes().Count > 0) OpenNextBox();
            else
            {
                _opening.gameObject.SetActive(false);
                RefreshDaily();
                RefreshDailyDot();
            }
        }

        /// <summary>Results: what this run earned in the daily loop (quests, challenge, boxes).</summary>
        public void ShowDailyNews(List<string> news)
        {
            if (news == null || news.Count == 0) return;
            ShowToast(news.Count == 1 ? news[0] : news[0] + "  ·  +" + (news.Count - 1) + " MORE IN DAILY");
        }

        // ---------------------------------------------------------------- refresh / animate

        private void RefreshDailyDot()
        {
            if (_dailyDot != null) _dailyDot.gameObject.SetActive(Daily.HasSomethingWaiting);
        }

        private void RefreshDaily()
        {
            if (_daily == null || !_daily.gameObject.activeSelf) return;

            // Streak calendar: claimed days solid, today glowing, the week's big day 7 in gold.
            bool canClaim = Daily.CanClaimLogin;
            int streak = canClaim ? Daily.StreakIfClaimed : Daily.Streak;
            int today = Daily.CycleDay(streak);
            _streakTitle.text = UIFactory.Spaced("login streak") + "    " + (Daily.Streak == 0 ? "START TODAY" : Daily.Streak + (Daily.Streak == 1 ? " DAY" : " DAYS"));
            for (int i = 0; i < _streakCells.Count; i++)
            {
                BoxRarity[] boxes = Daily.LoginBoxes(i);
                BoxRarity top = boxes[0];
                foreach (BoxRarity b in boxes) if (b > top) top = b;
                bool claimed = i < today || (i == today && !canClaim);
                bool isToday = i == today && canClaim;
                Color rc = RarityColor[(int)top];
                _streakCells[i].color = claimed ? new Color(rc.r, rc.g, rc.b, 0.35f) : isToday ? rc : new Color(1f, 1f, 1f, 0.08f);
                _streakTexts[i].text = "DAY " + (i + 1) + "\n<size=24>x" + boxes.Length + "</size>\n<size=13>" + (claimed ? "DONE" : RarityName[(int)top].ToUpperInvariant()) + "</size>";
                _streakTexts[i].color = isToday ? new Color(0.08f, 0.04f, 0.2f, 1f) : claimed ? UIFactory.Dim : Color.white;
            }
            _claimBtn.interactable = canClaim;
            _claimText.text = canClaim ? UIFactory.Spaced("claim day " + (today + 1)) : UIFactory.Spaced("claimed");
            UpdateResetNote();

            // Challenge.
            Daily.Challenge c = Daily.TodaysChallenge(_game.Library);
            if (c.song != null)
            {
                bool locked = !GameSettings.IsUnlocked(c.song);
                _chSong.text = c.song.title.ToUpperInvariant() + (locked ? "  <size=22><color=#" + UIFactory.Hex(UIFactory.Pink) + ">FREE TRY</color></size>" : string.Empty);
                _chSong.supportRichText = true;
                _chInfo.text = c.difficulty.ToString().ToUpperInvariant() + "  ·  clear it with " + Mathf.RoundToInt(c.goal * 100f) + "% accuracy for an EPIC box";
            }
            float best = Daily.ChallengeBest;
            _chBest.text = best > 0f ? "BEST TODAY  " + (best * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" : "3 attempts a day · watch an ad for more";
            int left = Daily.AttemptsLeft;
            for (int i = 0; i < _chPips.Count; i++)
            {
                _chPips[i].gameObject.SetActive(i < Daily.FreeAttempts + Daily.AdAttempts);
                _chPips[i].color = i < left ? UIFactory.Cyan : new Color(1f, 1f, 1f, 0.15f);
            }
            if (Daily.ChallengeDone)
            {
                _chBtnText.text = UIFactory.Spaced("complete · new one at midnight");
                _chBtnText.fontSize = 22;
                _chBtn.interactable = false;
            }
            else if (left > 0)
            {
                _chBtnText.text = UIFactory.Spaced("play") + "   " + left + (left == 1 ? " try left" : " tries left");
                _chBtnText.fontSize = 26;
                _chBtn.interactable = true;
            }
            else
            {
                _chBtnText.text = Daily.CanBuyAttempt ? UIFactory.Spaced("watch ad: +1 try") : UIFactory.Spaced("out of tries · back at midnight");
                _chBtnText.fontSize = 22;
                _chBtn.interactable = Daily.CanBuyAttempt;
            }

            // Quests.
            Daily.Quest[] q = Daily.Quests();
            for (int i = 0; i < q.Length && i < _qText.Count; i++)
            {
                _qText[i].text = Daily.Describe(q[i]);
                _qFill[i].fillAmount = Mathf.Clamp01(q[i].progress / (float)q[i].target);
                _qFill[i].color = q[i].Done ? UIFactory.Green : UIFactory.Cyan;
                _qProg[i].text = q[i].progress.ToString("N0", CultureInfo.InvariantCulture) + " / " + q[i].target.ToString("N0", CultureInfo.InvariantCulture) +
                                 "   ·   " + RarityName[(int)q[i].reward].ToUpperInvariant() + " BOX";
                bool claimable = q[i].Done && !q[i].claimed;
                _qBtn[i].interactable = claimable;
                UIFactory.TintButton(_qBtn[i], claimable ? Color.white : new Color(1f, 1f, 1f, 0.22f));
                _qBtnText[i].text = UIFactory.Spaced(q[i].claimed ? "claimed" : q[i].Done ? "claim" : "to do");
            }

            // Boxes.
            int n = Daily.Boxes().Count;
            _boxCount.text = n + (n == 1 ? " BEAT BOX" : " BEAT BOXES");
            int meter = Daily.BoxMeter;
            _boxMeter.text = "Clear " + (Daily.ClearsPerBox - meter) + " more " + (Daily.ClearsPerBox - meter == 1 ? "song" : "songs") + " for the next box";
            _boxMeterFill.fillAmount = meter / (float)Daily.ClearsPerBox;
            _boxPity.text = "Legendary guaranteed within " + Daily.BoxesToGuaranteedLegendary + " boxes";
            _openBtn.interactable = n > 0;
            UIFactory.TintButton(_openBtn, n > 0 ? Color.white : new Color(1f, 1f, 1f, 0.22f));
        }

        private void UpdateResetNote()
        {
            System.TimeSpan t = Daily.UntilReset;
            string clock = ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            _streakNote.text = (Daily.CanClaimLogin ? "Claim today to keep your streak" : "Next reward in " + clock) +
                               (Daily.Freezes > 0 ? "   ·   streak freeze ready" : string.Empty);
        }

        private const float ShakeTime = 1.3f;

        private void AnimateDaily(float time)
        {
            if (_daily == null || !_daily.gameObject.activeSelf) return;
            float a = Mathf.Clamp01((time - _dailyOpened) / 0.25f);
            float e = 1f - (1f - a) * (1f - a) * (1f - a);
            _dailyCard.anchoredPosition = new Vector2(0, Mathf.Lerp(-1600f, 0f, e));
            if (time - _dailyTick > 1f)
            {
                _dailyTick = time;
                UpdateResetNote();
            }
            if (_opening.gameObject.activeSelf) AnimateOpening(time - _openT0);
        }

        /// <summary>
        /// Box opening: it shakes harder and faster while its glow flickers through every rarity (each tick a
        /// rising note), then the lid flies off and it settles on what it rolled.
        /// </summary>
        private void AnimateOpening(float age)
        {
            SfxPlayer sfx = _game.Sfx;
            Color final = RarityColor[(int)_openReward.rarity];
            if (age < ShakeTime)
            {
                float u = age / ShakeTime;
                float shake = Mathf.Lerp(3f, 16f, u * u);
                _openBox.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(age * Mathf.Lerp(18f, 60f, u)) * shake);
                _openBox.localScale = Vector3.one * (1f + 0.08f * u + 0.03f * Mathf.Sin(age * 40f));
                _openLid.anchoredPosition = new Vector2(0, 176);
                int flick = Mathf.FloorToInt(age * Mathf.Lerp(6f, 18f, u)) % 4;
                Color teaser = RarityColor[flick];
                _openBoxImg.color = teaser;
                _openHalo.color = new Color(teaser.r, teaser.g, teaser.b, 0.25f + 0.25f * u);
                _openHalo.rectTransform.localScale = Vector3.one * (0.8f + 0.3f * u);
                _openRays.gameObject.SetActive(false);
                int tick = Mathf.FloorToInt(age / Mathf.Lerp(0.16f, 0.07f, u));
                if (tick > _openTicks && sfx != null)
                {
                    _openTicks = tick;
                    sfx.PlayNow(sfx.ScaleNote(Mathf.Min(10, tick / 2)), 0.25f);
                }
                return;
            }

            float r = age - ShakeTime;
            if (!_openRevealed)
            {
                _openRevealed = true;
                if (sfx != null)
                {
                    switch (_openReward.rarity)
                    {
                        case BoxRarity.Legendary: sfx.PlayNow(sfx.Impact, 0.8f); sfx.PlayNow(sfx.NewBest, 0.8f); break;
                        case BoxRarity.Epic: sfx.PlayNow(sfx.Fanfare, 0.75f); break;
                        case BoxRarity.Rare: sfx.PlayNow(sfx.PowerUp, 0.6f); sfx.PlayNow(sfx.ScaleNote(7), 0.4f); break;
                        default: sfx.PlayNow(sfx.PowerUp, 0.45f); break;
                    }
                }
                _openRarity.text = UIFactory.Spaced(RarityName[(int)_openReward.rarity]);
                _openRarity.color = final;
                if (string.IsNullOrEmpty(_openReward.item)) _openItem.text = "Everything is unlocked!";
                else if (_openReward.unlocked) _openItem.text = _openReward.item + " " + _openReward.itemKind.ToUpperInvariant() + " UNLOCKED!";
                else _openItem.text = "+" + _openReward.progress + " toward " + _openReward.item + " " + _openReward.itemKind.ToUpperInvariant() +
                                      "  (" + _openReward.have + "/" + _openReward.need + ")";
                _openXp.text = "+" + _openReward.xp + " XP";
                int more = Daily.Boxes().Count;
                _openHint.text = more > 0 ? UIFactory.Spaced("tap to open the next box") + "  (" + more + ")" : UIFactory.Spaced("tap to continue");
            }
            float pop = Mathf.Clamp01(r / 0.25f);
            _openBox.localRotation = Quaternion.identity;
            _openBox.localScale = Vector3.one * (pop < 0.5f ? Mathf.Lerp(1.12f, 1.3f, pop * 2f) : Mathf.Lerp(1.3f, 1f, (pop - 0.5f) * 2f));
            _openLid.anchoredPosition = new Vector2(Mathf.Lerp(0f, 140f, pop), 176 + Mathf.Lerp(0f, 260f, pop));
            _openLid.localRotation = Quaternion.Euler(0f, 0f, -40f * pop);
            _openBoxImg.color = final;
            _openHalo.color = new Color(final.r, final.g, final.b, 0.75f);
            _openHalo.rectTransform.localScale = Vector3.one * (1.1f + 0.08f * Mathf.Sin(r * 4f) + 0.6f * (1f - pop));
            bool bigRays = _openReward.rarity >= BoxRarity.Epic;
            _openRays.gameObject.SetActive(_openReward.rarity >= BoxRarity.Rare);
            _openRays.localRotation = Quaternion.Euler(0f, 0f, -r * (bigRays ? 40f : 20f));
            _openRays.localScale = Vector3.one * (bigRays ? 1.15f : 0.85f) * pop;
            float tp = Mathf.Clamp01((r - 0.1f) / 0.25f);
            _openRarity.rectTransform.localScale = Vector3.one * (tp < 0.6f ? Mathf.Lerp(2f, 0.95f, tp / 0.6f) : Mathf.Lerp(0.95f, 1f, (tp - 0.6f) / 0.4f));
        }
    }
}
