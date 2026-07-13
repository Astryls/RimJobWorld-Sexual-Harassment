using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The owner chases a pet that bolted from a beating, re-pathing as it runs and spooking it further when
    /// they close in. Once the owner catches the pet (adjacent or it drops), HarassmentEngine.OnCaughtFlee
    /// fires the retaliation (onahole / beatdown / gangbang / beat-to-death).
    /// </summary>
    public class JobDriver_PursueFlee : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;
        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;
        private int _giveUpTick = -1;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Dead || pawn == Victim);

            var chase = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
            chase.initAction = delegate
            {
                _giveUpTick = Find.TickManager.TicksGame + 4000; // ~1.6 in-game hours safety cap
                pawn.pather.StartPath(Victim, PathEndMode.Touch);
            };
            chase.tickAction = delegate
            {
                var v = Victim;
                if (v == null || v.Dead) { EndJobWith(JobCondition.Incompletable); return; }
                bool caught = v.Downed || pawn.Position.DistanceTo(v.Position) <= 2f;
                if (caught || Find.TickManager.TicksGame >= _giveUpTick)
                {
                    HarassmentEngine.OnCaughtFlee(pawn, v);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                if (!pawn.pather.Moving) pawn.pather.StartPath(v, PathEndMode.Touch);
                pawn.rotationTracker.FaceTarget(v);
                // Keep it a chase: if the pet stopped and we are closing in, spook it into running again.
                if (pawn.IsHashIntervalTick(120) && pawn.Position.DistanceTo(v.Position) < 6f)
                    HarassmentEngine.FleeFurther(v, pawn);
            };
            yield return chase;
        }
    }
}
