using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The owner walks to their collared pet and either physically disciplines them (a short bout of unarmed
    /// melee strikes, capped non-lethal, animated by Melee Animation if present) or rewards them (a brief
    /// gesture). The conditioning + mood effects apply when the bout finishes.
    /// </summary>
    public class JobDriver_OwnerInteract : JobDriver
    {
        private const TargetIndex PetInd = TargetIndex.A;
        private Pawn Pet => job.GetTarget(PetInd).Thing as Pawn;
        private bool IsReward => job.def == RJWSH_JobDefOf.RJWSH_RewardPet;
        private bool IsDress => job.def == RJWSH_JobDefOf.RJWSH_DressPet;
        private bool IsDiscipline => job.def == RJWSH_JobDefOf.RJWSH_DisciplinePet;
        private bool IsTrain => job.def == RJWSH_JobDefOf.RJWSH_TrainPet;
        private bool _fled;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(PetInd);

            yield return Toils_Goto.GotoThing(PetInd, PathEndMode.Touch).FailOnDespawnedOrNull(PetInd);

            var act = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = IsDiscipline ? 200 : 120,
                handlingFacing = true
            };
            act.FailOnDespawnedOrNull(PetInd);
            act.tickAction = delegate
            {
                var pet = Pet;
                if (pet == null) return;
                // The pet bolted mid-beating: abort the discipline and give chase (pursue + retaliate on catch).
                if (IsDiscipline && pet.Spawned && !pet.Downed && pet.Position.DistanceTo(pawn.Position) > 3f)
                {
                    _fled = true;
                    EndJobWith(JobCondition.Incompletable);
                    HarassmentEngine.StartPursue(pawn, pet);
                    return;
                }
                pawn.rotationTracker.FaceTarget(pet);
                if (IsDiscipline && pawn.IsHashIntervalTick(50))
                    HarassmentEngine.DisciplineStrike(pawn, pet);
            };
            act.AddFinishAction(delegate
            {
                var pet = Pet;
                if (pet == null || _fled) return; // fled -> handled by the pursuit, not the bout
                if (IsDress) HarassmentEngine.DressUp(pawn, pet);
                else if (IsReward) HarassmentEngine.FinishReward(pawn, pet);
                else if (IsTrain) HarassmentEngine.FinishTraining(pawn, pet);
                else HarassmentEngine.FinishDiscipline(pawn, pet);
            });
            yield return act;
        }
    }
}
