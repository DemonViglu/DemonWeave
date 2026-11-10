using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonViglu.Startup.Unity
{
    /// <summary>Derive once per project. Declare services in Configure, release gameplay in StopServices.</summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public abstract class AppHost : MonoBehaviour
    {
        public static AppHost Current { get; private set; }
        public ServiceRegistry Services { get; private set; }
        public EventBus Events { get; private set; }
        public bool IsRunning { get; private set; }
        private bool stopping;
        private bool starting;

        protected abstract void Configure(ServiceRegistry services);
        protected virtual void StartServices() { }
        protected virtual void StopServices() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            if (Current != null) Current.StopApplication();
            Current = null;
        }
        protected void Awake()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError("[Startup] Duplicate application root rejected.", this);
                enabled = false;
                return;
            }
            if (transform.parent != null)
            {
                Debug.LogError("[Startup] Application component must be on a scene root.", this);
                enabled = false;
                return;
            }
            Current = this;
            DontDestroyOnLoad(gameObject);
            Services = new ServiceRegistry();
            Events = new EventBus();
            Services.Add<IEventBus>(Events);
            starting = true;
            try
            {
                Configure(Services);
                Services.Freeze();
                StartServices();
                IsRunning = true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                starting = false;
                StopApplication();
                enabled = false;
            }
            finally { starting = false; }
        }
        public void StopApplication()
        {
            if (starting) throw new InvalidOperationException("Cannot stop during application startup.");
            if (stopping || Services == null || Services.IsDisposed) return;
            stopping = true;
            IsRunning = false;
            var errors = new List<Exception>();
            try
            {
                // Stop project activity while both services and events are still available.
                try { StopServices(); } catch (Exception error) { errors.Add(error); }
                try { Services.Dispose(); } catch (Exception error) { errors.Add(error); }
                try { Events.Dispose(); } catch (Exception error) { errors.Add(error); }
            }
            finally
            {
                if (Current == this) Current = null;
                stopping = false;
            }
            if (errors.Count != 0) Debug.LogException(new AggregateException("Application cleanup failed.", errors), this);
        }
        protected void OnDisable() { if (Current == this) StopApplication(); }
        protected void OnDestroy() { if (Current == this) StopApplication(); }
        // Every project application root inherits queued dispatch; no queue component or registration is needed.
        protected void LateUpdate()
        {
            if (!IsRunning) return;
            try { Events.Flush(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
        // Native providers finish OnApplicationQuit before Unity disables the root.
    }
}
