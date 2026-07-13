using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Scene-extension job: the attacker carries the victim (targetA) to a private cell (targetB), drops
    /// them, and the assault continues there (queues another forced act). The victim begs for help the
    /// whole way (floating motes, since a carried pawn is not spawned and cannot show a SpeakUp bubble).
    /// </summary>
    public class JobDriver_DragToPrivate : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B;

        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Reserve(job.GetTarget(VictimInd), job, 10, 0, null, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Dead);

            yield return Toils_Goto.GotoThing(VictimInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(VictimInd);

            var grab = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            grab.initAction = delegate
            {
                var v = Victim;
                if (v == null || !v.Spawned) { EndJobWith(JobCondition.Incompletable); return; }
                if (HarassmentEngine.BeginDragGrab(pawn, v) == DragMode.Failed)
                    EndJobWith(JobCondition.Incompletable);
            };
            yield return grab;

            var haul = Toils_Goto.GotoCell(CellInd, PathEndMode.OnCell);
            haul.AddPreTickAction(delegate { if (pawn.IsHashIntervalTick(140)) HarassmentEngine.DragBegTick(pawn, Victim); });
            yield return haul;

            var arrive = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            arrive.initAction = delegate
            {
                var v = Victim;
                IntVec3 cell = job.GetTarget(CellInd).Cell;
                HarassmentEngine.EndDrag(pawn, v, cell);
                if (v != null && v.Spawned)
                {
                    if (HarassmentEngine.InvolvesPlayerPawn(pawn, v))
                        Messages.Message(pawn.LabelShort + " dragged " + v.LabelShort + " somewhere private.",
                            new LookTargets(v), MessageTypeDefOf.NegativeEvent, false);
                    // Continue the assault here (rolls its own scene-end again afterwards).
                    MapComponent_HarassmentScan.EnqueueForcedAct(pawn, v);
                }
            };
            yield return arrive;
        }
    }
}
