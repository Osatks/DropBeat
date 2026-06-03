using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    [CreateAssetMenu(menuName = "BeatDrop/Note Chart", fileName = "NoteChart")]
    public sealed class NoteChart : ScriptableObject
    {
        [SerializeField] private List<NoteData> notes = new List<NoteData>(256);

        public IReadOnlyList<NoteData> Notes => notes;

        public int Count => notes.Count;

        public NoteData this[int index] => notes[index];

        /// <summary>
        /// Replaces chart data with the provided ordered notes.
        /// Primarily used by editor tooling (ALD generator).
        /// </summary>
        public void ReplaceNotes(IReadOnlyList<NoteData> newNotes)
        {
            notes.Clear();
            if (newNotes == null)
            {
                return;
            }

            for (int i = 0; i < newNotes.Count; i++)
            {
                notes.Add(newNotes[i]);
            }
        }
    }
}
