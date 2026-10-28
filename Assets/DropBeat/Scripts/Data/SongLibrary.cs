using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    [CreateAssetMenu(menuName = "DropBeat/Song Library", fileName = "SongLibrary")]
    public sealed class SongLibrary : ScriptableObject
    {
        public List<SongData> songs = new List<SongData>();

        public int PlayableCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < songs.Count; i++) if (songs[i] != null && songs[i].IsAnalyzed && songs[i].clip != null) c++;
                return c;
            }
        }
    }
}
