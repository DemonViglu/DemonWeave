using UnityEngine;

namespace DemonViglu.Startup.Unity
{
    public sealed class UnityKernelLogSink : IKernelLogSink
    {
        private readonly Object context;
        public UnityKernelLogSink(Object context) { this.context = context; }
        public void Write(KernelLogEntry entry)
        {
            string text = $"[Startup #{entry.Sequence} {entry.Owner}/{entry.Stage}] {entry.Message}";
            if (entry.Exception != null) text += "\n" + entry.Exception;
            if (entry.Level == KernelLogLevel.Error) Debug.LogError(text, context);
            else if (entry.Level == KernelLogLevel.Warning) Debug.LogWarning(text, context);
            else Debug.Log(text, context);
        }
    }
}
