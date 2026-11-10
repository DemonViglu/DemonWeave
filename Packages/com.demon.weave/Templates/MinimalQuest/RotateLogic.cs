using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.Samples.Minimal
{
    public enum RotateResult { Done }

    [QuestLogic("sample.rotate", "Rotate an object")]
    public sealed class RotateLogic : QuestNodeLogic<RotateResult>
    {
        [ShowInQuest] public Transform target;
        [ShowInQuest] public float seconds = 2;
        [ShowInQuest(Advanced = true)] public float degreesPerSecond = 90;
        [ShowInQuest(Advanced = true, Required = false)] public AudioSource sound;

        public override string CheckConfiguration(QuestBindings objects) =>
            seconds < 0 ? "Seconds must be nonnegative." : "";

        protected override void OnStart()
        {
            if (sound) sound.Play();
        }

        protected override void OnTick()
        {
            if (!target) throw new System.InvalidOperationException("Rotation target was destroyed.");
            if (ActiveTime >= seconds) { Finish(RotateResult.Done); return; }
            target.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime);
        }

        protected override void OnStop(QuestStopReason reason)
        {
            if (sound) sound.Stop();
        }
    }
}
