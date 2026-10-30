using UnityEngine;

namespace DemonWeave.World
{
    [RequireComponent(typeof(CombatActor))]
    public sealed class CombatActorVisual : MonoBehaviour
    {
        private CombatActor actor;
        private float flashUntil;
        private bool flashing;
        private void Awake() { actor = GetComponent<CombatActor>(); actor.Died += OnDeath; actor.HealthChanged += OnHit; }
        private void OnHit(CombatActor source)
        {
            if (source.IsDead) return;
            flashUntil = Time.unscaledTime + .12f; flashing = true;
            foreach (var renderer in GetComponentsInChildren<Renderer>()) { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); block.SetColor("_Color", Color.white); renderer.SetPropertyBlock(block); }
        }
        private void Update()
        {
            if (!flashing || Time.unscaledTime < flashUntil || actor.IsDead) return;
            flashing = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(null);
        }
        private void OnDeath(CombatActor source)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                block.SetColor("_Color", new Color(.22f, .23f, .25f)); renderer.SetPropertyBlock(block);
            }
            if (!GetComponent<CharacterController>()) { transform.rotation = Quaternion.Euler(0, 0, 90); foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false; }
        }
        private void OnDestroy() { if (actor) { actor.Died -= OnDeath; actor.HealthChanged -= OnHit; } }
    }
}
