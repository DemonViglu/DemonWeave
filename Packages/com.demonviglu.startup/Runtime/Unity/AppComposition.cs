using System.Collections.Generic;
using UnityEngine;

namespace DemonViglu.Startup.Unity
{
    /// <summary>Explicit Inspector reference on AppHost. Returns fresh features for every run.</summary>
    public abstract class AppComposition : MonoBehaviour
    {
        public virtual bool UsePlayerLoop => false;
        public virtual int PeriodicBudget => 128;
        public virtual IEnumerable<Feature> CreateFeatures() => System.Array.Empty<Feature>();
        public virtual void Configure(ServiceRegistry services) { }
    }
}
