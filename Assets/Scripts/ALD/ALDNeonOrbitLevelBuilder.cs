using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Generic ALD level builder (legacy class name kept for compatibility).
    /// ALD provides beat times; this builder chooses stable beats and places rings with physics constraints.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BeatDrop/ALD Level Builder (Legacy Name)")]
    public class ALDNeonOrbitLevelBuilder : MonoBehaviour
    {
        private enum FlightGenerationModel
        {
            JerkAssist = 0,
            Ballistic = 1
        }

        [Header("Sources")]
        [SerializeField] private ALDLevelAsset levelAsset;
        [SerializeField] private Transform ringChainRoot;
        [SerializeField] private GameObject ringPrefab;
        [SerializeField] private GameSynthezerAndController gameplayController;
        [SerializeField] private Camera targetCamera;

        [Header("Build Trigger")]
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField] private bool clearExistingRings = true;

        [Header("Beat Filtering")]
        [SerializeField] private int maxRings = 160;
        [SerializeField] private float minNoteGapSeconds = 0.18f;
        [SerializeField] private float minBeatGapSeconds = 0.26f;
        [SerializeField] private float maxBeatGapSeconds = 3.2f;
        [SerializeField] private int candidateSearchWindow = 16;

        [Header("Placement")]
        [SerializeField] private float startY = 2f;
        [SerializeField] private float minSpacing = 1.8f;
        [SerializeField] private float maxSpacing = 16f;
        [SerializeField] private float spacingMultiplier = 1f;
        [SerializeField] private float verticalUnitsPerSecond = 7.2f;
        [SerializeField] private bool spacingStrictFromBeatTime = true;
        [SerializeField] private float beatTimeSpacingUnitsPerSecond = 8.2f;
        [SerializeField] private float minBeatGapSecondsForSpacing = 0.08f;

        [Header("Distance Fit")]
        [SerializeField] private float minRingDistance3D = 2.6f;
        [SerializeField] private float maxRingDistance3D = 16f;
        [SerializeField] private float timingDistanceUnitsPerSecond = 8.1f;
        [SerializeField] private float lateralDistanceWeight = 1.0f;
        [SerializeField] private float sectionSpacingWeight = 0.45f;
        [SerializeField] private float timingSpacingWeight = 0.55f;

        [Header("Aspect Fit")]
        [SerializeField] private float laneWidthMargin = 0.78f;
        [SerializeField] private float ringBaseRadius = 2.2f;
        [SerializeField] private float minRingScale = 0.62f;
        [SerializeField] private float maxRingScale = 1.15f;

        [Header("Stability Constraints")]
        [SerializeField] private FlightGenerationModel generationMotionModel = FlightGenerationModel.JerkAssist;
        [SerializeField] private BeatFlightConstraints generationConstraints = default;
        [SerializeField] private bool enforceVFinalRange = true;
        [SerializeField] private Vector3 targetFinalVelocity = Vector3.zero;
        [SerializeField] private float maxGeneratedInitialAcceleration = 95f;
        [SerializeField] private float maxGeneratedJerk = 240f;
        [SerializeField] private bool logSelectionSummary = true;

        [Header("Controller Sync")]
        [SerializeField] private bool updateGameplayTimingFromALD = true;
        [SerializeField] private float travelTimeBias = 0.06f;

        [Header("Warmup Start")]
        [SerializeField] private bool insertWarmupRing = true;
        [SerializeField] private float warmupLeadSeconds = 0.55f;
        [SerializeField] private float warmupToFirstRingSpacing = 4.8f;
        [SerializeField] private bool alignFirstMusicalRingToBeatOffset = true;
        [SerializeField] private float firstBeatExtraOffsetSeconds = 0f;

        private struct PlannedRing
        {
            public int noteIndex;
            public float hitTime;
            public Vector3 localPos;
        }

        private void Awake()
        {
            if (!buildOnAwake)
            {
                return;
            }

            BuildLevelFromALD();
        }

        [ContextMenu("Generate Level")]
        public void BuildLevelFromALD()
        {
            ResolveReferences();
            if (!TryBuildRingPlan(out List<PlannedRing> plan, out float avgTravelTime))
            {
                return;
            }

            if (clearExistingRings && ringChainRoot != null)
            {
                ClearChildren(ringChainRoot);
            }

            float ringScale = ComputeRingScaleForAspect(0f);
            for (int i = 0; i < plan.Count; i++)
            {
                CreateRing(i, plan[i].localPos.x, plan[i].localPos.y, ringScale);
            }

            float travelTime = Mathf.Max(0.12f, avgTravelTime + travelTimeBias);
            List<float> selectedHitTimes = new List<float>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                selectedHitTimes.Add(plan[i].hitTime);
            }

            if (gameplayController != null)
            {
                gameplayController.ConfigureFromALDSelection(
                    levelAsset,
                    updateGameplayTimingFromALD ? travelTime : -1f,
                    selectedHitTimes);
                gameplayController.SetStartPose(new Vector3(0f, 1f, 0f));
                gameplayController.ReinitializeLevelFromRingChain();
            }

            if (logSelectionSummary)
            {
                Debug.Log(
                    "[ALDLevelBuilder] Generated rings=" + plan.Count +
                    " avgTravel=" + avgTravelTime.ToString("F3") + "s",
                    this);
            }
        }

        public List<float> BuildSelectedHitTimesOnly()
        {
            ResolveReferences();
            if (!TryBuildRingPlan(out List<PlannedRing> plan, out _))
            {
                return null;
            }

            List<float> selected = new List<float>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                selected.Add(plan[i].hitTime);
            }

            return selected;
        }

        public void GenerateLevel()
        {
            BuildLevelFromALD();
        }

        private bool TryBuildRingPlan(out List<PlannedRing> plan, out float avgTravelTime)
        {
            plan = null;
            avgTravelTime = 0f;
            ResolveReferences();
            EnsureConstraintsDefaults();

            if (levelAsset == null || ringChainRoot == null || ringPrefab == null)
            {
                return false;
            }

            IReadOnlyList<ALDNoteEvent> notes = levelAsset.Notes;
            if (notes == null || notes.Count == 0)
            {
                return false;
            }

            List<int> baseCandidates = SelectBaseNotes(notes, Mathf.Max(1, maxRings * 6), Mathf.Max(0.01f, minNoteGapSeconds));
            if (baseCandidates.Count == 0)
            {
                return false;
            }

            float runMaxBeatGap = ComputeAdaptiveMaxBeatGapSeconds(notes, baseCandidates);
            generationConstraints.maxTravelTimeSeconds = Mathf.Max(generationConstraints.maxTravelTimeSeconds, runMaxBeatGap * 1.1f);
            generationConstraints.Clamp();

            float shift = ComputeGlobalHitTimeShift(notes, baseCandidates[0]);
            List<PlannedRing> built = new List<PlannedRing>(Mathf.Max(8, maxRings + 2));

            Vector3 previousPos = new Vector3(0f, startY, 0f);
            float previousHitTime = insertWarmupRing ? -Mathf.Max(0.05f, warmupLeadSeconds) : notes[baseCandidates[0]].hitTimeSeconds + shift;
            float previousX = 0f;
            int searchStart = 0;

            if (insertWarmupRing)
            {
                built.Add(new PlannedRing
                {
                    noteIndex = -1,
                    hitTime = previousHitTime,
                    localPos = previousPos
                });
                previousPos.y += Mathf.Max(0.5f, warmupToFirstRingSpacing);
            }
            else
            {
                int firstIdx = baseCandidates[0];
                ALDNoteEvent firstNote = notes[firstIdx];
                int laneCount = GetLaneCountAtTime(firstNote.hitTimeSeconds);
                int laneIndex = Mathf.Clamp(firstNote.laneIndex, 0, Mathf.Max(0, laneCount - 1));
                float x = ComputeLaneX(laneCount, laneIndex, 0f);
                previousPos = new Vector3(x, startY, 0f);
                previousX = x;
                built.Add(new PlannedRing
                {
                    noteIndex = firstIdx,
                    hitTime = previousHitTime,
                    localPos = previousPos
                });
                searchStart = 1;
            }

            float travelAccum = 0f;
            int travelCount = 0;
            int maxPlayableRings = Mathf.Max(1, maxRings);
            int safetyCounter = 0;

            while (built.Count < maxPlayableRings + (insertWarmupRing ? 1 : 0))
            {
                if (safetyCounter++ > baseCandidates.Count * 2)
                {
                    break;
                }

                if (!TryPickNextRing(
                    notes,
                    baseCandidates,
                    shift,
                    searchStart,
                    previousPos,
                    previousX,
                    previousHitTime,
                    runMaxBeatGap,
                    out PlannedRing picked,
                    out int pickedCandidateOffset))
                {
                    break;
                }

                built.Add(picked);
                searchStart = pickedCandidateOffset + 1;

                float dt = Mathf.Max(0.01f, picked.hitTime - previousHitTime);
                travelAccum += dt;
                travelCount++;

                previousPos = picked.localPos;
                previousX = picked.localPos.x;
                previousHitTime = picked.hitTime;
            }

            if (built.Count <= (insertWarmupRing ? 1 : 0))
            {
                return false;
            }

            avgTravelTime = travelCount > 0 ? (travelAccum / travelCount) : 0.6f;
            plan = built;
            return true;
        }

        private bool TryPickNextRing(
            IReadOnlyList<ALDNoteEvent> notes,
            List<int> baseCandidates,
            float timeShift,
            int searchStart,
            Vector3 previousPos,
            float previousX,
            float previousHitTime,
            float runMaxBeatGap,
            out PlannedRing picked,
            out int pickedCandidateOffset)
        {
            picked = default;
            pickedCandidateOffset = -1;
            if (searchStart >= baseCandidates.Count)
            {
                return false;
            }

            int window = Mathf.Clamp(candidateSearchWindow, 1, 128);
            int end = Mathf.Min(baseCandidates.Count, searchStart + window);
            float bestScore = float.MaxValue;
            bool found = false;

            for (int candidateOffset = searchStart; candidateOffset < end; candidateOffset++)
            {
                int noteIndex = baseCandidates[candidateOffset];
                ALDNoteEvent note = notes[noteIndex];
                float hitTime = note.hitTimeSeconds + timeShift;
                float dt = hitTime - previousHitTime;
                if (dt < minBeatGapSeconds)
                {
                    continue;
                }

                if (dt > runMaxBeatGap)
                {
                    break;
                }

                int laneCount = GetLaneCountAtTime(note.hitTimeSeconds);
                int laneIndex = Mathf.Clamp(note.laneIndex, 0, Mathf.Max(0, laneCount - 1));
                float x = ComputeLaneX(laneCount, laneIndex, 0f);
                float sectionSpacing = GetSectionSpacingAtTime(note.hitTimeSeconds);
                float step = ComputeVerticalStep(dt, sectionSpacing, x, previousX);
                float y = previousPos.y + Mathf.Clamp(step, minSpacing, maxSpacing);
                Vector3 candidatePos = new Vector3(x, y, 0f);

                if (!TryEvaluateCandidateMotion(
                    previousPos,
                    candidatePos,
                    dt,
                    out Vector3 launchVelocity,
                    out Vector3 finalVelocity,
                    out float initialAccelerationMagnitude,
                    out float jerkMagnitude))
                {
                    continue;
                }

                float v0Speed = launchVelocity.magnitude;
                float vfSpeed = finalVelocity.magnitude;
                if (v0Speed < generationConstraints.minInitialSpeed || v0Speed > generationConstraints.maxInitialSpeed)
                {
                    continue;
                }

                if (enforceVFinalRange && (vfSpeed < generationConstraints.minFinalSpeed || vfSpeed > generationConstraints.maxFinalSpeed))
                {
                    continue;
                }

                if (generationMotionModel == FlightGenerationModel.JerkAssist &&
                    maxGeneratedInitialAcceleration > 0f &&
                    initialAccelerationMagnitude > maxGeneratedInitialAcceleration)
                {
                    continue;
                }

                if (generationMotionModel == FlightGenerationModel.JerkAssist &&
                    maxGeneratedJerk > 0f &&
                    jerkMagnitude > maxGeneratedJerk)
                {
                    continue;
                }

                float targetVf = 0.5f * (generationConstraints.minFinalSpeed + generationConstraints.maxFinalSpeed);
                float vfError = Mathf.Abs(vfSpeed - targetVf);
                float initialPressure = Mathf.InverseLerp(generationConstraints.minInitialSpeed, generationConstraints.maxInitialSpeed, v0Speed);
                float spacingPressure = Mathf.InverseLerp(minSpacing, maxSpacing, Mathf.Abs(y - previousPos.y));
                float accelerationPressure = maxGeneratedInitialAcceleration > 0f
                    ? Mathf.Clamp01(initialAccelerationMagnitude / maxGeneratedInitialAcceleration)
                    : 0f;
                float jerkPressure = maxGeneratedJerk > 0f
                    ? Mathf.Clamp01(jerkMagnitude / maxGeneratedJerk)
                    : 0f;
                float strengthBonus = Mathf.Clamp01(note.strength) * 0.22f;
                float score = vfError +
                              (initialPressure * 0.55f) +
                              (spacingPressure * 0.25f) +
                              (accelerationPressure * 0.25f) +
                              (jerkPressure * 0.18f) -
                              strengthBonus;

                if (!found || score < bestScore)
                {
                    found = true;
                    bestScore = score;
                    picked = new PlannedRing
                    {
                        noteIndex = noteIndex,
                        hitTime = hitTime,
                        localPos = candidatePos
                    };
                    pickedCandidateOffset = candidateOffset;
                }
            }

            if (found)
            {
                return true;
            }

            // Fallback: keep flow by taking the earliest beat in range even if not ideal.
            for (int candidateOffset = searchStart; candidateOffset < baseCandidates.Count; candidateOffset++)
            {
                int noteIndex = baseCandidates[candidateOffset];
                ALDNoteEvent note = notes[noteIndex];
                float hitTime = note.hitTimeSeconds + timeShift;
                float dt = hitTime - previousHitTime;
                if (dt < minBeatGapSeconds)
                {
                    continue;
                }
                if (dt > runMaxBeatGap)
                {
                    break;
                }

                int laneCount = GetLaneCountAtTime(note.hitTimeSeconds);
                int laneIndex = Mathf.Clamp(note.laneIndex, 0, Mathf.Max(0, laneCount - 1));
                float x = ComputeLaneX(laneCount, laneIndex, 0f);
                float sectionSpacing = GetSectionSpacingAtTime(note.hitTimeSeconds);
                float step = ComputeVerticalStep(dt, sectionSpacing, x, previousX);
                float y = previousPos.y + Mathf.Clamp(step, minSpacing, maxSpacing);
                picked = new PlannedRing
                {
                    noteIndex = noteIndex,
                    hitTime = hitTime,
                    localPos = new Vector3(x, y, 0f)
                };
                pickedCandidateOffset = candidateOffset;
                return true;
            }

            return false;
        }

        private bool TryEvaluateCandidateMotion(
            Vector3 previousPos,
            Vector3 candidatePos,
            float dt,
            out Vector3 launchVelocity,
            out Vector3 finalVelocity,
            out float initialAccelerationMagnitude,
            out float jerkMagnitude)
        {
            launchVelocity = Vector3.zero;
            finalVelocity = Vector3.zero;
            initialAccelerationMagnitude = 0f;
            jerkMagnitude = 0f;

            if (generationMotionModel == FlightGenerationModel.JerkAssist)
            {
                if (!BeatJerkFlightPlanner.TrySolveFinalAccelerationJerk(
                        previousPos,
                        candidatePos,
                        dt,
                        targetFinalVelocity,
                        Physics.gravity,
                        out launchVelocity,
                        out Vector3 initialAcceleration,
                        out Vector3 jerk))
                {
                    return false;
                }

                finalVelocity = targetFinalVelocity;
                initialAccelerationMagnitude = initialAcceleration.magnitude;
                jerkMagnitude = jerk.magnitude;
                return true;
            }

            if (!BeatFlightPlanner.TrySolveBallistic(
                    previousPos,
                    candidatePos,
                    dt,
                    Physics.gravity,
                    out launchVelocity,
                    out finalVelocity))
            {
                return false;
            }

            return true;
        }

        private float ComputeAdaptiveMaxBeatGapSeconds(IReadOnlyList<ALDNoteEvent> notes, List<int> baseCandidates)
        {
            float configured = Mathf.Max(minBeatGapSeconds + 0.01f, maxBeatGapSeconds);
            if (notes == null || baseCandidates == null || baseCandidates.Count < 2)
            {
                return configured;
            }

            List<float> gaps = new List<float>(Mathf.Min(128, baseCandidates.Count - 1));
            int gapCount = Mathf.Min(baseCandidates.Count - 1, 128);
            for (int i = 1; i <= gapCount; i++)
            {
                float dt = notes[baseCandidates[i]].hitTimeSeconds - notes[baseCandidates[i - 1]].hitTimeSeconds;
                if (dt > 0.001f)
                {
                    gaps.Add(dt);
                }
            }

            if (gaps.Count == 0)
            {
                return configured;
            }

            gaps.Sort();
            int p75Index = Mathf.Clamp(Mathf.FloorToInt((gaps.Count - 1) * 0.75f), 0, gaps.Count - 1);
            int p90Index = Mathf.Clamp(Mathf.FloorToInt((gaps.Count - 1) * 0.90f), 0, gaps.Count - 1);
            float p75Gap = gaps[p75Index];
            float p90Gap = gaps[p90Index];
            float openingGap = notes[baseCandidates[1]].hitTimeSeconds - notes[baseCandidates[0]].hitTimeSeconds;
            float adaptive = Mathf.Max(
                p75Gap * 1.35f,
                p90Gap * 1.18f,
                openingGap * 1.22f);
            adaptive = Mathf.Clamp(adaptive, minBeatGapSeconds + 0.05f, 6f);
            return Mathf.Max(configured, adaptive);
        }

        private float ComputeGlobalHitTimeShift(IReadOnlyList<ALDNoteEvent> notes, int firstNoteIndex)
        {
            if (!alignFirstMusicalRingToBeatOffset || firstNoteIndex < 0 || firstNoteIndex >= notes.Count)
            {
                return 0f;
            }

            float originalFirstTime = Mathf.Max(0f, notes[firstNoteIndex].hitTimeSeconds);
            float firstBeatTarget = Mathf.Max(0f, levelAsset.BeatPhaseOffsetSeconds + firstBeatExtraOffsetSeconds);
            return firstBeatTarget - originalFirstTime;
        }

        private List<int> SelectBaseNotes(IReadOnlyList<ALDNoteEvent> notes, int maxCount, float minGap)
        {
            List<int> outIdx = new List<int>(Mathf.Min(maxCount, notes.Count));
            float lastTime = float.NegativeInfinity;
            for (int i = 0; i < notes.Count; i++)
            {
                float t = notes[i].hitTimeSeconds;
                if (t - lastTime < minGap)
                {
                    continue;
                }

                outIdx.Add(i);
                lastTime = t;
                if (outIdx.Count >= maxCount)
                {
                    break;
                }
            }

            if (outIdx.Count == 0 && notes.Count > 0)
            {
                outIdx.Add(0);
            }

            return outIdx;
        }

        private float ComputeVerticalStep(float dtSeconds, float sectionSuggestedSpacing, float x, float previousX)
        {
            float clampedDt = Mathf.Max(minBeatGapSecondsForSpacing, dtSeconds);
            float spacingFromSection = Mathf.Max(0.2f, sectionSuggestedSpacing) * spacingMultiplier;
            float spacingFromTime = Mathf.Max(0.2f, clampedDt * verticalUnitsPerSecond * spacingMultiplier);
            float spacingFromBeatTime = Mathf.Max(0.2f, clampedDt * Mathf.Max(0.01f, beatTimeSpacingUnitsPerSecond));
            float mixedVertical = spacingStrictFromBeatTime
                ? spacingFromBeatTime
                : (spacingFromSection * Mathf.Clamp01(sectionSpacingWeight)) +
                  (spacingFromTime * Mathf.Clamp01(timingSpacingWeight));

            float desiredDistanceByTime = Mathf.Clamp(
                clampedDt * Mathf.Max(0.1f, timingDistanceUnitsPerSecond),
                minRingDistance3D,
                maxRingDistance3D);

            float desiredDistance3D = spacingStrictFromBeatTime
                ? Mathf.Max(mixedVertical, desiredDistanceByTime)
                : Mathf.Max(desiredDistanceByTime, mixedVertical);
            float dx = Mathf.Abs(x - previousX) * Mathf.Max(0f, lateralDistanceWeight);
            float dyFromDistance = Mathf.Sqrt(Mathf.Max(0.01f, (desiredDistance3D * desiredDistance3D) - (dx * dx)));

            if (dx > desiredDistance3D * 0.86f)
            {
                dyFromDistance += (dx - (desiredDistance3D * 0.86f)) * 0.55f;
            }

            float step = spacingStrictFromBeatTime
                ? Mathf.Max(spacingFromBeatTime, dyFromDistance)
                : Mathf.Max(mixedVertical, dyFromDistance);
            return Mathf.Max(0.2f, step);
        }

        private void ResolveReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (ringChainRoot == null)
            {
                GameObject chain = GameObject.Find("RingChain");
                if (chain != null)
                {
                    ringChainRoot = chain.transform;
                }
            }

            if (gameplayController == null)
            {
                gameplayController = Object.FindAnyObjectByType<GameSynthezerAndController>();
            }
        }

        private void EnsureConstraintsDefaults()
        {
            bool looksUninitialized =
                generationConstraints.maxInitialSpeed <= 0.2f &&
                generationConstraints.maxFinalSpeed <= 0.2f &&
                generationConstraints.maxTravelTimeSeconds <= 0.3f;
            if (looksUninitialized)
            {
                generationConstraints = BeatFlightConstraints.CreateDefault();
            }

            generationConstraints.Clamp();
            EnsureGeneratedFinalVelocityFitsConstraints();
            generationConstraints.minTravelTimeSeconds = Mathf.Max(generationConstraints.minTravelTimeSeconds, minBeatGapSeconds * 0.85f);
            generationConstraints.maxTravelTimeSeconds = Mathf.Min(generationConstraints.maxTravelTimeSeconds, maxBeatGapSeconds * 1.15f);
            generationConstraints.allowRingSkipRecovery = true;
            generationConstraints.Clamp();
            EnsureGeneratedFinalVelocityFitsConstraints();
        }

        private void EnsureGeneratedFinalVelocityFitsConstraints()
        {
            if (generationMotionModel != FlightGenerationModel.JerkAssist)
            {
                return;
            }

            float finalSpeed = targetFinalVelocity.magnitude;
            generationConstraints.minFinalSpeed = Mathf.Min(generationConstraints.minFinalSpeed, finalSpeed);
            generationConstraints.maxFinalSpeed = Mathf.Max(generationConstraints.maxFinalSpeed, finalSpeed + 0.05f);
            generationConstraints.Clamp();
        }

        private float GetSectionSpacingAtTime(float time)
        {
            IReadOnlyList<ALDSectionInfo> sections = levelAsset.Sections;
            if (sections == null || sections.Count == 0)
            {
                return 2.4f;
            }

            int idx = GetSectionIndexAtTime(time, sections);
            return Mathf.Max(0.5f, sections[idx].suggestedRingSpacing);
        }

        private int GetSectionIndexAtTime(float time, IReadOnlyList<ALDSectionInfo> sections)
        {
            int best = 0;
            float bestStart = float.MinValue;
            for (int i = 0; i < sections.Count; i++)
            {
                float s = sections[i].startTimeSeconds;
                if (s <= time && s >= bestStart)
                {
                    bestStart = s;
                    best = i;
                }
            }

            return best;
        }

        private int GetLaneCountAtTime(float time)
        {
            IReadOnlyList<ALDLevelCommand> commands = levelAsset.Commands;
            int laneCount = 1;
            if (commands == null || commands.Count == 0)
            {
                return laneCount;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                ALDLevelCommand cmd = commands[i];
                if (cmd.timeSeconds > time)
                {
                    break;
                }

                if (cmd.type == ALDCommandType.SetLaneCount)
                {
                    laneCount = Mathf.Max(1, cmd.laneCount);
                }
            }

            return laneCount;
        }

        private float ComputeLaneX(int laneCount, int laneIndex, float ringWorldZ)
        {
            if (laneCount <= 1)
            {
                return 0f;
            }

            float halfWidth = ComputeUsableHalfWidth(ringWorldZ);
            float t = laneIndex / Mathf.Max(1f, laneCount - 1f);
            return Mathf.Lerp(-halfWidth, halfWidth, t);
        }

        private float ComputeRingScaleForAspect(float ringWorldZ)
        {
            float halfWidth = ComputeUsableHalfWidth(ringWorldZ);
            float desiredRadius = Mathf.Max(1.05f, halfWidth * 0.66f);
            float s = desiredRadius / Mathf.Max(0.01f, ringBaseRadius);
            return Mathf.Clamp(s, minRingScale, maxRingScale);
        }

        private void CreateRing(int ringOrdinal, float x, float y, float scale)
        {
            GameObject ringObj = Instantiate(ringPrefab, ringChainRoot);
            ringObj.name = "LevelRing_" + ringOrdinal.ToString("00");
            ringObj.transform.localPosition = new Vector3(x, y, 0f);
            ringObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringObj.transform.localScale = Vector3.one * scale;
        }

        private float ComputeUsableHalfWidth(float ringWorldZ)
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                return 2f;
            }

            float distance = Mathf.Abs(cam.transform.position.z - ringWorldZ);
            float halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * cam.aspect;
            return Mathf.Max(0.5f, halfWidth * laneWidthMargin);
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(child.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureConstraintsDefaults();
            maxRings = Mathf.Max(1, maxRings);
            minNoteGapSeconds = Mathf.Max(0.01f, minNoteGapSeconds);
            minBeatGapSeconds = Mathf.Clamp(minBeatGapSeconds, 0.06f, 2f);
            maxBeatGapSeconds = Mathf.Max(minBeatGapSeconds + 0.01f, maxBeatGapSeconds);
            candidateSearchWindow = Mathf.Clamp(candidateSearchWindow, 1, 128);
            minSpacing = Mathf.Max(0.2f, minSpacing);
            maxSpacing = Mathf.Max(minSpacing + 0.1f, maxSpacing);
            beatTimeSpacingUnitsPerSecond = Mathf.Max(0.01f, beatTimeSpacingUnitsPerSecond);
            minBeatGapSecondsForSpacing = Mathf.Max(0.01f, minBeatGapSecondsForSpacing);
            travelTimeBias = Mathf.Clamp(travelTimeBias, -0.2f, 0.4f);
            warmupLeadSeconds = Mathf.Max(0.05f, warmupLeadSeconds);
            warmupToFirstRingSpacing = Mathf.Max(0.2f, warmupToFirstRingSpacing);
            maxGeneratedInitialAcceleration = Mathf.Max(0f, maxGeneratedInitialAcceleration);
            maxGeneratedJerk = Mathf.Max(0f, maxGeneratedJerk);
            generationConstraints.Clamp();
            EnsureGeneratedFinalVelocityFitsConstraints();
        }
#endif
    }
}
