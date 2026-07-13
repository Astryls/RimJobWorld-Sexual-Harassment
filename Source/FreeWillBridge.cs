using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Bridge to a free-will mod so a pawn under active control loses free will (owner-directed tasks, not
    /// self-chosen). Reflection-based and defensive: it probes the well-known Free Will mod
    /// (freemapa.freewill) for a per-pawn free-will toggle and no-ops if the API can't be matched. Wire the
    /// exact member here once the target mod is confirmed. Gated on the suppressFreeWillWhenControlled setting.
    /// </summary>
    public static class FreeWillBridge
    {
        private static bool _tried;
        private static Type _mapCompType;      // FreeWill.FreeWill_MapComponent (or similar)
        private static MethodInfo _setFreeWill; // (Pawn, bool) style setter, if one exists

        public static bool Active
        {
            get
            {
                Ensure();
                return _mapCompType != null;
            }
        }

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                if (ModLister.GetActiveModWithIdentifier("freemapa.freewill", true) == null
                    && ModLister.GetActiveModWithIdentifier("freemapa.freewill") == null) return;
                _mapCompType = AccessTools.TypeByName("FreeWill.FreeWill_MapComponent");
                if (_mapCompType != null)
                {
                    // Probe a couple of plausible per-pawn free-will setters (finalised once confirmed).
                    _setFreeWill = AccessTools.Method(_mapCompType, "SetFreeWill", new[] { typeof(Pawn), typeof(bool) })
                                ?? AccessTools.Method(_mapCompType, "SetPawnFreeWill", new[] { typeof(Pawn), typeof(bool) });
                }
                if (_mapCompType != null && _setFreeWill == null)
                    Log.WarningOnce("[RJW Sexual Harassment] Free Will mod detected but its per-pawn setter was not matched; free-will suppression is idle until the API is wired.", 0x5A1370);
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] Free Will bridge init failed (non-fatal): " + e.Message); }
        }

        /// <summary>Disable free will for a controlled pawn (no-op without the setting, the mod, or a matched API).</summary>
        public static void SuppressFor(Pawn pawn)
        {
            if (pawn?.Map == null) return;
            if (RimJobWorldSexualHarassmentMod.Settings == null || !RimJobWorldSexualHarassmentMod.Settings.suppressFreeWillWhenControlled) return;
            Ensure();
            if (_mapCompType == null || _setFreeWill == null) return;
            try
            {
                var comp = pawn.Map.GetComponent(_mapCompType);
                if (comp != null) _setFreeWill.Invoke(comp, new object[] { pawn, false });
            }
            catch { }
        }
    }
}
