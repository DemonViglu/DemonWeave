using System;
using System.Collections;
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
        public GameLoop Loop { get; private set; }
        public Scheduler Scheduler { get; private set; }
        public bool IsRunning { get; private set; }
        private bool stopping;
        private bool starting;
        private Coroutine postPhysics;

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
            Loop = new GameLoop();
            Scheduler = new Scheduler();
            Services.Add<IEventBus>(Events);
            Services.Add(Loop);
            Services.Add(Scheduler);
            starting = true;
            try
            {
                Configure(Services);
                Services.Freeze();
                StartServices();
                IsRunning = true;
                postPhysics = StartCoroutine(DrivePostPhysics());
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
            if (postPhysics != null) { StopCoroutine(postPhysics); postPhysics = null; }
            var errors = new List<Exception>();
            try
            {
                // Stop project activity while both services and events are still available.
                try { StopServices(); } catch (Exception error) { errors.Add(error); }
                try { Scheduler.Dispose(); } catch (Exception error) { errors.Add(error); }
                try { Loop.Dispose(); } catch (Exception error) { errors.Add(error); }
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
        protected void Update()
        {
            if (!IsRunning) return;
            var time = new FrameTime(Time.deltaTime, Time.unscaledDeltaTime);
            try { Scheduler.Tick(time); }
            catch (Exception error) { Debug.LogException(error, this); }
            Drive(LoopPhase.Update, time);
        }
        protected void FixedUpdate() => Drive(LoopPhase.Fixed,
            new FrameTime(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime));
        private IEnumerator DrivePostPhysics()
        {
            var wait = new WaitForFixedUpdate();
            while (IsRunning)
            {
                yield return wait;
                // Unity's automatic fixed-step physics has completed when this coroutine resumes.
                if (Physics.simulationMode == SimulationMode.FixedUpdate)
                    Drive(LoopPhase.PostPhysics, new FrameTime(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime));
            }
        }
        private void Drive(LoopPhase phase, FrameTime time)
        {
            if (!IsRunning) return;
            try { Loop.Tick(phase, time); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
        // Every project application root inherits queued dispatch; no queue component or registration is needed.
        protected void LateUpdate()
        {
            if (!IsRunning) return;
            Drive(LoopPhase.Late, new FrameTime(Time.deltaTime, Time.unscaledDeltaTime));
            if (!IsRunning) return;
            try { Events.Flush(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
        // Native providers finish OnApplicationQuit before Unity disables the root.
    }
}
