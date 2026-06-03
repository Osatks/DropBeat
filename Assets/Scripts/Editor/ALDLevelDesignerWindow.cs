using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BeatDrop.EditorTools
{
    internal enum ALDNoteFocusMode : byte
    {
        Balanced = 0,
        LowBeatHighPresence = 1,
        HighPresence = 2
    }

    public sealed class ALDLevelDesignerWindow : EditorWindow
    {
        [SerializeField] private AudioClip inputClip;
        [SerializeField] private string outputFolder = "Assets/BeatDrop/Data";
        [SerializeField] private string aldAssetName = "ALD_AutoLevel";
        [SerializeField] private bool createCompatibilityNoteChart = true;
        [SerializeField] private string noteChartAssetName = "AutoLevel_Chart";

        [Header("ALD v1 (Deterministic, One Lane)")]
        [SerializeField] private int seed = 0;
        [SerializeField] private bool useRandomness = false;
        [SerializeField] private int fftWindowSize = 1024;
        [SerializeField] private int hopSize = 512;
        [SerializeField] private float bpmMin = 80f;
        [SerializeField] private float bpmMax = 180f;
        [SerializeField] private float onsetSensitivity = 1.35f;
        [SerializeField] private float noteStrengthGate = 0.18f;
        [SerializeField] private float minNoteSpacingSeconds = 0.18f;
        [SerializeField] private int sectionBeats = 8;
        [SerializeField] private bool enableMultiSubdivision = true;
        [SerializeField] private bool allowTripletSubdivision = true;
        [SerializeField] private int maxSubBeatsPerBeat = 2;
        [SerializeField] private float subBeatStrengthBias = 1.07f;
        [SerializeField] private ALDNoteFocusMode noteFocusMode = ALDNoteFocusMode.Balanced;
        [SerializeField] private float lowBeatBias = 0.95f;
        [SerializeField] private float highPresenceBias = 0.85f;
        [SerializeField] private float lowBeatGate = 0.14f;
        [SerializeField] private bool useManualBeatSelection = true;
        [SerializeField] private float previewLeadSeconds = 0.12f;
        [SerializeField] private int maxBeatPreviewRows = 180;
        [SerializeField] private bool showRepeatedBeats = true;
        [SerializeField] private bool showOnlySpecialBeats = false;
        [SerializeField] private bool showOnlyUniqueBeats = false;
        [SerializeField] private bool showOnlyHighPresenceBeats = false;
        [SerializeField] private float highPresenceSelectionThreshold = 0.62f;
        [SerializeField] private float duplicateBeatToleranceSeconds = 0.055f;
        [SerializeField] private bool autoAlignBeatOffsetToPulseStart = true;
        [SerializeField] private bool snapNotesToPulseStart = true;
        [SerializeField] private float manualBeatPhaseShiftSeconds = 0f;
        [SerializeField] private float pulseStartSearchSeconds = 0.07f;
        [SerializeField] private float pulseStartThresholdRatio = 0.34f;

        private string _lastSummary = string.Empty;
        private Vector2 _scroll;
        private Vector2 _beatScroll;
        private int _nextHighPresencePreviewIndex;
        private readonly List<BeatCandidateSelection> _beatCandidates = new List<BeatCandidateSelection>(1024);

        [MenuItem("BeatDrop/ALD/Level Designer")]
        public static void Open()
        {
            GetWindow<ALDLevelDesignerWindow>("ALD Level Designer");
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("ALD - Algorithm Level Designer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generates a deterministic one-lane level from music.\n" +
                "Output includes:\n" +
                "- ALDLevelAsset (commands, notes, section design, judge profiles)\n" +
                "- Optional NoteChart asset for current runtime compatibility.",
                MessageType.Info);

            inputClip = (AudioClip)EditorGUILayout.ObjectField("Input Music", inputClip, typeof(AudioClip), false);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            aldAssetName = EditorGUILayout.TextField("ALD Asset Name", aldAssetName);
            createCompatibilityNoteChart = EditorGUILayout.Toggle("Create NoteChart", createCompatibilityNoteChart);
            if (createCompatibilityNoteChart)
            {
                noteChartAssetName = EditorGUILayout.TextField("NoteChart Name", noteChartAssetName);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Analysis Settings", EditorStyles.boldLabel);
            seed = EditorGUILayout.IntField("Seed", seed);
            useRandomness = EditorGUILayout.Toggle("Use Randomness", useRandomness);
            fftWindowSize = EditorGUILayout.IntPopup(
                "FFT Window",
                Mathf.Clamp(fftWindowSize, 256, 4096),
                new[] { "256", "512", "1024", "2048", "4096" },
                new[] { 256, 512, 1024, 2048, 4096 });
            hopSize = EditorGUILayout.IntPopup(
                "Hop Size",
                Mathf.Clamp(hopSize, 128, 2048),
                new[] { "128", "256", "512", "1024", "2048" },
                new[] { 128, 256, 512, 1024, 2048 });
            bpmMin = EditorGUILayout.FloatField("BPM Min", bpmMin);
            bpmMax = EditorGUILayout.FloatField("BPM Max", bpmMax);
            onsetSensitivity = EditorGUILayout.Slider("Onset Sensitivity", onsetSensitivity, 1.05f, 2f);
            noteStrengthGate = EditorGUILayout.Slider("Note Strength Gate", noteStrengthGate, 0.05f, 0.65f);
            minNoteSpacingSeconds = EditorGUILayout.Slider("Min Note Spacing (s)", minNoteSpacingSeconds, 0.05f, 0.5f);
            sectionBeats = EditorGUILayout.IntSlider("Section Beats", sectionBeats, 4, 16);
            enableMultiSubdivision = EditorGUILayout.Toggle("Multi Subdivision", enableMultiSubdivision);
            allowTripletSubdivision = EditorGUILayout.Toggle("Allow Triplets", allowTripletSubdivision);
            maxSubBeatsPerBeat = EditorGUILayout.IntSlider("Max Sub-Beats / Beat", maxSubBeatsPerBeat, 0, 4);
            subBeatStrengthBias = EditorGUILayout.Slider("Sub-Beat Strength Bias", subBeatStrengthBias, 1.0f, 1.45f);
            noteFocusMode = (ALDNoteFocusMode)EditorGUILayout.EnumPopup("Note Focus Mode", noteFocusMode);
            if (noteFocusMode == ALDNoteFocusMode.LowBeatHighPresence || noteFocusMode == ALDNoteFocusMode.HighPresence)
            {
                if (noteFocusMode == ALDNoteFocusMode.LowBeatHighPresence)
                {
                    lowBeatBias = EditorGUILayout.Slider("Low Beat Bias", lowBeatBias, 0.1f, 1.6f);
                    lowBeatGate = EditorGUILayout.Slider("Low Beat Gate", lowBeatGate, 0.01f, 0.5f);
                }
                highPresenceBias = EditorGUILayout.Slider("High Presence Bias", highPresenceBias, 0.1f, 1.6f);
                EditorGUILayout.HelpBox(
                    noteFocusMode == ALDNoteFocusMode.HighPresence
                        ? "High Presence mode favors beats with clear high/mid spectral presence so they are easier to hear and pick from the table."
                        : "LowBeat + HighPresence mode favors strong low beats that still carry high-frequency presence so notes are easier to hear over the background.",
                    MessageType.None);
            }
            useManualBeatSelection = EditorGUILayout.Toggle("Manual Beat Selection", useManualBeatSelection);
            previewLeadSeconds = EditorGUILayout.Slider("Preview Lead (s)", previewLeadSeconds, 0f, 0.4f);
            maxBeatPreviewRows = EditorGUILayout.IntSlider("Max Beat Rows", maxBeatPreviewRows, 32, 512);
            highPresenceSelectionThreshold = EditorGUILayout.Slider("High Presence Select", highPresenceSelectionThreshold, 0.2f, 0.95f);
            duplicateBeatToleranceSeconds = EditorGUILayout.Slider("Duplicate Beat Tolerance (s)", duplicateBeatToleranceSeconds, 0.02f, 0.12f);
            autoAlignBeatOffsetToPulseStart = EditorGUILayout.Toggle("Auto Align Pulse Start", autoAlignBeatOffsetToPulseStart);
            snapNotesToPulseStart = EditorGUILayout.Toggle("Snap Notes To Pulse Start", snapNotesToPulseStart);
            manualBeatPhaseShiftSeconds = EditorGUILayout.Slider("Manual Phase Shift (s)", manualBeatPhaseShiftSeconds, -0.25f, 0.25f);
            pulseStartSearchSeconds = EditorGUILayout.Slider("Pulse Start Search (s)", pulseStartSearchSeconds, 0.01f, 0.16f);
            pulseStartThresholdRatio = EditorGUILayout.Slider("Pulse Start Threshold", pulseStartThresholdRatio, 0.12f, 0.75f);
            EditorGUILayout.HelpBox(
                "Pulse-start alignment moves ALD timing toward the beginning of a transient instead of only the loudest peak. If the chart feels late, try a small negative Manual Phase Shift.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(inputClip == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Use High Presence Mode", GUILayout.Height(26f)))
                {
                    noteFocusMode = ALDNoteFocusMode.HighPresence;
                    AnalyzeBeatsForManualSelection();
                }
                if (GUILayout.Button("Analyze Beats For Selection", GUILayout.Height(26f)))
                {
                    AnalyzeBeatsForManualSelection();
                }
                EditorGUILayout.EndHorizontal();
            }

            DrawBeatSelectionUI();

            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(inputClip == null))
            {
                if (GUILayout.Button("Generate ALD Level", GUILayout.Height(32f)))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrEmpty(_lastSummary))
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_lastSummary, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawBeatSelectionUI()
        {
            if (!useManualBeatSelection)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Beat Selection", EditorStyles.boldLabel);
            if (_beatCandidates.Count == 0)
            {
                EditorGUILayout.HelpBox("Run 'Analyze Beats For Selection' to preview and choose beats.", MessageType.None);
                return;
            }

            RefreshHighPresenceTags();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                for (int i = 0; i < _beatCandidates.Count; i++)
                {
                    _beatCandidates[i] = _beatCandidates[i].WithSelected(true);
                }
            }
            if (GUILayout.Button("Select None"))
            {
                for (int i = 0; i < _beatCandidates.Count; i++)
                {
                    _beatCandidates[i] = _beatCandidates[i].WithSelected(false);
                }
            }
            if (GUILayout.Button("Select Special"))
            {
                for (int i = 0; i < _beatCandidates.Count; i++)
                {
                    _beatCandidates[i] = _beatCandidates[i].WithSelected(_beatCandidates[i].isSpecial);
                }
            }
            if (GUILayout.Button("Select Unique"))
            {
                for (int i = 0; i < _beatCandidates.Count; i++)
                {
                    _beatCandidates[i] = _beatCandidates[i].WithSelected(_beatCandidates[i].isUnique);
                }
            }
            if (GUILayout.Button("Select High Presence"))
            {
                for (int i = 0; i < _beatCandidates.Count; i++)
                {
                    _beatCandidates[i] = _beatCandidates[i].WithSelected(_beatCandidates[i].isHighPresence);
                }
            }
            if (GUILayout.Button("Play Next High Presence"))
            {
                PlayNextHighPresenceBeat();
            }
            if (GUILayout.Button("Stop Preview"))
            {
                StopPreviewClip();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showRepeatedBeats = EditorGUILayout.ToggleLeft("Show Repeated", showRepeatedBeats, GUILayout.Width(120f));
            showOnlySpecialBeats = EditorGUILayout.ToggleLeft("Only Special", showOnlySpecialBeats, GUILayout.Width(120f));
            showOnlyUniqueBeats = EditorGUILayout.ToggleLeft("Only Unique", showOnlyUniqueBeats, GUILayout.Width(120f));
            showOnlyHighPresenceBeats = EditorGUILayout.ToggleLeft("Only High Presence", showOnlyHighPresenceBeats, GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();

            int selectedCount = 0;
            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                if (_beatCandidates[i].selected)
                {
                    selectedCount++;
                }
            }

            EditorGUILayout.LabelField("Selected Beats: " + selectedCount + " / " + _beatCandidates.Count);

            int uniqueCount = 0;
            int repeatedCount = 0;
            int specialCount = 0;
            int highPresenceCount = 0;
            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                if (_beatCandidates[i].isUnique) uniqueCount++;
                if (_beatCandidates[i].isRepeated) repeatedCount++;
                if (_beatCandidates[i].isSpecial) specialCount++;
                if (_beatCandidates[i].isHighPresence) highPresenceCount++;
            }
            EditorGUILayout.LabelField("Unique: " + uniqueCount + "  Repeated: " + repeatedCount + "  Special: " + specialCount + "  High Presence: " + highPresenceCount);

            _beatScroll = EditorGUILayout.BeginScrollView(_beatScroll, GUILayout.Height(240f));
            int shownRows = 0;
            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                BeatCandidateSelection item = _beatCandidates[i];
                if (!showRepeatedBeats && item.isRepeated)
                {
                    continue;
                }
                if (showOnlySpecialBeats && !item.isSpecial)
                {
                    continue;
                }
                if (showOnlyUniqueBeats && !item.isUnique)
                {
                    continue;
                }
                if (showOnlyHighPresenceBeats && !item.isHighPresence)
                {
                    continue;
                }
                if (shownRows >= maxBeatPreviewRows)
                {
                    break;
                }

                EditorGUILayout.BeginHorizontal();
                bool selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(18f));
                string tags = BuildBeatTags(item);
                EditorGUILayout.LabelField(
                    "#" + i.ToString("000") +
                    "  t=" + item.time.ToString("F3") +
                    "  str=" + item.strength.ToString("F2") +
                    "  hp=" + item.highPresence.ToString("F2") +
                    "  nov=" + item.novelty.ToString("F2") +
                    "  " + item.subdivisionLabel + " " + tags,
                    GUILayout.Width(520f));

                if (GUILayout.Button("Play", GUILayout.Width(46f)))
                {
                    PlayPreviewAtTime(item.time);
                }
                EditorGUILayout.EndHorizontal();
                shownRows++;

                if (selected != item.selected)
                {
                    _beatCandidates[i] = item.WithSelected(selected);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void AnalyzeBeatsForManualSelection()
        {
            if (inputClip == null)
            {
                return;
            }

            ALDGenerationSettings settings = new ALDGenerationSettings
            {
                Seed = seed,
                UseRandomness = useRandomness,
                FftWindowSize = fftWindowSize,
                HopSize = hopSize,
                BpmMin = Mathf.Max(40f, bpmMin),
                BpmMax = Mathf.Max(bpmMin + 5f, bpmMax),
                OnsetSensitivity = Mathf.Clamp(onsetSensitivity, 1.01f, 2.5f),
                NoteStrengthGate = Mathf.Clamp01(noteStrengthGate),
                MinNoteSpacingSeconds = Mathf.Max(0.01f, minNoteSpacingSeconds),
                SectionBeats = Mathf.Clamp(sectionBeats, 4, 16),
                EnableMultiSubdivision = enableMultiSubdivision,
                AllowTripletSubdivision = allowTripletSubdivision,
                MaxSubBeatsPerBeat = Mathf.Clamp(maxSubBeatsPerBeat, 0, 4),
                SubBeatStrengthBias = Mathf.Clamp(subBeatStrengthBias, 1f, 1.6f),
                NoteFocusMode = noteFocusMode,
                LowBeatBias = Mathf.Clamp(lowBeatBias, 0.1f, 2f),
                HighPresenceBias = Mathf.Clamp(highPresenceBias, 0.1f, 2f),
                LowBeatGate = Mathf.Clamp(lowBeatGate, 0.01f, 0.75f),
                AutoAlignBeatOffsetToPulseStart = autoAlignBeatOffsetToPulseStart,
                SnapNotesToPulseStart = snapNotesToPulseStart,
                ManualBeatPhaseShiftSeconds = Mathf.Clamp(manualBeatPhaseShiftSeconds, -0.5f, 0.5f),
                PulseStartSearchSeconds = Mathf.Clamp(pulseStartSearchSeconds, 0.005f, 0.25f),
                PulseStartThresholdRatio = Mathf.Clamp(pulseStartThresholdRatio, 0.05f, 0.95f)
            };

            ALDGenerationResult result = ALDLevelDesignerGenerator.Generate(inputClip, settings);
            _beatCandidates.Clear();
            _nextHighPresencePreviewIndex = 0;
            float beatPeriod = 60f / Mathf.Max(1f, result.EstimatedBpm);
            float dedupeTolerance = Mathf.Clamp(duplicateBeatToleranceSeconds, 0.01f, 0.2f);
            List<ALDNoteEvent> deduped = DeduplicateNotesByTime(result.Notes, dedupeTolerance);
            float specialStrengthCutoff = ComputeStrengthPercentile(deduped, 0.86f);
            float highPresenceCutoff = Mathf.Clamp01(highPresenceSelectionThreshold);

            for (int i = 0; i < deduped.Count; i++)
            {
                ALDNoteEvent n = deduped[i];
                ALDNoteAnalysisEvent analysis = FindClosestNoteAnalysis(result.NoteAnalysis, n.hitTimeSeconds);
                float highPresence = analysis.isValid ? analysis.highPresence : Mathf.Clamp01(n.strength * 0.5f);
                float phase = Mathf.Repeat((n.hitTimeSeconds - result.BeatOffsetSeconds) / Mathf.Max(0.0001f, beatPeriod), 1f);
                string label = "Beat";
                if (Mathf.Abs(phase - 0.5f) <= 0.08f)
                {
                    label = "Half";
                }
                else if (Mathf.Abs(phase - (1f / 3f)) <= 0.08f || Mathf.Abs(phase - (2f / 3f)) <= 0.08f)
                {
                    label = "Triplet";
                }

                _beatCandidates.Add(new BeatCandidateSelection
                {
                    time = n.hitTimeSeconds,
                    strength = n.strength,
                    selected = true,
                    subdivisionLabel = label,
                    novelty = 0f,
                    highPresence = highPresence,
                    lowBand = analysis.low,
                    midBand = analysis.mid,
                    highBand = analysis.high,
                    isHighPresence = highPresence >= highPresenceCutoff,
                    isSpecial = false,
                    isRepeated = false,
                    isUnique = true
                });
            }

            TagBeatCandidates(beatPeriod, specialStrengthCutoff);
        }

        private void Generate()
        {
            if (inputClip == null)
            {
                EditorUtility.DisplayDialog("ALD", "Please assign an input AudioClip.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(outputFolder) || !outputFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("ALD", "Output Folder must start with 'Assets/'.", "OK");
                return;
            }

            ALDGenerationSettings settings = new ALDGenerationSettings
            {
                Seed = seed,
                UseRandomness = useRandomness,
                FftWindowSize = fftWindowSize,
                HopSize = hopSize,
                BpmMin = Mathf.Max(40f, bpmMin),
                BpmMax = Mathf.Max(bpmMin + 5f, bpmMax),
                OnsetSensitivity = Mathf.Clamp(onsetSensitivity, 1.01f, 2.5f),
                NoteStrengthGate = Mathf.Clamp01(noteStrengthGate),
                MinNoteSpacingSeconds = Mathf.Max(0.01f, minNoteSpacingSeconds),
                SectionBeats = Mathf.Clamp(sectionBeats, 4, 16),
                EnableMultiSubdivision = enableMultiSubdivision,
                AllowTripletSubdivision = allowTripletSubdivision,
                MaxSubBeatsPerBeat = Mathf.Clamp(maxSubBeatsPerBeat, 0, 4),
                SubBeatStrengthBias = Mathf.Clamp(subBeatStrengthBias, 1f, 1.6f),
                NoteFocusMode = noteFocusMode,
                LowBeatBias = Mathf.Clamp(lowBeatBias, 0.1f, 2f),
                HighPresenceBias = Mathf.Clamp(highPresenceBias, 0.1f, 2f),
                LowBeatGate = Mathf.Clamp(lowBeatGate, 0.01f, 0.75f),
                AutoAlignBeatOffsetToPulseStart = autoAlignBeatOffsetToPulseStart,
                SnapNotesToPulseStart = snapNotesToPulseStart,
                ManualBeatPhaseShiftSeconds = Mathf.Clamp(manualBeatPhaseShiftSeconds, -0.5f, 0.5f),
                PulseStartSearchSeconds = Mathf.Clamp(pulseStartSearchSeconds, 0.005f, 0.25f),
                PulseStartThresholdRatio = Mathf.Clamp(pulseStartThresholdRatio, 0.05f, 0.95f)
            };

            try
            {
                ALDGenerationResult result = ALDLevelDesignerGenerator.Generate(inputClip, settings);
                if (useManualBeatSelection && _beatCandidates.Count > 0)
                {
                    ApplyManualBeatSelection(result);
                }
                EnsureFolderPath(outputFolder);

                string aldPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(outputFolder, aldAssetName + ".asset").Replace("\\", "/"));

                ALDLevelAsset aldAsset = CreateInstance<ALDLevelAsset>();
                aldAsset.SetGeneratedData(
                    inputClip,
                    settings.Seed,
                    settings.UseRandomness,
                    "ALD.v1.1-pulse-start",
                    result.EstimatedBpm,
                    result.BeatOffsetSeconds,
                    result.HopSeconds,
                    result.Commands,
                    result.Notes,
                    result.JudgeProfiles,
                    result.Sections,
                    result.RawBeatOffsetSeconds,
                    result.AutoPulseStartCorrectionSeconds,
                    result.ManualPhaseShiftSeconds);

                AssetDatabase.CreateAsset(aldAsset, aldPath);

                string noteChartPath = string.Empty;
                if (createCompatibilityNoteChart)
                {
                    NoteChart noteChart = CreateInstance<NoteChart>();
                    noteChart.ReplaceNotes(result.ToNoteData());
                    noteChartPath = AssetDatabase.GenerateUniqueAssetPath(
                        Path.Combine(outputFolder, noteChartAssetName + ".asset").Replace("\\", "/"));
                    AssetDatabase.CreateAsset(noteChart, noteChartPath);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorGUIUtility.PingObject(aldAsset);

                _lastSummary = BuildSummary(result, aldPath, noteChartPath);
                Debug.Log(_lastSummary, aldAsset);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("ALD Generation Failed", ex.Message, "OK");
            }
        }

        private static string BuildSummary(ALDGenerationResult result, string aldPath, string noteChartPath)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.AppendLine("ALD generation complete.");
            sb.AppendLine("BPM: " + result.EstimatedBpm.ToString("F2"));
            sb.AppendLine("Beat Offset: " + result.BeatOffsetSeconds.ToString("F3") + "s");
            sb.AppendLine("Raw Beat Offset: " + result.RawBeatOffsetSeconds.ToString("F3") + "s");
            sb.AppendLine("Auto Pulse Correction: " + result.AutoPulseStartCorrectionSeconds.ToString("+#0.000;-#0.000;0.000") + "s");
            sb.AppendLine("Manual Phase Shift: " + result.ManualPhaseShiftSeconds.ToString("+#0.000;-#0.000;0.000") + "s");
            sb.AppendLine("Notes: " + result.Notes.Count);
            sb.AppendLine("Commands: " + result.Commands.Count);
            sb.AppendLine("Sections: " + result.Sections.Count);
            sb.AppendLine("Judge Profiles: " + result.JudgeProfiles.Count);
            sb.AppendLine("ALD Asset: " + aldPath);
            if (!string.IsNullOrEmpty(noteChartPath))
            {
                sb.AppendLine("NoteChart: " + noteChartPath);
            }
            return sb.ToString();
        }

        private void ApplyManualBeatSelection(ALDGenerationResult result)
        {
            if (result == null)
            {
                return;
            }

            List<ALDNoteEvent> selected = new List<ALDNoteEvent>(_beatCandidates.Count);
            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                BeatCandidateSelection c = _beatCandidates[i];
                if (!c.selected)
                {
                    continue;
                }

                selected.Add(new ALDNoteEvent
                {
                    hitTimeSeconds = Mathf.Max(0f, c.time),
                    laneIndex = 0,
                    plane = EvaluatePlaneForTime(c.time, result.Sections),
                    strength = Mathf.Clamp01(c.strength)
                });
            }

            selected.Sort((a, b) => a.hitTimeSeconds.CompareTo(b.hitTimeSeconds));
            result.Notes = DeduplicateNotesByTime(selected, Mathf.Clamp(duplicateBeatToleranceSeconds, 0.01f, 0.2f));
        }

        private static ALDPlaneMode EvaluatePlaneForTime(float t, List<ALDSectionInfo> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return ALDPlaneMode.XY;
            }

            for (int i = sections.Count - 1; i >= 0; i--)
            {
                if (t >= sections[i].startTimeSeconds)
                {
                    return sections[i].plane;
                }
            }

            return sections[0].plane;
        }

        private static List<ALDNoteEvent> DeduplicateNotesByTime(List<ALDNoteEvent> notes, float toleranceSeconds)
        {
            List<ALDNoteEvent> sorted = new List<ALDNoteEvent>(notes);
            sorted.Sort((a, b) => a.hitTimeSeconds.CompareTo(b.hitTimeSeconds));
            List<ALDNoteEvent> outNotes = new List<ALDNoteEvent>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                ALDNoteEvent n = sorted[i];
                if (outNotes.Count == 0)
                {
                    outNotes.Add(n);
                    continue;
                }

                ALDNoteEvent prev = outNotes[outNotes.Count - 1];
                if (Mathf.Abs(n.hitTimeSeconds - prev.hitTimeSeconds) <= toleranceSeconds)
                {
                    if (n.strength > prev.strength)
                    {
                        outNotes[outNotes.Count - 1] = n;
                    }
                }
                else
                {
                    outNotes.Add(n);
                }
            }

            return outNotes;
        }

        private static float ComputeStrengthPercentile(List<ALDNoteEvent> notes, float percentile01)
        {
            if (notes == null || notes.Count == 0)
            {
                return 0.85f;
            }

            List<float> values = new List<float>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
            {
                values.Add(notes[i].strength);
            }
            values.Sort();
            int idx = Mathf.Clamp(Mathf.RoundToInt((values.Count - 1) * Mathf.Clamp01(percentile01)), 0, values.Count - 1);
            return values[idx];
        }

        private void TagBeatCandidates(float beatPeriod, float specialStrengthCutoff)
        {
            if (_beatCandidates.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                BeatCandidateSelection item = _beatCandidates[i];
                float prevDt = i > 0 ? item.time - _beatCandidates[i - 1].time : beatPeriod;
                float nextDt = i < _beatCandidates.Count - 1 ? _beatCandidates[i + 1].time - item.time : beatPeriod;
                float meanDt = Mathf.Max(0.0001f, (prevDt + nextDt) * 0.5f);
                float rhythmDeviation = Mathf.Abs(meanDt - beatPeriod) / Mathf.Max(0.0001f, beatPeriod);
                float localStrengthMean = item.strength;
                int localCount = 1;
                for (int j = Mathf.Max(0, i - 2); j <= Mathf.Min(_beatCandidates.Count - 1, i + 2); j++)
                {
                    if (j == i)
                    {
                        continue;
                    }
                    localStrengthMean += _beatCandidates[j].strength;
                    localCount++;
                }
                localStrengthMean /= Mathf.Max(1, localCount);
                float novelty = Mathf.Clamp01((item.strength - localStrengthMean) * 1.8f + (rhythmDeviation * 0.65f));

                bool repeated = false;
                if (i >= 2)
                {
                    BeatCandidateSelection a = _beatCandidates[i - 1];
                    BeatCandidateSelection b = _beatCandidates[i - 2];
                    float dt1 = item.time - a.time;
                    float dt2 = a.time - b.time;
                    bool sameSubdivision = item.subdivisionLabel == a.subdivisionLabel && a.subdivisionLabel == b.subdivisionLabel;
                    bool similarInterval = Mathf.Abs(dt1 - dt2) <= Mathf.Max(0.02f, beatPeriod * 0.14f);
                    repeated = sameSubdivision && similarInterval;
                }

                bool special = item.strength >= specialStrengthCutoff || novelty >= 0.42f;
                bool highPresence = item.highPresence >= Mathf.Clamp01(highPresenceSelectionThreshold);
                item.novelty = novelty;
                item.isRepeated = repeated;
                item.isSpecial = special;
                item.isHighPresence = highPresence;
                item.isUnique = !repeated || special;
                item.selected = item.isHighPresence || item.isUnique || item.isSpecial;
                _beatCandidates[i] = item;
            }
        }

        private void RefreshHighPresenceTags()
        {
            float threshold = Mathf.Clamp01(highPresenceSelectionThreshold);
            for (int i = 0; i < _beatCandidates.Count; i++)
            {
                BeatCandidateSelection item = _beatCandidates[i];
                item.isHighPresence = item.highPresence >= threshold;
                _beatCandidates[i] = item;
            }
        }

        private static string BuildBeatTags(BeatCandidateSelection item)
        {
            string tags = string.Empty;
            if (item.isHighPresence)
            {
                tags += "[High]";
            }
            if (item.isSpecial)
            {
                tags += "[Special]";
            }
            if (item.isRepeated)
            {
                tags += "[Repeated]";
            }
            if (string.IsNullOrEmpty(tags))
            {
                tags = "[Unique]";
            }

            return tags;
        }

        private void PlayNextHighPresenceBeat()
        {
            if (_beatCandidates.Count == 0)
            {
                return;
            }

            for (int offset = 0; offset < _beatCandidates.Count; offset++)
            {
                int index = (_nextHighPresencePreviewIndex + offset) % _beatCandidates.Count;
                BeatCandidateSelection item = _beatCandidates[index];
                if (!item.isHighPresence)
                {
                    continue;
                }

                _nextHighPresencePreviewIndex = (index + 1) % _beatCandidates.Count;
                PlayPreviewAtTime(item.time);
                return;
            }
        }

        private static ALDNoteAnalysisEvent FindClosestNoteAnalysis(List<ALDNoteAnalysisEvent> analysis, float time)
        {
            if (analysis == null || analysis.Count == 0)
            {
                return default;
            }

            int best = 0;
            float bestDistance = Mathf.Abs(analysis[0].time - time);
            for (int i = 1; i < analysis.Count; i++)
            {
                float d = Mathf.Abs(analysis[i].time - time);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return bestDistance <= 0.08f ? analysis[best] : default;
        }

        private void PlayPreviewAtTime(float beatTimeSeconds)
        {
            if (inputClip == null)
            {
                return;
            }

            float previewStart = Mathf.Clamp(beatTimeSeconds - Mathf.Max(0f, previewLeadSeconds), 0f, Mathf.Max(0f, inputClip.length - 0.01f));
            int startSample = Mathf.Clamp(Mathf.FloorToInt(previewStart * inputClip.frequency), 0, Mathf.Max(0, inputClip.samples - 1));

            Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null)
            {
                return;
            }

            StopPreviewClip();

            MethodInfo[] methods = audioUtilType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo playMethod = null;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "PlayPreviewClip")
                {
                    continue;
                }

                playMethod = m;
                break;
            }

            if (playMethod == null)
            {
                return;
            }

            ParameterInfo[] p = playMethod.GetParameters();
            object[] args = new object[p.Length];
            if (p.Length >= 1)
            {
                args[0] = inputClip;
            }
            if (p.Length >= 2)
            {
                args[1] = startSample;
            }
            if (p.Length >= 3)
            {
                args[2] = false;
            }
            playMethod.Invoke(null, args);
        }

        private static void StopPreviewClip()
        {
            Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null)
            {
                return;
            }

            MethodInfo stop = audioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (stop != null)
            {
                stop.Invoke(null, null);
            }
        }

        [Serializable]
        private struct BeatCandidateSelection
        {
            public float time;
            public float strength;
            public bool selected;
            public string subdivisionLabel;
            public float novelty;
            public float highPresence;
            public float lowBand;
            public float midBand;
            public float highBand;
            public bool isHighPresence;
            public bool isSpecial;
            public bool isRepeated;
            public bool isUnique;

            public BeatCandidateSelection WithSelected(bool value)
            {
                BeatCandidateSelection copy = this;
                copy.selected = value;
                return copy;
            }
        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                throw new InvalidOperationException("Folder path must start with Assets.");
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                {
                    continue;
                }

                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }

    internal struct ALDGenerationSettings
    {
        public int Seed;
        public bool UseRandomness;
        public int FftWindowSize;
        public int HopSize;
        public float BpmMin;
        public float BpmMax;
        public float OnsetSensitivity;
        public float NoteStrengthGate;
        public float MinNoteSpacingSeconds;
        public int SectionBeats;
        public bool EnableMultiSubdivision;
        public bool AllowTripletSubdivision;
        public int MaxSubBeatsPerBeat;
        public float SubBeatStrengthBias;
        public ALDNoteFocusMode NoteFocusMode;
        public float LowBeatBias;
        public float HighPresenceBias;
        public float LowBeatGate;
        public bool AutoAlignBeatOffsetToPulseStart;
        public bool SnapNotesToPulseStart;
        public float ManualBeatPhaseShiftSeconds;
        public float PulseStartSearchSeconds;
        public float PulseStartThresholdRatio;
    }

    internal sealed class ALDGenerationResult
    {
        public float EstimatedBpm;
        public float BeatOffsetSeconds;
        public float RawBeatOffsetSeconds;
        public float AutoPulseStartCorrectionSeconds;
        public float ManualPhaseShiftSeconds;
        public float HopSeconds;
        public List<ALDLevelCommand> Commands = new List<ALDLevelCommand>(64);
        public List<ALDNoteEvent> Notes = new List<ALDNoteEvent>(2048);
        public List<ALDNoteAnalysisEvent> NoteAnalysis = new List<ALDNoteAnalysisEvent>(2048);
        public List<ALDJudgeProfileSection> JudgeProfiles = new List<ALDJudgeProfileSection>(64);
        public List<ALDSectionInfo> Sections = new List<ALDSectionInfo>(64);

        public IReadOnlyList<NoteData> ToNoteData()
        {
            List<NoteData> outNotes = new List<NoteData>(Notes.Count);
            for (int i = 0; i < Notes.Count; i++)
            {
                ALDNoteEvent n = Notes[i];
                NoteData converted = new NoteData
                {
                    hitTimeSeconds = n.hitTimeSeconds,
                    lane = n.laneIndex % 2 == 0 ? LaneId.Left : LaneId.Right
                };
                outNotes.Add(converted);
            }
            return outNotes;
        }
    }

    internal struct ALDNoteAnalysisEvent
    {
        public bool isValid;
        public float time;
        public float onset;
        public float flux;
        public float low;
        public float mid;
        public float high;
        public float total;
        public float highPresence;
    }

    internal static class ALDLevelDesignerGenerator
    {
        private struct SpectrumAnalysis
        {
            public int FrameCount;
            public float HopSeconds;
            public float[] Flux;
            public float[] Onset;
            public float[] Low;
            public float[] Mid;
            public float[] High;
            public float[] Total;
        }

        public static ALDGenerationResult Generate(AudioClip clip, ALDGenerationSettings settings)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0)
            {
                throw new InvalidOperationException("AudioClip has invalid sample metadata.");
            }

            float[] interleaved = new float[clip.samples * clip.channels];
            if (!clip.GetData(interleaved, 0))
            {
                throw new InvalidOperationException(
                    "Could not read PCM data from AudioClip. Try setting clip Load Type to Decompress On Load.");
            }

            float[] mono = ToMono(interleaved, clip.channels);
            SpectrumAnalysis analysis = AnalyzeSpectrum(mono, clip.frequency, settings);

            float bpm = EstimateBpm(analysis.Onset, clip.frequency / (float)settings.HopSize, settings.BpmMin, settings.BpmMax);
            float beatPeriod = 60f / Mathf.Max(1f, bpm);
            float rawBeatOffset = EstimateBeatOffset(analysis.Onset, analysis.HopSeconds, beatPeriod);
            float autoPulseCorrection = settings.AutoAlignBeatOffsetToPulseStart
                ? EstimatePulseStartCorrection(
                    analysis.Onset,
                    analysis.HopSeconds,
                    beatPeriod,
                    rawBeatOffset,
                    settings.PulseStartSearchSeconds,
                    settings.PulseStartThresholdRatio)
                : 0f;
            float beatOffset = NormalizeBeatOffset(
                rawBeatOffset + autoPulseCorrection + settings.ManualBeatPhaseShiftSeconds,
                beatPeriod);

            ALDGenerationResult result = new ALDGenerationResult
            {
                EstimatedBpm = bpm,
                BeatOffsetSeconds = beatOffset,
                RawBeatOffsetSeconds = rawBeatOffset,
                AutoPulseStartCorrectionSeconds = autoPulseCorrection,
                ManualPhaseShiftSeconds = settings.ManualBeatPhaseShiftSeconds,
                HopSeconds = analysis.HopSeconds
            };

            BuildSections(clip.length, beatPeriod, beatOffset, analysis, settings.SectionBeats, result.Sections);
            BuildJudgeProfiles(result.Sections, result.JudgeProfiles);
            BuildCommands(result.Sections, result.JudgeProfiles.Count, result.Commands);
            BuildNotes(clip.length, beatPeriod, beatOffset, analysis, settings, result.Sections, result.Notes);
            BuildNoteAnalysis(result.Notes, analysis, settings, result.NoteAnalysis);

            if (result.Notes.Count == 0)
            {
                // Safe fallback to keep output usable.
                float t = Mathf.Max(beatOffset, 0f);
                int hardCap = Mathf.Min(64, Mathf.FloorToInt((clip.length - t) / beatPeriod));
                for (int i = 0; i < hardCap; i++)
                {
                    float hitTime = t + i * beatPeriod;
                    if (hitTime >= clip.length)
                    {
                        break;
                    }

                    result.Notes.Add(new ALDNoteEvent
                    {
                        hitTimeSeconds = hitTime,
                        laneIndex = 0,
                        plane = EvaluatePlaneAtTime(hitTime, result.Sections),
                        strength = 0.35f
                    });
                }
                BuildNoteAnalysis(result.Notes, analysis, settings, result.NoteAnalysis);
            }

            return result;
        }

        private static void BuildNoteAnalysis(
            List<ALDNoteEvent> notes,
            SpectrumAnalysis analysis,
            ALDGenerationSettings settings,
            List<ALDNoteAnalysisEvent> outAnalysis)
        {
            outAnalysis.Clear();
            if (notes == null || analysis.FrameCount <= 0)
            {
                return;
            }

            for (int i = 0; i < notes.Count; i++)
            {
                float time = notes[i].hitTimeSeconds;
                int frame = Mathf.Clamp(Mathf.RoundToInt(time / analysis.HopSeconds), 0, analysis.FrameCount - 1);
                float onset = SampleLocalPeak(analysis.Onset, frame);
                float flux = SampleLocalPeak(analysis.Flux, frame);
                float total = SampleLocalPeak(analysis.Total, frame);
                float low = SampleLocalPeak(analysis.Low, frame);
                float mid = SampleLocalPeak(analysis.Mid, frame);
                float high = SampleLocalPeak(analysis.High, frame);

                outAnalysis.Add(new ALDNoteAnalysisEvent
                {
                    isValid = true,
                    time = time,
                    onset = onset,
                    flux = flux,
                    low = low,
                    mid = mid,
                    high = high,
                    total = total,
                    highPresence = ComputeHighPresenceScore(low, mid, high, flux, total, settings.HighPresenceBias)
                });
            }
        }

        private static float ComputeHighPresenceScore(float low, float mid, float high, float flux, float total, float bias)
        {
            float safeTotal = Mathf.Max(0.0001f, total);
            float highRatio = high / safeTotal;
            float midRatio = mid / safeTotal;
            float brightness = Mathf.Clamp01((highRatio * 0.70f) + (midRatio * 0.30f));
            float clarity = Mathf.Clamp01((high * (0.80f + bias * 0.36f)) + (mid * 0.26f) + (flux * 0.22f));
            float contrast = Mathf.Clamp01(high - (low * 0.55f));
            return Mathf.Clamp01((clarity * 0.62f) + (brightness * 0.24f) + (contrast * 0.14f));
        }

        private static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1)
            {
                return interleaved;
            }

            int monoCount = interleaved.Length / channels;
            float[] mono = new float[monoCount];

            int src = 0;
            for (int i = 0; i < monoCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[src++];
                }
                mono[i] = sum / channels;
            }

            return mono;
        }

        private static SpectrumAnalysis AnalyzeSpectrum(float[] mono, int sampleRate, ALDGenerationSettings settings)
        {
            int windowSize = Mathf.NextPowerOfTwo(Mathf.Clamp(settings.FftWindowSize, 256, 4096));
            int hop = Mathf.Clamp(settings.HopSize, 64, windowSize);
            float hopSeconds = hop / (float)sampleRate;

            int maxStart = Mathf.Max(0, mono.Length - windowSize);
            int frameCount = 1 + (maxStart / hop);

            int half = windowSize / 2;
            float[] window = BuildHannWindow(windowSize);
            float[] real = new float[windowSize];
            float[] imag = new float[windowSize];
            float[] prevMag = new float[half];

            float[] flux = new float[frameCount];
            float[] low = new float[frameCount];
            float[] mid = new float[frameCount];
            float[] high = new float[frameCount];
            float[] total = new float[frameCount];

            float lowMinHz = 30f;
            float lowMaxHz = 220f;
            float midMaxHz = 2200f;
            float highMaxHz = 9000f;

            for (int frame = 0; frame < frameCount; frame++)
            {
                int start = frame * hop;
                FillFftBuffer(mono, start, window, real, imag);
                FFTInPlace(real, imag);

                float frameFlux = 0f;
                float frameLow = 0f;
                float frameMid = 0f;
                float frameHigh = 0f;

                for (int bin = 1; bin < half; bin++)
                {
                    float re = real[bin];
                    float im = imag[bin];
                    float mag = Mathf.Sqrt((re * re) + (im * im));
                    float freq = bin * sampleRate / (float)windowSize;

                    if (frame > 0)
                    {
                        float delta = mag - prevMag[bin];
                        if (delta > 0f)
                        {
                            float weight = freq < midMaxHz ? 1.15f : 0.85f;
                            frameFlux += delta * weight;
                        }
                    }

                    prevMag[bin] = mag;

                    if (freq >= lowMinHz && freq < lowMaxHz)
                    {
                        frameLow += mag;
                    }
                    else if (freq >= lowMaxHz && freq < midMaxHz)
                    {
                        frameMid += mag;
                    }
                    else if (freq >= midMaxHz && freq < highMaxHz)
                    {
                        frameHigh += mag;
                    }
                }

                flux[frame] = frameFlux;
                low[frame] = frameLow;
                mid[frame] = frameMid;
                high[frame] = frameHigh;
                total[frame] = frameLow + frameMid + frameHigh;
            }

            NormalizeInPlace(flux);
            NormalizeInPlace(low);
            NormalizeInPlace(mid);
            NormalizeInPlace(high);
            NormalizeInPlace(total);

            float[] threshold = ComputeMovingAverage(flux, Mathf.Max(4, Mathf.RoundToInt(0.10f / hopSeconds)));
            float[] onset = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                onset[i] = Mathf.Max(0f, flux[i] - (threshold[i] * settings.OnsetSensitivity));
            }
            NormalizeInPlace(onset);

            return new SpectrumAnalysis
            {
                FrameCount = frameCount,
                HopSeconds = hopSeconds,
                Flux = flux,
                Onset = onset,
                Low = low,
                Mid = mid,
                High = high,
                Total = total
            };
        }

        private static float EstimateBpm(float[] onset, float frameRate, float bpmMin, float bpmMax)
        {
            if (onset == null || onset.Length < 16)
            {
                return 120f;
            }

            int minLag = Mathf.Max(1, Mathf.RoundToInt(frameRate * 60f / bpmMax));
            int maxLag = Mathf.Max(minLag + 1, Mathf.RoundToInt(frameRate * 60f / bpmMin));
            maxLag = Mathf.Min(maxLag, onset.Length - 2);

            if (minLag >= maxLag)
            {
                return 120f;
            }

            float bestScore = float.NegativeInfinity;
            int bestLag = minLag;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float sum = 0f;
                for (int i = lag; i < onset.Length; i++)
                {
                    sum += onset[i] * onset[i - lag];
                }

                // Slight preference for human BPM center to reduce half/double errors.
                float candidateBpm = frameRate * 60f / lag;
                float centerBias = 1f - Mathf.Abs(candidateBpm - 128f) / 256f;
                float score = sum * Mathf.Clamp(centerBias, 0.65f, 1.05f);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            return Mathf.Clamp(frameRate * 60f / bestLag, bpmMin, bpmMax);
        }

        private static float EstimateBeatOffset(float[] onset, float hopSeconds, float beatPeriod)
        {
            if (onset == null || onset.Length == 0)
            {
                return 0f;
            }

            int phaseSteps = 96;
            float bestOffset = 0f;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < phaseSteps; i++)
            {
                float phase = beatPeriod * (i / (float)phaseSteps);
                float score = 0f;

                for (float t = phase; t < onset.Length * hopSeconds; t += beatPeriod)
                {
                    int idx = Mathf.RoundToInt(t / hopSeconds);
                    score += SampleLocalPeak(onset, idx);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = phase;
                }
            }

            return Mathf.Max(0f, bestOffset);
        }

        private static float NormalizeBeatOffset(float offset, float beatPeriod)
        {
            if (beatPeriod <= 0.0001f)
            {
                return Mathf.Max(0f, offset);
            }

            if (offset < 0f)
            {
                return 0f;
            }

            return offset >= beatPeriod ? Mathf.Repeat(offset, beatPeriod) : offset;
        }

        private static float EstimatePulseStartCorrection(
            float[] onset,
            float hopSeconds,
            float beatPeriod,
            float beatOffset,
            float searchSeconds,
            float thresholdRatio)
        {
            if (onset == null || onset.Length == 0 || hopSeconds <= 0f || beatPeriod <= 0.0001f)
            {
                return 0f;
            }

            int searchFrames = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.005f, searchSeconds) / hopSeconds));
            float ratio = Mathf.Clamp(thresholdRatio, 0.05f, 0.95f);
            float clipLength = onset.Length * hopSeconds;
            int maxBeats = Mathf.Min(128, Mathf.Max(0, Mathf.CeilToInt((clipLength - beatOffset) / beatPeriod)));
            float weightedSum = 0f;
            float weightTotal = 0f;

            for (int beat = 0; beat < maxBeats; beat++)
            {
                float expected = beatOffset + beat * beatPeriod;
                int center = Mathf.RoundToInt(expected / hopSeconds);
                PulseTiming timing = FindPulseStartNearFrame(onset, center, searchFrames, ratio, hopSeconds);
                if (!timing.isValid)
                {
                    continue;
                }

                float correction = timing.startTime - expected;
                if (Mathf.Abs(correction) > searchSeconds)
                {
                    continue;
                }

                float weight = Mathf.Max(0.001f, timing.peakValue);
                weightedSum += correction * weight;
                weightTotal += weight;
            }

            return weightTotal > 0f
                ? Mathf.Clamp(weightedSum / weightTotal, -searchSeconds, searchSeconds)
                : 0f;
        }

        private struct PulseTiming
        {
            public bool isValid;
            public float startTime;
            public int peakFrame;
            public float peakValue;
        }

        private static PulseTiming FindPulseStartNearFrame(
            float[] onset,
            int centerFrame,
            int searchFrames,
            float thresholdRatio,
            float hopSeconds)
        {
            if (onset == null || onset.Length == 0 || hopSeconds <= 0f)
            {
                return default;
            }

            int radius = Mathf.Max(1, searchFrames);
            int a = Mathf.Clamp(centerFrame - radius, 0, onset.Length - 1);
            int b = Mathf.Clamp(centerFrame + radius, a, onset.Length - 1);
            int peak = a;
            float peakValue = -1f;

            for (int i = a; i <= b; i++)
            {
                if (onset[i] > peakValue)
                {
                    peakValue = onset[i];
                    peak = i;
                }
            }

            if (peakValue <= 0.0001f)
            {
                return default;
            }

            float threshold = peakValue * Mathf.Clamp(thresholdRatio, 0.05f, 0.95f);
            int start = peak;
            while (start > 0 && start > peak - radius && onset[start] > threshold)
            {
                start--;
            }
            int firstAudibleFrame = Mathf.Min(peak, start + 1);

            return new PulseTiming
            {
                isValid = true,
                startTime = Mathf.Max(0f, firstAudibleFrame * hopSeconds),
                peakFrame = peak,
                peakValue = peakValue
            };
        }

        private static void BuildSections(
            float clipLength,
            float beatPeriod,
            float beatOffset,
            SpectrumAnalysis analysis,
            int sectionBeats,
            List<ALDSectionInfo> outSections)
        {
            outSections.Clear();

            float sectionDuration = Mathf.Max(beatPeriod * Mathf.Max(1, sectionBeats), 0.5f);
            int sectionCount = Mathf.Max(1, Mathf.CeilToInt((clipLength - beatOffset) / sectionDuration));
            ALDPlaneMode[] planeDraft = new ALDPlaneMode[sectionCount];

            float globalMeanTotal = Mean(analysis.Total);

            for (int i = 0; i < sectionCount; i++)
            {
                float start = beatOffset + (i * sectionDuration);
                float end = Mathf.Min(clipLength, start + sectionDuration);

                RangeToFrameIndices(start, end, analysis.HopSeconds, analysis.FrameCount, out int a, out int b);
                float avgLow = MeanSlice(analysis.Low, a, b);
                float avgMid = MeanSlice(analysis.Mid, a, b);
                float avgHigh = MeanSlice(analysis.High, a, b);
                float avgTotal = MeanSlice(analysis.Total, a, b);

                bool highDriven = (avgMid + avgHigh) > (avgLow * 1.12f) && avgTotal >= (globalMeanTotal * 0.92f);
                planeDraft[i] = highDriven ? ALDPlaneMode.XZ : ALDPlaneMode.XY;
            }

            // Remove one-section flicker.
            for (int i = 1; i < sectionCount - 1; i++)
            {
                if (planeDraft[i] != planeDraft[i - 1] && planeDraft[i - 1] == planeDraft[i + 1])
                {
                    planeDraft[i] = planeDraft[i - 1];
                }
            }

            for (int i = 0; i < sectionCount; i++)
            {
                float start = beatOffset + (i * sectionDuration);
                float end = Mathf.Min(clipLength, start + sectionDuration);

                RangeToFrameIndices(start, end, analysis.HopSeconds, analysis.FrameCount, out int a, out int b);
                float avgLow = MeanSlice(analysis.Low, a, b);
                float avgMid = MeanSlice(analysis.Mid, a, b);
                float avgHigh = MeanSlice(analysis.High, a, b);
                float intensity = MeanSlice(analysis.Total, a, b);

                float suggestedTravel = Mathf.Lerp(1.25f, 0.82f, intensity) * beatPeriod;
                suggestedTravel = Mathf.Clamp(suggestedTravel, 0.24f, 1.85f);

                float suggestedRingSpacing = planeDraft[i] == ALDPlaneMode.XY
                    ? Mathf.Lerp(1.55f, 2.45f, intensity)
                    : Mathf.Lerp(2.25f, 3.75f, intensity);

                string hint = planeDraft[i] == ALDPlaneMode.XY
                    ? "XY: prioritize vertical timing readability."
                    : "XZ: emphasize projectile motion readability.";

                ALDSectionInfo section = new ALDSectionInfo
                {
                    startTimeSeconds = Mathf.Max(0f, start),
                    endTimeSeconds = Mathf.Max(start, end),
                    plane = planeDraft[i],
                    laneCount = 1,
                    intensity = intensity,
                    lowBandEnergy = avgLow,
                    midBandEnergy = avgMid,
                    highBandEnergy = avgHigh,
                    suggestedTravelTimeSeconds = suggestedTravel,
                    suggestedRingSpacing = suggestedRingSpacing,
                    designHint = hint
                };

                outSections.Add(section);
            }
        }

        private static void BuildJudgeProfiles(List<ALDSectionInfo> sections, List<ALDJudgeProfileSection> outProfiles)
        {
            outProfiles.Clear();

            for (int i = 0; i < sections.Count; i++)
            {
                ALDSectionInfo s = sections[i];
                float clarity = Mathf.Clamp01((s.midBandEnergy * 0.45f) + (s.highBandEnergy * 0.35f) + (s.intensity * 0.2f));

                // High clarity => tighter windows.
                float perfect = Mathf.Lerp(0.045f, 0.028f, clarity);
                float great = Mathf.Lerp(0.090f, 0.062f, clarity);
                float earlyLate = Mathf.Lerp(0.125f, 0.095f, clarity);

                ALDJudgeProfileSection profile = new ALDJudgeProfileSection
                {
                    startTimeSeconds = s.startTimeSeconds,
                    perfectWindowSeconds = perfect,
                    greatWindowSeconds = great,
                    earlyLateWindowSeconds = earlyLate,
                    description = "Auto from ALD section clarity=" + clarity.ToString("F2")
                };

                outProfiles.Add(profile);
            }
        }

        private static void BuildCommands(List<ALDSectionInfo> sections, int judgeCount, List<ALDLevelCommand> outCommands)
        {
            outCommands.Clear();

            outCommands.Add(new ALDLevelCommand
            {
                timeSeconds = 0f,
                type = ALDCommandType.SetLaneCount,
                laneCount = 1,
                plane = ALDPlaneMode.XY,
                judgeProfileIndex = 0,
                note = "ALD v1 deterministic lane setup."
            });

            ALDPlaneMode currentPlane = ALDPlaneMode.XY;
            if (sections.Count > 0)
            {
                currentPlane = sections[0].plane;
            }

            outCommands.Add(new ALDLevelCommand
            {
                timeSeconds = 0f,
                type = ALDCommandType.SetPlane,
                plane = currentPlane,
                laneCount = 1,
                judgeProfileIndex = 0,
                note = "Initial plane."
            });

            for (int i = 0; i < sections.Count; i++)
            {
                ALDSectionInfo s = sections[i];
                int profileIndex = Mathf.Clamp(i, 0, Mathf.Max(0, judgeCount - 1));

                outCommands.Add(new ALDLevelCommand
                {
                    timeSeconds = s.startTimeSeconds,
                    type = ALDCommandType.SetJudgeProfile,
                    judgeProfileIndex = profileIndex,
                    laneCount = s.laneCount,
                    plane = s.plane,
                    note = "Use judge profile " + profileIndex
                });

                if (s.plane != currentPlane)
                {
                    currentPlane = s.plane;
                    outCommands.Add(new ALDLevelCommand
                    {
                        timeSeconds = s.startTimeSeconds,
                        type = ALDCommandType.SetPlane,
                        plane = currentPlane,
                        laneCount = s.laneCount,
                        judgeProfileIndex = profileIndex,
                        note = "ALD plane transition."
                    });
                }
            }
        }

        private static void BuildNotes(
            float clipLength,
            float beatPeriod,
            float beatOffset,
            SpectrumAnalysis analysis,
            ALDGenerationSettings settings,
            List<ALDSectionInfo> sections,
            List<ALDNoteEvent> outNotes)
        {
            outNotes.Clear();

            float localGate = Mathf.Clamp(settings.NoteStrengthGate, 0.05f, 0.95f);
            float lastNoteTime = -999f;
            int beatIndex = 0;
            List<CandidateNote> candidates = new List<CandidateNote>(6);

            for (float t = beatOffset; t < clipLength - 0.02f; t += beatPeriod)
            {
                candidates.Clear();

                ALDSectionInfo section = EvaluateSectionAtTime(t, sections);
                float sectionDrive = Mathf.Clamp01((section.intensity * 0.55f) + (section.midBandEnergy * 0.3f) + (section.highBandEnergy * 0.15f));
                float beatGate = localGate * Mathf.Lerp(1.08f, 0.76f, sectionDrive);
                AddCandidate(candidates, t, 0, beatGate, clipLength, beatPeriod, analysis, settings, sections);

                if (settings.EnableMultiSubdivision && settings.MaxSubBeatsPerBeat > 0)
                {
                    // Half-beat candidate.
                    float halfTime = t + (beatPeriod * 0.5f);
                    float halfGate = beatGate * Mathf.Lerp(settings.SubBeatStrengthBias, 0.96f, sectionDrive);
                    AddCandidate(candidates, halfTime, 1, halfGate, clipLength, beatPeriod, analysis, settings, sections);

                    if (settings.AllowTripletSubdivision)
                    {
                        float trip1 = t + (beatPeriod / 3f);
                        float trip2 = t + ((beatPeriod * 2f) / 3f);
                        float tripGate = beatGate * Mathf.Lerp(settings.SubBeatStrengthBias * 1.04f, 1.02f, sectionDrive);
                        AddCandidate(candidates, trip1, 2, tripGate, clipLength, beatPeriod, analysis, settings, sections);
                        AddCandidate(candidates, trip2, 2, tripGate, clipLength, beatPeriod, analysis, settings, sections);
                    }
                }

                // Always keep at least one anchor beat every 4 beats for readability.
                if (candidates.Count == 0 && beatIndex % 4 == 0)
                {
                    int frame = Mathf.Clamp(Mathf.RoundToInt(t / analysis.HopSeconds), 0, analysis.FrameCount - 1);
                    float fallbackStrength = Mathf.Max(
                        SampleLocalPeak(analysis.Onset, frame),
                        SampleLocalPeak(analysis.Total, frame) * 0.82f);
                    candidates.Add(new CandidateNote
                    {
                        time = t,
                        strength = Mathf.Clamp01(fallbackStrength),
                        priority = 100
                    });
                }

                candidates.Sort((a, b) =>
                {
                    int byPriority = b.priority.CompareTo(a.priority);
                    if (byPriority != 0)
                    {
                        return byPriority;
                    }

                    int byStrength = b.strength.CompareTo(a.strength);
                    if (byStrength != 0)
                    {
                        return byStrength;
                    }

                    return a.time.CompareTo(b.time);
                });

                int addedThisBeat = 0;
                int maxAdds = Mathf.Max(1, 1 + settings.MaxSubBeatsPerBeat);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (addedThisBeat >= maxAdds)
                    {
                        break;
                    }

                    CandidateNote c = candidates[i];
                    if ((c.time - lastNoteTime) < settings.MinNoteSpacingSeconds)
                    {
                        continue;
                    }

                    outNotes.Add(new ALDNoteEvent
                    {
                        hitTimeSeconds = c.time,
                        laneIndex = 0,
                        plane = EvaluatePlaneAtTime(c.time, sections),
                        strength = Mathf.Clamp01(c.strength)
                    });

                    lastNoteTime = c.time;
                    addedThisBeat++;
                }
                beatIndex++;
            }

            outNotes.Sort((a, b) => a.hitTimeSeconds.CompareTo(b.hitTimeSeconds));
        }

        private struct CandidateNote
        {
            public float time;
            public float strength;
            public int priority;
        }

        private static void AddCandidate(
            List<CandidateNote> candidates,
            float candidateTime,
            int subdivisionType,
            float gate,
            float clipLength,
            float beatPeriod,
            SpectrumAnalysis analysis,
            ALDGenerationSettings settings,
            List<ALDSectionInfo> sections)
        {
            if (candidateTime < 0f || candidateTime >= clipLength - 0.02f)
            {
                return;
            }

            float noteTime = candidateTime;
            int frame = Mathf.Clamp(Mathf.RoundToInt(candidateTime / analysis.HopSeconds), 0, analysis.FrameCount - 1);
            if (settings.SnapNotesToPulseStart)
            {
                int searchFrames = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.005f, settings.PulseStartSearchSeconds) / analysis.HopSeconds));
                PulseTiming timing = FindPulseStartNearFrame(
                    analysis.Onset,
                    frame,
                    searchFrames,
                    settings.PulseStartThresholdRatio,
                    analysis.HopSeconds);
                if (timing.isValid && timing.startTime >= 0f && timing.startTime < clipLength - 0.02f)
                {
                    noteTime = timing.startTime;
                    frame = Mathf.Clamp(timing.peakFrame, 0, analysis.FrameCount - 1);
                }
            }

            float onset = SampleLocalPeak(analysis.Onset, frame);
            float flux = SampleLocalPeak(analysis.Flux, frame);
            float total = SampleLocalPeak(analysis.Total, frame);
            float low = SampleLocalPeak(analysis.Low, frame);
            float mid = SampleLocalPeak(analysis.Mid, frame);
            float high = SampleLocalPeak(analysis.High, frame);
            ALDSectionInfo section = EvaluateSectionAtTime(noteTime, sections);

            float bandShape = (low * 0.22f) + (mid * 0.48f) + (high * 0.30f);
            float structural = Mathf.Clamp01((section.intensity * 0.5f) + (section.midBandEnergy * 0.32f) + (section.highBandEnergy * 0.18f));
            float strength = (onset * 0.58f) + (flux * 0.20f) + (total * 0.12f) + (bandShape * 0.10f);
            strength *= Mathf.Lerp(0.92f, 1.08f, structural);

            float candidateGate = gate;
            if (settings.NoteFocusMode == ALDNoteFocusMode.LowBeatHighPresence)
            {
                float lowDrive = Mathf.Clamp01((low * (0.75f + (settings.LowBeatBias * 0.55f))) + (onset * 0.28f));
                float highPresence = ComputeHighPresenceScore(low, mid, high, flux, total, settings.HighPresenceBias);
                float contrast = Mathf.Clamp01(highPresence - (total * 0.42f));
                float focusStrength = Mathf.Clamp01((lowDrive * 0.58f) + (highPresence * 0.30f) + (contrast * 0.12f));
                strength = Mathf.Lerp(strength, focusStrength, 0.62f);

                float lowGate = Mathf.Clamp01(settings.LowBeatGate);
                if (low < lowGate && onset < (candidateGate * 1.1f))
                {
                    return;
                }

                candidateGate *= Mathf.Lerp(1.04f, 0.90f, highPresence);
            }
            else if (settings.NoteFocusMode == ALDNoteFocusMode.HighPresence)
            {
                float highPresence = ComputeHighPresenceScore(low, mid, high, flux, total, settings.HighPresenceBias);
                float highDrive = Mathf.Clamp01((highPresence * 0.70f) + (onset * 0.18f) + (flux * 0.12f));
                strength = Mathf.Lerp(strength, highDrive, 0.72f);
                candidateGate *= Mathf.Lerp(1.08f, 0.82f, highPresence);
            }

            if (subdivisionType == 1)
            {
                candidateGate *= Mathf.Lerp(1.08f, 0.94f, structural);
            }
            else if (subdivisionType == 2)
            {
                candidateGate *= Mathf.Lerp(1.14f, 0.98f, structural);
            }

            if (strength < candidateGate)
            {
                return;
            }

            // Penalize very close to beat edges for triplets/half-beats to reduce clutter.
            float phase = Mathf.Repeat((noteTime - EvaluateBeatStart(noteTime, beatPeriod, sections)) / Mathf.Max(0.0001f, beatPeriod), 1f);
            float edgePenalty = Mathf.Abs((phase * 2f) - 1f);
            float weightedStrength = strength * Mathf.Lerp(1f, 0.86f, edgePenalty * 0.45f);

            int priority = subdivisionType == 0 ? 30 : (subdivisionType == 1 ? 20 : 16);
            priority += Mathf.RoundToInt(weightedStrength * 10f);

            candidates.Add(new CandidateNote
            {
                time = noteTime,
                strength = weightedStrength,
                priority = priority
            });
        }

        private static float EvaluateBeatStart(float candidateTime, float beatPeriod, List<ALDSectionInfo> sections)
        {
            if (beatPeriod <= 0.0001f)
            {
                return 0f;
            }

            float sectionStart = 0f;
            ALDSectionInfo section = EvaluateSectionAtTime(candidateTime, sections);
            sectionStart = section.startTimeSeconds;
            float beats = Mathf.Floor((candidateTime - sectionStart) / beatPeriod);
            return sectionStart + (beats * beatPeriod);
        }

        private static ALDPlaneMode EvaluatePlaneAtTime(float t, List<ALDSectionInfo> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return ALDPlaneMode.XY;
            }

            for (int i = sections.Count - 1; i >= 0; i--)
            {
                if (t >= sections[i].startTimeSeconds)
                {
                    return sections[i].plane;
                }
            }

            return sections[0].plane;
        }

        private static ALDSectionInfo EvaluateSectionAtTime(float t, List<ALDSectionInfo> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return default;
            }

            for (int i = sections.Count - 1; i >= 0; i--)
            {
                if (t >= sections[i].startTimeSeconds)
                {
                    return sections[i];
                }
            }

            return sections[0];
        }

        private static void RangeToFrameIndices(float start, float end, float hopSeconds, int frameCount, out int a, out int b)
        {
            a = Mathf.Clamp(Mathf.FloorToInt(start / hopSeconds), 0, frameCount - 1);
            b = Mathf.Clamp(Mathf.CeilToInt(end / hopSeconds), a + 1, frameCount);
        }

        private static float Mean(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return sum / values.Length;
        }

        private static float MeanSlice(float[] values, int startInclusive, int endExclusive)
        {
            if (values == null || values.Length == 0 || endExclusive <= startInclusive)
            {
                return 0f;
            }

            int a = Mathf.Clamp(startInclusive, 0, values.Length - 1);
            int b = Mathf.Clamp(endExclusive, a + 1, values.Length);
            float sum = 0f;
            int count = 0;
            for (int i = a; i < b; i++)
            {
                sum += values[i];
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        private static float SampleLocalPeak(float[] values, int center)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            int a = Mathf.Max(0, center - 1);
            int b = Mathf.Min(values.Length - 1, center + 1);
            float peak = 0f;
            for (int i = a; i <= b; i++)
            {
                if (values[i] > peak)
                {
                    peak = values[i];
                }
            }
            return peak;
        }

        private static void NormalizeInPlace(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            float max = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            if (max <= 0.0000001f)
            {
                return;
            }

            float inv = 1f / max;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] *= inv;
            }
        }

        private static float[] ComputeMovingAverage(float[] values, int radius)
        {
            int n = values.Length;
            float[] avg = new float[n];
            float[] prefix = new float[n + 1];

            for (int i = 0; i < n; i++)
            {
                prefix[i + 1] = prefix[i] + values[i];
            }

            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - radius);
                int b = Mathf.Min(n, i + radius + 1);
                avg[i] = (prefix[b] - prefix[a]) / Mathf.Max(1, b - a);
            }

            return avg;
        }

        private static float[] BuildHannWindow(int size)
        {
            float[] window = new float[size];
            if (size <= 1)
            {
                window[0] = 1f;
                return window;
            }

            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5f * (1f - Mathf.Cos((2f * Mathf.PI * i) / (size - 1f)));
            }

            return window;
        }

        private static void FillFftBuffer(float[] mono, int start, float[] window, float[] real, float[] imag)
        {
            int size = window.Length;
            for (int i = 0; i < size; i++)
            {
                int idx = start + i;
                float sample = idx >= 0 && idx < mono.Length ? mono[idx] : 0f;
                real[i] = sample * window[i];
                imag[i] = 0f;
            }
        }

        // Iterative radix-2 FFT in-place.
        private static void FFTInPlace(float[] real, float[] imag)
        {
            int n = real.Length;
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j |= bit;

                if (i < j)
                {
                    float tr = real[i];
                    real[i] = real[j];
                    real[j] = tr;

                    float ti = imag[i];
                    imag[i] = imag[j];
                    imag[j] = ti;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                float angle = -2f * Mathf.PI / len;
                float wLenCos = Mathf.Cos(angle);
                float wLenSin = Mathf.Sin(angle);

                for (int i = 0; i < n; i += len)
                {
                    float wCos = 1f;
                    float wSin = 0f;
                    int half = len >> 1;

                    for (int k = 0; k < half; k++)
                    {
                        int even = i + k;
                        int odd = even + half;

                        float oddReal = (real[odd] * wCos) - (imag[odd] * wSin);
                        float oddImag = (real[odd] * wSin) + (imag[odd] * wCos);

                        real[odd] = real[even] - oddReal;
                        imag[odd] = imag[even] - oddImag;
                        real[even] += oddReal;
                        imag[even] += oddImag;

                        float nextCos = (wCos * wLenCos) - (wSin * wLenSin);
                        float nextSin = (wCos * wLenSin) + (wSin * wLenCos);
                        wCos = nextCos;
                        wSin = nextSin;
                    }
                }
            }
        }
    }
}
