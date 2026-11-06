using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    public sealed class RotatingCube : MonoBehaviour
    {
        public float DegreesPerSecond = 90;
        public bool IsRotating { get; private set; }
        private Quaternion initialRotation;
        private void Awake() => initialRotation = transform.localRotation;
        public void ResetRotation() { IsRotating = false; transform.localRotation = initialRotation; }
        public void StartRotating() => IsRotating = true;
        private void Update()
        {
            if (IsRotating) transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
