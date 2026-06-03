using System;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// One chart note event in absolute song-time seconds.
    /// </summary>
    [Serializable]
    public struct NoteData
    {
        [Min(0f)] public float hitTimeSeconds;
        public LaneId lane;
    }
}
