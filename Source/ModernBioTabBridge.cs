using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Modern Bio Tab (astryl.ModernBioTab) integration. Preferred: register our compact attribute block into
    /// MBT's existing Sexuality panel via its RegisterSexualityStat hook (MBT owns the layout / stats-mode
    /// gating, so no fragile drawer patch). If that hook isn't present yet, fall back to registering a
    /// standalone "Sexual Attributes" panel via the public API. Either way SexualityTabInjector suppresses the
    /// standalone inspect tab when MBT is present. Reflection-based - no hard dependency on Modern Bio Tab.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModernBioTabBridge
    {
        private static Vector2 _scroll;
        private const string ModId = "astryl.RJWSexualHarassment";

        /// <summary>True when Modern Bio Tab's API is available (used to suppress the standalone tab).</summary>
        public static bool Active => AccessTools.TypeByName("ModernBioTab.ModernBioTabAPI") != null;

        static ModernBioTabBridge()
        {
            try
            {
                var apiType = AccessTools.TypeByName("ModernBioTab.ModernBioTabAPI");
                if (apiType == null) return;

                // Preferred: append into the existing Sexuality panel via MBT's own hook (it handles layout +
                // stats-mode gating). Try a measure+draw signature first, then a plain draw signature.
                if (TryRegisterSexualityStat(apiType)) return;

                // Fallback: a standalone panel through the public panel API.
                if (TryRegisterStandalonePanel(apiType)) return;

                Log.Warning("[RJW Sexual Harassment] Modern Bio Tab present but neither RegisterSexualityStat nor RegisterPanel matched.");
            }
            catch (Exception e)
            {
                Log.Warning("[RJW Sexual Harassment] Modern Bio Tab integration failed (non-fatal): " + e.Message);
            }
        }

        private static bool TryRegisterSexualityStat(Type apiType)
        {
            // Signature A (measure-then-draw): RegisterSexualityStat(string modId, Func<Pawn,float> measure, Action<Rect,Pawn> draw)
            var measureDraw = AccessTools.Method(apiType, "RegisterSexualityStat",
                new[] { typeof(string), typeof(Func<Pawn, float>), typeof(Action<Rect, Pawn>) });
            if (measureDraw != null)
            {
                Func<Pawn, float> measure = SexualityPanelDrawer.MeasureCompact;
                Action<Rect, Pawn> draw = SafeDrawCompact;
                measureDraw.Invoke(null, new object[] { ModId, measure, draw });
                Log.Message("[RJW Sexual Harassment] appended Sexual Attributes into Modern Bio Tab's Sexuality panel (measure+draw).");
                return true;
            }

            // Signature B (draw only): RegisterSexualityStat(string modId, Action<Rect,Pawn> draw)
            var drawOnly = AccessTools.Method(apiType, "RegisterSexualityStat",
                new[] { typeof(string), typeof(Action<Rect, Pawn>) });
            if (drawOnly != null)
            {
                Action<Rect, Pawn> draw = SafeDrawCompact;
                drawOnly.Invoke(null, new object[] { ModId, draw });
                Log.Message("[RJW Sexual Harassment] appended Sexual Attributes into Modern Bio Tab's Sexuality panel.");
                return true;
            }
            return false;
        }

        private static bool TryRegisterStandalonePanel(Type apiType)
        {
            var m = AccessTools.Method(apiType, "RegisterPanel", new[]
            {
                typeof(string), typeof(string), typeof(string),
                typeof(Action<Rect, Pawn>), typeof(Func<Pawn, bool>), typeof(float)
            });
            if (m == null) return false;
            Action<Rect, Pawn> draw = (rect, pawn) => { try { SexualityPanelDrawer.Draw(rect, pawn, ref _scroll); } catch { } };
            Func<Pawn, bool> available = (pawn) => pawn?.RaceProps != null && pawn.RaceProps.Humanlike;
            m.Invoke(null, new object[] { ModId, "SexAttributes", "Sexual Attributes", draw, available, 1f });
            Log.Message("[RJW Sexual Harassment] registered a standalone Sexual Attributes panel with Modern Bio Tab (RegisterSexualityStat not available yet).");
            return true;
        }

        private static void SafeDrawCompact(Rect rect, Pawn pawn)
        {
            try { SexualityPanelDrawer.DrawCompact(rect, pawn); } catch { }
        }
    }
}
