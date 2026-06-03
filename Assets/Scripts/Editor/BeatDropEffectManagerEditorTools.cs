using BeatDrop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BeatDrop.EditorTools
{
    internal static class BeatDropEffectManagerEditorTools
    {
        [MenuItem("BeatDrop/FX/Ensure Effect Manager In Scene")]
        private static void EnsureEffectManagerInScene()
        {
            BeatDropEffectManager manager = Object.FindAnyObjectByType<BeatDropEffectManager>();
            if (manager == null)
            {
                GameObject root = GameObject.Find("EffectManager");
                if (root == null)
                {
                    root = new GameObject("EffectManager");
                    Undo.RegisterCreatedObjectUndo(root, "Create EffectManager");
                }

                manager = root.GetComponent<BeatDropEffectManager>();
                if (manager == null)
                {
                    manager = Undo.AddComponent<BeatDropEffectManager>(root);
                }
            }

            manager.EnsureSineWaveChildNow();
            Selection.activeGameObject = manager.gameObject;
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
}
