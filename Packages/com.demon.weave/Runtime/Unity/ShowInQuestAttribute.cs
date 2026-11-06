using System;

namespace DemonWeave.Unity
{
    /// <summary>Expose a serialized configuration field in the Quest detail panel as well as the Inspector.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ShowInQuestAttribute : Attribute { }
}
