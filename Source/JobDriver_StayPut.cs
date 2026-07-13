using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Holds a commanded pawn at a chosen spot indefinitely - a hidden "drafted"-style stay. The hold toil never
    /// completes on its own (the pawn won't wander off or pick up work), and because the JobDef is
    /// casualInterruptible=false the colonist AI won't casually replace it. The only escape is the survival valve:
    /// if the pawn turns urgently hungry or exhausted the job ends so they can eat/sleep, after which the engine's
    /// stay enforcement re-pins them. An explicit player command (playerInterruptible=true) also overrides it.
    /// </summary>
    public class JobDriver_StayPut : JobDriver
    {
        private const TargetIndex CellInd = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(CellInd, PathEndMode.OnCell);

            var hold = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                handlingFacing = false
            };
            hold.tickAction = delegate
            {
                var food = pawn.needs?.food;
                if (food != null && food.CurCategory >= HungerCategory.UrgentlyHungry)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                var rest = pawn.needs?.rest;
                if (rest != null && rest.CurCategory >= RestCategory.Exhausted)
                {
                    EndJobWith(JobCondition.InterruptForced);
                }
            };
            yield return hold;
        }
    }
}
