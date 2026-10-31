using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Game feel and reward loop: the streak ladder (level-ups land on the downbeat with a sting and a
    /// music layer), the hit riff, misses scaled to the streak lost, the overdrive payout, 8-bar section
    /// clears, and the finale on the last note. All sounds are either played at once on a hit (as before)
    /// or scheduled on the song's beat grid; nothing here changes timing or judging.
    /// </summary>
    public sealed partial class GameController
    {
        // Streak ladder (Easy / Normal / Hard): GROOVE, FEVER, BLAZE, LEGEND.
        private static readonly int[][] TierAt = { new[] { 6, 12, 24, 48 }, new[] { 10, 20, 40, 80 }, new[] { 15, 30, 60, 120 } };
        // Two 2-bar riffs (scale degrees per beat) that the hits walk through, alternating every 4 bars.
        private static readonly int[][] Riff = { new[] { 0, 1, 2, 3, 4, 3, 2, 3 }, new[] { 0, 2, 1, 3, 3, 4, 2, 1 } };

        private ImpactFX _impactFx;
        private int _tier;
        private float _tierVis;
        private int _pendingTier;
        private int _pendingTierBeat = int.MinValue;
        private int _pendingTierVoice = -1;
        private int _nextFifty;
        private bool _requeueStings;
        private int _lastStep;
        private int _perfectChain;

        // Misses: how deep and how long the music dips (0 Hz = no low-pass).
        private float _muffleHz;
        private float _muffleDur = 1f;

        // Overdrive
        private int _odBonus;
        private int _powerUpBeat = int.MinValue;

        // Sections (8-bar phrases)
        private struct SectionAward
        {
            public int section;
            public double at;
            public int kind; // 1 clean, 2 flawless, 3 broken
            public int bonus;
            public int voice;
        }
        private int[] _noteSection = new int[0];
        private int[] _secTotal = new int[0], _secDone = new int[0], _secMiss = new int[0], _secPerfect = new int[0], _secPoints = new int[0];
        private readonly List<double> _secStart = new List<double>(), _secEnd = new List<double>();
        private readonly List<SectionAward> _awards = new List<SectionAward>();
        private int _curSection = -1;

        // Finale
        private bool _finaleDone;
        private double _finaleAt = -1.0;
        private int _finaleBeat = int.MinValue;
        private bool _finaleShown;
        private bool _finaleFc, _finaleAp;
        private int _finaleVoice = -1;
        private readonly List<float> _finaleWaves = new List<float>();
        private int _fireworks;
        private double _fireworksAt;

        // Results
        private float _failProgress;
        private bool _healthHitZero;
        private float _resultsStartVolume = 1f;

        public SfxPlayer Sfx => sfx;

        private static float MusicGainOf(SongData s) => s != null ? s.MusicGain : 1f;

        private int TierOf(int combo)
        {
            int[] at = TierAt[Mathf.Clamp((int)_difficulty, 0, 2)];
            int t = 0;
            while (t < at.Length && combo >= at[t]) t++;
            return t;
        }

        private int NextDownbeat(double after)
        {
            int k = _song.BeatIndexAt(after) + 1;
            while (!_song.IsDownbeat(k)) k++;
            return k;
        }

        private float BeatPhase(double t)
        {
            int k = _song.BeatIndexAt(t);
            double b0 = _song.BeatTime(k), b1 = _song.BeatTime(k + 1);
            return Mathf.Clamp01((float)((t - b0) / System.Math.Max(1e-3, b1 - b0)));
        }

        /// <summary>Per-song reset, after the layout and the HUD are set up.</summary>
        private void ResetFeel()
        {
            _continueUsed = false;
            _continueAdUp = false;
            _tier = 0;
            _tierVis = 0f;
            _pendingTier = 0;
            _pendingTierBeat = int.MinValue;
            _pendingTierVoice = -1;
            _nextFifty = 0;
            _requeueStings = false;
            _lastStep = 0;
            _perfectChain = 0;
            _muffleHz = 0f;
            _odBonus = 0;
            _powerUpBeat = int.MinValue;
            _finaleDone = false;
            _finaleAt = -1.0;
            _finaleBeat = int.MinValue;
            _finaleShown = false;
            _finaleVoice = -1;
            _finaleWaves.Clear();
            _fireworks = 0;
            _healthHitZero = false;
            _failProgress = 0f;
            _awards.Clear();
            _curSection = -1;
            MixBus.Reset();
            if (_impactFx != null) _impactFx.Clear();
            BuildSections();
        }

        // ---------------------------------------------------------------- sections

        private void BuildSections()
        {
            var notes = _layout.Notes;
            _secStart.Clear();
            _secEnd.Clear();
            _noteSection = new int[notes.Count];
            if (notes.Count == 0)
            {
                _secTotal = _secDone = _secMiss = _secPerfect = _secPoints = new int[0];
                ui.SetupSections(_secStart, _secEnd);
                return;
            }
            // Phrase of every note, then sections = phrases with at least 6 notes (smaller ones join the previous).
            var phraseOf = new int[notes.Count];
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < notes.Count; i++)
            {
                int p = Mathf.Max(0, ChartDesigner.PhraseOf(_song, _song.BeatIndexAt(notes[i].time + 1e-4)));
                phraseOf[i] = p;
                counts[p] = counts.TryGetValue(p, out int c) ? c + 1 : 1;
            }
            int lastPhrase = -1, section = -1;
            for (int i = 0; i < notes.Count; i++)
            {
                int p = phraseOf[i];
                if (p != lastPhrase)
                {
                    lastPhrase = p;
                    if (section < 0 || counts[p] >= 6)
                    {
                        section++;
                        _secStart.Add(section == 0 ? System.Math.Min(notes[0].time, PhraseStart(p)) : PhraseStart(p));
                        _secEnd.Add(PhraseStart(p + 1));
                    }
                    else _secEnd[section] = PhraseStart(p + 1);
                }
                _noteSection[i] = section;

            }
            int n = section + 1;
            _secEnd[n - 1] = System.Math.Max(_secEnd[n - 1], _lastNoteTime + 0.05);
            _secTotal = new int[n];
            _secDone = new int[n];
            _secMiss = new int[n];
            _secPerfect = new int[n];
            _secPoints = new int[n];
            for (int i = 0; i < notes.Count; i++) _secTotal[_noteSection[i]]++;
            ui.SetupSections(_secStart, _secEnd);
        }

        private double PhraseStart(int p) => _song.BeatTime(_song.downbeatPhase + 32 * p);

        private void SectionUpdate(PlayNote n, Grade g, int points, double now)
        {
            if (n.index < 0 || n.index >= _noteSection.Length) return;
            int s = _noteSection[n.index];
            _secDone[s]++;
            if (g == Grade.Miss) _secMiss[s]++;
            else if (g == Grade.Perfect) _secPerfect[s]++;
            _secPoints[s] += points;
            if (_secDone[s] != _secTotal[s]) return;

            int kind = _secMiss[s] > 0 ? 3 : _secPerfect[s] == _secTotal[s] ? 2 : 1;
            int bonus = kind == 3 ? 0 : Mathf.RoundToInt(_secPoints[s] * (kind == 2 ? 0.25f : 0.10f));
            bool last = s == _secTotal.Length - 1;
            var award = new SectionAward
            {
                section = s,
                kind = kind,
                bonus = bonus,
                at = kind == 3 || last ? now : System.Math.Max(now + 0.05, _secEnd[s]),
                voice = -1
            };
            // The last section's payout is absorbed by the finale (its sound and banner).
            if (kind != 3 && !last) award.voice = ScheduleSectionSting(award);
            _awards.Add(award);
        }

        private int ScheduleSectionSting(SectionAward a)
        {
            if (!GameSettings.HitSound || _finaleDone) return -1;
            double dsp = clock.StartDsp + a.at;
            // Sting priority on a shared beat: the section sting wins over a tier sting.
            if (_pendingTier > 0 && System.Math.Abs(_song.BeatTime(_pendingTierBeat) - a.at) < 0.05)
            {
                sfx.CancelScheduled(_pendingTierVoice);
                _pendingTierVoice = -1;
            }
            if (dsp - AudioSettings.dspTime < 0.01)
            {
                sfx.PlayNow(sfx.SectionSting(a.kind == 2), 0.75f);
                return -1;
            }
            return sfx.PlayAt(sfx.SectionSting(a.kind == 2), dsp, 0.75f);
        }

        // ---------------------------------------------------------------- streak ladder

        private void QueueTierUp(int tier, double now)
        {
            _pendingTier = tier;
            _pendingTierBeat = NextDownbeat(now + 0.06);
            _pendingTierVoice = -1;
            if (!GameSettings.HitSound || _finaleDone) return;
            double at = _song.BeatTime(_pendingTierBeat);
            foreach (SectionAward a in _awards)
            {
                if (a.voice >= 0 && System.Math.Abs(a.at - at) < 0.05) return; // a section sting owns this beat
            }
            _pendingTierVoice = sfx.PlayAt(sfx.TierSting(Mathf.Clamp(tier - 1, 0, 3)), clock.StartDsp + at, 0.7f);
        }

        private void FireTierUp()
        {
            int t = _pendingTier;
            _pendingTier = 0;
            _pendingTierVoice = -1;
            if (t > 4)
            {
                // Every +50 above the top tier: a banner on the bar line.
                ui.ShowBanner("+" + ((t - 4) * 50) + "  STREAK", UIFactory.Gold, Color.white, 0);
                MixBus.Duck(5f, 0.25f);
                return;
            }
            _tier = t;
            MixBus.Duck(5f, 0.25f);
            background.Shockwave(ball.transform.position, GameUI.TierColor(_tier), 1.5f);
            background.HitFlash(1.3f);
            cameraRig.Kick(1.4f);
            ui.TierUp(_tier);
            _layerBeat = _pendingTierBeat;
            if (_tier >= 4) _nextFifty = TierAt[Mathf.Clamp((int)_difficulty, 0, 2)][3] + 50;
        }

        /// <summary>Hit note on the riff: follows the bar, resolves on the tonic each 4-bar phrase, an octave up from FEVER.</summary>
        private int RiffStep(PlayNote n)
        {
            if (n.partner != null && n.partner.judged) return Mathf.Min(_lastStep + 3, 10); // second half of a dual: a fifth above
            int k = _song.BeatIndexAt(n.time + 1e-4);
            int rel = k - _song.downbeatPhase;
            int b = ((rel % 8) + 8) % 8;
            int phrase = ((rel >= 0 ? rel : rel - 15) / 16) & 1;
            int deg = Riff[phrase][b] + (n.kind == NoteKind.Offbeat ? 1 : 0);
            if (((rel % 16) + 16) % 16 == 0 && n.kind != NoteKind.Offbeat) deg = 0;
            return Mathf.Clamp(deg + (_tier >= 2 ? 5 : 0), 0, 10);
        }

        private void Punch(PlayNote n, float amp)
        {
            BallPath path = _layout.MainPath;
            if (n.ball > 0)
            {
                foreach (BallPath p in _layout.Paths)
                {
                    if (p.ball == n.ball && n.time >= p.Start && n.time <= p.End) path = p;
                }
            }
            if (path == null) return;
            path.Evaluate(n.time - 0.01, out Vector3 v);
            cameraRig.Punch(v, amp);
        }

        // ---------------------------------------------------------------- misses

        private void MissFeel(PlayNote n, int lost)
        {
            int[] at = TierAt[Mathf.Clamp((int)_difficulty, 0, 2)];
            float glitch, shake;
            if (lost < at[0])
            {
                // A small slip: a short dip, the song stays clear.
                _muffleHz = 0f;
                MixBus.Duck(1.9f, 0.1f);
                glitch = 0.3f;
                shake = 0.2f;
            }
            else if (lost < at[2])
            {
                _muffleHz = 1600f;
                _muffleDur = 0.7f;
                _muffleAt = Time.realtimeSinceStartupAsDouble;
                glitch = 0.45f;
                shake = 0.35f;
            }
            else
            {
                _muffleHz = 900f;
                _muffleDur = 1.0f;
                _muffleAt = Time.realtimeSinceStartupAsDouble;
                glitch = 0.5f;
                shake = 0.5f;
                ui.ComboBreak(lost);
            }
            cameraRig.Shake(shake);
            if (GameSettings.HitSound) sfx.PlayNow(sfx.Miss, glitch);
            sfx.FadeLayer(0.25f);
            _layerBeat = int.MinValue;
            _tier = 0;
            _nextFifty = 0;
            if (_pendingTier > 0)
            {
                sfx.CancelScheduled(_pendingTierVoice);
                _pendingTier = 0;
                _pendingTierVoice = -1;
            }
        }

        private double NextPendingNoteTime(double t)
        {
            var notes = _layout.Notes;
            for (int i = _nextJudge; i < notes.Count; i++)
            {
                if (!notes[i].judged) return notes[i].time;
            }
            return t + 100.0;
        }

        // ---------------------------------------------------------------- finale

        private void CheckFinale(Vector3 pos, double now)
        {
            if (_finaleDone || _nextJudge < _layout.Notes.Count || _holds.Count > 0 || _swipeNote != null) return;
            if (!(_autoplay || GameSettings.NoFail || _score.Health > 0f)) return;
            BeginFinale(pos, now);
        }

        private void BeginFinale(Vector3 pos, double now)
        {
            _finaleDone = true;
            _finaleFc = _score.FullCombo;
            _finaleAp = _score.TotalNotes > 0 && _score.Perfect == _score.TotalNotes;

            cameraRig.Kick(2.4f);
            cameraRig.Shake(0.3f);
            background.HitFlash(2f);
            _finaleWaves.Clear();
            _finaleWaves.Add(Time.unscaledTime);
            _finaleWaves.Add(Time.unscaledTime + 0.08f);
            _finaleWaves.Add(Time.unscaledTime + 0.16f);
            Color gold = new Color(3f, 2.6f, 1.6f, 1f);
            Color a = _finaleAp ? _song.accent : _finaleFc ? gold : visuals.perfect;
            _burst.EmitSparks(pos, Quaternion.identity, 1.1f, a, 30);
            _burst.EmitSparks(pos, Quaternion.Euler(90f, 0f, 0f), 1.1f, _finaleAp ? _song.accent2 : a, 30);
            _burst.Emit(pos, _finaleAp ? gold : a, 40);
            _impactFx.Play(pos, now, 1.6f);
            MixBus.Duck(6f, 0.5f);

            _finaleAt = now;
            _finaleBeat = _song.BeatIndexAt(now + 0.15) + 1;
            // The finale owns its beat: drop any tier or section sting still waiting.
            sfx.CancelScheduled(_pendingTierVoice);
            _pendingTierVoice = -1;
            for (int i = 0; i < _awards.Count; i++)
            {
                SectionAward aw = _awards[i];
                if (aw.voice >= 0 && aw.at > now - 0.01)
                {
                    sfx.CancelScheduled(aw.voice);
                    aw.voice = -1;
                    _awards[i] = aw;
                }
            }
            if (GameSettings.HitSound) _finaleVoice = sfx.PlayAt(sfx.Finale(_finaleFc), clock.StartDsp + _song.BeatTime(_finaleBeat), 0.9f);
        }

        // ---------------------------------------------------------------- per frame

        private void TickFeel(double t)
        {
            _tierVis = Mathf.MoveTowards(_tierVis, _tier, Time.unscaledDeltaTime * 3f);

            if (_requeueStings)
            {
                // Pausing stopped every scheduled sound: put the ones still ahead back on their beats.
                _requeueStings = false;
                if (_pendingTier > 0 && GameSettings.HitSound && !_finaleDone)
                    _pendingTierVoice = sfx.PlayAt(sfx.TierSting(Mathf.Clamp(_pendingTier - 1, 0, 3)), clock.StartDsp + _song.BeatTime(_pendingTierBeat), 0.7f);
                for (int i = 0; i < _awards.Count; i++)
                {
                    SectionAward aw = _awards[i];
                    if (aw.voice < 0 || aw.at <= t) continue;
                    aw.voice = ScheduleSectionSting(aw);
                    _awards[i] = aw;
                }
                if (_powerUpBeat != int.MinValue && clock.StartDsp + _song.BeatTime(_powerUpBeat) - 0.65 > AudioSettings.dspTime + 0.02)
                    sfx.PlayAt(sfx.PowerUp, clock.StartDsp + _song.BeatTime(_powerUpBeat) - 0.65, 1f);
                if (_finaleDone && _finaleBeat != int.MinValue && _song.BeatTime(_finaleBeat) > t && GameSettings.HitSound)
                    _finaleVoice = sfx.PlayAt(sfx.Finale(_finaleFc), clock.StartDsp + _song.BeatTime(_finaleBeat), 0.9f);
            }

            if (_pendingTier > 0 && t >= _song.BeatTime(_pendingTierBeat)) FireTierUp();
            if (_tier >= 4 && _nextFifty > 0 && _score.Combo >= _nextFifty && _pendingTier == 0)
            {
                _pendingTier = 4 + (_nextFifty - TierAt[Mathf.Clamp((int)_difficulty, 0, 2)][3]) / 50;
                _pendingTierBeat = NextDownbeat(t + 0.06);
                _nextFifty += 50;
                if (GameSettings.HitSound) _pendingTierVoice = sfx.PlayAt(sfx.TierSting(3), clock.StartDsp + _song.BeatTime(_pendingTierBeat), 0.6f);
            }

            for (int i = _awards.Count - 1; i >= 0; i--)
            {
                SectionAward aw = _awards[i];
                if (t < aw.at) continue;
                _awards.RemoveAt(i);
                if (aw.kind != 3)
                {
                    _score.AddBonus(aw.bonus);
                    if (aw.section != _secTotal.Length - 1)
                    {
                        MixBus.Duck(5f, 0.25f);
                        background.Shockwave(ball.transform.position, aw.kind == 2 ? UIFactory.Gold : UIFactory.Cyan, 1.3f);
                        cameraRig.Kick(1.2f);
                        background.HitFlash(1.2f);
                    }
                }
                ui.SectionResult(aw.section, aw.kind, aw.bonus);
            }

            if (_powerUpBeat != int.MinValue && t >= _song.BeatTime(_powerUpBeat))
            {
                _powerUpBeat = int.MinValue;
                MixBus.Duck(6f, 0.4f);
                cameraRig.Kick(1.5f);
                background.HitFlash(1.5f);
            }

            int sec = _curSection;
            while (sec + 1 < _secStart.Count && t >= _secStart[sec + 1]) sec++;
            if (sec != _curSection)
            {
                _curSection = sec;
                ui.SetSectionCurrent(sec);
            }

            // Finale: three shockwaves right away, then the banner and fireworks on the next beat.
            for (int i = _finaleWaves.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _finaleWaves[i]) continue;
                float scale = 1.5f + 0.55f * (2 - i);
                background.Shockwave(ball.transform.position, i == 0 ? UIFactory.Gold : i == 1 ? _song.accent : _song.accent2, scale);
                _finaleWaves.RemoveAt(i);
            }
            if (_finaleDone && !_finaleShown && t >= _song.BeatTime(_finaleBeat))
            {
                _finaleShown = true;
                string title = _finaleAp ? "ALL PERFECT" : _finaleFc ? "FULL COMBO" : "SONG CLEAR";
                Color ca = _finaleAp ? Color.white : _finaleFc ? UIFactory.Gold : UIFactory.Cyan;
                Color cb = _finaleAp ? UIFactory.Gold : UIFactory.Pink;
                ui.ShowFinale(title, (_score.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%   ·   MAX COMBO " + _score.MaxCombo, ca, cb);
                _fireworks = 3;
                _fireworksAt = t;
            }
            if (_fireworks > 0 && t >= _fireworksAt)
            {
                Color[] cols = { _song.accent, _song.accent2, visuals.perfect };
                _burst.EmitSparks(ball.transform.position + Vector3.up * 2.5f + Vector3.right * (_fireworks - 2) * 1.5f, Quaternion.identity, 0.3f, cols[3 - _fireworks], 30);
                _burst.Emit(ball.transform.position + Vector3.up * 2.5f, cols[3 - _fireworks], 40);
                _fireworks--;
                _fireworksAt = t + 0.3;
            }
        }

        /// <summary>Music volume while playing: settings x song loudness x hit duck x finale fade.</summary>
        private float PlayingMusicVolume(double t)
        {
            float finale = _finaleAt >= 0.0 ? Mathf.Lerp(1f, 0.5f, Mathf.Clamp01((float)(t - _finaleAt) / 0.4f)) : 1f;
            return GameSettings.MusicVolume * MusicGainOf(_song) * MixBus.MusicDuck * finale;
        }

        /// <summary>After three fails in a row: the same song on Easy (after the usual between-songs ad).</summary>
        public void TryEasy()
        {
            if (_song == null || Ads.Showing) return;
            SongData song = _song;
            _challengeRun = false; // a challenge is played as set; Easy is a normal run
            Ads.BetweenSongs(() =>
            {
                SelectDifficulty(Difficulty.Easy);
                StartSong(song, Difficulty.Easy, false);
            });
        }
    }
}
