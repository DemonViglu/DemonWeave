using System;
using UnityEditor;
using UnityEngine;
namespace DemonViglu.Startup.Editor
{
    public static class StartupAcceptance
    {
        [MenuItem("Tools/Startup/Check Core Utilities")]
        public static void ValidateCore()
        {
            Debug.Log("STARTUP_CORE_PASS cases=" + CoreAcceptance.Run());
            Debug.Log("QUEUED_EVENTS_PASS cases=" + QueuedEventAcceptance.Run());
            Debug.Log("STARTUP_PERIODIC_PASS cases=" + PeriodicAcceptance.Run());
            Debug.Log("STARTUP_BINDING_PASS cases=" + BindingAcceptance.Run());
        }
        public static void RunBatch()
        {
            try { ValidateCore(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
