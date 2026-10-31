using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Runs the song's twist while it plays: banners when it kicks in, camera roll (drift, flip, warp),
    /// beat effects (surge lightning, dance-cam flashes, sonar pings), the boss (its health, its view and
    /// the camera on its arena) and the gold-ring score. Everything is a function of song time, like the
    /// rest of the game.
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
        private BossView _boss;
        private LevelLayout _layout;
        private double _rushStart;
        private bool _rushAnnounced;
        private int _nextPhase, _nextShot;
        private readonly System.Collections.Generic.List<double> _shots = new System.Collections.Generic.List<double>();

        public SongTwist Twist => _chart != null ? _chart.twist : SongTwist.None;

        /// <summary>The boss of this run was beaten (read before <see cref="End"/>).</summary>
        public bool BossDefeated => _chart != null && _chart.twist == SongTwist.BossFight && _bossDone;

        /// <summary>A minimum overdrive look (eased) a twist can ask for; none of the current twists do.</summary>
        public float OverdriveFloor { get; private set; }

        public void Begin(SongData song, Chart chart, LevelLayout layout, GameUI ui, CameraRig cam, BackgroundFX bg, SfxPlayer sfx,
                          VisualAssets assets, Transform fxParent)
        {
            _layout = layout;
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

            if (Twist == SongTwist.BossFight && layout.HasBoss && chart.twistSpans.Count > 0)
            {
                if (_boss == null) _boss = BossView.Create(fxParent, assets);
                TwistSpan arena = chart.twistSpans[chart.twistSpans.Count - 1];
                _boss.Begin(layout.BossCenter, LevelLayout.BossOrbitPerBeat, arena.start, arena.end, song.SecondsPerBeat);
            }
            else if (_boss != null) _boss.Clear();
            // The boss's attacks: a banner for each (the first is the fight's own), a muzzle flash per fired ring.
            _nextPhase = 1;
            _nextShot = 0;
            _shots.Clear();
            foreach (PlayNote n in layout.Notes)
            {
                if (n.fx == NoteFx.Boss && n.bossMove == BossMove.Barrage) _shots.Add(n.time - n.fxLead);
            }
            _shots.Sort();
            _rushStart = Twist == SongTwist.Overtime ? TwistDesigner.OvertimeRushStart(song, chart) : double.PositiveInfinity;
            _rushAnnounced = false;

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
            if (_boss != null) _boss.Clear();
            _chart = null;
        }

        /// <summary>
        /// Where the camera should look instead of following the ball, and how much (0..1): the boss arena
        /// is framed whole, easing in over the four beats before the fight.
        /// </summary>
        public float CameraFocus(double t, out Vector3 focus)
        {
            focus = Vector3.zero;
            if (Twist != SongTwist.BossFight || _layout == null || !_layout.HasBoss || _chart.twistSpans.Count == 0) return 0f;
            TwistSpan arena = _chart.twistSpans[_chart.twistSpans.Count - 1];
            double spb = _song.SecondsPerBeat;
            // The side view looks a little above its focus; this puts the boss in the middle of the screen.
            focus = _layout.BossCenter + new Vector3(0f, -1.1f, 0f);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)((t - (arena.start - spb * 4.0)) / (spb * 4.0))));
        }

        public bool ActiveAt(double t) => _chart != null && TwistDesigner.InSpans(_chart.twistSpans, t);

        /// <summary>Score multiplier the twist gives this judgement.</summary>
        public int Multiplier(PlayNote n, Grade g)
        {
            if (g == Grade.Miss) return 1;
            return n.golden ? 5 : 1;
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

                case SongTwist.BossFight:
                    if (_bossDone || !ActiveAt(n.time)) break;
                    if (g == Grade.Miss)
                    {
                        _bossHp = Mathf.Min(_bossMax, _bossHp + 0.5f); // the boss heals on your misses
                        break;
                    }
                    _bossHp -= g == Grade.Perfect ? 1f : g == Grade.Great ? 0.7f : 0.4f;
                    _cam.Shake(0.12f);
                    double at = n.judgedAt > 0.0 ? n.judgedAt : n.time;
                    if (_boss != null) _boss.Hit(n.position, n.twistTint.a > 0f ? n.twistTint : Color.white, g == Grade.Perfect ? 1f : g == Grade.Great ? 0.7f : 0.45f, at);
                    if (_bossHp <= 0f)
                    {
                        if (_boss != null) _boss.Defeat(at);
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

            OverdriveFloor = Mathf.MoveTowards(OverdriveFloor, 0f, Time.unscaledDeltaTime * 1.5f);

            if (_boss != null && Twist == SongTwist.BossFight)
            {
                _boss.Tick(t, beatPos, _song.IsDownbeat(k), _bossMax > 0f ? _bossHp / _bossMax : 0f);
                var phases = _layout.BossPhases;
                while (_nextPhase < phases.Count && phases[_nextPhase].Key <= t)
                {
                    BossMove move = phases[_nextPhase++].Value;
                    if (_bossDone) continue;
                    _ui.ShowMilestone(AttackName(move));
                    _sfx.PlayNow(_sfx.Whoosh, 0.5f);
                    _boss.Roar(t);
                    _cam.Kick(1f);
                }
                while (_nextShot < _shots.Count && _shots[_nextShot] <= t)
                {
                    _nextShot++;
                    if (!_bossDone) _boss.Fire(t);
                }
            }

            // Overtime: the last stretch, where the rings start sliding in twice as fast.
            if (!_rushAnnounced && t >= _rushStart)
            {
                _rushAnnounced = true;
                _ui.ShowMilestone("OVERTIME!  FASTER");
                _sfx.PlayNow(_sfx.PowerUp, 0.6f);
                _bg.HitFlash(1.2f);
                _cam.Kick(1.2f);
            }

            UpdateMeter(t, on);
        }

        private static string AttackName(BossMove m)
        {
            switch (m)
            {
                case BossMove.Barrage: return "BARRAGE!";
                case BossMove.Sweep: return "SWEEP!";
                case BossMove.Spiral: return "SPIRAL!";
                default: return "ORBIT!";
            }
        }

        private void Announce()
        {
            string text;
            switch (Twist)
            {
                case SongTwist.Overtime: text = "SLIDE RUSH!"; break;
                case SongTwist.BossFight: text = "BOSS FIGHT!"; break;
                case SongTwist.Surge: text = "SURGE!"; break;
                case SongTwist.Flip: text = "GRAVITY FLIP!"; break;
                case SongTwist.Warp: text = "WARP!"; break;
                case SongTwist.Stampede: text = "STAMPEDE!"; break;
                default: text = SongTwists.Title(Twist); break;
            }
            _ui.ShowMilestone(text);
            _sfx.PlayNow(_sfx.Whoosh, 0.5f);
            if (Twist == SongTwist.BossFight) _sfx.PlayNow(_sfx.PowerUp, 0.6f);
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
