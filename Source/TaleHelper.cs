using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Safe tale recording: RJWSH milestones become Tales so vanilla art (sculptures, drug lord
    /// murals, grand slabs) can immortalize them - "the day Kira was collared" three years later.
    /// Null-guarded (defs may be edited out) and try/caught so story flavor never breaks a job.
    /// </summary>
    public static class TaleHelper
    {
        public static void Record(string defName, params object[] args)
        {
            try
            {
                var def = DefDatabase<TaleDef>.GetNamedSilentFail(defName);
                if (def == null) return;
                TaleRecorder.RecordTale(def, args);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] tale record failed (" + defName + "): " + ex.Message, 0x5A1401);
            }
        }
    }
}
