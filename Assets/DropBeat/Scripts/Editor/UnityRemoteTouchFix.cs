using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DropBeat.EditorTools
{
    /// <summary>
    /// Input System 1.19 looks for <c>UnityEditor.Remote.GenericRemote</c> in the core editor assembly, but
    /// Unity 6000.5 moved it to <c>UnityEditor.GenericRemoteModule</c>, so Unity Remote touches never reach
    /// the Input System. This hooks the Input System's own remote handler up to the moved class.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityRemoteTouchFix
    {
        private static Func<IntPtr, bool> s_Handler;

        static UnityRemoteTouchFix()
        {
            Install();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Install();
            };
        }

        private static void Install()
        {
            Type remote = Type.GetType("UnityEditor.Remote.GenericRemote, UnityEditor.GenericRemoteModule");
            if (remote == null || typeof(EditorApplication).Assembly.GetType("UnityEditor.Remote.GenericRemote") != null)
                return; // Not moved (the Input System already handles it) or not present.

            Type support = Type.GetType("UnityEngine.InputSystem.UnityRemoteSupport, Unity.InputSystem");
            MethodInfo process = support?.GetMethod("ProcessMessageFromUnityRemote", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo add = remote.GetMethod("AddMessageHandler", BindingFlags.Public | BindingFlags.Static);
            MethodInfo removeHandler = remote.GetMethod("RemoveMessageHandler", BindingFlags.Public | BindingFlags.Static);
            if (process == null || add == null)
            {
                Debug.LogWarning("[DropBeat] Unity Remote touch fix: Input System remote API not found.");
                return;
            }

            if (s_Handler != null) removeHandler?.Invoke(null, new object[] { s_Handler });
            s_Handler = (Func<IntPtr, bool>)Delegate.CreateDelegate(typeof(Func<IntPtr, bool>), process);
            add.Invoke(null, new object[] { s_Handler });
        }
    }
}
