using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Renders every ball skin, slowly spinning, into one RenderTexture (a 5 x 3 grid) for the locker
    /// panel's tiles. The preview spheres and camera sit far below the level on their own layer, and
    /// the camera only renders while the panel is open, so it costs nothing during play.
    /// </summary>
    public sealed class SkinPreview : MonoBehaviour
    {
        public const int Layer = 30;
        private const int Columns = 5, Rows = 3;
        private const int CellPixels = 256;
        private const float CellWorld = 1.25f;
        private static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);
        private static readonly int SkinId = Shader.PropertyToID("_Skin");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int SeamId = Shader.PropertyToID("_SeamColor");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");

        private Camera _cam;
        private Transform[] _balls;
        private MeshRenderer[] _renderers;
        private MaterialPropertyBlock _mpb;

        public RenderTexture Texture { get; private set; }

        public static SkinPreview Create(Material chrome, Mesh sphere)
        {
            var root = new GameObject("SkinPreview");
            var p = root.AddComponent<SkinPreview>();
            p.Build(chrome, sphere);
            return p;
        }

        private void Build(Material chrome, Mesh sphere)
        {
            _mpb = new MaterialPropertyBlock();
            Texture = new RenderTexture(Columns * CellPixels, Rows * CellPixels, 16, RenderTextureFormat.ARGB32)
            {
                name = "SkinPreviewRT",
                antiAliasing = 4,
            };
            Texture.Create();

            int n = BallStyles.SkinCount;
            _balls = new Transform[n];
            _renderers = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Preview" + i);
                go.layer = Layer;
                go.transform.SetParent(transform, false);
                go.transform.position = CellCenter(i);
                go.transform.localScale = Vector3.one * (CellWorld * 0.78f);
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                _renderers[i] = go.AddComponent<MeshRenderer>();
                _renderers[i].sharedMaterial = chrome;
                _renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderers[i].receiveShadows = false;
                _balls[i] = go.transform;
                go.transform.rotation = Quaternion.Euler(15f, i * 37f, 8f);
            }

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.position = Origin + new Vector3(0f, 0f, -10f);
            camGo.transform.rotation = Quaternion.identity;
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = Rows * CellWorld * 0.5f;
            _cam.aspect = Columns / (float)Rows;
            _cam.nearClipPlane = 1f;
            _cam.farClipPlane = 30f;
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.04f, 0.16f, 1f);
            _cam.targetTexture = Texture;
            _cam.allowHDR = false;
            _cam.enabled = false;

            // Keep the gameplay cameras from ever drawing the preview layer.
            foreach (var c in Camera.allCameras)
            {
                if (c != _cam) c.cullingMask &= ~(1 << Layer);
            }
        }

        private static Vector3 CellCenter(int i)
        {
            int col = i % Columns, row = i / Columns;
            return Origin + new Vector3((col - (Columns - 1) * 0.5f) * CellWorld, ((Rows - 1) * 0.5f - row) * CellWorld, 0f);
        }

        /// <summary>UV rectangle of a skin's cell in <see cref="Texture"/> (for a RawImage).</summary>
        public static Rect UvFor(BallSkin skin)
        {
            int i = (int)skin;
            int col = i % Columns, row = i / Columns;
            return new Rect(col / (float)Columns, 1f - (row + 1) / (float)Rows, 1f / Columns, 1f / Rows);
        }

        /// <summary>Renders only while the locker is showing balls.</summary>
        public bool Active
        {
            get => _cam != null && _cam.enabled;
            set { if (_cam != null) _cam.enabled = value; }
        }

        private void Update()
        {
            if (!Active) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _balls.Length; i++)
            {
                _balls[i].Rotate(new Vector3(0.25f, 1f, 0.1f), 40f * dt, Space.World);
                var skin = (BallSkin)i;
                _mpb.Clear();
                _mpb.SetFloat(SkinId, i);
                _mpb.SetColor(BaseColorId, BallView.SkinTint(skin));
                _mpb.SetColor(AccentId, new Color(0.2f, 2.0f, 2.6f, 1f));
                _mpb.SetColor(SeamId, new Color(0.15f, 1.5f, 2.0f, 1f));
                _mpb.SetColor(FlashId, Color.clear);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }

        private void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
