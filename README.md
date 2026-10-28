# DropBeat

A neon rhythm game for Unity. A chrome ball hops up a chain of rings; it lands in each ring **exactly on the beat**, and you tap at that moment.

## How to play

1. Open the project in Unity `6000.5` (URP, Input System).
2. Open `Assets/DropBeat/Scenes/Game.unity` and press Play.
3. Pick a song and difficulty, press **PLAY** (or Enter).

| Action | Keys |
| --- | --- |
| Hit | Space, D, F, J, K, Z, X, left mouse, touch, gamepad A / triggers |
| Pause | Esc or P (or the pause button) |
| Menu navigation | Up/Down = song, Left/Right = difficulty, Enter = play |
| Results | R = retry, Esc/Enter = song select |

Each ring shows a shrinking approach circle that closes at the beat, and the ring glows gold just before it. Judgement windows (Normal): Perfect ±45 ms, Great ±90 ms, Good ±135 ms. Easy is 20% wider and Hard 10% tighter. The bar at the bottom shows how early or late every tap was.

Run **CALIBRATE** once per audio device (speakers and Bluetooth headphones differ a lot). After a song the results screen also suggests an offset if your taps were consistently early or late.

Menu options: Autoplay (demo), Assist Tick (a click on every note, scheduled on the audio clock, useful for checking sync by ear), Hit Sound, No Fail.

## Why the timing is right

The old prototype picked "high-presence" peaks and moved the ball with physics, so notes could land on the wrong half of the beat and the ball could drift from the music. The redesign fixes timing at every stage:

1. **Beat analysis** (`BeatAnalyzer`). Spectral-flux onsets at ~5 ms resolution → tempo from autocorrelation → tempo refined to 0.0005 BPM by folding the onsets over the whole song → **beat vs off-beat decided by the kick drum** (off-beat hats and bass are often louder in broadband and fooled the old system) → the detector's own timing bias is measured on a synthetic kick track and removed → per-segment drift check.
2. **Analysis runs on Unity's own decoded samples.** Unity's MP3 decoder starts these files ~49 ms later than ffmpeg does, so analysing the same samples that will be played removes that error.
3. **Charts only use grid positions.** Every note is a beat (or an exact half-beat on Hard); the music only decides *which* beats get notes. See `ChartGenerator`.
4. **Audio clock** (`SongClock`). The music is started with `PlayScheduled` on the DSP clock. Song time is a smoothed, monotonic estimate locked to the audio hardware, minus the calibrated output latency.
5. **The ball is a function of song time** (`LevelLayout.BallAt`). It reaches ring *i* at note *i* by construction, so it can never drift.
6. **Taps use input-event timestamps** (`TapInput`), not frame times, so a slow frame doesn't change your judgement.

Verified results (2026-10-29):

- Synthetic songs with known beats (96–172 BPM, off-beat bass and hats, 2 sample rates): tempo error < 0.01 BPM, first-beat error ≤ 1.5 ms, beat/off-beat always correct (`DropBeat > Validate Beat Analyzer`).
- Real songs: *Neon Orbit Run* 128.000 BPM (drift 2 ms); *Overtime* 172.018 BPM (drift 5 ms). Beat-synchronous averaging shows the drum attacks within ~5 ms of the grid.
- In-game: injected key events land with 0.0 ms measured error at 170 fps, and exactly +60.0 ms when pressed 60 ms late at 15 fps. Music position matches the clock within 0.02 ms after pause/resume.

## Adding songs

1. Drop an `.mp3`/`.wav`/`.ogg` into `Assets/DropBeat/Audio/`.
2. Run **DropBeat > Analyze All Songs**. This creates or updates `Assets/DropBeat/Songs/<name>.asset` and the library in `Assets/DropBeat/Resources/SongLibrary.asset`. Check the console log; it warns when the beat/off-beat choice is uncertain.
3. Optional: select the song asset and use **Export Click Preview** to write `Verification/<song>_Normal_clicks.wav` (song + a click on every note) and listen. If the clicks sit between the kicks, tick *Flip Half Beat* and press **Analyse**. If the tempo is wrong, set *Bpm Override*. *Song Offset Ms* nudges a single song.

## Project layout

```text
Assets/DropBeat/
  Audio/            songs
  Songs/            SongData assets (beat grid + analysis log)
  Resources/        SongLibrary
  Scenes/Game.unity the only scene (rebuild with DropBeat > Build Game Scene)
  Shaders/          DropBeat/Neon (emissive + rim), DropBeat/NeonAdditive (glow)
  Scripts/
    Analysis/       FFT, BeatAnalyzer
    Data/           SongData, SongLibrary, ChartGenerator
    Core/           SongClock, TapInput, SfxPlayer, GameSettings
    Gameplay/       GameController (state machine), LevelLayout, Judge/ScoreState
    Visual/         RingView, BallView, CameraRig, BackgroundFX, HitBurst, ProceduralMeshes
    UI/             GameUI (menu, HUD, pause, results, calibration), UIFactory
    Debug/          TimingProbe (injects timed key presses to test the input pipeline)
    Editor/         SongTools (analysis, validation, click preview), SceneBuilder
```

Rings, the ball trail, the grid and the UI are generated at runtime, so the scene file stays small. The old prototype's main scene was 150 MB because of ring meshes saved inside it.
