using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Victim's hold job: keeps them pinned (and facing) beside the harasser while being stripped, so
    /// they cannot wander off. Ends once the harasser is no longer running the strip job (resolved, or
    /// replaced by the RJW forced act, which re-holds them itself). checkOverrideOnDamage=Never on the
    /// def stops the unarmed blows from triggering a flee override.
    /// </summary>
    public class JobDriver_HarassVictim : JobDriver
    {
        private const TargetIndex HarasserInd = TargetIndex.A;
        private int idleTicks; // ticks the harasser has spent neither stripping nor starting the act

        private Pawn Harasser => job.GetTarget(HarasserInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var wait = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                handlingFacing = true
            };
            wait.initAction = delegate { pawn.pather?.StopDead(); };
            wait.tickAction = delegate
            {
                var h = Harasser;
                if (h == null || h.Dead || !h.Spawned) { EndJobWith(JobCondition.Succeeded); return; }
                // Keep holding the victim through the strip -> forced-act handoff: the harasser is briefly
                // jobless between the strip job ending and RandomRape starting, and an awake victim would
                // otherwise slip loose in that gap and the rape would chase a moving target forever. Tolerate
                // a short idle window; the rape's own receiver job replaces this hold once it grabs the pawn.
                // Only stay held while the harasser is actively engaged with THIS victim (their job targets us),
                // so the hold releases the instant they move on to someone else - never stranding us far away.
                var ct = h.CurJob;
                bool targetsMe = ct != null && ct.GetTarget(TargetIndex.A).Thing == pawn;
                bool harasserBusy = (h.CurJobDef == RJWSH_JobDefOf.RJWSH_StripVictim && targetsMe)
                                    || (h.CurJobDef == RJWSH_JobDefOf.RJWSH_Harass && targetsMe)   // verbal exchange stage
                                    || (h.CurJobDef == rjw.xxx.RapeRandom && targetsMe)
                                    || h.jobs?.curDriver is rjw.JobDriver_Sex;
                if (harasserBusy) idleTicks = 0;
                else if (++idleTicks > 90) { EndJobWith(JobCondition.Succeeded); return; }
                pawn.pather?.StopDead();
                pawn.rotationTracker?.FaceTarget(h);
            };
            yield return wait;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref idleTicks, "idleTicks", 0);
        }
    }
}
