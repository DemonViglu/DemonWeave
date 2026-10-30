using UnityEngine;

namespace DemonViglu.Startup.Sample
{
    public sealed class NativeUpdateExample : MonoBehaviour
    {
        public int Updates { get; private set; }
        private void Update() { Updates++; transform.Rotate(0, 40 * Time.deltaTime, 0); }
    }
}
