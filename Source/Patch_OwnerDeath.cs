using HarmonyLib;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>When any humanlike dies, check whether they were a pet-owner and run the legacy reactions
    /// (#3). Postfix so the death has fully resolved; guarded so a thrown handler never blocks the kill.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_OwnerDeath
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance == null || !__instance.RaceProps.Humanlike) return;
            try { HarassmentEngine.OnOwnerDied(__instance); }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] owner-death legacy failed: " + ex.Message, 0x5A1411);
            }
        }
    }
}
