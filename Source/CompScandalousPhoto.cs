using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    public class CompProperties_ScandalousPhoto : CompProperties
    {
        public CompProperties_ScandalousPhoto() { compClass = typeof(CompScandalousPhoto); }
    }

    /// <summary>Custom photo item so the map-hover readout (LabelMouseover) shows what the photo depicts,
    /// not just the item name. The full lore also feeds the inspect card via CompInspectStringExtra.</summary>
    public class Thing_ScandalousPhoto : ThingWithComps
    {
        private string LorePlus
        {
            get
            {
                var c = GetComp<CompScandalousPhoto>();
                if (c == null || c.loreDesc.NullOrEmpty()) return null;
                return c.loreDesc + (c.distributed ? "\nCopies are in circulation." : "");
            }
        }

        // Map-hover readout.
        public override string LabelMouseover
        {
            get
            {
                string b = base.LabelMouseover;
                string lore = LorePlus;
                return lore == null ? b : b + "\n" + lore;
            }
        }

        // Inventory/Gear-tab tooltip (and other GetTooltip callers) show the same lore as the map hover.
        public override TipSignal GetTooltip()
        {
            var tip = base.GetTooltip();
            string lore = LorePlus;
            if (lore != null) tip.text += "\n\n" + lore;
            return tip;
        }
    }

    /// <summary>
    /// Holds who a scandalous photo is of and the generated lore describing the captured act. Provides
    /// the inspect text and a "Destroy" command so the player can defuse the blackmail leverage.
    /// </summary>
    public class CompScandalousPhoto : ThingComp
    {
        public Pawn subject;
        public string loreDesc;
        public bool distributed;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref subject, "subject");
            Scribe_Values.Look(ref loreDesc, "loreDesc");
            Scribe_Values.Look(ref distributed, "distributed", false);
        }

        // Each photo is unique (its own subject + lore), so it must never merge into a stack.
        public override bool AllowStackWith(Thing other) => false;

        public override string TransformLabel(string label)
        {
            return subject != null ? label + " of " + subject.LabelShort : label;
        }

        public override string CompInspectStringExtra()
        {
            if (loreDesc.NullOrEmpty()) return null;
            return loreDesc + (distributed ? "\nCopies are in circulation." : "");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (subject != null)
                yield return new Command_Action
                {
                    defaultLabel = "Auction photo",
                    defaultDesc = "Sell this scandalous photo to passing traders for silver. " + subject.LabelShort
                        + " will be humiliated when they find out - and if they are a collared pet, word spreads and curious visitors come to see them in person.",
                    icon = HarassmentTextures.BurnPhoto,
                    action = delegate { HarassmentEngine.AuctionPhoto(subject, parent); }
                };
            if (subject != null)
                yield return new Command_Action
                {
                    defaultLabel = "Blackmail",
                    defaultDesc = "Threaten a faction with this photo of " + subject.LabelShort
                        + " and demand silver. They may pay to keep it quiet - or call your bluff, turn on you, and send people to silence you.",
                    icon = HarassmentTextures.BurnPhoto,
                    action = delegate { HarassmentEngine.OpenBlackmailMenu(subject, parent); }
                };
            yield return new Command_Action
            {
                defaultLabel = "Destroy photo",
                defaultDesc = "Burn this photo so it can never be used for blackmail.",
                icon = HarassmentTextures.BurnPhoto,
                action = delegate
                {
                    if (!parent.Destroyed) parent.Destroy(DestroyMode.Vanish);
                }
            };
        }
    }
}
