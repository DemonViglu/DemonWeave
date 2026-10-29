using System;
using UnityEngine;

namespace DemonViglu.Startup.Unity
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class AppHost : MonoBehaviour
    {
        [SerializeField] private AppComposition composition;
        [SerializeField] private AppLoopDriver driver;
        private static AppHost current;
        public static AppHost Current => current;
        public Kernel Kernel { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            // Handles disabled domain reload. Scene reload remains required (documented).
            if (current != null) current.StopApplication();
            current = null;
        }

        private void Awake()
        {
            if (current != null && current != this)
            {
                Debug.LogError("[Startup] Duplicate AppHost rejected; existing application retained.", this);
                enabled = false;
                return;
            }
            if (composition == null || composition.gameObject != gameObject || transform.parent != null ||
                (composition.UsePlayerLoop && (driver == null || driver.gameObject != gameObject || !driver.enabled || !driver.gameObject.activeInHierarchy)))
            {
                Debug.LogError("[Startup] Bind Composition on the root; opt-in PlayerLoop requires an enabled Driver on the same root.", this);
                enabled = false;
                return;
            }
            current = this;
            DontDestroyOnLoad(gameObject);
            try
            {
                Kernel = new Kernel(composition.CreateFeatures(), composition.Configure,
                    new KernelLog(sink: new UnityKernelLogSink(this)), composition.PeriodicBudget);
                Kernel.Start();
                if (composition.UsePlayerLoop) driver.Install(this);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                StopApplication();
                enabled = false;
            }
        }

        internal void Drive(LoopPhase phase, float delta, float unscaled)
        {
            if (current != this || Kernel == null) return;
            try { Kernel.Tick(phase, new FrameTime(delta, unscaled)); }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                StopApplication();
                enabled = false;
            }
        }

        public void StopApplication()
        {
            if (current == this) driver?.Uninstall();
            try { Kernel?.Dispose(); }
            catch (Exception e) { Debug.LogException(e, this); }
            finally { if (current == this) current = null; }
        }

        private void OnDisable() { if (current == this) StopApplication(); }
        private void OnDestroy() { if (current == this) StopApplication(); }
        // Keep services alive throughout OnApplicationQuit: native providers (including
        // network transports) release their objects and publish final events there.
        // Unity then disables this host; OnDisable performs the same idempotent cleanup.
    }
}
