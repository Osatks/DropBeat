using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// All screens (menu, HUD, pause, results, calibration) in a "neon glass" style, built in code.
    /// Two layouts: landscape (desktop) and portrait (phone), switched automatically, inside the safe area.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        public enum Screen { Menu, Hud, Pause, Results, Calibration }

        private static readonly Color DiffEasy = new Color(0.25f, 0.95f, 0.55f);
        private static readonly Color DiffNormal = new Color(0.2f, 0.75f, 1f);
        private static readonly Color DiffHard = new Color(1f, 0.3f, 0.6f);

        private GameController _game;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _root, _safe;
        private Vector2Int _lastScreen;
        private Rect _lastSafe;

        private RectTransform _menu, _hud, _pause, _results, _calibration;
        private readonly Dictionary<RectTransform, float> _fadeStart = new Dictionary<RectTransform, float>();

        // Menu
        private RectTransform _title, _sub, _sheet, _settings, _settingsCard, _heroEq, _dotRow;
        private Text _titleText, _heroTitle, _heroInfo, _heroIndex, _heroBadgeText;
        private Image _heroBg, _heroStripe, _heroBadge;
        private Image[] _heroEqBars;
        private readonly List<Image> _dots = new List<Image>();
        private float _settingsOpened;
        private readonly Button[] _diffButtons = new Button[3];
        private readonly Text[] _diffTexts = new Text[3];
        private readonly UIGradient[] _diffGradients = new UIGradient[3];
        private Text _infoText, _offsetValue, _calibHint;
        private readonly Toggle4[] _toggles = new Toggle4[4];

        private sealed class Toggle4
        {
            public Text label;
            public Image track;
            public RectTransform knob;
        }

        // HUD
        private RectTransform _hudTopLeft, _hudTopRight, _comboRoot, _progressFill, _healthFill, _odFill, _pauseButton, _meter, _meanMarker;
        private Image _healthImage, _odFillImage, _diffChip;
        private Text _hudTitle, _hudDiff, _hudBpm, _scoreText, _accText, _multText, _comboText, _comboLabel, _countIn, _milestone, _odLabel;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<float> _tickBorn = new List<float>();
        private int _nextTick;
        private float _comboPunch;
        private float _milestoneTime = -10f;
        private const float MeterHalfWidth = 260f;

        private struct Popup
        {
            public Text main;
            public Text sub;
            public RectTransform rt;
            public float born;
            public Vector2 start;
        }
        private readonly List<Popup> _popups = new List<Popup>();
        private int _nextPopup;
        private readonly List<Popup> _hints = new List<Popup>();
        private readonly List<Image> _hintBgs = new List<Image>();
        private int _nextHint;

        // Pause
        private RectTransform _pauseCard;
        private Text _resumeCount;

        // Results
        private RectTransform _resPanel, _resRankRt, _resStatsRt;
        private Text _resStatus, _resSong, _resRank, _resScore, _resCounts, _resRecord, _resSuggest;
        private Image _resRankRing;
        private readonly Text[] _tileValues = new Text[4];
        private Button _resApply;
        private Text _resApplyText;

        // Calibration
        private RectTransform _calHelp;
        private Text _calHelpText, _calStatus, _calValue;
        private Image _calCircle, _calRing;
        private Button _calSave;
        private float _calPulse;

        public Screen Current { get; private set; }
        public static bool IsPortrait => UnityEngine.Screen.height > UnityEngine.Screen.width;

        // ================================================================ setup

        public void Init(GameController game)
        {
            _game = game;
            EnsureEventSystem();

            var canvasGo = new GameObject("UI", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)canvasGo.transform;
            _safe = UIFactory.Stretch(_root, "SafeArea");

            BuildHud();
            BuildMenu();
            BuildPause();
            BuildResults();
            BuildCalibration();
            ApplyLayout();
            Show(Screen.Menu);
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
        }

        private RectTransform MakeScreen(string name)
        {
            var rt = UIFactory.Stretch(_safe, name);
            rt.gameObject.AddComponent<CanvasGroup>();
            return rt;
        }

        public void Show(Screen s)
        {
            Current = s;
            SetScreen(_menu, s == Screen.Menu);
            SetScreen(_hud, s == Screen.Hud || s == Screen.Pause || s == Screen.Results);
            SetScreen(_pause, s == Screen.Pause);
            SetScreen(_results, s == Screen.Results);
            SetScreen(_calibration, s == Screen.Calibration);
            if (s == Screen.Menu) RefreshMenu();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void SetScreen(RectTransform rt, bool on)
        {
            if (rt.gameObject.activeSelf == on) return;
            rt.gameObject.SetActive(on);
            if (on) _fadeStart[rt] = Time.unscaledTime;
        }

        private void Update()
        {
            if (_safe == null) return;
            var size = new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (size != _lastScreen || UnityEngine.Screen.safeArea != _lastSafe) ApplyLayout();

            // Screen fade + slight scale-in.
            foreach (var kv in _fadeStart)
            {
                if (!kv.Key.gameObject.activeSelf) continue;
                float a = Mathf.Clamp01((Time.unscaledTime - kv.Value) / 0.22f);
                float e = 1f - (1f - a) * (1f - a);
                kv.Key.GetComponent<CanvasGroup>().alpha = e;
                kv.Key.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, e);
            }

            if (Current == Screen.Menu) AnimateMenu();
        }

        // ================================================================ layout

        // Laid out for a phone held upright (1080 units wide, any height). Landscape (editor, tablets) shows
        // the same layout matched to the height, so nothing has to be laid out twice.
        private void ApplyLayout()
        {
            int w = Mathf.Max(1, UnityEngine.Screen.width);
            int h = Mathf.Max(1, UnityEngine.Screen.height);
            _lastScreen = new Vector2Int(w, h);
            _lastSafe = UnityEngine.Screen.safeArea;
            _safe.anchorMin = new Vector2(_lastSafe.xMin / w, _lastSafe.yMin / h);
            _safe.anchorMax = new Vector2(_lastSafe.xMax / w, _lastSafe.yMax / h);
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;

            _scaler.referenceResolution = new Vector2(1080, 1920);
            _scaler.matchWidthOrHeight = h > w ? 0f : 1f;

            Place(_resPanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 1420));
            Place(_resRankRt, new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(320, 320));
            Place(_resStatsRt, new Vector2(0.5f, 1f), new Vector2(0, -560), new Vector2(880, 720));
            Place(_calHelp, new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(980, 220));
            _calHelpText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Text SectionLabel(Transform parent, string text, float height)
        {
            var t = UIFactory.Label(parent, "Section", UIFactory.Spaced(text), 22, UIFactory.Pink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Layout(t, height);
            return t;
        }

        // ================================================================ MENU
        // Phone first: logo at the top, the scene (ball and sun) in the middle, and everything you touch in a
        // bottom sheet within thumb reach. Options live in a separate settings sheet.

        private void BuildMenu()
        {
            _menu = MakeScreen("Menu");

            // Dark fade under the sheet so it reads over the scene; it runs into the home-indicator area.
            var fade = UIFactory.Rect(_menu, "Fade", Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -220), new Vector2(0, 1350));
            UIFactory.Gradient(UIFactory.Image(fade, Color.white), new Color(0.02f, 0f, 0.07f, 0f), new Color(0.02f, 0f, 0.07f, 0.95f), false);

            _titleText = UIFactory.Label(UIFactory.At(_menu, "Title", new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(1000, 130)),
                UIFactory.Spaced("DROPBEAT"), 100, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_titleText, new Color(0.25f, 0.95f, 1f), new Color(1f, 0.35f, 0.9f), true);
            var tOut = _titleText.gameObject.AddComponent<Outline>();
            tOut.effectColor = new Color(0.55f, 0.15f, 0.9f, 0.55f);
            tOut.effectDistance = new Vector2(3f, -3f);
            _title = _titleText.rectTransform;
            UIFactory.Halo(_title, new Color(0.6f, 0.2f, 1f, 0.22f), 1.3f);
            _sub = UIFactory.Label(UIFactory.At(_menu, "Sub", new Vector2(0.5f, 1f), new Vector2(0, -192), new Vector2(1000, 40)),
                UIFactory.Spaced("land the ball on the beat", 1), 24, UIFactory.Dim, TextAnchor.MiddleCenter).rectTransform;

            _sheet = UIFactory.At(_menu, "Sheet", new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(1000, 830));
            UIFactory.VStack(_sheet, 22, 0, TextAnchor.LowerCenter);

            BuildSongHero();

            _dotRow = UIFactory.Rect(_sheet, "Dots", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dots = UIFactory.HStack(_dotRow, 12);
            dots.childForceExpandWidth = false;
            dots.childForceExpandHeight = false;
            UIFactory.Layout(_dotRow, 14);

            var seg = UIFactory.Rect(_sheet, "Difficulty", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Image(seg, new Color(0.05f, 0.03f, 0.13f, 0.92f), UIFactory.Pill);
            UIFactory.Layout(seg, 108);
            var segRow = UIFactory.Rect(seg, "Row", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-14, -14));
            UIFactory.HStack(segRow, 8);
            string[] names = { "EASY", "NORMAL", "HARD" };
            Color[] diffColors = { DiffEasy, DiffNormal, DiffHard };
            for (int i = 0; i < 3; i++)
            {
                int d = i;
                _diffButtons[i] = UIFactory.Button(segRow, names[i], UIFactory.Spaced(names[i].ToLowerInvariant()), 28, () => _game.SelectDifficulty((Difficulty)d), out _diffTexts[i]);
                _diffGradients[i] = UIFactory.Gradient(_diffButtons[i].targetGraphic, diffColors[i], Color.Lerp(diffColors[i], new Color(0.6f, 0.3f, 1f), 0.6f), true);
            }

            _infoText = UIFactory.Label(_sheet, "Info", string.Empty, 24, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Layout(_infoText, 32);

            var play = UIFactory.Button(_sheet, "PlayButton", UIFactory.Spaced("play"), 60, () => _game.Play(), out _, UIFactory.ButtonStyle.Primary);
            UIFactory.Layout(play, 150);
            UIFactory.Halo((RectTransform)play.transform, new Color(1f, 0.3f, 0.85f, 0.35f), 1.3f);

            var row = UIFactory.Rect(_sheet, "Row", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.HStack(row, 20);
            UIFactory.Layout(row, 92);
            UIFactory.Button(row, "Settings", UIFactory.Spaced("settings"), 26, () => ShowSettings(true), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Button(row, "Calibrate", UIFactory.Spaced("calibrate"), 26, StartCalibration, out _, UIFactory.ButtonStyle.Outline);

            BuildSettings();
        }

        private void BuildSongHero()
        {
            var hero = UIFactory.Rect(_sheet, "Song", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Layout(hero, 290);
            _heroBg = UIFactory.Image(hero, UIFactory.GlassTint, UIFactory.Glass, true);
            hero.gameObject.AddComponent<SwipeArea>().onSwipe = dir => _game.SelectSong(_game.SelectedSong + dir);

            var stripe = UIFactory.Rect(hero, "Stripe", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(120, 0), new Vector2(10, 170));
            _heroStripe = UIFactory.Image(stripe, UIFactory.Cyan, UIFactory.Pill);

            _heroIndex = UIFactory.Label(UIFactory.Rect(hero, "Index", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(152, -46), new Vector2(-400, 30)),
                string.Empty, 20, UIFactory.Pink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _heroTitle = UIFactory.Label(UIFactory.Rect(hero, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(152, -84), new Vector2(-400, 76)),
                string.Empty, 54, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _heroTitle.resizeTextForBestFit = true;
            _heroTitle.resizeTextMinSize = 30;
            _heroTitle.resizeTextMaxSize = 54;
            _heroTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _heroInfo = UIFactory.Label(UIFactory.Rect(hero, "Info", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(152, -168), new Vector2(-400, 34)),
                string.Empty, 25, UIFactory.TextColor, TextAnchor.MiddleLeft);

            _heroEq = UIFactory.Rect(hero, "EQ", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(152, 40), new Vector2(60, 40));
            _heroEqBars = new Image[5];
            for (int i = 0; i < _heroEqBars.Length; i++)
            {
                var bar = UIFactory.Rect(_heroEq, "Bar" + i, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(i * 12f, 0), new Vector2(7, 20));
                _heroEqBars[i] = UIFactory.Image(bar, UIFactory.Cyan, UIFactory.Pill);
            }

            var badge = UIFactory.Rect(hero, "Badge", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-124, 0), new Vector2(104, 104));
            _heroBadge = UIFactory.Image(badge, UIFactory.Faint, UIFactory.RingOutline);
            _heroBadgeText = UIFactory.Label(badge, "Text", string.Empty, 42, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Label(UIFactory.Rect(badge, "Best", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(0, 24)),
                UIFactory.Spaced("best"), 16, UIFactory.Faint, TextAnchor.MiddleCenter, FontStyle.Bold);

            Chevron(hero, "Prev", 0f, "<", -1);
            Chevron(hero, "Next", 1f, ">", 1);
        }

        private void Chevron(RectTransform hero, string name, float side, string glyph, int dir)
        {
            var b = UIFactory.Button(hero, name, glyph, 44, () => _game.SelectSong(_game.SelectedSong + dir), out _, UIFactory.ButtonStyle.Glass);
            var rt = (RectTransform)b.transform;
            Place(rt, new Vector2(side, 0.5f), new Vector2(side == 0f ? 18 : -18, 0), new Vector2(88, 88), new Vector2(side, 0.5f));
            b.targetGraphic.GetComponent<Image>().sprite = UIFactory.Circle;
        }

        private void BuildSettings()
        {
            _settings = UIFactory.Stretch(_menu, "Settings");
            var dim = UIFactory.Image(_settings, new Color(0.01f, 0f, 0.04f, 0.72f), null, true);
            var dimBtn = _settings.gameObject.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() => ShowSettings(false));
            // Run past the safe area so the dim covers the notch and home indicator too.
            _settings.offsetMin = new Vector2(0, -300);
            _settings.offsetMax = new Vector2(0, 300);

            _settingsCard = UIFactory.At(_settings, "Card", new Vector2(0.5f, 0f), new Vector2(0, 330), new Vector2(1000, 960));
            var cardImg = UIFactory.Card(_settingsCard);
            cardImg.raycastTarget = true; // taps on the card must not close it
            var stack = UIFactory.Rect(_settingsCard, "Stack", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-64, -64));
            UIFactory.VStack(stack, 16, 0);

            var header = UIFactory.Rect(stack, "Header", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Layout(header, 84);
            var title = UIFactory.Label(UIFactory.Rect(header, "Title", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(12, 0), Vector2.zero),
                UIFactory.Spaced("settings"), 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Cyan, UIFactory.Pink, true);
            var done = UIFactory.Button(header, "Done", UIFactory.Spaced("done"), 24, () => ShowSettings(false), out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)done.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(190, 76), new Vector2(1f, 0.5f));

            _toggles[0] = MakeToggle(stack, "autoplay  (demo)", () => _game.ToggleAutoplay());
            _toggles[1] = MakeToggle(stack, "assist tick", () => _game.ToggleAssistTick());
            _toggles[2] = MakeToggle(stack, "hit sounds", () => _game.ToggleHitSound());
            _toggles[3] = MakeToggle(stack, "no fail", () => _game.ToggleNoFail());

            var offRow = UIFactory.Rect(stack, "Offset", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Image(offRow, new Color(0.16f, 0.11f, 0.32f, 0.92f), UIFactory.Pill);
            UIFactory.Layout(offRow, 104);
            var hs = UIFactory.HStack(offRow, 10);
            hs.padding = new RectOffset(44, 12, 12, 12);
            hs.childForceExpandWidth = false;
            var offLabel = UIFactory.Label(offRow, "Label", UIFactory.Spaced("audio offset"), 24, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Layout(offLabel, 80).flexibleWidth = 1f;
            UIFactory.Layout(UIFactory.Button(offRow, "Minus", "-", 40, () => _game.AdjustOffset(-5f), out _, UIFactory.ButtonStyle.Outline), 80, 96);
            _offsetValue = UIFactory.Label(offRow, "Value", "+0 ms", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(_offsetValue, 80, 160);
            UIFactory.Layout(UIFactory.Button(offRow, "Plus", "+", 40, () => _game.AdjustOffset(5f), out _, UIFactory.ButtonStyle.Outline), 80, 96);

            UIFactory.Layout(UIFactory.Button(stack, "Calibrate", UIFactory.Spaced("calibrate offset"), 26, StartCalibration, out _, UIFactory.ButtonStyle.Outline), 96);

            _calibHint = UIFactory.Label(stack, "Hint", string.Empty, 22, UIFactory.Gold, TextAnchor.MiddleCenter);
            _calibHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Layout(_calibHint, 64);

            _settings.gameObject.SetActive(false);
        }

        private void ShowSettings(bool on)
        {
            if (_settings.gameObject.activeSelf == on) return;
            _settings.gameObject.SetActive(on);
            _sheet.gameObject.SetActive(!on);
            _settingsOpened = Time.unscaledTime;
        }

        private void StartCalibration()
        {
            ShowSettings(false);
            _game.StartCalibration();
        }

        private Toggle4 MakeToggle(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIFactory.Button(parent, label, UIFactory.Spaced(label), 24, onClick, out Text t, UIFactory.ButtonStyle.Glass);
            UIFactory.Layout(b, 100);
            t.alignment = TextAnchor.MiddleLeft;
            t.rectTransform.offsetMin = new Vector2(44, 0);
            var track = UIFactory.Rect(b.transform, "Track", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(116, 60));
            var trackImg = UIFactory.Image(track, UIFactory.Faint, UIFactory.Pill);
            var knob = UIFactory.Rect(track, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48, 48));
            UIFactory.Image(knob, Color.white, UIFactory.Circle);
            return new Toggle4 { label = t, track = trackImg, knob = knob };
        }

        public void RefreshMenu()
        {
            if (_game == null || _game.Library == null) return;
            var songs = _game.Library.songs;
            int count = songs.Count;
            int sel = _game.SelectedSong;
            SongData song = _game.CurrentSong;
            Color accent = song != null ? Ldr(song.accent) : UIFactory.Cyan;

            while (_dots.Count < count)
            {
                var d = UIFactory.Rect(_dotRow, "Dot" + _dots.Count, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
                _dots.Add(UIFactory.Image(d, Color.white, UIFactory.Pill));
            }
            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].gameObject.SetActive(i < count && count > 1);
                UIFactory.Layout(_dots[i], 14, i == sel ? 44 : 14);
                _dots[i].color = i == sel ? accent : UIFactory.Faint;
            }

            if (song != null)
            {
                _heroIndex.text = UIFactory.Spaced("song") + "   " + (sel + 1) + " / " + count;
                _heroTitle.text = song.title.ToUpperInvariant();
                _heroInfo.text = song.IsAnalyzed
                    ? song.bpm.ToString("F0", CultureInfo.InvariantCulture) + " BPM   ·   " + FormatTime(song.duration) + "   ·   " + BeatAnalyzer.KeyName(song.keyRoot, song.keyMinor).ToUpperInvariant()
                    : "not analysed: run DropBeat > Analyze All Songs";
                _heroBg.color = new Color(accent.r * 0.45f + 0.18f, accent.g * 0.35f + 0.12f, accent.b * 0.4f + 0.38f, 0.96f);
                _heroStripe.color = accent;
                foreach (var bar in _heroEqBars) bar.color = accent;

                var best = GameSettings.GetBest(song, _game.SelectedDifficulty);
                _heroBadgeText.text = best.Exists ? best.rank : "-";
                _heroBadge.color = best.Exists ? RankColor(best.rank) : UIFactory.Faint;
                _heroBadgeText.color = best.Exists ? RankColor(best.rank) : UIFactory.Faint;

                int notes = _game.NoteCount(_game.SelectedDifficulty);
                _infoText.text = notes + " NOTES" + (best.Exists
                    ? "   ·   BEST " + (best.accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%   " + best.score.ToString("N0", CultureInfo.InvariantCulture) + (best.fullCombo ? "   FC" : string.Empty)
                    : "   ·   NO RECORD YET");
            }

            Color[] diffColors = { DiffEasy, DiffNormal, DiffHard };
            for (int d = 0; d < 3; d++)
            {
                bool on = (int)_game.SelectedDifficulty == d;
                _diffGradients[d].enabled = on;
                UIFactory.TintButton(_diffButtons[d], on ? Color.white : new Color(1f, 1f, 1f, 0f));
                _diffTexts[d].color = on ? Color.white : diffColors[d] * 0.9f;
            }

            SetToggle(_toggles[0], GameSettings.Autoplay);
            SetToggle(_toggles[1], GameSettings.AssistTick);
            SetToggle(_toggles[2], GameSettings.HitSound);
            SetToggle(_toggles[3], GameSettings.NoFail);
            _offsetValue.text = GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
            _calibHint.text = GameSettings.Calibrated
                ? "Calibrated. Re-run it if you switch to Bluetooth headphones or other speakers."
                : "Tip: calibrate once so your taps match your speakers or headphones.";
        }

        private static void SetToggle(Toggle4 t, bool on)
        {
            t.knob.anchoredPosition = new Vector2(on ? 28f : -28f, 0f);
            t.track.color = on ? UIFactory.Cyan : new Color(1f, 1f, 1f, 0.16f);
            t.label.color = on ? Color.white : UIFactory.Dim;
        }

        private void AnimateMenu()
        {
            float time = Time.unscaledTime;
            for (int b = 0; b < _heroEqBars.Length; b++)
            {
                float hgt = 8f + 30f * Mathf.Abs(Mathf.Sin(time * (5.3f + b * 1.7f) + b));
                _heroEqBars[b].rectTransform.sizeDelta = new Vector2(7f, hgt);
            }

            // Settings sheet slides up.
            if (_settings.gameObject.activeSelf)
            {
                float a = Mathf.Clamp01((time - _settingsOpened) / 0.22f);
                float e = 1f - (1f - a) * (1f - a) * (1f - a);
                _settingsCard.anchoredPosition = new Vector2(0, Mathf.Lerp(-700f, 330f, e));
            }
        }

        private static Color Ldr(Color hdr)
        {
            float m = Mathf.Max(1f, Mathf.Max(hdr.r, Mathf.Max(hdr.g, hdr.b)));
            return new Color(hdr.r / m, hdr.g / m, hdr.b / m, 1f);
        }

        private static Color RankColor(string rank)
        {
            if (string.IsNullOrEmpty(rank)) return UIFactory.Faint;
            if (rank == "F") return UIFactory.Red;
            if (rank.StartsWith("S")) return UIFactory.Gold;
            if (rank == "A") return UIFactory.Cyan;
            if (rank == "B") return UIFactory.Green;
            return UIFactory.Pink;
        }

        private static string FormatTime(float s)
        {
            int m = Mathf.FloorToInt(s / 60f);
            int sec = Mathf.FloorToInt(s - m * 60f);
            return m + ":" + sec.ToString("00");
        }

        // ================================================================ HUD

        private void BuildHud()
        {
            _hud = MakeScreen("Hud");

            // Shade behind the top bar so it reads over bright rings and the sun (runs up under the notch).
            var shade = UIFactory.Rect(_hud, "TopShade", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 220), new Vector2(0, 560));
            UIFactory.Gradient(UIFactory.Image(shade, Color.white), new Color(0.02f, 0f, 0.07f, 0.85f), new Color(0.02f, 0f, 0.07f, 0f), false);

            var progress = UIFactory.Rect(_hud, "Progress", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 6));
            UIFactory.Image(progress, new Color(1f, 1f, 1f, 0.07f));
            _progressFill = UIFactory.Rect(progress, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Gradient(UIFactory.Image(_progressFill, Color.white), UIFactory.Cyan, UIFactory.Pink, true);

            // Pause: top-left, out of the way of tapping thumbs.
            _pauseButton = UIFactory.At(_hud, "PauseBtn", new Vector2(0, 1), new Vector2(28, -26), new Vector2(104, 104));
            var pbImg = UIFactory.Image(_pauseButton, new Color(0.16f, 0.11f, 0.32f, 0.9f), UIFactory.Circle, true);
            UIFactory.Image(UIFactory.Stretch(_pauseButton, "Ring"), new Color(0.6f, 0.55f, 1f, 0.6f), UIFactory.RingOutline);
            var pbtn = _pauseButton.gameObject.AddComponent<Button>();
            pbtn.targetGraphic = pbImg;
            pbtn.navigation = new Navigation { mode = Navigation.Mode.None };
            pbtn.onClick.AddListener(() => _game.PauseGame());
            _pauseButton.gameObject.AddComponent<PressFX>();
            UIFactory.Image(UIFactory.Rect(_pauseButton, "BarL", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-10, 0), new Vector2(11, 36)), Color.white, UIFactory.Pill);
            UIFactory.Image(UIFactory.Rect(_pauseButton, "BarR", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(11, 36)), Color.white, UIFactory.Pill);

            _hudTopLeft = UIFactory.At(_hud, "TopLeft", new Vector2(0, 1), new Vector2(152, -30), new Vector2(480, 100));
            _hudTitle = UIFactory.Label(UIFactory.Rect(_hudTopLeft, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 46)), "", 32, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _hudTitle.resizeTextForBestFit = true;
            _hudTitle.resizeTextMinSize = 20;
            _hudTitle.resizeTextMaxSize = 32;
            _hudTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            var chipRt = UIFactory.Rect(_hudTopLeft, "DiffChip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -56), new Vector2(128, 36));
            _diffChip = UIFactory.Image(chipRt, DiffNormal, UIFactory.Pill);
            _hudDiff = UIFactory.Label(chipRt, "Text", "", 19, new Color(0.05f, 0.02f, 0.1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _hudBpm = UIFactory.Label(UIFactory.Rect(_hudTopLeft, "Bpm", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(144, -56), new Vector2(-144, 36)), "", 22, UIFactory.Dim, TextAnchor.MiddleLeft);

            _hudTopRight = UIFactory.At(_hud, "TopRight", new Vector2(1, 1), new Vector2(-30, -20), new Vector2(420, 130));
            _scoreText = UIFactory.Label(UIFactory.Rect(_hudTopRight, "Score", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 76)), "0", 66, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Gradient(_scoreText, Color.white, new Color(0.45f, 0.9f, 1f), false);
            _accText = UIFactory.Label(UIFactory.Rect(_hudTopRight, "Acc", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-112, -82), new Vector2(-112, 36)), "100.00%", 26, UIFactory.TextColor, TextAnchor.MiddleRight);
            var mult = UIFactory.Rect(_hudTopRight, "Mult", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -82), new Vector2(98, 36));
            UIFactory.Image(mult, new Color(1f, 0.8f, 0.3f, 0.95f), UIFactory.PillOutline);
            _multText = UIFactory.Label(mult, "Text", "x1.0", 21, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Life and overdrive: two full-width bars under the top bar.
            var hb = UIFactory.Rect(_hud, "Health", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -158), new Vector2(-60, 10));
            UIFactory.Image(hb, new Color(1f, 1f, 1f, 0.1f), UIFactory.Pill);
            _healthFill = UIFactory.Rect(hb, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _healthImage = UIFactory.Image(_healthFill, Color.white, UIFactory.Pill);
            UIFactory.Gradient(_healthImage, UIFactory.Green, UIFactory.Cyan, true);

            var odRoot = UIFactory.Rect(_hud, "Overdrive", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -178), new Vector2(-60, 44));
            var odTrack = UIFactory.Rect(odRoot, "Track", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 12));
            UIFactory.Image(odTrack, new Color(1f, 1f, 1f, 0.1f), UIFactory.Pill);
            _odFill = UIFactory.Rect(odTrack, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _odFillImage = UIFactory.Image(_odFill, Color.white, UIFactory.Pill);
            UIFactory.Gradient(_odFillImage, UIFactory.Gold, UIFactory.Pink, true);
            _odLabel = UIFactory.Label(UIFactory.Rect(odRoot, "Label", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 26)), UIFactory.Spaced("overdrive"), 17, UIFactory.Dim, TextAnchor.MiddleRight, FontStyle.Bold);

            _comboRoot = UIFactory.At(_hud, "Combo", new Vector2(0.5f, 1), new Vector2(0, -240), new Vector2(520, 180));
            _comboText = UIFactory.Label(UIFactory.Rect(_comboRoot, "N", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 130)), "", 120, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_comboText, Color.white, new Color(1f, 0.45f, 0.9f), false);
            var cOut = _comboText.gameObject.AddComponent<Outline>();
            cOut.effectColor = new Color(0.6f, 0.1f, 0.8f, 0.5f);
            cOut.effectDistance = new Vector2(2f, -2f);
            _comboLabel = UIFactory.Label(UIFactory.Rect(_comboRoot, "L", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -128), new Vector2(0, 30)), "", 20, UIFactory.Pink, TextAnchor.MiddleCenter, FontStyle.Bold);

            _countIn = UIFactory.Label(UIFactory.At(_hud, "CountIn", new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(800, 240)), "", 180, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_countIn, Color.white, UIFactory.Cyan, false);
            _milestone = UIFactory.Label(UIFactory.At(_hud, "Milestone", new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 120)), "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_milestone, UIFactory.Gold, UIFactory.Pink, true);

            // Hit error meter at the bottom, labels above its ends so nothing crowds the screen edges.
            _meter = UIFactory.At(_hud, "Meter", new Vector2(0.5f, 0), new Vector2(0, 56), new Vector2(MeterHalfWidth * 2f, 12));
            JudgeWindows jw = JudgeWindows.For(Difficulty.Normal);
            AddZone(_meter, 1f, new Color(0.3f, 1f, 0.45f, 0.25f));
            AddZone(_meter, (float)(jw.great / jw.good), new Color(0.2f, 0.9f, 1f, 0.35f));
            AddZone(_meter, (float)(jw.perfect / jw.good), new Color(1f, 0.85f, 0.3f, 0.5f));
            UIFactory.Image(UIFactory.Rect(_meter, "Center", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3, 26)), Color.white);
            for (int i = 0; i < 28; i++)
            {
                var t = UIFactory.Rect(_meter, "Tick" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 32));
                _ticks.Add(UIFactory.Image(t, Color.clear, UIFactory.Pill));
                _tickBorn.Add(-100f);
            }
            _meanMarker = UIFactory.Rect(_meter, "Mean", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(14, 14));
            UIFactory.Image(_meanMarker, Color.white, UIFactory.Circle);
            UIFactory.Label(UIFactory.Rect(_meter, "Early", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(0, 16), new Vector2(160, 26)), UIFactory.Spaced("early"), 17, UIFactory.Faint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Label(UIFactory.Rect(_meter, "Late", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 16), new Vector2(160, 26)), UIFactory.Spaced("late"), 17, UIFactory.Faint, TextAnchor.MiddleRight, FontStyle.Bold);

            for (int i = 0; i < 8; i++)
            {
                var rt = UIFactory.Rect(_hud, "Popup" + i, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 90));
                rt.gameObject.AddComponent<CanvasGroup>();
                var main = UIFactory.Label(UIFactory.Stretch(rt, "Main"), "", 54, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                main.gameObject.AddComponent<UIGradient>();
                var sh = main.gameObject.AddComponent<Outline>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
                sh.effectDistance = new Vector2(2f, -2f);
                var sub = UIFactory.Label(UIFactory.Rect(rt, "Sub", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1f), new Vector2(0, 4), new Vector2(0, 28)), "", 21, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
                rt.gameObject.SetActive(false);
                _popups.Add(new Popup { main = main, sub = sub, rt = rt, born = -10f });
            }

            for (int i = 0; i < 4; i++)
            {
                var rt = UIFactory.Rect(_hud, "Hint" + i, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 52));
                rt.gameObject.AddComponent<CanvasGroup>();
                var bg = UIFactory.Image(rt, UIFactory.Cyan, UIFactory.PillOutline);
                var txt = UIFactory.Label(UIFactory.Stretch(rt, "Text"), "", 23, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                rt.gameObject.SetActive(false);
                _hints.Add(new Popup { main = txt, rt = rt, born = -10f });
                _hintBgs.Add(bg);
            }
        }

        private void AddZone(RectTransform meter, float fraction, Color c)
        {
            var z = UIFactory.Rect(meter, "Zone", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MeterHalfWidth * 2f * fraction, 0));
            UIFactory.Image(z, c, UIFactory.Pill);
        }

        /// <summary>True if a pointer tap at this screen position is on the pause button (so it is not a hit).</summary>
        public bool IsOverPauseButton(Vector2 screen)
        {
            return _pauseButton != null && RectTransformUtility.RectangleContainsScreenPoint(_pauseButton, screen, null);
        }

        public void SetupHud(SongData song, Difficulty d, bool autoplay)
        {
            _hudTitle.text = song.title.ToUpperInvariant();
            _hudDiff.text = d.ToString().ToUpperInvariant();
            _diffChip.color = d == Difficulty.Easy ? DiffEasy : d == Difficulty.Hard ? DiffHard : DiffNormal;
            _hudBpm.text = song.bpm.ToString("F0", CultureInfo.InvariantCulture) + " BPM" +
                           (autoplay ? "   <color=#" + UIFactory.Hex(UIFactory.Pink) + ">" + UIFactory.Spaced("auto") + "</color>" : string.Empty);
            _countIn.text = string.Empty;
            _milestone.text = string.Empty;
            _comboText.text = string.Empty;
            _comboLabel.text = string.Empty;
            for (int i = 0; i < _ticks.Count; i++)
            {
                _ticks[i].color = Color.clear;
                _tickBorn[i] = -100f;
            }
            foreach (var p in _popups) p.rt.gameObject.SetActive(false);
            foreach (var p in _hints) p.rt.gameObject.SetActive(false);
            _meanMarker.anchoredPosition = new Vector2(0, 8);
            UpdateOverdrive(0f, false);
        }

        public void UpdateHud(ScoreState s, float progress, float meanErrorMs, JudgeWindows windows)
        {
            _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _healthFill.anchorMax = new Vector2(Mathf.Clamp01(s.Health), 1f);
            _healthImage.color = Color.Lerp(UIFactory.Red, Color.white, Mathf.InverseLerp(0.2f, 0.6f, s.Health));
            _scoreText.text = s.Score.ToString("N0", CultureInfo.InvariantCulture);
            _accText.text = (s.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
            _multText.text = "x" + s.ComboMultiplier.ToString("F1", CultureInfo.InvariantCulture);

            bool showCombo = s.Combo >= 3;
            _comboText.text = showCombo ? s.Combo.ToString() : string.Empty;
            _comboLabel.text = showCombo ? UIFactory.Spaced("combo") : string.Empty;
            _comboPunch = Mathf.MoveTowards(_comboPunch, 0f, Time.unscaledDeltaTime * 4f);
            _comboRoot.localScale = Vector3.one * (1f + 0.18f * _comboPunch);

            float now = Time.unscaledTime;
            for (int i = 0; i < _ticks.Count; i++)
            {
                float age = now - _tickBorn[i];
                if (age > 4f)
                {
                    if (_ticks[i].color.a > 0f) _ticks[i].color = Color.clear;
                    continue;
                }
                Color c = _ticks[i].color;
                c.a = Mathf.Clamp01(1f - age / 4f);
                _ticks[i].color = c;
            }
            float meanX = Mathf.Clamp(meanErrorMs * 0.001f / (float)windows.good, -1f, 1f) * MeterHalfWidth;
            _meanMarker.anchoredPosition = new Vector2(Mathf.Lerp(_meanMarker.anchoredPosition.x, meanX, Time.unscaledDeltaTime * 6f), 8);

            float ma = Mathf.Clamp01(1.4f - (now - _milestoneTime) / 1.2f);
            _milestone.color = new Color(1f, 1f, 1f, ma);
            _milestone.rectTransform.localScale = Vector3.one * (1f + 0.3f * Mathf.Clamp01(1f - (now - _milestoneTime) / 0.2f));

            AnimatePopups(_popups, now, 0.6f, 50f);
            AnimatePopups(_hints, now, 1.0f, 30f);
        }

        private static void AnimatePopups(List<Popup> list, float now, float life, float rise)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Popup p = list[i];
                if (!p.rt.gameObject.activeSelf) continue;
                float age = now - p.born;
                if (age > life)
                {
                    p.rt.gameObject.SetActive(false);
                    continue;
                }
                float a = Mathf.Clamp01(1f - (age - life * 0.45f) / (life * 0.55f));
                p.rt.anchoredPosition = p.start + new Vector2(0, rise * (1f - Mathf.Pow(1f - age / life, 3f)));
                p.rt.localScale = Vector3.one * (1f + 0.35f * Mathf.Clamp01(1f - age / 0.1f));
                p.rt.GetComponent<CanvasGroup>().alpha = a;
            }
        }

        public void UpdateOverdrive(float meter, bool active)
        {
            _odFill.anchorMax = new Vector2(Mathf.Clamp01(meter), 1f);
            float pulse = active ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
            _odFillImage.color = new Color(pulse, pulse, pulse, 1f);
            _odLabel.text = active ? UIFactory.Spaced("overdrive x2") : UIFactory.Spaced("overdrive");
            _odLabel.color = active ? UIFactory.Gold : UIFactory.Dim;
        }

        public void PunchCombo() => _comboPunch = 1f;

        public void ShowMilestone(string text)
        {
            _milestone.text = text;
            _milestoneTime = Time.unscaledTime;
        }

        public void SetCountIn(string text, float scale)
        {
            _countIn.text = text;
            _countIn.rectTransform.localScale = Vector3.one * scale;
        }

        private Vector2 HudPoint(Vector3 world, Camera cam, Vector2 offset)
        {
            Vector2 screen = cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, screen, null, out Vector2 local);
            return local + _hud.rect.size * 0.5f + offset;
        }

        public void ShowJudgement(Grade grade, double error, Vector3 worldPos, Camera cam, JudgeWindows w, string label = null)
        {
            int idx = _nextPopup;
            _nextPopup = (_nextPopup + 1) % _popups.Count;
            Popup p = _popups[idx];
            p.start = HudPoint(worldPos, cam, new Vector2(0, 74));
            p.born = Time.unscaledTime;
            p.rt.anchoredPosition = p.start;
            p.rt.gameObject.SetActive(true);

            Color a, b;
            switch (grade)
            {
                case Grade.Perfect: p.main.text = "PERFECT"; a = new Color(1f, 0.95f, 0.6f); b = UIFactory.Gold; break;
                case Grade.Great: p.main.text = "GREAT"; a = Color.white; b = UIFactory.Cyan; break;
                case Grade.Good: p.main.text = "GOOD"; a = Color.white; b = UIFactory.Green; break;
                default: p.main.text = "MISS"; a = new Color(1f, 0.6f, 0.6f); b = UIFactory.Red; break;
            }
            if (!string.IsNullOrEmpty(label)) p.main.text = label;
            p.main.color = Color.white;
            p.main.GetComponent<UIGradient>().Set(a, b, false);

            if (grade == Grade.Perfect || grade == Grade.Miss || double.IsNaN(error)) p.sub.text = string.Empty;
            else p.sub.text = (error < 0 ? "EARLY " : "LATE ") + System.Math.Abs(error * 1000.0).ToString("F0", CultureInfo.InvariantCulture) + " MS";
            p.sub.color = error < 0 ? UIFactory.Cyan : UIFactory.Pink;
            _popups[idx] = p;

            if (grade != Grade.Miss && !double.IsNaN(error)) AddTick(error, grade, w);
        }

        /// <summary>Small chip above a special ring ("HOLD", "SWIPE LEFT", "TAP BOTH", "+1 BALL").</summary>
        public void ShowHint(string text, Vector3 worldPos, Camera cam, Color color)
        {
            int idx = _nextHint;
            _nextHint = (_nextHint + 1) % _hints.Count;
            Popup p = _hints[idx];
            p.start = HudPoint(worldPos, cam, new Vector2(0, 92));
            p.born = Time.unscaledTime;
            p.rt.anchoredPosition = p.start;
            p.main.text = UIFactory.Spaced(text.ToLowerInvariant());
            p.main.color = color;
            _hintBgs[idx].color = color;
            p.rt.sizeDelta = new Vector2(80 + text.Length * 26, 52);
            p.rt.gameObject.SetActive(true);
            _hints[idx] = p;
        }

        private void AddTick(double error, Grade grade, JudgeWindows w)
        {
            int i = _nextTick;
            _nextTick = (_nextTick + 1) % _ticks.Count;
            float x = Mathf.Clamp((float)(error / w.good), -1f, 1f) * MeterHalfWidth;
            _ticks[i].rectTransform.anchoredPosition = new Vector2(x, 0);
            _ticks[i].color = grade == Grade.Perfect ? UIFactory.Gold : grade == Grade.Great ? UIFactory.Cyan : UIFactory.Green;
            _tickBorn[i] = Time.unscaledTime;
        }

        // ================================================================ PAUSE

        private void BuildPause()
        {
            _pause = MakeScreen("Pause");
            UIFactory.Image(_pause, new Color(0.01f, 0f, 0.04f, 0.7f), null, true);
            _pauseCard = UIFactory.At(_pause, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 600));
            UIFactory.Card(_pauseCard);
            var title = UIFactory.Label(UIFactory.Rect(_pauseCard, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(0, 90)), UIFactory.Spaced("paused"), 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Cyan, UIFactory.Pink, true);
            var buttons = UIFactory.Rect(_pauseCard, "Buttons", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(-120, -230));
            UIFactory.VStack(buttons, 18, 0, TextAnchor.MiddleCenter);
            UIFactory.Layout(UIFactory.Button(buttons, "Resume", UIFactory.Spaced("resume"), 32, () => _game.ResumeGame(), out _, UIFactory.ButtonStyle.Primary), 92);
            UIFactory.Layout(UIFactory.Button(buttons, "Restart", UIFactory.Spaced("restart"), 28, () => _game.Retry(), out _, UIFactory.ButtonStyle.Outline), 84);
            UIFactory.Layout(UIFactory.Button(buttons, "Quit", UIFactory.Spaced("song select"), 28, () => _game.ToMenu(), out _, UIFactory.ButtonStyle.Outline), 84);
            _resumeCount = UIFactory.Label(UIFactory.At(_pause, "Count", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 300)), "", 200, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_resumeCount, Color.white, UIFactory.Cyan, false);
        }

        public void SetResumeCountdown(string text)
        {
            bool counting = !string.IsNullOrEmpty(text);
            _resumeCount.text = text;
            _pauseCard.gameObject.SetActive(!counting);
        }

        // ================================================================ RESULTS

        private void BuildResults()
        {
            _results = MakeScreen("Results");
            UIFactory.Image(_results, new Color(0.01f, 0f, 0.04f, 0.6f), null, true);
            _resPanel = UIFactory.At(_results, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300, 880));
            UIFactory.Card(_resPanel);

            _resStatus = UIFactory.Label(UIFactory.Rect(_resPanel, "Status", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(0, 70)), "", 58, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_resStatus, UIFactory.Cyan, UIFactory.Pink, true);
            _resSong = UIFactory.Label(UIFactory.Rect(_resPanel, "Song", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -108), new Vector2(0, 32)), "", 22, UIFactory.Dim, TextAnchor.MiddleCenter);

            _resRankRt = UIFactory.At(_resPanel, "Rank", new Vector2(0f, 0.5f), new Vector2(80, 30), new Vector2(340, 340));
            UIFactory.Halo(_resRankRt, new Color(1f, 0.6f, 0.2f, 0.25f), 1.25f);
            _resRankRing = UIFactory.Image(_resRankRt, UIFactory.Gold, UIFactory.RingOutline);
            _resRank = UIFactory.Label(_resRankRt, "Letter", "S", 190, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _resRank.gameObject.AddComponent<UIGradient>();

            _resStatsRt = UIFactory.At(_resPanel, "Stats", new Vector2(1f, 0.5f), new Vector2(-70, 30), new Vector2(760, 520));
            UIFactory.VStack(_resStatsRt, 14, 0, TextAnchor.UpperCenter);
            _resScore = UIFactory.Label(_resStatsRt, "Score", "", 76, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(_resScore, Color.white, new Color(0.45f, 0.9f, 1f), false);
            UIFactory.Layout(_resScore, 86);

            var tiles = UIFactory.Rect(_resStatsRt, "Tiles", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var grid = tiles.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(372, 96);
            grid.spacing = new Vector2(14, 14);
            UIFactory.Layout(tiles, 206);
            string[] labels = { "accuracy", "max combo", "timing", "spread" };
            for (int i = 0; i < 4; i++)
            {
                var tile = UIFactory.Rect(tiles, "Tile" + i, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UIFactory.Image(tile, new Color(0.35f, 0.3f, 0.8f, 0.85f), UIFactory.Glass);
                UIFactory.Label(UIFactory.Rect(tile, "Label", new Vector2(0, 0.55f), new Vector2(1, 1), new Vector2(0, 1), new Vector2(24, -10), new Vector2(-48, 0)), UIFactory.Spaced(labels[i]), 15, UIFactory.Dim, TextAnchor.UpperLeft, FontStyle.Bold);
                _tileValues[i] = UIFactory.Label(UIFactory.Rect(tile, "Value", new Vector2(0, 0), new Vector2(1, 0.6f), new Vector2(0, 0), new Vector2(24, 10), new Vector2(-48, 0)), "", 34, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            }

            _resCounts = UIFactory.Label(_resStatsRt, "Counts", "", 24, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Layout(_resCounts, 36);
            _resRecord = UIFactory.Label(_resStatsRt, "Record", "", 28, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(_resRecord, UIFactory.Gold, UIFactory.Pink, true);
            UIFactory.Layout(_resRecord, 36);

            _resSuggest = UIFactory.Label(UIFactory.Rect(_resPanel, "Suggest", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 186), new Vector2(-120, 34)), "", 21, UIFactory.Gold, TextAnchor.MiddleCenter);
            _resApply = UIFactory.Button(_resPanel, "Apply", "APPLY", 20, () => _game.ApplySuggestedOffset(), out _resApplyText, UIFactory.ButtonStyle.Outline);
            Place((RectTransform)_resApply.transform, new Vector2(0.5f, 0), new Vector2(0, 128), new Vector2(380, 50));

            var row = UIFactory.At(_resPanel, "Buttons", new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(780, 84));
            UIFactory.HStack(row, 22);
            UIFactory.Button(row, "Retry", UIFactory.Spaced("retry"), 32, () => _game.Retry(), out _, UIFactory.ButtonStyle.Primary);
            UIFactory.Button(row, "Menu", UIFactory.Spaced("song select"), 26, () => _game.ToMenu(), out _, UIFactory.ButtonStyle.Outline);
        }

        public void ShowResults(SongData song, Difficulty d, ScoreState s, bool failed, bool autoplay, bool newRecord, float suggestedOffsetMs, bool canSuggest)
        {
            Show(Screen.Results);
            // Keep the results card clean: hide the in-game combo, count-in and popups behind it.
            _comboText.text = string.Empty;
            _comboLabel.text = string.Empty;
            _countIn.text = string.Empty;
            _milestone.text = string.Empty;
            foreach (var p in _popups) p.rt.gameObject.SetActive(false);
            foreach (var p in _hints) p.rt.gameObject.SetActive(false);
            string rank = s.Rank(failed);
            _resStatus.text = UIFactory.Spaced(failed ? "failed" : s.FullCombo ? (s.Perfect == s.TotalNotes ? "all perfect" : "full combo") : "clear");
            _resSong.text = song.title.ToUpperInvariant() + "   ·   " + d.ToString().ToUpperInvariant() + (autoplay ? "   ·   AUTOPLAY" : string.Empty);
            Color rc = RankColor(rank);
            _resRank.text = rank;
            _resRank.GetComponent<UIGradient>().Set(Color.white, rc, false);
            _resRankRing.color = rc;
            _resScore.text = s.Score.ToString("N0", CultureInfo.InvariantCulture);
            s.ErrorStats(out float mean, out float std, out _);
            _tileValues[0].text = (s.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
            _tileValues[1].text = s.MaxCombo + " / " + s.TotalNotes;
            _tileValues[2].text = s.Errors.Count > 0 ? mean.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms" : "-";
            _tileValues[3].text = s.Errors.Count > 0 ? std.ToString("F1", CultureInfo.InvariantCulture) + " ms" : "-";
            _resCounts.text =
                "<color=#" + UIFactory.Hex(UIFactory.Gold) + ">PERFECT " + s.Perfect + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Cyan) + ">GREAT " + s.Great + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Green) + ">GOOD " + s.Good + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Red) + ">MISS " + s.Miss + "</color>";
            _resRecord.text = newRecord ? UIFactory.Spaced("new record") : string.Empty;

            _resSuggest.text = canSuggest
                ? "Your taps were consistently " + Mathf.Abs(suggestedOffsetMs).ToString("F0") + " ms " + (suggestedOffsetMs > 0 ? "late" : "early") + ". Adjust the audio offset?"
                : string.Empty;
            _resApply.gameObject.SetActive(canSuggest);
            _resApplyText.text = "APPLY OFFSET " + (GameSettings.OffsetMs + suggestedOffsetMs).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " MS";
        }

        public void HideOffsetSuggestion()
        {
            _resApply.gameObject.SetActive(false);
            _resSuggest.text = "Offset saved: " + GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
        }

        // ================================================================ CALIBRATION

        private void BuildCalibration()
        {
            _calibration = MakeScreen("Calibration");
            UIFactory.Image(_calibration, new Color(0.01f, 0f, 0.04f, 0.78f), null, true);
            var title = UIFactory.Label(UIFactory.At(_calibration, "Title", new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1400, 100)), UIFactory.Spaced("calibration"), 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Cyan, UIFactory.Pink, true);
            _calHelpText = UIFactory.Label(UIFactory.At(_calibration, "Help", new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(1500, 90)),
                "Listen to the clicks and tap (Space, click or touch) exactly on each click.\nFollow your ears, not the circle: this measures the delay of your speakers or headphones.", 26, UIFactory.TextColor, TextAnchor.MiddleCenter);
            _calHelp = _calHelpText.rectTransform;

            var circleRt = UIFactory.At(_calibration, "Circle", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(260, 260));
            _calRing = UIFactory.Image(circleRt, UIFactory.Cyan, UIFactory.RingOutline);
            var inner = UIFactory.At(circleRt, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 200));
            _calCircle = UIFactory.Image(inner, UIFactory.Cyan, UIFactory.GlowSprite);
            _calStatus = UIFactory.Label(UIFactory.At(_calibration, "Status", new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(1000, 50)), "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _calValue = UIFactory.Label(UIFactory.At(_calibration, "Value", new Vector2(0.5f, 0.5f), new Vector2(0, -256), new Vector2(1000, 50)), "", 26, UIFactory.Gold, TextAnchor.MiddleCenter);

            var row = UIFactory.At(_calibration, "Buttons", new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(980, 80));
            UIFactory.HStack(row, 18);
            _calSave = UIFactory.Button(row, "Save", UIFactory.Spaced("save"), 26, () => _game.SaveCalibration(), out _, UIFactory.ButtonStyle.Primary);
            UIFactory.Button(row, "Restart", UIFactory.Spaced("restart"), 22, () => _game.StartCalibration(), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Button(row, "Default", UIFactory.Spaced("default"), 22, () => _game.ResetOffsetToDefault(), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Button(row, "Cancel", UIFactory.Spaced("back"), 22, () => _game.CancelCalibration(), out _, UIFactory.ButtonStyle.Outline);
        }

        public void PulseCalibration() => _calPulse = 1f;

        public void UpdateCalibration(float beatPhase, int taps, int needed, float medianMs, float spreadMs, bool ready)
        {
            _calPulse = Mathf.MoveTowards(_calPulse, 0f, Time.unscaledDeltaTime * 5f);
            float flash = Mathf.Exp(-beatPhase * 8f);
            _calRing.rectTransform.localScale = Vector3.one * (1f + 0.18f * flash + 0.12f * _calPulse);
            _calCircle.color = new Color(UIFactory.Cyan.r, UIFactory.Cyan.g, UIFactory.Cyan.b, 0.25f + 0.75f * flash);
            _calStatus.text = taps < needed ? UIFactory.Spaced("taps") + "   " + taps + " / " + needed : "DONE  ·  " + taps + " TAPS";
            _calValue.text = taps >= 4
                ? "Measured offset " + medianMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms   (spread " + spreadMs.ToString("F0") + " ms)" + (spreadMs > 40f ? "  ·  keep going, taps are uneven" : string.Empty)
                : "Current offset " + GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
            _calSave.interactable = ready;
        }
    }
}
