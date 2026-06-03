using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BeatDrop.EditorTools
{
    [CustomEditor(typeof(BeatDrop.ALDNeonOrbitLevelBuilder), true)]
    public sealed class ALDLevelBuilderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate Level", GUILayout.Width(160f), GUILayout.Height(28f)))
                {
                    BeatDrop.ALDNeonOrbitLevelBuilder builder = (BeatDrop.ALDNeonOrbitLevelBuilder)target;
                    if (builder != null)
                    {
                        Undo.RegisterCompleteObjectUndo(builder.gameObject, "Generate ALD Level");
                        builder.GenerateLevel();
                        EditorUtility.SetDirty(builder);
                        if (!Application.isPlaying)
                        {
                            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }
    }
}
