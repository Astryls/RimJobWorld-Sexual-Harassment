using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Ownership-meme ritual effects (#8). The rites are sanctioned gatherings; their quality-scaled
    /// payoff is a conditioning surge on the colony's collared pets (collaring ceremony) or that plus a bump
    /// to the colony's infamy and the pets' humiliation (devotion parade).</summary>
    public static partial class HarassmentEngine
    {
        /// <summary>Apply a completed Ownership rite: deepen conditioning on collared pets by ritual quality.</summary>
        public static void ApplyOwnershipRiteOutcome(Map map, float quality, bool parade)
        {
            if (map == null) return;
            float t = Mathf.Clamp01(quality);
            float condBoost = parade ? Mathf.Lerp(1f, 8f, t) : Mathf.Lerp(2f, 12f, t);
            if (condBoost <= 0f) return;

            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;

            int affected = 0;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pet = pawns[i];
                if (pet == null || pet.Dead || !pet.RaceProps.Humanlike) continue;
                if (!IsPlayerOwned(pet)) continue;
                var prof = gc.GetProfileIfExists(pet);
                if (prof == null) continue;
                if (!IsCollared(pet) && (prof.ownerId < 0 && prof.relationshipOwnerId < 0)) continue;

                prof.ApplyCond(parade ? "Devotion parade" : "Collaring rite", condBoost, parade ? -2f : 1f);
                AttrDelta(pet, subdom: -condBoost * 0.5f, trauma: parade ? condBoost * 0.2f : -condBoost * 0.15f);
                if (parade) ApplyThought(pet, null, RJWSH_ThoughtDefOf.RJWSH_Humiliated);
                Chronicle(pet, parade ? "Shown off at a devotion parade before the faithful." : "Deepened at a collaring rite before the faithful.", 1);
                affected++;
            }

            if (parade && affected > 0) gc.AddNotoriety(3, "rjwsh_parade");
        }
    }
}
