using System;
using UnityEngine;

namespace DemonWeave.World
{
    public sealed class CombatActor : MonoBehaviour
    {
        public int MaxHealth = 3;
        public bool CanTakeDamage = true;
        public Guid RuntimeId { get; private set; }
        public int Health { get; private set; }
        public bool IsDead => Health <= 0;
        public event Action<CombatActor> Died;
        public event Action<CombatActor> Removed;
        public event Action<CombatActor> HealthChanged;
        private void Awake() { RuntimeId = Guid.NewGuid(); ResetHealth(); }
        public void ResetHealth() { Health = Mathf.Max(1, MaxHealth); HealthChanged?.Invoke(this); }
        public bool Damage(int amount)
        {
            if (!CanTakeDamage || IsDead || amount < 1) return false;
            Health = Mathf.Max(0, Health - amount);
            HealthChanged?.Invoke(this);
            if (Health == 0) Died?.Invoke(this);
            return true;
        }
        private void OnDestroy() { Removed?.Invoke(this); Died = null; Removed = null; }
    }
}
