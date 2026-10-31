using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// The player profile (level, lifetime stats, clear rewards) and the results screen's reward card that
    /// announces a newly won ball or trail.
    /// </summary>
    public sealed partial class GameUI
    {
        // ---------------------------------------------------------------- profile sheet

        private RectTransform _profile, _profileCard;
        private float _profileOpened;
        private Text _profLevel, _profTitle, _profXp;
        private Image _profRing;
        private readonly List<Text> _profValues = new List<Text>();
        private Text _profSkinStatus, _profTrailStatus;
        private RawImage _profSkinBall;

        private static readonly string[] ProfileLabels =
        {
            "songs played", "clears", "full combos", "all perfect",
            "best combo", "accuracy", "perfect hits", "total score",
            "stars", "play time", "bosses beaten", "songs unlocked"
        };

        private void BuildProfile()
        {
            _profile = UIFactory.Stretch(_menu, "Profile");
            UIFactory.Image(_profile, new Color(0.01f, 0f, 0.04f, 0.95f), null, true);
            _profile.offsetMin = new Vector2(0, -300);
            _profile.offsetMax = new Vector2(0, 300);

            _profileCard = UIFactory.Rect(_profile, "Card", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _profileCard.offsetMin = new Vector2(-500, 330);
            _profileCard.offsetMax = new Vector2(500, -330);
            UIFactory.Card(_profileCard).raycastTarget = true;

            var header = UIFactory.Rect(_profileCard, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(-64, 84));
            var title = UIFactory.Label(UIFactory.Rect(header, "Title", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(12, 0), Vector2.zero),
                UIFactory.Spaced("profile"), 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Cyan, UIFactory.Pink, true);
            var done = UIFactory.Button(header, "Done", UIFactory.Spaced("done"), 24, () => ShowProfile(false), out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)done.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(190, 76), new Vector2(1f, 0.5f));

            // Scrolling content: level, stats grid, rewards.
            var view = UIFactory.Rect(_profileCard, "View", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
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

            // ---- level: a ring that fills with XP, the level number inside, the title beside it.
            var level = UIFactory.Rect(content, "Level", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Layout(level, 250);
            UIFactory.Image(level, UIFactory.GlassTint, UIFactory.Glass);
            var ring = UIFactory.At(level, "Ring", new Vector2(0f, 0.5f), new Vector2(150, 0), new Vector2(190, 190));
            UIFactory.Image(ring, new Color(1f, 1f, 1f, 0.12f), UIFactory.RingOutline);
            _profRing = UIFactory.Image(UIFactory.Stretch(ring, "Fill"), UIFactory.Gold, UIFactory.RingOutline);
            _profRing.type = Image.Type.Filled;
            _profRing.fillMethod = Image.FillMethod.Radial360;
            _profRing.fillOrigin = (int)Image.Origin360.Top;
            UIFactory.Label(UIFactory.Rect(ring, "Lv", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 46), new Vector2(0, 30)),
                UIFactory.Spaced("level"), 18, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            _profLevel = UIFactory.Label(UIFactory.Rect(ring, "Num", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, -8), Vector2.zero),
                "1", 76, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _profTitle = UIFactory.Label(UIFactory.Rect(level, "Title", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(290, 34), new Vector2(-320, 64)),
                "ROOKIE", 46, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(_profTitle, UIFactory.Gold, UIFactory.Pink, true);
            _profXp = UIFactory.Label(UIFactory.Rect(level, "Xp", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(290, -30), new Vector2(-320, 40)),
                "", 24, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);

            // ---- stats: two columns of tiles.
            var grid = UIFactory.Rect(content, "Stats", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(458, 132);
            gl.spacing = new Vector2(18, 18);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;
            gl.childAlignment = TextAnchor.UpperCenter;
            UIFactory.Layout(grid, Mathf.Ceil(ProfileLabels.Length / 2f) * 150f);
            for (int i = 0; i < ProfileLabels.Length; i++)
            {
                var tile = UIFactory.Rect(grid, "Stat" + i, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UIFactory.Image(tile, new Color(0.16f, 0.11f, 0.32f, 0.92f), UIFactory.Rounded);
                var value = UIFactory.Label(UIFactory.Rect(tile, "Value", new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(-32, 0)),
                    "0", 44, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                value.rectTransform.offsetMin = new Vector2(28, value.rectTransform.offsetMin.y);
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 24;
                value.resizeTextMaxSize = 44;
                _profValues.Add(value);
                UIFactory.Label(UIFactory.Rect(tile, "Label", new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 10), new Vector2(-28, 0)),
                    UIFactory.Spaced(ProfileLabels[i]), 19, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // ---- rewards won by clearing songs.
            UIFactory.Layout(UIFactory.Label(content, "RewardsTitle", UIFactory.Spaced("clear rewards"), 24, UIFactory.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 36);
            var rewards = UIFactory.Rect(content, "Rewards", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Layout(rewards, 230);
            UIFactory.HStack(rewards, 18);
            _profSkinStatus = RewardTile(rewards, "Champion ball", out RectTransform skinArea);
            if (_preview != null)
            {
                _profSkinBall = UIFactory.Rect(skinArea, "Ball", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
                _profSkinBall.texture = _preview.Texture;
                _profSkinBall.uvRect = SkinPreview.UvFor(BallSkin.Champion);
                _profSkinBall.raycastTarget = false;
            }
            _profTrailStatus = RewardTile(rewards, "Aurora trail", out RectTransform trailArea);
            TrailIcon(trailArea, TrailEffect.Aurora);

            _profile.gameObject.SetActive(false);
        }

        private static Text RewardTile(RectTransform row, string name, out RectTransform area)
        {
            var tile = UIFactory.Rect(row, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Image(tile, UIFactory.GlassTint, UIFactory.Glass);
            area = UIFactory.Rect(tile, "Preview", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(150, 150));
            UIFactory.Label(UIFactory.Rect(tile, "Name", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(-16, 30)),
                UIFactory.Spaced(name.ToLowerInvariant()), 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            return UIFactory.Label(UIFactory.Rect(tile, "Status", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(-16, 28)),
                "", 17, UIFactory.Pink, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void ShowProfile(bool on)
        {
            if (_profile == null || _profile.gameObject.activeSelf == on) return;
            _profile.gameObject.SetActive(on);
            _sheet.gameObject.SetActive(!on);
            _profileOpened = Time.unscaledTime;
            if (_preview != null) _preview.Active = on || (_style.gameObject.activeSelf && !_styleTrails);
            if (on) RefreshProfile();
        }

        private void RefreshProfile()
        {
            GameSettings.Stats st = GameSettings.GetStats();
            int level = GameSettings.Level(st.Xp, out float progress);
            _profLevel.text = level.ToString(CultureInfo.InvariantCulture);
            _profTitle.text = GameSettings.LevelTitle(level);
            _profRing.fillAmount = progress;
            long xpInto = 0;
            for (int l = 1; l < level; l++) xpInto += 250L * l; // XP spent reaching this level
            long need = 250L * level;
            _profXp.text = (st.Xp - xpInto).ToString("N0", CultureInfo.InvariantCulture) + " / " + need.ToString("N0", CultureInfo.InvariantCulture) + " XP  ·  NEXT LEVEL";

            int songs = 0, unlocked = 0;
            if (_game.Library != null)
            {
                foreach (SongData s in _game.Library.songs)
                {
                    if (s == null) continue;
                    songs++;
                    if (GameSettings.IsUnlocked(s)) unlocked++;
                }
            }
            string[] values =
            {
                st.runs.ToString("N0", CultureInfo.InvariantCulture),
                st.clears.ToString("N0", CultureInfo.InvariantCulture),
                st.fullCombos.ToString("N0", CultureInfo.InvariantCulture),
                st.allPerfects.ToString("N0", CultureInfo.InvariantCulture),
                st.bestCombo.ToString("N0", CultureInfo.InvariantCulture),
                st.Hits + st.miss > 0 ? (st.Accuracy * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" : "-",
                st.perfect.ToString("N0", CultureInfo.InvariantCulture),
                st.totalScore.ToString("N0", CultureInfo.InvariantCulture),
                GameSettings.TotalStars(_game.Library) + " / " + songs * 9,
                PlayTime(st.playSeconds),
                st.bossesDefeated.ToString("N0", CultureInfo.InvariantCulture),
                unlocked + " / " + songs
            };
            for (int i = 0; i < _profValues.Count && i < values.Length; i++) _profValues[i].text = values[i];

            bool skin = GameSettings.HardClearReward, trail = GameSettings.NormalClearReward;
            _profSkinStatus.text = UIFactory.Spaced(skin ? "won · equip in style" : "clear any song on hard");
            _profSkinStatus.color = skin ? UIFactory.Green : UIFactory.Pink;
            _profTrailStatus.text = UIFactory.Spaced(trail ? "won · equip in style" : "clear any song on normal");
            _profTrailStatus.color = trail ? UIFactory.Green : UIFactory.Pink;
            if (_profSkinBall != null) _profSkinBall.color = skin ? Color.white : new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        private static string PlayTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            return m >= 60 ? (m / 60) + "h " + (m % 60).ToString("00", CultureInfo.InvariantCulture) + "m" : m + "m " + Mathf.FloorToInt(seconds % 60f).ToString("00", CultureInfo.InvariantCulture) + "s";
        }

        private void AnimateProfile(float time)
        {
            if (_profile == null || !_profile.gameObject.activeSelf) return;
            float a = Mathf.Clamp01((time - _profileOpened) / 0.25f);
            float e = 1f - (1f - a) * (1f - a) * (1f - a);
            _profileCard.anchoredPosition = new Vector2(0, Mathf.Lerp(-1600f, 0f, e));
        }

        // ---------------------------------------------------------------- results: reward card

        private RectTransform _reward;
        private CanvasGroup _rewardGroup;
        private RawImage _rewardBall;
        private RectTransform _rewardTrail;
        private Text _rewardTitle, _rewardName;
        private bool _rewardSkin, _rewardTrailWon;

        private void BuildRewardCard()
        {
            // In the results card, between the next-goal line and the buttons: the ball (or trail) won,
            // what it is, and where to equip it.
            _reward = UIFactory.At(_resPanel, "Reward", new Vector2(0.5f, 0.5f), new Vector2(0, -447), new Vector2(900, 220));
            _rewardGroup = _reward.gameObject.AddComponent<CanvasGroup>();
            _rewardGroup.blocksRaycasts = false;
            UIFactory.Image(_reward, new Color(0.1f, 0.06f, 0.22f, 0.97f), UIFactory.Pill);
            UIFactory.Image(UIFactory.Stretch(_reward, "Edge"), UIFactory.Gold, UIFactory.PillOutline);
            var icon = UIFactory.At(_reward, "Icon", new Vector2(0f, 0.5f), new Vector2(140, 0), new Vector2(190, 190));
            UIFactory.Halo(icon, new Color(1f, 0.8f, 0.3f, 0.35f), 1.3f);
            if (_preview != null)
            {
                _rewardBall = UIFactory.Stretch(icon, "Ball").gameObject.AddComponent<RawImage>();
                _rewardBall.texture = _preview.Texture;
                _rewardBall.uvRect = SkinPreview.UvFor(BallSkin.Champion);
                _rewardBall.raycastTarget = false;
            }
            _rewardTrail = UIFactory.Stretch(icon, "Trail");
            TrailIcon(_rewardTrail, TrailEffect.Aurora);
            _rewardTitle = UIFactory.Label(UIFactory.Rect(_reward, "Title", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(270, 42), new Vector2(-300, 44)),
                "", 26, UIFactory.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _rewardName = UIFactory.Label(UIFactory.Rect(_reward, "Name", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(270, -18), new Vector2(-300, 70)),
                "", 56, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(_rewardName, Color.white, UIFactory.Gold, true);
            UIFactory.Label(UIFactory.Rect(_reward, "Hint", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(270, -72), new Vector2(-300, 30)),
                UIFactory.Spaced("equip it in style"), 18, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _reward.gameObject.SetActive(false);
        }

        private void SetupRewardCard(GameSettings.RunRecord rec)
        {
            _rewardSkin = rec.unlockedSkin;
            _rewardTrailWon = rec.unlockedTrail;
            if (_reward != null) _reward.gameObject.SetActive(false);
        }

        /// <summary>The reward card drops in after the stars: the ball first, then (if both were won) the trail.</summary>
        private void AnimateRewardCard(float age, bool quiet)
        {
            if (_reward == null || (!_rewardSkin && !_rewardTrailWon)) return;
            const float At = 3.0f, Swap = 2.6f;
            bool showTrail = !_rewardSkin || (_rewardTrailWon && age >= At + Swap);
            float start = showTrail && _rewardSkin ? At + Swap : At;
            if (age < At)
            {
                _reward.gameObject.SetActive(false);
                return;
            }
            _reward.gameObject.SetActive(true);
            float a = Mathf.Clamp01((age - start) / 0.3f);
            _rewardGroup.alpha = a;
            _reward.localScale = Vector3.one * (a < 0.6f ? Mathf.Lerp(1.4f, 0.95f, a / 0.6f) : Mathf.Lerp(0.95f, 1f, (a - 0.6f) / 0.4f));
            _rewardTitle.text = UIFactory.Spaced(showTrail ? "new trail unlocked" : "new ball unlocked");
            _rewardName.text = showTrail ? BallStyles.Name(TrailEffect.Aurora) : BallStyles.Name(BallSkin.Champion);
            if (_rewardBall != null) _rewardBall.gameObject.SetActive(!showTrail);
            _rewardTrail.gameObject.SetActive(showTrail);
            if (_preview != null) _preview.Active = !showTrail; // the spinning 3D ball, only while it shows

            if (Fire(showTrail && _rewardSkin ? 9 : 8, age, start))
            {
                Burst(_reward.anchoredPosition, 46);
                SfxPlayer sfx = _game != null ? _game.Sfx : null;
                if (sfx != null && !quiet)
                {
                    sfx.PlayNow(sfx.Fanfare, 0.7f);
                    sfx.PlayNow(sfx.PowerUp, 0.4f);
                }
            }
        }
    }
}
