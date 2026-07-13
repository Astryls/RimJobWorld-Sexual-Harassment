using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Deep per-pawn sexual attributes for ANY humanlike (not just pets). Own-tracked values are stored and
    /// scribed here; live values (arousal, alcohol, reputation) are MIRRORED from existing sources at read
    /// time and never stored. Seeded once (deterministically by pawn id + existing RJW records/traits) the
    /// first time a pawn's attributes are accessed. This is the data model; behaviour wiring comes later.
    /// </summary>
    public class SexAttributes : IExposable
    {
        // ── Physical (own-tracked wear 0..100; arousal + alcohol are mirrored, not stored). Only the parts a
        // pawn actually has are shown; the others stay at 0. ──
        public float wearOral = 0f;
        public float wearVaginal = 0f;
        public float wearAnal = 0f;
        public float wearPenis = 0f;

        // ── Psychological (own-tracked 0..100, except subDom -100..100) ──
        public float willpower = 50f;     // resistance to coercion / conditioning
        public float selfEsteem = 50f;    // self-worth; low -> easier to break down
        public float spirit = 50f;        // inner drive / resilience
        public float subDom = 0f;         // -100 fully submissive .. +100 fully dominant
        public float sexAddiction = 0f;   // craving / compulsion 0..100
        public float trauma = 0f;         // accumulated sexual trauma 0..100

        public bool seeded = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref wearOral, "wearOral", 0f);
            Scribe_Values.Look(ref wearVaginal, "wearVaginal", 0f);
            Scribe_Values.Look(ref wearAnal, "wearAnal", 0f);
            Scribe_Values.Look(ref wearPenis, "wearPenis", 0f);
            Scribe_Values.Look(ref willpower, "willpower", 50f);
            Scribe_Values.Look(ref selfEsteem, "selfEsteem", 50f);
            Scribe_Values.Look(ref spirit, "spirit", 50f);
            Scribe_Values.Look(ref subDom, "subDom", 0f);
            Scribe_Values.Look(ref sexAddiction, "sexAddiction", 0f);
            Scribe_Values.Look(ref trauma, "trauma", 0f);
            Scribe_Values.Look(ref seeded, "seeded", false);
        }

        /// <summary>Seeds the own-tracked attributes once, deterministically, from the pawn's traits and its
        /// existing RJW records so a freshly-inspected pawn already has plausible values.</summary>
        public void SeedFrom(Pawn pawn)
        {
            if (seeded || pawn == null) return;
            seeded = true;
            try
            {
                Rand.PushState(pawn.thingIDNumber ^ 0x5E4A77);
                willpower = Rand.Range(30f, 70f);
                selfEsteem = Rand.Range(30f, 70f);
                spirit = Rand.Range(30f, 70f);
                subDom = Rand.Range(-45f, 45f); // wide base so most pawns land clearly sub or dom, not "switch"
                Rand.PopState();

                SeedFromBackstory(pawn);
                SeedFromRimPsyche(pawn);

                var traits = pawn.story?.traits;
                if (traits != null)
                {
                    if (HasTrait(traits, "Masochist")) { subDom -= 35f; sexAddiction += 15f; }
                    if (HasTrait(traits, "Nymphomaniac")) sexAddiction += 45f;
                    if (HasTrait(traits, "Ascetic")) sexAddiction -= 25f;
                    if (HasTrait(traits, "Bloodlust")) subDom += 30f;
                    if (HasTrait(traits, "Psychopath")) { subDom += 20f; }
                    if (HasTrait(traits, "Sadist") || HasTrait(traits, "RJWSH_Sadist")) subDom += 40f;
                    if (HasTraitDegree(traits, "Nerves", 2) || HasTraitDegree(traits, "Nerves", 1)) willpower += 12f;
                    if (HasTrait(traits, "Wimp")) { willpower -= 15f; selfEsteem -= 10f; }
                    if (HasTrait(traits, "TooSmart")) spirit += 8f;
                    if (HasTrait(traits, "Beautiful") || HasTrait(traits, "Pretty")) selfEsteem += 12f;
                    if (HasTrait(traits, "Ugly") || HasTrait(traits, "Staggeringlyugly")) selfEsteem -= 12f;
                }

                // Trauma seeded from how often this pawn has been raped (RJW victim records).
                float victimCount = Rec(pawn, "CountOfBeenRapedByHumanlikes") + Rec(pawn, "CountOfBeenRapedByAnimals")
                                  + Rec(pawn, "CountOfBeenRapedByInsects") + Rec(pawn, "CountOfBeenRapedByOthers");
                trauma = UnityEngine.Mathf.Clamp(victimCount * 6f, 0f, 100f);
                if (traits != null && HasTrait(traits, "Masochist")) trauma *= 0.4f; // pain reads differently

                // Wear starts at 0% for every pawn and accrues only through sex during play (capped at 100%
                // by Clamp100 in ApplyActWear). No history-based seeding, so a fresh pawn begins unused.

                // Sex addiction floored by RJW sex drive.
                try { sexAddiction += UnityEngine.Mathf.Clamp01(rjw.xxx.get_sex_drive(pawn) - 1f) * 20f; } catch { }

                willpower = Clamp01to100(willpower);
                selfEsteem = Clamp01to100(selfEsteem);
                spirit = Clamp01to100(spirit);
                subDom = UnityEngine.Mathf.Clamp(subDom, -100f, 100f);
                sexAddiction = Clamp01to100(sexAddiction);
            }
            catch { }
        }

        // Backstory keyword scan: servile/dominant/confident/broken language shifts sub-dom and self-worth.
        private void SeedFromBackstory(Pawn pawn)
        {
            try
            {
                var story = pawn.story;
                if (story == null) return;
                string txt = (BsText(story.Childhood) + " " + BsText(story.Adulthood)).ToLowerInvariant();
                if (txt.Length == 0) return;
                foreach (var kw in SubKeywords) if (txt.Contains(kw)) subDom -= 12f;
                foreach (var kw in DomKeywords) if (txt.Contains(kw)) subDom += 12f;
                foreach (var kw in MeekKeywords) if (txt.Contains(kw)) { selfEsteem -= 8f; willpower -= 6f; }
                foreach (var kw in StrongKeywords) if (txt.Contains(kw)) { willpower += 8f; spirit += 6f; }
            }
            catch { }
        }
        private static string BsText(BackstoryDef b) => b == null ? "" : (b.title + " " + b.titleShort + " " + b.baseDesc);
        private static readonly string[] SubKeywords = { "slave", "servant", "maid", "submissive", "captive", "prisoner", "pet", "concubine", "harem", "obedien" };
        private static readonly string[] DomKeywords = { "leader", "commander", "noble", "master", "overseer", "boss", "tyrant", "captain", "officer", "dominant", "warlord" };
        private static readonly string[] MeekKeywords = { "timid", "shy", "meek", "abused", "bullied", "orphan", "outcast", "beaten" };
        private static readonly string[] StrongKeywords = { "soldier", "warrior", "survivor", "fighter", "hardened", "veteran", "mercenary" };

        // RimPsyche personality facets (reflected, optional): Assertiveness -> dominance + willpower,
        // Volatility/Neuroticism -> lower willpower, Compassion -> softer (more submissive).
        private void SeedFromRimPsyche(Pawn pawn)
        {
            float assert = RimPsycheFacet(pawn, "Assertiveness");
            float volat = RimPsycheFacet(pawn, "Volatility");
            float comp = RimPsycheFacet(pawn, "Compassion");
            if (!float.IsNaN(assert)) { subDom += assert * 0.5f; willpower += assert * 0.25f; }
            if (!float.IsNaN(volat)) willpower -= volat * 0.2f;
            if (!float.IsNaN(comp)) subDom -= comp * 0.2f;
        }

        private static System.Type _psycheComp; private static System.Reflection.MethodInfo _facetGetter; private static System.Type _facetEnum; private static bool _rpTried;
        private static float RimPsycheFacet(Pawn pawn, string facet)
        {
            try
            {
                if (!_rpTried)
                {
                    _rpTried = true;
                    _psycheComp = HarmonyLib.AccessTools.TypeByName("Maux36.RimPsyche.CompPsyche");
                    _facetEnum = HarmonyLib.AccessTools.TypeByName("Maux36.RimPsyche.Facet");
                    if (_psycheComp != null)
                        _facetGetter = HarmonyLib.AccessTools.Method(_psycheComp, "GetFacetValue", new[] { _facetEnum });
                }
                if (_psycheComp == null || _facetGetter == null || _facetEnum == null) return float.NaN;
                var comp = ((ThingWithComps)pawn).AllComps.Find(c => c.GetType() == _psycheComp);
                if (comp == null) return float.NaN;
                var val = System.Enum.Parse(_facetEnum, facet);
                return System.Convert.ToSingle(_facetGetter.Invoke(comp, new[] { val }));
            }
            catch { return float.NaN; }
        }

        // ── Part detection (only show/wear parts a pawn actually has) ──────────
        public static bool HasMouth(Pawn p) { try { return rjw.Genital_Helper.has_mouth(p); } catch { return true; } }
        public static bool HasVagina(Pawn p) { try { return rjw.Genital_Helper.has_vagina(p); } catch { return false; } }
        public static bool HasAnus(Pawn p) { try { return rjw.Genital_Helper.has_anus(p); } catch { return true; } }
        public static bool HasPenis(Pawn p) { try { return rjw.Genital_Helper.has_male_bits(p); } catch { return false; } }

        // ── Passive drift (feedback: attributes change even when nothing is done TO the pawn) ──
        /// <summary>Called ~hourly: worn parts slowly recover, trauma and (non-nympho) addiction slowly fade.</summary>
        public void HourlyDrift(bool nympho)
        {
            if (!seeded) return;
            wearOral = Recover(wearOral); wearVaginal = Recover(wearVaginal);
            wearAnal = Recover(wearAnal); wearPenis = Recover(wearPenis);
            trauma = UnityEngine.Mathf.Max(0f, trauma - 0.05f);
            if (!nympho) sexAddiction = UnityEngine.Mathf.Max(0f, sexAddiction - 0.15f);
            // The psychological stats slowly settle back toward the middle when nothing is acting on them,
            // so a pawn recovers (or loses their edge) over time if left alone.
            willpower = DriftTo(willpower, 50f, 0.15f);
            selfEsteem = DriftTo(selfEsteem, 50f, 0.12f);
            spirit = DriftTo(spirit, 50f, 0.12f);
        }
        private static float Recover(float v) => UnityEngine.Mathf.Max(0f, v - 0.35f);
        private static float DriftTo(float v, float target, float step)
        {
            if (UnityEngine.Mathf.Abs(v - target) <= step) return target;
            return v + (v < target ? step : -step);
        }

        private static float Clamp01to100(float v) => UnityEngine.Mathf.Clamp(v, 0f, 100f);
        private static bool HasTrait(TraitSet t, string defName)
        {
            var td = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return td != null && t.HasTrait(td);
        }
        private static bool HasTraitDegree(TraitSet t, string defName, int degree)
        {
            var td = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return td != null && t.HasTrait(td) && t.DegreeOfTrait(td) == degree;
        }
        private static float Rec(Pawn p, string recDefName)
        {
            try { var r = DefDatabase<RecordDef>.GetNamedSilentFail(recDefName); return r != null ? p.records.GetValue(r) : 0f; }
            catch { return 0f; }
        }

        // ── Mirrored live reads (never stored) ────────────────────────────────
        /// <summary>Arousal 0..100 mirrored from RJW's sex need (frustrated = high). -1 if unavailable.</summary>
        public static float Arousal(Pawn pawn)
        {
            try
            {
                var need = pawn?.needs?.TryGetNeed<rjw.Need_Sex>();
                if (need != null) return UnityEngine.Mathf.Clamp01(1f - need.CurLevel) * 100f;
            }
            catch { }
            return -1f;
        }

        /// <summary>Alcohol intoxication 0..100 mirrored from the vanilla AlcoholHigh hediff severity.</summary>
        public static float Alcohol(Pawn pawn)
        {
            try
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail("AlcoholHigh");
                var h = def != null ? pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) : null;
                return h != null ? UnityEngine.Mathf.Clamp01(h.Severity) * 100f : 0f;
            }
            catch { return 0f; }
        }
    }
}
