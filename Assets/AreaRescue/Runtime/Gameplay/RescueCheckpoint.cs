using System;
using System.Collections;
using DemonViglu.Startup.Unity;
using DemonWeave.Startup;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    [DefaultExecutionOrder(-500)]
    public sealed class RescueCheckpoint : MonoBehaviour
    {
        public AppHost Application;
        public QuestSceneHost Quest;
        public CombatActor Player;
        public Transform Gate;
        public RescueEntryZone Entry;
        public RescueEncounter[] Encounters;
        public Guid AttemptId { get; private set; } = Guid.NewGuid();
        public bool Recovering { get; private set; }
        public bool Stopped { get; private set; }
        public int RecoveryCount { get; private set; }
        public string Feedback { get; private set; } = "";
        private SafeCheckpoint checkpoint;
        private IDisposable stopping;
        private Coroutine recovery;
        private void Start()
        {
            if (Application && !Application.IsRunning) { Stopped = true; Quest.enabled = false; Debug.LogError("Startup application is not running.", this); return; }
            checkpoint = new SafeCheckpoint(Player.transform, Gate);
            if (Application) stopping = Application.Events.Subscribe<WeaveApplicationStopping>(_ => Stop());
        }
        public void Restore()
        {
            if (Stopped || Recovering || checkpoint == null) return;
            Recovering = true; RecoveryCount++; Feedback = "Returning to the courtyard checkpoint…";
            EndAttempt(); recovery = StartCoroutine(RestoreAfterCleanup());
        }
        public void WaitForRetry() { if (Stopped) return; EndAttempt(); Feedback = "Defeated. Select RETRY CHECKPOINT."; }
        private void EndAttempt()
        {
            AttemptId = Guid.NewGuid(); Quest.StopRun();
            foreach (var encounter in Encounters) if (encounter) { try { encounter.Clear(); } catch (Exception e) { Debug.LogException(e, this); } }
        }
        private IEnumerator RestoreAfterCleanup()
        {
            yield return null;
            if (Stopped) yield break;
            var controller = Player.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            checkpoint.Restore(); Player.ResetHealth(); Player.CanTakeDamage = true; Entry.ResetEntry();
            if (controller) controller.enabled = true;
            Physics.SyncTransforms(); Recovering = false; Feedback = ""; recovery = null; Quest.RestartRun();
        }
        private void Stop()
        {
            if (Stopped) return; Stopped = true;
            if (recovery != null) StopCoroutine(recovery); recovery = null; Recovering = false;
            if (Quest) EndAttempt();
        }
        private void OnDisable() { stopping?.Dispose(); stopping = null; Stop(); }
    }
}


