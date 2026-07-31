using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Game-wide store of per-pawn harassment profiles. Lazily assigns a stable morality and
    /// confidence the first time a pawn is queried, seeded by the pawn id so it is deterministic
    /// across reloads and never re-rolls.
    /// </summary>
    public class GameComponent_Harassment : GameComponent
    {
        public static GameComponent_Harassment Instance;

        private Dictionary<int, PawnProfile> profiles = new Dictionary<int, PawnProfile>();

        // Apparel we locked by injecting a HoloCrypto stamp (non-bondage_gear_def gear like pet collars).
        // The comp is not in def.comps, so it vanishes on load and must be re-injected from these records.
        public List<LockedExtraRecord> lockedExtras = new List<LockedExtraRecord>();
        private bool _needReinject;

        // Colony notoriety: how widely the colony's depravity is known. Drives an outside-faction reputation hit.
        public int notoriety;
        // Saved harem regimens (role + focus + schedule) applied to pets in bulk from the dashboard.
        public List<HaremPreset> haremPresets = new List<HaremPreset>();
        // When set, a curious-visitor group fires at this tick (drawn by an auctioned photo of a collared pet).
        private int curiousVisitorTick = -1;

        public void AddNotoriety(int amt) => AddNotoriety(amt, "rjwsh_notoriety");

        /// <summary>Bumps the local notoriety meter AND mirrors it into Karma &amp; Reputation as colony
        /// INFAMY (negative regard, x3 scale) so the wider world layer reacts. No-ops without Karma.</summary>
        public void AddNotoriety(int amt, string reason)
        {
            notoriety += amt;
            if (notoriety > 100) notoriety = 100;
            if (amt > 0) ReputationBridge.AddColonyReputation(-amt * 3f, reason);
        }

        // Photos sold/handed into the wider world - tracked so they still show in the photo gallery even after
        // the physical copy has left the map with a caravan/visitor.
        public List<CirculatingPhoto> circulatingPhotos = new List<CirculatingPhoto>();

        // Nemesis arc (#4): escaped/sold pets the world remembers - they can return as hostile raid leaders.
        public List<Nemesis> nemeses = new List<Nemesis>();

        // Pet market: a weekly restock of buyable pets. Pending deliveries are transient (finalized once landed).
        public List<MarketEntry> market = new List<MarketEntry>();
        public int marketRefreshTick = -1;
        private readonly List<System.ValueTuple<Pawn, int, int>> _pendingDeliveries = new List<System.ValueTuple<Pawn, int, int>>();
        public void AddCirculatingPhoto(Pawn subject, string lore, string holder, Faction faction = null)
        {
            if (subject == null) return;
            if (circulatingPhotos == null) circulatingPhotos = new List<CirculatingPhoto>();
            circulatingPhotos.Add(new CirculatingPhoto { subject = subject, lore = lore, holder = holder, faction = faction });
        }
        /// <summary>True while a curious-visitor arrival (drawn by the colony's notoriety) is scheduled.</summary>
        public bool CuriousVisitorsPending => curiousVisitorTick > 0;
        /// <summary>Ticks until the next curious-visitor arrival, or -1 if none scheduled.</summary>
        public int NextCuriousVisitorTicksLeft => curiousVisitorTick > 0 ? System.Math.Max(0, curiousVisitorTick - Find.TickManager.TicksGame) : -1;
        public void ScheduleCuriousVisitors()
        {
            if (curiousVisitorTick < 0) curiousVisitorTick = Find.TickManager.TicksGame + Rand.Range(60000, 180000);
        }

        // Scribe scratch lists
        private List<int> _keys;
        private List<PawnProfile> _vals;

        public GameComponent_Harassment(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            Instance = this;
            _needReinject = true; // re-apply injected locks on the first tick, once everything is spawned
        }

        public void RecordLockedExtra(Apparel app, string name, string key)
        {
            if (app == null) return;
            if (lockedExtras == null) lockedExtras = new List<LockedExtraRecord>();
            lockedExtras.Add(new LockedExtraRecord { apparel = app, stampName = name, stampKey = key });
        }

        public void RemoveLockedExtra(Apparel app)
        {
            lockedExtras?.RemoveAll(r => r == null || r.apparel == app);
        }

        /// <summary>Returns an existing profile without creating one. Use in hot/UI paths (e.g. gizmos).</summary>
        public PawnProfile GetProfileIfExists(Pawn pawn)
        {
            if (pawn == null) return null;
            return profiles.TryGetValue(pawn.thingIDNumber, out var p) ? p : null;
        }

        public PawnProfile GetProfile(Pawn pawn)
        {
            if (pawn == null) return null;
            int id = pawn.thingIDNumber;
            if (profiles.TryGetValue(id, out var p)) return p;

            p = new PawnProfile();
            // Deterministic roll seeded by pawn id so morality/confidence are stable.
            Rand.PushState(id ^ 0x5EED1A7);
            float m = Rand.Value;
            // Roughly: 45% decent, 35% questionable, 20% evil.
            p.morality = m < 0.45f ? Morality.Decent : (m < 0.80f ? Morality.Questionable : Morality.Evil);
            p.confidence = Rand.Range(1f, 100f);
            Rand.PopState();

            profiles[id] = p;
            return p;
        }

        // Slowly decay hypnosis conditioning over time.
        public override void GameComponentTick()
        {
            if (_needReinject) { _needReinject = false; HarassmentEngine.ReinjectLockedExtras(lockedExtras); }
            int now = Find.TickManager.TicksGame;

            // Curious visitors drawn by an auctioned photo of a collared pet.
            if (curiousVisitorTick > 0 && now >= curiousVisitorTick)
            {
                curiousVisitorTick = -1;
                HarassmentEngine.FireCuriousVisitors();
            }
            // Colony notoriety: once the colony's depravity is well known, an outside faction hears of it (~daily).
            if ((now + 12007) % 60000 == 0 && notoriety > 0)
            {
                if (notoriety >= 10) HarassmentEngine.NotorietyConsequence();
                notoriety = System.Math.Max(0, notoriety - 2);
            }
            // World-layer incidents fire on their own mean-time-between, gated by each worker's preconditions.
            // An infamous colony (Karma & Reputation regard) draws the world's attention faster: rival slavers
            // smell profit and moralists mount rescues. Neutral 1.0 without Karma installed.
            if ((now + 41011) % 60000 == 0)   // phased off the notoriety decay above so they never share a tick
            {
                float mtbFactor = 1f;
                if (ReputationBridge.TryGetColonyReputation(out float rep))
                {
                    if (rep <= -750f) mtbFactor = 0.4f;
                    else if (rep <= -250f) mtbFactor = 0.6f;
                    else if (rep >= 250f) mtbFactor = 1.4f; // a renowned colony draws less underworld attention
                }
                TryFireWorldIncident("RJWSH_ScandalLeak", 6f * mtbFactor);
                TryFireWorldIncident("RJWSH_RivalSlaver", 9f * mtbFactor);
                TryFireWorldIncident("RJWSH_RescueRaid", 12f * mtbFactor);
                // Nemesis returns: a remembered escapee comes back at the head of a raid.
                HarassmentEngine.NemesisTick();
            }
            // Active rescue raids: a raider reaching the flagged pet frees them.
            if ((now + 137) % 500 == 0)   // phased off the per-map EvilKeyScavenge, which also runs at 500
                foreach (var m in Find.Maps) HarassmentEngine.RescueRaidTick(m);

            // Pet market: weekly restock, and finalize any purchased pet once its drop pod has landed.
            if (Find.AnyPlayerHomeMap != null && (marketRefreshTick < 0 || now >= marketRefreshTick)) RefreshMarket();
            if (_pendingDeliveries.Count > 0 && now % 60 == 0)
            {
                for (int i = _pendingDeliveries.Count - 1; i >= 0; i--)
                {
                    var pd = _pendingDeliveries[i];
                    if (pd.Item1 == null || pd.Item1.Dead) { _pendingDeliveries.RemoveAt(i); continue; }
                    if (pd.Item1.Spawned) { HarassmentEngine.MarketDeliverPet(pd.Item1, FindPawnByIdLocal(pd.Item2), pd.Item3); _pendingDeliveries.RemoveAt(i); }
                }
            }

            // ~once per in-game hour: conditioning decay + history sampling.
            // Phased (2213) so this whole-store sweep never lands on the same tick as the map-side 2500-cadence
            // passes (ControlUpkeep breakout, ConditioningUpkeep, RecomputeHeadGirls). See MapComponent.Due().
            if ((now + 2213) % 2500 != 0) return;
            foreach (var p in profiles.Values)
            {
                if (p.hypnosisLevel > 0f)
                    p.hypnosisLevel = System.Math.Max(0f, p.hypnosisLevel - 0.5f);
                // Sample history for owned/conditioned pets so the dashboard can graph their break-in over time.
                if (p.ownerId >= 0 || p.relationshipOwnerId >= 0 || p.hypnosisLevel > 5f)
                    p.RecordHistorySample();
                // Passive drift of the deep sexual attributes (wear recovers, trauma/addiction fade slowly).
                p.sex?.HourlyDrift(false);
            }
        }

        /// <summary>Rolls a mean-time-between and fires the named RJWSH incident on a player home map when its
        /// worker preconditions are met. Uses the incident/worker infrastructure (letters, refire gating) but is
        /// driven here rather than by the vanilla storyteller, so pacing is predictable.</summary>
        private void TryFireWorldIncident(string defName, float mtbDays)
        {
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            if (!Rand.MTBEventOccurs(mtbDays, 60000f, 60000f)) return;
            var map = Find.RandomPlayerHomeMap;
            if (map == null) return;
            var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            if (def.Worker.CanFireNow(parms)) def.Worker.TryExecute(parms);
        }

        // Restock the market with 4-8 fresh pets (~weekly). Old unbought stock is discarded.
        public void RefreshMarket()
        {
            if (market != null) for (int i = 0; i < market.Count; i++) { try { market[i]?.pawn?.Discard(true); } catch { } }
            market = new List<MarketEntry>();
            int n = Rand.RangeInclusive(4, 8);
            for (int i = 0; i < n; i++) { var e = HarassmentEngine.MakeMarketEntry(); if (e != null) market.Add(e); }
            marketRefreshTick = Find.TickManager.TicksGame + 420000; // ~7 in-game days
        }

        // Buy a market pet with silver or faction goodwill: it drops in by pod, then gets collared to the strongest/leader.
        public bool BuyMarketPawn(MarketEntry entry, bool useGoodwill)
        {
            if (entry?.pawn == null || market == null || !market.Contains(entry)) return false;
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map == null) return false;
            if (useGoodwill)
            {
                if (entry.goodwillFaction == null || entry.goodwillFaction.PlayerGoodwill < entry.goodwillCost)
                { Messages.Message("Not enough goodwill for that pet.", MessageTypeDefOf.RejectInput, false); return false; }
                entry.goodwillFaction.TryAffectGoodwillWith(Faction.OfPlayer, -entry.goodwillCost, false, false);
            }
            else
            {
                if (HarassmentEngine.ColonySilver(map) < entry.priceSilver)
                { Messages.Message("Not enough silver for that pet.", MessageTypeDefOf.RejectInput, false); return false; }
                HarassmentEngine.SpendColonySilver(map, entry.priceSilver);
            }
            var pawn = entry.pawn;
            // Chance to be dropped off on foot by a passing trader instead of a drop pod.
            bool walked = false;
            IntVec3 edge;
            if (Rand.Chance(0.45f) && CellFinder.TryFindRandomEdgeCellWith(
                    c => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReachColony(c),
                    map, CellFinder.EdgeRoadChance_Friendly, out edge))
            {
                try { pawn.SetFaction(Faction.OfPlayer); } catch { }   // so they don't wander off the edge before the collar finalizes
                GenSpawn.Spawn(pawn, edge, map);
                walked = true;
            }
            if (!walked)
            {
                var cell = DropCellFinder.TradeDropSpot(map);
                DropPodUtility.DropThingsNear(cell, map, Gen.YieldSingle<Thing>(pawn), 110, false, false, false, false);
            }
            var keyHolder = HarassmentEngine.StrongestOrLeader(map);
            _pendingDeliveries.Add(new System.ValueTuple<Pawn, int, int>(pawn, keyHolder?.thingIDNumber ?? -1, entry.role));
            market.Remove(entry);
            LookTargets look = pawn.Spawned ? new LookTargets(pawn) : new LookTargets(new TargetInfo(DropCellFinder.TradeDropSpot(map), map));
            Messages.Message("A new pet " + (walked ? "was dropped off by a passing trader" : "is dropping in by pod") + (keyHolder != null ? " for " + keyHolder.LabelShort : "") + ".",
                look, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        // Routed through the shared per-tick pawn index (was a linear cross-map AllPawnsSpawned scan).
        private static Pawn FindPawnByIdLocal(int id) => PawnLookup.AnyMap(id);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref profiles, "profiles", LookMode.Value, LookMode.Deep, ref _keys, ref _vals);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && profiles == null)
                profiles = new Dictionary<int, PawnProfile>();
            Scribe_Collections.Look(ref lockedExtras, "lockedExtras", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lockedExtras == null)
                lockedExtras = new List<LockedExtraRecord>();
            Scribe_Values.Look(ref notoriety, "notoriety", 0);
            Scribe_Collections.Look(ref haremPresets, "haremPresets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && haremPresets == null) haremPresets = new List<HaremPreset>();
            Scribe_Values.Look(ref curiousVisitorTick, "curiousVisitorTick", -1);
            Scribe_Collections.Look(ref circulatingPhotos, "circulatingPhotos", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && circulatingPhotos == null)
                circulatingPhotos = new List<CirculatingPhoto>();
            Scribe_Collections.Look(ref nemeses, "nemeses", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nemeses == null)
                nemeses = new List<Nemesis>();
            Scribe_Collections.Look(ref market, "market", LookMode.Deep);
            Scribe_Values.Look(ref marketRefreshTick, "marketRefreshTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && market == null) market = new List<MarketEntry>();
        }
    }

    /// <summary>A scandalous photo that has been sold or handed into the wider world - kept so the gallery can
    /// still list it after the physical copy has left the map.</summary>
    /// <summary>A buyable pet in the weekly market: a generated pawn (held unspawned until purchased), a role, and a
    /// silver price plus an alternative faction-goodwill cost.</summary>
    public class MarketEntry : IExposable
    {
        public Pawn pawn;
        public int role;
        public int priceSilver;
        public int goodwillCost;
        public Faction goodwillFaction;
        public void ExposeData()
        {
            Scribe_Deep.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref role, "role", 0);
            Scribe_Values.Look(ref priceSilver, "priceSilver", 0);
            Scribe_Values.Look(ref goodwillCost, "goodwillCost", 0);
            Scribe_References.Look(ref goodwillFaction, "goodwillFaction");
        }
    }

    public class CirculatingPhoto : IExposable
    {
        public Pawn subject;
        public string lore;
        public string holder;
        public Faction faction;   // real faction holding a photo sold off the map (null = on-map / vague)
        public void ExposeData()
        {
            Scribe_References.Look(ref subject, "subject");
            Scribe_Values.Look(ref lore, "lore");
            Scribe_Values.Look(ref holder, "holder");
            Scribe_References.Look(ref faction, "faction");
        }
    }

    /// <summary>Persisted record of an injected (non-bondage_gear_def) lock so it can be re-applied on load.</summary>
    public class LockedExtraRecord : IExposable
    {
        public Apparel apparel;
        public string stampName;
        public string stampKey;

        public void ExposeData()
        {
            Scribe_References.Look(ref apparel, "apparel");
            Scribe_Values.Look(ref stampName, "stampName");
            Scribe_Values.Look(ref stampKey, "stampKey");
        }
    }
}
