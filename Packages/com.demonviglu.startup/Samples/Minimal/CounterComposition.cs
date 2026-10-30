using System;
using DemonViglu.Startup.Unity;
using UnityEngine;
namespace DemonViglu.Startup.Sample
{
    // AppHost owns the utilities. Projects only register callbacks and tasks.
    [DefaultExecutionOrder(-10000)]
    public sealed class CounterComposition : AppHost
    {
        [SerializeField] private CounterConfig config;
        private int updateCount, fixedCount, decisions;
        protected override void Configure(ServiceRegistry services)
        {
            Loop.Register(LoopPhase.Update, _ => updateCount++);
            Loop.Register(LoopPhase.Fixed, _ => fixedCount++);
            Scheduler.Repeat(config != null ? config.Interval : .25, _ => decisions++);
        }
        private void OnGUI() => GUILayout.Label($"Update: {updateCount}  Fixed: {fixedCount}  Decisions: {decisions}");
    }
}
