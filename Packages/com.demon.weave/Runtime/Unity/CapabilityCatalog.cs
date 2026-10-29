using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DemonWeave.Unity
{
    public sealed class CapabilityDescriptor
    {
        public string Id { get; internal set; }
        public string Label { get; internal set; }
        public Type ParametersType { get; internal set; }
        public Type ImplementationType { get; internal set; }
        internal Func<string, string> Check;
        internal Func<string, ActionResult> Invoke;
        public string DefaultJson() => JsonConvert.SerializeObject(Activator.CreateInstance(ParametersType));
    }

    // Typed registration; parameter fields drive validation and author widgets. No quest IDs, opcodes or reflection invocation.
    public sealed class CapabilityCatalog
    {
        private readonly Dictionary<string, CapabilityDescriptor> entries = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
        public IReadOnlyList<CapabilityDescriptor> Entries => entries.Values.ToArray();
        public CapabilityDescriptor Find(string id) => id != null && entries.TryGetValue(id, out var entry) ? entry : null;
        public void Register<T>(string id, string label, Func<T, string> validate, Func<T, ActionResult> execute, Type implementationType = null) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(id) || validate == null || execute == null) throw new ArgumentException("Capability ID, validator and executor are required.");
            if (entries.ContainsKey(id)) throw new ArgumentException("Duplicate capability: " + id);
            entries.Add(id, new CapabilityDescriptor { Id = id, Label = label, ParametersType = typeof(T), ImplementationType = implementationType,
                Check = json => { try { return validate(Parse<T>(json)) ?? ""; } catch (Exception e) { return e.Message; } },
                Invoke = json => execute(Parse<T>(json)) });
        }
        public string Validate(string id, string json) => Find(id)?.Check(json) ?? "Unknown capability: " + id;
        public ActionResult Execute(string id, string json)
        {
            var entry = Find(id);
            if (entry == null) return new ActionResult(false, "Unknown capability: " + id);
            var error = entry.Check(json);
            if (!string.IsNullOrEmpty(error)) return new ActionResult(false, error);
            try { return entry.Invoke(json); } catch (Exception e) { return new ActionResult(false, e.Message); }
        }
        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Capability parameters must be a JSON object.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (token.Type != JTokenType.Object || reader.Read()) throw new ArgumentException("Expected exactly one parameter object.");
                var fields = typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var property in ((JObject)token).Properties())
                {
                    var field = fields.SingleOrDefault(f => f.Name == property.Name) ?? throw new ArgumentException("Unknown parameter: " + property.Name);
                    var type = field.FieldType; var kind = property.Value.Type;
                    bool wrong = type == typeof(string) ? kind != JTokenType.String : type == typeof(bool) ? kind != JTokenType.Boolean
                        : type == typeof(int) ? kind != JTokenType.Integer : type == typeof(float) || type == typeof(double) ? kind != JTokenType.Float && kind != JTokenType.Integer : false;
                    if (wrong) throw new ArgumentException(property.Name + ": expected " + type.Name + ", received " + kind);
                }
                return token.ToObject<T>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None,
                    MissingMemberHandling = MissingMemberHandling.Error })) ?? throw new ArgumentException("Null parameters.");
            }
        }
    }
}
