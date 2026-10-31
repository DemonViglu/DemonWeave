using System;
using DemonViglu.UI;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DemonWeave.AreaRescue
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class RescueInput : MonoBehaviour
    {
        public RescueEncounter Encounter;
        public RescueCheckpoint Checkpoint;
        public Camera Camera;
        public UiRoot Ui;
        public float MoveSpeed = 5;
        public float AttackRange = 3;
        public float AttackInterval = .35f;
        public event Action RescueRequested;
        private CharacterController controller;
        private float nextAttack;
        private Vector3? destination;
        private Guid movementAttempt;
        private CombatActor attackTarget;
        private GameObject talkTarget;
        private void Awake() => controller = GetComponent<CharacterController>();
        private void Update()
        {
            if (!Encounter || !Checkpoint || Checkpoint.Stopped || Checkpoint.Recovering || Encounter.Player.IsDead || (Ui && Ui.BlocksGameplayInput)) return;
            if (movementAttempt != Checkpoint.AttemptId) { movementAttempt = Checkpoint.AttemptId; destination = null; attackTarget = null; talkTarget = null; }
            if (attackTarget && attackTarget.IsDead) { attackTarget = null; destination = null; }
            var target = attackTarget ? attackTarget.transform : talkTarget ? talkTarget.transform : null;
            if (target)
            {
                var distance = Vector3.Distance(transform.position, target.position);
                if (distance > AttackRange * .85f) destination = target.position;
                else
                {
                    destination = null;
                    if (attackTarget && Time.time >= nextAttack) { if (attackTarget.Damage(1)) nextAttack = Time.time + AttackInterval; }
                    else if (talkTarget) { talkTarget = null; RescueRequested?.Invoke(); }
                }
            }
            var keyboard = Keyboard.current;
            if (controller.enabled)
            {
                float x = (keyboard?.dKey.isPressed == true ? 1 : 0) - (keyboard?.aKey.isPressed == true ? 1 : 0);
                float z = (keyboard?.wKey.isPressed == true ? 1 : 0) - (keyboard?.sKey.isPressed == true ? 1 : 0);
                #if ENABLE_LEGACY_INPUT_MANAGER
                if (Mathf.Abs(x) < .01f && Mathf.Abs(z) < .01f) { x = Input.GetAxisRaw("Horizontal"); z = Input.GetAxisRaw("Vertical"); }
                #endif
                var forward = Vector3.ProjectOnPlane(Camera.transform.forward, Vector3.up).normalized;
                var right = Vector3.ProjectOnPlane(Camera.transform.right, Vector3.up).normalized;
                var motion = Vector3.ClampMagnitude(right * x + forward * z, 1) * MoveSpeed;
                if (motion.sqrMagnitude > .01f) { destination = null; attackTarget = null; talkTarget = null; }
                else if (destination.HasValue)
                {
                    var offset = destination.Value - transform.position; offset.y = 0;
                    if (offset.magnitude < .2f) destination = null;
                    else motion = offset.normalized * MoveSpeed;
                }
                controller.Move((motion + Vector3.down * 2) * Time.deltaTime);
                if (motion.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(motion);
            }
            var mouse = Mouse.current;
            bool clicked = mouse?.leftButton.wasPressedThisFrame == true;
            Vector2 pointer = mouse?.position.ReadValue() ?? Vector2.zero;
            #if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) { clicked = true; pointer = Input.mousePosition; }
            #endif
            if (!clicked || (EventSystem.current?.IsPointerOverGameObject() ?? false)) return;
            if (!Physics.Raycast(Camera.ScreenPointToRay(pointer), out var hit, 150, ~0, QueryTriggerInteraction.Ignore)) return;
            var companion = Encounter.Companion;
            if (companion && hit.collider.transform.IsChildOf(companion.transform))
            { attackTarget = null; talkTarget = companion; return; }
            var actor = hit.collider.GetComponentInParent<CombatActor>();
            if (!actor) { attackTarget = null; talkTarget = null; destination = hit.point; return; }
            if (actor == Encounter.Player || actor.IsDead) return;
            attackTarget = actor; talkTarget = null;
        }
    }
}

