using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Fixed-size object pool for mobile-friendly note lifecycle.
    /// </summary>
    public sealed class RhythmNotePool : MonoBehaviour
    {
        [SerializeField] private RhythmNote notePrefab;
        [SerializeField] private int initialSize = 64;

        private readonly Queue<RhythmNote> _available = new Queue<RhythmNote>(128);

        public void Initialize()
        {
            if (notePrefab == null)
            {
                Debug.LogError("RhythmNotePool: notePrefab is missing.", this);
                return;
            }

            for (int i = _available.Count; i < initialSize; i++)
            {
                RhythmNote note = Instantiate(notePrefab, transform);
                note.Deactivate();
                _available.Enqueue(note);
            }
        }

        public RhythmNote Rent()
        {
            if (_available.Count == 0)
            {
                RhythmNote extra = Instantiate(notePrefab, transform);
                extra.Deactivate();
                _available.Enqueue(extra);
            }

            return _available.Dequeue();
        }

        public void Return(RhythmNote note)
        {
            note.Deactivate();
            _available.Enqueue(note);
        }
    }
}
