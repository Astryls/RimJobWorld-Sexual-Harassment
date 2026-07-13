using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Blackmail as a weapon (#6): scandalous photos stop being passive. Threaten a faction with a
    /// photo of one of their own (or someone they would rather not see disgraced) and demand silver.
    /// They may pay grudgingly - or call the bluff, sour on you, and send someone to shut you up.
    /// </summary>
    public static partial class HarassmentEngine
    {
        /// <summary>Opens a faction picker for extorting silver with this photo. Each entry shows the demand.</summary>
        public static void OpenBlackmailMenu(Pawn subject, Thing photo)
        {
            if (subject == null || photo == null) return;
            var opts = new List<FloatMenuOption>();
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.IsPlayer || f.defeated || f.Hidden || f.temporary) continue;
                if (f.def == null || !f.def.humanlikeFaction) continue;
                var faction = f;
                int demand = BlackmailDemand(subject, photo, faction);
                float chance = BlackmailSuccessChance(photo, faction);
                opts.Add(new FloatMenuOption(
                    faction.Name + " - demand " + demand + " silver  (" + chance.ToStringPercent("0") + " comply)",
                    () => ExecuteBlackmail(subject, photo, faction, demand)));
            }
            if (opts.Count == 0)
            {
                Messages.Message("There is no faction to blackmail.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static int BlackmailDemand(Pawn subject, Thing photo, Faction faction)
        {
            int demand = 250;
            try { demand += (int)(subject.GetStatValue(StatDefOf.PawnBeauty) * 80f); } catch { }
            if (WearingControlCollar(subject)) demand += 150;                 // a collared subject is juicier leverage
            if (faction.leader == subject || RelatedToFaction(subject, faction)) demand += 200; // it is their own
            return Mathf.Max(100, demand);
        }

        private static bool RelatedToFaction(Pawn subject, Faction faction)
        {
            if (subject?.relations == null) return false;
            foreach (var rel in subject.relations.DirectRelations)
                if (rel?.otherPawn?.Faction == faction) return true;
            return subject.Faction == faction;
        }

        private static float BlackmailSuccessChance(Thing photo, Faction faction)
        {
            float c = 0.6f;
            var comp = photo.TryGetComp<CompScandalousPhoto>();
            if (comp == null || !comp.distributed) c += 0.15f;   // fresh, uncirculated leverage is more frightening
            if (faction.HostileTo(Faction.OfPlayer)) c -= 0.25f; // an enemy cares little for your threats
            return Mathf.Clamp(c, 0.15f, 0.9f);
        }

        private static void ExecuteBlackmail(Pawn subject, Thing photo, Faction faction, int demand)
        {
            var map = photo.MapHeld ?? subject.MapHeld ?? Find.AnyPlayerHomeMap;
            var comp = photo.TryGetComp<CompScandalousPhoto>();
            float chance = BlackmailSuccessChance(photo, faction);
            bool paid = Rand.Chance(chance);

            // The threat is made: the photo is now known leverage (marks it circulated, spends the surprise).
            if (comp != null && !comp.distributed)
            {
                comp.distributed = true;
                GameComponent_Harassment.Instance?.AddCirculatingPhoto(subject, comp.loreDesc, "Held over " + faction.Name);
            }
            GameComponent_Harassment.Instance?.AddNotoriety(2, "rjwsh_blackmail");

            if (paid)
            {
                if (map != null)
                {
                    var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = demand;
                    var cell = DropCellFinder.TradeDropSpot(map);
                    DropPodUtility.DropThingsNear(cell, map, Gen.YieldSingle<Thing>(silver), 110, false, false, false, false);
                }
                try { faction.TryAffectGoodwillWith(Faction.OfPlayer, -4, false, false, null, null); } catch { }
                Chronicle(subject, "Their photo was used to extort " + demand + " silver from " + faction.Name + ".", 3);
                Find.LetterStack.ReceiveLetter("Blackmail paid",
                    faction.Name + " would rather not see " + subject.LabelShortCap + "'s photo spread. "
                    + demand + " silver has been dropped off. They will remember this humiliation.",
                    LetterDefOf.PositiveEvent, map != null ? new LookTargets(DropCellFinder.TradeDropSpot(map), map) : null, faction);
            }
            else
            {
                try { faction.TryAffectGoodwillWith(Faction.OfPlayer, -10, false, false, null, null); } catch { }
                GameComponent_Harassment.Instance?.AddNotoriety(4, "rjwsh_blackmail");
                Chronicle(subject, "A blackmail threat against " + faction.Name + " was called - the photo leaked instead.", 3);
                bool raid = Rand.Chance(0.3f) && map != null
                    && SpawnAssaultRaid(map, faction, Mathf.Max(StorytellerUtility.DefaultThreatPointsNow(map), 300f), null,
                        "Blackmail backfires", faction.Name + " refused to be extorted over " + subject.LabelShortCap
                        + " and has sent people to make the point that they will not be threatened.");
                if (!raid)
                    Find.LetterStack.ReceiveLetter("Blackmail refused",
                        faction.Name + " called your bluff over " + subject.LabelShortCap + ". No silver comes - only their contempt.",
                        LetterDefOf.NegativeEvent, null, faction);
            }
        }
    }
}
