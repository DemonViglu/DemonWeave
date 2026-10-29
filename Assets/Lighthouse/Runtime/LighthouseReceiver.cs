using UnityEngine;

namespace DemonWeave.Lighthouse
{
    // Each interchangeable tower owns its internal object references. Quest binds the tower once.
    public sealed class LighthouseReceiver : MonoBehaviour
    {
        public Transform BatterySocket;
        public Light Lamp;
        public bool Ready => BatterySocket && Lamp;
    }
}
