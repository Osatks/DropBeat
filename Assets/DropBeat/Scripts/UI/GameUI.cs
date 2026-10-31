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
    public sealed partial class GameUI : MonoBehaviour
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
        private Text _titleText, _heroTitle, _heroInfo, _heroIndex, _heroBadgeText, _heroTwist;
        // Song twist badge in the HUD (name + blurb, or its meter).
        private RectTransform _twistPill, _twistTrack, _twistFill;
        private Text _twistText;
        private Image _twistEdge, _twistFillImage;
        private string _twistDefault = string.Empty;
        private Color _twistTint = Color.white;
        private Image _heroBg, _heroStripe, _heroBadge;
        private Image[] _heroEqBars;
        private readonly List<Image> _dots = new List<Image>();
        private float _settingsOpened;
        // Style locker: every ball skin / trail as a tile (live 3D previews for balls).
        private RectTransform _style, _styleCard, _ballGrid, _trailGrid;
        private ScrollRect _styleScroll;
        private float _styleOpened;
        private bool _styleTrails;
        private Button _tabBalls, _tabTrails;
        private Text _tabBallsText, _tabTrailsText, _styleInfo;
        private UIGradient _tabBallsGrad, _tabTrailsGrad;
        private SkinPreview _preview;
        private readonly List<LockerTile> _skinTiles = new List<LockerTile>();
        private readonly List<LockerTile> _trailTiles = new List<LockerTile>();
        private RectTransform _toast;
        private Text _toastText;
        private float _toastTime = -10f;

        private sealed class LockerTile
        {
            public Image bg;
            public Graphic previewTint;
            public Text name, status;
            public Image[] pips;
            public RectTransform pipRow, lockIcon;
        }
        private readonly Button[] _diffButtons = new Button[3];
        private readonly Text[] _diffTexts = new Text[3];
        private readonly UIGradient[] _diffGradients = new UIGradient[3];
        private Text _infoText, _offsetValue, _calibHint, _playText;
        private readonly Toggle4[] _toggles = new Toggle4[5];

        private sealed class Toggle4
        {
            public Text label;
            public Image track;
            public RectTransform knob;
        }

        // HUD
        private RectTransform _hudTopLeft, _hudTopRight, _comboRoot, _progressFill, _healthFill, _odFill, _pauseButton, _meter, _meanMarker;
        private Image _healthImage, _odFillImage, _diffChip, _progressRing;
        private RectTransform _odBadge;
        private UIGradient _comboGrad;
        private int _comboTier = -1;
        private float _shownScore;
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
            public Grade grade;
            public Vector3 world;
            public int lane;
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
            BuildToast();
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
            // Paused: the frozen count-in and milestone must not show through the pause card or the resume countdown.
            _countIn.enabled = s != Screen.Pause;
            _milestone.enabled = s != Screen.Pause;
            if (s == Screen.Menu) RefreshMenu();
            // The skin preview camera only renders while the locker is visible.
            if (_preview != null) _preview.Active = s == Screen.Menu && ((_style.gameObject.activeSelf && !_styleTrails) || (_profile != null && _profile.gameObject.activeSelf));
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
            if (Current == Screen.Results) AnimateResults();
            if (_hud != null && _hud.gameObject.activeSelf) AnimateFeel();
            AnimateToast();
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

            var play = UIFactory.Button(_sheet, "PlayButton", UIFactory.Spaced("play"), 60, () => _game.Play(), out _playText, UIFactory.ButtonStyle.Primary);
            UIFactory.Layout(play, 150);
            UIFactory.Halo((RectTransform)play.transform, new Color(1f, 0.3f, 0.85f, 0.35f), 1.3f);

            var row = UIFactory.Rect(_sheet, "Row", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.HStack(row, 20);
            UIFactory.Layout(row, 92);
            UIFactory.Button(row, "Settings", UIFactory.Spaced("settings"), 26, () => ShowSettings(true), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Button(row, "Style", UIFactory.Spaced("style"), 26, () => ShowStyle(true), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Button(row, "Profile", UIFactory.Spaced("profile"), 26, () => ShowProfile(true), out _, UIFactory.ButtonStyle.Outline);

            BuildSettings();
            BuildStyle();
            BuildProfile();
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

            // The song's twist, next to the EQ bars.
            _heroTwist = UIFactory.Label(UIFactory.Rect(hero, "Twist", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(228, 30), new Vector2(-488, 40)),
                string.Empty, 21, UIFactory.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            _heroTwist.resizeTextForBestFit = true;
            _heroTwist.resizeTextMinSize = 14;
            _heroTwist.resizeTextMaxSize = 21;

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

            bool testModeAllowed = AdsConfig.Instance.allowTestMode;
            _settingsCard = UIFactory.At(_settings, "Card", new Vector2(0.5f, 0f), new Vector2(0, 330), new Vector2(1000, testModeAllowed ? 1076 : 960));
            var cardImg = UIFactory.Card(_settingsCard);
            cardImg.raycastTarget = true;
            _settingsCard.gameObject.AddComponent<ClickBlocker>(); // taps on the card must not reach the close button behind it
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
            if (testModeAllowed) _toggles[4] = MakeToggle(stack, "test mode  (unlock all)", () => _game.ToggleTestMode());

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

        /// <summary>
        /// Style locker: tabs for balls and trails, each a grid of tiles. Ball tiles show live, spinning 3D
        /// previews (<see cref="SkinPreview"/>); trail tiles show a drawn icon of the effect. Each tile says
        /// whether it is equipped, free, or how many rewarded ads are left to unlock it.
        /// </summary>
        private void BuildStyle()
        {
            _style = UIFactory.Stretch(_menu, "Style");
            UIFactory.Image(_style, new Color(0.01f, 0f, 0.04f, 0.95f), null, true);
            _style.offsetMin = new Vector2(0, -300);
            _style.offsetMax = new Vector2(0, 300);

            _styleCard = UIFactory.Rect(_style, "Card", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _styleCard.offsetMin = new Vector2(-500, 330);
            _styleCard.offsetMax = new Vector2(500, -330);
            UIFactory.Card(_styleCard).raycastTarget = true;

            var header = UIFactory.Rect(_styleCard, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(-64, 84));
            var title = UIFactory.Label(UIFactory.Rect(header, "Title", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(12, 0), Vector2.zero),
                UIFactory.Spaced("style"), 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Cyan, UIFactory.Pink, true);
            var done = UIFactory.Button(header, "Done", UIFactory.Spaced("done"), 24, () => ShowStyle(false), out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)done.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(190, 76), new Vector2(1f, 0.5f));

            var tabs = UIFactory.Rect(_styleCard, "Tabs", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -128), new Vector2(-64, 96));
            UIFactory.Image(tabs, new Color(0.05f, 0.03f, 0.13f, 0.92f), UIFactory.Pill);
            var tabRow = UIFactory.Rect(tabs, "Row", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-14, -14));
            UIFactory.HStack(tabRow, 8);
            _tabBalls = UIFactory.Button(tabRow, "Balls", UIFactory.Spaced("balls"), 26, () => SetStyleTab(false), out _tabBallsText);
            _tabBallsGrad = UIFactory.Gradient(_tabBalls.targetGraphic, UIFactory.Cyan, new Color(0.6f, 0.3f, 1f), true);
            _tabTrails = UIFactory.Button(tabRow, "Trails", UIFactory.Spaced("trails"), 26, () => SetStyleTab(true), out _tabTrailsText);
            _tabTrailsGrad = UIFactory.Gradient(_tabTrails.targetGraphic, UIFactory.Pink, new Color(0.6f, 0.3f, 1f), true);

            _styleInfo = UIFactory.Label(UIFactory.Rect(_styleCard, "Info", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -236), new Vector2(-64, 36)),
                string.Empty, 20, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Scrollable grid area (fits short 16:9 phones as well as tall ones).
            var view = UIFactory.Rect(_styleCard, "View", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.offsetMin = new Vector2(30, 30);
            view.offsetMax = new Vector2(-30, -284);
            UIFactory.Image(view, new Color(0f, 0f, 0f, 0.001f), null, true); // catches drags
            view.gameObject.AddComponent<RectMask2D>();
            _styleScroll = view.gameObject.AddComponent<ScrollRect>();
            _styleScroll.horizontal = false;
            _styleScroll.vertical = true;
            _styleScroll.movementType = ScrollRect.MovementType.Elastic;
            _styleScroll.scrollSensitivity = 30f;
            _styleScroll.viewport = view;
            _ballGrid = MakeGrid(view, "BallGrid");
            _trailGrid = MakeGrid(view, "TrailGrid");

            Material chrome = _game.Ball != null ? _game.Ball.BodyMaterial : null;
            Mesh sphere = _game.Ball != null ? _game.Ball.BodyMesh : null;
            if (chrome != null && sphere != null) _preview = SkinPreview.Create(chrome, sphere);

            for (int i = 0; i < BallStyles.SkinCount; i++)
            {
                var skin = (BallSkin)i;
                var tile = MakeTile(_ballGrid, BallStyles.Name(skin), () => _game.SelectSkin(skin),
                    GameSettings.IsClearReward(skin) ? 0 : GameSettings.CosmeticAdsToUnlock, out RectTransform area);
                if (_preview != null)
                {
                    var raw = UIFactory.Rect(area, "Ball", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
                    raw.texture = _preview.Texture;
                    raw.uvRect = SkinPreview.UvFor(skin);
                    raw.raycastTarget = false;
                    tile.previewTint = raw;
                }
                LockIcon(tile, area);
                _skinTiles.Add(tile);
            }
            for (int i = 0; i < BallStyles.TrailCount; i++)
            {
                var trail = (TrailEffect)i;
                var tile = MakeTile(_trailGrid, BallStyles.Name(trail), () => _game.SelectTrail(trail),
                    GameSettings.IsClearReward(trail) ? 0 : GameSettings.CosmeticAdsToUnlock, out RectTransform area);
                var icon = UIFactory.Rect(area, "Icon", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var group = icon.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                TrailIcon(icon, trail);
                tile.previewTint = null;
                tile.lockIcon = null;
                LockIcon(tile, area);
                _trailTiles.Add(tile);
            }

            _style.gameObject.SetActive(false);
            SetStyleTab(false);
        }

        private static RectTransform MakeGrid(RectTransform view, string name)
        {
            var grid = UIFactory.Rect(view, name, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 0));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(290, 336);
            layout.spacing = new Vector2(22, 22);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(0, 0, 6, 24);
            var fit = grid.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return grid;
        }

        private LockerTile MakeTile(RectTransform grid, string label, UnityEngine.Events.UnityAction onClick, int adsNeeded, out RectTransform area)
        {
            var rt = UIFactory.Rect(grid, label, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var tile = new LockerTile();
            tile.bg = UIFactory.Image(rt, UIFactory.GlassTint, UIFactory.Glass, true);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = tile.bg;
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(onClick);
            rt.gameObject.AddComponent<PressFX>();

            area = UIFactory.Rect(rt, "Preview", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(206, 206));
            tile.name = UIFactory.Label(UIFactory.Rect(rt, "Name", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(-16, 34)),
                UIFactory.Spaced(label.ToLowerInvariant()), 21, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            tile.pipRow = UIFactory.Rect(rt, "Pips", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 56), new Vector2(adsNeeded * 20f, 12));
            tile.pips = new Image[adsNeeded];
            for (int i = 0; i < adsNeeded; i++)
            {
                var pip = UIFactory.Rect(tile.pipRow, "Pip" + i, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 20f + 2f, 0), new Vector2(16, 10));
                tile.pips[i] = UIFactory.Image(pip, UIFactory.Faint, UIFactory.Pill);
            }
            tile.status = UIFactory.Label(UIFactory.Rect(rt, "Status", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-16, 32)),
                string.Empty, 18, UIFactory.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            return tile;
        }

        /// <summary>A padlock drawn from UI shapes, centred on the preview.</summary>
        private static void LockIcon(LockerTile tile, RectTransform area)
        {
            var lockRt = UIFactory.Rect(area, "Lock", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(84, 96));
            var shackle = UIFactory.Rect(lockRt, "Shackle", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(54, 60));
            UIFactory.Image(shackle, new Color(1f, 0.85f, 0.4f, 1f), UIFactory.RingOutline);
            var body = UIFactory.Rect(lockRt, "Body", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(84, 62));
            UIFactory.Gradient(UIFactory.Image(body, Color.white, UIFactory.Rounded), UIFactory.Gold, new Color(1f, 0.55f, 0.2f), false);
            var hole = UIFactory.Rect(body, "Hole", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(12, 22));
            UIFactory.Image(hole, new Color(0.25f, 0.12f, 0.05f, 1f), UIFactory.Pill);
            tile.lockIcon = lockRt;
        }

        /// <summary>Drawn preview of a trail effect: the ball on the right, its trail streaming left.</summary>
        private static void TrailIcon(RectTransform area, TrailEffect t)
        {
            Vector2 ballPos = new Vector2(62, 0);
            switch (t)
            {
                case TrailEffect.Rainbow:
                {
                    Color[] c = { new Color(0.7f, 0.2f, 1f), new Color(0.2f, 0.6f, 1f), new Color(0.2f, 1f, 0.3f), new Color(1f, 0.8f, 0.1f), new Color(1f, 0.45f, 0.1f), new Color(1f, 0.2f, 0.2f) };
                    for (int i = 0; i < c.Length; i++)
                    {
                        var seg = UIFactory.Rect(area, "Seg" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-88 + i * 26, 0), new Vector2(28, 14 + i * 3));
                        UIFactory.Image(seg, new Color(c[i].r, c[i].g, c[i].b, 0.35f + 0.11f * i));
                    }
                    break;
                }
                case TrailEffect.Comet:
                {
                    var tail = UIFactory.Rect(area, "Tail", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24, 0), new Vector2(170, 38));
                    UIFactory.Gradient(UIFactory.Image(tail, Color.white, UIFactory.Pill), new Color(0.7f, 0.05f, 0.02f, 0f), new Color(1f, 0.75f, 0.25f, 1f), true);
                    float[,] embers = { { -70, 26, 10 }, { -40, -24, 8 }, { -88, -12, 7 }, { -10, 30, 6 }, { -60, -30, 6 } };
                    for (int i = 0; i < embers.GetLength(0); i++)
                    {
                        var e = UIFactory.Rect(area, "Ember" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(embers[i, 0], embers[i, 1]), new Vector2(embers[i, 2], embers[i, 2]));
                        UIFactory.Image(e, new Color(1f, 0.55f, 0.15f, 0.9f), UIFactory.Circle);
                    }
                    break;
                }
                case TrailEffect.Sparkle:
                {
                    var tail = UIFactory.Rect(area, "Tail", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24, 0), new Vector2(160, 10));
                    UIFactory.Gradient(UIFactory.Image(tail, Color.white, UIFactory.Pill), new Color(0.2f, 0.9f, 1f, 0f), new Color(1f, 1f, 1f, 0.9f), true);
                    float[,] stars = { { -80, 22, 16 }, { -50, -20, 12 }, { -20, 26, 10 }, { -95, -18, 9 }, { 0, -24, 8 } };
                    for (int i = 0; i < stars.GetLength(0); i++)
                    {
                        float sz = stars[i, 2];
                        var a = UIFactory.Rect(area, "StarA" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(stars[i, 0], stars[i, 1]), new Vector2(sz, sz * 0.25f));
                        UIFactory.Image(a, new Color(1f, 1f, 1f, 0.95f));
                        var b = UIFactory.Rect(area, "StarB" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(stars[i, 0], stars[i, 1]), new Vector2(sz * 0.25f, sz));
                        UIFactory.Image(b, new Color(1f, 1f, 1f, 0.95f));
                    }
                    break;
                }
                case TrailEffect.Pixel:
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float size = 30 - i * 3.5f;
                        var sq = UIFactory.Rect(area, "Px" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26 - i * 26, (i % 2 == 0 ? 4 : -4)), new Vector2(size, size));
                        Color c = i % 2 == 0 ? UIFactory.Cyan : UIFactory.Pink;
                        UIFactory.Image(sq, new Color(c.r, c.g, c.b, 1f - i * 0.14f));
                    }
                    break;
                }
                case TrailEffect.Electric:
                {
                    Vector2[] pts = { new Vector2(-96, 6), new Vector2(-70, -18), new Vector2(-46, 16), new Vector2(-22, -14), new Vector2(4, 12), new Vector2(34, -4) };
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        Vector2 a = pts[i], b = pts[i + 1];
                        Vector2 mid = (a + b) * 0.5f;
                        float len = (b - a).magnitude;
                        float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                        var glowSeg = UIFactory.Rect(area, "Glow" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), mid, new Vector2(len + 4, 12));
                        glowSeg.localRotation = Quaternion.Euler(0, 0, ang);
                        UIFactory.Image(glowSeg, new Color(0.3f, 0.8f, 1f, 0.3f));
                        var seg = UIFactory.Rect(area, "Bolt" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), mid, new Vector2(len + 2, 4));
                        seg.localRotation = Quaternion.Euler(0, 0, ang);
                        UIFactory.Image(seg, new Color(0.85f, 0.97f, 1f, 1f));
                    }
                    break;
                }
                case TrailEffect.Aurora:
                {
                    // Bands of northern light: green, cyan, violet and pink ribbons, with a few stars.
                    Color[] c = { new Color(1f, 0.35f, 0.8f), new Color(0.55f, 0.35f, 1f), new Color(0.2f, 0.85f, 1f), new Color(0.25f, 1f, 0.55f) };
                    for (int i = 0; i < c.Length; i++)
                    {
                        var band = UIFactory.Rect(area, "Band" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(-70 + i * 22, (i % 2 == 0 ? 6 : -6)), new Vector2(120, 40 - i * 4));
                        UIFactory.Gradient(UIFactory.Image(band, Color.white, UIFactory.Pill), new Color(c[i].r, c[i].g, c[i].b, 0f), new Color(c[i].r, c[i].g, c[i].b, 0.75f), true);
                    }
                    float[,] stars = { { -86, 30, 8 }, { -40, -32, 6 }, { -8, 34, 7 } };
                    for (int i = 0; i < stars.GetLength(0); i++)
                    {
                        var st = UIFactory.Rect(area, "Star" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(stars[i, 0], stars[i, 1]), new Vector2(stars[i, 2], stars[i, 2]));
                        UIFactory.Image(st, new Color(1f, 1f, 1f, 0.9f), UIFactory.Circle);
                    }
                    break;
                }
                case TrailEffect.Ghost:
                {
                    for (int i = 3; i >= 0; i--)
                    {
                        var g = UIFactory.Rect(area, "Ghost" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ballPos.x - 30 - i * 30, 0), new Vector2(46 - i * 3, 46 - i * 3));
                        UIFactory.Image(g, new Color(0.3f, 0.9f, 1f, 0.42f - i * 0.09f), UIFactory.Circle);
                    }
                    break;
                }
                default:
                {
                    var tail = UIFactory.Rect(area, "Tail", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24, 0), new Vector2(170, 26));
                    UIFactory.Gradient(UIFactory.Image(tail, Color.white, UIFactory.Pill), new Color(0.9f, 0.2f, 0.9f, 0f), new Color(1f, 1f, 1f, 0.95f), true);
                    break;
                }
            }
            // The ball itself, with a small highlight.
            var ball = UIFactory.Rect(area, "Ball", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ballPos, new Vector2(50, 50));
            UIFactory.Gradient(UIFactory.Image(ball, Color.white, UIFactory.Circle), new Color(0.95f, 0.97f, 1f), new Color(0.45f, 0.5f, 0.65f), false);
            var hl = UIFactory.Rect(ball, "Highlight", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-8, 9), new Vector2(14, 10));
            UIFactory.Image(hl, new Color(1f, 1f, 1f, 0.9f), UIFactory.Circle);
        }

        private void SetStyleTab(bool trails)
        {
            _styleTrails = trails;
            _ballGrid.gameObject.SetActive(!trails);
            _trailGrid.gameObject.SetActive(trails);
            _styleScroll.content = trails ? _trailGrid : _ballGrid;
            _styleScroll.verticalNormalizedPosition = 1f;
            _tabBallsGrad.enabled = !trails;
            _tabTrailsGrad.enabled = trails;
            UIFactory.TintButton(_tabBalls, trails ? new Color(1f, 1f, 1f, 0f) : Color.white);
            UIFactory.TintButton(_tabTrails, trails ? Color.white : new Color(1f, 1f, 1f, 0f));
            _tabBallsText.color = trails ? UIFactory.Dim : Color.white;
            _tabTrailsText.color = trails ? Color.white : UIFactory.Dim;
            if (_preview != null) _preview.Active = _style.gameObject.activeSelf && !trails;
            RefreshLocker();
        }

        private void RefreshLocker()
        {
            if (_style == null) return;
            bool test = GameSettings.TestMode;
            _styleInfo.text = test
                ? UIFactory.Spaced("test mode: everything unlocked")
                : UIFactory.Spaced("unlock each with " + GameSettings.CosmeticAdsToUnlock + " ads, or win it");
            for (int i = 0; i < _skinTiles.Count; i++)
            {
                var skin = (BallSkin)i;
                UpdateTile(_skinTiles[i], GameSettings.IsUnlocked(skin), GameSettings.BallSkin == skin, skin == BallSkin.Chrome, GameSettings.AdsWatched(skin),
                    GameSettings.IsClearReward(skin) ? "clear on hard" : null);
            }
            for (int i = 0; i < _trailTiles.Count; i++)
            {
                var trail = (TrailEffect)i;
                UpdateTile(_trailTiles[i], GameSettings.IsUnlocked(trail), GameSettings.Trail == trail, trail == TrailEffect.Classic, GameSettings.AdsWatched(trail),
                    GameSettings.IsClearReward(trail) ? "clear on normal" : null);
            }
        }

        /// <param name="earnBy">For a clear reward (no ads): how to win it; null for items unlocked with ads.</param>
        private static void UpdateTile(LockerTile tile, bool unlocked, bool equipped, bool free, int watched, string earnBy = null)
        {
            tile.bg.color = equipped ? new Color(0.25f, 0.75f, 1f, 1f) : UIFactory.GlassTint;
            if (tile.previewTint != null) tile.previewTint.color = unlocked ? Color.white : new Color(0.45f, 0.45f, 0.5f, 1f);
            if (tile.lockIcon != null) tile.lockIcon.gameObject.SetActive(!unlocked);
            tile.pipRow.gameObject.SetActive(!unlocked);
            for (int i = 0; i < tile.pips.Length; i++) tile.pips[i].color = i < watched ? UIFactory.Gold : new Color(1f, 1f, 1f, 0.18f);
            if (equipped)
            {
                tile.status.text = UIFactory.Spaced("equipped");
                tile.status.color = Color.white;
            }
            else if (unlocked)
            {
                tile.status.text = UIFactory.Spaced(free ? "free" : "tap to equip");
                tile.status.color = UIFactory.Cyan;
            }
            else if (earnBy != null)
            {
                tile.status.text = UIFactory.Spaced(earnBy);
                tile.status.color = UIFactory.Pink;
            }
            else
            {
                tile.status.text = UIFactory.Spaced("watch ad") + "  " + watched + "/" + tile.pips.Length;
                tile.status.color = UIFactory.Gold;
            }
        }

        private void ShowStyle(bool on)
        {
            if (_style.gameObject.activeSelf == on) return;
            _style.gameObject.SetActive(on);
            _sheet.gameObject.SetActive(!on);
            _styleOpened = Time.unscaledTime;
            if (_preview != null) _preview.Active = on && !_styleTrails;
            if (on) RefreshLocker();
        }

        /// <summary>Short message over the menu (unlock progress, ad not ready...).</summary>
        public void ShowToast(string text)
        {
            if (_toast == null) return;
            _toastText.text = text;
            _toastTime = Time.unscaledTime;
            _toast.gameObject.SetActive(true);
        }

        private void BuildToast()
        {
            _toast = UIFactory.At(_safe, "Toast", new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(760, 96));
            var group = _toast.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            UIFactory.Image(_toast, new Color(0.08f, 0.05f, 0.2f, 0.94f), UIFactory.Pill);
            UIFactory.Image(UIFactory.Stretch(_toast, "Edge"), new Color(1f, 0.85f, 0.35f, 0.9f), UIFactory.PillOutline);
            _toastText = UIFactory.Label(_toast, "Text", string.Empty, 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _toast.gameObject.SetActive(false);
        }

        private void AnimateToast()
        {
            if (_toast == null || !_toast.gameObject.activeSelf) return;
            float age = Time.unscaledTime - _toastTime;
            if (age > 2.2f)
            {
                _toast.gameObject.SetActive(false);
                return;
            }
            var group = _toast.GetComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((2.2f - age) / 0.4f);
            _toast.localScale = Vector3.one * (1f + 0.12f * Mathf.Clamp01(1f - age / 0.15f));
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
                bool unlocked = GameSettings.IsUnlocked(song);
                int watched = GameSettings.AdsWatched(song);
                _heroIndex.text = UIFactory.Spaced("song") + "   " + (sel + 1) + " / " + count + (unlocked ? string.Empty : "    " + UIFactory.Spaced("locked"));
                _playText.text = unlocked ? UIFactory.Spaced("play") : UIFactory.Spaced("watch ad to unlock") + "   " + watched + " / " + GameSettings.SongAdsToUnlock;
                _playText.fontSize = unlocked ? 60 : 32;
                _heroTitle.text = song.title.ToUpperInvariant();
                _heroInfo.text = song.IsAnalyzed
                    ? song.bpm.ToString("F0", CultureInfo.InvariantCulture) + " BPM   ·   " + FormatTime(song.duration) + "   ·   " + BeatAnalyzer.KeyName(song.keyRoot, song.keyMinor).ToUpperInvariant()
                    : "not analysed: run DropBeat > Analyze All Songs";
                SongTwist twist = SongTwists.For(song);
                _heroTwist.text = twist == SongTwist.None ? string.Empty
                    : "<color=#" + UIFactory.Hex(SongTwists.Tint(twist)) + ">" + UIFactory.Spaced("twist") + "  " + SongTwists.Title(twist) + "</color>   " + SongTwists.Blurb(twist);
                _heroBg.color = new Color(accent.r * 0.45f + 0.18f, accent.g * 0.35f + 0.12f, accent.b * 0.4f + 0.38f, 0.96f);
                _heroStripe.color = accent;
                foreach (var bar in _heroEqBars) bar.color = accent;

                var best = GameSettings.GetBest(song, _game.SelectedDifficulty);
                _heroBadgeText.text = best.Exists ? best.rank : "-";
                _heroBadge.color = best.Exists ? RankColor(best.rank) : UIFactory.Faint;
                _heroBadgeText.color = best.Exists ? RankColor(best.rank) : UIFactory.Faint;

                int notes = _game.NoteCount(_game.SelectedDifficulty);
                RefreshMenuStars(song, best);
                _infoText.text = MenuInfoLine(notes, best);
            }

            Color[] diffColors = { DiffEasy, DiffNormal, DiffHard };
            for (int d = 0; d < 3; d++)
            {
                bool on = (int)_game.SelectedDifficulty == d;
                _diffGradients[d].enabled = on;
                UIFactory.TintButton(_diffButtons[d], on ? Color.white : new Color(1f, 1f, 1f, 0f));
                _diffTexts[d].color = on ? Color.white : diffColors[d] * 0.9f;
            }

            RefreshLocker();
            SetToggle(_toggles[0], GameSettings.Autoplay);
            SetToggle(_toggles[1], GameSettings.AssistTick);
            SetToggle(_toggles[2], GameSettings.HitSound);
            SetToggle(_toggles[3], GameSettings.NoFail);
            if (_toggles[4] != null) SetToggle(_toggles[4], GameSettings.TestMode);
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
            if (_style.gameObject.activeSelf)
            {
                float a = Mathf.Clamp01((time - _styleOpened) / 0.25f);
                float e = 1f - (1f - a) * (1f - a) * (1f - a);
                _styleCard.anchoredPosition = new Vector2(0, Mathf.Lerp(-1600f, 0f, e));
            }
            AnimateProfile(time);
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

            // Shade behind the top panel so it reads over bright rings and the sun (runs up under the notch).
            var shade = UIFactory.Rect(_hud, "TopShade", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 220), new Vector2(0, 520));
            UIFactory.Gradient(UIFactory.Image(shade, Color.white), new Color(0.02f, 0f, 0.07f, 0.75f), new Color(0.02f, 0f, 0.07f, 0f), false);

            // One glass panel holds everything at the top: pause (with a song-progress ring), song,
            // score, and the life / overdrive bars.
            var panel = UIFactory.Rect(_hud, "TopPanel", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(-40, 214));
            UIFactory.Card(panel, new Color(0.22f, 0.16f, 0.5f, 0.78f));

            var progress = UIFactory.Rect(panel, "Progress", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 2), new Vector2(-60, 4));
            UIFactory.Image(progress, new Color(1f, 1f, 1f, 0.06f), UIFactory.Pill);
            _progressFill = UIFactory.Rect(progress, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Gradient(UIFactory.Image(_progressFill, Color.white, UIFactory.Pill), UIFactory.Cyan, UIFactory.Pink, true);
            BuildSectionBar(panel);

            // Pause, wrapped in a ring that fills as the song plays.
            _pauseButton = UIFactory.At(panel, "PauseBtn", new Vector2(0, 1), new Vector2(26, -22), new Vector2(104, 104));
            var ringTrack = UIFactory.Rect(_pauseButton, "RingTrack", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(122, 122));
            UIFactory.Image(ringTrack, new Color(1f, 1f, 1f, 0.12f), UIFactory.RingOutline);
            var ring = UIFactory.Rect(_pauseButton, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(122, 122));
            _progressRing = UIFactory.Image(ring, UIFactory.Cyan, UIFactory.RingOutline);
            _progressRing.type = Image.Type.Filled;
            _progressRing.fillMethod = Image.FillMethod.Radial360;
            _progressRing.fillOrigin = (int)Image.Origin360.Top;
            _progressRing.fillClockwise = true;
            _progressRing.fillAmount = 0f;
            var pbImg = UIFactory.Image(_pauseButton, new Color(0.12f, 0.08f, 0.28f, 0.95f), UIFactory.Circle, true);
            var pbtn = _pauseButton.gameObject.AddComponent<Button>();
            pbtn.targetGraphic = pbImg;
            pbtn.navigation = new Navigation { mode = Navigation.Mode.None };
            pbtn.onClick.AddListener(() => _game.PauseGame());
            _pauseButton.gameObject.AddComponent<PressFX>();
            UIFactory.Image(UIFactory.Rect(_pauseButton, "BarL", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-10, 0), new Vector2(11, 36)), Color.white, UIFactory.Pill);
            UIFactory.Image(UIFactory.Rect(_pauseButton, "BarR", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(11, 36)), Color.white, UIFactory.Pill);

            _hudTopLeft = UIFactory.At(panel, "TopLeft", new Vector2(0, 1), new Vector2(156, -24), new Vector2(430, 100));
            _hudTitle = UIFactory.Label(UIFactory.Rect(_hudTopLeft, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 46)), "", 32, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _hudTitle.resizeTextForBestFit = true;
            _hudTitle.resizeTextMinSize = 20;
            _hudTitle.resizeTextMaxSize = 32;
            _hudTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            var chipRt = UIFactory.Rect(_hudTopLeft, "DiffChip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -56), new Vector2(128, 36));
            _diffChip = UIFactory.Image(chipRt, DiffNormal, UIFactory.Pill);
            _hudDiff = UIFactory.Label(chipRt, "Text", "", 19, new Color(0.05f, 0.02f, 0.1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _hudBpm = UIFactory.Label(UIFactory.Rect(_hudTopLeft, "Bpm", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(144, -56), new Vector2(-144, 36)), "", 22, UIFactory.Dim, TextAnchor.MiddleLeft);

            _hudTopRight = UIFactory.At(panel, "TopRight", new Vector2(1, 1), new Vector2(-30, -16), new Vector2(400, 130));
            _scoreText = UIFactory.Label(UIFactory.Rect(_hudTopRight, "Score", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 78)), "0", 68, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Gradient(_scoreText, Color.white, new Color(0.45f, 0.9f, 1f), false);
            var scoreShadow = _scoreText.gameObject.AddComponent<Shadow>();
            scoreShadow.effectColor = new Color(0.1f, 0f, 0.3f, 0.6f);
            scoreShadow.effectDistance = new Vector2(0f, -3f);
            _accText = UIFactory.Label(UIFactory.Rect(_hudTopRight, "Acc", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-112, -84), new Vector2(-112, 36)), "100.00%", 26, UIFactory.TextColor, TextAnchor.MiddleRight);
            var mult = UIFactory.Rect(_hudTopRight, "Mult", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -84), new Vector2(98, 36));
            UIFactory.Image(mult, new Color(1f, 0.8f, 0.3f, 0.95f), UIFactory.PillOutline);
            _multText = UIFactory.Label(mult, "Text", "x1.0", 21, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Life (left) and overdrive (right): segmented neon bars side by side along the panel's bottom.
            var lifeRoot = UIFactory.Rect(panel, "Life", new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(30, 26), new Vector2(-46, 40));
            var heart = UIFactory.Label(UIFactory.Rect(lifeRoot, "Icon", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(40, 40)), "\u2665", 30, UIFactory.Pink, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hb = UIFactory.Rect(lifeRoot, "Track", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(-48, 16));
            UIFactory.Image(hb, new Color(1f, 1f, 1f, 0.09f), UIFactory.Pill);
            _healthFill = UIFactory.Rect(hb, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _healthImage = UIFactory.Image(_healthFill, Color.white, UIFactory.Pill);
            UIFactory.Gradient(_healthImage, UIFactory.Green, UIFactory.Cyan, true);
            Segment(hb, 10);

            var odRoot = UIFactory.Rect(panel, "Overdrive", new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(16, 26), new Vector2(-46, 40));
            _odLabel = UIFactory.Label(UIFactory.Rect(odRoot, "Label", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(60, 40)), "OD", 22, UIFactory.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            var odTrack = UIFactory.Rect(odRoot, "Track", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(-60, 16));
            UIFactory.Image(odTrack, new Color(1f, 1f, 1f, 0.09f), UIFactory.Pill);
            _odFill = UIFactory.Rect(odTrack, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _odFillImage = UIFactory.Image(_odFill, Color.white, UIFactory.Pill);
            UIFactory.Gradient(_odFillImage, UIFactory.Gold, UIFactory.Pink, true);
            Segment(odTrack, 8);
            _odBadge = UIFactory.Rect(odRoot, "Badge", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 30), new Vector2(84, 34));
            UIFactory.Gradient(UIFactory.Image(_odBadge, Color.white, UIFactory.Pill), UIFactory.Gold, UIFactory.Pink, true);
            UIFactory.Label(_odBadge, "Text", "x2", 22, new Color(0.08f, 0.02f, 0.1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _odBadge.gameObject.SetActive(false);

            _comboRoot = UIFactory.At(_hud, "Combo", new Vector2(0.5f, 1), new Vector2(0, -240), new Vector2(520, 180));
            UIFactory.Halo(_comboRoot, new Color(0.7f, 0.2f, 1f, 0.18f), 1.1f);
            _comboText = UIFactory.Label(UIFactory.Rect(_comboRoot, "N", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 130)), "", 120, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _comboGrad = UIFactory.Gradient(_comboText, Color.white, new Color(1f, 0.45f, 0.9f), false);
            var cOut = _comboText.gameObject.AddComponent<Outline>();
            cOut.effectColor = new Color(0.6f, 0.1f, 0.8f, 0.5f);
            cOut.effectDistance = new Vector2(2f, -2f);
            _comboLabel = UIFactory.Label(UIFactory.Rect(_comboRoot, "L", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -128), new Vector2(0, 30)), "", 20, UIFactory.Pink, TextAnchor.MiddleCenter, FontStyle.Bold);

            _countIn = UIFactory.Label(UIFactory.At(_hud, "CountIn", new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(800, 240)), "", 180, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_countIn, Color.white, UIFactory.Cyan, false);
            _milestone = UIFactory.Label(UIFactory.At(_hud, "Milestone", new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 120)), "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(_milestone, UIFactory.Gold, UIFactory.Pink, true);

            // Twist badge above the hit error meter: the song's trick, or its meter (gold, boss, overtime).
            _twistPill = UIFactory.At(_hud, "Twist", new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(600, 56));
            UIFactory.Image(_twistPill, new Color(0.08f, 0.05f, 0.2f, 0.62f), UIFactory.Pill);
            _twistEdge = UIFactory.Image(UIFactory.Stretch(_twistPill, "Edge"), Color.white, UIFactory.PillOutline);
            _twistText = UIFactory.Label(UIFactory.Rect(_twistPill, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(-36, -8)),
                string.Empty, 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _twistText.resizeTextForBestFit = true;
            _twistText.resizeTextMinSize = 14;
            _twistText.resizeTextMaxSize = 22;
            _twistTrack = UIFactory.Rect(_twistPill, "Track", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 7), new Vector2(-64, 7));
            UIFactory.Image(_twistTrack, new Color(1f, 1f, 1f, 0.12f), UIFactory.Pill);
            _twistFill = UIFactory.Rect(_twistTrack, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _twistFillImage = UIFactory.Image(_twistFill, Color.white, UIFactory.Pill);
            _twistTrack.gameObject.SetActive(false);

            // Hit error meter at the bottom, labels above its ends so nothing crowds the screen edges.
            var meterBg = UIFactory.At(_hud, "MeterBg", new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(MeterHalfWidth * 2f + 90f, 88));
            UIFactory.Image(meterBg, new Color(0.08f, 0.05f, 0.2f, 0.55f), UIFactory.Pill);
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
            UIFactory.Label(UIFactory.Rect(_meter, "Early", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(0, 14), new Vector2(160, 26)), UIFactory.Spaced("early"), 17, UIFactory.Cyan * 0.85f, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Label(UIFactory.Rect(_meter, "Late", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 14), new Vector2(160, 26)), UIFactory.Spaced("late"), 17, UIFactory.Pink * 0.85f, TextAnchor.MiddleRight, FontStyle.Bold);

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
            BuildFeelHud();
        }

        /// <summary>Thin dark gaps across a bar so it reads as neon segments.</summary>
        private static void Segment(RectTransform track, int count)
        {
            for (int i = 1; i < count; i++)
            {
                float x = i / (float)count;
                var gap = UIFactory.Rect(track, "Gap" + i, new Vector2(x, 0), new Vector2(x, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 4));
                UIFactory.Image(gap, new Color(0.1f, 0.06f, 0.24f, 0.95f));
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
            _shownScore = 0f;
            _comboTier = -1;
            _progressRing.color = Ldr(song.accent);
            UpdateOverdrive(0f, false);
            ResetFeelHud();
        }

        /// <param name="tier">Streak tier 0..4 from the game (below 0: none yet).</param>
        /// <param name="beatPhase">0..1 position inside the current beat, so pulses land on the beat (below 0: free-running).</param>
        public void UpdateHud(ScoreState s, float progress, float meanErrorMs, JudgeWindows windows, int tier = -1, float beatPhase = -1f)
        {
            _beatPh = beatPhase;
            _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _progressRing.fillAmount = Mathf.Clamp01(progress);
            _healthFill.anchorMax = new Vector2(Mathf.Clamp01(s.Health), 1f);
            float danger = Mathf.InverseLerp(0.35f, 0.1f, s.Health);
            float beat = beatPhase >= 0f ? Mathf.Exp(-6f * beatPhase) : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
            _healthImage.color = Color.Lerp(Color.Lerp(UIFactory.Red, Color.white, Mathf.InverseLerp(0.2f, 0.6f, s.Health)), UIFactory.Red * (0.6f + 0.4f * beat), danger);
            // Score counts up smoothly instead of jumping.
            if (s.Score < _shownScore) _shownScore = s.Score;
            _shownScore = Mathf.Lerp(_shownScore, s.Score, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            if (s.Score - _shownScore < 1f) _shownScore = s.Score;
            _scoreText.text = Mathf.RoundToInt(_shownScore).ToString("N0", CultureInfo.InvariantCulture);
            float sp = Mathf.Clamp01(1f - (Time.unscaledTime - _scorePunchAt) / 0.12f);
            _scoreText.rectTransform.localScale = Vector3.one * (1f + 0.08f * sp);
            _accText.text = (s.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
            _multText.text = "x" + s.ComboMultiplier.ToString("F1", CultureInfo.InvariantCulture);

            bool showCombo = s.Combo >= 3;
            _comboText.text = showCombo ? s.Combo.ToString() : string.Empty;
            // Combo colour and word follow the streak ladder (COMBO, GROOVE, FEVER, BLAZE, LEGEND).
            tier = Mathf.Clamp(tier, 0, 4);
            if (tier != _comboTier)
            {
                _comboTier = tier;
                Color tc = TierColor(tier);
                _comboGrad.Set(tier == 4 ? Color.white : Color.Lerp(Color.white, tc, 0.15f), tier == 0 ? new Color(1f, 0.45f, 0.9f) : tier == 4 ? UIFactory.Gold : tc, false);
            }
            _comboLabel.text = showCombo ? UIFactory.Spaced(TierNames[tier]) : string.Empty;
            _comboLabel.color = tier == 0 ? UIFactory.Pink : TierColor(tier);
            _comboPunch = Mathf.MoveTowards(_comboPunch, 0f, Time.unscaledDeltaTime * 4f);
            float tierPop = Mathf.Clamp01(1f - (Time.unscaledTime - _tierAt) / 0.3f);
            _comboRoot.localScale = Vector3.one * (1f + _punchAmt * _comboPunch + 0.45f * tierPop * tierPop);
            _comboText.color = Color.Lerp(Color.white, Color.white * 2f, Mathf.Clamp01(1f - (Time.unscaledTime - _tierAt) / 0.15f));

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

            AnimateJudgements(now);
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

        public void UpdateOverdrive(float meter, bool active, float beatPhase = -1f)
        {
            _odFill.anchorMax = new Vector2(Mathf.Clamp01(meter), 1f);
            float wave = beatPhase >= 0f ? 2f * Mathf.Exp(-6f * beatPhase) - 1f : Mathf.Sin(Time.unscaledTime * 10f);
            float pulse = active ? 0.75f + 0.25f * wave : 1f;
            _odFillImage.color = new Color(pulse, pulse, pulse, 1f);
            _odLabel.color = active ? UIFactory.Gold : meter >= 0.999f ? Color.white : UIFactory.Dim;
            _odBadge.gameObject.SetActive(active);
            _multText.transform.parent.gameObject.SetActive(!active); // the x2 badge sits where the multiplier pill is
            if (active) _odBadge.localScale = Vector3.one * (1f + 0.08f * wave);
        }

        /// <summary>Combo bump on a hit, bigger for better grades.</summary>
        public void PunchCombo(Grade g = Grade.Perfect)
        {
            _comboPunch = 1f;
            _punchAmt = g == Grade.Perfect ? 0.24f : g == Grade.Great ? 0.14f : 0.07f;
        }

        public void ShowMilestone(string text)
        {
            _milestone.text = text;
            _milestoneTime = Time.unscaledTime;
        }

        /// <summary>The song's twist for the HUD badge (empty title hides it).</summary>
        public void SetTwist(string title, string blurb, Color tint)
        {
            _twistTint = tint;
            _twistDefault = string.IsNullOrEmpty(title) ? string.Empty
                : "<color=#" + UIFactory.Hex(tint) + ">" + title + "</color>   <color=#" + UIFactory.Hex(UIFactory.Dim) + ">" + blurb + "</color>";
            _twistPill.gameObject.SetActive(!string.IsNullOrEmpty(title));
            _twistEdge.color = new Color(tint.r, tint.g, tint.b, 0.55f);
            SetTwistMeter(null, -1f);
        }

        /// <summary>Twist meter: text (null = the twist's name and blurb) and a 0..1 bar (below 0 hides it).</summary>
        public void SetTwistMeter(string text, float fill)
        {
            _twistText.text = text == null ? _twistDefault : "<color=#" + UIFactory.Hex(_twistTint) + ">" + text + "</color>";
            bool bar = fill >= 0f;
            _twistTrack.gameObject.SetActive(bar);
            if (bar)
            {
                _twistFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
                _twistFillImage.color = _twistTint;
            }
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
            UIFactory.Image(FullDim(_pause), new Color(0.01f, 0f, 0.04f, 0.7f), null, true);
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
            UIFactory.Image(FullDim(_results), new Color(0.01f, 0f, 0.04f, 0.6f), null, true);
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
            _resButtons = row;
            BuildResultsReveal();
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
            UIFactory.Image(FullDim(_calibration), new Color(0.01f, 0f, 0.04f, 0.78f), null, true);
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
