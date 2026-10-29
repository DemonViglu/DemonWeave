using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.SampleGameplay
{
    [CreateAssetMenu(menuName = "DemonWeave/Actions/Set signal light")]
    public sealed class SetSignalLight : WorldAction
    {
        public string TargetRole = "signal";
        [Range(0, 12)] public float Intensity = 5;
        public Color Color = new Color(1, 0.65f, 0.18f);
        public override string CapabilityId => "world.light.set";
        public override string Validate(QuestBindings bindings)
        {
            var target = bindings.Find(TargetRole);
            if (!target || !target.GetComponentInChildren<Light>(true)) return "light.target: role needs a Light component";
            if (float.IsNaN(Intensity) || float.IsInfinity(Intensity) || Intensity < 0 || Intensity > 12) return "light.intensity: expected 0..12";
            return "";
        }
        public override ActionResult Execute(QuestBindings bindings)
        {
            var target = bindings.Require(TargetRole);
            var light = target.GetComponentInChildren<Light>(true);
            light.color = Color; light.intensity = Intensity; light.enabled = Intensity > 0;
            var renderer = light.GetComponent<Renderer>();
            if (renderer)
            {
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                properties.SetColor("_Color", Color);
                properties.SetColor("_EmissionColor", Color * Intensity);
                renderer.SetPropertyBlock(properties);
            }
            return new ActionResult(true, $"{target.DisplayName} / intensity {Intensity:0.0}");
        }
    }
}
