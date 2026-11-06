using UnityEngine;
using UnityEngine.InputSystem;

namespace DemonWeave.TriggerBranch
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class SimplePlayerMover : MonoBehaviour
    {
        [Min(0)] public float Speed = 4f;
        private Rigidbody body;
        private void Awake() => body = GetComponent<Rigidbody>();
        private void FixedUpdate()
        {
            var keyboard = Keyboard.current;
            var direction = Vector3.zero;
            if (keyboard != null)
            {
                direction.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                direction.z = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            }
            var velocity = Vector3.ClampMagnitude(direction, 1) * Speed;
            velocity.y = body.linearVelocity.y;
            body.linearVelocity = velocity;
        }
    }
}
