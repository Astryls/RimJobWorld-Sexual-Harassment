using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// A brief, strictly 1v1 unarmed scuffle between the pawn (a slave fighting back) and their owner. Drives
    /// capped non-lethal strikes in BOTH directions so it reads as a mutual brawl, but only ever involves these
    /// two pawns - no social-fight mental state, no AI re-targeting, no bystanders pulled in.
    /// </summary>
    public class JobDriver_Scuffle : JobDriver
    {
        private const TargetIndex FoeInd = TargetIndex.A;
        private Pawn Foe => job.GetTarget(FoeInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(FoeInd);

            yield return Toils_Goto.GotoThing(FoeInd, PathEndMode.Touch).FailOnDespawnedOrNull(FoeInd);

            var fight = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 240,
                handlingFacing = true
            };
            fight.FailOnDespawnedOrNull(FoeInd);
            fight.tickAction = delegate
            {
                var foe = Foe;
                if (foe == null || foe.Downed || pawn.Downed) { EndJobWith(JobCondition.Succeeded); return; }
                pawn.rotationTracker.FaceTarget(foe);
                if (pawn.IsHashIntervalTick(38))
                {
                    HarassmentEngine.ScuffleStrike(pawn, foe); // the slave strikes the owner
                    HarassmentEngine.ScuffleStrike(foe, pawn); // the owner strikes back
                }
            };
            yield return fight;
        }
    }
}
