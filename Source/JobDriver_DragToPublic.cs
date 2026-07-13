using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Bound-in-public job: the rapist carries the victim (targetA) to a public cell (targetB), drops them,
    /// and locks one or more RJW devices on them so the colony sees them restrained and exposed. Freeing
    /// them needs the RJW key the captor keeps. Works without the Onahole Extension.
    /// </summary>
    public class JobDriver_DragToPublic : JobDriver
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

            var dump = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            dump.initAction = delegate
            {
                var v = Victim;
                IntVec3 cell = job.GetTarget(CellInd).Cell;
                HarassmentEngine.EndDrag(pawn, v, cell);
                if (v != null && v.Spawned)
                {
                    try
                    {
                        HarassmentEngine.LockDevices(v, pawn); // bind them; captor keeps the key(s)
                        var vp = GameComponent_Harassment.Instance?.GetProfile(v);
                        if (vp != null) vp.boundInPublic = true; // begs for help until freed
                        HarassmentEngine.NotifyBoundInPublic(pawn, v);
                    }
                    catch { }
                }
            };
            yield return dump;
        }
    }
}
