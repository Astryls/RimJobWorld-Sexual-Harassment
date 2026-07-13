using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>A consensual / content-pet affection moment: the actor walks to their partner and they share a
    /// kiss or hold hands for a few seconds (visual + social log + a small "tender moment" mood buff).</summary>
    public class JobDriver_Affection : JobDriver
    {
        private const TargetIndex PartnerInd = TargetIndex.A;
        private Pawn Partner => job.GetTarget(PartnerInd).Thing as Pawn;
        private AffectionKind Kind => (AffectionKind)job.count;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PartnerInd);
            this.FailOn(() => Partner == null || Partner.Dead || Partner.Downed || !Partner.Spawned);

            yield return Toils_Goto.GotoThing(PartnerInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(PartnerInd);

            var pose = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 220,
                handlingFacing = true,
                socialMode = RandomSocialMode.Off
            };
            pose.initAction = delegate { HarassmentEngine.OnAffectionStart(pawn, Partner, Kind); };
            pose.tickAction = delegate
            {
                var pt = Partner;
                if (pt == null) return;
                pawn.rotationTracker?.FaceTarget(pt);
                if (pawn.IsHashIntervalTick(45)) HarassmentEngine.AffectionTick(pawn, pt, Kind);
            };
            yield return pose;
        }
    }
}
