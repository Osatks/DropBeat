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
    /// time the player is hearing; every ball's position is a function of that time; presses, releases
    /// and swipes are converted from their input-event timestamps to song time and judged.
    ///
    /// Elements: taps, hold rails, swipe dashes, dual rings (split ball), companion balls, moving rings,
    /// overdrive, reactive music (muffle on miss, extra layers on combo) and camera perspective shifts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class GameController : MonoBehaviour
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

        private const double TailWindow = 0.12;
        private const double SwipeWait = 0.35;
        private const int OverdriveBeats = 16;

        private readonly LevelLayout _layout = new LevelLayout();
        private readonly TwistDirector _twists = new TwistDirector();
        private AudioClip _previewClip;
        private readonly List<RingView> _ringPool = new List<RingView>(48);
        private readonly List<BallView> _extraBalls = new List<BallView>(6);
        private readonly Dictionary<BallPath, BallView> _ballFor = new Dictionary<BallPath, BallView>();
        private readonly List<PlayNote> _holds = new List<PlayNote>(4);
        private readonly HashSet<PlayNote> _hinted = new HashSet<PlayNote>();
        private TapInput _input;
        private HitBurst _burst;
        private Transform _pad;
        private MeshRenderer _padRenderer;
        private MaterialPropertyBlock _mpb;
        private AudioLowPassFilter _lpf;
        private AudioHighPassFilter _hpf;

        private State _state = State.Menu;
        private SongData _song;
        private Chart _chart;
        private Difficulty _difficulty;
        private bool _autoplay;
        private ScoreState _score;
        private JudgeWindows _windows;
        private int _nextJudge;
        private int _nextTick;
        private double _lastTickTime;
        private int _firstBeat;
        private double _lastNoteTime;
        private double _resumeAt;
        private float _failTimer;
        private Vector3 _failVelocity;
        private float _resultsFade;
        private float _suggestedOffsetMs;
        private Vector3 _ballVelocity;
        private int _lastCountBeat = int.MinValue;
        private int _cueIndex;

        // Pending swipe: the finger is down on a swipe ring and we wait for its direction.
        private PlayNote _swipeNote;
        private int _swipeId;
        private double _swipePressSong;
        private double _swipeDeadline;

        // Overdrive
        private float _overdrive;
        private bool _overdriveOn;
        private double _overdriveStart;
        private double _overdriveEnd;

        // Reactive music
        private double _muffleAt = -100;
        private int _layerBeat;

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
        public float OverdriveMeter => _overdrive;
        public bool OverdriveActive => _overdriveOn;

        /// <summary>Camera focus in the menu: puts the beat-bouncing ball below the menu panels (scaled with the portrait pull-back).</summary>
        // In portrait the menu's bottom sheet covers the lower third, so frame the ball above it.
        private Vector3 MenuFocus => new Vector3(0f, cameraRig.DistanceScale > 1f ? -2.6f : 2.8f, 0f);

        // ================================================================ lifecycle

        private void Awake()
        {
#if UNITY_ANDROID || UNITY_IOS
            // Phones default to 30 fps. Ask for 120: ProMotion / 120 Hz phones run at 120, the OS caps others
            // at their display rate (60 or 90). Never dim mid-song.
            Application.targetFrameRate = Mathf.Max(120, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            // Phones would otherwise start on a lower quality level (lower-resolution textures, fewer particles).
            int high = System.Array.IndexOf(QualitySettings.names, "High");
            if (high >= 0) QualitySettings.SetQualityLevel(high, true);
#elif !UNITY_EDITOR
            // Smooth motion; judging uses input-event timestamps, so frame pacing never affects scores.
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
#endif
            if (library == null) library = Resources.Load<SongLibrary>("SongLibrary");
            _mpb = new MaterialPropertyBlock();
            _input = new TapInput();

            // Reactive music: a low-pass for "underwater" misses and a high-pass for the overdrive swell.
            _lpf = clock.Music.GetComponent<AudioLowPassFilter>();
            if (_lpf == null) _lpf = clock.Music.gameObject.AddComponent<AudioLowPassFilter>();
            _lpf.cutoffFrequency = 22000f;
            _lpf.lowpassResonanceQ = 1.2f;
            _hpf = clock.Music.GetComponent<AudioHighPassFilter>();
            if (_hpf == null) _hpf = clock.Music.gameObject.AddComponent<AudioHighPassFilter>();
            _hpf.cutoffFrequency = 10f;
            _hpf.highpassResonanceQ = 1.4f;

            if (ringRoot == null) ringRoot = new GameObject("Rings").transform;
            _burst = HitBurst.Create(transform, visuals.softAdditive);
            _impactFx = ImpactFX.Create(transform, visuals.softAdditive);
            MixBus.Ensure();
            BuildPad();
            background.Init(visuals, cameraRig, clock.Music);
            background.Beat += OnBeat;
            ui.Init(this);
            Ads.ShowingChanged += OnAdShowing;

            SelectedSong = Mathf.Clamp(GameSettings.SongIndex, 0, Mathf.Max(0, (library != null ? library.songs.Count : 1) - 1));
            SelectedDifficulty = GameSettings.Difficulty;
            ApplyAudioSettings();
        }

        private void Start()
        {
            Ads.Init();
            ball.SetSkin(GameSettings.BallSkin);
            ball.SetTrailEffect(GameSettings.Trail, visuals);
            EnterMenu();
        }

        /// <summary>The main ball (the locker panel renders its skin previews with the same material).</summary>
        public BallView Ball => ball;

        /// <summary>
        /// Locker: equips an unlocked skin, otherwise plays a rewarded ad that counts toward its unlock
        /// (<see cref="GameSettings.CosmeticAdsToUnlock"/> ads); the skin is equipped once it unlocks.
        /// </summary>
        public void SelectSkin(BallSkin skin)
        {
            if (Ads.Showing) return;
            if (GameSettings.IsUnlocked(skin))
            {
                EquipSkin(skin);
                sfx.PlayNow(sfx.UiClick, 0.6f);
                return;
            }
            WatchAdToward(() => GameSettings.WatchedAdFor(skin) >= GameSettings.CosmeticAdsToUnlock, () => EquipSkin(skin));
        }

        /// <summary>Locker: same as <see cref="SelectSkin"/> for trail effects.</summary>
        public void SelectTrail(TrailEffect trail)
        {
            if (Ads.Showing) return;
            if (GameSettings.IsUnlocked(trail))
            {
                EquipTrail(trail);
                sfx.PlayNow(sfx.UiClick, 0.6f);
                return;
            }
            WatchAdToward(() => GameSettings.WatchedAdFor(trail) >= GameSettings.CosmeticAdsToUnlock, () => EquipTrail(trail));
        }

        private void EquipSkin(BallSkin skin)
        {
            GameSettings.BallSkin = skin;
            GameSettings.Save();
            ball.SetSkin(GameSettings.BallSkin);
            ui.RefreshMenu();
        }

        private void EquipTrail(TrailEffect trail)
        {
            GameSettings.Trail = trail;
            GameSettings.Save();
            ball.SetTrailEffect(GameSettings.Trail, visuals);
            ui.RefreshMenu();
        }

        /// <summary>
        /// Plays one rewarded ad. If the reward is earned, <paramref name="countAndCheck"/> records it and
        /// returns true when that completes the unlock (then <paramref name="onUnlocked"/> runs).
        /// </summary>
        private void WatchAdToward(System.Func<bool> countAndCheck, System.Action onUnlocked)
        {
            if (!Ads.RewardedReady)
            {
                ui.ShowToast("Ad not ready yet, try again in a moment");
                return;
            }
            Ads.Rewarded(result =>
            {
                if (result != Ads.RewardResult.Earned)
                {
                    ui.ShowToast(result == Ads.RewardResult.Failed ? "Ad not ready yet, try again in a moment" : "Watch the whole ad to count it");
                    ui.RefreshMenu();
                    return;
                }
                if (countAndCheck())
                {
                    onUnlocked?.Invoke();
                    sfx.PlayNow(sfx.Fanfare, 0.7f);
                    ui.ShowToast("Unlocked!");
                }
                else
                {
                    sfx.PlayNow(sfx.UiClick, 0.6f);
                }
                ui.RefreshMenu();
            });
        }

        /// <summary>Settings: test mode unlocks everything and skips between-song ads.</summary>
        public void ToggleTestMode()
        {
            GameSettings.TestMode = !GameSettings.TestMode;
            ball.SetSkin(GameSettings.BallSkin);
            ball.SetTrailEffect(GameSettings.Trail, visuals);
            sfx.PlayNow(sfx.UiClick, 0.6f);
            ui.RefreshMenu();
        }

        private void OnDestroy()
        {
            Ads.ShowingChanged -= OnAdShowing;
            _input?.Dispose();
            GameSettings.Save();
        }

#if !UNITY_EDITOR
        private void OnApplicationFocus(bool focus)
        {
            if (!focus && _state == State.Playing) PauseGame();
            if (!focus && _state == State.Resuming)
            {
                // The countdown runs on realtime: back to the pause menu so the player resumes on purpose.
                _state = State.Paused;
                ui.Show(GameUI.Screen.Pause);
                ui.SetResumeCountdown(string.Empty);
            }
        }
#endif

        /// <summary>Ads pause all audio; in the menu the preview clock pauses with it so visuals stay on the music.</summary>
        private void OnAdShowing(bool showing)
        {
            if (_state != State.Menu || !clock.IsRunning) return;
            if (showing) clock.Pause();
            else clock.Resume(0.05);
        }

        private void ApplyAudioSettings()
        {
            clock.OutputLatency = GameSettings.OffsetMs * 0.001;
            clock.Music.volume = GameSettings.MusicVolume * MusicGainOf(CurrentSong);
            sfx.Volume = GameSettings.SfxVolume;
        }

        private void BuildPad()
        {
            var go = new GameObject("StartPad");
            go.transform.SetParent(ringRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Quad();
            _padRenderer = go.AddComponent<MeshRenderer>();
            _padRenderer.sharedMaterial = visuals.ring;
            go.transform.localRotation = RingView.Tilt;
            go.transform.localScale = Vector3.one * (RingView.QuadSize * 1.35f);
            _pad = go.transform;
        }

        // ================================================================ menu

        private void EnterMenu()
        {
            _state = State.Menu;
            _twists.End();
            ball.gameObject.SetActive(true);
            ball.SetTrailWidth(1f);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            _impactFx.Clear();
            ReleaseRings();
            HideExtraBalls();
            ResetMusicFx();
            cameraRig.SetMode(CameraMode.Side, 0f);
            cameraRig.SetOverdrive(0f);
            cameraRig.SetCrowd(0f);
            background.SetOverdrive(0f);
            background.SetPlaying(false);
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
            background.Snap();
            background.ResetBeat();
            if (s == null || s.clip == null) return;
            ball.SetAccent(s.accent, s.accent2);
            background.SetTheme(s);
            sfx.SetKey(s.HitRoot, s.HitMinor, s.hitStyle);
            // Only the previewed (and playing) song stays decoded in memory.
            if (_previewClip != null && _previewClip != s.clip && (_song == null || _previewClip != _song.clip)) _previewClip.UnloadAudioData();
            _previewClip = s.clip;
            s.clip.LoadAudioData();
            clock.Play(s.clip, Mathf.Clamp(s.previewTime, 0f, Mathf.Max(0f, s.duration - 5f)), 0.15);
            clock.Music.volume = GameSettings.MusicVolume * 0.75f * s.MusicGain;
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
            return s != null && s.IsAnalyzed ? ChartDesigner.Design(s, d).notes.Count : 0;
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
            if (Ads.Showing) return;
            SongData s = CurrentSong;
            if (s == null || s.clip == null || !s.IsAnalyzed)
            {
                Debug.LogWarning("[DropBeat] No playable song selected. Run DropBeat > Analyze All Songs.");
                return;
            }

            if (!GameSettings.IsUnlocked(s))
            {
                UnlockSelectedSong();
                return;
            }

            StartSong(s, SelectedDifficulty, GameSettings.Autoplay);
        }

        /// <summary>Plays a rewarded ad; when the player earns the reward the selected song stays unlocked.</summary>
        public void UnlockSelectedSong()
        {
            SongData s = CurrentSong;
            if (s == null || GameSettings.IsUnlocked(s) || Ads.Showing) return;
            WatchAdToward(() => GameSettings.WatchedAdFor(s) >= GameSettings.SongAdsToUnlock, null);
        }

        public void Retry()
        {
            if (_song == null || Ads.Showing) return;
            if (_state == State.Results) Ads.BetweenSongs(() => StartSong(_song, _difficulty, _autoplay));
            else StartSong(_song, _difficulty, _autoplay);
        }

        public void StartSong(SongData song, Difficulty difficulty, bool autoplay)
        {
            _song = song;
            _difficulty = difficulty;
            _autoplay = autoplay;
            ball.gameObject.SetActive(true);
            ApplyAudioSettings();
            sfx.SetKey(song.HitRoot, song.HitMinor, song.hitStyle);
            sfx.PrepareStingers(song.bpm);
            if (song.clip.loadState != AudioDataLoadState.Loaded) song.clip.LoadAudioData();

            _chart = ChartDesigner.Design(song, difficulty);
            // Fixed field width (what a portrait phone shows), not the live screen width, so the ring map
            // is identical on every device and every play.
            const float halfWidth = 2.5f;
            // Stable seed: a song always gets the same ring map on every play and every device.
            _layout.Build(song, _chart, halfWidth, ChartDesigner.StableHash(song.name) ^ ((int)difficulty * 7919));
            _score = new ScoreState(_layout.JudgementCount, difficulty);
            _windows = JudgeWindows.For(difficulty);
            _nextJudge = 0;
            _nextTick = 0;
            _lastTickTime = double.NegativeInfinity;
            _lastNoteTime = 0.0;
            foreach (var n in _layout.Notes) _lastNoteTime = System.Math.Max(_lastNoteTime, n.type == NoteType.Hold ? n.endTime : n.time);
            int firstMain = 0;
            while (firstMain < _chart.notes.Count && _chart.notes[firstMain].ball != 0) firstMain++;
            _firstBeat = _chart.notes.Count > 0 ? _chart.notes[System.Math.Min(firstMain, _chart.notes.Count - 1)].beat : 0;
            _lastCountBeat = int.MinValue;
            _cueIndex = 0;
            _holds.Clear();
            _hinted.Clear();
            _swipeNote = null;
            _overdrive = 0f;
            _overdriveOn = false;
            _layerBeat = int.MinValue;
            ResetMusicFx();
            ReleaseRings();
            HideExtraBalls();
            _input.Clear();
            sfx.StopScheduled();

            // Count-in over the 4 beats before the first note; skip long empty intros.
            double countStart = song.BeatTime(_firstBeat - 4);
            double startAt = countStart - 1.2;
            if (startAt > 6.0) startAt = countStart - 2.5;
            else startAt = System.Math.Min(startAt, -0.3);

            _pad.position = _layout.PadPosition;
            ball.SetAccent(song.accent, song.accent2);
            background.SetTheme(song);
            background.ResetBeat();
            background.SetOverdrive(0f);
            cameraRig.SetMode(CameraMode.Side, 0f);
            cameraRig.SetOverdrive(0f);
            cameraRig.SetCrowd(0f);
            Vector3 p = _layout.BallAt(startAt, out _);
            ball.Teleport(p);
            _twists.End(); // no roll left over from the last run (Restart from a flipped pause)
            cameraRig.Snap(new Vector3(p.x * 0.35f, p.y + 1.5f, 0f));
            background.Snap();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            clock.Music.volume = GameSettings.MusicVolume * song.MusicGain;
            clock.Play(song.clip, startAt, 0.2);
            _state = State.Playing;
            ui.Show(GameUI.Screen.Hud);
            ui.SetupHud(song, difficulty, autoplay);
            _twists.Begin(song, _chart, _layout, ui, cameraRig, background, sfx);
            background.SetPlaying(true);
            ResetFeel();

            if (logJudgements)
            {
                Debug.Log("[DropBeat] Start " + song.title + " " + difficulty + " rings=" + _layout.Notes.Count +
                          " judgements=" + _layout.JudgementCount + " startAt=" + startAt.ToString("F3", CultureInfo.InvariantCulture));
            }
        }

        public void ToMenu()
        {
            if (Ads.Showing) return;
            sfx.PlayNow(sfx.UiClick, 0.6f);
            if (_state == State.Results) Ads.BetweenSongs(EnterMenu);
            else EnterMenu();
        }

        // ================================================================ pause

        public void PauseGame()
        {
            if (_state != State.Playing) return;
            clock.Pause();
            sfx.StopScheduled();
            sfx.StopHold();
            // A swipe waiting for its direction is dropped (its deadline is realtime); it can be swiped again after resume.
            _swipeNote = null;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
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
            if (kb == null || Ads.Showing) return;
            if (_state == State.Menu && ui.SheetOpen) return; // settings / style sheet has the focus
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
            _input.Poll();
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
            TintPad(s != null ? s.accent : Color.cyan, 0.8f, s, t);
            cameraRig.Follow(MenuFocus, 0.4f);
            background.Tick(s, t, Vector3.zero, 0f);
            if (clock.IsRunning && !clock.Music.isPlaying && clock.SongTime > 1.0) StartPreview();
        }

        private void TickPlaying()
        {
            double t = clock.SongTime;
            _input.Poll();

            while (_input.TryDequeue(out TapInput.Tap tap))
            {
                if (tap.isPointer && tap.kind == TapInput.Kind.Press && ui.IsOverPauseButton(tap.screenPosition)) continue;
                if (_autoplay) continue;
                double tt = clock.SongTimeAtRealtime(tap.realtime);
                switch (tap.kind)
                {
                    case TapInput.Kind.Press: HandlePress(tt, tap, t); break;
                    case TapInput.Kind.Release: HandleRelease(tt, tap.id, t); break;
                    case TapInput.Kind.Swipe: HandleSwipe(tt, tap, t); break;
                }
            }

            if (_swipeNote != null && Time.realtimeSinceStartupAsDouble > _swipeDeadline)
            {
                PlayNote n = _swipeNote;
                _swipeNote = null;
                Judge(n, Grade.Miss, double.NaN, t, "SWIPE!");
            }

            var notes = _layout.Notes;
            if (_autoplay)
            {
                for (int i = _nextJudge; i < notes.Count && notes[i].time <= t; i++)
                {
                    PlayNote n = notes[i];
                    if (n.judged) continue;
                    Judge(n, Grade.Perfect, 0.0, t);
                    if (n.type == NoteType.Hold) StartHold(n, -1);
                }
            }

            // Late misses.
            for (int i = _nextJudge; i < notes.Count; i++)
            {
                PlayNote n = notes[i];
                if (t - n.time <= _windows.good) break;
                if (n.judged || n == _swipeNote) continue;
                Judge(n, Grade.Miss, double.NaN, t);
            }

            TickHolds(t);
            TickOverdrive(t);
            TickMusicFx(t);
            TickCameraCues(t);
            _twists.Tick(t);
            TickFeel(t);
            ScheduleTicks();
            UpdateCountIn(t);
            UpdateHints(t);
            UpdateWorld(t);
            float ph = BeatPhase(t);
            ui.UpdateHud(_score, Progress(t), MeanErrorMs(), _windows, _tier, ph);
            ui.UpdateOverdrive(_overdrive, _overdriveOn, ph);

            if (_score.Health <= 0f) _healthHitZero = true;
            if (!_autoplay && !GameSettings.NoFail && _score.Health <= 0f)
            {
                BeginFail();
                return;
            }

            if (_nextJudge >= notes.Count && _holds.Count == 0 && t > _lastNoteTime + 1.2)
            {
                FinishSong(false);
            }
        }

        // ================================================================ judging

        /// <summary>Earliest pending ring that a press at time t can hit (null = nothing close enough).</summary>
        private PlayNote FindCandidate(double t, TapSide side)
        {
            var notes = _layout.Notes;
            for (int i = _nextJudge; i < notes.Count; i++)
            {
                PlayNote n = notes[i];
                if (n.judged || n == _swipeNote) continue;
                double err = t - n.time;
                if (err > _windows.good) continue;       // already too late; the frame loop misses it
                if (err < -_windows.earlyMiss) return null;
                if (n.partner != null && side != TapSide.Any && n.side != side && !n.partner.judged) return n.partner;
                return n;
            }
            return null;
        }

        private void HandlePress(double t, TapInput.Tap tap, double now)
        {
            PlayNote n = FindCandidate(t, tap.side);
            if (n == null)
            {
                ball.Pulse(new Color(0.6f, 0.6f, 1.2f), 0.15f);
                return;
            }

            double err = t - n.time;
            if (n.type == NoteType.Swipe)
            {
                if (tap.isPointer)
                {
                    // Judged at the finger-down time once the drag direction is known.
                    _swipeNote = n;
                    _swipeId = tap.id;
                    _swipePressSong = t;
                    _swipeDeadline = Time.realtimeSinceStartupAsDouble + SwipeWait;
                    // Instant snap at touch-down; the grade, whoosh and smash follow once the direction is known.
                    if (GameSettings.HitSound && System.Math.Abs(err) <= _windows.good) sfx.PlayNow(sfx.SwipeSnap, 0.5f);
                }
                else
                {
                    Judge(n, Grade.Miss, err, now, "SWIPE!");
                }
                return;
            }

            Grade g = _windows.Grade(err);
            Judge(n, g, err, now);
            if (n.type == NoteType.Hold && g != Grade.Miss) StartHold(n, tap.id);
        }

        private void HandleSwipe(double t, TapInput.Tap tap, double now)
        {
            if (_swipeNote != null && tap.id == _swipeId)
            {
                PlayNote s = _swipeNote;
                _swipeNote = null;
                double err = _swipePressSong - s.time;
                if (tap.swipe == s.swipe) Judge(s, _windows.Grade(err), err, now);
                else Judge(s, Grade.Miss, err, now, "WRONG WAY");
                return;
            }

            if (tap.isPointer) return; // a drag on a normal ring: the press already counted

            // Arrow keys / d-pad: press and direction in one event.
            PlayNote n = FindCandidate(t, TapSide.Any);
            if (n == null) return;
            double e = t - n.time;
            if (n.type == NoteType.Swipe)
            {
                if (tap.swipe == n.swipe) Judge(n, _windows.Grade(e), e, now);
                else Judge(n, Grade.Miss, e, now, "WRONG WAY");
            }
            else
            {
                Grade g = _windows.Grade(e);
                Judge(n, g, e, now);
                if (n.type == NoteType.Hold && g != Grade.Miss) StartHold(n, -1);
            }
        }

        private void HandleRelease(double t, int id, double now)
        {
            if (_swipeNote != null && id == _swipeId)
            {
                // Lifted without dragging: it was a tap on a swipe ring.
                PlayNote s = _swipeNote;
                _swipeNote = null;
                Judge(s, Grade.Miss, _swipePressSong - s.time, now, "SWIPE!");
                return;
            }

            for (int i = _holds.Count - 1; i >= 0; i--)
            {
                PlayNote h = _holds[i];
                if (h.holdId != id) continue;
                double early = h.endTime - t;
                if (early <= TailWindow) JudgeTail(h, early > TailWindow * 0.5 ? Grade.Great : Grade.Perfect, now);
                else JudgeTail(h, Grade.Miss, now);
                _holds.RemoveAt(i);
            }
        }

        private void StartHold(PlayNote n, int id)
        {
            n.holding = true;
            n.holdId = id;
            _holds.Add(n);
            if (GameSettings.HitSound) sfx.StartHold((float)(n.endTime - n.time));
        }

        private void TickHolds(double t)
        {
            for (int i = _holds.Count - 1; i >= 0; i--)
            {
                PlayNote h = _holds[i];
                if (t >= h.endTime)
                {
                    JudgeTail(h, Grade.Perfect, t);
                    _holds.RemoveAt(i);
                }
            }
        }

        private void JudgeTail(PlayNote h, Grade g, double now)
        {
            if (h.tailJudged) return;
            h.tailJudged = true;
            h.tailGrade = g;
            h.holding = false;
            _score.Register(g, 0.0, _overdriveOn ? 2 : 1, countError: false);
            sfx.StopHold();
            Vector3 at = _layout.BallAt(now, out _);
            if (g == Grade.Miss)
            {
                Muffle(now);
                ui.ShowJudgement(Grade.Miss, double.NaN, at, cameraRig.Cam, _windows, "BREAK");
                if (GameSettings.HitSound) sfx.PlayMiss();
            }
            else
            {
                _burst.Emit(h.railEnd, visuals.perfect, 30);
                if (GameSettings.HitSound) sfx.PlayNow(sfx.HoldRelease, 0.6f);
                ui.ShowJudgement(g, double.NaN, at, cameraRig.Cam, _windows, "HOLD!");
                AddOverdrive(g);
            }
        }

        private void Judge(PlayNote n, Grade grade, double error, double now, string label = null)
        {
            if (n.judged) return;
            n.judged = true;
            n.grade = grade;
            n.error = error;
            n.judgedAt = now;
            int lost = _score.Combo;
            int pts = _score.Register(grade, double.IsNaN(error) ? 0.0 : error, (_overdriveOn ? 2 : 1) * _twists.Multiplier(n, grade));
            _score.Mark(n.time, grade);
            _twists.OnJudged(n, grade, _score);
            if (label == null) label = _twists.Label(n, grade);
            if (_overdriveOn && pts > 0) _odBonus += pts / 2;
            while (_nextJudge < _layout.Notes.Count && _layout.Notes[_nextJudge].judged) _nextJudge++;
            if (n.type == NoteType.Hold && grade == Grade.Miss) JudgeTail(n, Grade.Miss, now);

            if (n.view != null)
            {
                n.view.SetHitContext(_tier / 4f, _overdriveOn);
                n.view.OnJudged(grade, now);
            }
            Color c = visuals.GradeColor(grade);
            // Effects land where the ring is drawn (moving rings, mirages), not on its beat position.
            Vector3 pos = n.view != null ? n.view.transform.position : n.PositionAt(now);
            if (grade != Grade.Miss)
            {
                bool perfect = grade == Grade.Perfect;
                bool great = grade == Grade.Great;
                _perfectChain = perfect ? _perfectChain + 1 : 0;
                Color col = n.ball > 0 ? n.tint : c;
                if (_overdriveOn) col = Color.Lerp(col, new Color(2.6f, 0.6f, 2.2f, 1f), 0.5f);
                int sparks = Mathf.Min(48, Mathf.RoundToInt((perfect ? 24 : great ? 14 : 0) * (1f + 0.25f * _tier)));
                _burst.EmitSparks(pos, n.type == NoteType.Swipe ? Quaternion.identity : n.facing, 0.81f * n.scale, col, sparks);
                _burst.Emit(pos, col, perfect ? 12 : 8);
                if (perfect) _impactFx.Play(pos, now, _score.Combo >= 100 || _score.Perfect == _score.Judged ? 1.3f : 1f);
                BallView bv = BallForNote(n);
                if (bv != null) bv.Pulse(perfect ? Color.Lerp(UIFactory.Gold, Color.white * 2f, 0.5f) : c, perfect ? 1f : great ? 0.6f : 0.35f);
                cameraRig.Kick(perfect ? Mathf.Min(1.3f, 1f + 0.1f * _tier) : great ? 0.6f : 0.3f);
                background.HitFlash(perfect ? 1f : great ? 0.6f : 0.3f);
                Punch(n, perfect ? 0.10f : great ? 0.06f : 0.03f);
                MixBus.Duck(perfect ? 4f : great ? 3f : 1.5f);
                if (GameSettings.HitSound)
                {
                    float pan = n.side == TapSide.Left ? -0.65f : n.side == TapSide.Right ? 0.65f : 0f;
                    int step = n.ball > 0 ? Mathf.Min(_lastStep + 2 * n.ball, 10) : RiffStep(n);
                    sfx.PlayHit(grade, step, _tier, _overdriveOn, pan, _difficulty == Difficulty.Hard);
                    if (n.ball > 0) sfx.PlayHarmony(_lastStep, n.ball);
                    else _lastStep = step;
                    if (n.type == NoteType.Swipe)
                    {
                        sfx.PlayNow(sfx.Whoosh, 0.7f);
                        sfx.PlayNow(sfx.Smash, 0.55f);
                    }
                }
                if (n.impact)
                {
                    // Big moment of the song: everything hits harder.
                    cameraRig.Kick(2f);
                    cameraRig.Shake(0.45f);
                    background.HitFlash(1.6f);
                    _burst.Emit(pos, visuals.perfect, 70);
                    Punch(n, 0.25f);
                    MixBus.Duck(7f, 0.3f);
                    if (GameSettings.HitSound) sfx.PlayNow(sfx.Impact, 0.85f);
                }
                if (n.type == NoteType.Swipe)
                {
                    cameraRig.Kick(1.2f);
                    cameraRig.Shake(0.35f);
                    _burst.Emit(pos, visuals.great, 40);
                    MixBus.Duck(7f, 0.3f);
                }
                ui.PunchCombo(grade);
                ui.PunchScore();
                int nt = TierOf(_score.Combo);
                if (nt > _tier && nt != _pendingTier) QueueTierUp(nt, now);
                AddOverdrive(grade);
            }
            else
            {
                _perfectChain = 0;
                BallView bv = BallForNote(n);
                if (bv != null) bv.Pulse(c, 0.3f);
                MissFeel(n, lost);
                if (!_overdriveOn) _overdrive = Mathf.Max(0f, _overdrive - 0.15f);
            }
            SectionUpdate(n, grade, pts, now);

            BallView owner = BallForNote(n);
            int lane = n.ball > 0 ? n.ball : n.side == TapSide.Right ? 9 : 0;
            ui.ShowJudgement(grade, error, owner != null ? owner.transform.position : pos, cameraRig.Cam, _windows, label, pts, _perfectChain, _overdriveOn, lane);
            CheckFinale(pos, now);
            if (logJudgements)
            {
                Debug.Log("[DropBeat] note#" + n.index + " " + n.type + (n.ball > 0 ? " ball" + n.ball : string.Empty) +
                          " t=" + n.time.ToString("F3", CultureInfo.InvariantCulture) + " " + grade +
                          (double.IsNaN(error) ? string.Empty : " err=" + (error * 1000.0).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "ms"));
            }
        }

        // ================================================================ overdrive

        private void AddOverdrive(Grade g)
        {
            if (_overdriveOn) return;
            _overdrive = Mathf.Clamp01(_overdrive + (g == Grade.Perfect ? 0.04f : g == Grade.Great ? 0.025f : 0.006f));
            if (_overdrive >= 1f) ActivateOverdrive();
        }

        private void ActivateOverdrive()
        {
            double t = clock.SongTime;
            int k = _song.BeatIndexAt(t);
            _overdriveOn = true;
            _overdriveStart = t;
            _overdriveEnd = _song.BeatTime(k + OverdriveBeats);
            _odBonus = 0;
            // The power-up's boom (0.65 s into the clip) lands on a beat.
            int b = k + 1;
            while (clock.StartDsp + _song.BeatTime(b) - 0.65 < AudioSettings.dspTime + 0.03) b++;
            _powerUpBeat = b;
            sfx.PlayAt(sfx.PowerUp, clock.StartDsp + _song.BeatTime(b) - 0.65, 1f);
            ui.ShowMilestone("OVERDRIVE  x2");
            cameraRig.Kick(1.5f);
            background.HitFlash(1.5f);
        }

        private void TickOverdrive(double t)
        {
            float floor = _twists.OverdriveFloor;
            if (!_overdriveOn)
            {
                cameraRig.SetOverdrive(floor);
                background.SetOverdrive(floor * 0.8f);
                return;
            }

            double span = System.Math.Max(0.1, _overdriveEnd - _overdriveStart);
            _overdrive = Mathf.Clamp01((float)((_overdriveEnd - t) / span));
            float ramp = Mathf.Clamp01((float)(t - _overdriveStart) / 0.4f) * Mathf.Clamp01((float)(_overdriveEnd - t) / 0.4f);
            cameraRig.SetOverdrive(Mathf.Max(ramp, floor));
            background.SetOverdrive(Mathf.Max(ramp, floor * 0.8f));
            if (t >= _overdriveEnd)
            {
                _overdriveOn = false;
                _overdrive = 0f;
                if (_odBonus > 0)
                {
                    // Ends on a payout, not a deflating sweep.
                    if (GameSettings.HitSound && !_finaleDone) sfx.PlayNow(sfx.TierSting(3), 0.6f);
                    ui.ShowBanner("OVERDRIVE  +" + _odBonus.ToString("N0", CultureInfo.InvariantCulture), UIFactory.Gold, UIFactory.Pink, 2);
                }
                else sfx.PlayNow(sfx.PowerDown, 0.6f);
                _odBonus = 0;
            }
        }

        // ================================================================ reactive music

        private void ResetMusicFx()
        {
            _muffleAt = -100;
            _muffleHz = 0f;
            if (_lpf != null) _lpf.cutoffFrequency = 22000f;
            if (_hpf != null) _hpf.cutoffFrequency = 10f;
            sfx.StopLayer();
        }

        private void Muffle(double now)
        {
            _muffleHz = 900f;
            _muffleDur = 1f;
            _muffleAt = Time.realtimeSinceStartupAsDouble;
            sfx.FadeLayer(0.25f);
            _layerBeat = int.MinValue;
        }

        private void TickMusicFx(double t)
        {
            // After a big miss the song goes underwater and clears; it is always fully open again 150 ms
            // before the next note, so the beat is there when the player needs it.
            if (_muffleHz > 0f)
            {
                float since = (float)(Time.realtimeSinceStartupAsDouble - _muffleAt);
                float ahead = (float)(NextPendingNoteTime(t) - t - 0.15);
                float u = Mathf.Clamp01(Mathf.Max(since / _muffleDur, 1f - ahead / _muffleDur));
                _lpf.cutoffFrequency = u >= 1f ? 22000f : _muffleHz * Mathf.Pow(22000f / _muffleHz, u * u);
                if (u >= 1f) _muffleHz = 0f;
            }
            else _lpf.cutoffFrequency = 22000f;

            // Overdrive: a gentle high-pass swell over one beat (the kick stays audible).
            _hpf.cutoffFrequency = _overdriveOn ? Mathf.Lerp(300f, 10f, Mathf.Clamp01((float)(t - _overdriveStart) / _song.SecondsPerBeat)) : 10f;
            if (_state == State.Playing) clock.Music.volume = PlayingMusicVolume(t);

            // Streak layers: chord stabs on beats 2 and 4 from GROOVE, an eighth-note arpeggio from FEVER.
            if (_tier < 1 || !GameSettings.HitSound || _finaleDone)
            {
                _layerBeat = int.MinValue;
                return;
            }

            int now = _song.BeatIndexAt(t);
            if (_layerBeat < now) _layerBeat = now + 1;
            double dspNow = AudioSettings.dspTime;
            while (true)
            {
                double bt = _song.BeatTime(_layerBeat);
                double dsp = clock.StartDsp + bt;
                if (dsp > dspNow + 0.45) break;
                if (dsp > dspNow + 0.01)
                {
                    int pos = ((_layerBeat - _song.downbeatPhase) % 4 + 4) % 4;
                    if (pos == 1 || pos == 3) sfx.PlayLayerAt(sfx.Stab(pos == 1 ? 0 : 1), dsp, 0.6f);
                    if (_tier >= 2)
                    {
                        double half = 0.5 * (_song.BeatTime(_layerBeat + 1) - bt);
                        sfx.PlayLayerAt(sfx.ArpNote(_layerBeat * 2), dsp, 0.40f);
                        sfx.PlayLayerAt(sfx.ArpNote(_layerBeat * 2 + 1), dsp + half, 0.32f);
                    }
                }
                _layerBeat++;
            }
        }

        // ================================================================ camera, hints, ticks

        private void TickCameraCues(double t)
        {
            var cues = _chart.cameraCues;
            bool changed = false;
            while (_cueIndex + 1 < cues.Count && cues[_cueIndex + 1].time <= t)
            {
                _cueIndex++;
                changed = true;
            }
            if (changed)
            {
                // Dance-cam cuts snap on the beat; phrase changes blend over two beats with a whoosh.
                bool cut = cues[_cueIndex].cut;
                cameraRig.SetMode(cues[_cueIndex].mode, cut ? 0.12f : _song.SecondsPerBeat * 2f);
                if (!cut) sfx.PlayNow(sfx.Whoosh, 0.35f);
            }
        }

        private void UpdateHints(double t)
        {
            var notes = _layout.Notes;
            for (int i = _nextJudge; i < notes.Count; i++)
            {
                PlayNote n = notes[i];
                if (n.time > t + 0.9) break;
                if (n.judged || _hinted.Contains(n)) continue;
                string hint = null;
                Color col = UIFactory.Cyan;
                switch (n.type)
                {
                    case NoteType.Hold: hint = "HOLD"; col = UIFactory.Gold; break;
                    case NoteType.Swipe: hint = n.swipe == SwipeDir.Left ? "SWIPE LEFT" : n.swipe == SwipeDir.Right ? "SWIPE RIGHT" : "SWIPE UP"; col = UIFactory.Pink; break;
                    case NoteType.Dual: if (n.side == TapSide.Left) { hint = "TAP BOTH"; col = UIFactory.Violet; } break;
                }
                if (hint == null && n.ball > 0 && (i == 0 || !HasRecentCompanion(n))) { hint = "+1 BALL"; col = UIFactory.Gold; }
                _hinted.Add(n);
                if (hint != null)
                {
                    Vector3 at = n.type == NoteType.Dual && n.partner != null ? 0.5f * (n.position + n.partner.position) : n.position;
                    ui.ShowHint(hint, at, cameraRig.Cam, col);
                }
            }
        }

        private bool HasRecentCompanion(PlayNote n)
        {
            var notes = _layout.Notes;
            for (int i = n.index - 1; i >= 0; i--)
            {
                if (n.time - notes[i].time > _song.SecondsPerBeat * 8.5) return false;
                if (notes[i].ball == n.ball) return true;
            }
            return false;
        }

        private void ScheduleTicks()
        {
            var notes = _layout.Notes;
            double dspNow = AudioSettings.dspTime;
            while (_nextTick < notes.Count)
            {
                PlayNote n = notes[_nextTick];
                // Tick plays through the same audio path as the music, so it uses raw (unshifted) song time.
                double dsp = clock.StartDsp + n.time;
                if (dsp > dspNow + 0.5) break;
                if (GameSettings.AssistTick && dsp > dspNow + 0.005 && n.time - _lastTickTime > 0.01)
                {
                    sfx.PlayAt(n.kind == NoteKind.Downbeat ? sfx.TickAccent : sfx.Tick, dsp, 0.8f);
                    _lastTickTime = n.time;
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

        private float Fever => _score == null ? 0f : _tierVis * 0.15f + (_overdriveOn ? 0.6f : 0f);

        // ================================================================ world

        private void UpdateWorld(double t)
        {
            Vector3 pos = _layout.BallAt(t, out _ballVelocity);
            ball.SetPose(pos, _ballVelocity);
            ball.SetTrailWidth(1f + 0.15f * _tierVis + (_overdriveOn ? 0.36f : 0f));
            UpdateExtraBalls(t);
            _impactFx.Tick(t, cameraRig.transform);

            BindRings(t);
            double approach = Mathf.Clamp(_song.SecondsPerBeat * 2f, 0.55f, 1.1f);
            for (int i = 0; i < _ringPool.Count; i++)
            {
                RingView v = _ringPool[i];
                if (v.Free) continue;
                PlayNote n = v.Note;
                double ap = n != null ? approach * n.approachScale : approach; // surge rings rush in
                bool target = n != null && !n.judged && n.time - t <= ap * 1.05;
                v.Tick(t, ap, target, Fever, ball.transform.position);
            }

            TintPad(_song.accent, t < _layout.PadLeaveTime ? 0.9f : 0.25f, _song, t);

            Vector3 target2 = pos;
            if (_nextJudge < _layout.Notes.Count) target2 = Vector3.Lerp(pos, _layout.Notes[_nextJudge].position, 0.35f);
            cameraRig.SetCrowd(_ballFor.Count > 0 ? 1f : 0f);
            cameraRig.Follow(new Vector3(target2.x * 0.35f * (1f - cameraRig.Crowd), target2.y + 0.5f, 0f), 0.22f);
            background.Tick(_song, t, pos, Fever);
        }

        private void UpdateExtraBalls(double t)
        {
            var paths = _layout.Paths;
            for (int i = 0; i < paths.Count; i++)
            {
                BallPath p = paths[i];
                if (p == _layout.MainPath) continue;
                bool active = t >= p.Start && t <= p.End;
                _ballFor.TryGetValue(p, out BallView v);
                if (!active)
                {
                    if (v != null)
                    {
                        v.gameObject.SetActive(false);
                        _ballFor.Remove(p);
                    }
                    continue;
                }

                if (v == null)
                {
                    v = GetExtraBall();
                    _ballFor[p] = v;
                    v.SetAccent(p.tint);
                    v.Teleport(p.Evaluate(t, out _));
                    v.gameObject.SetActive(true);
                }
                Vector3 pos = p.Evaluate(t, out Vector3 vel);
                v.SetPose(pos, vel);
            }
        }

        private BallView GetExtraBall()
        {
            foreach (var b in _extraBalls)
            {
                if (!b.gameObject.activeSelf && !_ballFor.ContainsValue(b)) return b;
            }
            var go = Instantiate(ball.gameObject, ball.transform.parent);
            go.name = "Ball (extra)";
            var v = go.GetComponent<BallView>();
            v.SetScale(0.8f);
            _extraBalls.Add(v);
            return v;
        }

        private void HideExtraBalls()
        {
            foreach (var b in _extraBalls) b.gameObject.SetActive(false);
            _ballFor.Clear();
        }

        private BallView BallForNote(PlayNote n)
        {
            if (n.ball == 0 && n.side != TapSide.Right) return ball;
            foreach (var kv in _ballFor)
            {
                if ((n.ball > 0 && kv.Key.ball == n.ball) || (n.side == TapSide.Right && kv.Key.ball == -1)) return kv.Value;
            }
            return ball;
        }

        private void BindRings(double t)
        {
            var notes = _layout.Notes;
            int bound = 0;
            for (int i = _nextJudge; i < notes.Count && bound < 32; i++)
            {
                PlayNote n = notes[i];
                if (n.time > t + 3.6) break;
                bound++;
                if (n.judged || n.view != null) continue;
                RingView v = GetFreeRing();
                Color acc = n.ball > 0 ? n.tint : _song.accent;
                Color acc2 = _song.accent2;
                if (n.twistTint.a > 0f) acc = acc2 = n.twistTint; // gold rings, boss rings
                v.Bind(n, acc, acc2);
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

        /// <summary>The start pad is a big 16-segment ring whose segments charge on every beat (a visual metronome).</summary>
        private void TintPad(Color accent, float intensity, SongData song, double t)
        {
            float charge = 0f;
            if (song != null && song.IsAnalyzed && !double.IsNaN(t))
            {
                int k = song.BeatIndexAt(t);
                double b0 = song.BeatTime(k), b1 = song.BeatTime(k + 1);
                charge = Mathf.Clamp01((float)((t - b0) / System.Math.Max(0.05, b1 - b0)));
            }
            Color c = accent;
            c.a = intensity;
            _mpb.Clear();
            _mpb.SetColor(RingView.ColorId, c);
            _mpb.SetColor(RingView.FillColorId, accent * 1.2f);
            _mpb.SetFloat(RingView.FillId, charge);
            _mpb.SetFloat(RingView.BurstId, 0f);
            _mpb.SetFloat(RingView.ZoneId, 0.5f);
            _mpb.SetFloat(RingView.SegmentsId, 16f);
            _mpb.SetFloat(RingView.ThicknessId, 0.06f);
            _mpb.SetFloat(RingView.SpinId, 0f);
            _mpb.SetVector(RingView.ArrowId, Vector4.zero);
            _padRenderer.SetPropertyBlock(_mpb);
        }

        private void OnBeat(int beat, bool downbeat)
        {
            // While playing, hits drive the camera; the metronome kick stays as a light ambient pulse.
            if (_state == State.Playing || _state == State.Menu) cameraRig.Kick((downbeat ? 0.6f : 0.25f) * (_state == State.Playing ? 0.4f : 1f));
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
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                clock.Resume(0.05);
                // Re-arm assist ticks and combo layers from the resume point; holds resume as held.
                double t = clock.SongTime;
                _nextTick = _layout.FindNextNote(t);
                _layerBeat = int.MinValue;
                _requeueStings = true;
                foreach (var h in _holds) h.holdId = -1;
                _state = State.Playing;
            }
        }

        // ================================================================ end of song

        private void BeginFail()
        {
            _state = State.Failing;
            _twists.End();
            _failTimer = 0f;
            _failVelocity = _ballVelocity;
            _failProgress = Progress(clock.SongTime);
            ui.UpdateOverdrive(0f, false);
            clock.Freeze();
            sfx.StopScheduled();
            sfx.StopHold();
            sfx.StopLayer();
            cameraRig.Shake(1.2f);
            sfx.PlayNow(sfx.Miss, 0.8f);
        }

        private void TickFailing()
        {
            _input.Clear();
            float dt = Time.unscaledDeltaTime;
            _failTimer += dt;
            clock.Music.pitch = Mathf.Max(0f, 1f - _failTimer / 1.4f);
            _lpf.cutoffFrequency = Mathf.Lerp(_lpf.cutoffFrequency, 400f, dt * 3f);
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
            _twists.End();
            _resultsFade = 0f;
            _impactFx.Clear();
            if (!failed && _finaleVoice >= 0) sfx.StopScheduledExcept(_finaleVoice);
            else sfx.StopScheduled();
            sfx.StopHold();
            sfx.StopLayer();
            _overdriveOn = false;
            cameraRig.SetOverdrive(0f);
            background.SetOverdrive(0f);
            ui.UpdateOverdrive(0f, false);
            ball.SetTrailWidth(1f);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            if (!failed)
            {
                // The music plays on under the results: open the miss / overdrive filters.
                _lpf.cutoffFrequency = 22000f;
                _hpf.cutoffFrequency = 10f;
            }
            string rank = _score.Rank(failed);
            // Every real run counts (fails too); autoplay never does. A NoFail run that hit zero life is not a clear.
            var record = new GameSettings.RunRecord { before = GameSettings.GetBest(_song, _difficulty) };
            record.after = record.before;
            bool cleared = !failed && !_healthHitZero;
            float progress = failed ? _failProgress : 1f;
            if (!_autoplay) record = GameSettings.SubmitRun(_song, _difficulty, _score, rank, cleared, progress);
            _resultsStartVolume = clock.Music.volume;

            _score.ErrorStats(out float mean, out float std, out float median);
            bool suggest = !_autoplay && _score.Errors.Count >= 20 && Mathf.Abs(median) >= 8f && std < 45f;
            _suggestedOffsetMs = Mathf.Round(median);
            ui.ShowResults(_song, _difficulty, _score, failed, _autoplay, record, progress, _suggestedOffsetMs, suggest);
            Ads.SongFinished();

            Debug.Log("[DropBeat] Result " + _song.title + " " + _difficulty + (_autoplay ? " (auto)" : string.Empty) +
                      ": " + rank + " score=" + _score.Score + " acc=" + (_score.Accuracy * 100f).ToString("F2", CultureInfo.InvariantCulture) +
                      "% P/G/Good/M=" + _score.Perfect + "/" + _score.Great + "/" + _score.Good + "/" + _score.Miss +
                      " maxCombo=" + _score.MaxCombo + "/" + _score.TotalNotes + " meanErr=" + mean.ToString("F1", CultureInfo.InvariantCulture) + "ms std=" + std.ToString("F1", CultureInfo.InvariantCulture) + "ms");
        }

        private void TickResults()
        {
            _input.Clear();
            _resultsFade += Time.unscaledDeltaTime;
            if (clock.IsRunning && !clock.IsFrozen)
            {
                clock.Music.volume = _resultsStartVolume * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(_resultsFade / 2.5f));
                double t = clock.SongTime;
                Vector3 pos = _layout.BallAt(t, out Vector3 v);
                ball.SetPose(pos, v);
                UpdateExtraBalls(t);
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
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            clock.Stop();
            sfx.StopScheduled();
            ReleaseRings();
            HideExtraBalls();
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

            _input.Poll();
            while (_input.TryDequeue(out TapInput.Tap tap))
            {
                if (tap.kind != TapInput.Kind.Press || _calErrors.Count >= 32) continue;
                if (tap.isPointer && ui.IsOverCalibrationButtons(tap.screenPosition)) continue; // Save / Back presses are not samples
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
