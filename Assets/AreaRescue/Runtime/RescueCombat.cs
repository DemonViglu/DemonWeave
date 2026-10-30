using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public sealed class RescueCombat : MonoBehaviour
    {
        public AreaRescueScenario Scenario;
        public float ApproachSpeed = 1.3f;
        public float AttackRange = 2;
        public float AttackInterval = 1.4f;
        private Guid attempt;
        private readonly Dictionary<Guid, float> nextHit = new Dictionary<Guid, float>();
        private void Update()
        {
            if (!Scenario || Scenario.Recovering || Scenario.Player.IsDead) return;
            if (attempt != Scenario.AttemptId) { attempt = Scenario.AttemptId; nextHit.Clear(); }
            if (Scenario.Spawner.Attempt?.IsReleased != true) return;
            foreach (var enemy in Scenario.Spawner.Members)
            {
                if (!enemy || enemy.IsDead) continue;
                var target = Scenario.Player.transform.position; target.y = enemy.transform.position.y;
                float distance = Vector3.Distance(enemy.transform.position, target);
                if (distance > AttackRange) enemy.transform.position = Vector3.MoveTowards(enemy.transform.position, target, ApproachSpeed * Time.deltaTime);
                else if (!nextHit.TryGetValue(enemy.RuntimeId, out var when) || Time.time >= when)
                {
                    nextHit[enemy.RuntimeId] = Time.time + AttackInterval;
                    Scenario.Player.Damage(1);
                    if (Scenario.Recovering) break; // Death invalidates/clears the current roster during this callback.
                }
            }
        }
    }
}
