using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The slave walks to a paying client, propositions them, and (on a stat-based success roll done on
    /// arrival) the service act plays out - after which the owner is paid in SexUtility.Aftersex. A failed
    /// roll means the client declines and nobody is paid. This replaces the old instant pay-on-click flow.
    /// </summary>
    public class JobDriver_Whore : JobDriver
    {
        private const TargetIndex ClientInd = TargetIndex.A;
        private Pawn Client => job.GetTarget(ClientInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Reserve(job.GetTarget(ClientInd), job, 1, -1, null, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(ClientInd);
            this.FailOn(() => Client == null || Client.Dead || Client.Downed);

            // 1) Walk over to the client.
            yield return Toils_Goto.GotoThing(ClientInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(ClientInd);

            // 2) Solicit: a short come-on while facing the client.
            var solicit = Toils_General.Wait(80);
            solicit.handlingFacing = true;
            solicit.WithProgressBarToilDelay(ClientInd);
            solicit.tickAction = delegate { var c = Client; if (c != null) pawn.rotationTracker?.FaceTarget(c); };
            yield return solicit;

            // 3) Roll + start the act (or get turned down).
            var resolve = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            resolve.initAction = delegate { HarassmentEngine.ResolveWhoreAttempt(pawn, Client); };
            yield return resolve;
        }
    }
}
