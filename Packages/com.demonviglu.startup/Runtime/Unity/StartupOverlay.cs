using UnityEngine;

namespace DemonViglu.Startup.Unity
{
    /// <summary>Optional development diagnostics; never owns gameplay or input.</summary>
    public sealed class StartupOverlay : MonoBehaviour
    {
        [SerializeField] private AppHost host;
        private void OnGUI()
        {
            if (!Debug.isDebugBuild || host == null || host.Kernel == null) return;
            var kernel = host.Kernel;
            GUI.Box(new Rect(12, 12, 540, 158), "Startup 0.1");
            var latest = kernel.App.Log.Latest;
            GUI.Label(new Rect(24, 38, 516, 124),
                $"State: {kernel.State} | Features: {kernel.RunningFeatureCount}\n" +
                $"Services: {kernel.App.Services.Count} | Events: {kernel.App.Events.SubscriptionCount}\n" +
                $"Ticks: {kernel.App.Loop.Count} | Timers: {kernel.App.Scheduler.Count}\n" +
                $"Log: {kernel.App.Log.Count}/{kernel.App.Log.Capacity} | Sink errors: {kernel.App.Log.SinkFailures}\n" +
                (latest == null ? "" : $"#{latest.Sequence} {latest.Owner}/{latest.Stage}: {latest.Message}"));
        }
    }
}
