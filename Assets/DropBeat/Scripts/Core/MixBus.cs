using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Master mix helpers. Ducks the music for a moment under each hit so the hit cuts through, and
    /// soft-clips the final output so louder hits and layers never clip. Sits on the AudioListener.
    /// The duck only changes the music source's volume; nothing here moves any sound in time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MixBus : MonoBehaviour
    {
        private static double _at = -10.0;
        private static float _depth;
        private static float _tau = 0.14f;
        private static double _last = -10.0;

        /// <summary>Dips the music by <paramref name="db"/>, recovering with time constant <paramref name="tau"/> seconds.</summary>
        public static void Duck(float db, float tau = 0.14f)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _last < 0.12) db = Mathf.Min(db, 2.5f); // fast streams of hits: no pumping
            _last = now;
            float g = Mathf.Pow(10f, -db / 20f);
            if (g >= MusicDuck) return; // a deeper duck is already running
            _at = now;
            _depth = 1f - g;
            _tau = Mathf.Max(0.01f, tau);
        }

        /// <summary>Current music gain from ducking (1 = no duck).</summary>
        public static float MusicDuck
        {
            get
            {
                float t = (float)(Time.realtimeSinceStartupAsDouble - _at);
                float env = t < 0.02f ? 1f : Mathf.Exp(-(t - 0.02f) / _tau);
                return 1f - _depth * env;
            }
        }

        public static void Reset()
        {
            _at = -10.0;
            _depth = 0f;
        }

        /// <summary>Makes sure the listener has a MixBus (scenes built before it existed).</summary>
        public static void Ensure()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && listener.GetComponent<MixBus>() == null) listener.gameObject.AddComponent<MixBus>();
        }

        // Audio thread: gentle soft-clip above 0.84, no lookahead (zero latency), no allocations.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            const float knee = 0.84f, room = 0.16f;
            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                float a = x < 0f ? -x : x;
                if (a <= knee) continue;
                float y = knee + room * (float)System.Math.Tanh((a - knee) / room);
                data[i] = x < 0f ? -y : y;
            }
        }
    }
}
