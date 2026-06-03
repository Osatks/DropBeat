using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    public enum ALDPlaneMode : byte
    {
        XY = 0,
        XZ = 1
    }

    public enum ALDCommandType : byte
    {
        SetPlane = 0,
        SetLaneCount = 1,
        SetJudgeProfile = 2
    }

    [Serializable]
    public struct ALDLevelCommand
    {
        [Min(0f)] public float timeSeconds;
        public ALDCommandType type;
        public ALDPlaneMode plane;
        [Min(1)] public int laneCount;
        [Min(0)] public int judgeProfileIndex;
        public string note;
    }

    [Serializable]
    public struct ALDNoteEvent
    {
        [Min(0f)] public float hitTimeSeconds;
        [Min(0)] public int laneIndex;
        public ALDPlaneMode plane;
        [Range(0f, 1f)] public float strength;
    }

    [Serializable]
    public struct ALDJudgeProfileSection
    {
        [Min(0f)] public float startTimeSeconds;
        [Min(0.001f)] public float perfectWindowSeconds;
        [Min(0.001f)] public float greatWindowSeconds;
        [Min(0.001f)] public float earlyLateWindowSeconds;
        public string description;
    }

    [Serializable]
    public struct ALDSectionInfo
    {
        [Min(0f)] public float startTimeSeconds;
        [Min(0f)] public float endTimeSeconds;
        public ALDPlaneMode plane;
        [Min(1)] public int laneCount;
        [Range(0f, 1f)] public float intensity;
        [Range(0f, 1f)] public float lowBandEnergy;
        [Range(0f, 1f)] public float midBandEnergy;
        [Range(0f, 1f)] public float highBandEnergy;
        [Min(0.05f)] public float suggestedTravelTimeSeconds;
        [Min(0.01f)] public float suggestedRingSpacing;
        public string designHint;
    }

    [CreateAssetMenu(menuName = "BeatDrop/ALD/ALD Level Asset", fileName = "ALD_Level")]
    public sealed class ALDLevelAsset : ScriptableObject
    {
        [Header("Source")]
        [SerializeField] private AudioClip sourceClip;
        [SerializeField] private int seed;
        [SerializeField] private bool useRandomness;
        [SerializeField] private string generatorVersion = "ALD.v1.0";
        [SerializeField] private float estimatedBpm = 120f;
        [SerializeField] private float beatPhaseOffsetSeconds;
        [SerializeField] private float analysisHopSeconds = 0.01f;
        [SerializeField] private float rawBeatPhaseOffsetSeconds;
        [SerializeField] private float autoPulseStartCorrectionSeconds;
        [SerializeField] private float manualPhaseShiftSeconds;

        [Header("Generated Data")]
        [SerializeField] private List<ALDLevelCommand> commands = new List<ALDLevelCommand>(64);
        [SerializeField] private List<ALDNoteEvent> notes = new List<ALDNoteEvent>(2048);
        [SerializeField] private List<ALDJudgeProfileSection> judgeProfileSections = new List<ALDJudgeProfileSection>(64);
        [SerializeField] private List<ALDSectionInfo> sections = new List<ALDSectionInfo>(64);

        public AudioClip SourceClip => sourceClip;
        public int Seed => seed;
        public bool UseRandomness => useRandomness;
        public string GeneratorVersion => generatorVersion;
        public float EstimatedBpm => estimatedBpm;
        public float BeatPhaseOffsetSeconds => beatPhaseOffsetSeconds;
        public float AnalysisHopSeconds => analysisHopSeconds;
        public float RawBeatPhaseOffsetSeconds => rawBeatPhaseOffsetSeconds;
        public float AutoPulseStartCorrectionSeconds => autoPulseStartCorrectionSeconds;
        public float ManualPhaseShiftSeconds => manualPhaseShiftSeconds;
        public IReadOnlyList<ALDLevelCommand> Commands => commands;
        public IReadOnlyList<ALDNoteEvent> Notes => notes;
        public IReadOnlyList<ALDJudgeProfileSection> JudgeProfileSections => judgeProfileSections;
        public IReadOnlyList<ALDSectionInfo> Sections => sections;

#if UNITY_EDITOR
        public void SetGeneratedData(
            AudioClip clip,
            int levelSeed,
            bool randomnessEnabled,
            string version,
            float bpm,
            float beatOffset,
            float hopSeconds,
            List<ALDLevelCommand> newCommands,
            List<ALDNoteEvent> newNotes,
            List<ALDJudgeProfileSection> newJudgeSections,
            List<ALDSectionInfo> newSections,
            float rawBeatOffset = -1f,
            float autoPulseCorrection = 0f,
            float manualPhaseShift = 0f)
        {
            sourceClip = clip;
            seed = levelSeed;
            useRandomness = randomnessEnabled;
            generatorVersion = string.IsNullOrWhiteSpace(version) ? "ALD.v1.0" : version;
            estimatedBpm = Mathf.Max(1f, bpm);
            beatPhaseOffsetSeconds = Mathf.Max(0f, beatOffset);
            analysisHopSeconds = Mathf.Max(0.001f, hopSeconds);
            rawBeatPhaseOffsetSeconds = rawBeatOffset >= 0f ? rawBeatOffset : beatPhaseOffsetSeconds;
            autoPulseStartCorrectionSeconds = autoPulseCorrection;
            manualPhaseShiftSeconds = manualPhaseShift;
            commands = newCommands ?? new List<ALDLevelCommand>(0);
            notes = newNotes ?? new List<ALDNoteEvent>(0);
            judgeProfileSections = newJudgeSections ?? new List<ALDJudgeProfileSection>(0);
            sections = newSections ?? new List<ALDSectionInfo>(0);
        }
#endif
    }
}
