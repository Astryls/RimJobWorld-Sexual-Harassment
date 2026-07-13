using System;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Biotech gene reads. The gene defs live in Biotech/Defs (LoadFolders-gated) and only exist when
    /// Biotech is active; these lookups no-op safely otherwise (a pawn with no genes tracker returns false).
    /// </summary>
    public static class GeneHelper
    {
        public static bool HasGene(Pawn p, string defName)
        {
            var genes = p?.genes;
            if (genes == null) return false;
            var list = genes.GenesListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g != null && g.Active && g.def != null && g.def.defName == defName) return true;
            }
            return false;
        }

        /// <summary>Multiplier on conditioning GAINS: a compliant psyche breaks fast, a willful one resists.</summary>
        public static float ConditioningGainFactor(Pawn p)
        {
            float f = 1f;
            if (HasGene(p, "RJWSH_Gene_Willful")) f *= 0.4f;
            else if (HasGene(p, "RJWSH_Gene_Submissive")) f *= 1.8f;
            else if (HasGene(p, "RJWSH_Gene_Compliant")) f *= 1.6f;
            if (HasGene(p, "RJWSH_Gene_Docile")) f *= 1.35f;
            // RJW Genes: a masochist/hypersexual psyche breaks faster; a dominant (rapist) one resists.
            if (HasGene(p, "rjw_genes_masochist")) f *= 1.35f;
            if (HasGene(p, "rjw_genes_hypersexual")) f *= 1.25f;
            if (HasGene(p, "rjw_genes_rapist")) f *= 0.7f;
            return f;
        }

        /// <summary>Multiplier on conditioning DECAY: willful pawns recover fast, compliant/codependent ones slowly.</summary>
        public static float ConditioningDecayFactor(Pawn p)
        {
            if (HasGene(p, "RJWSH_Gene_Willful")) return 3f;
            if (HasGene(p, "rjw_genes_rapist")) return 1.5f;               // RJW Genes: a dominant psyche shrugs conditioning off
            if (HasGene(p, "RJWSH_Gene_Submissive")) return 0.4f;
            if (HasGene(p, "RJWSH_Gene_Compliant")) return 0.5f;
            if (HasGene(p, "RJWSH_Gene_Docile")) return 0.7f;
            if (HasGene(p, "rjw_genes_masochist")) return 0.6f;            // craves it - recovers slowly
            if (TraitHooks.HasTraitNamed(p, "Codependent")) return 0.5f;   // Psychology: clings to captors
            return 1f;
        }

        public static bool IsPredator(Pawn p) => HasGene(p, "RJWSH_Gene_Predatory");

        /// <summary>Multiplier on per-pawn break susceptibility from genes (folded into HarassmentEngine.BreakSusceptibility
        /// when it is first rolled). Submission genes make a pawn break far faster; iron will makes them resist.</summary>
        public static float SusceptibilityGeneFactor(Pawn p)
        {
            float f = 1f;
            if (HasGene(p, "RJWSH_Gene_Willful")) f *= 0.45f;
            if (HasGene(p, "RJWSH_Gene_Submissive")) f *= 2.0f;
            if (HasGene(p, "RJWSH_Gene_Compliant")) f *= 1.6f;
            if (HasGene(p, "RJWSH_Gene_Docile")) f *= 1.4f;
            if (HasGene(p, "rjw_genes_masochist")) f *= 1.3f;
            if (HasGene(p, "rjw_genes_rapist")) f *= 0.7f;
            return f;
        }

        /// <summary>Adds a heritable endogene to a pawn (needs Biotech). Returns true if newly added. Used by
        /// deep conditioning to etch an RJW Genes trait into a fully-broken pet.</summary>
        public static bool TryAddEndogene(Pawn p, string defName)
        {
            if (!ModsConfig.BiotechActive || p?.genes == null || HasGene(p, defName)) return false;
            var gd = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
            if (gd == null) return false;
            try { p.genes.AddGene(gd, false); return true; }   // xenogene:false -> heritable endogene
            catch { return false; }
        }
    }

    /// <summary>Trait reads used to nudge harasser aggression and victim resilience. Covers vanilla and
    /// third-party (Psychology, RJW) traits; all resolved by defName so absent mods just don't match.</summary>
    public static class TraitHooks
    {
        public static bool HasTraitNamed(Pawn p, string defName)
        {
            var traits = p?.story?.traits;
            if (traits == null) return false;
            var td = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return td != null && traits.HasTrait(td);
        }

        /// <summary>How much a pawn's traits push them toward harassing. Psychology's Volatile and vanilla
        /// Abrasive make pushier aggressors.</summary>
        public static float HarasserTraitFactor(Pawn p)
        {
            float f = 1f;
            if (HasTraitNamed(p, "Volatile")) f *= 2.4f;   // Psychology (Community Update)
            if (HasTraitNamed(p, "Abrasive")) f *= 1.3f;
            return f;
        }
    }

    /// <summary>Royalty psylink read: a disciplined psychic mind resists hypnosis/conditioning.</summary>
    public static class PsyResist
    {
        /// <summary>Additive resistance to a hypnosis attempt (subtracted from the convince chance).</summary>
        public static float HypnosisResist(Pawn target)
        {
            try
            {
                int level = target != null ? target.GetPsylinkLevel() : 0;
                return level * 0.06f;   // +6% resistance per psylink level
            }
            catch { return 0f; }
        }
    }

    /// <summary>Ideology precept/meme reads. Pawns whose faith celebrates domination or pleasure harass more
    /// readily and are not disgusted witnessing it; body-purity faiths are horrified. All by-name + guarded.</summary>
    public static class IdeologyHooks
    {
        private static MemeDef Meme(string name) => DefDatabase<MemeDef>.GetNamedSilentFail(name);

        /// <summary>Multiplier on harasser willingness from the pawn's ideology.</summary>
        public static float HarasserPreceptFactor(Pawn p)
        {
            try
            {
                var ideo = p?.Ideo;
                if (ideo == null) return 1f;
                float f = 1f;
                var precepts = ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    var dn = precepts[i]?.def?.defName;
                    if (dn == null) continue;
                    if (Has(dn, "Rape") && Has(dn, "Approved")) f *= 1.8f;
                    if (Has(dn, "Raider") || Has(dn, "Supremacist")) f *= 1.15f;
                }
                if (HasMeme(ideo, "Hedonist")) f *= 1.3f;
                if (HasMeme(ideo, "Supremacist")) f *= 1.2f;
                if (HasMeme(ideo, "PainIsVirtue")) f *= 1.2f;
                if (HasMeme(ideo, "RJWSH_Ownership")) f *= 1.25f;   // the Ownership meme (Phase 7)
                return f;
            }
            catch { return 1f; }
        }

        /// <summary>True when the witness's faith would not be bothered by (or would approve of) the cruelty,
        /// so they get no "saw something wrong" mood.</summary>
        public static bool ApprovesOfCruelty(Pawn witness)
        {
            try
            {
                var ideo = witness?.Ideo;
                if (ideo == null) return false;
                if (HasMeme(ideo, "Hedonist") || HasMeme(ideo, "Supremacist") || HasMeme(ideo, "Raider")
                    || HasMeme(ideo, "PainIsVirtue") || HasMeme(ideo, "Cannibal") || HasMeme(ideo, "RJWSH_Ownership")) return true;
                var precepts = ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    var dn = precepts[i]?.def?.defName;
                    if (dn != null && Has(dn, "Rape") && Has(dn, "Approved")) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>True for a body-purity / high-minded faith that is extra horrified by harassment.</summary>
        public static bool AbhorsCruelty(Pawn witness)
        {
            try
            {
                var ideo = witness?.Ideo;
                if (ideo == null) return false;
                return HasMeme(ideo, "TreeConnection") || HasMeme(ideo, "Nature") || HasMeme(ideo, "Loyalist");
            }
            catch { return false; }
        }

        private static bool HasMeme(Ideo ideo, string name)
        {
            var m = Meme(name);
            return m != null && ideo.HasMeme(m);
        }

        private static bool Has(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The Leader's Harem meme (external mod): confers key-free authority over pets to the leader.</summary>
        public static bool HasHaremMeme(Pawn p)
        {
            try
            {
                var ideo = p?.Ideo;
                if (ideo == null) return false;
                var precepts = ideo.PreceptsListForReading;
                // detect by meme or precept naming since the exact defName varies by version
                foreach (var meme in ideo.memes)
                {
                    var dn = meme?.defName;
                    if (dn != null && Has(dn, "Harem")) return true;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
