using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Overwrites the two relationship defs' labels from the chosen scheme so the social tab reads
    /// "owner/pet", "master|mistress/slave", or "master|mistress/property". Applied at startup and whenever
    /// the setting changes (the label is a plain def field, shared everywhere it is displayed).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RelationLabels
    {
        static RelationLabels()
        {
            Apply(RimJobWorldSexualHarassmentMod.Settings != null
                ? RimJobWorldSexualHarassmentMod.Settings.relationScheme
                : RelationScheme.OwnerPet);
        }

        public static void Apply(RelationScheme scheme)
        {
            var owner = RJWSH_RelationDefOf.RJWSH_RelOwner;
            var pet = RJWSH_RelationDefOf.RJWSH_RelPet;
            if (owner == null || pet == null) return;
            switch (scheme)
            {
                case RelationScheme.MasterSlave:
                    owner.label = "master"; owner.labelFemale = "mistress";
                    pet.label = "slave"; pet.labelFemale = null;
                    break;
                case RelationScheme.MasterProperty:
                    owner.label = "master"; owner.labelFemale = "mistress";
                    pet.label = "property"; pet.labelFemale = null;
                    break;
                default: // OwnerPet
                    owner.label = "owner"; owner.labelFemale = "owner";
                    pet.label = "pet"; pet.labelFemale = null;
                    break;
            }
        }
    }
}
