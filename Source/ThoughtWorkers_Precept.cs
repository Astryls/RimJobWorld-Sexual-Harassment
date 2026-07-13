using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Ideology (Phase 7): the owner of a devoted (conditioned) pet gets a mood buff under an ideo whose
    /// Collaring precept is Honored. The base ThoughtWorker_Precept gates this to pawns whose ideo has the precept.</summary>
    public class ThoughtWorker_Precept_OwnsConditionedPet : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null) return ThoughtState.Inactive;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return ThoughtState.Inactive;
            var pawns = p.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pet = pawns[i];
                if (pet == null || pet == p || pet.Dead) continue;
                var prof = gc.GetProfileIfExists(pet);
                if (prof == null || !prof.IsConditioned) continue;
                if (HarassmentEngine.FindKeyHolderFor(pet) == p) return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }

    /// <summary>Ideology (Phase 7): a believer whose ideo forbids collaring is distressed while any collared pet is
    /// present in the colony. Precept-gated by the base class.</summary>
    public class ThoughtWorker_Precept_CollaredPetPresent : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null) return ThoughtState.Inactive;
            var pawns = p.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pet = pawns[i];
                if (pet == null || pet == p || pet.Dead) continue;
                if (HarassmentEngine.IsCollared(pet)) return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }
}
