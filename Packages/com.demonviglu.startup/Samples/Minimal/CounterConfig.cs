using UnityEngine;

namespace DemonViglu.Startup.Sample
{
    [CreateAssetMenu(menuName = "Startup/Sample Counter Config")]
    public sealed class CounterConfig : ScriptableObject
    {
        [Min(0.01f)] public float Interval = 0.25f;
    }
}
