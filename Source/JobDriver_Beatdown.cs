using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The owner chases down a fleeing pet (targetA) and beats them with bare fists until they drop - downed
    /// for a normal beatdown, or dead when job.count == 1 (the beat-to-death retaliation, gated in config).
    /// </summary>
    public class JobDriver_Beatdown : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;
        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;
        private bool Lethal => job.count == 1;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Dead || pawn == Victim);

            // Chase the (fleeing) victim, re-pathing as they move.
            var chase = Toils_Goto.GotoThing(VictimInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(VictimInd);
            chase.AddPreTickAction(delegate
            {
                var v = Victim;
                if (v != null && v.Spawned && pawn.Position.DistanceTo(v.Position) > 2f)
                    pawn.pather.StartPath(v, PathEndMode.Touch);
            });
            yield return chase;

            var beat = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
            beat.tickAction = delegate
            {
                var v = Victim;
                if (v == null || v.Dead || (!Lethal && v.Downed)) { EndJobWith(JobCondition.Succeeded); return; }
                if (pawn.Position.DistanceTo(v.Position) > 2f) { pawn.pather.StartPath(v, PathEndMode.Touch); return; }
                pawn.rotationTracker.FaceTarget(v);
                if (pawn.IsHashIntervalTick(35))
                    HarassmentEngine.ForceMeleeBeatdown(pawn, v, Lethal);
            };
            beat.AddFinishAction(delegate
            {
                var v = Victim;
                if (v != null) HarassmentEngine.FinishBeatdown(pawn, v, Lethal);
            });
            yield return beat;
        }
    }
}
