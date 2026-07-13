using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Ideology ritual outcome (#8) for the Ownership meme's rites. Subclasses the vanilla quality worker
    /// so it inherits all the working comp/quality/mood machinery (no fragile minimal-outcome NRE traps),
    /// then layers the RJWSH effect on top: a quality-scaled conditioning surge across the colony's
    /// collared pets. Branches on def.defName (collaring rite vs devotion parade).
    /// </summary>
    public class RitualOutcomeEffectWorker_RJWSHOwnership : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_RJWSHOwnership() { }
        public RitualOutcomeEffectWorker_RJWSHOwnership(RitualOutcomeEffectDef def) : base(def) { }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            base.Apply(progress, totalPresence, jobRitual);   // standard mood memory + outcome letter
            try
            {
                float quality = GetQuality(jobRitual, progress);
                var map = jobRitual?.selectedTarget.Map ?? Find.AnyPlayerHomeMap;
                bool parade = def != null && def.defName.IndexOf("Parade", StringComparison.OrdinalIgnoreCase) >= 0;
                HarassmentEngine.ApplyOwnershipRiteOutcome(map, quality, parade);
            }
            catch (Exception ex)
            {
                Log.Warning("[RJW Sexual Harassment] ownership rite outcome failed (non-fatal): " + ex.Message);
            }
        }
    }
}
