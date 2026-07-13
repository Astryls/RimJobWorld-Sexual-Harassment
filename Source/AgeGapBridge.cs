using System.Reflection;
using HarmonyLib;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Soft reflection bridge to the Age Gap Attraction addon. Returns a victim-selection weight multiplier
    /// (1.0 = neutral) so an inclined harasser prefers younger adult targets and an averse harasser avoids
    /// large-gap targets. No-ops (returns 1) when Age Gap Attraction is absent.
    /// </summary>
    public static class AgeGapBridge
    {
        private static bool _init;
        private static MethodInfo _factor;

        private static void Init()
        {
            _init = true;
            try
            {
                var t = AccessTools.TypeByName("AgeGapAttraction.HarassmentApi");
                if (t != null)
                    _factor = AccessTools.Method(t, "HarassWeightFactor", new[] { typeof(Pawn), typeof(Pawn) });
            }
            catch { }
        }

        public static float WeightFactor(Pawn harasser, Pawn target)
        {
            if (!_init) Init();
            if (_factor == null) return 1f;
            try { return (float)_factor.Invoke(null, new object[] { harasser, target }); }
            catch { return 1f; }
        }
    }
}
