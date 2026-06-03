using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Neon background FX controller:
    /// - Animates a synthwave waveform line.
    /// - Pulses grid emission with beat + lightweight spectrum energy.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BeatDropNeonWaveform : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Renderer[] gridRenderers;
        [SerializeField] private bool hideUntilAudioStarts = true;

        [Header("Wave Shape")]
        [SerializeField] private int sampleCount = 144;
        [SerializeField] private float width = 21f;
        [SerializeField] private float baseAmplitude = 1.15f;
        [SerializeField] private float frequency = 0.14f;
        [SerializeField] private float scrollSpeed = 0.22f;

        [Header("Glow")]
        [SerializeField] private Color waveColor = new Color(1f, 0.06f, 0.73f, 1f);
        [SerializeField] private float lineWidth = 0.14f;
        [SerializeField] private float emissionBase = 1.2f;
        [SerializeField] private float emissionBeatBoost = 1.8f;
        [SerializeField] private float ambientWaveGlow = 0.85f;

        [Header("Audio Spectrum")]
        [SerializeField] private int spectrumBins = 64;
        [SerializeField] private FFTWindow spectrumWindow = FFTWindow.BlackmanHarris;
        [SerializeField] private float spectrumGain = 30f;
        [SerializeField] private float spectrumSmoothing = 6f;

        private const float Tau = 6.28318530718f;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private LineRenderer _line;
        private Vector3[] _points;
        private float[] _spectrum;
        private MaterialPropertyBlock _mpb;
        private float _smoothedEnergy;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _mpb = new MaterialPropertyBlock();

            sampleCount = Mathf.Max(32, sampleCount);
            spectrumBins = Mathf.Clamp(spectrumBins, 32, 512);

            _line.positionCount = sampleCount;
            _line.useWorldSpace = false;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth;
            _line.enabled = !hideUntilAudioStarts;

            _points = new Vector3[sampleCount];
            _spectrum = new float[spectrumBins];

            if (audioSource == null)
            {
                DspBeatSync sync = beatSync;
                if (sync != null)
                {
                    audioSource = sync.GetComponent<AudioSource>();
                }
            }

            if (beatSync == null)
            {
                beatSync = Object.FindAnyObjectByType<DspBeatSync>();
                if (beatSync != null && audioSource == null)
                {
                    audioSource = beatSync.GetComponent<AudioSource>();
                }
            }

            if ((gridRenderers == null || gridRenderers.Length == 0) && transform.parent != null)
            {
                var list = new List<Renderer>(64);
                Renderer[] found = transform.parent.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < found.Length; i++)
                {
                    Renderer r = found[i];
                    if (r != null && r.gameObject != gameObject)
                    {
                        list.Add(r);
                    }
                }
                gridRenderers = list.ToArray();
            }

            if (gridRenderers == null || gridRenderers.Length == 0)
            {
                GridPulseOnlyController grid = Object.FindAnyObjectByType<GridPulseOnlyController>();
                if (grid != null)
                {
                    Renderer[] found = grid.GetComponentsInChildren<Renderer>(true);
                    if (found != null && found.Length > 0)
                    {
                        gridRenderers = found;
                    }
                }
            }
        }

        private void Update()
        {
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            bool audioActive = audioSource != null && audioSource.isPlaying;
            if (hideUntilAudioStarts && !audioActive)
            {
                if (_line.enabled)
                {
                    _line.enabled = false;
                }
                PulseGridIdle();
                return;
            }

            if (!_line.enabled)
            {
                _line.enabled = true;
            }

            float phase = beatSync != null ? beatSync.NormalizedBeatPhase : 0f;
            float beatPulse = 0.6f + 0.4f * (1f - Mathf.Abs(phase * 2f - 1f));
            float time = Time.time * scrollSpeed;

            float energy = SampleSpectrumEnergy();
            _smoothedEnergy = Mathf.Lerp(_smoothedEnergy, energy, Time.deltaTime * spectrumSmoothing);

            float step = width / (sampleCount - 1);
            float startX = -width * 0.5f;
            float amp = baseAmplitude * (0.65f + beatPulse * 0.55f + _smoothedEnergy * 0.65f);

            for (int i = 0; i < sampleCount; i++)
            {
                float x = startX + step * i;
                float s1 = Mathf.Sin(((x * frequency) + time) * Tau);
                float s2 = Mathf.Sin((((x * frequency) * 0.35f) - time * 0.6f) * Tau);
                _points[i] = new Vector3(x, (s1 * 0.72f + s2 * 0.28f) * amp, 0f);
            }

            _line.SetPositions(_points);

            float ambient = 0.5f + (Mathf.Sin(Time.time * 1.7f) * 0.5f);
            float emission = emissionBase + beatPulse * emissionBeatBoost + _smoothedEnergy * 1.8f + (ambient * ambientWaveGlow);
            _line.startColor = waveColor;
            _line.endColor = waveColor;
            _line.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColor, waveColor * emission);
            _mpb.SetColor(BaseColor, waveColor);
            _line.SetPropertyBlock(_mpb);

            PulseGrid(emission, beatPulse);
        }

        private float SampleSpectrumEnergy()
        {
            if (audioSource == null || !audioSource.isPlaying)
            {
                return 0f;
            }

            audioSource.GetSpectrumData(_spectrum, 0, spectrumWindow);

            float sum = 0f;
            // Focus low-mid bins for visible bass pulse behavior.
            int maxBin = Mathf.Min(_spectrum.Length, 24);
            for (int i = 2; i < maxBin; i++)
            {
                sum += _spectrum[i];
            }

            return Mathf.Clamp01(sum * spectrumGain);
        }

        private void PulseGrid(float waveEmission, float beatPulse)
        {
            if (gridRenderers == null || gridRenderers.Length == 0)
            {
                return;
            }
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            Color magenta = new Color(1f, 0.06f, 0.73f, 1f);
            float gridEmission = 0.35f + beatPulse * 0.95f + _smoothedEnergy * 1.65f;
            if (waveEmission > gridEmission)
            {
                gridEmission = waveEmission * 0.42f;
            }

            for (int i = 0; i < gridRenderers.Length; i++)
            {
                Renderer r = gridRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, magenta * gridEmission);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColor))
                {
                    _mpb.SetColor(BaseColor, magenta);
                }
                r.SetPropertyBlock(_mpb);
            }
        }

        private void PulseGridIdle()
        {
            if (gridRenderers == null || gridRenderers.Length == 0)
            {
                return;
            }
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            Color magenta = new Color(1f, 0.06f, 0.73f, 1f);
            float idleEmission = 0.24f;
            for (int i = 0; i < gridRenderers.Length; i++)
            {
                Renderer r = gridRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, magenta * idleEmission);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColor))
                {
                    _mpb.SetColor(BaseColor, magenta);
                }
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
