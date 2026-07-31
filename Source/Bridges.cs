using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Runtime detection of optional companion mods. RJW + Harmony are hard deps.</summary>
    public static class SoftDeps
    {
        public static bool KarmaActive { get; private set; }
        public static bool SpeakUpActive { get; private set; }
        public static bool RimTalkActive { get; private set; }
        public static bool FacialAnimActive { get; private set; }
        public static bool OnaholeActive { get; private set; }
        public static bool SimpleSlaveryCollarsActive { get; private set; }
        public static bool BondageBedActive { get; private set; }   // Mlie.BondageBedTorture
        // A personality-sexuality framework that patches vanilla RelationsUtility.AttractedToGender.
        public static bool SexualityFrameworkActive { get; private set; }
        public static bool QuirksActive { get; private set; }        // rjw.quirks (RJW Quirks)
        public static bool SexperienceActive { get; private set; }   // rjw.sexperience (RJW Sexperience)
        public static bool RjwGenesActive { get; private set; }      // Vegapnk.rjw.genes (RJW Genes)

        /// <summary>
        /// Active-mod probe that tolerates Steam's packageId postfix. A Steam-subscribed mod is registered
        /// under "&lt;packageId&gt;" + ModMetaData.SteamModPostfix ("_steam"), so the default exact-match lookup
        /// (ModLister.modsByPackageId) MISSES it entirely - RimWorld even marks the non-postfix-aware ModsConfig
        /// helpers [Obsolete] for this reason. Passing ignorePostfix:true routes through
        /// modsByPackageIdIgnorePostfix, which matches both the local and the Steam copy. Always use this;
        /// never call GetActiveModWithIdentifier directly for a soft dep.
        /// </summary>
        public static bool ModActive(string packageId)
        {
            try
            {
                return ModLister.GetActiveModWithIdentifier(packageId, true) != null
                       || ModLister.GetActiveModWithIdentifier(packageId) != null;
            }
            catch { return false; }
        }

        public static void Detect()
        {
            OnaholeActive = ModActive("rim.job.world.onahole.ext");
            SimpleSlaveryCollarsActive = ModActive("TRIBeagle.simpleslaverycollars");
            BondageBedActive = ModActive("Mlie.BondageBedTorture");
            KarmaActive = ModActive("astryl.KarmaReputation");
            SpeakUpActive = ModActive("JPT.speakup");
            RimTalkActive = ModActive("cj.rimtalk");
            FacialAnimActive = ModActive("Nals.FacialAnimation");
            SexualityFrameworkActive =
                ModActive("Maux36.Rimpsyche.Sexuality")
                || ModActive("Community.Psychology.UnofficialUpdate")
                || ModActive("zora.individuality")
                || ModActive("Syrchalis.Individuality");
            QuirksActive = ModActive("rjw.quirks");
            SexperienceActive = ModActive("rjw.sexperience");
            RjwGenesActive = ModActive("Vegapnk.rjw.genes");

            KarmaBridge.Init();
            ReputationBridge.Init();
            RimTalkBridge.Init();
            FABridge.Init();

            Log.Message($"[RJW Sexual Harassment] soft-deps: Karma={KarmaActive}, SpeakUp={SpeakUpActive}, RimTalk={RimTalkActive}, FacialAnim={FacialAnimActive}, Onahole={OnaholeActive}, SexualityFramework={SexualityFrameworkActive}, Quirks={QuirksActive}, Sexperience={SexperienceActive}, RjwGenes={RjwGenesActive}");
        }
    }

    /// <summary>
    /// Reflection bridge to [NL] Facial Animation. Forces a temporary face animation (e.g. the "Strip"
    /// blush) on a pawn via the public static FacialAnimationControllerComp.PlayTemporaryAnimation.
    /// No-ops gracefully when FA is absent.
    /// </summary>
    public static class FABridge
    {
        private static bool _tried;
        private static MethodInfo _play;

        public static void Init()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.FacialAnimActive) return;
            try
            {
                var t = AccessTools.TypeByName("FacialAnimation.FacialAnimationControllerComp");
                if (t != null)
                    _play = AccessTools.Method(t, "PlayTemporaryAnimation",
                        new[] { typeof(Pawn), typeof(int), typeof(string[]) });
            }
            catch (Exception ex)
            {
                Log.Warning("[RJW Sexual Harassment] Facial Animation bridge init failed (non-fatal): " + ex.Message);
            }
        }

        /// <summary>Plays a FaceAnimationDef by name (e.g. "Strip" = blush) as a temporary overlay.</summary>
        public static void PlayFace(Pawn pawn, string faceAnimDefName)
        {
            if (_play == null || pawn == null) return;
            try { _play.Invoke(null, new object[] { pawn, GenTicks.TicksGame, new[] { faceAnimDefName } }); }
            catch { }
        }
    }

    /// <summary>Reflection bridge to Karma &amp; Reputation's public static KarmaAPI.</summary>
    public static class KarmaBridge
    {
        private static MethodInfo _addKarma;
        private static MethodInfo _getKarma;
        private static bool _tried;

        public static void Init()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.KarmaActive) return;
            try
            {
                var t = AccessTools.TypeByName("AstrylMods.KarmaReputation.KarmaAPI");
                if (t != null)
                {
                    _addKarma = AccessTools.Method(t, "AddKarma", new[] { typeof(Pawn), typeof(float), typeof(string) });
                    _getKarma = AccessTools.Method(t, "GetKarma", new[] { typeof(Pawn) });
                }
                if (_addKarma == null)
                    Log.Warning("[RJW Sexual Harassment] Karma active but KarmaAPI.AddKarma not found; karma hooks disabled.");
            }
            catch (Exception ex)
            {
                Log.Warning("[RJW Sexual Harassment] Karma bridge init failed (non-fatal): " + ex.Message);
            }
        }

        public static void AddKarma(Pawn pawn, float amount, string reason)
        {
            if (_addKarma == null || pawn == null) return;
            try { _addKarma.Invoke(null, new object[] { pawn, amount, reason }); }
            catch (Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] AddKarma failed: " + ex.Message, 0x5A12C7); }
        }

        /// <summary>Reads a pawn's karma if the API exposes a getter; false otherwise. Negative = bad karma.</summary>
        public static bool TryGetKarma(Pawn pawn, out float karma)
        {
            karma = 0f;
            if (_getKarma == null || pawn == null) return false;
            try { if (_getKarma.Invoke(null, new object[] { pawn }) is float f) { karma = f; return true; } }
            catch { }
            return false;
        }
    }

    /// <summary>Reflection bridge to Karma &amp; Reputation's ReputationAPI - the SIGNED regard axis
    /// (-1000 infamous .. +1000 renowned). The colony's slaving activity feeds colony INFAMY (negative
    /// deltas); scandal subjects and parading owners pick up personal infamy. No-ops without Karma.</summary>
    public static class ReputationBridge
    {
        private static MethodInfo _addPawnRep;
        private static MethodInfo _addColonyRep;
        private static MethodInfo _getColonyRep;
        private static bool _tried;

        public static void Init()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.KarmaActive) return;
            try
            {
                var t = AccessTools.TypeByName("AstrylMods.KarmaReputation.ReputationAPI");
                if (t != null)
                {
                    _addPawnRep = AccessTools.Method(t, "AddReputation", new[] { typeof(Pawn), typeof(float), typeof(string) });
                    _addColonyRep = AccessTools.Method(t, "AddColonyReputation", new[] { typeof(float), typeof(string) });
                    _getColonyRep = AccessTools.Method(t, "GetColonyReputation", System.Type.EmptyTypes);
                }
                if (_addColonyRep == null)
                    Log.Warning("[RJW Sexual Harassment] Karma active but ReputationAPI not found (older version?); reputation hooks disabled.");
            }
            catch (Exception ex)
            {
                Log.Warning("[RJW Sexual Harassment] Reputation bridge init failed (non-fatal): " + ex.Message);
            }
        }

        /// <summary>Signed regard change on a pawn: positive = renown, negative = infamy.</summary>
        public static void AddReputation(Pawn pawn, float amount, string reason)
        {
            if (_addPawnRep == null || pawn == null) return;
            try { _addPawnRep.Invoke(null, new object[] { pawn, amount, reason }); }
            catch (Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] AddReputation failed: " + ex.Message, 0x5A13F1); }
        }

        /// <summary>Signed colony-scale regard change: negative = the colony's infamy spreads.</summary>
        public static void AddColonyReputation(float amount, string reason)
        {
            if (_addColonyRep == null) return;
            try { _addColonyRep.Invoke(null, new object[] { amount, reason }); }
            catch (Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] AddColonyReputation failed: " + ex.Message, 0x5A13F2); }
        }

        /// <summary>Reads colony regard (-1000..+1000). False (0) when Karma is absent.</summary>
        public static bool TryGetColonyReputation(out float rep)
        {
            rep = 0f;
            if (_getColonyRep == null) return false;
            try { if (_getColonyRep.Invoke(null, null) is float f) { rep = f; return true; } }
            catch { }
            return false;
        }
    }

    /// <summary>Bridge to RJW Brothel Colony. Records forced whoring sessions into the RJW whoring records so
    /// they surface in Brothel Colony's whoring tab/stats. No-ops without Brothel Colony.</summary>
    public static class BrothelBridge
    {
        private static bool _tried;
        private static MethodInfo _updateRecords;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var t = AccessTools.TypeByName("BrothelColony.WhoringHelper");
                if (t != null) _updateRecords = AccessTools.Method(t, "UpdateRecords", new[] { typeof(Pawn), typeof(int) });
            }
            catch (Exception ex) { Log.Warning("[RJW Sexual Harassment] Brothel Colony bridge init failed (non-fatal): " + ex.Message); }
        }

        /// <summary>Logs a completed whoring session (earned money + count) into Brothel Colony / RJW records.</summary>
        public static void RecordWhoring(Pawn whore, int price)
        {
            Ensure();
            if (_updateRecords == null || whore == null) return;
            try { _updateRecords.Invoke(null, new object[] { whore, price }); } catch { }
        }
    }

    /// <summary>Best-effort bridge to Rimpsyche: reads a pawn's Compassion personality to gauge cruelty.</summary>
    public static class RimpsycheBridge
    {
        private static bool _tried;
        private static MethodInfo _getComp, _getPersonality;
        private static PropertyInfo _personalityProp;
        private static object _compassionDef, _aggDef, _willDef;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var pcm = AccessTools.TypeByName("Maux36.RimPsyche.PsycheCacheManager");
                _getComp = pcm != null ? AccessTools.Method(pcm, "GetCompPsycheCached", new[] { typeof(Pawn) }) : null;
                _personalityProp = AccessTools.TypeByName("Maux36.RimPsyche.CompPsyche")?.GetProperty("Personality");
                var perT = AccessTools.TypeByName("Maux36.RimPsyche.Pawn_PersonalityTracker");
                var pdefT = AccessTools.TypeByName("Maux36.RimPsyche.PersonalityDef");
                if (perT != null && pdefT != null)
                    _getPersonality = AccessTools.Method(perT, "GetPersonality", new[] { pdefT });
                if (pdefT != null)
                {
                    // Resolve facet defs by their real defNames (confirmed from Rimpsyche's XML).
                    _compassionDef = GenDefDatabase.GetDefSilentFail(pdefT, "Rimpsyche_Compassion", false);
                    _aggDef = GenDefDatabase.GetDefSilentFail(pdefT, "Rimpsyche_Aggressiveness", false);
                    _willDef = GenDefDatabase.GetDefSilentFail(pdefT, "Rimpsyche_Tenacity", false);
                }
            }
            catch (Exception ex) { Log.Warning("[RJW Sexual Harassment] Rimpsyche bridge init failed (non-fatal): " + ex.Message); }
        }

        /// <summary>Reads one personality facet (roughly -1..1). 0 when Rimpsyche or the data is unavailable.</summary>
        private static float Facet(Pawn p, object facetDef)
        {
            if (p == null || facetDef == null) return 0f;
            if (_getComp == null || _personalityProp == null || _getPersonality == null) return 0f;
            try
            {
                var comp = _getComp.Invoke(null, new object[] { p });
                var pers = comp == null ? null : _personalityProp.GetValue(comp);
                if (pers == null) return 0f;
                if (_getPersonality.Invoke(pers, new[] { facetDef }) is float f) return f;
            }
            catch { }
            return 0f;
        }

        /// <summary>0 (compassionate/neutral) .. 1 (very cruel).</summary>
        public static float Cruelty(Pawn p)
        {
            Ensure();
            float c = Facet(p, _compassionDef);
            return c < 0f ? UnityEngine.Mathf.Clamp01(-c) : 0f;
        }

        /// <summary>-1 (meek) .. 1 (very aggressive) - a dominant, pushy disposition harasses more readily.</summary>
        public static float Aggressiveness(Pawn p)
        {
            Ensure();
            return Facet(p, _aggDef);
        }

        /// <summary>0 (pliable) .. 1 (iron-willed) - a tenacious disposition resists collar conditioning.</summary>
        public static float WillStrength(Pawn p)
        {
            Ensure();
            float t = Facet(p, _willDef);
            return t > 0f ? UnityEngine.Mathf.Clamp01(t) : 0f;
        }
    }

    /// <summary>
    /// Best-effort bridge to RimTalk. SpeakUp already speaks our interactions via their rulesStrings,
    /// so static dialogue always works. When RimTalk is present we additionally try to seed a talk
    /// context so its AI can voice the moment. The exact API is resolved by name and no-ops if absent.
    /// </summary>
    public static class RimTalkBridge
    {
        private static bool _tried;
        private static MethodInfo _talkMethod;

        public static void Init()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.RimTalkActive) return;
            try
            {
                // RimTalk's public surface varies by version; resolve a context/trigger entry point by name.
                var svc = AccessTools.TypeByName("RimTalk.Service.TalkService")
                          ?? AccessTools.TypeByName("RimTalk.TalkService")
                          ?? AccessTools.TypeByName("RimTalk.RimTalk");
                if (svc != null)
                    _talkMethod = AccessTools.Method(svc, "TriggerTalk")
                                  ?? AccessTools.Method(svc, "Talk")
                                  ?? AccessTools.Method(svc, "GenerateTalk");
            }
            catch (Exception ex)
            {
                Log.Warning("[RJW Sexual Harassment] RimTalk bridge init failed (non-fatal): " + ex.Message);
            }
        }

        /// <summary>Hook point for future RimTalk context seeding. Currently a graceful no-op.</summary>
        public static void NotifyHarassment(Pawn harasser, Pawn victim, ApproachType type)
        {
            // Intentionally conservative: until we pin RimTalk's signature, we rely on the play-log
            // entry (which RimTalk and SpeakUp both observe) produced by TryInteractWith.
        }
    }

    /// <summary>Reflection bridge to RJW Quirks (rjw.quirks). Reads/adds QuirkDef-based quirks through the
    /// ThingComp_QuirkTracker. Submissive/degradation quirks make a pet more receptive to conditioning, and
    /// conditioning milestones can install fitting quirks. No-ops without RJW Quirks.</summary>
    public static class QuirksBridge
    {
        private static bool _tried;
        private static Type _quirkDefType;
        private static MethodInfo _getQuirks, _hasQuirk, _tryAdd;
        private static readonly System.Collections.Generic.Dictionary<string, object> _defCache
            = new System.Collections.Generic.Dictionary<string, object>();

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.QuirksActive) return;
            try
            {
                var ext = AccessTools.TypeByName("RJWQuirks.PawnExtensions");
                var tracker = AccessTools.TypeByName("RJWQuirks.ThingComp_QuirkTracker");
                _quirkDefType = AccessTools.TypeByName("RJWQuirks.QuirkDef");
                if (ext != null && _quirkDefType != null)
                {
                    _getQuirks = AccessTools.Method(ext, "GetQuirks", new[] { typeof(Pawn) });
                    _hasQuirk = AccessTools.Method(ext, "HasQuirk", new[] { typeof(Pawn), _quirkDefType });
                }
                if (tracker != null && _quirkDefType != null)
                    _tryAdd = AccessTools.Method(tracker, "TryAdd", new[] { _quirkDefType, typeof(bool) });
                if (_hasQuirk == null)
                    Log.Warning("[RJW Sexual Harassment] RJW Quirks active but its API did not resolve; quirk hooks disabled.");
            }
            catch (Exception ex) { Log.Warning("[RJW Sexual Harassment] Quirks bridge init failed (non-fatal): " + ex.Message); }
        }

        private static object QuirkDef(string defName)
        {
            if (_quirkDefType == null) return null;
            if (_defCache.TryGetValue(defName, out var d)) return d;
            d = GenDefDatabase.GetDefSilentFail(_quirkDefType, defName, false);
            _defCache[defName] = d;
            return d;
        }

        public static bool HasQuirk(Pawn p, string defName)
        {
            Ensure();
            if (_hasQuirk == null || p == null) return false;
            var def = QuirkDef(defName);
            if (def == null) return false;
            try { return _hasQuirk.Invoke(null, new object[] { p, def }) is bool b && b; }
            catch { return false; }
        }

        public static bool TryAddQuirk(Pawn p, string defName)
        {
            Ensure();
            if (_getQuirks == null || _tryAdd == null || p == null) return false;
            var def = QuirkDef(defName);
            if (def == null) return false;
            try
            {
                var tracker = _getQuirks.Invoke(null, new object[] { p });
                if (tracker == null) return false;
                return _tryAdd.Invoke(tracker, new object[] { def, false }) is bool b && b;
            }
            catch { return false; }
        }

        /// <summary>Multiplier on conditioning gains from a pet's existing quirks: submissive/degradation quirks
        /// make them break faster. 1.0 when RJW Quirks is absent.</summary>
        public static float ReceptivityFactor(Pawn p)
        {
            Ensure();
            if (_hasQuirk == null || p == null) return 1f;
            float f = 1f;
            if (HasQuirk(p, "Cumslut")) f += 0.15f;
            if (HasQuirk(p, "Buttslut")) f += 0.15f;
            if (HasQuirk(p, "Exhibitionist")) f += 0.15f;
            if (HasQuirk(p, "Somnophile")) f += 0.05f;
            return f;
        }
    }

    /// <summary>Bridge to RJW Sexperience (rjw.sexperience). "Lust" is a vanilla RecordDef the mod tracks on
    /// pawn.records, so no assembly reference is needed. Sexperience already updates lust on RJW orgasms; here
    /// we add lust from non-sex conditioning (collar/parade/reward) and read it back to gauge receptivity.</summary>
    public static class SexperienceBridge
    {
        private static bool _tried;
        private static RimWorld.RecordDef _lust;

        private static void Ensure()
        {
            if (_tried) return;
            _tried = true;
            if (!SoftDeps.SexperienceActive) return;
            _lust = DefDatabase<RimWorld.RecordDef>.GetNamedSilentFail("Lust");
            if (_lust == null)
                Log.Warning("[RJW Sexual Harassment] RJW Sexperience active but the Lust record did not resolve; lust hooks disabled.");
        }

        public static float GetLust(Pawn p)
        {
            Ensure();
            if (_lust == null || p?.records == null) return 0f;
            try { return p.records.GetValue(_lust); } catch { return 0f; }
        }

        public static void AddLust(Pawn p, float delta)
        {
            Ensure();
            if (_lust == null || p?.records == null || delta == 0f) return;
            try { p.records.AddTo(_lust, delta); } catch { }
        }

        /// <summary>Multiplier (1.0 .. ~1.25) on conditioning gains from accumulated lust. 1.0 without Sexperience.</summary>
        public static float LustReceptivity(Pawn p)
        {
            float l = GetLust(p);
            if (l <= 0f) return 1f;
            return 1f + UnityEngine.Mathf.Clamp(l / 300f, 0f, 0.25f);
        }
    }
}
