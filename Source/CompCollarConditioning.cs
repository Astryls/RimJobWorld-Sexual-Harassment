using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    public class HediffCompProperties_CollarConditioning : HediffCompProperties
    {
        public float severityPerDayCollared = 0.08f; // deepens slowly while the control collar is worn (was 0.25 - broke pawns in ~4 days of just wearing it)
        public float severityPerDayLoose = -0.05f;    // fades once the collar comes off
        public float masochistAt = 0.5f;              // min severity before Masochist can be gained (also needs trauma)
        public float stockholmAt = 0.9f;              // min severity before Stockholm Syndrome can be gained (also needs trauma + submission)
        public float masochistTraumaGate = 10f;       // trauma (from abuse) required alongside severity for Masochist
        public float stockholmTraumaGate = 35f;       // trauma (from beatings) required alongside severity for Stockholm
        public float stockholmSubDomGate = -45f;      // how deeply broken toward submission (subDom) they must be for Stockholm

        public HediffCompProperties_CollarConditioning() { compClass = typeof(HediffComp_CollarConditioning); }
    }

    /// <summary>
    /// Drives collar conditioning: severity climbs while the wearer has the control collar on (and slowly
    /// decays otherwise), and at thresholds permanently grants the Masochist and Stockholm Syndrome traits.
    /// </summary>
    public class HediffComp_CollarConditioning : HediffComp
    {
        private int hourTick;

        public HediffCompProperties_CollarConditioning Props => (HediffCompProperties_CollarConditioning)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (Pawn == null) return;
            if (++hourTick < 2500) return; // act ~once per in-game hour
            hourTick = 0;

            bool collared = HarassmentEngine.IsCollared(Pawn);
            float perHour = (collared ? Props.severityPerDayCollared : Props.severityPerDayLoose) / 24f;
            float suscept = HarassmentEngine.BreakSusceptibility(Pawn);
            // Biotech/RJW genes bend the curve; RJW quirks + Sexperience lust + per-pawn susceptibility set the pace.
            if (perHour > 0f) perHour *= GeneHelper.ConditioningGainFactor(Pawn) * HarassmentEngine.ConditioningReceptivity(Pawn) * suscept;
            else perHour *= GeneHelper.ConditioningDecayFactor(Pawn);
            severityAdjustment += perHour;

            // Conditioning stokes lewdness: a trickle of Sexperience lust as the collar deepens (no-op without it).
            if (perHour > 0f) SexperienceBridge.AddLust(Pawn, perHour * 6f);

            // The milestone traits require accumulated ABUSE, not just collar time: Masochist needs some trauma,
            // Stockholm needs a lot of trauma (beatings) AND being broken toward submission. Highly-susceptible
            // pawns (the rare fast breakers) need proportionally less; resistant ones need far more.
            float projected = parent.Severity + perHour;
            var sx = GameComponent_Harassment.Instance?.GetProfileIfExists(Pawn)?.sex;
            float trauma = sx != null ? sx.trauma : 0f;
            float subdom = sx != null ? sx.subDom : 0f;
            if (projected >= Props.masochistAt && trauma >= Props.masochistTraumaGate / suscept)
                { TryGrantTrait("Masochist"); TryInstallConditioningQuirk("Cumslut"); }
            if (projected >= Props.stockholmAt && trauma >= Props.stockholmTraumaGate / suscept && subdom <= Props.stockholmSubDomGate / suscept)
                { TryGrantTrait("RJWSH_StockholmSyndrome"); TryInstallConditioningQuirk("Exhibitionist"); TryInstallConditioningGene(); }
        }

        private void TryGrantTrait(string defName)
        {
            if (Pawn.story?.traits == null) return;
            var td = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            if (td == null || Pawn.story.traits.HasTrait(td)) return;
            try
            {
                // Ensure the pet's sexual attributes exist so the trait-change hook (Patch_TraitSet_GainTrait)
                // reshapes them when the milestone trait is granted below.
                GameComponent_Harassment.Instance?.GetProfile(Pawn)?.SexAttr(Pawn);
                Pawn.story.traits.GainTrait(new Trait(td, 0), true);
                // (attribute effects are applied uniformly by the TraitSet.GainTrait Harmony patch)
                if (PawnUtility.ShouldSendNotificationAbout(Pawn))
                {
                    string label = td.degreeDatas != null && td.degreeDatas.Count > 0 ? td.degreeDatas[0].label : td.defName;
                    // The two conditioning milestones are real turning points in a pet's story - send a letter, not a fleeting message.
                    // Milestones become Tales: colony art can immortalize the breaking and the devotion.
                    if (defName == "RJWSH_StockholmSyndrome") { TaleHelper.Record("RJWSH_Tale_Devoted", Pawn); HarassmentEngine.Chronicle(Pawn, "Completely broken to the collar - devoted now, body and mind.", 1); }
                    else if (defName == "Masochist") { TaleHelper.Record("RJWSH_Tale_BrokenIn", Pawn); HarassmentEngine.Chronicle(Pawn, "Something gave way - the pain and degradation became a craving.", 1); }

                    if (defName == "RJWSH_StockholmSyndrome")
                        Find.LetterStack.ReceiveLetter("Conditioned: " + Pawn.LabelShort,
                            Pawn.LabelShortCap + " has been completely broken to the collar. They are devoted to their owner now, body and mind - there is no going back.",
                            LetterDefOf.NeutralEvent, new LookTargets(Pawn));
                    else if (defName == "Masochist")
                        Find.LetterStack.ReceiveLetter("Conditioned: " + Pawn.LabelShort,
                            Pawn.LabelShortCap + " has changed under the collar. The pain and degradation no longer break them - they have started to crave it.",
                            LetterDefOf.NeutralEvent, new LookTargets(Pawn));
                    else
                        Messages.Message(Pawn.LabelShortCap + " has been conditioned and gained " + label + ".",
                            new LookTargets(Pawn), MessageTypeDefOf.NeutralEvent, false);
                }
            }
            catch { }
        }

        // At a conditioning milestone, permanently install a fitting RJW quirk (Cumslut, Exhibitionist).
        private void TryInstallConditioningQuirk(string quirkDefName)
        {
            if (!RimJobWorldSexualHarassmentMod.Settings.conditioningInstallsQuirks) return;
            if (QuirksBridge.HasQuirk(Pawn, quirkDefName)) return;
            if (QuirksBridge.TryAddQuirk(Pawn, quirkDefName) && PawnUtility.ShouldSendNotificationAbout(Pawn))
                Messages.Message(Pawn.LabelShortCap + " has been conditioned into a new quirk.",
                    new LookTargets(Pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        // Full-break conditioning etches a heritable RJW Genes trait into the pet (needs Biotech + RJW Genes).
        private void TryInstallConditioningGene()
        {
            if (!RimJobWorldSexualHarassmentMod.Settings.conditioningInstallsGene) return;
            // A full break etches heritable submissiveness (and, with RJW Genes, hypersexuality) into the pet's line.
            bool added = GeneHelper.TryAddEndogene(Pawn, "RJWSH_Gene_Submissive");
            added |= GeneHelper.TryAddEndogene(Pawn, "rjw_genes_hypersexual");
            if (added && PawnUtility.ShouldSendNotificationAbout(Pawn))
                Messages.Message(Pawn.LabelShortCap + "'s body has been reshaped by their conditioning.",
                    new LookTargets(Pawn), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
