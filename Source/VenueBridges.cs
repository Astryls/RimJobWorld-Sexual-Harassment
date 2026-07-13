using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Bridge to Hospitality: Room Service. A collared pet set up with a solicitation bed can be directed to
    /// provide intimate room service to guests, driven entirely by Room Service's OWN validated job so we can
    /// never construct a broken job. Room Service already reduces a slave guest-server's will and can break
    /// unwavering slaves, so this dovetails with our conditioning. No-ops without the mod.
    /// </summary>
    public static class RoomServiceBridge
    {
        private static bool _tried;
        private static Type _util;
        private static MethodInfo _getBed, _canSolicit, _markAttempted;
        private static JobDef _solicitJob;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                _util = AccessTools.TypeByName("HospitalityRoomService.RoomServiceUtility");
                if (_util != null)
                {
                    _getBed = AccessTools.Method(_util, "GetSolicitationBed", new[] { typeof(Pawn) });
                    _canSolicit = AccessTools.Method(_util, "CanSolicit", new[] { typeof(Pawn), typeof(Pawn) });
                    _markAttempted = AccessTools.Method(_util, "MarkAttempted", new[] { typeof(Pawn), typeof(Pawn) });
                }
                _solicitJob = DefDatabase<JobDef>.GetNamedSilentFail("RoomService_SolicitGuest");
            }
            catch (Exception ex) { Log.Warning("[RJW Sexual Harassment] Room Service bridge init failed (non-fatal): " + ex.Message); }
        }

        public static bool Active { get { Ensure(); return _util != null && _solicitJob != null; } }

        /// <summary>True if the pet is a valid Room Service solicitor (owns an enabled 2-slot solicitation bed).</summary>
        public static bool CanSolicitGuests(Pawn pet)
        {
            Ensure();
            if (_getBed == null || pet == null) return false;
            try { return _getBed.Invoke(null, new object[] { pet }) != null; } catch { return false; }
        }

        /// <summary>Finds a nearby guest the pet can solicit, using Room Service's own CanSolicit gate.</summary>
        public static Pawn FindSolicitTarget(Pawn pet)
        {
            Ensure();
            if (_canSolicit == null || pet?.Map == null) return null;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var g = pawns[i];
                if (g == pet || !g.RaceProps.Humanlike || g.Dead || !g.Spawned) continue;
                if (pet.Position.DistanceTo(g.Position) > 30f) continue;
                try { if ((bool)_canSolicit.Invoke(null, new object[] { pet, g })) return g; } catch { }
            }
            return null;
        }

        /// <summary>Directs the pet to solicit the guest via Room Service's own RoomService_SolicitGuest job.</summary>
        public static bool SolicitGuest(Pawn pet, Pawn guest)
        {
            Ensure();
            if (pet?.jobs == null || guest == null || _solicitJob == null) return false;
            try
            {
                _markAttempted?.Invoke(null, new object[] { pet, guest });
                pet.jobs.StartJob(JobMaker.MakeJob(_solicitJob, guest), JobCondition.InterruptForced);
                return pet.CurJobDef == _solicitJob;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Bridge to Gastronomy. A pet employed at a restaurant can be directed to wait tables using Gastronomy's
    /// own WorkGiver_Serve to produce a validated serve job. No-ops without the mod or when the pet is not
    /// employed / has nothing to serve.
    /// </summary>
    public static class GastronomyBridge
    {
        private static bool _tried;
        private static object _wg;   // a WorkGiver_Serve instance (its Potential/HasJob/JobOnThing don't use this.def)
        private static MethodInfo _potential, _hasJob, _jobOnThing;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var t = AccessTools.TypeByName("Gastronomy.Waiting.WorkGiver_Serve");
                if (t == null) return;
                _wg = Activator.CreateInstance(t);
                _potential = AccessTools.Method(t, "PotentialWorkThingsGlobal", new[] { typeof(Pawn) });
                _hasJob = AccessTools.Method(t, "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                _jobOnThing = AccessTools.Method(t, "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            }
            catch (Exception ex) { Log.Warning("[RJW Sexual Harassment] Gastronomy bridge init failed (non-fatal): " + ex.Message); }
        }

        public static bool Active { get { Ensure(); return _wg != null; } }

        /// <summary>If the pet is an employed waiter with a pending order, starts a validated Gastronomy serve job.</summary>
        public static bool TryServe(Pawn pet)
        {
            Ensure();
            if (_wg == null || pet?.jobs == null) return false;
            try
            {
                var things = _potential.Invoke(_wg, new object[] { pet }) as IEnumerable;
                if (things == null) return false;
                foreach (var o in things)
                {
                    var thing = o as Thing;
                    if (thing == null) continue;
                    if (!(bool)_hasJob.Invoke(_wg, new object[] { pet, thing, false })) continue;
                    if (_jobOnThing.Invoke(_wg, new object[] { pet, thing, false }) is Job job)
                    {
                        pet.jobs.StartJob(job, JobCondition.InterruptForced);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
