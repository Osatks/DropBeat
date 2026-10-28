using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>All screens (menu, HUD, pause, results, calibration), built in code.</summary>
    public sealed class GameUI : MonoBehaviour
    {
        public enum Screen { Menu, Hud, Pause, Results, Calibration }

        private GameController _game;
        private Canvas _canvas;
        private RectTransform _root;

        private RectTransform _menu, _hud, _pause, _results, _calibration;

        // Menu
        private RectTransform _songList;
        private readonly List<Button> _songButtons = new List<Button>();
        private readonly List<Text> _songInfos = new List<Text>();
        private readonly Button[] _diffButtons = new Button[3];
        private readonly Text[] _diffTexts = new Text[3];
        private Text _bestText, _offsetText, _calibHint;
        private Text _autoText, _tickText, _hitText, _noFailText;
        private Text _songDetail;

        // HUD
        private RectTransform _progressFill, _healthFill;
        private Image _healthImage;
        private Text _hudTitle, _hudDiff, _scoreText, _accText, _multText, _comboText, _comboLabel, _countIn, _milestone, _autoBadge;
        private RectTransform _comboRoot;
        private RectTransform _pauseButton;
        private RectTransform _meter;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<float> _tickBorn = new List<float>();
        private int _nextTick;
        private RectTransform _meanMarker;
        private float _comboPunch;
        private float _milestoneTime = -10f;
        private float _meterHalfWidth = 260f;

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

        // Pause
        private Text _pauseTitle, _resumeCount;
        private RectTransform _pauseButtons;

        // Results
        private Text _resStatus, _resSong, _resRank, _resScore, _resAcc, _resCombo, _resCounts, _resTiming, _resRecord, _resSuggest;
        private Button _resApply;
        private Text _resApplyText;

        // Calibration
        private Image _calCircle;
        private Text _calStatus, _calValue;
        private Button _calSave;
        private float _calPulse;

        public Screen Current { get; private set; }

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
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)canvasGo.transform;

            BuildHud();
            BuildMenu();
            BuildPause();
            BuildResults();
            BuildCalibration();
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

        public void Show(Screen s)
        {
            Current = s;
            _menu.gameObject.SetActive(s == Screen.Menu);
            _hud.gameObject.SetActive(s == Screen.Hud || s == Screen.Pause || s == Screen.Results);
            _pause.gameObject.SetActive(s == Screen.Pause);
            _results.gameObject.SetActive(s == Screen.Results);
            _calibration.gameObject.SetActive(s == Screen.Calibration);
            if (s == Screen.Menu) RefreshMenu();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // ================================================================ MENU

        private void BuildMenu()
        {
            _menu = UIFactory.Stretch(_root, "Menu");

            var title = UIFactory.Label(UIFactory.Rect(_menu, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(1400, 140)),
                "D R O P B E A T", 112, UIFactory.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(title, new Color(1f, 0.3f, 0.9f, 0.85f), 3f);
            UIFactory.Label(UIFactory.Rect(_menu, "Sub", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(1400, 40)),
                "LAND THE BALL IN EVERY RING  -  RIGHT ON THE BEAT", 26, UIFactory.Dim, TextAnchor.MiddleCenter);

            // Song list
            var left = UIFactory.Rect(_menu, "Songs", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30, 0), new Vector2(760, 600));
            UIFactory.Image(left, UIFactory.PanelColor, UIFactory.Rounded);
            UIFactory.Label(UIFactory.Rect(left, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(-60, 50)),
                "SONGS", 30, UIFactory.Pink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _songList = UIFactory.Rect(left, "List", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(-40, -180));
            UIFactory.VStack(_songList, 14, 0);
            _songDetail = UIFactory.Label(UIFactory.Rect(left, "Detail", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(-60, 60)),
                string.Empty, 21, UIFactory.Dim, TextAnchor.LowerLeft);

            // Right panel
            var right = UIFactory.Rect(_menu, "Play", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30, 0), new Vector2(800, 600));
            UIFactory.Image(right, UIFactory.PanelColor, UIFactory.Rounded);
            var stack = UIFactory.Rect(right, "Stack", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-50, -40));
            UIFactory.VStack(stack, 10, 0);

            UIFactory.Layout(UIFactory.Label(stack, "DiffHeader", "DIFFICULTY", 30, UIFactory.Pink, TextAnchor.MiddleLeft, FontStyle.Bold), 44);
            var diffRow = UIFactory.Rect(stack, "DiffRow", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.HStack(diffRow, 14);
            UIFactory.Layout(diffRow, 90);
            string[] names = { "EASY", "NORMAL", "HARD" };
            for (int i = 0; i < 3; i++)
            {
                int d = i;
                _diffButtons[i] = UIFactory.Button(diffRow, names[i], names[i], 30, () => { _game.SelectDifficulty((Difficulty)d); }, out _diffTexts[i]);
            }

            _bestText = UIFactory.Label(stack, "Best", string.Empty, 26, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Layout(_bestText, 40);

            var play = UIFactory.Button(stack, "PlayButton", "PLAY", 54, () => _game.Play(), out Text playText, new Color(0f, 0.55f, 0.7f, 1f));
            UIFactory.Layout(play, 96);
            UIFactory.Glow(playText, new Color(0f, 0f, 0f, 0.5f), 2f);

            var opts = UIFactory.Rect(stack, "Options", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var grid = opts.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(361, 60);
            grid.spacing = new Vector2(14, 12);
            grid.childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Layout(opts, 132);
            UIFactory.Button(opts, "Auto", "", 24, () => _game.ToggleAutoplay(), out _autoText);
            UIFactory.Button(opts, "Tick", "", 24, () => _game.ToggleAssistTick(), out _tickText);
            UIFactory.Button(opts, "HitSound", "", 24, () => _game.ToggleHitSound(), out _hitText);
            UIFactory.Button(opts, "NoFail", "", 24, () => _game.ToggleNoFail(), out _noFailText);

            var offRow = UIFactory.Rect(stack, "OffsetRow", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.HStack(offRow, 10);
            UIFactory.Layout(offRow, 64);
            _offsetText = UIFactory.Label(offRow, "OffsetLabel", string.Empty, 24, UIFactory.TextColor, TextAnchor.MiddleLeft);
            UIFactory.Layout(_offsetText, 64, 236);
            AddSmallButton(offRow, "-5", () => _game.AdjustOffset(-5f));
            AddSmallButton(offRow, "-1", () => _game.AdjustOffset(-1f));
            AddSmallButton(offRow, "+1", () => _game.AdjustOffset(1f));
            AddSmallButton(offRow, "+5", () => _game.AdjustOffset(5f));
            var cal = UIFactory.Button(offRow, "Calibrate", "CALIBRATE", 24, () => _game.StartCalibration(), out _, new Color(0.45f, 0.1f, 0.4f, 1f));
            UIFactory.Layout(cal, 64, 176);

            _calibHint = UIFactory.Label(stack, "CalHint", string.Empty, 20, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Layout(_calibHint, 28);

            // Footer hints sit in the corners so the beat-bouncing ball stays visible in the middle.
            UIFactory.Label(UIFactory.Rect(_menu, "FooterLeft", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(44, 30), new Vector2(700, 60)),
                "HIT   SPACE / D F J K / CLICK / TAP\nPAUSE   ESC", 21, UIFactory.Dim, TextAnchor.LowerLeft);
            UIFactory.Label(UIFactory.Rect(_menu, "FooterRight", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44, 30), new Vector2(700, 60)),
                "PLAY   ENTER        SONG   UP / DOWN\nDIFFICULTY   LEFT / RIGHT", 21, UIFactory.Dim, TextAnchor.LowerRight);
        }

        private static void AddSmallButton(Transform parent, string label, UnityEngine.Events.UnityAction a)
        {
            var b = UIFactory.Button(parent, "B" + label, label, 24, a, out _);
            UIFactory.Layout(b, 64, 70);
        }

        public void RefreshMenu()
        {
            if (_game == null || _game.Library == null) return;
            var songs = _game.Library.songs;
            while (_songButtons.Count < songs.Count)
            {
                int index = _songButtons.Count;
                var b = UIFactory.Button(_songList, "Song" + index, string.Empty, 34, () => _game.SelectSong(index), out Text t);
                UIFactory.Layout(b, 112);
                t.alignment = TextAnchor.UpperLeft;
                t.rectTransform.offsetMin = new Vector2(26, 0);
                t.rectTransform.offsetMax = new Vector2(-20, -16);
                var info = UIFactory.Label(b.transform, "Info", string.Empty, 22, UIFactory.Dim, TextAnchor.LowerLeft);
                info.rectTransform.offsetMin = new Vector2(26, 16);
                info.rectTransform.offsetMax = new Vector2(-20, 0);
                _songButtons.Add(b);
                _songInfos.Add(info);
            }

            for (int i = 0; i < _songButtons.Count; i++)
            {
                bool exists = i < songs.Count && songs[i] != null;
                _songButtons[i].gameObject.SetActive(exists);
                if (!exists) continue;
                SongData s = songs[i];
                bool sel = i == _game.SelectedSong;
                var text = _songButtons[i].GetComponentInChildren<Text>();
                text.text = (sel ? "> " : string.Empty) + s.title.ToUpperInvariant();
                text.color = sel ? Color.white : UIFactory.TextColor;
                var best = GameSettings.GetBest(s, _game.SelectedDifficulty);
                _songInfos[i].text = s.IsAnalyzed
                    ? s.bpm.ToString("F0", CultureInfo.InvariantCulture) + " BPM   " + FormatTime(s.duration) +
                      (best.Exists ? "   BEST " + best.rank + "  " + (best.accuracy * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%" : string.Empty)
                    : "not analysed - run DropBeat > Analyze All Songs";
                SetButtonTint(_songButtons[i], sel ? new Color(0.0f, 0.42f, 0.55f, 1f) : UIFactory.ButtonColor);
            }

            SongData song = _game.CurrentSong;
            for (int d = 0; d < 3; d++)
            {
                bool sel = (int)_game.SelectedDifficulty == d;
                int count = song != null ? _game.NoteCount((Difficulty)d) : 0;
                string[] names = { "EASY", "NORMAL", "HARD" };
                _diffTexts[d].text = names[d] + "\n<size=20>" + count + " notes</size>";
                _diffTexts[d].supportRichText = true;
                SetButtonTint(_diffButtons[d], sel ? DiffColor((Difficulty)d) : UIFactory.ButtonColor);
            }

            if (song != null)
            {
                var best = GameSettings.GetBest(song, _game.SelectedDifficulty);
                _bestText.text = best.Exists
                    ? "BEST   " + best.rank + "   " + (best.accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%   " + best.score.ToString("N0", CultureInfo.InvariantCulture) + (best.fullCombo ? "   FC" : string.Empty)
                    : "NO RECORD YET";
                _songDetail.text = song.IsAnalyzed
                    ? "Beat grid " + song.bpm.ToString("F3", CultureInfo.InvariantCulture) + " BPM  -  " + song.beatTimes.Length + " beats  -  drift " + song.driftMs.ToString("F1", CultureInfo.InvariantCulture) + " ms"
                    : string.Empty;
            }

            _autoText.text = "AUTOPLAY: " + OnOff(GameSettings.Autoplay);
            _tickText.text = "ASSIST TICK: " + OnOff(GameSettings.AssistTick);
            _hitText.text = "HIT SOUND: " + OnOff(GameSettings.HitSound);
            _noFailText.text = "NO FAIL: " + OnOff(GameSettings.NoFail);
            _offsetText.text = "OFFSET " + GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
            _calibHint.text = GameSettings.Calibrated ? string.Empty : "Tip: press CALIBRATE once so taps match your speakers / headphones.";
        }

        private static Color DiffColor(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return new Color(0.15f, 0.55f, 0.3f, 1f);
                case Difficulty.Hard: return new Color(0.65f, 0.12f, 0.35f, 1f);
                default: return new Color(0.0f, 0.42f, 0.6f, 1f);
            }
        }

        private static void SetButtonTint(Button b, Color c)
        {
            var cb = b.colors;
            cb.normalColor = c;
            cb.highlightedColor = Color.Lerp(c, UIFactory.ButtonHot, 0.35f);
            cb.selectedColor = cb.highlightedColor;
            b.colors = cb;
        }

        private static string OnOff(bool v) => v ? "ON" : "OFF";

        private static string FormatTime(float s)
        {
            int m = Mathf.FloorToInt(s / 60f);
            int sec = Mathf.FloorToInt(s - m * 60f);
            return m + ":" + sec.ToString("00");
        }

        // ================================================================ HUD

        private void BuildHud()
        {
            _hud = UIFactory.Stretch(_root, "Hud");

            var progress = UIFactory.Rect(_hud, "Progress", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 8));
            UIFactory.Image(progress, new Color(1f, 1f, 1f, 0.08f));
            _progressFill = UIFactory.Rect(progress, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            UIFactory.Image(_progressFill, UIFactory.Cyan);

            var tl = UIFactory.Rect(_hud, "TopLeft", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -34), new Vector2(560, 140));
            _hudTitle = UIFactory.Label(UIFactory.Rect(tl, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 40)), "", 32, UIFactory.TextColor, TextAnchor.UpperLeft, FontStyle.Bold);
            _hudDiff = UIFactory.Label(UIFactory.Rect(tl, "Diff", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, -44), new Vector2(0, 30)), "", 22, UIFactory.Dim, TextAnchor.UpperLeft);
            var hb = UIFactory.Rect(tl, "Health", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -84), new Vector2(420, 18));
            UIFactory.Image(hb, new Color(1f, 1f, 1f, 0.1f), UIFactory.Rounded);
            _healthFill = UIFactory.Rect(hb, "Fill", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _healthImage = UIFactory.Image(_healthFill, UIFactory.Green, UIFactory.Rounded);

            var tr = UIFactory.Rect(_hud, "TopRight", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -26), new Vector2(600, 170));
            _scoreText = UIFactory.Label(UIFactory.Rect(tr, "Score", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 76)), "0", 70, Color.white, TextAnchor.UpperRight, FontStyle.Bold);
            UIFactory.Glow(_scoreText, new Color(0f, 0.8f, 1f, 0.5f), 2f);
            _accText = UIFactory.Label(UIFactory.Rect(tr, "Acc", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -82), new Vector2(0, 36)), "100.00%", 32, UIFactory.TextColor, TextAnchor.UpperRight);
            _multText = UIFactory.Label(UIFactory.Rect(tr, "Mult", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -122), new Vector2(0, 30)), "x1.0", 26, UIFactory.Gold, TextAnchor.UpperRight, FontStyle.Bold);

            _comboRoot = UIFactory.Rect(_hud, "Combo", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(400, 150));
            _comboText = UIFactory.Label(UIFactory.Rect(_comboRoot, "N", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100)), "", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(_comboText, new Color(1f, 0.3f, 0.9f, 0.7f), 3f);
            _comboLabel = UIFactory.Label(UIFactory.Rect(_comboRoot, "L", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -98), new Vector2(0, 30)), "COMBO", 24, UIFactory.Pink, TextAnchor.MiddleCenter, FontStyle.Bold);

            _countIn = UIFactory.Label(UIFactory.Rect(_hud, "CountIn", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(800, 200)), "", 150, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(_countIn, new Color(0f, 0.9f, 1f, 0.8f), 4f);
            _milestone = UIFactory.Label(UIFactory.Rect(_hud, "Milestone", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1000, 120)), "", 64, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(_milestone, new Color(1f, 0.4f, 0f, 0.7f), 3f);

            // Hit error meter: shows exactly how early/late each tap was.
            _meter = UIFactory.Rect(_hud, "Meter", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(_meterHalfWidth * 2f, 22));
            AddZone(_meter, 1f, new Color(0.3f, 1f, 0.4f, 0.22f));
            AddZone(_meter, (float)(JudgeWindows.For(Difficulty.Normal).great / JudgeWindows.For(Difficulty.Normal).good), new Color(0.2f, 0.9f, 1f, 0.3f));
            AddZone(_meter, (float)(JudgeWindows.For(Difficulty.Normal).perfect / JudgeWindows.For(Difficulty.Normal).good), new Color(1f, 0.85f, 0.3f, 0.4f));
            var center = UIFactory.Rect(_meter, "Center", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3, 14));
            UIFactory.Image(center, Color.white);
            for (int i = 0; i < 28; i++)
            {
                var t = UIFactory.Rect(_meter, "Tick" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 34));
                var img = UIFactory.Image(t, Color.clear);
                _ticks.Add(img);
                _tickBorn.Add(-100f);
            }
            _meanMarker = UIFactory.Rect(_meter, "Mean", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(14, 14));
            UIFactory.Image(_meanMarker, Color.white, UIFactory.Circle);
            UIFactory.Label(UIFactory.Rect(_meter, "Early", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(100, 30)), "EARLY", 18, UIFactory.Dim, TextAnchor.MiddleRight);
            UIFactory.Label(UIFactory.Rect(_meter, "Late", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(100, 30)), "LATE", 18, UIFactory.Dim, TextAnchor.MiddleLeft);

            for (int i = 0; i < 8; i++)
            {
                var rt = UIFactory.Rect(_hud, "Popup" + i, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 80));
                var main = UIFactory.Label(UIFactory.Rect(rt, "Main", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "", 46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Glow(main, new Color(0f, 0f, 0f, 0.6f), 2f);
                var sub = UIFactory.Label(UIFactory.Rect(rt, "Sub", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1f), new Vector2(0, 8), new Vector2(0, 26)), "", 22, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
                rt.gameObject.SetActive(false);
                _popups.Add(new Popup { main = main, sub = sub, rt = rt, born = -10f });
            }

            _pauseButton = UIFactory.Rect(_hud, "PauseBtn", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(34, 30), new Vector2(76, 76));
            var pb = _pauseButton.gameObject.AddComponent<Image>();
            pb.sprite = UIFactory.Rounded;
            pb.type = Image.Type.Sliced;
            pb.color = new Color(1f, 1f, 1f, 0.12f);
            var pbtn = _pauseButton.gameObject.AddComponent<Button>();
            pbtn.onClick.AddListener(() => _game.PauseGame());
            UIFactory.Label(_pauseButton, "Icon", "II", 34, UIFactory.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            pbtn.navigation = new Navigation { mode = Navigation.Mode.None };

            _autoBadge = UIFactory.Label(UIFactory.Rect(_hud, "Auto", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 34), new Vector2(400, 40)), "AUTOPLAY", 28, UIFactory.Pink, TextAnchor.LowerRight, FontStyle.Bold);
        }

        private void AddZone(RectTransform meter, float fraction, Color c)
        {
            var z = UIFactory.Rect(meter, "Zone", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_meterHalfWidth * 2f * fraction, 0));
            UIFactory.Image(z, c);
        }

        /// <summary>True if a pointer tap at this screen position is on the pause button (so it is not a hit).</summary>
        public bool IsOverPauseButton(Vector2 screen)
        {
            return _pauseButton != null && RectTransformUtility.RectangleContainsScreenPoint(_pauseButton, screen, null);
        }

        public void SetupHud(SongData song, Difficulty d, bool autoplay)
        {
            _hudTitle.text = song.title.ToUpperInvariant();
            _hudDiff.text = d.ToString().ToUpperInvariant() + "  -  " + song.bpm.ToString("F0", CultureInfo.InvariantCulture) + " BPM";
            _autoBadge.gameObject.SetActive(autoplay);
            _countIn.text = string.Empty;
            _milestone.text = string.Empty;
            _comboText.text = string.Empty;
            _comboLabel.text = string.Empty;
            for (int i = 0; i < _ticks.Count; i++)
            {
                _ticks[i].color = Color.clear;
                _tickBorn[i] = -100f;
            }
            for (int i = 0; i < _popups.Count; i++) _popups[i].rt.gameObject.SetActive(false);
            _meanMarker.anchoredPosition = new Vector2(0, 6);
        }

        public void UpdateHud(ScoreState s, float progress, float meanErrorMs, JudgeWindows windows)
        {
            _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _healthFill.anchorMax = new Vector2(Mathf.Clamp01(s.Health), 1f);
            _healthImage.color = Color.Lerp(UIFactory.Red, UIFactory.Green, Mathf.InverseLerp(0.2f, 0.7f, s.Health));
            _scoreText.text = s.Score.ToString("N0", CultureInfo.InvariantCulture);
            _accText.text = (s.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
            _multText.text = "x" + s.ComboMultiplier.ToString("F1", CultureInfo.InvariantCulture);

            bool showCombo = s.Combo >= 3;
            _comboText.text = showCombo ? s.Combo.ToString() : string.Empty;
            _comboLabel.text = showCombo ? "COMBO" : string.Empty;
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
            float meanX = Mathf.Clamp(meanErrorMs * 0.001f / (float)windows.good, -1f, 1f) * _meterHalfWidth;
            _meanMarker.anchoredPosition = new Vector2(Mathf.Lerp(_meanMarker.anchoredPosition.x, meanX, Time.unscaledDeltaTime * 6f), 6);

            float ma = Mathf.Clamp01(1.4f - (now - _milestoneTime) / 1.2f);
            _milestone.color = new Color(UIFactory.Gold.r, UIFactory.Gold.g, UIFactory.Gold.b, ma);
            _milestone.rectTransform.localScale = Vector3.one * (1f + 0.3f * Mathf.Clamp01(1f - (now - _milestoneTime) / 0.2f));

            for (int i = 0; i < _popups.Count; i++)
            {
                Popup p = _popups[i];
                if (!p.rt.gameObject.activeSelf) continue;
                float age = now - p.born;
                if (age > 0.6f)
                {
                    p.rt.gameObject.SetActive(false);
                    continue;
                }
                float a = Mathf.Clamp01(1f - (age - 0.25f) / 0.35f);
                p.rt.anchoredPosition = p.start + new Vector2(0, 50f * (1f - Mathf.Pow(1f - age / 0.6f, 3f)));
                float pop = 1f + 0.35f * Mathf.Clamp01(1f - age / 0.1f);
                p.rt.localScale = Vector3.one * pop;
                SetAlpha(p.main, a);
                SetAlpha(p.sub, a);
            }
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
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

        public void ShowJudgement(Grade grade, double error, Vector3 worldPos, Camera cam, JudgeWindows w)
        {
            Popup p = _popups[_nextPopup];
            _nextPopup = (_nextPopup + 1) % _popups.Count;
            Vector2 screen = cam.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            Vector2 anchored = local + _root.rect.size * 0.5f + new Vector2(0, 70);
            p.start = anchored;
            p.born = Time.unscaledTime;
            p.rt.anchoredPosition = anchored;
            p.rt.gameObject.SetActive(true);

            switch (grade)
            {
                case Grade.Perfect: p.main.text = "PERFECT"; p.main.color = UIFactory.Gold; break;
                case Grade.Great: p.main.text = "GREAT"; p.main.color = UIFactory.Cyan; break;
                case Grade.Good: p.main.text = "GOOD"; p.main.color = UIFactory.Green; break;
                default: p.main.text = "MISS"; p.main.color = UIFactory.Red; break;
            }

            double ms = error * 1000.0;
            if (grade == Grade.Perfect || double.IsNaN(error)) p.sub.text = string.Empty;
            else p.sub.text = (error < 0 ? "EARLY " : "LATE ") + System.Math.Abs(ms).ToString("F0", CultureInfo.InvariantCulture) + "ms";
            p.sub.color = error < 0 ? UIFactory.Cyan : UIFactory.Pink;
            _popups[(_nextPopup + _popups.Count - 1) % _popups.Count] = p;

            if (grade != Grade.Miss && !double.IsNaN(error)) AddTick(error, grade, w);
        }

        private void AddTick(double error, Grade grade, JudgeWindows w)
        {
            int i = _nextTick;
            _nextTick = (_nextTick + 1) % _ticks.Count;
            float x = Mathf.Clamp((float)(error / w.good), -1f, 1f) * _meterHalfWidth;
            _ticks[i].rectTransform.anchoredPosition = new Vector2(x, 0);
            Color c = grade == Grade.Perfect ? UIFactory.Gold : grade == Grade.Great ? UIFactory.Cyan : UIFactory.Green;
            _ticks[i].color = c;
            _tickBorn[i] = Time.unscaledTime;
        }

        // ================================================================ PAUSE

        private void BuildPause()
        {
            _pause = UIFactory.Stretch(_root, "Pause");
            UIFactory.Image(_pause, new Color(0f, 0f, 0.02f, 0.65f)).raycastTarget = true;
            var panel = UIFactory.Rect(_pause, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 560));
            UIFactory.Image(panel, UIFactory.PanelColor, UIFactory.Rounded);
            _pauseTitle = UIFactory.Label(UIFactory.Rect(panel, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(0, 80)), "PAUSED", 64, UIFactory.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _pauseButtons = UIFactory.Rect(panel, "Buttons", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(-120, -200));
            UIFactory.VStack(_pauseButtons, 18, 0, TextAnchor.MiddleCenter);
            UIFactory.Layout(UIFactory.Button(_pauseButtons, "Resume", "RESUME", 36, () => _game.ResumeGame(), out _, new Color(0f, 0.5f, 0.65f, 1f)), 96);
            UIFactory.Layout(UIFactory.Button(_pauseButtons, "Restart", "RESTART", 36, () => _game.Retry(), out _), 96);
            UIFactory.Layout(UIFactory.Button(_pauseButtons, "Quit", "SONG SELECT", 36, () => _game.ToMenu(), out _), 96);
            _resumeCount = UIFactory.Label(UIFactory.Rect(_pause, "Count", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 300)), "", 180, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        public void SetResumeCountdown(string text)
        {
            bool counting = !string.IsNullOrEmpty(text);
            _resumeCount.text = text;
            _pauseTitle.transform.parent.gameObject.SetActive(!counting);
        }

        // ================================================================ RESULTS

        private void BuildResults()
        {
            _results = UIFactory.Stretch(_root, "Results");
            UIFactory.Image(_results, new Color(0f, 0f, 0.02f, 0.55f)).raycastTarget = true;
            var panel = UIFactory.Rect(_results, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 860));
            UIFactory.Image(panel, UIFactory.PanelColor, UIFactory.Rounded);

            _resStatus = UIFactory.Label(UIFactory.Rect(panel, "Status", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(0, 70)), "", 60, UIFactory.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(_resStatus, new Color(1f, 0.3f, 0.9f, 0.6f), 3f);
            _resSong = UIFactory.Label(UIFactory.Rect(panel, "Song", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -106), new Vector2(0, 36)), "", 28, UIFactory.Dim, TextAnchor.MiddleCenter);
            _resRank = UIFactory.Label(UIFactory.Rect(panel, "Rank", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 40), new Vector2(380, 360)), "S", 300, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Glow(_resRank, new Color(1f, 0.5f, 0f, 0.8f), 5f);

            var stats = UIFactory.Rect(panel, "Stats", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(500, -170), new Vector2(-560, -330));
            UIFactory.VStack(stats, 8, 0, TextAnchor.UpperLeft);
            _resScore = Row(stats, 72, Color.white, FontStyle.Bold);
            _resAcc = Row(stats, 40, UIFactory.TextColor, FontStyle.Normal);
            _resCombo = Row(stats, 32, UIFactory.TextColor, FontStyle.Normal);
            _resCounts = Row(stats, 30, UIFactory.TextColor, FontStyle.Normal);
            _resCounts.supportRichText = true;
            _resTiming = Row(stats, 26, UIFactory.Dim, FontStyle.Normal);
            _resRecord = Row(stats, 34, UIFactory.Gold, FontStyle.Bold);

            _resSuggest = UIFactory.Label(UIFactory.Rect(panel, "Suggest", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(-120, 40)), "", 24, UIFactory.Gold, TextAnchor.MiddleCenter);
            _resApply = UIFactory.Button(panel, "Apply", "APPLY", 24, () => _game.ApplySuggestedOffset(), out _resApplyText, new Color(0.45f, 0.1f, 0.4f, 1f));
            var ar = (RectTransform)_resApply.transform;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0);
            ar.pivot = new Vector2(0.5f, 0);
            ar.anchoredPosition = new Vector2(0, 118);
            ar.sizeDelta = new Vector2(420, 50);

            var row = UIFactory.Rect(panel, "Buttons", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(760, 80));
            UIFactory.HStack(row, 24);
            UIFactory.Button(row, "Retry", "RETRY  (R)", 34, () => _game.Retry(), out _, new Color(0f, 0.5f, 0.65f, 1f));
            UIFactory.Button(row, "Menu", "SONG SELECT  (ESC)", 30, () => _game.ToMenu(), out _);
        }

        private static Text Row(RectTransform parent, int size, Color c, FontStyle style)
        {
            var t = UIFactory.Label(parent, "Row", "", size, c, TextAnchor.MiddleLeft, style);
            UIFactory.Layout(t, size + 10);
            return t;
        }

        public void ShowResults(SongData song, Difficulty d, ScoreState s, bool failed, bool autoplay, bool newRecord, float suggestedOffsetMs, bool canSuggest)
        {
            Show(Screen.Results);
            string rank = s.Rank(failed);
            _resStatus.text = failed ? "FAILED" : s.FullCombo ? (s.Perfect == s.TotalNotes ? "ALL PERFECT!" : "FULL COMBO!") : "CLEAR!";
            _resStatus.color = failed ? UIFactory.Red : UIFactory.Cyan;
            _resSong.text = song.title.ToUpperInvariant() + "  -  " + d.ToString().ToUpperInvariant() + (autoplay ? "  -  AUTOPLAY" : string.Empty);
            _resRank.text = rank;
            _resRank.color = rank == "F" ? UIFactory.Red : rank.StartsWith("S") ? UIFactory.Gold : rank == "A" ? UIFactory.Cyan : rank == "B" ? UIFactory.Green : UIFactory.Pink;
            _resScore.text = s.Score.ToString("N0", CultureInfo.InvariantCulture);
            _resAcc.text = "ACCURACY  " + (s.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
            _resCombo.text = "MAX COMBO  " + s.MaxCombo + " / " + s.TotalNotes;
            _resCounts.text =
                "<color=#" + UIFactory.Hex(UIFactory.Gold) + ">PERFECT " + s.Perfect + "</color>     " +
                "<color=#" + UIFactory.Hex(UIFactory.Cyan) + ">GREAT " + s.Great + "</color>     " +
                "<color=#" + UIFactory.Hex(UIFactory.Green) + ">GOOD " + s.Good + "</color>     " +
                "<color=#" + UIFactory.Hex(UIFactory.Red) + ">MISS " + s.Miss + "</color>";
            s.ErrorStats(out float mean, out float std, out float median);
            _resTiming.text = s.Errors.Count > 0
                ? "TIMING  average " + mean.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms (" + (mean > 0 ? "late" : "early") + ")   spread " + std.ToString("F1", CultureInfo.InvariantCulture) + " ms"
                : string.Empty;
            _resRecord.text = newRecord ? "NEW RECORD!" : string.Empty;

            _resSuggest.text = canSuggest
                ? "Your taps were consistently " + Mathf.Abs(suggestedOffsetMs).ToString("F0") + " ms " + (suggestedOffsetMs > 0 ? "late" : "early") + ". Adjust the audio offset to match your setup?"
                : string.Empty;
            _resApply.gameObject.SetActive(canSuggest);
            _resApplyText.text = "APPLY OFFSET " + (GameSettings.OffsetMs + suggestedOffsetMs).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
        }

        public void HideOffsetSuggestion()
        {
            _resApply.gameObject.SetActive(false);
            _resSuggest.text = "Offset saved: " + GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
        }

        // ================================================================ CALIBRATION

        private void BuildCalibration()
        {
            _calibration = UIFactory.Stretch(_root, "Calibration");
            UIFactory.Image(_calibration, new Color(0f, 0f, 0.02f, 0.75f)).raycastTarget = true;
            UIFactory.Label(UIFactory.Rect(_calibration, "Title", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1400, 100)), "AUDIO CALIBRATION", 72, UIFactory.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Label(UIFactory.Rect(_calibration, "Help", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -190), new Vector2(1500, 80)),
                "Listen to the clicks and tap SPACE / click / touch exactly on each click.\nDon't follow the circle; follow your ears. The game measures the delay of your speakers or headphones.", 28, UIFactory.TextColor, TextAnchor.MiddleCenter);

            var circleRt = UIFactory.Rect(_calibration, "Circle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(240, 240));
            _calCircle = UIFactory.Image(circleRt, UIFactory.Cyan, UIFactory.Circle);
            _calStatus = UIFactory.Label(UIFactory.Rect(_calibration, "Status", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1200, 50)), "", 34, UIFactory.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            _calValue = UIFactory.Label(UIFactory.Rect(_calibration, "Value", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(1200, 50)), "", 30, UIFactory.Gold, TextAnchor.MiddleCenter);

            var row = UIFactory.Rect(_calibration, "Buttons", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(1000, 84));
            UIFactory.HStack(row, 24);
            _calSave = UIFactory.Button(row, "Save", "SAVE", 34, () => _game.SaveCalibration(), out _, new Color(0f, 0.5f, 0.65f, 1f));
            UIFactory.Button(row, "Restart", "RESTART", 34, () => _game.StartCalibration(), out _);
            UIFactory.Button(row, "Default", "USE DEFAULT", 30, () => _game.ResetOffsetToDefault(), out _);
            UIFactory.Button(row, "Cancel", "BACK", 34, () => _game.CancelCalibration(), out _);
        }

        public void PulseCalibration() => _calPulse = 1f;

        public void UpdateCalibration(float beatPhase, int taps, int needed, float medianMs, float spreadMs, bool ready)
        {
            _calPulse = Mathf.MoveTowards(_calPulse, 0f, Time.unscaledDeltaTime * 5f);
            float s = 1f + 0.25f * Mathf.Exp(-beatPhase * 8f) + 0.2f * _calPulse;
            _calCircle.rectTransform.localScale = Vector3.one * s;
            _calCircle.color = Color.Lerp(UIFactory.Cyan * 0.6f, Color.white, Mathf.Exp(-beatPhase * 8f));
            _calStatus.text = taps < needed ? "TAPS  " + taps + " / " + needed : "DONE - " + taps + " taps";
            _calValue.text = taps >= 4
                ? "Measured offset " + medianMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms   (spread " + spreadMs.ToString("F0") + " ms)" + (spreadMs > 40f ? "   - keep going, taps are uneven" : string.Empty)
                : "Current offset " + GameSettings.OffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
            _calSave.interactable = ready;
        }
    }
}
