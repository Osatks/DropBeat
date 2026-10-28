using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DropBeat
{
    /// <summary>
    /// Game flow: menu (song preview) -> count-in -> play -> results, plus pause and calibration.
    ///
    /// Timing model: every note time comes from the analysed beat grid; the clock reports the song
    /// time the player is hearing; the ball's position is a function of that time; taps are converted
    /// from their input-event timestamps to song time and judged against the nearest pending note.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameController : MonoBehaviour
    {
        public enum State { Menu, Playing, Paused, Resuming, Failing, Results, Calibrating }

        [Header("Content")]
        [SerializeField] private SongLibrary library;

        [Header("Systems")]
        [SerializeField] private SongClock clock;
        [SerializeField] private SfxPlayer sfx;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private BallView ball;
        [SerializeField] private BackgroundFX background;
        [SerializeField] private GameUI ui;
        [SerializeField] private Transform ringRoot;
        [SerializeField] private VisualAssets visuals = new VisualAssets();

        [Header("Debug")]
        [SerializeField] private bool logJudgements;

        private readonly LevelLayout _layout = new LevelLayout();
        private readonly List<RingView> _ringPool = new List<RingView>(32);
        private TapInput _input;
        private HitBurst _burst;
        private Transform _pad;
        private MeshRenderer _padRenderer;
        private MaterialPropertyBlock _mpb;

        private State _state = State.Menu;
        private SongData _song;
        private Difficulty _difficulty;
        private bool _autoplay;
        private ScoreState _score;
        private JudgeWindows _windows;
        private int _nextJudge;
        private int _nextTick;
        private int _firstBeat;
        private double _lastNoteTime;
        private double _resumeAt;
        private float _failTimer;
        private Vector3 _failVelocity;
        private float _resultsFade;
        private float _suggestedOffsetMs;
        private Vector3 _ballVelocity;
        private int _lastCountBeat = int.MinValue;

        // Calibration
        private const double CalInterval = 0.6;
        private const int CalNeeded = 16;
        private double _calStartDsp;
        private int _calScheduled;
        private readonly List<float> _calErrors = new List<float>();

        public SongLibrary Library => library;
        public int SelectedSong { get; private set; }
        public Difficulty SelectedDifficulty { get; private set; }
        public SongData CurrentSong => library != null && SelectedSong >= 0 && SelectedSong < library.songs.Count ? library.songs[SelectedSong] : null;
        public State CurrentState => _state;
        public ScoreState Score => _score;
        public LevelLayout Layout => _layout;
        public SongClock Clock => clock;
        public JudgeWindows Windows => _windows;

        // ================================================================ lifecycle

        /// <summary>Camera focus in the menu: puts the beat-bouncing ball below the menu panels.</summary>
        private static readonly Vector3 MenuFocus = new Vector3(0f, 2.8f, 0f);

        private void Awake()
        {
#if !UNITY_EDITOR
            // Smooth motion; judging uses input-event timestamps, so frame pacing never affects scores.
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
#endif
            if (library == null) library = Resources.Load<SongLibrary>("SongLibrary");
            _mpb = new MaterialPropertyBlock();
            _input = new TapInput();

            if (ringRoot == null) ringRoot = new GameObject("Rings").transform;
            _burst = HitBurst.Create(transform, visuals.softAdditive);
            BuildPad();
            background.Init(visuals, cameraRig, clock.Music);
            background.Beat += OnBeat;
            ui.Init(this);

            SelectedSong = Mathf.Clamp(GameSettings.SongIndex, 0, Mathf.Max(0, (library != null ? library.songs.Count : 1) - 1));
            SelectedDifficulty = GameSettings.Difficulty;
            ApplyAudioSettings();
        }

        private void Start()
        {
            EnterMenu();
        }

        private void OnDestroy()
        {
            _input?.Dispose();
            GameSettings.Save();
        }

#if !UNITY_EDITOR
        private void OnApplicationFocus(bool focus)
        {
            if (!focus && _state == State.Playing) PauseGame();
        }
#endif

        private void ApplyAudioSettings()
        {
            clock.OutputLatency = GameSettings.OffsetMs * 0.001;
            clock.Music.volume = GameSettings.MusicVolume;
            sfx.Volume = GameSettings.SfxVolume;
        }

        private void BuildPad()
        {
            var go = new GameObject("StartPad");
            go.transform.SetParent(ringRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Torus(1.1f, 0.09f);
            _padRenderer = go.AddComponent<MeshRenderer>();
            _padRenderer.sharedMaterial = visuals.neon;
            go.transform.localRotation = RingView.Tilt;
            _pad = go.transform;
        }

        // ================================================================ menu

        private void EnterMenu()
        {
            _state = State.Menu;
            ball.gameObject.SetActive(true);
            ReleaseRings();
            ui.Show(GameUI.Screen.Menu);
            StartPreview();
        }

        private void StartPreview()
        {
            SongData s = CurrentSong;
            clock.Stop();
            sfx.StopScheduled();
            ball.Teleport(Vector3.zero + new Vector3(0f, ball.Radius, 0f));
            _pad.position = Vector3.zero;
            cameraRig.Snap(MenuFocus);
            background.ResetBeat();
            if (s == null || s.clip == null) return;
            ball.SetAccent(s.accent);
            background.SetAccent(s.accent);
            s.clip.LoadAudioData();
            clock.Play(s.clip, Mathf.Clamp(s.previewTime, 0f, Mathf.Max(0f, s.duration - 5f)), 0.15);
            clock.Music.volume = GameSettings.MusicVolume * 0.75f;
        }

        public void SelectSong(int index)
        {
            if (library == null || library.songs.Count == 0) return;
            int n = library.songs.Count;
            index = ((index % n) + n) % n;
            bool changed = index != SelectedSong;
            SelectedSong = index;
            GameSettings.SongIndex = index;
            sfx.PlayNow(sfx.UiClick, 0.6f);
            ui.RefreshMenu();
            if (changed && _state == State.Menu) StartPreview();
        }

        public void SelectDifficulty(Difficulty d)
        {
            SelectedDifficulty = (Difficulty)Mathf.Clamp((int)d, 0, 2);
            GameSettings.Difficulty = SelectedDifficulty;
            sfx.PlayNow(sfx.UiClick, 0.6f);
            ui.RefreshMenu();
        }

        public int NoteCount(Difficulty d)
        {
            SongData s = CurrentSong;
            return s != null && s.IsAnalyzed ? ChartGenerator.Generate(s, d).Count : 0;
        }

        public void ToggleAutoplay() { GameSettings.Autoplay = !GameSettings.Autoplay; ui.RefreshMenu(); sfx.PlayNow(sfx.UiClick, 0.6f); }
        public void ToggleAssistTick() { GameSettings.AssistTick = !GameSettings.AssistTick; ui.RefreshMenu(); sfx.PlayNow(sfx.UiClick, 0.6f); }
        public void ToggleHitSound() { GameSettings.HitSound = !GameSettings.HitSound; ui.RefreshMenu(); sfx.PlayNow(sfx.UiClick, 0.6f); }
        public void ToggleNoFail() { GameSettings.NoFail = !GameSettings.NoFail; ui.RefreshMenu(); sfx.PlayNow(sfx.UiClick, 0.6f); }

        public void AdjustOffset(float deltaMs)
        {
            GameSettings.OffsetMs = GameSettings.OffsetMs + deltaMs;
            ApplyAudioSettings();
            ui.RefreshMenu();
            sfx.PlayNow(sfx.UiClick, 0.6f);
        }

        public void ResetOffsetToDefault()
        {
            GameSettings.OffsetMs = GameSettings.DefaultOffsetMs;
            ApplyAudioSettings();
            CancelCalibration();
        }

        public void ApplySuggestedOffset()
        {
            GameSettings.OffsetMs = GameSettings.OffsetMs + _suggestedOffsetMs;
            GameSettings.Calibrated = true;
            ApplyAudioSettings();
            ui.HideOffsetSuggestion();
        }

        // ================================================================ start / restart

        public void Play()
        {
            SongData s = CurrentSong;
            if (s == null || s.clip == null || !s.IsAnalyzed)
            {
                Debug.LogWarning("[DropBeat] No playable song selected. Run DropBeat > Analyze All Songs.");
                return;
            }

            StartSong(s, SelectedDifficulty, GameSettings.Autoplay);
        }

        public void Retry()
        {
            if (_song != null) StartSong(_song, _difficulty, _autoplay);
        }

        public void StartSong(SongData song, Difficulty difficulty, bool autoplay)
        {
            _song = song;
            _difficulty = difficulty;
            ball.gameObject.SetActive(true);
            _autoplay = autoplay;
            ApplyAudioSettings();
            if (song.clip.loadState != AudioDataLoadState.Loaded) song.clip.LoadAudioData();

            List<ChartNote> chart = ChartGenerator.Generate(song, difficulty);
            float halfWidth = Mathf.Clamp(cameraRig.HalfWidthAtRings * 0.55f, 1.8f, 5f);
            _layout.Build(song, chart, halfWidth, song.name.GetHashCode() ^ ((int)difficulty * 7919));
            _score = new ScoreState(_layout.Notes.Count, difficulty);
            _windows = JudgeWindows.For(difficulty);
            _nextJudge = 0;
            _nextTick = 0;
            _lastNoteTime = _layout.Notes.Count > 0 ? _layout.Notes[_layout.Notes.Count - 1].time : 0.0;
            _firstBeat = chart.Count > 0 ? chart[0].beat : 0;
            _lastCountBeat = int.MinValue;
            ReleaseRings();
            _input.Clear();
            sfx.StopScheduled();

            // Count-in over the 4 beats before the first note; skip long empty intros.
            double countStart = song.BeatTime(_firstBeat - 4);
            double startAt = countStart - 1.2;
            if (startAt > 6.0) startAt = countStart - 2.5;
            else startAt = System.Math.Min(startAt, -0.3);

            _pad.position = _layout.PadPosition;
            ball.SetAccent(song.accent);
            background.SetAccent(song.accent);
            background.ResetBeat();
            Vector3 p = _layout.BallAt(startAt, out _);
            ball.Teleport(p);
            cameraRig.Snap(new Vector3(p.x * 0.35f, p.y + 1.5f, 0f));

            clock.Music.volume = GameSettings.MusicVolume;
            clock.Play(song.clip, startAt, 0.2);
            _state = State.Playing;
            ui.Show(GameUI.Screen.Hud);
            ui.SetupHud(song, difficulty, autoplay);

            if (logJudgements)
            {
                Debug.Log("[DropBeat] Start " + song.title + " " + difficulty + " notes=" + _layout.Notes.Count +
                          " first=" + (_layout.Notes.Count > 0 ? _layout.Notes[0].time.ToString("F3", CultureInfo.InvariantCulture) : "-") +
                          " startAt=" + startAt.ToString("F3", CultureInfo.InvariantCulture) + " offsetMs=" + GameSettings.OffsetMs);
            }
        }

        public void ToMenu()
        {
            sfx.PlayNow(sfx.UiClick, 0.6f);
            EnterMenu();
        }

        // ================================================================ pause

        public void PauseGame()
        {
            if (_state != State.Playing) return;
            clock.Pause();
            sfx.StopScheduled();
            _state = State.Paused;
            ui.Show(GameUI.Screen.Pause);
            ui.SetResumeCountdown(string.Empty);
        }

        public void ResumeGame()
        {
            if (_state != State.Paused) return;
            _state = State.Resuming;
            _resumeAt = Time.realtimeSinceStartupAsDouble + 1.5;
            ui.SetResumeCountdown("3");
        }

        // ================================================================ frame

        private void Update()
        {
            HandleKeys();
            switch (_state)
            {
                case State.Menu: TickMenu(); break;
                case State.Playing: TickPlaying(); break;
                case State.Paused: _input.Clear(); break;
                case State.Resuming: TickResuming(); break;
                case State.Failing: TickFailing(); break;
                case State.Results: TickResults(); break;
                case State.Calibrating: TickCalibration(); break;
            }
        }

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            switch (_state)
            {
                case State.Menu:
                    if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) SelectSong(SelectedSong - 1);
                    if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) SelectSong(SelectedSong + 1);
                    if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) SelectDifficulty((Difficulty)Mathf.Max(0, (int)SelectedDifficulty - 1));
                    if (kb.rightArrowKey.wasPressedThisFrame) SelectDifficulty((Difficulty)Mathf.Min(2, (int)SelectedDifficulty + 1));
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Play();
                    break;
                case State.Playing:
                    if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) PauseGame();
                    break;
                case State.Paused:
                    if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) ResumeGame();
                    if (kb.rKey.wasPressedThisFrame) Retry();
                    break;
                case State.Results:
                    if (kb.rKey.wasPressedThisFrame) Retry();
                    if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) ToMenu();
                    break;
                case State.Calibrating:
                    if (kb.escapeKey.wasPressedThisFrame) CancelCalibration();
                    break;
            }
        }

        private void TickMenu()
        {
            _input.Clear();
            SongData s = CurrentSong;
            double t = clock.IsRunning ? clock.SongTime : double.NaN;
            Vector3 pos = new Vector3(0f, ball.Radius, 0f);
            if (s != null && s.IsAnalyzed && !double.IsNaN(t))
            {
                int k = s.BeatIndexAt(t);
                double b0 = s.BeatTime(k), b1 = s.BeatTime(k + 1);
                float u = (float)((t - b0) / System.Math.Max(0.05, b1 - b0));
                pos.y += 4f * 0.9f * u * (1f - u);
            }
            ball.SetPose(pos, Vector3.zero);
            TintPad(s != null ? s.accent : Color.cyan, 0.6f);
            cameraRig.Follow(MenuFocus, 0.4f);
            background.Tick(s, t, Vector3.zero, 0f);
            if (clock.IsRunning && !clock.Music.isPlaying && clock.SongTime > 1.0) StartPreview();
        }

        private void TickPlaying()
        {
            double t = clock.SongTime;

            while (_input.TryDequeue(out TapInput.Tap tap))
            {
                if (tap.isPointer && ui.IsOverPauseButton(tap.screenPosition)) continue;
                if (_autoplay) continue;
                HandleTap(clock.SongTimeAtRealtime(tap.realtime), t);
            }

            var notes = _layout.Notes;
            if (_autoplay)
            {
                while (_nextJudge < notes.Count && notes[_nextJudge].time <= t)
                {
                    Judge(_nextJudge, Grade.Perfect, 0.0, t);
                }
            }

            while (_nextJudge < notes.Count && t - notes[_nextJudge].time > _windows.good)
            {
                Judge(_nextJudge, Grade.Miss, double.NaN, t);
            }

            ScheduleTicks();
            UpdateCountIn(t);
            UpdateWorld(t);
            ui.UpdateHud(_score, Progress(t), MeanErrorMs(), _windows);

            if (!_autoplay && !GameSettings.NoFail && _score.Health <= 0f)
            {
                BeginFail();
                return;
            }

            if (_nextJudge >= notes.Count && t > _lastNoteTime + 1.2)
            {
                FinishSong(false);
            }
        }

        private void HandleTap(double tapTime, double now)
        {
            var notes = _layout.Notes;
            while (_nextJudge < notes.Count)
            {
                PlayNote n = notes[_nextJudge];
                double err = tapTime - n.time;
                if (err < -_windows.earlyMiss)
                {
                    // Nothing close enough to hit: a free tap, no penalty.
                    ball.Pulse(new Color(0.6f, 0.6f, 1.2f), 0.15f);
                    return;
                }

                if (err > _windows.good)
                {
                    Judge(_nextJudge, Grade.Miss, double.NaN, now);
                    continue;
                }

                Judge(_nextJudge, _windows.Grade(err), err, now);
                return;
            }
        }

        private void Judge(int index, Grade grade, double error, double now)
        {
            PlayNote n = _layout.Notes[index];
            n.judged = true;
            n.grade = grade;
            n.error = error;
            n.judgedAt = now;
            _score.Register(grade, double.IsNaN(error) ? 0.0 : error);
            while (_nextJudge < _layout.Notes.Count && _layout.Notes[_nextJudge].judged) _nextJudge++;

            if (n.view != null) n.view.OnJudged(grade, now);
            Color c = visuals.GradeColor(grade);
            if (grade != Grade.Miss)
            {
                int count = grade == Grade.Perfect ? 34 : grade == Grade.Great ? 22 : 12;
                _burst.Emit(n.position, c, count);
                ball.Pulse(c, grade == Grade.Perfect ? 1f : 0.6f);
                cameraRig.Kick(grade == Grade.Perfect ? 0.8f : 0.4f);
                background.HitFlash(grade == Grade.Perfect ? 1f : 0.5f);
                if (GameSettings.HitSound) sfx.PlayNow(grade == Grade.Perfect ? sfx.HitPerfect : sfx.Hit, 0.55f);
                ui.PunchCombo();
                int combo = _score.Combo;
                if (combo > 0 && combo % 50 == 0) ui.ShowMilestone(combo + " COMBO!");
            }
            else
            {
                ball.Pulse(c, 0.3f);
                cameraRig.Shake(0.5f);
                if (GameSettings.HitSound) sfx.PlayNow(sfx.Miss, 0.4f);
            }

            ui.ShowJudgement(grade, error, n.position, cameraRig.Cam, _windows);
            if (logJudgements)
            {
                Debug.Log("[DropBeat] note#" + index + " t=" + n.time.ToString("F3", CultureInfo.InvariantCulture) + " " + grade +
                          (double.IsNaN(error) ? string.Empty : " err=" + (error * 1000.0).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "ms"));
            }
        }

        private void ScheduleTicks()
        {
            var notes = _layout.Notes;
            double dspNow = AudioSettings.dspTime;
            while (_nextTick < notes.Count)
            {
                // Tick plays through the same audio path as the music, so it uses raw (unshifted) song time.
                double dsp = clock.StartDsp + notes[_nextTick].time;
                if (dsp > dspNow + 0.5) break;
                if (GameSettings.AssistTick && dsp > dspNow + 0.005)
                {
                    sfx.PlayAt(notes[_nextTick].kind == NoteKind.Downbeat ? sfx.TickAccent : sfx.Tick, dsp, 0.8f);
                }
                _nextTick++;
            }
        }

        private void UpdateCountIn(double t)
        {
            if (_song == null) return;
            int k = _song.BeatIndexAt(t);
            int rel = k - _firstBeat; // -4..-1 before the first note
            if (rel >= -4 && rel <= -1 && t >= _song.BeatTime(_firstBeat - 4))
            {
                float u = (float)((t - _song.BeatTime(k)) / _song.SecondsPerBeat);
                string label = rel == -1 ? "GO!" : (-rel - 1).ToString();
                ui.SetCountIn(label, 1f + 0.35f * Mathf.Exp(-u * 6f));
                if (k != _lastCountBeat)
                {
                    _lastCountBeat = k;
                    sfx.PlayNow(rel == -1 ? sfx.TickAccent : sfx.Tick, 0.25f);
                }
            }
            else if (rel >= 0 && rel <= 1)
            {
                ui.SetCountIn(string.Empty, 1f);
            }
        }

        private float Fever => _score == null ? 0f : Mathf.Clamp01((_score.Combo - 20) / 80f);

        private void UpdateWorld(double t)
        {
            Vector3 pos = _layout.BallAt(t, out _ballVelocity);
            ball.SetPose(pos, _ballVelocity);
            ball.SetTrailWidth(1f + Fever * 0.6f);

            BindRings(t);
            double approach = Mathf.Clamp(_song.SecondsPerBeat * 2f, 0.55f, 1.1f);
            for (int i = 0; i < _ringPool.Count; i++)
            {
                RingView v = _ringPool[i];
                if (v.Free) continue;
                v.Tick(t, approach, v.Note != null && v.Note.index == _nextJudge, Fever);
            }

            TintPad(_song.accent, t < _layout.PadLeaveTime ? 0.8f : 0.25f);

            Vector3 target = pos;
            if (_nextJudge < _layout.Notes.Count) target = Vector3.Lerp(pos, _layout.Notes[_nextJudge].position, 0.35f);
            cameraRig.Follow(new Vector3(target.x * 0.35f, target.y + 0.5f, 0f), 0.22f);
            background.Tick(_song, t, pos, Fever);
        }

        private void BindRings(double t)
        {
            var notes = _layout.Notes;
            int bound = 0;
            for (int i = _nextJudge; i < notes.Count && bound < 26; i++)
            {
                PlayNote n = notes[i];
                if (n.time > t + 3.6) break;
                bound++;
                if (n.judged || n.view != null) continue;
                RingView v = GetFreeRing();
                v.Bind(n, _song.accent);
            }
        }

        private RingView GetFreeRing()
        {
            for (int i = 0; i < _ringPool.Count; i++) if (_ringPool[i].Free) return _ringPool[i];
            var v = RingView.Create(ringRoot, visuals);
            _ringPool.Add(v);
            return v;
        }

        private void ReleaseRings()
        {
            for (int i = 0; i < _ringPool.Count; i++) _ringPool[i].Release();
        }

        private void TintPad(Color accent, float intensity)
        {
            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, accent * intensity);
            _mpb.SetColor(VisualAssets.RimId, accent * 0.4f);
            _padRenderer.SetPropertyBlock(_mpb);
        }

        private void OnBeat(int beat, bool downbeat)
        {
            if (_state == State.Playing || _state == State.Menu) cameraRig.Kick(downbeat ? 0.6f : 0.25f);
        }

        private float Progress(double t)
        {
            if (_layout.Notes.Count == 0) return 0f;
            double a = _layout.Notes[0].time, b = _lastNoteTime;
            return Mathf.Clamp01((float)((t - a) / System.Math.Max(0.1, b - a)));
        }

        private float MeanErrorMs()
        {
            var e = _score.Errors;
            int n = Mathf.Min(16, e.Count);
            if (n == 0) return 0f;
            float s = 0f;
            for (int i = e.Count - n; i < e.Count; i++) s += e[i];
            return s / n * 1000f;
        }

        private void TickResuming()
        {
            _input.Clear();
            double remaining = _resumeAt - Time.realtimeSinceStartupAsDouble;
            ui.SetResumeCountdown(remaining > 1.0 ? "3" : remaining > 0.5 ? "2" : "1");
            if (remaining <= 0.0)
            {
                ui.SetResumeCountdown(string.Empty);
                ui.Show(GameUI.Screen.Hud);
                clock.Resume(0.05);
                // Re-arm assist ticks from the resume point.
                double t = clock.SongTime;
                _nextTick = _layout.FindNextNote(t);
                _state = State.Playing;
            }
        }

        // ================================================================ end of song

        private void BeginFail()
        {
            _state = State.Failing;
            _failTimer = 0f;
            _failVelocity = _ballVelocity;
            clock.Freeze();
            sfx.StopScheduled();
            cameraRig.Shake(1.2f);
            sfx.PlayNow(sfx.Miss, 0.8f);
        }

        private void TickFailing()
        {
            _input.Clear();
            float dt = Time.unscaledDeltaTime;
            _failTimer += dt;
            clock.Music.pitch = Mathf.Max(0f, 1f - _failTimer / 1.4f);
            _failVelocity += Physics.gravity * 1.4f * dt;
            ball.SetPose(ball.transform.position + _failVelocity * dt, _failVelocity);
            background.Tick(_song, double.NaN, ball.transform.position, 0f);
            ui.UpdateHud(_score, Progress(clock.SongTime), MeanErrorMs(), _windows);
            if (_failTimer > 1.6f)
            {
                clock.Stop();
                FinishSong(true);
            }
        }

        private void FinishSong(bool failed)
        {
            _state = State.Results;
            _resultsFade = 0f;
            sfx.StopScheduled();
            bool record = false;
            string rank = _score.Rank(failed);
            if (!_autoplay && !failed)
            {
                record = GameSettings.SubmitBest(_song, _difficulty, _score.Score, _score.Accuracy, rank, _score.FullCombo);
            }

            _score.ErrorStats(out float mean, out float std, out float median);
            bool suggest = !_autoplay && _score.Errors.Count >= 20 && Mathf.Abs(median) >= 8f && std < 45f;
            _suggestedOffsetMs = Mathf.Round(median);
            ui.ShowResults(_song, _difficulty, _score, failed, _autoplay, record, _suggestedOffsetMs, suggest);
            sfx.PlayNow(failed ? sfx.Miss : sfx.HitPerfect, 0.7f);

            Debug.Log("[DropBeat] Result " + _song.title + " " + _difficulty + (_autoplay ? " (auto)" : string.Empty) +
                      ": " + rank + " score=" + _score.Score + " acc=" + (_score.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) +
                      "% P/G/Good/M=" + _score.Perfect + "/" + _score.Great + "/" + _score.Good + "/" + _score.Miss +
                      " maxCombo=" + _score.MaxCombo + " meanErr=" + mean.ToString("F1", CultureInfo.InvariantCulture) + "ms std=" + std.ToString("F1", CultureInfo.InvariantCulture) + "ms");
        }

        private void TickResults()
        {
            _input.Clear();
            _resultsFade += Time.unscaledDeltaTime;
            if (clock.IsRunning && !clock.IsFrozen)
            {
                clock.Music.volume = GameSettings.MusicVolume * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(_resultsFade / 2.5f));
                double t = clock.SongTime;
                Vector3 pos = _layout.BallAt(t, out Vector3 v);
                ball.SetPose(pos, v);
                cameraRig.Follow(new Vector3(pos.x * 0.35f, pos.y - 1f, 0f), 0.6f);
                background.Tick(_song, t, pos, 0f);
                if (!clock.Music.isPlaying && t > 0.5) clock.Stop();
            }
            else
            {
                background.Tick(_song, double.NaN, ball.transform.position, 0f);
            }
        }

        // ================================================================ calibration

        public void StartCalibration()
        {
            clock.Stop();
            sfx.StopScheduled();
            ReleaseRings();
            _state = State.Calibrating;
            ball.gameObject.SetActive(false);
            _calErrors.Clear();
            _calStartDsp = AudioSettings.dspTime + 1.0;
            _calScheduled = 0;
            _input.Clear();
            ui.Show(GameUI.Screen.Calibration);
        }

        public void CancelCalibration()
        {
            sfx.StopScheduled();
            EnterMenu();
        }

        public void SaveCalibration()
        {
            if (_calErrors.Count < 8) return;
            GameSettings.OffsetMs = Mathf.Round(Median(_calErrors));
            GameSettings.Calibrated = true;
            GameSettings.Save();
            ApplyAudioSettings();
            CancelCalibration();
        }

        private void TickCalibration()
        {
            double dspNow = AudioSettings.dspTime;
            while (_calStartDsp + _calScheduled * CalInterval < dspNow + 0.5)
            {
                double at = _calStartDsp + _calScheduled * CalInterval;
                if (at > dspNow + 0.005) sfx.PlayAt(_calScheduled % 4 == 0 ? sfx.TickAccent : sfx.Tick, at, 1f);
                _calScheduled++;
            }

            while (_input.TryDequeue(out TapInput.Tap tap))
            {
                if (_calErrors.Count >= 32) continue;
                double tapDsp = clock.DspAtRealtime(tap.realtime);
                double rel = (tapDsp - _calStartDsp) / CalInterval;
                int j = (int)System.Math.Round(rel);
                if (j < 0) continue;
                double err = tapDsp - (_calStartDsp + j * CalInterval);
                if (System.Math.Abs(err) > CalInterval * 0.45) continue;
                _calErrors.Add((float)(err * 1000.0));
                ui.PulseCalibration();
            }

            // The circle flashes at the *heard* click time using the current offset.
            double heard = clock.DspNow - _calStartDsp - GameSettings.OffsetMs * 0.001;
            float phase = heard < 0 ? 1f : (float)(heard / CalInterval - System.Math.Floor(heard / CalInterval));
            float median = _calErrors.Count > 0 ? Median(_calErrors) : 0f;
            ui.UpdateCalibration(phase, _calErrors.Count, CalNeeded, median, Spread(_calErrors, median), _calErrors.Count >= 8);
            background.Tick(null, double.NaN, Vector3.zero, 0f);
        }

        private static float Median(List<float> v)
        {
            var c = new List<float>(v);
            c.Sort();
            return c[c.Count / 2];
        }

        private static float Spread(List<float> v, float median)
        {
            if (v.Count < 2) return 0f;
            var d = new List<float>(v.Count);
            foreach (float x in v) d.Add(Mathf.Abs(x - median));
            d.Sort();
            return d[d.Count / 2] * 1.4826f;
        }
    }
}
