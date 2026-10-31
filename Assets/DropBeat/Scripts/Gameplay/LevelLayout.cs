using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>Runtime state of one judgeable ring (a dual note becomes two of these).</summary>
    public sealed class PlayNote
    {
        public int index;
        public double time;
        public NoteKind kind;
        public NoteType type;
        public SwipeDir swipe;
        /// <summary>0 = main ball, 1..3 = companion balls.</summary>
        public int ball;
        /// <summary>Dual halves: Left / Right; otherwise Any.</summary>
        public TapSide side;
        public PlayNote partner;
        public bool moving;
        /// <summary>A big moment of the song (drop, new section, hardest hit): bigger ring, bigger hit.</summary>
        public bool impact;
        public float moveDir;
        public double movePeriod;

        // Song twist effects (see TwistDesigner.Decorate).
        public bool golden;
        /// <summary>Ring colour override (gold rings, boss rings); alpha 0 = none.</summary>
        public Color twistTint = Color.clear;
        /// <summary>Approach time multiplier (below 1 = the ring rushes in).</summary>
        public float approachScale = 1f;
        /// <summary>Only visible while it is the next target, or in a sonar ping.</summary>
        public bool hidden;
        public NoteFx fx;
        /// <summary>How long before its beat the fx motion runs.</summary>
        public double fxLead;
        /// <summary>Slide: offset the ring waits at, and how long before its beat it has arrived.</summary>
        public Vector3 fxFrom;
        public double fxSettle;
        /// <summary>Boss fight: which attack moves this ring (see <see cref="BossMove"/>).</summary>
        public BossMove bossMove;
        /// <summary>Orbit / spiral: the boss's centre, the ring's radius and landing angle (degrees) round it, and
        /// the carousel speed (degrees per second; the ring comes from further round and turns back to its spot).</summary>
        public Vector3 orbitCenter;
        public float orbitRadius;
        public float orbitAngle;
        public float orbitSpeed;
        /// <summary>Spiral: how fast the ring closes in on its radius (units per second).</summary>
        public float spiralGrow;
        /// <summary>Barrage / sweep: where the flight starts, how long it takes, and its curve (+1 / -1).</summary>
        public Vector3 fxStart;
        public double moveTime;
        public float moveCurve = 1f;

        /// <summary>Where the ring is exactly on the beat (the ball lands here).</summary>
        public Vector3 position;
        /// <summary>Ring orientation: turned to face the ball's direction of travel so it flies through.</summary>
        public Quaternion facing = RingView.Tilt;
        public float scale;
        public Color tint;

        // Hold notes
        public double endTime;
        public Vector3 railEnd;
        public bool holding;
        public int holdId;
        public bool tailJudged;
        public Grade tailGrade;

        // Judgement
        public bool judged;
        public Grade grade;
        public double error;
        public double judgedAt;
        public RingView view;

        /// <summary>Ring position at time t (moving rings sway and pass through <see cref="position"/> on the beat).</summary>
        public Vector3 PositionAt(double t)
        {
            if (judged) return position;
            Vector3 p = position;
            if (moving)
            {
                float phase = (float)((t - time) / movePeriod) * Mathf.PI * 2f;
                p += new Vector3(moveDir * 0.85f * Mathf.Sin(phase), 0.25f * Mathf.Sin(phase * 2f), 0f);
            }
            if (fx == NoteFx.Boss && t < time) return BossPositionAt(time - t);
            if (fx == NoteFx.Slide && t < time && fxLead > 0.0)
            {
                // Waits off screen, slides in over the beat before it arrives (fast, then easing in), then
                // settles with a small bounce well before its own beat.
                double dt = time - t;
                if (dt >= fxLead) return p + fxFrom;
                if (dt > fxSettle)
                {
                    float s = (float)((dt - fxSettle) / System.Math.Max(1e-3, fxLead - fxSettle));
                    return p + fxFrom * (s * s * s);
                }
                float bounce = (float)System.Math.Min(0.25, fxSettle * 0.8);
                float b = (float)((fxSettle - dt) / bounce);
                if (b < 1f) p -= fxFrom.normalized * (0.28f * Mathf.Sin(b * Mathf.PI) * (1f - b));
                return p;
            }
            if (fx != NoteFx.None && t < time && fxLead > 0.0)
            {
                // u: 1 at the start of the motion (or earlier), 0 on the beat, so the ring always ends on its spot.
                float u = Mathf.Clamp01((float)((time - t) / fxLead));
                if (fx == NoteFx.Mirage)
                {
                    // Waits on the mirrored side of the screen, then slides across to its real spot.
                    float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.75f, u));
                    p.x -= 2f * position.x * f;
                    p.y += 0.25f * Mathf.Sin(u * Mathf.PI) * f;
                }
                else if (fx == NoteFx.Petal)
                {
                    // Drifts down from above, swaying like a falling petal.
                    p.y += 2.6f * u * u;
                    p.x += 0.6f * Mathf.Sin(u * Mathf.PI * 3f) * u;
                }
            }
            return p;
        }

        /// <summary>
        /// Boss rings, <paramref name="dt"/> seconds before their beat. Every attack ends exactly on the ring's
        /// spot on its beat; before it shows (<see cref="fxLead"/>) the ring waits inside the boss or off screen.
        /// </summary>
        private Vector3 BossPositionAt(double dt)
        {
            double since = fxLead - dt; // time since the ring appeared
            switch (bossMove)
            {
                case BossMove.Orbit:
                {
                    // Spirals out of the boss onto the carousel, which turns it back to its spot.
                    if (since <= 0.0) return orbitCenter;
                    float a = (orbitAngle + orbitSpeed * (float)dt) * Mathf.Deg2Rad;
                    float r = orbitRadius * Mathf.Lerp(0.35f, 1f, Ease01(since / 0.35));
                    return orbitCenter + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
                }
                case BossMove.Spiral:
                {
                    // Comes in from outside, turning the other way round the boss as it closes in.
                    float d = (float)System.Math.Min(dt, fxLead);
                    float a = (orbitAngle + orbitSpeed * d) * Mathf.Deg2Rad;
                    float r = orbitRadius + spiralGrow * d;
                    return orbitCenter + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
                }
                case BossMove.Barrage:
                {
                    // Fired out of the boss: a fast curved flight that eases onto its spot.
                    if (since <= 0.0) return orbitCenter;
                    float u = Mathf.Clamp01((float)(since / moveTime));
                    float e = 1f - (1f - u) * (1f - u) * (1f - u);
                    Vector3 d = position - orbitCenter;
                    Vector3 side = new Vector3(-d.y, d.x, 0f).normalized * moveCurve;
                    return Vector3.LerpUnclamped(orbitCenter, position, e) + side * (1.1f * Mathf.Sin(u * Mathf.PI) * (1f - u));
                }
                default:
                {
                    // Sweep: races in from the screen edge along its row, weaving, and brakes onto its spot.
                    if (since <= 0.0) return fxStart;
                    float u = Mathf.Clamp01((float)(since / moveTime));
                    float e = 1f - (1f - u) * (1f - u) * (1f - u);
                    return Vector3.LerpUnclamped(fxStart, position, e) + new Vector3(0f, 0.7f * moveCurve * Mathf.Sin(u * Mathf.PI * 2f) * (1f - u), 0f);
                }
            }
        }

        private static float Ease01(double x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)x));

        /// <summary>Boss rings: 0 while still inside the boss (or off screen), lighting up as they appear.</summary>
        public float BossVisibility(double t)
        {
            if (fx != NoteFx.Boss || t >= time) return 1f;
            double since = fxLead - (time - t);
            switch (bossMove)
            {
                case BossMove.Spiral: return Ease01(since / 0.4);
                case BossMove.Sweep: return Ease01(since / (moveTime * 0.3));
                default: return Ease01(since / 0.35);
            }
        }

        /// <summary>Slide rings: dark while waiting off screen (any camera view), lighting up as the slide starts.</summary>
        public float SlideVisibility(double t)
        {
            if (fx != NoteFx.Slide || t >= time) return 1f;
            double slide = System.Math.Max(1e-3, fxLead - fxSettle);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)((fxLead - (time - t)) / (slide * 0.25))));
        }

        /// <summary>
        /// Ring orientation at time t: carousel rings turn with it, fired and swept rings spin in flight; all
        /// face their launch on the beat.
        /// </summary>
        public Quaternion FacingAt(double t)
        {
            if (fx != NoteFx.Boss || judged || t >= time) return facing;
            double dt = time - t;
            if (bossMove == BossMove.Orbit || bossMove == BossMove.Spiral)
                return Quaternion.Euler(0f, 0f, orbitSpeed * (float)System.Math.Min(dt, fxLead)) * facing;
            float u = Mathf.Clamp01((float)((fxLead - dt) / moveTime));
            float left = (1f - u) * (1f - u) * (1f - u);
            return Quaternion.Euler(0f, 0f, 540f * left * moveCurve) * facing;
        }
    }

    public enum NoteFx : byte { None, Mirage, Petal, Slide, Boss }

    /// <summary>The boss's attacks; the arena switches attack every 4 bars.</summary>
    public enum BossMove : byte
    {
        /// <summary>Rings spiral out of the boss and ride a carousel round it.</summary>
        Orbit,
        /// <summary>The boss fires each ring out to its spot on a curve; the ball zigzags across it.</summary>
        Barrage,
        /// <summary>Rings race in from the screen edges along rows above and below the boss.</summary>
        Sweep,
        /// <summary>Rings spiral in from outside, turning the other way; the ball circles clockwise.</summary>
        Spiral
    }

    /// <summary>A ball's route through time: a list of segments (arc, rail grind, dash).</summary>
    public sealed class BallPath
    {
        public enum SegKind : byte { Arc, Rail, Dash }

        public struct Seg
        {
            public double t0, t1;
            public Vector3 p0, p1;
            public SegKind kind;
        }

        public readonly List<Seg> segs = new List<Seg>(64);
        public int ball;
        public Color tint;
        public double Start => segs.Count > 0 ? segs[0].t0 : 0;
        public double End => segs.Count > 0 ? segs[segs.Count - 1].t1 : 0;

        public void Add(double t0, double t1, Vector3 p0, Vector3 p1, SegKind kind)
        {
            if (t1 <= t0) return;
            segs.Add(new Seg { t0 = t0, t1 = t1, p0 = p0, p1 = p1, kind = kind });
        }

        public Vector3 Evaluate(double t, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (segs.Count == 0) return Vector3.zero;
            if (t <= segs[0].t0) return segs[0].p0;
            Seg last = segs[segs.Count - 1];
            if (t >= last.t1) return last.p1;

            int lo = 0, hi = segs.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segs[mid].t0 <= t) lo = mid;
                else hi = mid - 1;
            }
            Seg s = segs[lo];
            if (t > s.t1)
            {
                // Gap between segments: hold the last point.
                return s.p1;
            }

            switch (s.kind)
            {
                case SegKind.Rail:
                {
                    float g = Mathf.Max(0.01f, (float)(s.t1 - s.t0));
                    float u = Mathf.Clamp01((float)((t - s.t0) / g));
                    velocity = (s.p1 - s.p0) / g;
                    return Vector3.LerpUnclamped(s.p0, s.p1, u);
                }
                case SegKind.Dash:
                {
                    // Fast launch, soft landing exactly on the beat (ease-out cubic).
                    float g = Mathf.Max(0.01f, (float)(s.t1 - s.t0));
                    float u = Mathf.Clamp01((float)((t - s.t0) / g));
                    float e = 1f - Mathf.Pow(1f - u, 3f);
                    float de = 3f * (1f - u) * (1f - u) / g;
                    velocity = (s.p1 - s.p0) * de;
                    return Vector3.LerpUnclamped(s.p0, s.p1, e);
                }
                default:
                    return LevelLayout.Flight(s.p0, s.p1, s.t0, s.t1, t, out velocity);
            }
        }
    }

    /// <summary>
    /// Places rings and builds every ball's route. Ball positions are pure functions of song time:
    /// each ball reaches each of its rings exactly on that note's beat, so nothing can drift.
    /// </summary>
    public sealed class LevelLayout
    {
        public static readonly Color[] CompanionTints =
        {
            new Color(2.6f, 1.6f, 0.2f, 1f),
            new Color(0.6f, 2.6f, 0.5f, 1f),
            new Color(2.6f, 0.6f, 0.3f, 1f)
        };

        /// <summary>All judgeable rings, time-sorted.</summary>
        public readonly List<PlayNote> Notes = new List<PlayNote>(512);
        public readonly List<BallPath> Paths = new List<BallPath>(16);
        public BallPath MainPath { get; private set; }
        public Vector3 PadPosition { get; private set; }
        public double PadLeaveTime { get; private set; }
        public double ExitTime { get; private set; }
        public float HalfWidth { get; private set; }
        public int JudgementCount { get; private set; }

        /// <summary>Boss fight: the finale's rings are moved by the boss's attacks (see <see cref="BossMove"/>).</summary>
        public bool HasBoss { get; private set; }
        public Vector3 BossCenter { get; private set; }
        public const float BossRadius = 3.2f;
        /// <summary>Carousel speed: the rings turn back toward the ball at this many degrees per beat.</summary>
        public const float BossOrbitPerBeat = 30f;
        /// <summary>When each attack starts (the arena switches attack every 4 bars).</summary>
        public readonly List<KeyValuePair<double, BossMove>> BossPhases = new List<KeyValuePair<double, BossMove>>();
        private static readonly BossMove[] BossCycle = { BossMove.Orbit, BossMove.Barrage, BossMove.Sweep, BossMove.Spiral };
        private int _bossBlock, _bossInBlock;
        private float _sweepDir, _sweepRow;

        private SongData _song;

        /// <summary>How long a ring's timing circle takes to close onto it (two beats, kept between 0.55 and 1.1 s).</summary>
        public static double ApproachTime(float secondsPerBeat) => Mathf.Clamp(secondsPerBeat * 2f, 0.55f, 1.1f);

        public void Build(SongData song, Chart chart, float halfWidth, int seed)
        {
            _song = song;
            Notes.Clear();
            Paths.Clear();
            HasBoss = false;
            BossPhases.Clear();
            _bossBlock = -1;
            var bossRings = new List<PlayNote>();
            double arenaStart = chart.twistSpans.Count > 0 ? chart.twistSpans[chart.twistSpans.Count - 1].start : 0.0;
            HalfWidth = Mathf.Max(1.8f, halfWidth);
            var src = chart.notes;
            if (src.Count == 0) return;

            // The map is designed (MapDesigner), not random: the same song always gets the same rings.
            float spb = song.SecondsPerBeat;
            float W = HalfWidth;

            var main = new List<ChartNote>(src.Count);
            var companionTimes = new List<double>();
            foreach (var c in src)
            {
                if (c.ball == 0) main.Add(c);
                else companionTimes.Add(c.time);
            }
            // While companion balls are on screen the main ball keeps to a centre lane and the companions
            // to fixed outer lanes, so rings of different balls never overlap.
            double shareWindow = spbD(song) * 6.0;
            bool Shared(double t)
            {
                int k = companionTimes.BinarySearch(t);
                if (k < 0) k = ~k;
                return (k < companionTimes.Count && companionTimes[k] - t <= shareWindow) ||
                       (k > 0 && t - companionTimes[k - 1] <= shareWindow);
            }
            float lead = spb * Mathf.Max(1, Mathf.CeilToInt(0.55f / spb));
            // Leave the pad on a beat of the grid (the pad hop lands on the grid), never too close to the first ring.
            int padBeat = song.BeatIndexAt(main[0].time - lead + 1e-6);
            PadLeaveTime = song.BeatTime(padBeat);
            if (main[0].time - PadLeaveTime < 0.3) PadLeaveTime = song.BeatTime(padBeat - 1);
            PadPosition = Vector3.zero;

            // ---- main ring positions
            var mainNotes = new List<PlayNote>(main.Count);
            var mainLanding = new List<Vector3>(main.Count);   // where the main ball lands
            var mainLeave = new List<Vector3>(main.Count);     // where it leaves from (rail end for holds)
            float x = 0f, y = 0f, z = 0f;
            double prevTime = PadLeaveTime;
            SwipeDir pendingDash = SwipeDir.None;
            int movingCount = 0;
            // Designed map, no randomness: each phrase draws a shape from the song's own palette.
            MapPattern[] plan = MapDesigner.PlanPhrases(song, chart);

            for (int i = 0; i < main.Count; i++)
            {
                ChartNote c = main[i];
                bool shared = Shared(c.time);
                float lim = shared ? W * CentreLane : W;
                // Next to companion balls the main ball must stay in its centre lane: a split ring or a
                // swipe dash would reach into the companions' outer lanes, so play those as plain taps.
                if (shared && (c.type == NoteType.Dual || c.type == NoteType.Swipe))
                {
                    c.type = NoteType.Tap;
                    c.swipe = SwipeDir.None;
                }
                float gap = (float)(c.time - prevTime);

                if (chart.twist == SongTwist.BossFight && TwistDesigner.InSpans(chart.twistSpans, c.time))
                {
                    // Boss fight: the arena sits round the boss, just above the last ring of the climb.
                    if (!HasBoss)
                    {
                        HasBoss = true;
                        // The first ring sits straight below the boss; keep it a full ring gap from the last one.
                        float up = Mathf.Max(1.3f, Mathf.Sqrt(Mathf.Max(0f, MinRingGap * MinRingGap - x * x - z * z)) + 0.05f);
                        BossCenter = new Vector3(0f, y + BossRadius + up, 0f);
                    }
                    var bn = PlaceBossRing(c, gap, spb, arenaStart, new Vector3(x, y, z));
                    Vector3 at = bn.position;
                    bossRings.Add(bn);
                    Notes.Add(bn);
                    mainNotes.Add(bn);
                    mainLanding.Add(at);
                    mainLeave.Add(at);
                    x = at.x;
                    y = at.y;
                    z = 0f;
                    prevTime = c.time;
                    continue;
                }

                float rise = Mathf.Clamp(0.7f + gap * 1.3f, 0.9f, 2.6f);
                float prevX = x, prevZ = z;

                // Where the design puts this beat (see MapDesigner); impact notes take centre stage.
                Vector2 place = MapDesigner.Place(song, plan, c.time, out _);
                float designedX = c.impact ? 0f : place.x * lim;
                if (pendingDash == SwipeDir.Left) { x -= 2.8f; rise *= 0.6f; }
                else if (pendingDash == SwipeDir.Right) { x += 2.8f; rise *= 0.6f; }
                else if (pendingDash == SwipeDir.Up) { rise += 1.8f; }
                else x = designedX;
                pendingDash = SwipeDir.None;

                // Depth: in the angled and tunnel views the route also moves toward and away from the
                // camera (spirals in the tunnel); the side view stays flat and readable.
                z = shared ? 0f : DepthFor(chart, c.time) * place.y;

                // Swipe rings sit on the opposite side so the dash has room.
                if (c.type == NoteType.Swipe)
                {
                    if (c.swipe == SwipeDir.Left) x = W * 0.6f;
                    else if (c.swipe == SwipeDir.Right) x = -W * 0.6f;
                    pendingDash = c.swipe;
                }

                if (x > lim) x = lim - (x - lim);
                else if (x < -lim) x = -lim + (-lim - x);
                x = Mathf.Clamp(x, -lim, lim);
                // Dual rings: the main ball lands in the left one; place it before the spacing check.
                if (c.type == NoteType.Dual) x = Mathf.Clamp(x, -W + 1.6f, W - 1.6f) - 1.6f;
                // Never crowd two rings: keep at least MinRingGap between consecutive centres.
                float dx = x - prevX, dz = z - prevZ;
                rise = Mathf.Max(rise, Mathf.Sqrt(Mathf.Max(0f, MinRingGap * MinRingGap - dx * dx - dz * dz)));
                y += rise;

                float scale = c.kind == NoteKind.Downbeat ? 1.15f : c.kind == NoteKind.Offbeat ? 0.8f : 1f;
                if (c.impact) scale = 1.35f;
                if (c.type == NoteType.Dual)
                {
                    float cx = x + 1.6f;
                    var left = NewNote(c, new Vector3(cx - 1.6f, y, z), scale);
                    var right = NewNote(c, new Vector3(cx + 1.6f, y, z), scale);
                    left.side = TapSide.Left;
                    right.side = TapSide.Right;
                    left.partner = right;
                    right.partner = left;
                    left.moving = right.moving = false;
                    Notes.Add(left);
                    Notes.Add(right);
                    mainNotes.Add(left);
                    mainLanding.Add(left.position);
                    mainLeave.Add(left.position);
                    x = cx - 1.6f;
                }
                else
                {
                    var n = NewNote(c, new Vector3(x, y, z), scale);
                    if (n.moving)
                    {
                        n.moveDir = (movingCount++ & 1) == 0 ? 1f : -1f;
                        n.movePeriod = spb * 2.0;
                    }

                    if (c.type == NoteType.Hold)
                    {
                        float holdBeats = (float)((c.endTime - c.time) / spb);
                        float railDir = x > 0f ? -1f : 1f;
                        float rdx = railDir * Mathf.Min(1.1f * holdBeats, W - Mathf.Abs(x) + W * 0.6f);
                        n.railEnd = new Vector3(Mathf.Clamp(x + rdx, -lim, lim), y + 0.9f * holdBeats, z);
                        x = n.railEnd.x;
                        y = n.railEnd.y;
                        prevTime = c.endTime;
                        Notes.Add(n);
                        mainNotes.Add(n);
                        mainLanding.Add(n.position);
                        mainLeave.Add(n.railEnd);
                        continue;
                    }

                    Notes.Add(n);
                    mainNotes.Add(n);
                    mainLanding.Add(n.position);
                    mainLeave.Add(n.position);
                }
                prevTime = c.time;
            }

            TimeBossRings(bossRings, spb);

            // ---- main path
            var path = new BallPath { ball = 0, tint = song.accent };
            Vector3 from = PadPosition;
            double tFrom = PadLeaveTime;
            for (int i = 0; i < mainNotes.Count; i++)
            {
                PlayNote n = mainNotes[i];
                bool dashIn = i > 0 && mainNotes[i - 1].type == NoteType.Swipe;
                path.Add(tFrom, n.time, from, mainLanding[i], dashIn ? BallPath.SegKind.Dash : BallPath.SegKind.Arc);
                if (n.type == NoteType.Hold)
                {
                    path.Add(n.time, n.endTime, n.position, n.railEnd, BallPath.SegKind.Rail);
                    tFrom = n.endTime;
                }
                else
                {
                    tFrom = n.time;
                }
                from = mainLeave[i];
            }
            ExitTime = tFrom + Mathf.Max(1.2f, spb * 2f);
            path.Add(tFrom, ExitTime, from, from + new Vector3(-Mathf.Sign(from.x + 0.001f) * 1.5f, 7f, 0f), BallPath.SegKind.Arc);
            MainPath = path;
            Paths.Add(path);

            // ---- twin balls for dual rings: the ball leaves the previous ring as one (so it still shoots out
            // along that ring's axis), splits in mid-air, the twin lands in the right ring, then rejoins.
            for (int i = 0; i < mainNotes.Count; i++)
            {
                PlayNote n = mainNotes[i];
                if (n.type != NoteType.Dual || n.side != TapSide.Left) continue;
                PlayNote right = n.partner;
                Vector3 a = i > 0 ? mainLeave[i - 1] : PadPosition;
                double ta = i > 0 ? (mainNotes[i - 1].type == NoteType.Hold ? mainNotes[i - 1].endTime : mainNotes[i - 1].time) : PadLeaveTime;
                var twin = new BallPath { ball = -1, tint = new Color(2.4f, 0.4f, 2.0f, 1f) };
                double split = ta + (right.time - ta) * 0.3;
                twin.Add(split, right.time, path.Evaluate(split, out _), right.position, BallPath.SegKind.Arc);
                if (i + 1 < mainNotes.Count) twin.Add(right.time, mainNotes[i + 1].time, right.position, mainLanding[i + 1], BallPath.SegKind.Arc);
                Paths.Add(twin);
            }

            // ---- companion balls: their rings sit beside the main ball's position at that moment.
            var byBall = new Dictionary<int, List<ChartNote>>();
            foreach (var c in src)
            {
                if (c.ball <= 0) continue;
                if (!byBall.TryGetValue(c.ball, out var list)) byBall[c.ball] = list = new List<ChartNote>();
                list.Add(c);
            }

            foreach (var kv in byBall)
            {
                int ballId = kv.Key;
                float side = ballId == 2 ? 1f : -1f;
                Color tint = CompanionTints[(ballId - 1) % CompanionTints.Length];
                BallPath cp = null;
                PlayNote prev = null;
                foreach (var c in kv.Value)
                {
                    Vector3 m = path.Evaluate(c.time, out _);
                    // Balls 1 and 2: the outer lanes; ball 3 (Spirit Balls on Hard): its own far-left lane.
                    float cx = ballId == 3 ? -(W + 1.8f) : side * (W + 0.4f);
                    var n = NewNote(c, new Vector3(cx, m.y + 0.6f, 0f), 0.85f);
                    n.tint = tint;
                    Notes.Add(n);

                    bool chain = prev != null && c.time - prev.time <= spb * 8.5;
                    if (!chain)
                    {
                        if (cp != null) AddExit(cp, prev, spb, W);
                        cp = new BallPath { ball = ballId, tint = tint };
                        Paths.Add(cp);
                        Vector3 entry = new Vector3(Mathf.Sign(cx) * (W + 7f), n.position.y - 2.5f, 0f);
                        cp.Add(c.time - spb * 2.0, c.time, entry, n.position, BallPath.SegKind.Arc);
                    }
                    else
                    {
                        cp.Add(prev.time, c.time, prev.position, n.position, BallPath.SegKind.Arc);
                    }
                    prev = n;
                }
                if (cp != null) AddExit(cp, prev, spb, W);
            }

            // ---- ring facing: every ring is a launcher. Its axis is exactly the ball's launch direction,
            // so the ball always shoots out orthogonal to the ring's plane (in 3D as well).
            foreach (PlayNote n in Notes) n.facing = FacingFor(LaunchDirection(n));
            TwistDesigner.Decorate(song, chart, Notes, W);

            Notes.Sort((a, b) => a.time != b.time ? a.time.CompareTo(b.time) : ((int)a.side).CompareTo((int)b.side));
            int holds = 0;
            for (int i = 0; i < Notes.Count; i++)
            {
                Notes[i].index = i;
                if (Notes[i].type == NoteType.Hold) holds++;
            }
            JudgementCount = Notes.Count + holds;
        }

        /// <summary>
        /// One boss ring: which attack it belongs to (by its 4-bar block of the arena) and its landing spot.
        /// Every spot keeps at least a ring gap from the previous one (<paramref name="prev"/>).
        /// </summary>
        private PlayNote PlaceBossRing(ChartNote c, float gap, float spb, double arenaStart, Vector3 prev)
        {
            int block = Mathf.Max(0, Mathf.FloorToInt((float)((c.time - arenaStart) / (spb * 16.0)) + 1e-3f));
            BossMove move = BossCycle[block % BossCycle.Length];
            Vector3 C = BossCenter;
            Vector3 rel = prev - C;
            if (block != _bossBlock)
            {
                _bossBlock = block;
                _bossInBlock = 0;
                BossPhases.Add(new KeyValuePair<double, BossMove>(arenaStart + block * 16.0 * spb, move));
                // A sweep starts on the row nearest the ball and runs away from its side.
                _sweepDir = prev.x > 0f ? -1f : 1f;
                _sweepRow = rel.y > 0f ? 1f : -1f;
            }
            int k = _bossInBlock++;
            float prevAngle = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
            float step = Mathf.Clamp(gap / spb * 40f, 40f, 70f);
            Vector3 at = C;
            float angle = 0f, radius = BossRadius, side = 1f, sweepFrom = 0f;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                switch (move)
                {
                    case BossMove.Orbit:
                        // Anticlockwise round the boss.
                        angle = prevAngle + step + attempt * 8f;
                        at = C + Dir(angle) * radius;
                        break;
                    case BossMove.Spiral:
                        // Clockwise, on a radius that breathes in and out.
                        radius = 2.85f + 0.4f * Mathf.Sin((k + attempt) * 1.1f);
                        angle = prevAngle - step - attempt * 8f;
                        at = C + Dir(angle) * radius;
                        break;
                    case BossMove.Barrage:
                    {
                        // Left and right of the boss in turn (a quick double stays on its side), wandering up and down.
                        bool quick = gap < spb * 0.75f && Mathf.Abs(prev.x) > 0.3f;
                        side = quick ? Mathf.Sign(prev.x) : (Mathf.Abs(prev.x) > 0.3f ? -Mathf.Sign(prev.x) : ((k & 1) == 0 ? 1f : -1f));
                        float wy = quick ? rel.y + (rel.y > 0f ? -2f : 2f) : 2.3f * Mathf.Sin((k + attempt) * 0.9f);
                        at = C + new Vector3(side * 2.6f, Mathf.Clamp(wy + attempt * 0.4f, -2.6f, 2.6f), 0f);
                        break;
                    }
                    default:
                    {
                        // Rows below and above the boss, four spots each, crossing back the other way on the next row.
                        int n = k + attempt;
                        int pass = n / 4, i = n % 4;
                        float dir = (pass & 1) == 0 ? _sweepDir : -_sweepDir;
                        float row = (pass & 1) == 0 ? _sweepRow : -_sweepRow;
                        int col = dir > 0f ? i : 3 - i;
                        at = C + new Vector3(-2.7f + 1.8f * col, row * 3.1f, 0f);
                        sweepFrom = dir; // it enters from the end of the row it is heading for
                        if (attempt > 0 && (prev - at).magnitude >= MinRingGap + 0.05f) _bossInBlock += attempt;
                        break;
                    }
                }
                if ((prev - at).magnitude >= MinRingGap + 0.05f) break;
            }

            var bn = NewNote(c, at, c.impact ? 1.35f : c.kind == NoteKind.Offbeat ? 0.85f : 1.05f);
            bn.moving = false;
            bn.fx = NoteFx.Boss;
            bn.bossMove = move;
            bn.orbitCenter = C;
            bn.orbitRadius = radius;
            bn.orbitAngle = angle;
            switch (move)
            {
                case BossMove.Orbit:
                    bn.orbitSpeed = BossOrbitPerBeat / spb;
                    break;
                case BossMove.Spiral:
                    bn.orbitSpeed = -BossOrbitPerBeat / spb;
                    bn.spiralGrow = 0.6f / spb;
                    break;
                case BossMove.Barrage:
                    bn.moveTime = spb;
                    bn.moveCurve = side;
                    break;
                default:
                    bn.moveTime = spb * 1.5;
                    bn.moveCurve = (k & 1) == 0 ? 1f : -1f;
                    bn.fxStart = new Vector3(sweepFrom * 7.5f, at.y, 0f);
                    break;
            }
            return bn;
        }

        private static Vector3 Dir(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }

        /// <summary>
        /// When each boss ring appears (<see cref="PlayNote.fxLead"/>): as early as possible (so the attack has
        /// time to play out) without ever coming within a ring of another ring on screen. Fired and swept rings
        /// launch on the half-beat grid, so the attacks themselves keep time.
        /// </summary>
        private static void TimeBossRings(List<PlayNote> rings, float spb)
        {
            const float Clearance = 1.75f;
            var candidates = new List<double>(80);
            for (int j = 0; j < rings.Count; j++)
            {
                PlayNote r = rings[j];
                candidates.Clear();
                if (r.bossMove == BossMove.Orbit || r.bossMove == BossMove.Spiral)
                {
                    double min = System.Math.Max(spb * (r.bossMove == BossMove.Spiral ? 1.0 : 0.75), 0.3);
                    double max = r.bossMove == BossMove.Spiral ? 3.0 : 3.4;
                    for (double l = max; l > min; l -= 0.05) candidates.Add(l);
                    candidates.Add(min);
                }
                else
                {
                    double half = spb * 0.5;
                    int nMax = (int)System.Math.Floor(3.0 / half);
                    int nMin = (int)System.Math.Ceiling((r.moveTime + half) / half);
                    for (int n = System.Math.Max(nMax, nMin); n >= nMin; n--) candidates.Add(n * half);
                }

                // The earliest clear lead wins; if none is clear, the one that keeps the rings furthest apart.
                double best = candidates[candidates.Count - 1];
                float bestGap = -1f;
                foreach (double lead in candidates)
                {
                    r.fxLead = lead;
                    float gap = ClosestApproach(rings, j, Clearance);
                    if (gap >= Clearance)
                    {
                        best = lead;
                        break;
                    }
                    if (gap > bestGap)
                    {
                        bestGap = gap;
                        best = lead;
                    }
                }
                r.fxLead = best;
            }
        }

        /// <summary>Closest ring <paramref name="j"/> comes to an earlier boss ring while both show (stops early below <paramref name="enough"/>).</summary>
        private static float ClosestApproach(List<PlayNote> rings, int j, float enough)
        {
            PlayNote r = rings[j];
            float closest = float.MaxValue;
            for (double t = r.time - r.fxLead; t < r.time; t += 0.03)
            {
                if (r.BossVisibility(t) < 0.3f) continue;
                Vector3 p = r.PositionAt(t);
                for (int i = j - 1; i >= 0; i--)
                {
                    PlayNote o = rings[i];
                    if (t >= o.time) break; // earlier rings are already hit (rings are in time order)
                    if (t < o.time - o.fxLead || o.BossVisibility(t) < 0.3f) continue;
                    closest = Mathf.Min(closest, (o.PositionAt(t) - p).magnitude);
                }
                if (closest < enough * 0.5f) return closest;
            }
            return closest;
        }

        /// <summary>Depth of the zigzag toward / away from the camera while the camera is angled or in the tunnel.</summary>
        private const float PathDepth = 1.7f;
        /// <summary>Smallest distance between consecutive ring centres (rings are about 1.6 wide).</summary>
        private const float MinRingGap = 1.9f;

        private static float DepthFor(Chart chart, double t)
        {
            CameraMode mode = CameraMode.Side;
            foreach (CameraCue cue in chart.cameraCues)
            {
                if (cue.time > t) break;
                mode = cue.mode;
            }
            return mode == CameraMode.Side ? 0f : PathDepth;
        }

        /// <summary>Exact direction the note's ball leaves the ring in (start of the path segment from it).</summary>
        public Vector3 LaunchDirection(PlayNote n)
        {
            foreach (BallPath p in Paths)
            {
                foreach (BallPath.Seg s in p.segs)
                {
                    if (System.Math.Abs(s.t0 - n.time) > 1e-4 || (s.p0 - n.position).sqrMagnitude > 1e-3) continue;
                    p.Evaluate(s.t0 + 1e-5, out Vector3 v);
                    if (v.sqrMagnitude > 1e-6f) return v.normalized;
                }
            }
            return Vector3.up;
        }

        /// <summary>Ring rotation whose axis (the ring's local Z, its plane's normal) is <paramref name="dir"/>.</summary>
        private static Quaternion FacingFor(Vector3 dir)
        {
            // Keep the ring's local Y toward the camera where possible, so its tilt reads cleanly.
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.back)) > 0.95f ? Vector3.up : Vector3.back;
            return Quaternion.LookRotation(dir, up);
        }

        /// <summary>Half width of the main ball's lane (fraction of the field) while companions are around.</summary>
        private const float CentreLane = 0.35f;

        private static double spbD(SongData song) => song.SecondsPerBeat;

        private static void AddExit(BallPath p, PlayNote last, float spb, float w)
        {
            Vector3 exit = new Vector3(Mathf.Sign(last.position.x + 0.001f) * (w + 7f), last.position.y + 4f, 0f);
            p.Add(last.time, last.time + spb * 2.0, last.position, exit, BallPath.SegKind.Arc);
        }

        private static PlayNote NewNote(ChartNote c, Vector3 pos, float scale)
        {
            return new PlayNote
            {
                time = c.time,
                kind = c.kind,
                type = c.type,
                swipe = c.swipe,
                ball = c.ball,
                moving = c.moving,
                impact = c.impact,
                golden = c.golden,
                endTime = c.type == NoteType.Hold ? c.endTime : c.time,
                position = pos,
                railEnd = pos,
                scale = scale,
                side = TapSide.Any,
                tint = Color.clear
            };
        }

        /// <summary>Main ball position (and velocity) at heard song time t; hops on the pad before the start.</summary>
        public Vector3 BallAt(double t, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (MainPath == null) return PadPosition;
            if (t < PadLeaveTime)
            {
                int k = _song.BeatIndexAt(t);
                double b0 = _song.BeatTime(k);
                double b1 = _song.BeatTime(k + 1);
                float s = (float)((t - b0) / System.Math.Max(0.05, b1 - b0));
                const float hop = 0.45f;
                float dur = (float)(b1 - b0);
                velocity = new Vector3(0f, 4f * hop * (1f - 2f * s) / Mathf.Max(0.05f, dur), 0f);
                return PadPosition + new Vector3(0f, 4f * hop * s * (1f - s), 0f);
            }
            return MainPath.Evaluate(t, out velocity);
        }

        /// <summary>Index of the first note with time &gt; t (Notes.Count if none).</summary>
        public int FindNextNote(double t)
        {
            int lo = 0, hi = Notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Notes[mid].time > t) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        public static Vector3 Flight(Vector3 a, Vector3 b, double ta, double tb, double t, out Vector3 velocity)
        {
            float g = Mathf.Max(0.05f, (float)(tb - ta));
            float s = Mathf.Clamp01((float)((t - ta) / g));
            float dy = b.y - a.y;
            // Apex sits above the target ring, so the ball always comes down into it on the beat.
            float h = 0.5f * Mathf.Max(0f, dy) + Mathf.Clamp(0.15f + 0.6f * g, 0.25f, 1.4f);
            Vector3 p = Vector3.LerpUnclamped(a, b, s);
            p.y = a.y + dy * s + 4f * h * s * (1f - s);
            velocity = new Vector3((b.x - a.x) / g, (dy + 4f * h * (1f - 2f * s)) / g, (b.z - a.z) / g);
            return p;
        }
    }
}
