#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace DropBeat.EditorTools
{
    /// <summary>
    /// iPhones with ProMotion only allow more than 60 fps when the app opts in with
    /// CADisableMinimumFrameDurationOnPhone; without it the game would stay capped at 60.
    /// </summary>
    internal static class IosBuildPostprocess
    {
        [PostProcessBuild(100)]
        private static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("CADisableMinimumFrameDurationOnPhone", true);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
