using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PlayerTrigger : MonoBehaviour
    {
        public SimplePlayerMover Player;
        public event Action Entered;
        public event Action Left;
        private readonly HashSet<Collider> contacts = new HashSet<Collider>();
        public bool IsInside => contacts.Count > 0;
        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;
        private void OnTriggerEnter(Collider other)
        {
            if (!Player || other.GetComponentInParent<SimplePlayerMover>() != Player) return;
            if (contacts.Add(other) && contacts.Count == 1) Entered?.Invoke();
        }
        private void OnTriggerExit(Collider other)
        {
            if (contacts.Remove(other) && contacts.Count == 0) Left?.Invoke();
        }
        private void OnDisable() => contacts.Clear();
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
