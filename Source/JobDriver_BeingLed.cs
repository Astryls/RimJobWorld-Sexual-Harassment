using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The victim's side of a "led" drag: keeps a conscious captive walking close behind their captor (TargetA)
    /// so they stay spawned the whole way. A spawned victim can plead in real interaction bubbles, unlike a
    /// carried (despawned) one. Never completes on its own; ends if the captor is gone, and the drag job ends it
    /// on arrival.
    /// </summary>
    public class JobDriver_BeingLed : JobDriver
    {
        private const TargetIndex LeaderInd = TargetIndex.A;
        private Pawn Leader => job.GetTarget(LeaderInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(LeaderInd);

            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
            toil.tickAction = delegate
            {
                var leader = Leader;
                if (leader == null || !leader.Spawned || leader.Dead) { EndJobWith(JobCondition.Incompletable); return; }

                if (pawn.Position.DistanceTo(leader.Position) > 2f)
                {
                    if (!pawn.pather.Moving || pawn.pather.Destination.Cell != leader.Position)
                        pawn.pather.StartPath(leader, PathEndMode.Touch);
                }
                else
                {
                    pawn.pather.StopDead();
                    pawn.rotationTracker?.FaceTarget(leader);
                }
            };
            yield return toil;
        }
    }
}
