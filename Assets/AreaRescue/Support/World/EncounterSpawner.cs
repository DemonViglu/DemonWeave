using System;
using System.Collections;
using System.Collections.Generic;
using DemonWeave.Encounters;
using UnityEngine;

namespace DemonWeave.World
{
    public sealed class EncounterSpawner : MonoBehaviour
    {
        public Transform[] SpawnPoints;
        public Transform CompanionPoint;
        private readonly List<CombatActor> members = new List<CombatActor>();
        private GameObject ownedRoot;
        private Coroutine preparation;
        private Action<CombatActor> death, removal;
        public EncounterAttempt Attempt { get; private set; }
        public IReadOnlyList<CombatActor> Members => members.AsReadOnly();
        public GameObject Companion { get; private set; }
        public string Error { get; private set; }
        public bool IsReady => Attempt != null && !Attempt.IsClosed && Attempt.IsSealed && Companion;
        public bool IsCleared => IsReady && Attempt.IsCleared;
        public event Action Changed;
        public void Prepare(EncounterDefinition definition, AttemptBindings bindings)
        {
            Cancel(); Error = "";
            if (!definition || bindings == null || bindings.IsClosed || !CompanionPoint) throw new ArgumentException("Bind definition, active context and companion point.");
            var error = definition.Validate(SpawnPoints?.Length ?? 0);
            if (error != "") throw new ArgumentException(error);
            foreach (var point in SpawnPoints) if (!point) throw new ArgumentException("Missing encounter spawn point.");
            // Snapshot author values before yielding; later author edits cannot change this spawn request.
            var enemy = definition.EnemyPrefab; var companion = definition.CompanionPrefab;
            var count = definition.EnemyCount; var role = definition.CompanionRole;
            var points = (Transform[])SpawnPoints.Clone(); var companionPoint = CompanionPoint;
            Attempt = new EncounterAttempt(count); var attempt = Attempt; var bindingId = bindings.Id;
            ownedRoot = new GameObject("Encounter " + attempt.Id); ownedRoot.transform.SetParent(transform, false);
            death = actor => { if (Attempt == attempt && attempt.ReportDeath(attempt.Id, actor.RuntimeId)) Changed?.Invoke(); };
            removal = actor => { if (Attempt == attempt && attempt.ReportUnavailable(attempt.Id, actor.RuntimeId)) Changed?.Invoke(); };
            preparation = StartCoroutine(Generate());
            IEnumerator Generate()
            {
                for (int i = 0; i < count; i++)
                {
                    if (Attempt != attempt || attempt.IsClosed || bindings.IsClosed) yield break;
                    try
                    {
                        if (!points[i]) throw new InvalidOperationException("Spawn point disappeared.");
                        var actor = Instantiate(enemy, points[i].position, points[i].rotation, ownedRoot.transform);
                        actor.CanTakeDamage = false; members.Add(actor); attempt.Register(actor.RuntimeId);
                        actor.Died += death; actor.Removed += removal;
                    }
                    catch (Exception exception) { Error = exception.Message; break; }
                    yield return null;
                }
                if (Attempt == attempt && !attempt.IsClosed && !bindings.IsClosed && string.IsNullOrEmpty(Error))
                {
                    try
                    {
                        if (!companionPoint) throw new InvalidOperationException("Companion spawn point disappeared.");
                        Companion = Instantiate(companion, companionPoint.position, companionPoint.rotation, ownedRoot.transform);
                        if (!bindings.BindGenerated(bindingId, role, Companion)) throw new InvalidOperationException("Binding attempt is obsolete.");
                        attempt.Seal();
                    }
                    catch (Exception exception) { Error = exception.Message; }
                }
                preparation = null; Changed?.Invoke();
            }
        }
        public bool Release()
        {
            if (!IsReady || !Attempt.Release()) return false;
            foreach (var actor in members) if (actor) actor.CanTakeDamage = true;
            Changed?.Invoke(); return true;
        }
        public void Cancel()
        {
            Attempt?.Dispose();
            if (preparation != null) StopCoroutine(preparation);
            preparation = null;
            foreach (var actor in members)
                if (actor) { actor.Died -= death; actor.Removed -= removal; actor.CanTakeDamage = false; }
            members.Clear(); Companion = null;
            if (ownedRoot) { ownedRoot.SetActive(false); Destroy(ownedRoot); }
            ownedRoot = null; death = null; removal = null; Attempt = null;
        }
        private void OnDisable() => Cancel();
    }
}
