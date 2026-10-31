# DropBeat style and game feel

This is how DropBeat looks and why hitting a ring feels satisfying. Keep to it when adding songs, twists, skins or UI, so the game stays one consistent thing.

Developer notes are in [CLAUDE.md](CLAUDE.md); the player overview is in [README.md](README.md).

## The core idea

Every good hit answers the tap with sound, picture and motion, all on the same instant and in time with the music.

- **Correct first.** A tap feels good only if the ring was really on the beat. Notes sit on the beat grid and the ball is a function of song time, so the reward never fights the music. Never trade timing for an effect.
- **Notes only where you feel a beat.** Rings go on kicks and snares that stand out, not on every beat. Empty beats are rests; they make the next hit land harder.
- **Every grade is clearly different.** Perfect, Great and Good each sound and look different, so the player always knows how they did without reading.
- **The game builds as you play.** Streaks add music layers and bigger effects, and the song ends on a finale, so a run feels like it's going somewhere.
- **Misses sting, but fairly.** A small slip is a short dip; losing a big streak hurts more. Nothing ever punishes the next tap.

## Visual style: neon glass in space

### Mood

Atmospheric, modern and dark, never "simple". Glowing neon on a deep space or scene background, with a chrome ball. Everything is portrait-phone first.

### The play area

- **Rings:** solid, segmented 3D hoops (`Ring3D` shader) with a soft glow ring behind (`Ring` shader, signed-distance, antialiased with `fwidth`).
  - The segments light up clockwise and finish exactly on the beat, while a thin approach circle closes onto the ring.
  - Rings face the ball's launch direction, so the ball always shoots straight out.
  - Avoid thin torus rings; the user disliked them.
- **Ring colours:** each song's accent and second accent from `Tools/themes.json`. Gold is reserved for special rings (impact notes, golden rings). Boss rings cycle through an 8-bit palette (red, gold, cyan, magenta, green).
- **Ball:** chrome metal (`Chrome` shader) with skins in `BallStyles`. It squashes on a spring when it lands and flashes on a hit.
- **Trail:** a soft additive line. The trail styles are classic, rainbow, comet, sparkle, pixel, electric and ghost.
- **Background:** one scene per song (synthwave sun, ice aurora, city skyline, pixel sky, desert, underwater, galaxy, and so on) from the `Backdrop` shader. A grid wall and floating cubes stay fixed in the world, so camera moves read as real depth. The sky reacts to the music's spectrum and to hits.
- **Camera views:** side, angled and tunnel, chosen from the song's sections.
  - The side view is the flat, readable view; multi-ball phrases always use it.
  - Any camera roll must keep every ring on screen. See the warp fix in CLAUDE.md.

### Colour palette (UI)

From `UI/UIFactory.cs`:

| Name | RGB | Used for |
|---|---|---|
| Text | 0.94, 0.96, 1.0 | main text |
| Dim | 0.62, 0.64, 0.80 | secondary text |
| Cyan | 0.20, 0.95, 1.0 | selection, hot buttons, Great |
| Pink | 1.0, 0.35, 0.85 | accents, gradient end |
| Gold | 1.0, 0.85, 0.35 | Perfect, rewards, stars, records |
| Green | 0.40, 1.0, 0.55 | good news, clears |
| Red | 1.0, 0.30, 0.38 | misses, health |
| Violet | 0.55, 0.40, 1.0 | glass cards, buttons |

- **Glass cards:** dark violet fill with a softly lit lavender edge (`GlassTint`).
- **Primary buttons:** pills with a cyan-to-pink gradient and a press animation.
- **Rewards:** stars use the filled and outline star sprites, in gold.
- **Font:** `Resources/Fonts/DropBeatFont` if present (drop in any TTF or OTF), else Unity's built-in font.

### UI layout

- **Menu:** a bottom sheet with the song carousel, difficulty and a big PLAY button, plus Settings, STYLE (the locker) and PROFILE sheets. Each song card shows its twist and its stars.
- **Profile:** a level ring that fills with XP, a title (ROOKIE, GROOVER, RHYTHM ACE, BEAT MASTER, LEGEND), 12 lifetime stat tiles and the two clear rewards with how to win them.
- **HUD:**
  - Top bar: pause button top-left, song and score, the health bar and the section bar.
  - The combo count sits in the middle of the screen with its tier name.
  - The timing bar sits at the bottom.
  - Popups stay clamped on screen and never cover the HUD.
- **Dims:** pause, results and calibration screens dim the whole screen, including the notch and home-bar areas.

## How a hit feels satisfying

All of these fire on the same frame as the judgement (`GameController`, in the hit branch of judging).

### Sound

- **The note:** a crisp note on the song's own instrument (pluck, koto, marimba, darbuka, chip blip, supersaw, and so on), in the song's key.
  - The notes walk a short riff tied to the bar and resolve on the tonic every 4-bar phrase, so tapping plays a melody instead of the same click.
  - `SfxPlayer`, `RiffStep`.
- **Same loudness for every instrument.** Each hit's level is measured over its first 120 ms through a phone-speaker weighting (high-pass 150 Hz, +4 dB shelf above 1.5 kHz), so every instrument sounds equally loud. The 13 styles are within about 2 dB of each other.
- **Grades are baked into the clips**, about 2.5 dB apart and with different brightness, so the difference is heard, not just seen. Good is a single plain note.
- **The streak stacks voices on Perfect and Great:**
  - Octave double from GROOVE.
  - Harmony from BLAZE.
  - A sparkle at LEGEND (not on Hard, where taps are too dense).
  - Overdrive adds a gold layer.
- **Music duck:** the music dips under the hit for a moment (`MixBus.Duck`): Perfect 4 dB, Great 3 dB, Good 1.5 dB, and impacts and swipes 7 dB. That leaves room for the hit without turning anything up.
- **No clipping, no clicks.** A soft clipper on the master catches peaks above 0.84, and every voice fades out over its last 12 ms.
- **Specials:**
  - Dual rings pan left and right.
  - Companion balls add a harmony note above the main hit, so more balls build a chord.
  - Swipes add a whoosh and a glass smash.
  - Impact notes add a deep falling boom, a crack and a shimmer.

### Picture

- **The ring itself:**
  - It flashes bright, more for higher grades and for streaks.
  - The approach circle rebounds outward as a shockwave.
  - A miss makes the ring implode.
- **Sparks:** sparks fly off the ring's rim (Perfect 24, Great 14, more at higher tiers, up to 48), plus a burst.
- **Perfect glint:** a Perfect gets a quick 4-point star glint (`ImpactFX`), 140 ms long and bigger on long streaks.
- **The ball:** it pulses gold-white on a Perfect.
- **The world:** the background flashes and a shockwave runs through the sky.
- **The HUD:** the combo and score punch on every hit, and a popup shows the grade, points and chain.
- **Keep flashes moderate.** Stacking a white-hot flash, glint and ball glow once bloomed into a screen-filling white blob. Bright is good; blinding is not. Keep the current values in CLAUDE.md as the ceiling.

### Motion

- **Camera kick:** a quick zoom kick on every hit (Perfect strongest), plus a tiny punch along the ball's travel that peaks about 29 ms after the hit and is gone by 150 ms.
- **Impacts and swipes:** they add camera shake and a bigger kick.

## The reward loop over a run

- **Streak ladder:** GROOVE, FEVER, BLAZE, LEGEND (Normal 10 / 20 / 40 / 80; Easy 6 / 12 / 24 / 48; Hard 15 / 30 / 60 / 120).
  - Each level-up waits for the next downbeat, then lands with its own sting, a world flash and a new music layer (chord stabs, then an arpeggio).
  - The hit notes climb an octave from FEVER.
- **Overdrive:** Perfect hits fill the meter. When full, the world turns gold, the camera sways, the music swells and the score doubles for 16 beats. Its boom lands on a beat, and it ends on a payout banner.
- **Sections:** every 8 bars is a section on the section bar. Finish one with no miss for SECTION CLEAR (+10%); all Perfect gives FLAWLESS (+25%). The sting is scheduled on the beat grid.
- **Finale:** the last ring is a big hit; the song ends on an in-key chord, fireworks and SONG CLEAR, FULL COMBO or ALL PERFECT.
- **Results reveal:**
  - The score counts up, the rank slams in with its own sound and the stars fill in.
  - A NEW BEST or FIRST CLEAR stamp appears with confetti, and the next goal is shown.
  - Records never go backwards. Tap to skip.
- **Long-term goals:**
  - 3 stars per chart (clear, full combo, rank A), 135 in total, shown on the menu.
  - Songs unlock for 5 ads each; skins and trails for 10 ads each.
  - **Clear rewards** can't be bought with ads, only won. The first clear of any song on Normal or Hard wins the **Aurora** trail (a wide northern-lights ribbon whose colours drift). The first clear on Hard wins the **Champion** ball (a cut diamond throwing rainbow fire, with glowing gold seams). The results card announces each one with its spinning preview, a fanfare and confetti.
  - **Player level:** every real run earns XP (1 per 100 points, plus 50 per clear). Each level needs 250 XP more than the last.
- **Misses by size:**
  - A small slip: a 1.9 dB dip and a light shake.
  - Losing a mid streak: the music is muffled (low-pass 1600 Hz for 0.7 s).
  - Losing a big streak: a deeper muffle (900 Hz for 1 s), a stronger shake and a COMBO BREAK banner.
  - After three fails in a row the game offers the song on Easy.

## Song twists

Every song has one trick that makes it feel different. A twist must:

1. Change what the player sees or does, not just a number.
2. Stay fair:
   - Never move a ring's beat.
   - Never hide where a tap is due.
   - Never put swipes or dual rings where the screen is turning.
3. Show itself: a banner when it starts, a badge in the HUD, and a line on the song card.
4. Keep every ring on screen and readable on a portrait phone.

| Song | Twist | What it does |
|---|---|---|
| Neon Orbit Run | Golden Rings | gold rings score ×5 |
| Overtime | Slide Rush | rings slide in from the sides and the top on the drums; faster in the last stretch |
| Solar Drift | Drift | the world banks gently into turns |
| Glacier Bloom | Ice Slide | every ring glides into place |
| Voltage Run | Voltage Surge | rings rush in faster |
| 8-Bit Hero | Boss Fight | a pixel boss with four attacks (orbit, barrage, sweep, spiral); every hit blasts it with a laser |
| Jade Temple | Spirit Balls | extra balls from the very first beat (3 balls on Hard) |
| Pixel Arcade | Gravity Flip | the screen turns upside down |
| Desert Mirage | Mirage | rings slide in from the mirrored side |
| Deep Ocean | Sonar | rings hide until the sonar ping shows them |
| Sakura Funk | Petal Fall | rings float down like petals |
| Thunder Forge | Anvil Strikes | every other bar ends on a big hit |
| Cosmic Trance | Warp Tunnel | the tunnel corkscrews (taps only) |
| Jungle Pulse | Stampede | double-time drum runs |
| Midnight Disco | Dance Cam | the camera cuts every bar |

### The boss fight (8-Bit Hero)

- **The boss:** a crowned pixel demon made of glowing voxels (purple body, red eyes, gold crown, white horns and teeth), floating behind the ring circle so rings always pass in front of it.
  - It grows in with an overshoot four beats before the finale, bobs on the beat, stomps on each bar and sways a little so its voxel depth shows.
  - Low health makes it flicker red.
- **The arena:** the camera holds the side view and frames the whole fight. The boss switches attack every 4 bars, cycling through four, each with its own banner, a roar (eyes flare, body swells) and a camera kick:
  - **Orbit:** rings spiral out of the boss onto a carousel turning against the ball (30° per beat), so each comes round to meet it; the ball circles anticlockwise.
  - **Barrage:** the boss fires each ring out to its spot on a fast, spinning, curved flight, its eyes flaring with every shot. Shots leave on the half-beat grid. The ball zigzags left and right across the boss.
  - **Sweep:** rings race in from the screen edge along rows above and below the boss, weaving and braking onto their spots; the ball runs along a row, then leaps to the other row and back.
  - **Spiral:** rings spiral in from outside, turning the other way, onto a radius that breathes in and out; the ball circles clockwise.
- **Readable:** every attack ends with the ring on its spot exactly on its beat. Each ring shows as early as possible without ever coming within 1.75 units of another ring on screen.
- **The beauty layer:** three halos of pixel dots in the 8-bit palette.
  - The middle one turns with the orbit carousel; the inner and outer ones turn the other way.
  - All three pulse on the beat.
- **Every hit:** a chunky pixel laser from the ring into the boss, a short hot flash (not a white-out) and a recoil away from the ring.
- **Defeat:** the boss bursts into spinning voxels that fly out and fall, the halos blow outward and fade, and the screen shows "BOSS DEFEATED! +10000". If the song ends first, the boss flies off upward.

### Slide Rush (Overtime)

- **Where the rings come from follows the drums.** The big hits and the kick opening every other bar drop in from the top; the rest sweep in from their own side of the screen like a snare. Centre rings alternate sides by beat.
- **The slide keeps time.** It takes one beat, starts fast and eases in, and lands on a beat of the grid: as the ring's timing circle appears. A small bounce and a lock-in flash mark the landing.
- **Rings stay dark while they wait off screen**, so no camera view shows a pile of waiting rings.
- **Overtime stretch:** in the phrase holding the last 30% of the chart, a banner shows "OVERTIME! FASTER", and from then on rings land halfway through their timing circle instead.

## Checklist for new content

- **New song:**
  - Give it its own scene, accent colours, hit instrument and (if needed) `gainDb` and `hitKey` in `themes.json`.
  - Pick a twist that changes play, not just score.
  - Play it on all three difficulties and check every chart.
- **New effect:** fire it on the hit frame or schedule it on the beat grid, never on a timer. Check it doesn't stack into a white-out with the other hit effects.
- **New sound:**
  - Match its loudness the same phone-weighted way.
  - Fade its end.
  - Check it on a real iPhone speaker.
- **New UI:**
  - Use the `UIFactory` glass kit and palette.
  - Make it readable at phone width, and keep it clear of the notch and the home bar.
