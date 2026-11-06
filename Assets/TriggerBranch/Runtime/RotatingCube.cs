using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    public sealed class RotatingCube : MonoBehaviour
    {
        public float DegreesPerSecond = 90;
        public bool IsRotating { get; private set; }
        public void StartRotating() => IsRotating = true;
        private void Update()
        {
            if (IsRotating) transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
