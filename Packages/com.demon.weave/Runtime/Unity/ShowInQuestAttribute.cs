using System;
namespace DemonWeave.Unity
{
    /// <summary>Display a public configuration field in the main node panel. Other fields appear in Advanced.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ShowInQuestAttribute : Attribute
    {
        // Required applies to object references. Unassigned optional references become null.
        public bool Required { get; set; } = true;
        public bool Advanced { get; set; }
    }
}
