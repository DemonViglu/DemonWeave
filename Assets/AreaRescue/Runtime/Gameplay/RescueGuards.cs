using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public sealed class RescueGuards : MonoBehaviour
    {
        public RescueEncounter Encounter;
        public RescueCheckpoint Checkpoint;
        public float ApproachSpeed = 1.3f;
        public float AttackRange = 2;
        public float AttackInterval = 1.4f;
        private Guid attempt;
        private readonly Dictionary<Guid, float> nextHit = new Dictionary<Guid, float>();
        private void Update()
        {
            if (!Encounter || !Checkpoint || Checkpoint.Stopped || Checkpoint.Recovering || Encounter.Player.IsDead) return;
            if (attempt != Checkpoint.AttemptId) { attempt = Checkpoint.AttemptId; nextHit.Clear(); }
            if (Encounter.Spawner.Attempt?.IsReleased != true) return;
            foreach (var enemy in Encounter.Spawner.Members)
            {
                if (!enemy || enemy.IsDead) continue;
                var target = Encounter.Player.transform.position; target.y = enemy.transform.position.y;
                float distance = Vector3.Distance(enemy.transform.position, target);
                if (distance > AttackRange) enemy.transform.position = Vector3.MoveTowards(enemy.transform.position, target, ApproachSpeed * Time.deltaTime);
                else if (!nextHit.TryGetValue(enemy.RuntimeId, out var when) || Time.time >= when)
                {
                    nextHit[enemy.RuntimeId] = Time.time + AttackInterval;
                    Encounter.Player.Damage(1);
                    if (Checkpoint.Recovering) break; // Death invalidates/clears the current roster during this callback.
                }
            }
        }
    }
}

