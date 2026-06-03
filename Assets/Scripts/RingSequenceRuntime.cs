using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Runtime container for ring-chain ordering and target indexing.
    /// </summary>
    public sealed class RingSequenceRuntime
    {
        private readonly List<Transform> _rings = new List<Transform>(128);

        public int CurrentIndex { get; private set; }
        public int Count => _rings.Count;
        public bool IsCompleted => Count > 0 && CurrentIndex >= Count - 1;
        public IReadOnlyList<Transform> Rings => _rings;

        public Transform CurrentRing
        {
            get
            {
                if (_rings.Count == 0)
                {
                    return null;
                }

                int clamped = Mathf.Clamp(CurrentIndex, 0, _rings.Count - 1);
                return _rings[clamped];
            }
        }

        public void Rebuild(Transform ringChainRoot, Transform fallbackRing, bool sortByHeight)
        {
            _rings.Clear();
            CurrentIndex = 0;

            if (ringChainRoot != null)
            {
                for (int i = 0; i < ringChainRoot.childCount; i++)
                {
                    Transform child = ringChainRoot.GetChild(i);
                    if (child != null)
                    {
                        _rings.Add(child);
                    }
                }
            }

            if (sortByHeight)
            {
                _rings.Sort((a, b) =>
                {
                    if (ReferenceEquals(a, b))
                    {
                        return 0;
                    }
                    if (a == null)
                    {
                        return 1;
                    }
                    if (b == null)
                    {
                        return -1;
                    }
                    return a.position.y.CompareTo(b.position.y);
                });
            }

            if (_rings.Count == 0 && fallbackRing != null)
            {
                _rings.Add(fallbackRing);
            }
        }

        public void SetCurrentIndex(int ringIndex)
        {
            if (_rings.Count == 0)
            {
                CurrentIndex = 0;
                return;
            }

            CurrentIndex = Mathf.Clamp(ringIndex, 0, _rings.Count - 1);
        }

        public bool TryGetRing(int ringIndex, out Transform ring)
        {
            ring = null;
            if (ringIndex < 0 || ringIndex >= _rings.Count)
            {
                return false;
            }

            ring = _rings[ringIndex];
            return ring != null;
        }

        public bool TryGetNext(out int ringIndex, out Transform ring)
        {
            ringIndex = Mathf.Min(CurrentIndex + 1, Mathf.Max(0, _rings.Count - 1));
            if (ringIndex == CurrentIndex || ringIndex < 0 || ringIndex >= _rings.Count)
            {
                ring = null;
                return false;
            }

            ring = _rings[ringIndex];
            return ring != null;
        }

        public bool Advance()
        {
            if (_rings.Count == 0 || CurrentIndex >= _rings.Count - 1)
            {
                return false;
            }

            CurrentIndex++;
            return true;
        }
    }
}
