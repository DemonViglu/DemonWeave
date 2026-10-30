using System;
namespace DemonViglu.Startup
{
    /// <summary>Optional module. The project explicitly calls Initialize and Dispose.</summary>
    public abstract class Feature : IDisposable
    {
        public abstract void Initialize(ServiceRegistry services);
        public abstract void Dispose();
    }
}
