using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DropBeat.EditorTools
{
    /// <summary>
    /// DropBeat > Apply Song Themes: copies each song's palette, hit instrument and lock state from
    /// Tools/themes.json onto its SongData asset and orders the song library the same way.
    /// Run it after DropBeat > Analyze All Songs (which creates the assets for new audio files).
    /// </summary>
    public static class SongThemes
    {
        [Serializable]
        private sealed class ThemeFile
        {
            public Entry[] songs;
        }

        [Serializable]
        private sealed class Entry
        {
            public string name;
            public int order;
            public string hit;
            public string scene;
            public bool locked;
            public float gainDb;
            public string hitKey;
            public float[] accent, accent2, skyTop, skyBottom, sunTop, sunBottom, haze;
        }

        [MenuItem("DropBeat/Apply Song Themes", priority = 2)]
        public static void Apply()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tools", "themes.json");
            if (!File.Exists(path))
            {
                Debug.LogError("[DropBeat] Missing " + path);
                return;
            }
            var file = JsonUtility.FromJson<ThemeFile>(File.ReadAllText(path));
            var library = Resources.Load<SongLibrary>("SongLibrary");
            if (file?.songs == null || library == null)
            {
                Debug.LogError("[DropBeat] Could not read themes or the song library.");
                return;
            }

            int applied = 0;
            foreach (Entry e in file.songs)
            {
                SongData song = library.songs.Find(s => s != null && s.name == e.name);
                if (song == null)
                {
                    Debug.LogWarning("[DropBeat] Theme for '" + e.name + "' but no such song (run Analyze All Songs first).");
                    continue;
                }
                Undo.RecordObject(song, "Apply song theme");
                if (Enum.TryParse(e.hit, out HitStyle style)) song.hitStyle = style;
                if (!string.IsNullOrEmpty(e.scene) && Enum.TryParse(e.scene, out BackdropScene scene)) song.scene = scene;
                song.locked = e.locked;
                song.musicGainDb = Mathf.Min(0f, e.gainDb);
                song.hitKeyRoot = -1;
                if (!string.IsNullOrEmpty(e.hitKey) && TryParseKey(e.hitKey, out int keyRoot, out bool keyMinor))
                {
                    song.hitKeyRoot = keyRoot;
                    song.hitKeyMinor = keyMinor;
                }
                Set(ref song.accent, e.accent);
                Set(ref song.accent2, e.accent2);
                Set(ref song.skyTop, e.skyTop);
                Set(ref song.skyBottom, e.skyBottom);
                Set(ref song.sunTop, e.sunTop);
                Set(ref song.sunBottom, e.sunBottom);
                Set(ref song.haze, e.haze);
                EditorUtility.SetDirty(song);
                applied++;
            }

            Undo.RecordObject(library, "Order songs");
            library.songs.Sort((a, b) => OrderOf(file, a).CompareTo(OrderOf(file, b)));
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log("[DropBeat] Applied " + applied + " song theme(s); library order: " + string.Join(", ", library.songs.ConvertAll(s => s != null ? s.title : "-")));
        }

        private static int OrderOf(ThemeFile file, SongData song)
        {
            if (song == null) return int.MaxValue;
            foreach (Entry e in file.songs) if (e.name == song.name) return e.order;
            return 1000;
        }

        /// <summary>Parses "E minor", "A# major" or "Bb minor" into a root (0 = C) and mode.</summary>
        private static bool TryParseKey(string text, out int root, out bool minor)
        {
            root = 0;
            minor = true;
            string[] parts = text.Trim().Split(' ');
            if (parts.Length < 1 || parts[0].Length < 1) return false;
            int[] naturals = { 9, 11, 0, 2, 4, 5, 7 }; // A B C D E F G
            char letter = char.ToUpperInvariant(parts[0][0]);
            if (letter < 'A' || letter > 'G') return false;
            root = naturals[letter - 'A'];
            if (parts[0].Length > 1) root += parts[0][1] == '#' ? 1 : parts[0][1] == 'b' ? -1 : 0;
            root = (root % 12 + 12) % 12;
            minor = parts.Length < 2 || parts[1].ToLowerInvariant().StartsWith("min");
            return true;
        }

        private static void Set(ref Color c, float[] v)
        {
            if (v == null || v.Length < 3) return;
            c = new Color(v[0], v[1], v[2], 1f);
        }
    }
}
