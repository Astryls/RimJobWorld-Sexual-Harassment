using System;
using System.Reflection;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Snapshot of a pawn's RJW Sexperience sex history.</summary>
    public class SexHistoryData
    {
        public int totalSex, partners, virginsTaken, raped, beenRaped;
        public float avgSat;
        public string recentPartner, mostPartner, firstPartner, bestSextype;
    }

    /// <summary>Reads RJW Sexperience's per-pawn SexHistoryComp by reflection. Sexperience is a SOFT dependency, so
    /// there is no compile-time reference to its assembly; everything degrades to null when it is absent.</summary>
    public static class SexHistoryBridge
    {
        private static bool _resolved;
        private static Type _compType;
        private static PropertyInfo _pTotalSex, _pPartnerCount, _pVirginsTaken, _pRaped, _pBeenRaped, _pAvgSat, _pRecent, _pMost, _pFirst, _rLabel;
        private static MethodInfo _mBestSextype;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!SoftDeps.SexperienceActive) return;
            _compType = GenTypes.GetTypeInAnyAssembly("RJWSexperience.SexHistory.SexHistoryComp");
            if (_compType == null) return;
            _pTotalSex = _compType.GetProperty("TotalSexHad");
            _pPartnerCount = _compType.GetProperty("PartnerCount");
            _pVirginsTaken = _compType.GetProperty("VirginsTaken");
            _pRaped = _compType.GetProperty("RapedCount");
            _pBeenRaped = _compType.GetProperty("BeenRapedCount");
            _pAvgSat = _compType.GetProperty("AVGSat");
            _pRecent = _compType.GetProperty("RecentPartnerRecord");
            _pMost = _compType.GetProperty("MostPartnerRecord");
            _pFirst = _compType.GetProperty("FirstPartnerRecord");
            _mBestSextype = _compType.GetMethod("GetBestSextype");
            var recType = GenTypes.GetTypeInAnyAssembly("RJWSexperience.SexHistory.SexPartnerHistoryRecord");
            _rLabel = recType?.GetProperty("Label");
        }

        private static object Comp(Pawn p)
        {
            Resolve();
            if (_compType == null || p == null) return null;
            var comps = p.AllComps;
            if (comps == null) return null;
            for (int i = 0; i < comps.Count; i++)
                if (comps[i] != null && _compType.IsInstanceOfType(comps[i])) return comps[i];
            return null;
        }

        public static bool Available(Pawn p) => Comp(p) != null;

        public static SexHistoryData Read(Pawn p)
        {
            var c = Comp(p);
            if (c == null) return null;
            var d = new SexHistoryData
            {
                totalSex = (int)GetF(_pTotalSex, c),
                partners = GetI(_pPartnerCount, c),
                virginsTaken = GetI(_pVirginsTaken, c),
                raped = GetI(_pRaped, c),
                beenRaped = GetI(_pBeenRaped, c),
                avgSat = GetF(_pAvgSat, c),
                recentPartner = Label(_pRecent, c),
                mostPartner = Label(_pMost, c),
                firstPartner = Label(_pFirst, c),
                bestSextype = BestSextype(c),
            };
            return d;
        }

        private static int GetI(PropertyInfo p, object c) { try { return p != null ? Convert.ToInt32(p.GetValue(c, null)) : 0; } catch { return 0; } }
        private static float GetF(PropertyInfo p, object c) { try { return p != null ? Convert.ToSingle(p.GetValue(c, null)) : 0f; } catch { return 0f; } }

        private static string Label(PropertyInfo recProp, object c)
        {
            try
            {
                var rec = recProp?.GetValue(c, null);
                if (rec == null || _rLabel == null) return null;
                return _rLabel.GetValue(rec, null) as string;
            }
            catch { return null; }
        }

        private static string BestSextype(object c)
        {
            if (_mBestSextype == null) return null;
            try
            {
                var args = new object[] { null };
                float sat = Convert.ToSingle(_mBestSextype.Invoke(c, args));
                if (sat <= 0f) return null;
                return args[0]?.ToString();
            }
            catch { return null; }
        }
    }
}
