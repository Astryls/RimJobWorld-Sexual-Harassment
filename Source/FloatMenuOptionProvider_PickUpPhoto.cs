using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Adds a "Pick up [photo]" float-menu option for scandalous photos, so a colonist can carry one in
    /// their inventory (and, if it depicts a pawn they harass, lean on it as repeat blackmail leverage).
    /// Auto-registered via subclass discovery.
    /// </summary>
    public class FloatMenuOptionProvider_PickUpPhoto : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var p = context.FirstSelectedPawn;
            return p != null && p.RaceProps.Humanlike && p.inventory != null;
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing == null || clickedThing.def != RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) return null;

            var pawn = context.FirstSelectedPawn;
            string label = "Pick up " + clickedThing.LabelShort;

            if (!pawn.CanReach(clickedThing, PathEndMode.ClosestTouch, Danger.Deadly))
                return new FloatMenuOption(label + " (" + "NoPath".Translate().CapitalizeFirst() + ")", null);
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return new FloatMenuOption(label + " (" + "Incapable".Translate().CapitalizeFirst() + ")", null);

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate
            {
                var job = JobMaker.MakeJob(JobDefOf.TakeInventory, clickedThing);
                job.count = 1;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), pawn, clickedThing);
        }
    }
}
