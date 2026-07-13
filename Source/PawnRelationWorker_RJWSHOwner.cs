using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Implied relation: true when "me" is currently owned (collared / key-controlled) by "other". This
    /// labels the owner on the slave's social card; the stored RJWSH_RelPet labels the reverse direction.
    /// Reads ownership live from PawnProfile.ownerId, so it always reflects the current key-holder.
    /// </summary>
    public class PawnRelationWorker_RJWSHOwner : PawnRelationWorker
    {
        public override bool InRelation(Pawn me, Pawn other)
        {
            if (me == other || me == null || other == null) return false;
            if (RimJobWorldSexualHarassmentMod.Settings == null || !RimJobWorldSexualHarassmentMod.Settings.enableOwnerRelationship) return false;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(me);
            return prof != null && prof.relationshipOwnerId >= 0 && prof.relationshipOwnerId == other.thingIDNumber;
        }
    }
}
