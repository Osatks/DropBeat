using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Runs the song's twist while it plays: banners when it kicks in, camera roll (drift, flip, warp),
    /// beat effects (surge lightning, dance-cam flashes, sonar pings), and its scoring (gold rings,
    /// overtime, the boss's health). Everything is a function of song time, like the rest of the game.
    /// </summary>
    public sealed class TwistDirector
    {
        private SongData _song;
        private Chart _chart;
        private GameUI _ui;
        private CameraRig _cam;
        private BackgroundFX _bg;
        private SfxPlayer _sfx;

        private int _nextSpan;
        private double _lastAnnouncedEnd;
        private int _lastBeat;
        private int _goldHit;
        private float _bossHp, _bossMax;
        private bool _bossDone;
        private string _meterText;
        private float _meterFill;

        public SongTwist Twist => _chart != null ? _chart.twist : SongTwist.None;

        /// <summary>Overtime finale: a minimum overdrive look (eased), combined with the player's own overdrive.</summary>
        public float OverdriveFloor { get; private set; }

        public void Begin(SongData song, Chart chart, LevelLayout layout, GameUI ui, CameraRig cam, BackgroundFX bg, SfxPlayer sfx)
        {
            _song = song;
            _chart = chart;
            _ui = ui;
            _cam = cam;
            _bg = bg;
            _sfx = sfx;
            _nextSpan = 0;
            _lastAnnouncedEnd = double.NegativeInfinity;
            _lastBeat = int.MinValue;
            _goldHit = 0;
            _bossDone = false;
            _meterText = null;
            _meterFill = -2f;

            // The boss has about 70% of the finale's rings in health: mostly perfect play beats it.
            int finale = 0;
            foreach (PlayNote n in layout.Notes) if (ActiveAt(n.time)) finale++;
            _bossMax = _bossHp = Mathf.Max(8f, finale * 0.7f);

            cam.TwistRoll = 0f;
            OverdriveFloor = 0f;
            RingView.SonarPing = 0f;
            ui.SetTwist(SongTwists.Title(Twist), SongTwists.Blurb(Twist), SongTwists.Tint(Twist));
        }

        public void End()
        {
            if (_cam != null) _cam.TwistRoll = 0f; // the camera eases back upright
            OverdriveFloor = 0f;
            RingView.SonarPing = 0f;
            _chart = null;
        }

        public bool ActiveAt(double t) => _chart != null && TwistDesigner.InSpans(_chart.twistSpans, t);

        /// <summary>Score multiplier the twist gives this judgement.</summary>
        public int Multiplier(PlayNote n, Grade g)
        {
            if (g == Grade.Miss) return 1;
            int m = n.golden ? 5 : 1;
            if (Twist == SongTwist.Overtime && ActiveAt(n.time)) m *= 2;
            return m;
        }

        /// <summary>Label shown with the judgement (null = the normal grade text).</summary>
        public string Label(PlayNote n, Grade g) => n.golden && g != Grade.Miss ? "GOLD x5" : null;

        public void OnJudged(PlayNote n, Grade g, ScoreState score)
        {
            switch (Twist)
            {
                case SongTwist.GoldenRings:
                    if (!n.golden || g == Grade.Miss) break;
                    _goldHit++;
                    if (_goldHit == _chart.goldenCount)
                    {
                        score.AddBonus(5000);
                        _ui.ShowMilestone("ALL GOLD!  +5000");
                        _sfx.PlayNow(_sfx.Fanfare, 0.7f);
                    }
                    else _sfx.PlayNow(_sfx.PowerUp, 0.35f);
                    break;

                case SongTwist.Overtime:
                    if (g == Grade.Miss && ActiveAt(n.time)) score.ExtraMissDamage();
                    break;

                case SongTwist.BossFight:
                    if (_bossDone || !ActiveAt(n.time)) break;
                    if (g == Grade.Miss)
                    {
                        _bossHp = Mathf.Min(_bossMax, _bossHp + 0.5f); // the boss heals on your misses
                        break;
                    }
                    _bossHp -= g == Grade.Perfect ? 1f : g == Grade.Great ? 0.7f : 0.4f;
                    _cam.Shake(0.12f);
                    if (_bossHp <= 0f)
                    {
                        _bossDone = true;
                        _bossHp = 0f;
                        score.AddBonus(10000);
                        _ui.ShowMilestone("BOSS DEFEATED!  +10000");
                        _sfx.PlayNow(_sfx.Fanfare, 0.8f);
                        _sfx.PlayNow(_sfx.Impact, 0.8f);
                        _cam.Kick(2.5f);
                        _cam.Shake(0.8f);
                        _bg.HitFlash(2f);
                    }
                    break;
            }
        }

        public void Tick(double t)
        {
            if (_chart == null) return;
            var spans = _chart.twistSpans;
            double spb = _song.SecondsPerBeat;

            // Banner when the twist kicks in (not for every short burst of the same thing).
            while (_nextSpan < spans.Count && spans[_nextSpan].start <= t)
            {
                TwistSpan s = spans[_nextSpan++];
                if (t > s.end) continue;
                if (s.start - _lastAnnouncedEnd > spb * 8) Announce();
                _lastAnnouncedEnd = s.end;
            }

            bool on = ActiveAt(t);
            float beatPos = BeatPosition(t);

            // Camera roll.
            float roll = 0f;
            switch (Twist)
            {
                case SongTwist.Drift:
                    roll = 12f * Mathf.Sin(beatPos * Mathf.PI * 2f / 16f) * Envelope(t, spb * 2);
                    break;
                case SongTwist.Flip:
                    roll = 180f * Envelope(t, spb);
                    break;
                case SongTwist.Warp:
                    foreach (TwistSpan s in spans)
                    {
                        if (!s.Contains(t)) continue;
                        // Whole turns over the span, eased, so it ends back upright.
                        int turns = Mathf.Max(1, Mathf.RoundToInt((float)((s.end - s.start) / spb) / 16f));
                        roll = 360f * turns * Mathf.SmoothStep(0f, 1f, (float)((t - s.start) / (s.end - s.start)));
                        break;
                    }
                    break;
            }
            _cam.TwistRoll = roll;

            // Beat effects.
            int k = _song.BeatIndexAt(t);
            if (k != _lastBeat)
            {
                _lastBeat = k;
                bool down = _song.IsDownbeat(k);
                if (on)
                {
                    switch (Twist)
                    {
                        case SongTwist.Surge:
                            if (down)
                            {
                                _bg.HitFlash(1.1f);
                                _cam.Shake(0.2f);
                            }
                            break;
                        case SongTwist.DanceCam:
                            _bg.HitFlash(down ? 0.7f : 0.35f);
                            _cam.Kick(down ? 0.9f : 0.5f);
                            break;
                        case SongTwist.BossFight:
                            if (down && !_bossDone) _cam.Kick(0.7f); // the boss stomps
                            break;
                        case SongTwist.Sonar:
                            if (down) _bg.HitFlash(0.35f);
                            break;
                    }
                }
            }

            if (Twist == SongTwist.Sonar)
            {
                // A ping on every bar lights the hidden rings for about a beat.
                int bar = k - (((k - _song.downbeatPhase) % 4) + 4) % 4;
                float since = (float)((t - _song.BeatTime(bar)) / spb);
                RingView.SonarPing = Mathf.Exp(-since * 1.5f);
            }

            OverdriveFloor = Mathf.MoveTowards(OverdriveFloor, Twist == SongTwist.Overtime && on ? 0.45f : 0f, Time.unscaledDeltaTime * 1.5f);

            UpdateMeter(t, on);
        }

        private void Announce()
        {
            string text;
            switch (Twist)
            {
                case SongTwist.Overtime: text = "OVERTIME!  x2"; break;
                case SongTwist.BossFight: text = "BOSS FIGHT!"; break;
                case SongTwist.Surge: text = "SURGE!"; break;
                case SongTwist.Flip: text = "GRAVITY FLIP!"; break;
                case SongTwist.Warp: text = "WARP!"; break;
                case SongTwist.Stampede: text = "STAMPEDE!"; break;
                default: text = SongTwists.Title(Twist); break;
            }
            _ui.ShowMilestone(text);
            _sfx.PlayNow(_sfx.Whoosh, 0.5f);
            if (Twist == SongTwist.Overtime || Twist == SongTwist.BossFight) _sfx.PlayNow(_sfx.PowerUp, 0.6f);
        }

        private void UpdateMeter(double t, bool on)
        {
            string text = null;
            float fill = -1f;
            switch (Twist)
            {
                case SongTwist.GoldenRings:
                    text = "GOLD   " + _goldHit + " / " + _chart.goldenCount;
                    fill = _chart.goldenCount > 0 ? _goldHit / (float)_chart.goldenCount : 0f;
                    break;
                case SongTwist.BossFight:
                    if (_bossDone) { text = "BOSS DEFEATED"; fill = 0f; }
                    else if (on) { text = "BOSS"; fill = _bossHp / _bossMax; }
                    else if (_chart.twistSpans.Count > 0 && t > _chart.twistSpans[0].end) { text = "BOSS ESCAPED"; fill = _bossHp / _bossMax; }
                    else { text = "BOSS AHEAD"; fill = 1f; }
                    break;
                case SongTwist.Overtime:
                    if (on)
                    {
                        foreach (TwistSpan s in _chart.twistSpans)
                        {
                            if (!s.Contains(t)) continue;
                            text = "OVERTIME   x2";
                            fill = Mathf.Clamp01((float)((s.end - t) / (s.end - s.start)));
                        }
                    }
                    break;
            }
            // Quantise so the bar does not rebuild its text every frame.
            fill = fill < 0f ? -1f : Mathf.Round(fill * 100f) / 100f;
            if (text == _meterText && Mathf.Approximately(fill, _meterFill)) return;
            _meterText = text;
            _meterFill = fill;
            _ui.SetTwistMeter(text, fill);
        }

        /// <summary>0..1: inside a span (eased in over <paramref name="ramp"/> before it starts and out before it ends).</summary>
        private float Envelope(double t, double ramp)
        {
            float best = 0f;
            foreach (TwistSpan s in _chart.twistSpans)
            {
                if (t < s.start - ramp || t > s.end) continue;
                float a = Mathf.SmoothStep(0f, 1f, (float)((t - (s.start - ramp)) / ramp));
                float b = Mathf.SmoothStep(0f, 1f, (float)((s.end - t) / ramp));
                best = Mathf.Max(best, Mathf.Min(a, b));
            }
            return best;
        }

        private float BeatPosition(double t)
        {
            int k = _song.BeatIndexAt(t);
            double b0 = _song.BeatTime(k), b1 = _song.BeatTime(k + 1);
            return k + (float)((t - b0) / System.Math.Max(1e-4, b1 - b0));
        }
    }
}
