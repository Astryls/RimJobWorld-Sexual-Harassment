using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Persistent per-pawn harassment state. Morality and confidence are rolled once and
    /// kept for the life of the save. Impression drifts as the pawn submits to or resists
    /// harassment, feeding the target-weighting and emboldening loop.
    /// </summary>
    public class PawnProfile : IExposable
    {
        public Morality morality = Morality.Decent;
        public float confidence = 50f;       // 0..100 boldness as a harasser
        public float impression = 0f;        // -50..50 how much of a pushover this pawn looks like

        public int lastHarasserTick = -999999; // last tick this pawn initiated harassment
        public int lastVictimTick = -999999;   // last tick this pawn was harassed

        public float hypnosisLevel = 0f;        // 0..100 conditioning; bands None<30, Suggestible<60, Conditioned>=60
        public float rapport = 50f;             // 0..100 trust vs fear toward the owner; reward raises it, discipline/shocks lower it
        public float breakSusceptibility = -1f; // per-pawn conditioning pace multiplier; <0 = not yet rolled. Most 0.55-1.05 (slow), rare ~10% 1.8-2.8 (fast).
        public int rescueRaidUntilTick = 0;     // >0 while a rescue raid is active for this pet; a raider reaching them frees them.
        public bool dashboardPinned = false;    // pins this pet's card to the top of the harem list (independent of head girl).

        // Key-holder control state (set by the controlling pawn's gizmos):
        public int ownerId = -1;                // thingIDNumber of the controlling key-holder (-1 = none)
        public bool followOwner = false;        // forced to follow the owner around
        public bool autoService = false;        // auto-cast: periodically service a chosen group
        public bool autoReward = false;         // auto-cast: owner periodically rewards the pet
        public bool autoDiscipline = false;     // auto-cast: owner periodically disciplines the pet
        public int serviceTargetMode = 0;        // 0 owner, 1 colonists, 2 prisoners/slaves, 3 guests, 4 anyone
        public string serviceInteraction = null; // chosen sex InteractionDef defName; null = default quick service
        public bool checkedCirculation = false;  // rolled once for a spawn-with-photo-in-circulation
        public int sceneExtendCount = 0;         // how many times the current scene has already been extended
        public bool boundInPublic = false;       // left bound in a public spot; begs for help until freed
        public float slaveWill = 100f;           // 0..100 resistance of a controlled slave; drives breakout attempts
        public bool aiControlled = false;        // an AI pawn (not the player) is driving this collar -> gizmos locked
        public bool allowNeeds = false;          // owner lets the collared pawn freely attend its own needs
        public string cryptoName = null;         // this pawn's single shared lock stamp (all their locked gear + the one key)
        public string cryptoKey = null;
        public bool forceNudity = false;         // owner forces them to wear nothing but locked devices
        public bool hideControls = false;        // collapse this pawn's key-holder gizmos to keep the UI clean
        public int whoreOwnerId = -1;            // owner awaiting payment for a whore service in progress
        public int affectionCooldownTick = 0;    // next tick this pawn may seek a kiss/hold-hands affection moment
        public System.Collections.Generic.List<string> pendingDressUp; // device defNames queued by the dress-up window
        public int resistCooldownTick = -999999;  // cooldown on the victim's "fight back" gizmo
        public bool autoResist = false;          // victim auto-attempts to fight back when the cooldown is ready
        public int onaholeReleaseTick = -1;      // onahole time-limit tick: once passed, the slave begs the owner to be let out
        public int scuffleEndTick = -1;          // tick a capped fight-back social-fight scuffle is force-ended
        public int relationshipOwnerId = -1;     // persistent owner for the social-tab relationship (outlives the collar)
        public int controlCooldownTick = -999999; // shared cooldown for Command/Shock gizmos
        public int tendCooldownTick = -999999;   // shared cooldown for Discipline/Reward
        public IntVec3 stayCell = IntVec3.Invalid; // "Stay here" leash spot; Invalid = not staying
        public int shockUntil = 0;               // 0 none, 1 shock until downed, 2 shock until dead
        // Who has physically violated this pawn, and how often (thingID -> count). Drives on-sight recoil / vengeance.
        public System.Collections.Generic.Dictionary<int, int> harasserMemory;
        public int recoilCooldownTick = -999999; // cooldown on the on-sight recoil/vengeance reaction
        public bool satisfiedClient = false;     // a visitor who was serviced by a pet; spreads goodwill on departure
        public float latentHypnosis = 0f;        // remembered peak conditioning; a re-collared pawn re-breaks fast
        public int chatterCooldownTick = 0;      // shared throttle for ambient bubble lines (self-talk/banter/commiseration)
        // Rolling hourly samples of conditioning + rapport (0..100) for the pet-dashboard history graph (~360 = 15 days).
        public System.Collections.Generic.List<float> condHistory;
        public System.Collections.Generic.List<float> rapportHistory;
        // Discrete conditioning/rapport events (discipline, reward, shocks, forced acts, breakouts) for the overlay.
        public System.Collections.Generic.List<CondEvent> condEvents;
        // Prose life-story ledger (collared, broke, paraded, sold, owner died) for the History tab.
        public System.Collections.Generic.List<ChronicleEntry> chronicle;
        private System.Collections.Generic.List<int> _hmKeys;
        private System.Collections.Generic.List<int> _hmVals;

        // Deep per-pawn sexual attributes (physical/psychological/social). Lazily created + seeded on first
        // access (e.g. opening the Sexuality tab). See SexAttributes.
        public SexAttributes sex;
        public string pendingTrainStat;         // which attribute a queued conditioning session will try to shift
        public string trainFocus;               // ONGOING conditioning focus set in the Control tab (null = off)
        public int gangbangCount = 0;            // remaining aggressors queued to use this pawn (flee-beating retaliation)
        public int gangbangUntil = -1;           // tick the gangbang punishment window closes
        // Depth systems:
        public int breakStage = -1;              // last-known breaking stage (Defiant..Broken) for transition letters
        public int petRole = 0;                  // conditioning specialization: 0 none,1 pleasure,2 servant,3 bodyguard,4 performer
        public int depthCooldownTick = 0;        // shared throttle for ambient depth behaviors (autonomy/rivalry/pecking)
        public int nightTerrorTick = 0;          // cooldown on trauma night-terror wakeups
        public bool autoParade = false;          // schedule: periodically parade this pet during the day
        public bool curfew = false;              // schedule: keep this pet at the owner's side through the night
        public int paradeCooldownTick = -999999; // next tick auto-parade may fire
        public System.Collections.Generic.List<int> schedule; // 24-hour grid (null = none); 0 Free/1 Serve/2 Train/3 Parade/4 Rest/5 Confined
        public IntVec3 quartersCell = IntVec3.Invalid;         // "Confined" destination cell
        public int scheduleCooldownTick = -999999;             // throttle for scheduled Train sessions
        public int lifetimeEarnings = 0;                       // total silver this pet has earned (whoring + auction)
        public int dailyQuota = 0;                             // required services per day (0 = none)
        public int servicesToday = 0;                          // services rendered so far today
        public int quotaDay = -1;                              // day-of-year the service count belongs to (reset marker)
        public bool isHeadGirl = false;                        // this pet enforces the harem pecking order
        public SexAttributes SexAttr(Pawn pawn)
        {
            if (sex == null) sex = new SexAttributes();
            sex.SeedFrom(pawn);
            return sex;
        }

        public bool IsConditioned => hypnosisLevel >= 60f;
        public bool IsSuggestible => hypnosisLevel >= 30f;
        // Broken by fear rather than trust: obedient on the surface but liable to lash out.
        public bool IsVolatile => rapport < 30f;

        /// <summary>Appends one hourly sample of conditioning + rapport, keeping a rolling ~15-day window.</summary>
        public void RecordHistorySample()
        {
            if (condHistory == null) condHistory = new System.Collections.Generic.List<float>();
            if (rapportHistory == null) rapportHistory = new System.Collections.Generic.List<float>();
            condHistory.Add(hypnosisLevel);
            rapportHistory.Add(rapport);
            if (condHistory.Count > 360) condHistory.RemoveAt(0);
            if (rapportHistory.Count > 360) rapportHistory.RemoveAt(0);
        }

        /// <summary>Applies a conditioning + rapport change AND records it as a dashboard event (clamped 0..100).</summary>
        public void ApplyCond(string label, float condDelta, float rapDelta)
        {
            float c0 = hypnosisLevel, r0 = rapport;
            hypnosisLevel = UnityEngine.Mathf.Clamp(hypnosisLevel + condDelta, 0f, 100f);
            rapport = UnityEngine.Mathf.Clamp(rapport + rapDelta, 0f, 100f);
            LogCondEvent(label, hypnosisLevel - c0, rapport - r0);
        }

        /// <summary>Records a discrete conditioning/rapport event (does not itself change the stats).</summary>
        public void LogCondEvent(string label, float condDelta, float rapDelta)
        {
            if (condDelta == 0f && rapDelta == 0f) return;
            if (condEvents == null) condEvents = new System.Collections.Generic.List<CondEvent>();
            condEvents.Add(new CondEvent { tick = Find.TickManager.TicksGame, label = label, condDelta = condDelta, rapDelta = rapDelta });
            if (condEvents.Count > 50) condEvents.RemoveAt(0);
        }

        /// <summary>Appends a prose entry to the pawn's life-story chronicle (kept ~100 deep).</summary>
        public void AddChronicle(string text, int kind = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (chronicle == null) chronicle = new System.Collections.Generic.List<ChronicleEntry>();
            chronicle.Add(new ChronicleEntry(Find.TickManager?.TicksGame ?? 0, text, kind));
            if (chronicle.Count > 100) chronicle.RemoveAt(0);
        }

        public void RecordSubmitted()
        {
            impression += 6f;
            if (impression > 50f) impression = 50f;
        }

        public void RecordResisted()
        {
            impression -= 8f;
            if (impression < -50f) impression = -50f;
            // standing up to someone slightly dents a witnessed harasser's nerve handled elsewhere
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref morality, "morality", Morality.Decent);
            Scribe_Values.Look(ref confidence, "confidence", 50f);
            Scribe_Values.Look(ref impression, "impression", 0f);
            Scribe_Values.Look(ref lastHarasserTick, "lastHarasserTick", -999999);
            Scribe_Values.Look(ref lastVictimTick, "lastVictimTick", -999999);
            Scribe_Values.Look(ref hypnosisLevel, "hypnosisLevel", 0f);
            Scribe_Values.Look(ref rapport, "rapport", 50f);
            Scribe_Values.Look(ref breakSusceptibility, "breakSusceptibility", -1f);
            Scribe_Values.Look(ref rescueRaidUntilTick, "rescueRaidUntilTick", 0);
            Scribe_Values.Look(ref dashboardPinned, "dashboardPinned", false);
            Scribe_Values.Look(ref ownerId, "ownerId", -1);
            Scribe_Values.Look(ref followOwner, "followOwner", false);
            Scribe_Values.Look(ref autoService, "autoService", false);
            Scribe_Values.Look(ref autoReward, "autoReward", false);
            Scribe_Values.Look(ref autoDiscipline, "autoDiscipline", false);
            Scribe_Values.Look(ref serviceTargetMode, "serviceTargetMode", 0);
            Scribe_Values.Look(ref serviceInteraction, "serviceInteraction", null);
            Scribe_Values.Look(ref checkedCirculation, "checkedCirculation", false);
            Scribe_Values.Look(ref sceneExtendCount, "sceneExtendCount", 0);
            Scribe_Values.Look(ref boundInPublic, "boundInPublic", false);
            Scribe_Values.Look(ref slaveWill, "slaveWill", 100f);
            Scribe_Values.Look(ref aiControlled, "aiControlled", false);
            Scribe_Values.Look(ref allowNeeds, "allowNeeds", false);
            Scribe_Values.Look(ref cryptoName, "cryptoName", null);
            Scribe_Values.Look(ref cryptoKey, "cryptoKey", null);
            Scribe_Values.Look(ref forceNudity, "forceNudity", false);
            Scribe_Values.Look(ref hideControls, "hideControls", false);
            Scribe_Values.Look(ref whoreOwnerId, "whoreOwnerId", -1);
            Scribe_Values.Look(ref affectionCooldownTick, "affectionCooldownTick", 0);
            Scribe_Collections.Look(ref pendingDressUp, "pendingDressUp", LookMode.Value);
            Scribe_Values.Look(ref resistCooldownTick, "resistCooldownTick", -999999);
            Scribe_Values.Look(ref autoResist, "autoResist", false);
            Scribe_Values.Look(ref onaholeReleaseTick, "onaholeReleaseTick", -1);
            Scribe_Values.Look(ref scuffleEndTick, "scuffleEndTick", -1);
            Scribe_Values.Look(ref relationshipOwnerId, "relationshipOwnerId", -1);
            Scribe_Values.Look(ref controlCooldownTick, "controlCooldownTick", -999999);
            Scribe_Values.Look(ref tendCooldownTick, "tendCooldownTick", -999999);
            Scribe_Values.Look(ref stayCell, "stayCell", IntVec3.Invalid);
            Scribe_Values.Look(ref shockUntil, "shockUntil", 0);
            Scribe_Collections.Look(ref harasserMemory, "harasserMemory", LookMode.Value, LookMode.Value, ref _hmKeys, ref _hmVals);
            Scribe_Values.Look(ref recoilCooldownTick, "recoilCooldownTick", -999999);
            Scribe_Values.Look(ref satisfiedClient, "satisfiedClient", false);
            Scribe_Values.Look(ref latentHypnosis, "latentHypnosis", 0f);
            Scribe_Values.Look(ref chatterCooldownTick, "chatterCooldownTick", 0);
            Scribe_Collections.Look(ref condHistory, "condHistory", LookMode.Value);
            Scribe_Collections.Look(ref rapportHistory, "rapportHistory", LookMode.Value);
            Scribe_Collections.Look(ref condEvents, "condEvents", LookMode.Deep);
            Scribe_Collections.Look(ref chronicle, "chronicle", LookMode.Deep);
            Scribe_Deep.Look(ref sex, "sex");
            Scribe_Values.Look(ref pendingTrainStat, "pendingTrainStat", null);
            Scribe_Values.Look(ref trainFocus, "trainFocus", null);
            Scribe_Values.Look(ref gangbangCount, "gangbangCount", 0);
            Scribe_Values.Look(ref gangbangUntil, "gangbangUntil", -1);
            Scribe_Values.Look(ref breakStage, "breakStage", -1);
            Scribe_Values.Look(ref petRole, "petRole", 0);
            Scribe_Values.Look(ref depthCooldownTick, "depthCooldownTick", 0);
            Scribe_Values.Look(ref nightTerrorTick, "nightTerrorTick", 0);
            Scribe_Values.Look(ref autoParade, "autoParade", false);
            Scribe_Values.Look(ref curfew, "curfew", false);
            Scribe_Values.Look(ref paradeCooldownTick, "paradeCooldownTick", -999999);
            Scribe_Collections.Look(ref schedule, "schedule", LookMode.Value);
            Scribe_Values.Look(ref quartersCell, "quartersCell", IntVec3.Invalid);
            Scribe_Values.Look(ref scheduleCooldownTick, "scheduleCooldownTick", -999999);
            Scribe_Values.Look(ref lifetimeEarnings, "lifetimeEarnings", 0);
            Scribe_Values.Look(ref dailyQuota, "dailyQuota", 0);
            Scribe_Values.Look(ref servicesToday, "servicesToday", 0);
            Scribe_Values.Look(ref quotaDay, "quotaDay", -1);
            Scribe_Values.Look(ref isHeadGirl, "isHeadGirl", false);
        }
    }
}
