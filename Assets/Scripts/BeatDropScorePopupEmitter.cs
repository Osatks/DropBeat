using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Lightweight world-space score popups for arcade feedback.
    /// Uses 3D TextMesh to avoid UI canvas overhead in prototype scenes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropScorePopupEmitter : MonoBehaviour
    {
        [SerializeField] private BeatDropGameController gameController;
        [SerializeField] private Transform spawnAnchor;
        [SerializeField] private Color perfectColor = new Color(0.63f, 1f, 1f, 1f);
        [SerializeField] private Color goodColor = new Color(0.0f, 0.9f, 0.7f, 1f);
        [SerializeField] private float riseSpeed = 1.2f;
        [SerializeField] private float life = 0.75f;

        private void OnEnable()
        {
            if (gameController != null)
            {
                gameController.OnNoteJudged += HandleJudged;
            }
        }

        private void OnDisable()
        {
            if (gameController != null)
            {
                gameController.OnNoteJudged -= HandleJudged;
            }
        }

        private void HandleJudged(int scoreDelta, HitGrade grade, LaneId lane)
        {
            if (grade == HitGrade.Miss)
            {
                return;
            }

            Vector3 origin = spawnAnchor != null ? spawnAnchor.position : transform.position;
            origin += lane == LaneId.Left ? Vector3.left * 0.9f : Vector3.right * 0.9f;
            origin += Vector3.up * 0.2f;

            GameObject go = new GameObject("ScorePopup");
            go.transform.position = origin;

            TextMesh text = go.AddComponent<TextMesh>();
            text.text = "+" + scoreDelta;
            text.characterSize = 0.24f;
            text.fontSize = 80;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = grade == HitGrade.Perfect ? perfectColor : goodColor;

            var popup = go.AddComponent<PopupMotion>();
            popup.Initialize(life, riseSpeed);
        }

        private sealed class PopupMotion : MonoBehaviour
        {
            private float _life;
            private float _rise;
            private float _t;

            public void Initialize(float life, float rise)
            {
                _life = life;
                _rise = rise;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                transform.position += Vector3.up * (_rise * Time.deltaTime);

                if (_t >= _life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
