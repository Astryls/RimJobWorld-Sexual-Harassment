using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Onahole Extension compat job: the rapist carries the victim to a pre-spawned onahole bed
    /// (targetB) in a public spot, drops them, and tucks them in so they are locked inside as a public
    /// onahole. Capture uses vanilla RestUtility.TuckIntoBed (Building_OnaholeBed : Building_Bed), so
    /// no reference to the Onahole assembly is needed.
    /// </summary>
    public class JobDriver_DragToOnahole : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;
        private const TargetIndex BedInd = TargetIndex.B;

        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;
        private Thing Bed => job.GetTarget(BedInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Reserve(job.GetTarget(VictimInd), job, 10, 0, null, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Dead || Bed == null || Bed.Destroyed);

            // Walk to the victim and grab them.
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

            // Move them to the onahole bed - led (begging in bubbles) or carried (begging in motes) the whole way.
            var haul = Toils_Goto.GotoThing(BedInd, PathEndMode.Touch);
            haul.AddPreTickAction(delegate { if (pawn.IsHashIntervalTick(140)) HarassmentEngine.DragBegTick(pawn, Victim); });
            yield return haul;

            // Drop them and lock them in.
            var capture = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            capture.initAction = delegate
            {
                var v = Victim;
                var bed = Bed;
                HarassmentEngine.EndDrag(pawn, v, bed?.Position ?? pawn.Position);
                if (v != null && v.Spawned && bed is Building_Bed bb && !bb.Destroyed)
                {
                    try
                    {
                        v.Position = bed.Position;
                        RestUtility.TuckIntoBed(bb, v, v, false);
                        // Lock RJW device(s) on them too, so freeing them needs the RJW key (kept by the captor).
                        HarassmentEngine.LockDevices(v, pawn);
                        HarassmentEngine.ForceBondageStyle(bed); // show a bondage style, not the default
                        HarassmentEngine.NotifyOnaholeCapture(pawn, v, bed);
                    }
                    catch { }
                }
            };
            yield return capture;
        }
    }
}
