using System.Linq;
using DemonWeave.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DemonWeave.AreaRescue.Editor
{
    public static class AreaRescueArtPass
    {
        private const string Folder = "Assets/AreaRescue/Content/";
        public static void RefreshSentry()
        {
            var scene = EditorSceneManager.OpenScene(AreaRescueSetup.ScenePath);
            var sentry = Object.FindObjectsByType<CombatActor>().FirstOrDefault(a => a.name.StartsWith("Unrelated sentry"));
            if (!sentry || sentry.transform.Find("Character silhouette")) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Guard.prefab");
            Object.Instantiate(source.transform.Find("Character silhouette").gameObject, sentry.transform, false);
            sentry.GetComponent<MeshRenderer>().enabled = false;
            var label = Object.Instantiate(source.GetComponent<ActorBadge>().Label, sentry.transform, false);
            var badge = sentry.gameObject.AddComponent<ActorBadge>(); badge.DisplayName = "SENTRY"; badge.Label = label;
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(AreaRescueSetup.ScenePath);
            var host = Object.FindAnyObjectByType<AreaRescueScenario>();
            if (GameObject.Find("Outpost environment")) return;
            Material Mat(string name, Color color, bool emission = false)
            {
                var path = Folder + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
                material.color = color; material.SetFloat("_Glossiness", .15f);
                if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.4f); }
                EditorUtility.SetDirty(material); return material;
            }
            var concrete = Mat("OutpostConcrete", new Color(.27f, .31f, .33f));
            var metal = Mat("OutpostSteel", new Color(.09f, .13f, .16f));
            var amber = Mat("OutpostAmber", new Color(.94f, .54f, .15f), true);
            var teal = Mat("OutpostTeal", new Color(.12f, .67f, .65f), true);
            var pavement = Mat("OutpostPavement", new Color(.12f, .18f, .20f));
            var cloth = Mat("OutpostCloth", new Color(.65f, .69f, .62f));
            var enemy = Mat("WardenArmor", new Color(.55f, .18f, .12f));
            var player = Mat("RescuerArmor", new Color(.12f, .38f, .55f));
            var root = new GameObject("Outpost environment");
            GameObject Shape(string name, Transform parent, Vector3 pos, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube)
            {
                var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false);
                obj.transform.localPosition = pos; obj.transform.localScale = size; obj.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(obj.GetComponent<Collider>()); return obj;
            }
            var gate = host.Bindings.Single(b => b.Role == host.DoorRole).Entity.gameObject;
            gate.GetComponent<Renderer>().sharedMaterial = metal;
            for (int i = -1; i <= 1; i++) Shape("Gate amber rib", gate.transform, new Vector3(i * .28f, 0, -.52f), new Vector3(.025f, .92f, .035f), amber);
            Shape("Gate lintel", root.transform, new Vector3(0, 5.1f, 3), new Vector3(5.4f, .7f, 1), metal);
            foreach (float x in new[] { -2.6f, 2.6f })
            {
                Shape("Gate pillar", root.transform, new Vector3(x, 2.6f, 3), new Vector3(.7f, 5.2f, 1), metal);
                Shape("Gate light", root.transform, new Vector3(x, 3.6f, 2.43f), new Vector3(.18f, 1.8f, .08f), amber);
            }
            foreach (var renderer in Object.FindObjectsByType<Renderer>().Where(r => r.name.Contains("wall") || r.name.Contains("boundary"))) renderer.sharedMaterial = concrete;
            GameObject.Find("Courtyard foundation").GetComponent<Renderer>().sharedMaterial = pavement;
            GameObject.Find("Entry region marker").GetComponent<Renderer>().enabled = false;
            for (int i = 0; i < 5; i++) GameObject.Find("Entry path " + i).GetComponent<Renderer>().enabled = false;
            for (int z = -9; z <= 13; z += 2)
                for (int x = -10; x <= 10; x += 2)
                    Shape("Paving seam", root.transform, new Vector3(x, .012f, z), new Vector3(1.92f, .02f, 1.92f), ((x + z) % 4 == 0) ? concrete : pavement);
            for (int z = -7; z <= 1; z += 2)
            {
                Shape("Approach lane left", root.transform, new Vector3(-1.25f, .04f, z), new Vector3(.07f, .025f, 1.3f), teal);
                Shape("Approach lane right", root.transform, new Vector3(1.25f, .04f, z), new Vector3(.07f, .025f, 1.3f), teal);
            }
            for (int z = 5; z <= 13; z += 4)
                foreach (float x in new[] { -11.6f, 11.6f })
                {
                    Shape("Wall buttress", root.transform, new Vector3(x, 1.4f, z), new Vector3(.65f, 2.8f, .8f), metal);
                    Shape("Wall lamp", root.transform, new Vector3(x * .96f, 2.2f, z), new Vector3(.12f, .6f, .5f), teal);
                }
            foreach (var position in new[] { new Vector3(-8, .6f, 10), new Vector3(7, .6f, 11), new Vector3(-9, .6f, -6) })
            {
                Shape("Supply crate", root.transform, position, new Vector3(1.5f, 1.2f, 1.3f), metal);
                Shape("Crate strap", root.transform, position + Vector3.up * .62f, new Vector3(.2f, .08f, 1.32f), amber);
            }
            Shape("Captive platform", root.transform, new Vector3(0, .08f, 13), new Vector3(3, .15f, 2.2f), metal);
            Shape("Captive backplate", root.transform, new Vector3(0, 1.3f, 14), new Vector3(2.2f, 2.6f, .2f), metal);
            Shape("Captive beacon", root.transform, new Vector3(0, 2.9f, 14), new Vector3(1.4f, .1f, .2f), amber);
            void Model(GameObject actor, Material armor, string name)
            {
                if (actor.transform.Find("Character silhouette")) return;
                var original = actor.GetComponent<MeshRenderer>(); if (original) original.enabled = false;
                var body = new GameObject("Character silhouette"); body.transform.SetParent(actor.transform, false);
                Shape("Torso", body.transform, Vector3.zero, new Vector3(.66f, .8f, .4f), armor);
                Shape("Head", body.transform, new Vector3(0, .64f, 0), Vector3.one * .42f, cloth, PrimitiveType.Sphere);
                Shape("Visor", body.transform, new Vector3(0, .67f, -.2f), new Vector3(.34f, .12f, .06f), metal);
                foreach (float x in new[] { -.2f, .2f }) Shape("Boot", body.transform, new Vector3(x, -.64f, 0), new Vector3(.23f, .68f, .28f), metal);
                foreach (float x in new[] { -.44f, .44f }) Shape("Arm", body.transform, new Vector3(x, -.05f, 0), new Vector3(.17f, .65f, .23f), armor);
                var label = new GameObject("Actor label", typeof(TextMesh)); label.transform.SetParent(actor.transform, false); label.transform.localPosition = Vector3.up * 1.35f;
                var text = label.GetComponent<TextMesh>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 48; text.characterSize = .035f; text.anchor = TextAnchor.MiddleCenter; text.color = Color.white; text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
                var badge = actor.GetComponent<ActorBadge>() ?? actor.AddComponent<ActorBadge>(); badge.DisplayName = name; badge.Label = text;
            }
            foreach (var pair in new[] { ("Guard.prefab", enemy, "WARDEN"), ("Companion.prefab", cloth, "LIO") })
            {
                var prefab = PrefabUtility.LoadPrefabContents(Folder + pair.Item1);
                try { Model(prefab, pair.Item2, pair.Item3); PrefabUtility.SaveAsPrefabAsset(prefab, Folder + pair.Item1); }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            Model(host.Player.gameObject, player, "YOU");
            if (!host.Player.GetComponent<CombatActorVisual>()) host.Player.gameObject.AddComponent<CombatActorVisual>();
            host.Player.MaxHealth = 24; EditorUtility.SetDirty(host.Player);
            var combat = host.GetComponent<RescueCombat>(); combat.AttackInterval = 2.2f; EditorUtility.SetDirty(combat);
            var camera = Camera.main; camera.transform.position = new Vector3(20, 24, -26); camera.transform.LookAt(new Vector3(0, 0, 4)); camera.backgroundColor = new Color(.055f, .08f, .11f);
            RenderSettings.ambientLight = new Color(.38f, .44f, .50f);
            var light = Object.FindObjectsByType<Light>().First(l => l.type == LightType.Directional); light.color = new Color(1, .87f, .7f); light.shadowStrength = .8f;
            var controls = GameObject.Find("Controls").GetComponent<Text>(); controls.text = "Click ground to move · Click a warden to approach and attack · Click Lio to rescue\nWASD cancels auto-movement · Death restores the checkpoint";
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }
}
