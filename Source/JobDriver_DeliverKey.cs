using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The owner walks to the recipient (targetA) to physically deliver a collar key before control actually
    /// transfers. targetB is the collared victim, targetC is the held key (moved on a hand-over, read for its
    /// stamp on a copy). job.count == 1 means copy, 0 means hand over. The walk means collar control never
    /// teleports across the map - the owner has to reach the recipient first.
    /// </summary>
    public class JobDriver_DeliverKey : JobDriver
    {
        private const TargetIndex RecipientInd = TargetIndex.A;
        private const TargetIndex VictimInd = TargetIndex.B;
        private const TargetIndex KeyInd = TargetIndex.C;

        private Pawn Recipient => job.GetTarget(RecipientInd).Thing as Pawn;
        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;
        // Key lives in the owner's inventory (not spawned), so it is only read, never reserved.
        private Thing Key => job.GetTarget(KeyInd).Thing;
        private bool IsCopy => job.count == 1;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Recipient == null || Recipient.Dead || Victim == null || Victim.Dead || Key == null);

            yield return Toils_Goto.GotoThing(RecipientInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(RecipientInd);

            var give = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            give.initAction = delegate
            {
                var rec = Recipient;
                var vic = Victim;
                var key = Key;
                if (rec == null || !rec.Spawned || vic == null || key == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (IsCopy) HarassmentEngine.CompleteMintCopyKey(pawn, rec, vic, key);
                else HarassmentEngine.CompleteHandOverKey(pawn, rec, vic, key);
            };
            yield return give;
        }
    }
}
