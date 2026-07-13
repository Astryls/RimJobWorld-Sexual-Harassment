using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The vanilla-Work-tab face of the harem schedule. When a pet is scheduled "Confined" for the current
    /// hour, this issues the stay-at-quarters job through the normal work system (so the "Harem" work priority
    /// governs it during work hours). The other schedule assignments (Serve/Train/Parade) and off-hours
    /// Confined are driven by HarassmentEngine.RunScheduleTick, which is likewise paused when this work type
    /// is disabled (HaremWorkEnabled).
    /// </summary>
    public class WorkGiver_Harem : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
            if (prof?.schedule == null || prof.schedule.Count != 24) return null;
            if (prof.schedule[GenLocalDate.HourOfDay(pawn)] != 5) return null; // 5 = Confined
            IntVec3 dest = HarassmentEngine.ConfinementDest(pawn, prof, null);
            if (!dest.IsValid || pawn.Position == dest || !pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly)) return null;
            return JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_StayPut, dest);
        }
    }
}
