using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Injects the Sexuality inspect tab onto every humanlike ThingDef at startup (covers alien races
    /// too, not just vanilla Human), so any pawn can be inspected for their sexual attributes.</summary>
    [StaticConstructorOnStartup]
    public static class SexualityTabInjector
    {
        static SexualityTabInjector()
        {
            try
            {
                // When Modern Bio Tab is present, the sexual attributes are shown inside its bio tab
                // (ModernBioTabBridge) - so do NOT also inject the standalone Sexuality inspect tab.
                if (AccessTools.TypeByName("ModernBioTab.ModernBioTabAPI") != null)
                {
                    Log.Message("[RJW Sexual Harassment] Modern Bio Tab detected - standalone Sexuality tab suppressed (shown in the bio tab instead).");
                    return;
                }
                InjectTab(typeof(ITab_Pawn_Sexuality));
                Log.Message("[RJW Sexual Harassment] Sexuality tab injected onto humanlike races.");
            }
            catch (Exception e)
            {
                Log.Warning("[RJW Sexual Harassment] tab injection failed: " + e.Message);
            }
        }

        private static void InjectTab(Type tabType)
        {
            var shared = InspectTabManager.GetSharedInstance(tabType);
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
            {
                if (d?.race == null || !d.race.Humanlike) continue;
                if (d.inspectorTabs == null) d.inspectorTabs = new List<Type>();
                if (d.inspectorTabs.Contains(tabType)) continue;
                d.inspectorTabs.Add(tabType);
                if (d.inspectorTabsResolved == null) d.inspectorTabsResolved = new List<InspectTabBase>();
                d.inspectorTabsResolved.Add(shared);
            }
        }
    }
}
