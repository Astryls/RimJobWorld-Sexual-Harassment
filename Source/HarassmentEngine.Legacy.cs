using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Owner death & legacy (#3) plus the prose chronicle helper (#2). When a key-holder dies, their
    /// pets react by how deeply they were broken - devoted pets grieve hard (and may shatter), willful
    /// ones taste freedom and can shed the collar - and a still-collared pet passes to an heir.
    /// </summary>
    public static partial class HarassmentEngine
    {
        // ── Chronicle helper (#2) ────────────────────────────────────────────
        /// <summary>Append a prose line to a pawn's life-story chronicle (History tab). kind: 0 neutral,
        /// 1 dark, 2 bright, 3 world.</summary>
        public static void Chronicle(Pawn pawn, string text, int kind = 0)
        {
            if (pawn == null) return;
            GameComponent_Harassment.Instance?.GetProfile(pawn)?.AddChronicle(text, kind);
        }

        // ── Owner death & legacy (#3) ────────────────────────────────────────
        /// <summary>Called from the Pawn.Kill patch when any humanlike dies. If the deceased owned pets
        /// (live collar link or lasting relationship), each reacts by its break stage and a still-collared
        /// pet is handed to an heir. No-op when the dead pawn owned no one.</summary>
        public static void OnOwnerDied(Pawn owner)
        {
            if (owner == null || !owner.RaceProps.Humanlike) return;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;

            int ownerId = owner.thingIDNumber;
            // Gather this owner's pets from every map (live control link or lasting owner relationship).
            var pets = new List<Pawn>();
            foreach (var map in Find.Maps)
            {
                var ps = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    if (p == null || p == owner || p.Dead || !p.RaceProps.Humanlike) continue;
                    var prof = gc.GetProfileIfExists(p);
                    if (prof == null) continue;
                    if (prof.ownerId == ownerId || prof.relationshipOwnerId == ownerId) pets.Add(p);
                }
            }
            if (pets.Count == 0) return;

            string ownerName = owner.LabelShortCap;
            var inherited = new List<Pawn>();
            foreach (var pet in pets)
            {
                var prof = gc.GetProfileIfExists(pet);
                if (prof == null) continue;
                var stage = GetBreakStage(pet, prof);
                bool collared = WearingControlCollar(pet);

                if (stage >= BreakStage.Devoted)
                {
                    // A devoted pet is gutted: heavy, lasting grief - their whole world was that person.
                    TryAddMoodThought(pet, "RJWSH_OwnerDied");
                    AttrDelta(pet, esteem: -8f, spirit: -10f, trauma: 6f);
                    Chronicle(pet, ownerName + " died. " + pet.LabelShortCap + " is inconsolable.", 1);
                    // The most broken may snap entirely - a grief-maddened break.
                    if (stage == BreakStage.Broken && pet.needs?.mood != null && Rand.Chance(0.4f))
                        TryStartMentalBreak(pet);
                }
                else if (stage <= BreakStage.Wavering && !prof.aiControlled)
                {
                    // Barely broken: the death loosens the leash. A taste of freedom, and a chance to slip the collar.
                    prof.hypnosisLevel = System.Math.Max(0f, prof.hypnosisLevel - 20f);
                    AttrDelta(pet, will: 6f, spirit: 5f, esteem: 4f, subdom: 6f);
                    if (collared && Rand.Chance(0.5f))
                    {
                        FreeCollared(pet);
                        TryAddMoodThought(pet, "RJWSH_FreedByDeath");
                        Chronicle(pet, ownerName + " died. " + pet.LabelShortCap + " slipped the collar and is free.", 2);
                        continue; // freed: no inheritance
                    }
                    Chronicle(pet, ownerName + " died. " + pet.LabelShortCap + " feels the leash go slack.", 2);
                }
                else
                {
                    // Compliant middle: unsettled but not shattered.
                    AttrDelta(pet, spirit: -3f, trauma: 2f);
                    Chronicle(pet, ownerName + " died.", 0);
                }

                // Still collared and not freed above -> passes to an heir.
                if (collared || prof.ownerId == ownerId)
                {
                    var heir = PickHeir(owner, pet);
                    if (heir != null)
                    {
                        prof.ownerId = heir.thingIDNumber;
                        prof.relationshipOwnerId = heir.thingIDNumber;
                        EnsureOwnerRelation(heir, pet);
                        inherited.Add(pet);
                        Chronicle(pet, "Passed to " + heir.LabelShortCap + " after " + ownerName + "'s death.", 1);
                        Chronicle(heir, "Inherited " + pet.LabelShortCap + " after " + ownerName + "'s death.", 0);
                    }
                    else
                    {
                        // No heir: the control link dies with the owner (relationship/collar remain, but unowned).
                        prof.ownerId = -1;
                    }
                }
            }

            // One consolidated letter for the player.
            try
            {
                if (inherited.Count > 0)
                {
                    var heir0 = PickHeir(owner, inherited[0]);
                    string names = GenText.ToCommaList(inherited.ConvertAll(p => p.LabelShort), true);
                    Find.LetterStack.ReceiveLetter("Collar passes on",
                        ownerName + " has died. " + (inherited.Count == 1 ? "Their pet " : "Their pets ") + names
                        + " now " + (inherited.Count == 1 ? "answers" : "answer") + " to "
                        + (heir0 != null ? heir0.LabelShortCap : "a new keeper") + ".",
                        LetterDefOf.NeutralEvent, new LookTargets(inherited[0]));
                }
                else
                {
                    Find.LetterStack.ReceiveLetter("An owner has died",
                        ownerName + " has died. " + (pets.Count == 1 ? "Their pet reacts" : "Their pets react")
                        + " to the loss.", LetterDefOf.NeutralEvent, new LookTargets(pets[0]));
                }
            }
            catch { }
        }

        /// <summary>Choose who inherits a collared pet when its owner dies: the pet's other lover/relation if
        /// they are a free colonist, else the strongest free colonist on the map. Null when no heir exists.</summary>
        private static Pawn PickHeir(Pawn deadOwner, Pawn pet)
        {
            var map = pet?.MapHeld;
            if (map == null) return null;
            // Prefer a spouse/lover of the dead owner who is a free colonist (the household stays in the family).
            if (deadOwner?.relations != null)
            {
                foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(deadOwner, false))
                {
                    var lover = rel.otherPawn;
                    if (lover != null && lover.Spawned && !lover.Dead && lover.IsColonist && lover != pet)
                        return lover;
                }
            }
            var heir = StrongestOrLeader(map);
            return (heir != null && heir != pet && !heir.Dead) ? heir : null;
        }

        private static void TryStartMentalBreak(Pawn pet)
        {
            try
            {
                var def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Berserk");
                if (def != null && pet.mindState != null && pet.MentalStateDef == null)
                    pet.mindState.mentalStateHandler.TryStartMentalState(def, "grief", true);
            }
            catch { }
        }
    }
}
