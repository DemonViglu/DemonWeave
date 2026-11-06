using System;
using UnityEngine;

namespace DemonWeave.World
{
    // A bounded, local pre-encounter checkpoint. The host cancels owned operations before restoring it.
    public sealed class SafeCheckpoint
    {
        private readonly Transform player, door;
        private readonly Vector3 playerPosition, doorPosition;
        private readonly Quaternion playerRotation, doorRotation;
        public SafeCheckpoint(Transform player, Transform door)
        {
            if (!player || !door) throw new ArgumentException("Checkpoint requires player and door.");
            this.player = player; this.door = door;
            playerPosition = player.position; playerRotation = player.rotation;
            doorPosition = door.localPosition; doorRotation = door.localRotation;
        }
        public void Restore()
        {
            if (!player || !door) throw new InvalidOperationException("Checkpoint target was destroyed.");
            player.SetPositionAndRotation(playerPosition, playerRotation);
            door.localPosition = doorPosition; door.localRotation = doorRotation;
        }
    }
}
