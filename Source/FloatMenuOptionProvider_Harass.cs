using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Player-directed harassment. When RJW's hero control (RPG_hero_control, "hero mode") is on, right-clicking
    /// a pawn with a single colonist selected shows a "Harass [target]..." option that opens a submenu of every
    /// approach. Each routes through the normal paced JobDriver_Harass, so the directed scene plays out with the
    /// same approach -> exchange -> escalation pacing as emergent harassment. Auto-registered via subclass discovery.
    /// </summary>
    public class FloatMenuOptionProvider_Harass : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            if (RimJobWorldSexualHarassmentMod.Settings == null || !RimJobWorldSexualHarassmentMod.Settings.masterEnabled)
                return false;
            try { if (!rjw.RJWSettings.RPG_hero_control) return false; } catch { return false; }
            var p = context.FirstSelectedPawn;
            return p != null && p.RaceProps.Humanlike && p.Faction != null && p.Faction.IsPlayer;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var actor = context.FirstSelectedPawn;
            if (actor == null || clickedPawn == null || clickedPawn == actor) yield break;
            if (!clickedPawn.RaceProps.Humanlike || clickedPawn.Dead) yield break;

            var target = clickedPawn;
            string label = "Harass " + target.LabelShort + "...";

            if (!actor.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + " (" + "NoPath".Translate().CapitalizeFirst() + ")", null);
                yield break;
            }
            if (!rjw.xxx.can_do_loving(actor))
            {
                yield return new FloatMenuOption(label + " (incapable)", null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate { OpenHarassMenu(actor, target); });
        }

        private static void OpenHarassMenu(Pawn actor, Pawn target)
        {
            var opts = new List<FloatMenuOption>();
            void Add(string text, ApproachType type)
            {
                opts.Add(new FloatMenuOption(text, delegate { HarassmentEngine.StartDirectedHarass(actor, target, type); }));
            }
            Add("Catcall", ApproachType.Catcall);
            Add("Proposition", ApproachType.Proposition);
            Add("Flirt", ApproachType.Flirt);
            Add("Offer a spiked drink", ApproachType.SpikedDrink);
            Add("Hypnotize", ApproachType.Hypnosis);
            if (HarassmentEngine.HasPhotoOf(target)) Add("Blackmail with photos", ApproachType.Blackmail);
            Add("Grope", ApproachType.Grope);
            Add("Force (rape)", ApproachType.Forced);
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}
