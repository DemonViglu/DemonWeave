using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace DemonViglu.Startup.Unity
{
    /// <summary>Explicit application component. Installs only its own nodes and preserves other packages' loop nodes.</summary>
    [DisallowMultipleComponent]
    public sealed class AppLoopDriver : MonoBehaviour
    {
        private sealed class BeforePhysics { }
        private sealed class AfterPhysics { }
        private sealed class BeforeUpdate { }
        private sealed class AfterLate { }
        private AppHost owner;
        public bool IsInstalled => owner != null;

        internal void Install(AppHost host)
        {
            if (owner != null) throw new InvalidOperationException("Driver already installed.");
            if (!enabled || !gameObject.activeInHierarchy || host.gameObject != gameObject)
                throw new InvalidOperationException("Driver must be enabled on the AppHost root.");
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate), typeof(BeforePhysics), FixedTick, false) ||
                !Insert(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate), typeof(AfterPhysics), PostPhysicsTick, true) ||
                !Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), typeof(BeforeUpdate), UpdateTick, false) ||
                !Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), typeof(AfterLate), LateTick, true))
                throw new InvalidOperationException("Required Unity PlayerLoop anchors are missing; driver was not installed.");
            owner = host;
            PlayerLoop.SetPlayerLoop(loop);
        }

        internal void Uninstall()
        {
            if (owner == null) return;
            owner = null;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        private void FixedTick() => owner?.Drive(LoopPhase.Fixed, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        private void PostPhysicsTick() => owner?.Drive(LoopPhase.PostPhysics, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        private void UpdateTick() => owner?.Drive(LoopPhase.Update, Time.deltaTime, Time.unscaledDeltaTime);
        private void LateTick() => owner?.Drive(LoopPhase.Late, Time.deltaTime, Time.unscaledDeltaTime);

        private static bool Insert(ref PlayerLoopSystem root, Type anchor, Type marker,
            PlayerLoopSystem.UpdateFunction callback, bool after)
        {
            var children = root.subSystemList;
            if (children == null) return false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                {
                    var list = new List<PlayerLoopSystem>(children);
                    list.Insert(i + (after ? 1 : 0), new PlayerLoopSystem { type = marker, updateDelegate = callback });
                    root.subSystemList = list.ToArray();
                    return true;
                }
                var child = children[i];
                if (Insert(ref child, anchor, marker, callback, after))
                {
                    children[i] = child;
                    return true;
                }
            }
            return false;
        }

        private static void Remove(ref PlayerLoopSystem root)
        {
            if (root.subSystemList == null) return;
            var kept = new List<PlayerLoopSystem>();
            foreach (var original in root.subSystemList)
            {
                if (original.type == typeof(BeforePhysics) || original.type == typeof(AfterPhysics) ||
                    original.type == typeof(BeforeUpdate) || original.type == typeof(AfterLate)) continue;
                var child = original;
                Remove(ref child);
                kept.Add(child);
            }
            root.subSystemList = kept.ToArray();
        }

        private void OnDisable() { if (owner != null) owner.StopApplication(); }
        private void OnDestroy() { if (owner != null) owner.StopApplication(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }
    }
}
