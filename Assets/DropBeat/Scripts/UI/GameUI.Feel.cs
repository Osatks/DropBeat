using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// Reward feedback in the UI: streak tier-ups, combo breaks, banners, the finale, the section bar,
    /// graded judgement popups with points, the choreographed results reveal and the menu's stars.
    /// </summary>
    public sealed partial class GameUI
    {
        private static readonly string[] TierNames = { "combo", "groove", "fever", "blaze", "legend" };

        public static Color TierColor(int t)
        {
            switch (t)
            {
                case 1: return UIFactory.Cyan;
                case 2: return new Color(0.62f, 0.40f, 1f);
                case 3: return UIFactory.Gold;
                case 4: return new Color(1f, 0.95f, 0.85f);
                default: return UIFactory.Pink;
            }
        }

        // HUD feel
        private float _tierAt = -10f;
        private float _punchAmt = 0.18f;
        private float _beatPh = -1f;
        private float _scorePunchAt = -10f;
        private Image _tierRing;
        private Text _comboGhost;
        private float _ghostAt = -10f;
        private Text _banner;
        private float _bannerAt = -10f;
        private int _bannerPriority;
        private Text _finale, _finaleSub;
        private float _finaleAt = -10f;
        private Camera _popupCam;

        // Section bar
        private RectTransform _sectionBar;
        private readonly List<Image> _secSegs = new List<Image>();
        private readonly List<float> _secPopAt = new List<float>();
        private int[] _secState = new int[0]; // 0 upcoming, 1 clean, 2 flawless, 3 broken
        private int _secCurrent = -1;
        private Image _finalOutline;
        private Text _finalChip;

        /// <summary>Stops pointer clicks from bubbling up to a parent's button (a sheet's tap-outside-to-close).</summary>
        private sealed class ClickBlocker : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData) { }
        }

        /// <summary>A dim layer under a screen that runs past the safe area (notch and home indicator).</summary>
        private static RectTransform FullDim(RectTransform screen)
        {
            RectTransform dim = UIFactory.Stretch(screen, "Dim");
            dim.SetAsFirstSibling();
            dim.offsetMin = new Vector2(0, -300);
            dim.offsetMax = new Vector2(0, 300);
            return dim;
        }

        /// <summary>True while the settings or style sheet covers the menu (menu hotkeys pause then).</summary>
        public bool SheetOpen => (_settings != null && _settings.gameObject.activeSelf) || (_style != null && _style.gameObject.activeSelf) ||
                                 (_profile != null && _profile.gameObject.activeSelf);

        /// <summary>True if a pointer at this screen position is on the calibration buttons.</summary>
        public bool IsOverCalibrationButtons(Vector2 screen)
        {
            return _calSave != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_calSave.transform.parent, screen, null);
        }

        // ---------------------------------------------------------------- HUD build

        private void BuildSectionBar(RectTransform panel)
        {
            _sectionBar = UIFactory.Rect(panel, "Sections", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(-60, 10));
            _finalOutline = UIFactory.Image(UIFactory.Rect(_sectionBar, "FinalEdge", Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero), UIFactory.Gold, UIFactory.PillOutline);
            _finalOutline.gameObject.SetActive(false);
            _finalChip = UIFactory.Label(UIFactory.Rect(_sectionBar, "FinalChip", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, -6), new Vector2(160, 30)),
                UIFactory.Spaced("final"), 18, UIFactory.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            _finalChip.gameObject.SetActive(false);
        }

        private void BuildFeelHud()
        {
            // Tier-up ring: expands from behind the combo number.
            var ring = UIFactory.Rect(_comboRoot, "TierRing", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -65), new Vector2(170, 170));
            _tierRing = UIFactory.Image(ring, Color.clear, UIFactory.RingOutline);
            _tierRing.transform.SetAsFirstSibling();

            // Ghost of a lost streak: a red copy of the number that swells, drops and fades.
            _comboGhost = UIFactory.Label(UIFactory.Rect(_comboRoot, "Ghost", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 130)),
                "", 120, UIFactory.Red, TextAnchor.MiddleCenter, FontStyle.Bold);

            _banner = UIFactory.Label(UIFactory.At(_hud, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0, 430), new Vector2(1000, 90)), "", 52, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _banner.gameObject.AddComponent<UIGradient>();
            var bo = _banner.gameObject.AddComponent<Outline>();
            bo.effectColor = new Color(0f, 0f, 0f, 0.5f);
            bo.effectDistance = new Vector2(2f, -2f);

            var fin = UIFactory.At(_hud, "Finale", new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(940, 200));
            UIFactory.Halo(fin, new Color(1f, 0.7f, 0.3f, 0.2f), 1.1f);
            _finale = UIFactory.Label(UIFactory.Rect(fin, "Title", new Vector2(0, 0.35f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "", 88, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _finale.resizeTextForBestFit = true;
            _finale.resizeTextMinSize = 40;
            _finale.resizeTextMaxSize = 88;
            _finale.gameObject.AddComponent<UIGradient>();
            var fo = _finale.gameObject.AddComponent<Outline>();
            fo.effectColor = new Color(0.2f, 0f, 0.3f, 0.6f);
            fo.effectDistance = new Vector2(3f, -3f);
            _finaleSub = UIFactory.Label(UIFactory.Rect(fin, "Sub", new Vector2(0, 0), new Vector2(1, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "", 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Judgement popups: bigger text, room for the points line.
            foreach (Popup p in _popups)
            {
                p.sub.rectTransform.sizeDelta = new Vector2(0, 40);
                p.sub.fontSize = 32;
                var o = p.sub.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.6f);
                o.effectDistance = new Vector2(1.5f, -1.5f);
            }
            ResetFeelHud();
        }

        private void ResetFeelHud()
        {
            _tierAt = _ghostAt = _bannerAt = _finaleAt = _scorePunchAt = -10f;
            _bannerPriority = 0;
            if (_tierRing != null) _tierRing.color = Color.clear;
            if (_comboGhost != null) _comboGhost.text = string.Empty;
            if (_banner != null) _banner.text = string.Empty;
            if (_finale != null)
            {
                _finale.text = string.Empty;
                _finaleSub.text = string.Empty;
            }
        }

        // ---------------------------------------------------------------- HUD events

        /// <summary>Streak level-up (1 GROOVE .. 4 LEGEND), timed by the game to land on a downbeat.</summary>
        public void TierUp(int t)
        {
            _tierAt = Time.unscaledTime;
            _tierRing.color = TierColor(t);
        }

        /// <summary>A big streak was lost: its number drops away in red.</summary>
        public void ComboBreak(int lost)
        {
            _comboGhost.text = lost.ToString();
            _ghostAt = Time.unscaledTime;
        }

        public void PunchScore() => _scorePunchAt = Time.unscaledTime;

        /// <summary>A centre banner (section clears, overdrive payout). A lower priority never covers a higher one that is still up.</summary>
        public void ShowBanner(string text, Color a, Color b, int priority = 1)
        {
            if (Time.unscaledTime - _bannerAt < 0.9f && priority < _bannerPriority) return;
            _banner.text = text;
            _banner.GetComponent<UIGradient>().Set(a, b, true);
            _bannerAt = Time.unscaledTime;
            _bannerPriority = priority;
        }

        public void ShowFinale(string title, string sub, Color a, Color b)
        {
            _finale.text = UIFactory.Spaced(title.ToLowerInvariant());
            _finale.GetComponent<UIGradient>().Set(a, b, true);
            _finaleSub.text = sub;
            _finaleAt = Time.unscaledTime;
        }

        /// <summary>Section bar: one segment per 8-bar section, widths proportional to time.</summary>
        public void SetupSections(IList<double> starts, IList<double> ends)
        {
            int n = starts.Count;
            while (_secSegs.Count < n)
            {
                var rt = UIFactory.Rect(_sectionBar, "Seg" + _secSegs.Count, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                _secSegs.Add(UIFactory.Image(rt, Color.white, UIFactory.Pill));
                _secPopAt.Add(-10f);
            }
            _secState = new int[n];
            _secCurrent = -1;
            double t0 = n > 0 ? starts[0] : 0, t1 = n > 0 ? ends[n - 1] : 1;
            double span = System.Math.Max(0.1, t1 - t0);
            for (int i = 0; i < _secSegs.Count; i++)
            {
                bool on = i < n;
                _secSegs[i].gameObject.SetActive(on);
                if (!on) continue;
                RectTransform rt = _secSegs[i].rectTransform;
                rt.anchorMin = new Vector2((float)((starts[i] - t0) / span), 0f);
                rt.anchorMax = new Vector2((float)((ends[i] - t0) / span), 1f);
                rt.offsetMin = new Vector2(3f, 0f);
                rt.offsetMax = new Vector2(-3f, 0f);
                _secPopAt[i] = -10f;
            }
            if (n > 0)
            {
                RectTransform last = _secSegs[n - 1].rectTransform;
                RectTransform edge = _finalOutline.rectTransform;
                edge.anchorMin = new Vector2(last.anchorMin.x, -0.6f);
                edge.anchorMax = new Vector2(last.anchorMax.x, 1.6f);
                edge.offsetMin = edge.offsetMax = Vector2.zero;
            }
            _finalOutline.gameObject.SetActive(false);
            _finalChip.gameObject.SetActive(false);
        }

        public void SetSectionCurrent(int p)
        {
            _secCurrent = p;
            bool final = p >= 0 && p == _secState.Length - 1 && _secState.Length > 1;
            if (final && !_finalChip.gameObject.activeSelf)
            {
                _finalOutline.gameObject.SetActive(true);
                _finalChip.gameObject.SetActive(true);
            }
        }

        /// <param name="kind">1 clean, 2 flawless, 3 broken.</param>
        public void SectionResult(int p, int kind, int bonus)
        {
            if (p < 0 || p >= _secState.Length) return;
            _secState[p] = kind;
            _secPopAt[p] = Time.unscaledTime;
            if (kind == 2) ShowBanner("FLAWLESS  +" + bonus.ToString("N0", CultureInfo.InvariantCulture), UIFactory.Gold, UIFactory.Pink, 1);
            else if (kind == 1) ShowBanner("SECTION CLEAR  +" + bonus.ToString("N0", CultureInfo.InvariantCulture), UIFactory.Cyan, Color.white, 1);
        }

        private string SectionSummary()
        {
            int clean = 0, flawless = 0;
            foreach (int k in _secState)
            {
                if (k == 1) clean++;
                else if (k == 2) flawless++;
            }
            if (_secState.Length == 0) return string.Empty;
            return "SECTIONS " + (clean + flawless) + "/" + _secState.Length + " CLEAN" + (flawless > 0 ? "  ·  " + flawless + " FLAWLESS" : string.Empty);
        }

        // ---------------------------------------------------------------- per-frame HUD animation

        private void AnimateFeel()
        {
            float now = Time.unscaledTime;

            float ta = (now - _tierAt) / 0.28f;
            if (ta <= 1f)
            {
                float e = 1f - (1f - ta) * (1f - ta);
                _tierRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 2.2f, e);
                Color c = _tierRing.color;
                c.a = 1f - ta;
                _tierRing.color = c;
            }
            else if (_tierRing.color.a > 0f) _tierRing.color = Color.clear;

            float ga = (now - _ghostAt) / 0.4f;
            if (ga <= 1f)
            {
                _comboGhost.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.5f, ga);
                _comboGhost.rectTransform.anchoredPosition = new Vector2(0f, -60f * ga);
                _comboGhost.color = new Color(UIFactory.Red.r, UIFactory.Red.g, UIFactory.Red.b, 1f - ga);
            }
            else if (_comboGhost.text.Length > 0) _comboGhost.text = string.Empty;

            float ba = now - _bannerAt;
            if (ba <= 0.9f)
            {
                float pop = Mathf.Clamp01(ba / 0.12f);
                _banner.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, pop);
                _banner.color = new Color(1f, 1f, 1f, Mathf.Clamp01((0.9f - ba) / 0.25f));
            }
            else if (_banner.text.Length > 0) _banner.text = string.Empty;

            float fa = now - _finaleAt;
            if (fa <= 1.3f)
            {
                float s = fa < 0.25f ? EaseOutBack(fa / 0.25f) : 1f;
                _finale.rectTransform.parent.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, s);
                float alpha = fa < 0.9f ? 1f : Mathf.Clamp01((1.3f - fa) / 0.4f);
                _finale.color = new Color(1f, 1f, 1f, alpha);
                _finaleSub.color = new Color(1f, 1f, 1f, alpha * 0.9f);
            }
            else if (_finale.text.Length > 0)
            {
                _finale.text = string.Empty;
                _finaleSub.text = string.Empty;
            }

            // Section bar.
            float pulse = _beatPh >= 0f ? Mathf.Exp(-6f * _beatPh) : 0.5f;
            Color accent = _progressRing != null ? _progressRing.color : UIFactory.Cyan;
            for (int i = 0; i < _secState.Length && i < _secSegs.Count; i++)
            {
                Color c;
                switch (_secState[i])
                {
                    case 1: c = UIFactory.Cyan; break;
                    case 2: c = UIFactory.Gold; break;
                    case 3: c = new Color(UIFactory.Red.r, UIFactory.Red.g, UIFactory.Red.b, 0.35f); break;
                    default:
                        c = i == _secCurrent ? new Color(accent.r, accent.g, accent.b, 0.4f + 0.35f * pulse) : new Color(1f, 1f, 1f, 0.09f);
                        break;
                }
                _secSegs[i].color = c;
                float pa = Mathf.Clamp01((now - _secPopAt[i]) / 0.25f);
                _secSegs[i].rectTransform.localScale = new Vector3(1f, Mathf.Lerp(1.6f, 1f, pa), 1f);
            }
            if (_finalChip.gameObject.activeSelf) _finalChip.color = new Color(UIFactory.Gold.r, UIFactory.Gold.g, UIFactory.Gold.b, 0.6f + 0.4f * pulse);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x);
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        // ---------------------------------------------------------------- judgement popups

        /// <summary>
        /// Graded popup below the ball (the path climbs toward the next ring, so below never covers it).
        /// Shows the points earned, a Perfect chain, and EARLY / LATE for Great and Good. One per lane.
        /// </summary>
        public void ShowJudgement(Grade grade, double error, Vector3 worldPos, Camera cam, JudgeWindows w, string label = null,
                                  int points = 0, int chain = 0, bool overdrive = false, int lane = 0)
        {
            _popupCam = cam;
            for (int i = 0; i < _popups.Count; i++)
            {
                if (_popups[i].lane == lane && _popups[i].rt.gameObject.activeSelf) _popups[i].rt.gameObject.SetActive(false);
            }
            int idx = _nextPopup;
            _nextPopup = (_nextPopup + 1) % _popups.Count;
            Popup p = _popups[idx];
            p.grade = grade;
            p.world = worldPos;
            p.lane = lane;
            p.start = HudPoint(worldPos, cam, new Vector2(0, -170));
            p.born = Time.unscaledTime;
            p.rt.anchoredPosition = p.start;
            p.rt.gameObject.SetActive(true);

            Color a, b;
            switch (grade)
            {
                case Grade.Perfect: p.main.text = chain >= 3 ? "PERFECT x" + chain : "PERFECT"; a = new Color(1f, 0.95f, 0.6f); b = UIFactory.Gold; p.main.fontSize = 72; break;
                case Grade.Great: p.main.text = "GREAT"; a = Color.white; b = UIFactory.Cyan; p.main.fontSize = 62; break;
                case Grade.Good: p.main.text = "GOOD"; a = Color.white; b = UIFactory.Green; p.main.fontSize = 52; break;
                default: p.main.text = "MISS"; a = new Color(1f, 0.6f, 0.6f); b = UIFactory.Red; p.main.fontSize = 58; break;
            }
            if (!string.IsNullOrEmpty(label)) p.main.text = label;
            p.main.color = Color.white;
            p.main.GetComponent<UIGradient>().Set(a, b, false);

            string pts = points > 0 ? "+" + points.ToString("N0", CultureInfo.InvariantCulture) + (overdrive ? "  x2" : string.Empty) : string.Empty;
            if (grade == Grade.Miss || (double.IsNaN(error) && grade != Grade.Perfect))
            {
                p.sub.text = string.Empty;
            }
            else if (grade == Grade.Perfect || overdrive)
            {
                p.sub.text = pts;
                p.sub.color = UIFactory.Gold;
                p.sub.fontSize = 32;
            }
            else
            {
                p.sub.text = error < 0 ? "« EARLY" : "LATE »";
                p.sub.color = error < 0 ? UIFactory.Cyan : UIFactory.Pink;
                p.sub.fontSize = 36;
            }
            _popups[idx] = p;

            if (grade != Grade.Miss && !double.IsNaN(error)) AddTick(error, grade, w);
        }

        private void AnimateJudgements(float now)
        {
            const float life = 0.5f;
            for (int i = 0; i < _popups.Count; i++)
            {
                Popup p = _popups[i];
                if (!p.rt.gameObject.activeSelf) continue;
                float age = now - p.born;
                if (age > life)
                {
                    p.rt.gameObject.SetActive(false);
                    continue;
                }
                // Ride with the burst: re-project from the world point every frame.
                Vector2 at = _popupCam != null ? HudPoint(p.world, _popupCam, new Vector2(0, -170)) : p.start;
                float scale = 1f, alpha = Mathf.Clamp01((life - age) / 0.15f), dx = 0f, dy = 0f;
                switch (p.grade)
                {
                    case Grade.Perfect: scale = Pop(age, 0.5f, 1.18f); break;
                    case Grade.Great: scale = Pop(age, 0.7f, 1.08f); break;
                    case Grade.Good:
                        scale = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(age / 0.12f));
                        alpha *= 0.85f;
                        break;
                    default:
                        dy = -30f * Mathf.Clamp01(age / life);
                        if (age < 0.15f) dx = 8f * Mathf.Sin(age * 70f) * Mathf.Exp(-age / 0.08f);
                        break;
                }
                // Keep the whole text on screen near the edges.
                float half = Mathf.Max(p.main.preferredWidth, p.sub.preferredWidth) * 0.5f * scale + 24f;
                float width = _hud.rect.width;
                at.x = width > 2f * half ? Mathf.Clamp(at.x, half, width - half) : width * 0.5f;
                at.y = Mathf.Max(at.y, 290f); // above the twist badge and timing meter
                p.rt.anchoredPosition = at + new Vector2(dx, dy);
                p.rt.localScale = Vector3.one * scale;
                p.rt.GetComponent<CanvasGroup>().alpha = alpha;
            }
        }

        /// <summary>Pop-in: from <paramref name="from"/> to <paramref name="peak"/> at 70 ms, settling to 1 at 180 ms.</summary>
        private static float Pop(float age, float from, float peak)
        {
            if (age < 0.07f)
            {
                float u = age / 0.07f;
                return Mathf.Lerp(from, peak, 1f - (1f - u) * (1f - u));
            }
            if (age < 0.18f) return Mathf.Lerp(peak, 1f, (age - 0.07f) / 0.11f);
            return 1f;
        }

        // ---------------------------------------------------------------- results reveal

        private RectTransform _resButtons;
        private CanvasGroup _resGroup, _resButtonsGroup, _resCountsGroup;
        private Image _resRankFill, _resRankWave;
        private RectTransform _resStamp;
        private Text _resStampText, _resGoal, _resRankPct;
        private readonly Image[] _resStars = new Image[3];
        private readonly Text[] _resStarCaps = new Text[3];
        private readonly List<Image> _resStrip = new List<Image>();
        private readonly List<RectTransform> _confetti = new List<RectTransform>();
        private readonly List<Vector2> _confettiVel = new List<Vector2>();
        private readonly List<float> _confettiBorn = new List<float>();
        private Button _resTryEasy;
        private float _resT0 = -100f;
        private int _resFired;
        private bool _resSkipped;
        private int _resTicks;
        private Vector2 _resPanelPos;

        // Targets of the reveal.
        private int _rScore, _rCombo, _rTotal;
        private float _rAcc, _rProgress;
        private string _rRank, _rStatus;
        private bool _rFailed, _rAuto, _rStamp;
        private GameSettings.RunRecord _rRec;

        private void BuildResultsReveal()
        {
            _resGroup = _resPanel.gameObject.AddComponent<CanvasGroup>();
            _resPanel.gameObject.AddComponent<Canvas>();
            _resPanel.gameObject.AddComponent<GraphicRaycaster>();
            _resButtonsGroup = _resButtons.gameObject.AddComponent<CanvasGroup>();
            _resCountsGroup = _resCounts.gameObject.AddComponent<CanvasGroup>();

            // Tap anywhere on the dim to skip to the end of the reveal.
            var skip = _results.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(() =>
            {
                if (Time.unscaledTime - _resT0 < 3f)
                {
                    _resSkipped = true;
                    _resT0 -= 10f;
                }
            });

            // Rank ring fills like a clock; a shockwave ring behind the letter on the slam.
            _resRankRing.color = new Color(1f, 1f, 1f, 0.12f);
            _resRankFill = UIFactory.Image(UIFactory.Stretch(_resRankRt, "Fill"), UIFactory.Gold, UIFactory.RingOutline);
            _resRankFill.type = Image.Type.Filled;
            _resRankFill.fillMethod = Image.FillMethod.Radial360;
            _resRankFill.fillOrigin = (int)Image.Origin360.Top;
            _resRankWave = UIFactory.Image(UIFactory.Stretch(_resRankRt, "Wave"), Color.clear, UIFactory.RingOutline);
            _resRankWave.transform.SetSiblingIndex(0);
            _resRank.transform.SetAsLastSibling();
            _resRankPct = UIFactory.Label(UIFactory.Stretch(_resRankRt, "Pct"), "", 110, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Stamp (NEW BEST / FIRST CLEAR) to the left of the rank, stars to the right.
            _resStamp = UIFactory.Rect(_resRankRt, "Stamp", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150, 40), new Vector2(250, 70));
            UIFactory.Image(_resStamp, UIFactory.Gold, UIFactory.PillOutline);
            _resStampText = UIFactory.Label(_resStamp, "Text", "", 26, UIFactory.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _resStamp.localRotation = Quaternion.Euler(0f, 0f, 8f);
            string[] caps = { "clear", "full combo", "rank a" };
            for (int i = 0; i < 3; i++)
            {
                var srt = UIFactory.Rect(_resRankRt, "Star" + i, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110, 110 - i * 110), new Vector2(84, 84));
                _resStars[i] = UIFactory.Image(srt, UIFactory.Faint, UIFactory.StarOutline);
                _resStarCaps[i] = UIFactory.Label(UIFactory.Rect(srt, "Cap", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, 2), new Vector2(80, 22)),
                    UIFactory.Spaced(caps[i]), 15, UIFactory.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            // Next goal (in the stats column) and a song strip in the 4th tile.
            _resGoal = UIFactory.Label(_resStatsRt, "Goal", "", 24, UIFactory.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Layout(_resGoal, 34);
            Transform tile = _tileValues[3].transform.parent;
            foreach (Transform c in tile)
            {
                var label = c.GetComponent<Text>();
                if (label != null && c.name == "Label") label.text = UIFactory.Spaced("song");
            }
            var strip = UIFactory.Rect(tile, "Strip", new Vector2(0, 0), new Vector2(1, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(-48, -26));
            for (int i = 0; i < 40; i++)
            {
                var cell = UIFactory.Rect(strip, "C" + i, new Vector2(i / 40f, 0f), new Vector2((i + 1) / 40f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-2, 0));
                _resStrip.Add(UIFactory.Image(cell, Color.white, UIFactory.Pill));
            }

            for (int i = 0; i < 40; i++)
            {
                var c = UIFactory.Rect(_resPanel, "Confetti" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 22));
                Color[] cols = { UIFactory.Gold, UIFactory.Pink, UIFactory.Cyan };
                UIFactory.Image(c, cols[i % 3], UIFactory.Pill);
                c.gameObject.SetActive(false);
                _confetti.Add(c);
                _confettiVel.Add(Vector2.zero);
                _confettiBorn.Add(-10f);
            }

            _resTryEasy = UIFactory.Button(_resPanel, "TryEasy", UIFactory.Spaced("try easy"), 22, () => _game.TryEasy(), out _, UIFactory.ButtonStyle.Outline);
            Place((RectTransform)_resTryEasy.transform, new Vector2(0.5f, 0), new Vector2(0, 128), new Vector2(320, 54));
            _resTryEasy.gameObject.SetActive(false);
            BuildRewardCard();
        }

        public void ShowResults(SongData song, Difficulty d, ScoreState s, bool failed, bool autoplay, GameSettings.RunRecord rec, float progress,
                                float suggestedOffsetMs, bool canSuggest)
        {
            Show(Screen.Results);
            // Keep the results card clean: hide the in-game combo, count-in and popups behind it.
            _comboText.text = string.Empty;
            _comboLabel.text = string.Empty;
            _countIn.text = string.Empty;
            _milestone.text = string.Empty;
            ResetFeelHud();
            foreach (var p in _popups) p.rt.gameObject.SetActive(false);
            foreach (var p in _hints) p.rt.gameObject.SetActive(false);

            _rFailed = failed;
            _rAuto = autoplay;
            _rRec = rec;
            _rScore = s.Score;
            _rAcc = s.Accuracy;
            _rCombo = s.MaxCombo;
            _rTotal = s.TotalNotes;
            _rProgress = Mathf.Clamp01(progress);
            _rRank = autoplay ? "DEMO" : s.Rank(failed);
            bool ap = s.TotalNotes > 0 && s.Perfect == s.TotalNotes;
            _rStatus = failed
                ? (_rProgress >= 0.85f ? "SO CLOSE  ·  " + Mathf.RoundToInt(_rProgress * 100f) + "%" : "FAILED AT " + Mathf.RoundToInt(_rProgress * 100f) + "%")
                : ap ? "ALL PERFECT" : s.FullCombo ? "FULL COMBO" : "CLEAR";
            _rStamp = !autoplay && (rec.firstClear || (rec.newScore && rec.before.Exists) || (failed && rec.newProgress && rec.before.plays > 0));

            _resSong.text = song.title.ToUpperInvariant() + "   ·   " + d.ToString().ToUpperInvariant() + (autoplay ? "   ·   AUTOPLAY" : string.Empty);
            Color accent = Ldr(song.accent);
            _resPanel.GetComponent<Image>().color = new Color(accent.r * 0.12f + 0.08f, accent.g * 0.1f + 0.05f, accent.b * 0.12f + 0.18f, 0.97f);

            s.ErrorStats(out float mean, out float std, out _);
            _tileValues[2].text = s.Errors.Count == 0 ? "-" : TimingVerdict(mean, std);
            _tileValues[2].fontSize = 26;
            _tileValues[3].text = string.Empty;
            FillStrip(s);
            _resCounts.text =
                "<color=#" + UIFactory.Hex(UIFactory.Gold) + ">PERFECT " + s.Perfect + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Cyan) + ">GREAT " + s.Great + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Green) + ">GOOD " + s.Good + "</color>    " +
                "<color=#" + UIFactory.Hex(UIFactory.Red) + ">MISS " + s.Miss + "</color>";
            string sections = SectionSummary();
            if (sections.Length > 0) _resCounts.text += "\n<size=20>" + sections + "</size>";
            _resRecord.text = string.Empty;
            _resGoal.text = string.Empty;

            _resSuggest.text = canSuggest
                ? "Your taps were consistently " + Mathf.Abs(suggestedOffsetMs).ToString("F0") + " ms " + (suggestedOffsetMs > 0 ? "late" : "early") + ". Adjust the audio offset?"
                : string.Empty;
            _resApply.gameObject.SetActive(canSuggest);
            _resApplyText.text = "APPLY OFFSET " + (GameSettings.OffsetMs + suggestedOffsetMs).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " MS";
            _resTryEasy.gameObject.SetActive(failed && !autoplay && !canSuggest && d != Difficulty.Easy && rec.after.failRun >= 3);

            SetupRewardCard(autoplay ? default : rec);
            _resT0 = Time.unscaledTime;
            _resFired = 0;
            _resSkipped = false;
            _resTicks = 0;
            _resPanelPos = _resPanel.anchoredPosition;
            foreach (var c in _confetti) c.gameObject.SetActive(false);
            AnimateResults();
        }

        private static string TimingVerdict(float meanMs, float stdMs)
        {
            string when = Mathf.Abs(meanMs) < 8f ? "Right on the beat" : Mathf.Abs(meanMs).ToString("F0", CultureInfo.InvariantCulture) + " ms " + (meanMs > 0f ? "late" : "early");
            return when + "  ·  ±" + stdMs.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void FillStrip(ScoreState s)
        {
            int n = _resStrip.Count;
            var worst = new int[n];
            for (int i = 0; i < n; i++) worst[i] = -1;
            float t0 = float.MaxValue, t1 = float.MinValue;
            foreach (Vector2 m in s.Marks)
            {
                t0 = Mathf.Min(t0, m.x);
                t1 = Mathf.Max(t1, m.x);
            }
            float span = Mathf.Max(0.1f, t1 - t0);
            foreach (Vector2 m in s.Marks)
            {
                int i = Mathf.Clamp((int)((m.x - t0) / span * n), 0, n - 1);
                worst[i] = Mathf.Max(worst[i], (int)m.y);
            }
            for (int i = 0; i < n; i++)
            {
                Color c;
                switch (worst[i])
                {
                    case (int)Grade.Perfect: c = UIFactory.Gold; break;
                    case (int)Grade.Great: c = UIFactory.Cyan; break;
                    case (int)Grade.Good: c = UIFactory.Green; break;
                    case (int)Grade.Miss: c = UIFactory.Red; break;
                    default: c = new Color(1f, 1f, 1f, 0.1f); break;
                }
                _resStrip[i].color = c;
            }
        }

        private bool Fire(int bit, float age, float at)
        {
            if (age < at || (_resFired & (1 << bit)) != 0) return false;
            _resFired |= 1 << bit;
            return true;
        }

        private void AnimateResults()
        {
            float age = Time.unscaledTime - _resT0;
            SfxPlayer sfx = _game != null ? _game.Sfx : null;
            bool quiet = _resSkipped; // a skip jumps to the end without replaying every sound

            // Card rises and fades in.
            float ca = Mathf.Clamp01(age / 0.3f);
            float ce = 1f - Mathf.Pow(1f - ca, 3f);
            _resGroup.alpha = ce;
            _resPanel.anchoredPosition = _resPanelPos + new Vector2(0f, -90f * (1f - ce));

            // Status punch.
            _resStatus.text = age >= 0.2f ? UIFactory.Spaced(_rStatus.ToLowerInvariant()) : string.Empty;
            float sa = Mathf.Clamp01((age - 0.2f) / 0.18f);
            _resStatus.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, sa);

            // Score count-up in key.
            float su = Mathf.Clamp01((age - 0.35f) / 1.0f);
            float se = 1f - Mathf.Pow(1f - su, 4f);
            _resScore.text = Mathf.RoundToInt(_rScore * se).ToString("N0", CultureInfo.InvariantCulture);
            if (age >= 0.35f && age < 1.35f && sfx != null && !quiet)
            {
                int tick = Mathf.FloorToInt((age - 0.35f) / 0.07f);
                if (tick > _resTicks || (_resTicks == 0 && tick == 0 && (_resFired & 1) == 0))
                {
                    _resTicks = tick;
                    _resFired |= 1;
                    sfx.PlayNow(sfx.ScaleNote(Mathf.Min(10, tick * 10 / 14)), 0.16f);
                }
            }
            if (Fire(1, age, 1.35f) && !_rAuto) _resRecord.text = BestLine();

            // Tiles pop in and count up.
            for (int i = 0; i < 4; i++)
            {
                float ta = (age - (0.55f + 0.1f * i)) / 0.18f;
                Transform tile = _tileValues[i].transform.parent;
                float sc = ta < 0f ? 0.85f : ta < 0.5f ? Mathf.Lerp(0.85f, 1.06f, ta * 2f) : Mathf.Lerp(1.06f, 1f, Mathf.Clamp01((ta - 0.5f) * 2f));
                tile.localScale = Vector3.one * sc;
                tile.GetComponent<Image>().color = new Color(0.35f, 0.3f, 0.8f, 0.85f * Mathf.Clamp01(ta + 1f));
            }
            float cu = Mathf.Clamp01((age - 0.55f) / 0.5f);
            _tileValues[0].text = (_rAcc * 100f * cu).ToString("F2", CultureInfo.InvariantCulture) + "%" + (_rRec.newAccuracy && cu >= 1f ? "  <color=#" + UIFactory.Hex(UIFactory.Gold) + "><size=18>NEW</size></color>" : string.Empty);
            float cu2 = Mathf.Clamp01((age - 0.65f) / 0.5f);
            _tileValues[1].text = Mathf.RoundToInt(_rCombo * cu2) + " / " + _rTotal + (_rRec.newCombo && cu2 >= 1f ? "  <color=#" + UIFactory.Hex(UIFactory.Gold) + "><size=18>NEW</size></color>" : string.Empty);

            _resCountsGroup.alpha = Mathf.Clamp01((age - 1.0f) / 0.25f);
            _resButtonsGroup.alpha = Mathf.Clamp01((age - 1.2f) / 0.25f);
            _resButtonsGroup.interactable = _resButtonsGroup.blocksRaycasts = _resButtonsGroup.alpha > 0.5f; // no taps on invisible buttons

            // Rank ring fills, then the letter slams in.
            Color rc = _rFailed ? UIFactory.Red : RankColor(_rRank);
            float ra = Mathf.Clamp01((age - 1.4f) / 0.35f);
            _resRankFill.color = _rFailed ? Color.Lerp(UIFactory.Red, UIFactory.Pink, ra) : rc;
            _resRankFill.fillAmount = (_rFailed ? _rProgress : 1f) * ra;
            if (_rFailed)
            {
                _resRank.text = string.Empty;
                _resRankPct.text = age >= 1.4f ? Mathf.RoundToInt(_rProgress * 100f * ra) + "%" : string.Empty;
            }
            else
            {
                _resRankPct.text = string.Empty;
                _resRank.text = age >= 1.75f ? _rRank : string.Empty;
                _resRank.fontSize = _rRank.Length > 2 ? 110 : 190;
                _resRank.GetComponent<UIGradient>().Set(Color.white, rc, false);
            }
            float la = (age - 1.75f) / 0.22f;
            float ls = la < 0f ? 2.4f : la < 0.6f ? Mathf.Lerp(2.4f, 0.92f, la / 0.6f) : Mathf.Lerp(0.92f, 1f, Mathf.Clamp01((la - 0.6f) / 0.4f));
            _resRank.rectTransform.localScale = Vector3.one * ls;
            float wa = Mathf.Clamp01((age - 1.75f) / 0.28f);
            _resRankWave.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.8f, wa);
            _resRankWave.color = age >= 1.75f && wa < 1f ? new Color(rc.r, rc.g, rc.b, 0.8f * (1f - wa)) : Color.clear;
            if (Fire(2, age, 1.75f) && sfx != null)
            {
                AudioClip sting = _rFailed || _rAuto ? null : sfx.RankSting(_rRank);
                if (sting != null) sfx.PlayNow(sting, 0.8f);
                else if (_rFailed) sfx.PlayNow(sfx.PowerDown, 0.6f);
            }

            // Stamp and confetti.
            bool stampOn = _rStamp && age >= 2.0f;
            _resStamp.gameObject.SetActive(stampOn);
            if (stampOn)
            {
                _resStampText.text = _rRec.firstClear ? UIFactory.Spaced("first clear") : _rFailed ? "NEW BEST " + Mathf.RoundToInt(_rProgress * 100f) + "%" : UIFactory.Spaced("new best");
                Color stc = _rRec.firstClear ? UIFactory.Cyan : UIFactory.Gold;
                _resStampText.color = stc;
                _resStamp.GetComponent<Image>().color = stc;
                float pa = Mathf.Clamp01((age - 2.0f) / 0.18f);
                _resStamp.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, pa);
            }
            if (_rStamp && Fire(3, age, 2.0f))
            {
                Burst(_resStamp.anchoredPosition + _resRankRt.anchoredPosition, 40);
                if (sfx != null && !quiet) sfx.PlayNow(sfx.NewBest, 0.7f);
            }

            // Stars: newly earned ones pop in gold, owned ones sit in white, missing ones are outlines.
            int owned = _rAuto ? 0 : _rRec.after.stars;
            int fresh = _rAuto ? 0 : _rRec.newStars;
            for (int i = 0; i < 3; i++)
            {
                int bit = 1 << i;
                float at = 2.0f + 0.25f * i;
                bool isNew = (fresh & bit) != 0, has = (owned & bit) != 0;
                Image st = _resStars[i];
                if (isNew && age >= at)
                {
                    float pa = Mathf.Clamp01((age - at) / 0.25f);
                    st.sprite = UIFactory.Star;
                    st.color = UIFactory.Gold;
                    st.rectTransform.localScale = Vector3.one * (pa < 0.6f ? Mathf.Lerp(0f, 1.35f, pa / 0.6f) : Mathf.Lerp(1.35f, 1f, (pa - 0.6f) / 0.4f));
                    if (Fire(4 + i, age, at))
                    {
                        Burst(st.rectTransform.anchoredPosition + _resRankRt.anchoredPosition, 18);
                        if (sfx != null && !quiet)
                        {
                            int[] notes = { 2, 4, 5 };
                            sfx.PlayNow(sfx.ScaleNote(notes[i]), 0.5f);
                            if (i == 2) sfx.PlayNow(sfx.Smash, 0.25f);
                        }
                    }
                }
                else if (has && !isNew)
                {
                    st.sprite = UIFactory.Star;
                    st.color = new Color(1f, 1f, 1f, 0.55f);
                    st.rectTransform.localScale = Vector3.one;
                }
                else
                {
                    st.sprite = UIFactory.StarOutline;
                    st.color = UIFactory.Faint;
                    st.rectTransform.localScale = isNew ? Vector3.zero : Vector3.one;
                }
                _resStarCaps[i].color = isNew || has ? UIFactory.TextColor : UIFactory.Dim;
            }

            if (Fire(7, age, 2.7f) && !_rAuto) _resGoal.text = NextGoal();
            AnimateRewardCard(age, quiet);
            TickConfetti();
        }

        private string BestLine()
        {
            GameSettings.Best before = _rRec.before;
            if (_rFailed)
            {
                return _rRec.newProgress && before.plays > 0
                    ? "NEW BEST  " + Mathf.RoundToInt(before.progress * 100f) + "% → " + Mathf.RoundToInt(_rProgress * 100f) + "%"
                    : "BEST  " + Mathf.RoundToInt(Mathf.Max(before.progress, _rProgress) * 100f) + "%";
            }
            if (!before.Exists) return string.Empty;
            int delta = _rScore - before.score;
            return delta > 0
                ? "BEST  " + before.score.ToString("N0", CultureInfo.InvariantCulture) + " → " + _rScore.ToString("N0", CultureInfo.InvariantCulture) + "   +" + delta.ToString("N0", CultureInfo.InvariantCulture)
                : "<color=#" + UIFactory.Hex(UIFactory.Dim) + ">BEST  " + before.score.ToString("N0", CultureInfo.InvariantCulture) + "   " + delta.ToString("N0", CultureInfo.InvariantCulture) + "</color>";
        }

        /// <summary>The nearest goal still open on this chart.</summary>
        private string NextGoal()
        {
            int stars = _rRec.after.stars;
            float acc = _rRec.after.accuracy;
            if ((stars & GameSettings.StarClear) == 0)
                return "NEXT:  CLEAR IT  ·  " + Mathf.RoundToInt((1f - _rRec.after.progress) * 100f) + "% TO GO";
            float gapA = (stars & GameSettings.StarRankA) == 0 ? (0.9f - acc) / 0.1f : float.MaxValue;
            int misses = MissesThisRun();
            float gapFc = (stars & GameSettings.StarFullCombo) == 0 ? misses / 5f : float.MaxValue;
            if (gapA < float.MaxValue && gapA <= gapFc)
                return "NEXT:  " + ((0.9f - acc) * 100f).ToString("F1", CultureInfo.InvariantCulture) + "% MORE ACCURACY FOR THE RANK-A STAR";
            if (gapFc < float.MaxValue)
                return "NEXT:  " + misses + (misses == 1 ? " MISS" : " MISSES") + " FROM A FULL COMBO";
            if (acc < 0.95f) return "NEXT:  " + ((0.95f - acc) * 100f).ToString("F1", CultureInfo.InvariantCulture) + "% MORE FOR RANK S";
            return _game.SelectedDifficulty == Difficulty.Hard ? "MASTERED  ·  CHASE ALL PERFECT" : "NEXT:  TRY HARD FOR 3 NEW STARS";
        }

        private int MissesThisRun()
        {
            ScoreState s = _game.Score;
            return s != null ? s.Miss : 0;
        }

        private void Burst(Vector2 at, int count)
        {
            float now = Time.unscaledTime;
            int started = 0;
            for (int i = 0; i < _confetti.Count && started < count; i++)
            {
                if (_confetti[i].gameObject.activeSelf && now - _confettiBorn[i] < 1.4f) continue;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float spd = Random.Range(250f, 600f);
                _confettiVel[i] = new Vector2(Mathf.Cos(ang) * spd, Mathf.Abs(Mathf.Sin(ang)) * spd + 200f);
                _confettiBorn[i] = now;
                _confetti[i].anchoredPosition = at;
                _confetti[i].localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                _confetti[i].gameObject.SetActive(true);
                started++;
            }
        }

        private void TickConfetti()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _confetti.Count; i++)
            {
                RectTransform c = _confetti[i];
                if (!c.gameObject.activeSelf) continue;
                float a = now - _confettiBorn[i];
                if (a > 1.4f)
                {
                    c.gameObject.SetActive(false);
                    continue;
                }
                Vector2 v = _confettiVel[i];
                v.y -= 1800f * dt;
                _confettiVel[i] = v;
                c.anchoredPosition += v * dt;
                c.localRotation *= Quaternion.Euler(0f, 0f, 540f * dt);
            }
        }

        // ---------------------------------------------------------------- menu stars

        private Image[] _heroStars;
        private Text _starTotal;
        private readonly Text[] _diffStars = new Text[3];

        private void EnsureMenuStars()
        {
            if (_heroStars != null) return;
            _heroStars = new Image[3];
            RectTransform badge = _heroBadge.rectTransform;
            for (int i = 0; i < 3; i++)
            {
                var rt = UIFactory.Rect(badge, "Star" + i, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2((i - 1) * 30, -34), new Vector2(26, 26));
                _heroStars[i] = UIFactory.Image(rt, UIFactory.Faint, UIFactory.StarOutline);
            }
            var total = UIFactory.At(_menu, "StarTotal", new Vector2(1f, 1f), new Vector2(-36, -40), new Vector2(240, 46));
            UIFactory.Image(UIFactory.Rect(total, "Icon", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(36, 36)), UIFactory.Gold, UIFactory.Star);
            _starTotal = UIFactory.Label(UIFactory.Rect(total, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(24, 0), new Vector2(-48, 0)), "", 26, UIFactory.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int d = 0; d < 3; d++)
            {
                var rt = UIFactory.Rect(_diffButtons[d].transform, "Stars", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -6), new Vector2(54, 24));
                _diffStars[d] = UIFactory.Label(UIFactory.Rect(rt, "N", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-12, 0), new Vector2(-24, 0)), "", 17, UIFactory.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Image(UIFactory.Rect(rt, "Icon", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(20, 20)), UIFactory.Gold, UIFactory.Star);
                _diffStars[d].transform.parent.gameObject.SetActive(false);
            }
        }

        private void RefreshMenuStars(SongData song, GameSettings.Best best)
        {
            EnsureMenuStars();
            for (int i = 0; i < 3; i++)
            {
                bool has = (best.stars & (1 << i)) != 0;
                _heroStars[i].sprite = has ? UIFactory.Star : UIFactory.StarOutline;
                _heroStars[i].color = has ? UIFactory.Gold : UIFactory.Faint;
            }
            int all = 0;
            for (int d = 0; d < 3; d++)
            {
                int n = GameSettings.Stars(song, (Difficulty)d);
                all += n;
                _diffStars[d].text = n.ToString();
                _diffStars[d].transform.parent.gameObject.SetActive(n > 0);
            }
            if (all >= 9) _heroIndex.text += "    <color=#" + UIFactory.Hex(UIFactory.Gold) + ">" + UIFactory.Spaced("mastered") + "</color>";
            _starTotal.text = GameSettings.TotalStars(_game.Library) + " / " + (_game.Library.songs.Count * 9);

            // Carousel dots: tinted by the stars on the selected difficulty.
            var songs = _game.Library.songs;
            Color accent = Ldr(song.accent);
            for (int i = 0; i < _dots.Count && i < songs.Count; i++)
            {
                if (i == _game.SelectedSong || songs[i] == null) continue;
                int n = GameSettings.Stars(songs[i], _game.SelectedDifficulty);
                _dots[i].color = n >= 3 ? UIFactory.Gold : n > 0 ? new Color(accent.r, accent.g, accent.b, 0.7f) : UIFactory.Faint;
            }
        }

        private string MenuInfoLine(int notes, GameSettings.Best best)
        {
            if (!best.Exists)
            {
                return notes + " NOTES" + (best.plays > 0 ? "   ·   BEST " + Mathf.RoundToInt(best.progress * 100f) + "%" : "   ·   NO RECORD YET");
            }
            string line = notes + " NOTES   ·   BEST " + (best.accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%" + (best.fullCombo ? "   FC" : string.Empty);
            float next = ScoreState.NextRankThreshold(best.accuracy);
            if (next < 1f)
            {
                string letter = next >= 0.95f ? "S" : next >= 0.9f ? "A" : next >= 0.8f ? "B" : "C";
                line += "   ·   " + ((next - best.accuracy) * 100f).ToString("F1", CultureInfo.InvariantCulture) + "% TO " + letter;
            }
            return line;
        }
    }
}
