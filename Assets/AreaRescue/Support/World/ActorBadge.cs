using UnityEngine;

namespace DemonWeave.World
{
    public sealed class ActorBadge : MonoBehaviour
    {
        public string DisplayName;
        public TextMesh Label;
        private CombatActor actor;
        private Camera view;
        private void Start() { actor = GetComponent<CombatActor>(); view = Camera.main; }
        private void LateUpdate()
        {
            if (!Label) return;
            if (view) Label.transform.rotation = view.transform.rotation;
            Label.text = DisplayName + (actor ? "\n" + actor.Health + " / " + actor.MaxHealth : "");
            Label.gameObject.SetActive(!actor || !actor.IsDead);
        }
    }
}
