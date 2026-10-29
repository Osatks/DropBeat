# DropBeat

A neon rhythm game for mobile (portrait) and desktop, built in Unity. A chrome ball hops up a chain of 3D neon rings; it lands in each ring **exactly on the beat**, you tap at that moment, and the ring launches it on to the next one.

## How to play

1. Open the project in Unity `6000.5` (URP, Input System).
2. Open `Assets/DropBeat/Scenes/Game.unity` and press Play.
3. Pick a song and difficulty, press **PLAY** (or Enter).

| Action | Touch | Keyboard / pad |
| --- | --- | --- |
| Hit | tap anywhere | Space, D F J K Z X, gamepad A / triggers |
| Hold | keep the finger down, lift on the end beat | keep Space held |
| Swipe | drag left / right / up on the beat | arrow keys, d-pad |
| Dual rings | two thumbs (left and right half) | D/F + J/K together |
| Pause | pause button | Esc or P |
| Menu | tap | Up/Down = song, Left/Right = difficulty, Enter = play |
| Results | tap | R = retry, Esc/Enter = song select |

Each ring's segments light up clockwise and complete exactly on the beat, while a thin approach circle closes onto it. Judgement windows (Normal): Perfect ±45 ms, Great ±90 ms, Good ±135 ms. Easy is 20% wider and Hard 10% tighter. The bar at the bottom shows how early or late every tap was.

### Gameplay elements

Placed by `ChartDesigner` from the music's structure (8-bar phrases ranked by energy), always on the beat grid:

- **Launcher rings**: every ring is a solid 3D hoop aimed along the ball's launch, so the ball always shoots out orthogonal to the ring. Rings alternate left and right; in the angled and tunnel views the route also zigzags in depth.
- **Impact notes** on the song's big moments (drops, new sections, its hardest hits): bigger gold rings with a deep boom, burst and camera shake.

- **Hold rails** on long, calm gaps: the ball grinds a gold rail; a riser builds while you hold.
- **Swipe dashes** where a section kicks in: chevron rings; swipe the shown way and the ball dashes through with a whoosh and glass smash.
- **Dual rings** on the heaviest kicks: the ball splits in two; hit both rings (stereo hit sounds).
- **Companion balls** in the most intense phrases: gold/green/orange balls fly in and land on the beats your ball skips; each catch adds a harmony note. Your ball keeps to a centre lane and each companion to its own outer lane, always in the side view, so several balls stay readable.
- **Moving rings** in mid-energy phrases: they sway in rhythm and arrive exactly where the ball lands.
- **Overdrive**: Perfect hits charge the meter; when full the world turns gold, the camera sways, the music swells and score is doubled for 16 beats.
- **Perspective shifts**: side, angled and tunnel views follow the song's sections. The sky turns with the camera and the grid wall and floating cubes are fixed in the world, so a change of view reads as real depth.
- **Reactive music**: a miss muffles the music for a moment; a long combo adds claps and an arpeggio in the song's key.

Hit sounds are synthesized in the song's detected key (Krumhansl key estimate) and climb a pentatonic scale as your combo grows.

Run **CALIBRATE** once per audio device (speakers and Bluetooth headphones differ a lot). After a song the results screen also suggests an offset if your taps were consistently early or late.

Menu options: Autoplay (demo), Assist Tick (a click on every note, scheduled on the audio clock, useful for checking sync by ear), Hit Sound, No Fail.

## Why the timing is right

The old prototype picked "high-presence" peaks and moved the ball with physics, so notes could land on the wrong half of the beat and the ball could drift from the music. The redesign fixes timing at every stage:

1. **Beat analysis** (`BeatAnalyzer`). Spectral-flux onsets at ~5 ms resolution → tempo from autocorrelation → tempo refined to 0.0005 BPM by folding the onsets over the whole song → **beat vs off-beat decided by the kick drum** (off-beat hats and bass are often louder in broadband and fooled the old system) → the detector's own timing bias is measured on a synthetic kick track and removed → per-segment drift check.
2. **Analysis runs on Unity's own decoded samples.** Unity's MP3 decoder starts these files ~49 ms later than ffmpeg does, so analysing the same samples that will be played removes that error.
3. **Charts only use grid positions.** Every note is a beat (or an exact half-beat on Hard); the music only decides *which* beats get notes: beats where a kick or snare/clap hit stands out from its neighbours, following the song's repeating 2-bar groove. Beats with nothing to feel are skipped. See `ChartGenerator`.
4. **Audio clock** (`SongClock`). The music is started with `PlayScheduled` on the DSP clock. Song time is a smoothed, monotonic estimate locked to the audio hardware, minus the calibrated output latency.
5. **The ball is a function of song time** (`LevelLayout.BallAt`). It reaches ring *i* at note *i* by construction, so it can never drift.
6. **Taps use input-event timestamps** (`TapInput`), not frame times, so a slow frame doesn't change your judgement.

Verified results (2026-10-29):

- Synthetic songs with known beats (96–172 BPM, off-beat bass and hats, 2 sample rates): tempo error < 0.01 BPM, first-beat error ≤ 1.5 ms, beat/off-beat always correct (`DropBeat > Validate Beat Analyzer`).
- Real songs: *Neon Orbit Run* 128.000 BPM (drift 2 ms); *Overtime* 172.018 BPM (drift 5 ms). Beat-synchronous averaging shows the drum attacks within ~5 ms of the grid.
- In-game: injected key events land with 0.0 ms measured error at 170 fps, and exactly +60.0 ms when pressed 60 ms late at 15 fps. Music position matches the clock within 0.02 ms after pause/resume.

## Install on an iPhone (from Windows)

Unity on Windows can only export the Xcode project; the `.ipa` is compiled on a GitHub macOS runner.

1. In Unity: build iOS to `Builds/iOS` (the post-build step enables 120 Hz ProMotion).
2. Zip the folder's contents as `DropBeat-iOS-xcode.zip` and upload it to the `ios-xcode` release (replace the old one).
3. Run the **iOS IPA (unsigned)** workflow (`.github/workflows/ios-ipa.yml`, on the `ios-build` branch). It attaches `DropBeat.ipa` to the release.
4. Install it with **Sideloadly** (signs it with your Apple ID). With a free Apple ID the app must be re-installed every 7 days.

The app icon is drawn by `Tools/make_icon.py` (`python Tools/make_icon.py Assets/DropBeat/Icons/AppIcon.png`).

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
  Shaders/          Ring3D (solid segmented ring), Ring (SDF glow, arrows), Chrome (metal ball), Backdrop (nebula + sun), Neon, NeonAdditive
  Icons/            AppIcon.png
  Scripts/
    Analysis/       FFT, BeatAnalyzer
    Data/           SongData, SongLibrary, ChartGenerator (beats to notes), ChartDesigner (elements + camera cues)
    Core/           SongClock, TapInput, SfxPlayer, GameSettings
    Gameplay/       GameController (state machine), LevelLayout, Judge/ScoreState
    Visual/         RingView, BallView, CameraRig, BackgroundFX, HitBurst, ProceduralMeshes
    UI/             GameUI (menu, HUD, pause, results, calibration), UIFactory (neon-glass kit)
    Debug/          TimingProbe (injects timed taps, holds, dual presses and swipes to test judging)
    Editor/         SongTools (analysis, validation, click preview), SceneBuilder
```

Rings, the ball trail, the grid and the UI are generated at runtime, so the scene file stays small. The old prototype's main scene was 150 MB because of ring meshes saved inside it.
