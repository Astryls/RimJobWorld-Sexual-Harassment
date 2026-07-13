using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The stripped pet walks a humiliating circuit around the colony, pausing near each colonist while
    /// onlookers react. Started by DepthStartParade after the pet is stripped bare.
    /// </summary>
    public class JobDriver_Parade : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn == null || pawn.Downed || pawn.Dead);

            var stops = HarassmentEngine.ParadeStops(pawn, 5);
            foreach (var cell in stops)
            {
                IntVec3 c = cell;
                var setTarget = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
                setTarget.initAction = delegate { job.SetTarget(TargetIndex.A, c); };
                yield return setTarget;

                yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

                var showOff = new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 160, handlingFacing = false };
                showOff.initAction = delegate { HarassmentEngine.DepthParadeReactAround(pawn); };
                showOff.AddPreTickAction(delegate { if (pawn.IsHashIntervalTick(80)) HarassmentEngine.DepthParadeReactAround(pawn); });
                yield return showOff;
            }
        }
    }
}
