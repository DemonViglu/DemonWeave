using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemonWeave.Quests;
using Newtonsoft.Json.Linq;

namespace DemonWeave.Unity
{
    public sealed class QuestLogicDescriptor
    {
        public string Id { get; }
        public string Name { get; }
        public Type Type { get; }
        public IReadOnlyList<FieldInfo> Fields { get; }
        public IReadOnlyList<string> Results => New().Results;
        public string Comment => New().Comment;
        internal QuestLogicDescriptor(Type type, QuestLogicAttribute identity)
        {
            Type = type; Id = identity.Id; Name = identity.Name;
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || type.GetConstructor(System.Type.EmptyTypes) == null)
                throw new ArgumentException(type.FullName + ": QuestLogic requires an ID, name and public parameterless constructor.");
            Fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => !f.IsNotSerialized).ToArray();
            foreach (var field in Fields)
                if (field.IsInitOnly || !Supported(field.FieldType)) throw new ArgumentException(type.Name + "." + field.Name + ": use a writable bool/int/float/double/string/enum or UnityEngine.Object field.");
            if (Fields.GroupBy(f => f.Name).Any(g => g.Count() > 1)) throw new ArgumentException(type.Name + ": hidden fields have duplicate names.");
        }
        private QuestNodeLogic New() => (QuestNodeLogic)Activator.CreateInstance(Type);
        public static Type ObjectType(Type type) => typeof(UnityEngine.Object).IsAssignableFrom(type) ? type : null;
        public static bool IsRequired(FieldInfo field) => field.GetCustomAttribute<ShowInQuestAttribute>()?.Required ?? true;
        private static bool Supported(Type type) => type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(string) || type.IsEnum || ObjectType(type) != null;
        public JObject Defaults()
        {
            var result = new JObject(); var instance = New();
            foreach (var field in Fields) {
                var value = field.GetValue(instance);
                result[field.Name] = ObjectType(field.FieldType) != null
                    ? new JObject { ["Id"] = "" }
                    : field.FieldType.IsEnum ? new JValue(value.ToString()) : value == null ? JValue.CreateNull() : JToken.FromObject(value);
            }
            return result;
        }
        public QuestNodeLogic Create(JObject parameters, QuestBindings objects = null)
        {
            if (parameters == null) throw new ArgumentException(Id + ": Parameters must be an object.");
            foreach (var property in parameters.Properties())
                if (!Fields.Any(f => f.Name == property.Name)) throw new ArgumentException(Id + ".Parameters." + property.Name + ": unknown parameter.");
            var instance = New();
            foreach (var field in Fields) {
                var path = Id + ".Parameters." + field.Name;
                if (!parameters.TryGetValue(field.Name, StringComparison.Ordinal, out var token)) throw new ArgumentException(path + ": missing parameter. Review the updated code defaults.");
                try { field.SetValue(instance, Decode(field.FieldType, token, objects, IsRequired(field))); }
                catch (Exception error) { throw new ArgumentException(path + ": " + error.Message, error); }
            }
            var configurationError = instance.CheckConfiguration(objects ?? new QuestBindings(Array.Empty<ObjectBinding>()));
            if (!string.IsNullOrEmpty(configurationError)) throw new ArgumentException(Id + ": " + configurationError);
            return instance;
        }
        private static object Decode(Type type, JToken token, QuestBindings objects, bool required)
        {
            var objectType = ObjectType(type);
            if (objectType != null) {
                if (token is not JObject reference || reference.Count != 1 || reference["Id"]?.Type != JTokenType.String)
                    throw new ArgumentException("Select an object reference {Id:string}.");
                var id = (string)reference["Id"];
                if (string.IsNullOrWhiteSpace(id)) {
                    if (!required) return null;
                    throw new ArgumentException("Select a required object reference.");
                }
                if (objects == null) throw new ArgumentException("Select a scene runner to resolve object references.");
                return objects.Require(id, objectType);
            }
            if (type.IsEnum) {
                if (token.Type != JTokenType.String || !Enum.GetNames(type).Contains((string)token)) throw new ArgumentException("Choose one of: " + string.Join(", ", Enum.GetNames(type)));
                return Enum.Parse(type, (string)token);
            }
            if (type == typeof(string) && token.Type == JTokenType.String) return (string)token;
            if (type == typeof(bool) && token.Type == JTokenType.Boolean) return (bool)token;
            if (type == typeof(int) && token.Type == JTokenType.Integer) return checked((int)token);
            if ((type == typeof(float) || type == typeof(double)) && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)) {
                var number = (double)token;
                if (double.IsNaN(number) || double.IsInfinity(number) || type == typeof(float) && Math.Abs(number) > float.MaxValue) throw new ArgumentException("Number must be finite and in range.");
                if (type == typeof(float)) return (float)number;
                return number;
            }
            throw new ArgumentException("Expected " + type.Name + ", found " + token.Type);
        }
    }
    public static class QuestLogicCatalog
    {
        private static Dictionary<string, QuestLogicDescriptor> entries;
        public static IReadOnlyCollection<QuestLogicDescriptor> All { get { Ensure(); return entries.Values; } }
        public static QuestLogicDescriptor Find(string id) { Ensure(); return id != null && entries.TryGetValue(id, out var value) ? value : null; }
        private static void Ensure()
        {
            if (entries != null) return;
            var found = new Dictionary<string, QuestLogicDescriptor>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] types;
                try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException error) { types = error.Types; }
                foreach (var type in types.Where(t => t != null && !t.IsAbstract && !t.ContainsGenericParameters && typeof(QuestNodeLogic).IsAssignableFrom(t))) {
                    var identity = type.GetCustomAttribute<QuestLogicAttribute>(false);
                    if (identity == null) continue;
                    var descriptor = new QuestLogicDescriptor(type, identity);
                    if (found.ContainsKey(descriptor.Id)) throw new ArgumentException("Duplicate QuestLogic ID: " + descriptor.Id);
                    found.Add(descriptor.Id, descriptor);
                }
            }
            entries = found;
        }
        public static QuestNodeLogic Validate(QuestStage node, QuestBindings objects = null)
        {
            var descriptor = Find(node.LogicId) ?? throw new ArgumentException(node.Title + ": unknown LogicId " + node.LogicId);
            var logic = descriptor.Create(node.Parameters, objects);
            if (node.Results == null || !logic.Results.SequenceEqual(node.Results)) throw new ArgumentException(node.Title + ": result contract changed. Review the logic selection and connections.");
            return logic;
        }
    }
}
