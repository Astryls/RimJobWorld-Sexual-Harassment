using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Persistent forced-follow job for a collared/key-locked pawn. Manages pathing directly (walk when
    /// far, wait when near) so it never oscillates, and ends itself when the follow toggle is cleared,
    /// the owner is gone, or the pawn has an urgent food/rest need (so they don't starve while leashed).
    /// </summary>
    public class JobDriver_HarassFollow : JobDriver
    {
        private const TargetIndex OwnerInd = TargetIndex.A;
        private const float FollowRadius = 2f;

        private Pawn Owner => job.GetTarget(OwnerInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(OwnerInd);

            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
            toil.tickAction = delegate
            {
                var owner = Owner;
                if (owner == null || !owner.Spawned || owner.Dead) { EndJobWith(JobCondition.Incompletable); return; }

                var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
                if (prof == null || !prof.followOwner || prof.ownerId != owner.thingIDNumber)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                // Owner permits free needs (toggle) or is asleep -> release so the pawn can sleep/eat/drink/bathe.
                if (HarassmentEngine.NeedsAllowed(prof, owner))
                { EndJobWith(JobCondition.Succeeded); return; }

                // Yield to urgent needs so a leashed pawn can still eat/sleep (ControlUpkeep re-issues after).
                if (pawn.needs?.food != null && pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry)
                { EndJobWith(JobCondition.Succeeded); return; }
                if (pawn.needs?.rest != null && pawn.needs.rest.CurCategory >= RestCategory.VeryTired)
                { EndJobWith(JobCondition.Succeeded); return; }

                if (pawn.Position.DistanceTo(owner.Position) > FollowRadius)
                {
                    if (!pawn.pather.Moving || pawn.pather.Destination.Cell != owner.Position)
                        pawn.pather.StartPath(owner, PathEndMode.Touch);
                }
                else
                {
                    pawn.pather.StopDead();
                    pawn.rotationTracker?.FaceTarget(owner);
                }
            };
            yield return toil;
        }
    }
}
