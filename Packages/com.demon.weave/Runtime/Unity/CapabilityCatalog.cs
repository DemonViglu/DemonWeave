using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DemonWeave.Unity
{
    public sealed class CapabilityArgument
    {
        public string Name { get; internal set; }
        public string Label { get; internal set; }
        public Type Type { get; internal set; }
        public object Default { get; internal set; }
        public bool EntityRole { get; internal set; }
    }
    public sealed class CapabilityDescriptor
    {
        public string Id { get; internal set; }
        public string Label { get; internal set; }
        public Type ImplementationType { get; internal set; }
        public string MethodName { get; internal set; }
        public IReadOnlyList<CapabilityArgument> Arguments { get; internal set; }
        public IReadOnlyList<string> TargetRoles { get; internal set; }
        internal Func<string, string> Check;
        internal Func<string, ActionResult> Invoke;
        public string DefaultJson(string targetRole = null)
        {
            var values = new JObject();
            foreach (var argument in Arguments) values[argument.Name] = argument.Default == null ? JValue.CreateNull() : JToken.FromObject(argument.Default);
            var target = targetRole ?? (TargetRoles.Count == 1 ? TargetRoles[0] : null);
            if (target != null) values[CapabilityCatalog.TargetKey] = target;
            return values.ToString(Formatting.None);
        }
    }

    // Only explicitly attributed instance methods on bound objects are callable.
    public sealed class CapabilityCatalog
    {
        public const string TargetKey = "$target";
        private readonly List<CapabilityDescriptor> entries = new List<CapabilityDescriptor>();
        public IReadOnlyList<CapabilityDescriptor> Entries => entries.ToArray();
        public CapabilityDescriptor Find(string id, string entityRole = null)
        {
            var candidates = entries.Where(e => e.Id == id && (entityRole == null || e.TargetRoles.Contains(entityRole))).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        public static CapabilityCatalog FromBindings(IReadOnlyList<EntityBinding> bindings)
        {
            var catalog = new CapabilityCatalog();
            var calls = QuestMethods.Scan(bindings, false).ToArray();
            foreach (var group in calls.GroupBy(c => new { c.Id, c.Method.Module, c.Method.MetadataToken }))
            {
                var targets = group.ToArray(); var method = targets[0].Method;
                var attribute = method.GetCustomAttribute<QuestActionAttribute>();
                var parameters = method.GetParameters();
                var arguments = parameters.Select(p => new CapabilityArgument {
                    Name = p.Name, Label = p.GetCustomAttribute<CapabilityParameterAttribute>()?.Label ?? p.Name,
                    Type = p.ParameterType, EntityRole = p.ParameterType == typeof(GameObject),
                    Default = p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(string) || p.ParameterType == typeof(GameObject) ? (object)"" : Activator.CreateInstance(p.ParameterType)
                }).ToArray();
                MethodInfo validator = null;
                if (!string.IsNullOrEmpty(attribute.Validator))
                {
                    validator = method.DeclaringType.GetMethod(attribute.Validator, BindingFlags.Instance | BindingFlags.Public, null, parameters.Select(p => p.ParameterType).ToArray(), null);
                    if (validator == null || validator.ReturnType != typeof(string)) throw new ArgumentException("Invalid validator for " + group.Key.Id);
                }
                (QuestMethods.Call call, object[] args) Decode(string json)
                {
                    var data = ReadObject(json);
                    foreach (var property in data.Properties())
                        if (property.Name != TargetKey && !arguments.Any(a => a.Name == property.Name)) throw new ArgumentException("Unknown parameter: " + property.Name);
                    var roleToken = data[TargetKey];
                    if (roleToken != null && roleToken.Type != JTokenType.String) throw new ArgumentException("Action target must be a scene binding.");
                    var role = roleToken?.Value<string>();
                    var candidates = targets.Where(t => (role == null || t.Role == role) && t.Component).ToArray();
                    var distinct = candidates.GroupBy(t => t.Component).Select(g => g.First()).ToArray();
                    if (distinct.Length != 1) throw new ArgumentException("Select one target object for action " + group.Key.Id);
                    var values = new object[arguments.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        var a = arguments[i]; var token = data[a.Name];
                        if (token == null) token = a.Default == null ? JValue.CreateNull() : JToken.FromObject(a.Default);
                        var type = a.Type;
                        bool valid = type == typeof(GameObject) || type == typeof(string) ? token.Type == JTokenType.String
                            : type == typeof(bool) ? token.Type == JTokenType.Boolean : type == typeof(int) ? token.Type == JTokenType.Integer
                            : token.Type == JTokenType.Float || token.Type == JTokenType.Integer;
                        if (!valid) throw new ArgumentException(a.Name + ": expected " + type.Name);
                        if (type == typeof(GameObject))
                        {
                            var objectRole = token.Value<string>(); var targetObject = bindings.FirstOrDefault(b => b.Role == objectRole)?.Target;
                            if (!targetObject) throw new ArgumentException(a.Name + ": select a scene object.");
                            values[i] = targetObject;
                        }
                        else values[i] = token.ToObject(type);
                        if (values[i] is float f && !float.IsFinite(f) || values[i] is double d && (double.IsInfinity(d) || double.IsNaN(d))) throw new ArgumentException(a.Name + ": number must be finite.");
                    }
                    return (distinct[0], values);
                }
                string Validate(string json)
                {
                    try { var decoded = Decode(json); return validator == null ? "" : (string)validator.Invoke(decoded.call.Component, decoded.args) ?? ""; }
                    catch (Exception e) { return QuestMethods.Message(e); }
                }
                catalog.entries.Add(new CapabilityDescriptor {
                    Id = group.Key.Id, Label = attribute.Label ?? method.Name, ImplementationType = method.DeclaringType, MethodName = method.Name,
                    Arguments = arguments, TargetRoles = targets.Select(t => t.Role).Distinct().ToArray(), Check = Validate,
                    Invoke = json => { var decoded = Decode(json); var result = method.Invoke(decoded.call.Component, decoded.args); return result is ActionResult action ? action : new ActionResult(true, method.Name + " returned."); }
                });
            }
            return catalog;
        }
        public string Validate(string id, string json)
        {
            try { var data = ReadObject(json); return Find(id, (string)data[TargetKey])?.Check(json) ?? "Action is missing or ambiguous on the selected Entity: " + id; }
            catch (Exception e) { return QuestMethods.Message(e); }
        }
        public ActionResult Execute(string id, string json)
        {
            var error = Validate(id, json); if (!string.IsNullOrEmpty(error)) return new ActionResult(false, error);
            try { return Find(id, (string)ReadObject(json)[TargetKey]).Invoke(json); }
            catch (Exception e) { return new ActionResult(false, QuestMethods.Message(e)); }
        }
        private static JObject ReadObject(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json ?? "")) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (!(token is JObject obj) || reader.Read()) throw new ArgumentException("Expected one parameter object.");
                return obj;
            }
        }
    }

    public static class QuestMethods
    {
        internal sealed class Call { public string Id, Role; public QuestEntity Component; public MethodInfo Method; }
        private static readonly Dictionary<Type, MethodInfo[]> methods = new Dictionary<Type, MethodInfo[]>();
        private static MethodInfo[] Methods(Type type)
        {
            if (methods.TryGetValue(type, out var cached)) return cached;
            var result = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.IsDefined(typeof(QuestConditionAttribute), true) || m.IsDefined(typeof(QuestActionAttribute), true)).ToArray();
            foreach (var method in result)
            {
                var condition = method.IsDefined(typeof(QuestConditionAttribute), true);
                var action = method.IsDefined(typeof(QuestActionAttribute), true);
                if (!method.IsPublic || method.IsStatic || condition && action || method.IsGenericMethod || method.IsDefined(typeof(AsyncStateMachineAttribute), false)) throw new ArgumentException("Quest methods must be public, non-generic instance methods with one Quest attribute (no async void): " + method);
                if (condition && (method.ReturnType != typeof(bool) && method.ReturnType != typeof(bool?) || method.GetParameters().Length != 0)) throw new ArgumentException("QuestCondition requires a parameterless bool/bool? method: " + method);
                if (action && method.ReturnType != typeof(void) && method.ReturnType != typeof(ActionResult)) throw new ArgumentException("QuestAction returns void or ActionResult; async work must expose completion as a condition: " + method);
                foreach (var p in method.GetParameters())
                    if (p.ParameterType != typeof(string) && p.ParameterType != typeof(int) && p.ParameterType != typeof(float) && p.ParameterType != typeof(double) && p.ParameterType != typeof(bool) && p.ParameterType != typeof(GameObject))
                        throw new ArgumentException("Unsupported Quest parameter: " + method.Name + "." + p.Name);
            }
            return methods[type] = result;
        }
        internal static IEnumerable<Call> Scan(IReadOnlyList<EntityBinding> bindings, bool conditions)
        {
            foreach (var binding in bindings.Where(b => b?.Entity))
            {
                var found = new HashSet<string>();
                foreach (var component in new[] { binding.Entity })
                    foreach (var method in Methods(component.GetType()))
                    {
                        var condition = method.GetCustomAttribute<QuestConditionAttribute>(); var action = method.GetCustomAttribute<QuestActionAttribute>();
                        if (conditions ? condition == null : action == null) continue;
                        var id = (conditions ? condition.Id : action.Id) ?? method.DeclaringType.FullName + "." + method.Name;
                        if (string.IsNullOrWhiteSpace(id) || !found.Add(id)) throw new ArgumentException("Ambiguous or empty Quest method on " + binding.Entity.name + ": " + id);
                        yield return new Call { Id = id, Role = binding.Role, Component = component, Method = method };
                    }
            }
        }
        public static IReadOnlyList<WorldSignal> Conditions(IReadOnlyList<EntityBinding> bindings) => Scan(bindings, true).Select(c =>
            new WorldSignal(c.Role, c.Id, c.Method.GetCustomAttribute<QuestConditionAttribute>().Label ?? c.Method.Name, false, c.Method.DeclaringType, c.Method.Name)).ToArray();
        public static bool? Read(IReadOnlyList<EntityBinding> bindings, string role, string key)
        {
            var call = Scan(bindings.Where(b => b.Role == role).ToArray(), true).SingleOrDefault(c => c.Id == key);
            if (call == null) return null;
            try { return (bool?)call.Method.Invoke(call.Component, null); }
            catch (Exception e) { throw new InvalidOperationException(key + ": " + Message(e)); }
        }
        internal static string Message(Exception e) => e is TargetInvocationException && e.InnerException != null ? e.InnerException.Message : e.Message;
    }
}
