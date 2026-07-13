using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Interactive training (#5): a short read-the-pet minigame that replaces fire-and-forget Discipline.
    /// Each round the pet is in a hidden state (defiant / fearful / yearning) leaked by a "tell"; the owner
    /// picks Firm / Gentle / Degrade. Match the state and conditioning deepens; misread and you harden their
    /// resistance and pile on trauma. Disposition (traits, quirks, attributes) weights which states appear.
    /// </summary>
    public class Window_TrainingSession : Window
    {
        private enum PetState { Defiant, Fearful, Yearning }
        private enum Approach { Firm, Gentle, Degrade }

        private const int Rounds = 3;

        private readonly Pawn owner;
        private readonly Pawn pet;
        private readonly PawnProfile prof;

        private int round;
        private PetState state;
        private string tell;
        private string lastResult;

        // Accumulated outcome, applied on finish.
        private float condAccum, rapAccum, traumaAccum, willAccum, subdomAccum, esteemAccum;
        private int hits;

        // Per-pawn disposition weights for which state shows up.
        private readonly float wDefiant, wFearful, wYearning;
        private readonly float intensity;

        public override Vector2 InitialSize => new Vector2(540f, 460f);
        protected override float Margin => 0f;   // Modern Suite: we draw our own flat panel edge-to-edge

        public Window_TrainingSession(Pawn owner, Pawn pet, PawnProfile prof)
        {
            this.owner = owner; this.pet = pet; this.prof = prof;
            // NON-pausing: the owner physically walks over and disciplines the pet in the background
            // (Melee Animation plays) while the player reads the tells. forcePause=true would freeze that
            // job, so the animation never played - that was the bug. The reads modulate the outcome on finish.
            forcePause = false;
            doCloseX = false;              // we draw our own Modern-Suite close-X
            doWindowBackground = false;    // no vanilla frame - flat ModernStyle panel instead
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            preventCameraMotion = false;

            var sx = prof?.sex;
            float will = sx?.willpower ?? 50f;
            float trauma = sx?.trauma ?? 0f;
            float subdom = sx?.subDom ?? 0f;   // -100 submissive .. +100 dominant

            bool maso = TraitHooks.HasTraitNamed(pet, "Masochist") || GeneHelper.HasGene(pet, "rjw_genes_masochist")
                        || QuirksBridge.HasQuirk(pet, "Cumslut");
            bool exhib = QuirksBridge.HasQuirk(pet, "Exhibitionist");

            // Defiant: dominant/willful and not yet broken. Fearful: traumatized, low will. Yearning: masochist/craving.
            wDefiant = Mathf.Clamp(0.3f + subdom / 200f + will / 300f, 0.05f, 1f);
            wFearful = Mathf.Clamp(0.3f + trauma / 150f + (60f - will) / 300f, 0.05f, 1f);
            wYearning = Mathf.Clamp(0.2f + (maso ? 0.4f : 0f) + (exhib ? 0.2f : 0f) - subdom / 300f, 0.05f, 1f);

            intensity = 1f * (HarassmentEngine.ConditioningReceptivity(pet));
            NextRound();

            // Kick off the physical discipline immediately so the owner walks over and the animation plays
            // out while the session runs. Its base effect lands from the job; the reads add a modifier.
            if (owner != null && pet != null) HarassmentEngine.StartDiscipline(owner, pet);
        }

        private void NextRound()
        {
            lastResult = null;
            float total = wDefiant + wFearful + wYearning;
            float r = Rand.Value * total;
            if (r < wDefiant) state = PetState.Defiant;
            else if (r < wDefiant + wFearful) state = PetState.Fearful;
            else state = PetState.Yearning;

            switch (state)
            {
                case PetState.Defiant:
                    tell = pet.LabelShortCap + " meets your eyes, chin raised. There is still fight in " + Pronoun() + ".";
                    break;
                case PetState.Fearful:
                    tell = pet.LabelShortCap + " flinches and stares at the floor, trembling.";
                    break;
                default:
                    tell = pet.LabelShortCap + " leans toward you, breath quickening, hungry for the attention.";
                    break;
            }
        }

        private string Pronoun() => pet.gender == Gender.Female ? "her" : (pet.gender == Gender.Male ? "him" : "them");

        private static Approach Correct(PetState s) =>
            s == PetState.Defiant ? Approach.Firm : (s == PetState.Fearful ? Approach.Gentle : Approach.Degrade);

        private void Choose(Approach a)
        {
            bool right = a == Correct(state);
            float mag = Mathf.Lerp(3f, 7f, Mathf.Clamp01(intensity)) ;

            if (right)
            {
                hits++;
                condAccum += mag;
                subdomAccum -= mag * 0.8f;
                traumaAccum -= mag * 0.3f;   // a good read soothes rather than scars
                if (a == Approach.Gentle) rapAccum += mag;                 // reassurance builds trust
                else if (a == Approach.Firm) { rapAccum -= mag * 0.4f; }   // dominance breaks defiance, costs a little trust
                else esteemAccum -= mag * 0.6f;                            // degradation feeds the craving, erodes self-worth
                lastResult = right ? "You read " + Pronoun() + " right. Conditioning deepens." : null;
            }
            else
            {
                condAccum -= mag * 0.4f;
                traumaAccum += mag * 0.9f;
                willAccum += mag * 0.7f;    // a misread hardens resistance
                rapAccum -= mag * 0.6f;
                lastResult = "You misread " + Pronoun() + ". " + pet.LabelShortCap + " recoils - resistance hardens.";
            }

            round++;
            if (round >= Rounds) Finish();
            else NextRound();
        }

        private void Finish()
        {
            if (prof != null)
            {
                prof.ApplyCond("Training session", condAccum, rapAccum);
                HarassmentEngine.AttrDelta(pet, will: willAccum, esteem: esteemAccum, trauma: traumaAccum, subdom: subdomAccum);
                string verdict = hits >= 3 ? "a flawless session" : hits >= 2 ? "a good session" : hits >= 1 ? "a clumsy session" : "a botched session";
                HarassmentEngine.Chronicle(pet, "Training with " + (owner != null ? owner.LabelShortCap : "their owner") + " - " + verdict + ".", hits >= 2 ? 1 : 0);
            }
            if (owner != null && pet != null)
                Messages.Message(owner.LabelShortCap + " finished training " + pet.LabelShort + " (" + hits + "/" + Rounds + " read right).",
                    new LookTargets(pet), hits >= 2 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
            Close();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Flat Modern-Suite panel + border (matches the Command deck / photo gallery chrome).
            Widgets.DrawBoxSolid(inRect, ModernStyle.BGD);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(inRect, 1); GUI.color = Color.white;
            var pad = inRect.ContractedBy(14f);

            // Our own close-X (no vanilla frame X).
            var closeR = new Rect(pad.xMax - 24f, pad.y + 2f, 24f, 24f);
            GUI.color = ModernStyle.TextDim;
            if (Widgets.ButtonText(closeR, "\u2715", drawBackground: false)) { Close(); return; }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(closeR, "Close");

            Text.Font = GameFont.Medium;
            GUI.color = ModernStyle.Accent;
            Widgets.Label(new Rect(pad.x, pad.y, pad.width - 30f, 34f), "Training - " + pet.LabelShortCap);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(pad.x, pad.y + 34f, pad.width, 22f), "Round " + Mathf.Min(round + 1, Rounds) + " / " + Rounds);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            float y = pad.y + 60f;

            // Current-state tell card.
            var tellCard = new Rect(pad.x, y, pad.width, 78f);
            ModernStyle.DrawCard(tellCard);
            GUI.color = ModernStyle.Body;
            Widgets.Label(tellCard.ContractedBy(10f), tell);
            GUI.color = Color.white;
            y += tellCard.height + 10f;

            // Last-round feedback.
            if (!string.IsNullOrEmpty(lastResult))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.7f, 0.75f, 0.8f);
                Widgets.Label(new Rect(pad.x, y, pad.width, 20f), lastResult);
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            y += 24f;

            // Three approach buttons.
            float bh = 46f, gap = 8f;
            if (ApproachButton(new Rect(pad.x, y, pad.width, bh), "Firm hand", "Assert dominance - break defiance with a steady, unyielding hand."))
                Choose(Approach.Firm);
            y += bh + gap;
            if (ApproachButton(new Rect(pad.x, y, pad.width, bh), "Gentle touch", "Reassure and soothe - reach a frightened pet with kindness."))
                Choose(Approach.Gentle);
            y += bh + gap;
            if (ApproachButton(new Rect(pad.x, y, pad.width, bh), "Degrade", "Feed the craving - humiliation and attention for a pet who wants it."))
                Choose(Approach.Degrade);

            // Footer hint.
            Text.Font = GameFont.Tiny;
            GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(pad.x, pad.yMax - 20f, pad.width, 18f),
                "Read the tell, then match your approach to " + Pronoun() + " state.");
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        private bool ApproachButton(Rect r, string label, string tip)
        {
            bool hover = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, hover ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.16f) : ModernStyle.PanelBG);
            if (hover) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 2f, r.height), ModernStyle.Accent);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = hover ? Color.white : ModernStyle.Body;
            Widgets.Label(r, label);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }
    }
}
