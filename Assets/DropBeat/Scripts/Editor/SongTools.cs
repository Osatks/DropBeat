using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DropBeat.EditorTools
{
    /// <summary>Song import, beat analysis and timing verification tools.</summary>
    public static class SongTools
    {
        public const string AudioFolder = "Assets/DropBeat/Audio";
        public const string SongFolder = "Assets/DropBeat/Songs";
        public const string LibraryPath = "Assets/DropBeat/Resources/SongLibrary.asset";

        private static readonly Color[] Accents =
        {
            new Color(0f, 2.2f, 2.6f, 1f),
            new Color(2.6f, 0.35f, 1.9f, 1f),
            new Color(2.6f, 1.4f, 0.2f, 1f),
            new Color(0.5f, 2.6f, 0.6f, 1f),
        };

        [MenuItem("DropBeat/Analyze All Songs", priority = 1)]
        public static void AnalyzeAllSongs()
        {
            EnsureFolder(SongFolder);
            EnsureFolder("Assets/DropBeat/Resources");

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
            var songs = new List<SongData>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                EnsureImportSettings(path);
                string name = Path.GetFileNameWithoutExtension(path);
                string songPath = SongFolder + "/" + name + ".asset";
                var song = AssetDatabase.LoadAssetAtPath<SongData>(songPath);
                if (song == null)
                {
                    song = ScriptableObject.CreateInstance<SongData>();
                    song.clip = clip;
                    song.title = name;
                    song.artist = string.Empty;
                    song.accent = Accents[songs.Count % Accents.Length];
                    AssetDatabase.CreateAsset(song, songPath);
                }

                song.clip = clip;
                EditorUtility.DisplayProgressBar("DropBeat", "Analysing " + name, (i + 0.5f) / guids.Length);
                try
                {
                    AnalyzeSong(song);
                }
                catch (Exception e)
                {
                    // One bad clip (too short, unreadable) must not stop the others or the library update.
                    Debug.LogError("[DropBeat] Could not analyse " + name + ": " + e.Message);
                    continue;
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
                songs.Add(song);
            }

            var library = AssetDatabase.LoadAssetAtPath<SongLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SongLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.songs.RemoveAll(s => s == null);
            foreach (var s in songs)
            {
                if (!library.songs.Contains(s)) library.songs.Add(s);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log("[DropBeat] Analysed " + songs.Count + " song(s). Library: " + LibraryPath);
        }

        public static void AnalyzeSong(SongData song)
        {
            if (song == null || song.clip == null) throw new ArgumentException("Song has no clip.");
            float[] mono = ReadMono(song.clip, out int sampleRate);
            var settings = BeatAnalyzer.Settings.Default;
            settings.bpmOverride = song.bpmOverride;
            settings.flipHalfBeat = song.flipHalfBeat;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            BeatAnalysis a = BeatAnalyzer.Analyze(mono, sampleRate, settings);
            sw.Stop();
            song.Apply(a, sampleRate);
            if (song.previewTime <= 0f || song.previewTime > a.duration - 10f) song.previewTime = Mathf.Min(30f, a.duration * 0.3f);
            EditorUtility.SetDirty(song);

            var sb = new StringBuilder();
            sb.Append("[DropBeat] ").Append(song.title).Append(": ").Append(a.bpm.ToString("F3", CultureInfo.InvariantCulture))
              .Append(" BPM, first beat ").Append(a.beatTimes.Length > 0 ? a.beatTimes[0].ToString("F4", CultureInfo.InvariantCulture) : "-")
              .Append("s, beats ").Append(a.beatTimes.Length)
              .Append(", confidence ").Append(a.phaseConfidence.ToString("F2", CultureInfo.InvariantCulture))
              .Append(", drift ").Append(a.driftMs.ToString("F1", CultureInfo.InvariantCulture)).Append("ms")
              .Append(", notes E/N/H ")
              .Append(ChartGenerator.Generate(song, Difficulty.Easy).Count).Append('/')
              .Append(ChartGenerator.Generate(song, Difficulty.Normal).Count).Append('/')
              .Append(ChartGenerator.Generate(song, Difficulty.Hard).Count)
              .Append(" (").Append(sw.ElapsedMilliseconds).Append(" ms)\n").Append(a.log);
            if (a.phaseConfidence < 1.15f)
            {
                sb.Append("\nLow beat/off-beat confidence: export a click preview and check the ticks sit on the kick.");
            }
            Debug.Log(sb.ToString());
        }

        public static float[] ReadMono(AudioClip clip, out int sampleRate)
        {
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            int channels = clip.channels;
            int frames = clip.samples;
            float[] data = new float[frames * channels];
            if (!clip.GetData(data, 0))
            {
                throw new InvalidOperationException("Could not read samples from " + clip.name + ". Set Load Type to Decompress On Load.");
            }

            float[] mono = new float[frames];
            float inv = 1f / channels;
            for (int i = 0; i < frames; i++)
            {
                float s = 0f;
                for (int c = 0; c < channels; c++) s += data[i * channels + c];
                mono[i] = s * inv;
            }

            sampleRate = clip.frequency;
            return mono;
        }

        private static void EnsureImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var s = importer.defaultSampleSettings;
            bool dirty = false;
            // Decompressed PCM in memory: sample-exact GetData for analysis and no streaming jitter at runtime.
            if (s.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                dirty = true;
            }
            if (importer.loadInBackground)
            {
                importer.loadInBackground = false;
                dirty = true;
            }
            if (dirty)
            {
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ validation

        [MenuItem("DropBeat/Validate Beat Analyzer", priority = 20)]
        public static void ValidateAnalyzer()
        {
            Debug.Log(RunValidation(out _));
        }

        /// <summary>
        /// Synthetic songs with known beat times: kicks on the beat, hats and (sometimes) bass on the
        /// off-beat, small human jitter. Checks tempo, beat-vs-off-beat choice and absolute timing.
        /// </summary>
        public static string RunValidation(out bool allPassed)
        {
            var sb = new StringBuilder("[DropBeat] Beat analyzer validation\n");
            allPassed = true;
            var cases = new (float bpm, float phase, bool offBass, int sr)[]
            {
                (128f, 0.1234f, false, 48000),
                (128f, 0.3111f, true, 48000),
                (172f, 0.0500f, true, 44100),
                (96f, 0.2000f, false, 48000),
                (140f, 0.0710f, true, 44100),
                (110f, 0.4000f, false, 48000),
            };

            var errors = new List<float>();
            foreach (var c in cases)
            {
                float[] x = SynthesizeSong(c.bpm, c.phase, c.offBass, c.sr, 45f, new System.Random(7));
                BeatAnalysis a = BeatAnalyzer.Analyze(x, c.sr, BeatAnalyzer.Settings.Default);
                float period = 60f / c.bpm;
                float err = BeatAnalyzer.WrapSigned(a.beatTimes[0] - c.phase, period) * 1000f;
                float bpmErr = a.bpm - c.bpm;
                // Last-beat error captures tempo error accumulated over the whole track.
                int last = a.beatTimes.Length - 1;
                float lastTrue = c.phase + Mathf.Round((a.beatTimes[last] - c.phase) / period) * period;
                float lastErr = (a.beatTimes[last] - lastTrue) * 1000f;
                bool ok = Mathf.Abs(err) < 5f && Mathf.Abs(lastErr) < 6f && Mathf.Abs(bpmErr) < 0.05f;
                allPassed &= ok;
                errors.Add(err);
                sb.Append(ok ? "  PASS " : "  FAIL ")
                  .Append(c.bpm.ToString("F0")).Append(" BPM off-bass=").Append(c.offBass ? "yes" : "no ")
                  .Append(" sr=").Append(c.sr)
                  .Append(" -> bpm ").Append(a.bpm.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(", first-beat err ").Append(err.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)).Append("ms")
                  .Append(", last-beat err ").Append(lastErr.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)).Append("ms")
                  .Append(", conf ").Append(a.phaseConfidence.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }

            float mean = 0f;
            foreach (float e in errors) mean += e;
            mean /= errors.Count;
            sb.Append(allPassed ? "ALL PASSED" : "SOME FAILED").Append(", mean first-beat error ")
              .Append(mean.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)).Append(" ms");
            return sb.ToString();
        }

        private static float[] SynthesizeSong(float bpm, float phase, bool offBass, int sr, float seconds, System.Random rng)
        {
            float period = 60f / bpm;
            int n = (int)(sr * seconds);
            float[] x = new float[n];

            float[] kick = Envelope(sr, 0.3f, (t, ph) => Math.Sin(ph) * Math.Exp(-t * 12), t => 50 + 160 * Math.Exp(-t * 45));
            float[] snare = Noise(sr, 0.15f, 25, rng, 0.35f);
            float[] hat = Noise(sr, 0.05f, 70, rng, 0.25f);
            float[] bass = Envelope(sr, 0.22f, (t, ph) => Math.Sin(ph) * Math.Min(1, t * 60) * Math.Exp(-t * 8) * 0.45, t => 55);

            for (int k = 0; ; k++)
            {
                double beat = phase + k * period + (rng.NextDouble() - 0.5) * 0.003;
                if (beat * sr >= n) break;
                Add(x, kick, beat, sr, 0.9f);
                if (k % 2 == 1) Add(x, snare, beat, sr, 1f);
                double off = beat + period * 0.5;
                Add(x, hat, off, sr, 1f);
                if (offBass) Add(x, bass, off, sr, 1f);
            }

            // Sustained pad so the onset detector also has non-percussive content.
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                x[i] += (float)(0.08 * Math.Sin(2 * Math.PI * 220 * t) + 0.05 * Math.Sin(2 * Math.PI * 330.5 * t));
                x[i] += (float)((rng.NextDouble() - 0.5) * 0.01);
            }
            return x;
        }

        private static float[] Envelope(int sr, float seconds, Func<double, double, double> shape, Func<double, double> freq)
        {
            float[] y = new float[(int)(sr * seconds)];
            double ph = 0;
            for (int i = 0; i < y.Length; i++)
            {
                double t = i / (double)sr;
                ph += 2 * Math.PI * freq(t) / sr;
                y[i] = (float)shape(t, ph);
            }
            return y;
        }

        private static float[] Noise(int sr, float seconds, float decay, System.Random rng, float gain)
        {
            float[] y = new float[(int)(sr * seconds)];
            for (int i = 0; i < y.Length; i++)
            {
                double t = i / (double)sr;
                y[i] = (float)((rng.NextDouble() * 2 - 1) * Math.Exp(-t * decay) * gain);
            }
            return y;
        }

        private static void Add(float[] x, float[] s, double t, int sr, float gain)
        {
            int start = (int)Math.Round(t * sr);
            for (int i = 0; i < s.Length && start + i < x.Length; i++)
            {
                if (start + i >= 0) x[start + i] += s[i] * gain;
            }
        }

        /// <summary>
        /// Independent check on real audio: for every strong beat, find the steepest rise of the
        /// short-time (3 ms) log energy within +-30 ms of the grid and report attack - grid.
        /// A median near 0 ms means the grid sits on the physical drum attacks.
        /// </summary>
        public static string MeasureAttackAlignment(SongData song)
        {
            float[] x = ReadMono(song.clip, out int sr);
            int hop = Math.Max(1, sr / 1000);
            int win = Math.Max(2, 3 * sr / 1000);
            int frames = (x.Length - win) / hop;
            double[] prefix = new double[x.Length + 1];
            for (int i = 0; i < x.Length; i++) prefix[i + 1] = prefix[i] + x[i] * (double)x[i];
            float[] le = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                int a = f * hop;
                le[f] = (float)(10.0 * Math.Log10((prefix[a + win] - prefix[a]) / win + 1e-10));
            }

            var diffs = new List<float>();
            for (int k = 0; k < song.beatTimes.Length; k++)
            {
                if (song.kickStrength[k] < 0.5f) continue;
                float t = song.beatTimes[k];
                // Frame of the beat in the real hop timebase (hop is sr/1000 rounded down, not exactly 1 ms).
                int c = (int)Math.Round((t * sr - win * 0.5) / hop);
                int radius = (int)Math.Round(0.030 * sr / hop);
                int bestF = -1;
                float best = 0f;
                for (int f = Math.Max(2, c - radius); f <= Math.Min(frames - 1, c + radius); f++)
                {
                    float d = le[f] - le[f - 2];
                    if (d > best)
                    {
                        best = d;
                        bestF = f;
                    }
                }
                if (bestF < 0 || best < 3f) continue;
                float attack = (bestF * hop + win * 0.5f) / sr;
                diffs.Add((attack - t) * 1000f);
            }

            diffs.Sort();
            if (diffs.Count == 0) return song.title + ": no strong attacks found";
            float median = diffs[diffs.Count / 2];
            float q1 = diffs[diffs.Count / 4];
            float q3 = diffs[diffs.Count * 3 / 4];
            int within = 0;
            foreach (float d in diffs) if (Math.Abs(d) <= 10f) within++;
            return song.title + ": attack - grid median " + median.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) +
                   " ms, IQR [" + q1.ToString("F1", CultureInfo.InvariantCulture) + ", " + q3.ToString("F1", CultureInfo.InvariantCulture) +
                   "] ms, " + (100f * within / diffs.Count).ToString("F0") + "% of " + diffs.Count + " strong beats within 10 ms";
        }

        /// <summary>
        /// Beat-synchronous average of the energy envelope (1 ms steps, -60..+100 ms around each grid beat).
        /// Unrelated sounds average out; the common drum attack remains. Reports where the averaged
        /// envelope rises fastest and where it crosses 50% of its rise, relative to the grid.
        /// </summary>
        public static string MeasureSyncAverage(float[] x, int sr, IList<float> grid, string label)
        {
            const int before = 60, after = 100;
            int win = Math.Max(2, 2 * sr / 1000);
            double[] prefix = new double[x.Length + 1];
            for (int i = 0; i < x.Length; i++) prefix[i + 1] = prefix[i] + x[i] * (double)x[i];
            double[] avg = new double[before + after + 1];
            int used = 0;
            foreach (float t in grid)
            {
                int c = (int)Math.Round(t * sr);
                if (c - before * sr / 1000 - win < 0 || c + after * sr / 1000 + win >= x.Length) continue;
                for (int j = -before; j <= after; j++)
                {
                    int a = c + j * sr / 1000 - win / 2;
                    avg[j + before] += (prefix[a + win] - prefix[a]) / win;
                }
                used++;
            }
            if (used == 0) return label + ": no beats";

            double[] le = new double[avg.Length];
            for (int i = 0; i < avg.Length; i++) le[i] = 10.0 * Math.Log10(avg[i] / used + 1e-12);
            int steep = 1;
            for (int i = 2; i < le.Length; i++) if (le[i] - le[i - 1] > le[steep] - le[steep - 1]) steep = i;
            double floor = le[0], peak = le[0];
            int peakI = 0;
            for (int i = 0; i < le.Length; i++) if (le[i] > peak) { peak = le[i]; peakI = i; }
            for (int i = 0; i < peakI; i++) floor = Math.Min(floor, le[i]);
            int half = peakI;
            for (int i = 0; i <= peakI; i++) if (le[i] >= floor + 0.5 * (peak - floor)) { half = i; break; }
            return label + ": sync-avg over " + used + " beats: steepest rise at " + (steep - before - 0.5).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) +
                   " ms, 50% rise at " + (half - before).ToString("+0;-0") + " ms, peak at " + (peakI - before).ToString("+0;-0") +
                   " ms (" + (peak - floor).ToString("F1", CultureInfo.InvariantCulture) + " dB)";
        }

        public static string MeasureSyncAverage(SongData song)
        {
            float[] x = ReadMono(song.clip, out int sr);
            var strong = new List<float>();
            for (int k = 0; k < song.beatTimes.Length; k++) if (song.kickStrength[k] >= 0.5f) strong.Add(song.beatTimes[k]);
            return MeasureSyncAverage(x, sr, strong, song.title);
        }

        public static string MeasureSyncAverageSynthetic()
        {
            var rng = new System.Random(3);
            float[] x = SynthesizeSong(128f, 0.1234f, true, 48000, 45f, rng);
            var grid = new List<float>();
            for (float t = 0.1234f; t < 44f; t += 60f / 128f) grid.Add(t);
            return MeasureSyncAverage(x, 48000, grid, "synthetic (true grid)");
        }

        // ------------------------------------------------------------------ click preview

        [MenuItem("DropBeat/Export Click Preview (Selected Song)", priority = 21)]
        public static void ExportClickPreviewMenu()
        {
            var song = Selection.activeObject as SongData;
            if (song == null)
            {
                EditorUtility.DisplayDialog("DropBeat", "Select a Song asset first (Assets/DropBeat/Songs).", "OK");
                return;
            }
            string path = ExportClickPreview(song, Difficulty.Normal);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>
        /// Writes the song mixed with a click at every chart note, so the beat placement can be checked by ear.
        /// </summary>
        public static string ExportClickPreview(SongData song, Difficulty difficulty)
        {
            float[] mono = ReadMono(song.clip, out int sr);
            var notes = ChartGenerator.Generate(song, difficulty);
            float[] click = new float[(int)(0.03f * sr)];
            for (int i = 0; i < click.Length; i++)
            {
                double t = i / (double)sr;
                click[i] = (float)(Math.Sin(2 * Math.PI * 2000 * t) * Math.Exp(-t * 180) * 0.6);
            }

            float[] mix = new float[mono.Length];
            for (int i = 0; i < mono.Length; i++) mix[i] = mono[i] * 0.7f;
            foreach (var note in notes) Add(mix, click, note.time, sr, note.kind == NoteKind.Downbeat ? 1f : 0.7f);

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Verification"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, song.title + "_" + difficulty + "_clicks.wav");
            WriteWav(file, mix, sr);
            Debug.Log("[DropBeat] Click preview written: " + file + " (" + notes.Count + " notes)");
            return file;
        }

        private static void WriteWav(string path, float[] samples, int sr)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(sr);
                w.Write(sr * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                for (int i = 0; i < samples.Length; i++)
                {
                    w.Write((short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f));
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    [CustomEditor(typeof(SongData))]
    public sealed class SongDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var song = (SongData)target;
            if (song.IsAnalyzed)
            {
                EditorGUILayout.HelpBox(
                    song.bpm.ToString("F3", CultureInfo.InvariantCulture) + " BPM  |  " + song.beatTimes.Length + " beats  |  confidence " +
                    song.phaseConfidence.ToString("F2", CultureInfo.InvariantCulture) + "  |  drift " +
                    song.driftMs.ToString("F1", CultureInfo.InvariantCulture) + " ms\nNotes  Easy " +
                    ChartGenerator.Generate(song, Difficulty.Easy).Count + "  Normal " +
                    ChartGenerator.Generate(song, Difficulty.Normal).Count + "  Hard " +
                    ChartGenerator.Generate(song, Difficulty.Hard).Count,
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Not analysed yet.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyse"))
                {
                    SongTools.AnalyzeSong(song);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Export Click Preview"))
                {
                    EditorUtility.RevealInFinder(SongTools.ExportClickPreview(song, Difficulty.Normal));
                }
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
