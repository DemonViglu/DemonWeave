using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    public enum RotateResult { Started }

    public sealed class RotateCubeEntity : QuestEntity<RotateResult>
    {
        [ShowInQuest, SerializeField] private RotatingCube cube;
        public override string CheckConfiguration() => cube ? "" : "Cube is missing. Assign the RotatingCube component.";
        public override QuestExecution CreateExecution() => new Execution(cube);
        private sealed class Execution : QuestExecution<RotateResult>
        {
            private readonly RotatingCube cube;
            public Execution(RotatingCube cube) => this.cube = cube;
            protected override void OnStart()
            {
                cube.StartRotating();
                Finish(RotateResult.Started);
            }
        }
    }
}
