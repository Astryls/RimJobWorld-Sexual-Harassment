using System.Collections.Generic;
using RimWorld;
using rjw;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Drives the periodic harassment roll on each map. Cheap by default: most ticks early-return on
    /// a single settings bool, and the real work only runs once per scan interval.
    /// </summary>
    public class MapComponent_HarassmentScan : MapComponent
    {
        private int nextScanTick = -1;
        private int lastEventTick = -999999;

        // Deferred handoffs run one tick out of the job call stack (transient; not saved).
        // kind 0 = begin physical, 1 = forced act, 2 = casual sex, 3 = onahole capture, 4 = bound in public.
        private readonly List<(Pawn harasser, Pawn victim, int kind)> pending = new List<(Pawn, Pawn, int)>();

        // Per-approach-type cooldown: an approach cannot be re-picked on this map until its ready tick.
        private readonly Dictionary<ApproachType, int> approachReady = new Dictionary<ApproachType, int>();

        // Multi-line harassment: flavor bubble lines scheduled to fire over the next few seconds (transient).
        private readonly List<(Pawn speaker, Pawn recipient, InteractionDef def, int tick)> scheduledLines
            = new List<(Pawn, Pawn, InteractionDef, int)>();

        /// <summary>Queues a flavor interaction line to fire after a delay, so SpeakUp shows it as a bubble.</summary>
        public static void ScheduleLine(Pawn speaker, Pawn recipient, InteractionDef def, int delayTicks)
        {
            var map = speaker?.Map;
            if (map == null || def == null || recipient == null) return;
            map.GetComponent<MapComponent_HarassmentScan>()?.scheduledLines
                .Add((speaker, recipient, def, Find.TickManager.TicksGame + delayTicks));
        }

        // Delayed dispatch of a staged act (kind: 0=physical, 1=forced act, 2=casual sex), used to let an
        // exchange play out before the act fires (e.g. consensual flirt banter -> then sex).
        private readonly List<(Pawn a, Pawn b, int kind, int tick)> scheduledActs
            = new List<(Pawn, Pawn, int, int)>();
        public static void ScheduleAct(Pawn a, Pawn b, int kind, int delayTicks)
        {
            var map = a?.Map ?? b?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.scheduledActs
                .Add((a, b, kind, Find.TickManager.TicksGame + delayTicks));
        }

        // ── Cadence phasing ──────────────────────────────────────────────────
        // Every periodic system used to key off `now % N == 0`, so four heavy passes (ConditioningUpkeep,
        // RecomputeHeadGirls, the ControlUpkeep breakout block, and GameComponent's profile sweep) all landed on
        // the SAME tick every 2500 ticks - a visible hitch roughly every 41s of game time. Each system now
        // carries a distinct prime-ish phase, so the same work is spread across the interval instead of
        // stacking. Cadences are unchanged; only the phase moved.
        //
        // `_mapPhase` additionally de-syncs maps from each other, so a colony + a caravan map do not fire their
        // upkeep on the same ticks either.
        private readonly int _mapPhase;

        /// <summary>True on exactly one tick per `interval`, offset by this map's phase plus a per-system phase.
        /// Returns false for a non-positive interval (a settings value of 0 disables that system).</summary>
        private bool Due(int now, int interval, int phase)
            => interval > 0 && ((now + _mapPhase + phase) % interval) == 0;

        public MapComponent_HarassmentScan(Map map) : base(map)
        {
            // Stable per-map, derived from the map's own id so it survives save/load unchanged.
            _mapPhase = map != null ? System.Math.Abs(map.uniqueID * 37) % 1000 : 0;
        }

        public static MapComponent_HarassmentScan For(Map map) => map?.GetComponent<MapComponent_HarassmentScan>();

        public bool ApproachReady(ApproachType t, int now) => !approachReady.TryGetValue(t, out var rt) || now >= rt;

        public void RecordApproach(ApproachType t, int now, int cooldown)
        {
            if (cooldown > 0) approachReady[t] = now + cooldown;
        }

        /// <summary>Queues the physical stage to run on the next tick, off the job's call stack.</summary>
        public static void EnqueuePhysical(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map ?? victim?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.pending.Add((harasser, victim, 0));
        }

        /// <summary>Queues the RJW forced act to run on the next tick, off the strip job's call stack.</summary>
        public static void EnqueueForcedAct(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map ?? victim?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.pending.Add((harasser, victim, 1));
        }

        /// <summary>Queues a player-directed (non-interactive) physical stage to run next tick.</summary>
        public static void EnqueueDirectedPhysical(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map ?? victim?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.pending.Add((harasser, victim, 4));
        }

        /// <summary>Queues consensual casual sex (flirt willing path) to run next tick, off the job stack.</summary>
        public static void EnqueueCasualSex(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map ?? victim?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.pending.Add((harasser, victim, 2));
        }

        /// <summary>Queues the end-of-scene handler (extend or restrain) to run next tick, off the job stack.</summary>
        public static void EnqueueSceneEnd(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map ?? victim?.Map;
            map?.GetComponent<MapComponent_HarassmentScan>()?.pending.Add((harasser, victim, 3));
        }

        // ── Per-tick pawn index ──────────────────────────────────────────────
        // Rebuilt at most once per game tick. Kills the old O(n^2) owner lookups (a linear AllPawnsSpawned scan
        // per profiled pet, every 60 ticks) and lets the upkeep loops iterate only humanlikes / profiled pawns,
        // skipping wildlife. Snapshot lists also make the loops safe if a pawn despawns mid-pass.
        private int _indexStamp = -1;
        private readonly Dictionary<int, Pawn> _byId = new Dictionary<int, Pawn>(128);
        private readonly List<Pawn> _humanlikes = new List<Pawn>(64);
        private readonly List<Pawn> _profiled = new List<Pawn>(32);

        private void EnsureIndex()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (_indexStamp == now) return;
            _indexStamp = now;
            _byId.Clear(); _humanlikes.Clear(); _profiled.Clear();
            var gc = GameComponent_Harassment.Instance;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null) continue;
                _byId[p.thingIDNumber] = p;
                if (p.RaceProps != null && p.RaceProps.Humanlike) _humanlikes.Add(p);
                if (gc != null && gc.GetProfileIfExists(p) != null) _profiled.Add(p);
            }
        }

        /// <summary>O(1) spawned-pawn lookup by thingIDNumber on this map (builds this tick's index on demand).</summary>
        public Pawn PawnById(int id)
        {
            EnsureIndex();
            return _byId.TryGetValue(id, out var p) && p.Spawned ? p : null;
        }

        public override void MapComponentTick()
        {
            DrainPending();

            var s = RimJobWorldSexualHarassmentMod.Settings;
            if (s == null || !s.masterEnabled) return;
            if (GameComponent_Harassment.Instance == null) return;

            int now = Find.TickManager.TicksGame;

            ControlUpkeep(now, s);
            // Phases chosen so no two of these share a tick within their common period (see Due()).
            if (Due(now, 2500, 811)) ConditioningUpkeep();
            if (scheduledLines.Count > 0) DrainScheduledLines(now);
            if (scheduledActs.Count > 0) DrainScheduledActs(now);
            if (Due(now, s.begInterval, 67)) BegUpkeep();
            if (Due(now, s.affectionInterval, 233)) AffectionUpkeep(now);
            if (Due(now, 500, 0)) HarassmentEngine.EvilKeyScavenge(map);
            if (Due(now, 550, 275)) HarassmentEngine.PhotoScavenge(map);   // never coincides with the key scan
            if (Due(now, 2500, 1607)) HarassmentEngine.RecomputeHeadGirls(map);
            if (Due(now, 1200, 431)) HarassmentEngine.HeadGirlTick(map);
            if (Due(now, 350, 149)) HarassmentEngine.MemoryReactionScan(map);

            if (nextScanTick < 0)
            {
                // Stagger initial scans so multiple maps do not all fire on the same tick.
                nextScanTick = now + Rand.Range(0, s.scanIntervalTicks);
                return;
            }

            if (now < nextScanTick) return;
            nextScanTick = now + s.scanIntervalTicks;

            // Global anti-spam: enforce a minimum gap between harassment events on this map.
            if (now - lastEventTick < s.minTicksBetweenEvents) return;

            if (!map.IsPlayerHome && map.mapPawns.FreeColonistsSpawnedCount == 0 && map.mapPawns.AllPawnsSpawnedCount == 0)
                return;

            // Once an event rolls, apply the cooldown whether or not a valid (orientation-matching) target
            // was found - so a dud attempt waits out the configured gap instead of retrying every scan.
            if (Rand.Chance(s.eventChancePerScan))
            {
                HarassmentEngine.TryRunOnMap(map);
                lastEventTick = now;
            }
        }

        // Forced-follow + auto-service upkeep for collared/key-locked pawns.
        private void ControlUpkeep(int now, HarassmentSettings s)
        {
            bool followTick = Due(now, 60, 0);
            bool autoTick = Due(now, 250, 0);
            bool shockTick = Due(now, 90, 0);
            bool breakoutTick = Due(now, 2500, 0);   // anchor phase; the other 2500-cadence systems offset off this
            if (!followTick && !autoTick && !shockTick && !breakoutTick) return;

            if (breakoutTick) HarassmentEngine.ReconcileOwnerRelations(this.map);
            // Raids build trauma across the colony while they rage.
            if (breakoutTick && HarassmentEngine.RaidChaosActive(map)) HarassmentEngine.RaidTraumaTick(map);

            EnsureIndex();
            var gc = GameComponent_Harassment.Instance;
            var pawns = _profiled;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                var prof = gc.GetProfileIfExists(p);
                if (prof == null) continue;

                // Base-game slavery tie-in: keep collared, conditioned colony slaves suppressed (no rebellion).
                if (autoTick) SlaveryHooks.SyncSuppression(p, prof);

                // A pawn under active control loses free will (owner-directed tasks) in a detected free-will mod.
                if (autoTick && (prof.ownerId >= 0 || prof.aiControlled)) FreeWillBridge.SuppressFor(p);

                // Karma as an active driver: slowly shape the attributes of already-tracked pawns (hourly).
                if (breakoutTick && prof.sex != null && prof.sex.seeded) HarassmentEngine.KarmaDriftTick(p);

                // Continuous shock-until-downed/dead runs regardless of follow/auto-service ownership.
                if (shockTick && prof.shockUntil > 0) HandleShockMode(p, prof);

                // Gangbang punishment queue (flee-beating retaliation) - pulls aggressors regardless of owner link.
                if (prof.gangbangCount > 0 && autoTick) HarassmentEngine.GangbangTick(p, prof);

                // Forced nudity strips non-locked clothing regardless of follow/auto-service state.
                if (autoTick && prof.forceNudity && !p.Dead && !p.Downed && !HarassmentEngine.IsInOnaholeBed(p))
                    HarassmentEngine.StripToBondage(p);

                if (p.Dead) continue;

                // End a capped fight-back scuffle (force both pawns out of the social fight) - any ownership.
                if (prof.scuffleEndTick > 0 && now >= prof.scuffleEndTick)
                {
                    prof.scuffleEndTick = -1;
                    HarassmentEngine.EndScuffle(p);
                }

                // Onahole captivity (any source): a time limit counts down, then the slave begs the owner to be
                // let out (no auto-release). Runs even without an active owner link.
                if (HarassmentEngine.IsInOnaholeBed(p))
                {
                    HarassmentEngine.ApplyOnaholeBoundHediff(p); // ensure the bound timer hediff (idempotent)
                    if (prof.onaholeReleaseTick <= 0) prof.onaholeReleaseTick = now + 6000; // ~2.4h default limit
                    // 600 is a multiple of followTick's 60, and both go through Due() with the same map phase,
                    // so this still lands on a tick where the enclosing loop actually runs. Do NOT change this
                    // back to a raw `now % 600` - the map phase would put it permanently out of alignment.
                    else if (now >= prof.onaholeReleaseTick && Due(now, 600, 0))
                        HarassmentEngine.BegOwnerForRelease(p, FindPawnById(prof.ownerId));
                    continue;
                }

                // Depth systems: breaking stages, autonomous behavior, rivalry, pecking order, trauma,
                // addiction, codependency. Run for any tracked (seeded) pet, independent of the control toggles.
                if (prof.sex != null && prof.sex.seeded)
                {
                    Pawn depthOwner = (prof.ownerId >= 0) ? FindPawnById(prof.ownerId)
                                    : (prof.relationshipOwnerId >= 0 ? FindPawnById(prof.relationshipOwnerId) : null);
                    if (breakoutTick)
                    {
                        // Core break-in progression + visible hediffs always run; only the heavier interaction
                        // sim (rivalry/pecking/codependency/training/autonomy/addiction) is gated by the toggle.
                        HarassmentEngine.DepthStageTick(p, prof);
                        HarassmentEngine.DepthTraumaTick(p, prof);
                        HarassmentEngine.SyncAttributeHediffs(p, prof); // surface trauma/addiction + marks + submission need
                        if (depthOwner != null && s.enableDepthSystems)
                        {
                            HarassmentEngine.DepthRivalryTick(p, prof, depthOwner);
                            HarassmentEngine.DepthPeckingTick(p, prof, depthOwner);
                            HarassmentEngine.DepthCodependencyTick(p, prof, depthOwner);
                            HarassmentEngine.DepthTrainingTick(p, prof, depthOwner); // ongoing conditioning focus
                        }
                    }
                    if (autoTick && s.enableDepthSystems)
                    {
                        HarassmentEngine.DepthAddictionTick(p, prof);
                        if (depthOwner != null) HarassmentEngine.DepthAutonomousTick(p, prof, depthOwner);
                    }
                }

                if (prof.ownerId < 0 || (!prof.followOwner && !prof.autoService)) continue;

                Pawn owner = FindPawnById(prof.ownerId);
                if (owner == null || !owner.Spawned || owner.Dead)
                {
                    prof.followOwner = false; prof.autoService = false; prof.ownerId = -1;
                    continue;
                }
                if (p.Downed) continue;
                // Bound-in-public timed release (still auto-releases).
                if (prof.onaholeReleaseTick > 0 && now >= prof.onaholeReleaseTick)
                {
                    prof.onaholeReleaseTick = -1; prof.boundInPublic = false;
                }

                // When the owner allows it (toggle) or is asleep, the collared pawn is freed to attend its own
                // needs - sleep, food, drink, hygiene/bathroom - by its normal think tree.
                bool needsAllowed = HarassmentEngine.NeedsAllowed(prof, owner);

                if (autoTick && !needsAllowed && s.enableAmbientBanter && Rand.Chance(0.12f * s.ambientBanterScale))
                    HarassmentEngine.FireOwnerSlaveBanter(owner, p);

                bool scheduled = prof.schedule != null && prof.schedule.Count == 24;
                if (scheduled && followTick) HarassmentEngine.RunScheduleTick(p, prof, owner); // full schedule overrides toggles
                bool curfewFollow = !scheduled && prof.curfew && HarassmentEngine.IsNightFor(p); // toggle curfew: confined to owner at night
                if (followTick && ((prof.followOwner && !needsAllowed) || curfewFollow))
                {
                    var jd = p.CurJobDef;
                    bool busyVital = jd == RJWSH_JobDefOf.RJWSH_Follow || jd == JobDefOf.Ingest || jd == JobDefOf.LayDown
                                     || jd == RJWSH_JobDefOf.RJWSH_BeingStripped
                                     || (p.jobs?.curDriver is rjw.JobDriver_Sex);
                    if (!busyVital && p.CanReach(owner, PathEndMode.Touch, Danger.Deadly))
                    {
                        var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Follow, owner);
                        p.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                }
                // Stay leash: hold the pawn at the chosen spot indefinitely (hidden-draft style). Once on the hold
                // job they stand put until the survival valve frees them for a critical need; we re-pin them here.
                else if (followTick && prof.stayCell.IsValid)
                {
                    var jd = p.CurJobDef;
                    bool staying = jd == RJWSH_JobDefOf.RJWSH_StayPut;
                    bool busy = jd == JobDefOf.Ingest || jd == JobDefOf.LayDown
                                || jd == RJWSH_JobDefOf.RJWSH_BeingStripped
                                || (p.jobs?.curDriver is rjw.JobDriver_Sex);
                    if (!staying && !busy && prof.stayCell.InBounds(this.map)
                        && p.CanReach(prof.stayCell, PathEndMode.OnCell, Danger.Deadly))
                    {
                        p.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_StayPut, prof.stayCell), JobCondition.InterruptForced);
                    }
                }

                // Auto-cast reward / discipline: the owner tends the pet on their own (respects the cooldown).
                if (autoTick && now >= prof.tendCooldownTick && p.Awake() && !p.Downed && !HarassmentEngine.IsBusyInAct(p)
                    && owner.Spawned && !owner.Downed && owner.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                {
                    bool fired = false;
                    if (prof.autoDiscipline && Rand.Chance(0.4f)) { HarassmentEngine.StartDiscipline(owner, p); fired = true; }
                    else if (prof.autoReward && Rand.Chance(0.4f)) { HarassmentEngine.StartReward(owner, p); fired = true; }
                    if (fired) prof.tendCooldownTick = now + Rand.Range(10000, 20000); // space out auto sessions (~4-8h)
                }

                // Auto-parade toggle (a full schedule overrides this).
                if (!scheduled && autoTick && prof.autoParade && now >= prof.paradeCooldownTick && !p.Downed
                    && !HarassmentEngine.IsBusyInAct(p) && HarassmentEngine.IsDaytimeFor(p))
                {
                    HarassmentEngine.DepthStartParade(owner, p);
                    prof.paradeCooldownTick = now + Rand.Range(45000, 75000); // ~once per day, staggered
                }

                // AI-controlled slaves periodically attempt a will-based breakout (may end the control).
                if (breakoutTick && prof.aiControlled)
                {
                    if (HarassmentEngine.SlaveWillBreakoutTick(p, owner)) continue; // broke free
                }

                // Raid chaos: while hostiles rampage, an AI-controlled captive seizes the distraction to break for it.
                if (breakoutTick && prof.aiControlled && now >= prof.resistCooldownTick && !p.Downed
                    && HarassmentEngine.RaidChaosActive(this.map)
                    && Rand.Chance(0.25f * (1f - prof.hypnosisLevel / 150f)))
                {
                    HarassmentEngine.AttemptFightBack(p); continue;
                }
                // Owner downed (e.g. in a raid) -> high-will slaves seize the chance to free themselves.
                if (breakoutTick && owner.Downed && prof.hypnosisLevel < 50f && now >= prof.resistCooldownTick
                    && Rand.Chance(1f - prof.hypnosisLevel / 100f))
                {
                    HarassmentEngine.AttemptFightBack(p); continue;
                }
                // Auto-resist toggle: keep attempting to fight back whenever off cooldown.
                if (breakoutTick && prof.autoResist && now >= prof.resistCooldownTick && !p.Downed)
                {
                    HarassmentEngine.AttemptFightBack(p); continue;
                }
                // Volatile pets (broken by fear, not trust) spontaneously lash out even when deeply conditioned -
                // the less rapport they have, the likelier the flare. This is the whip-broken instability.
                if (breakoutTick && !prof.autoResist && prof.rapport < 25f && prof.hypnosisLevel >= 40f
                    && now >= prof.resistCooldownTick && !p.Downed
                    && Rand.Chance(0.12f * (1f - prof.rapport / 25f)))
                {
                    HarassmentEngine.AttemptFightBack(p); continue;
                }

                if (autoTick && prof.autoService && !needsAllowed && now >= prof.controlCooldownTick
                    && !(p.jobs?.curDriver is rjw.JobDriver_Sex))
                {
                    // Venue service first: a pet set up for Hospitality room service solicits a guest, or an
                    // employed Gastronomy waiter serves a table (both via the host mod's own validated jobs).
                    if (HarassmentEngine.TryVenueService(p))
                    {
                        prof.controlCooldownTick = now + s.autoServiceIntervalTicks;
                    }
                    else if (prof.aiControlled)
                    {
                        if (Rand.Chance(0.5f)) HarassmentEngine.FireFlavorLine(owner, p, RJWSH_InteractionDefOf.RJWSH_TalkDown);
                        if (s.enableControllerBehaviors) HarassmentEngine.RunControllerBehavior(owner, p);
                        else HarassmentEngine.RunService(p, owner, prof.serviceInteraction);
                        prof.controlCooldownTick = now + s.autoServiceIntervalTicks;
                    }
                    else
                    {
                        var serviceTarget = HarassmentEngine.PickServiceTarget(p, prof, owner);
                        if (serviceTarget != null)
                        {
                            HarassmentEngine.RunService(p, serviceTarget, prof.serviceInteraction);
                            prof.controlCooldownTick = now + s.autoServiceIntervalTicks;
                        }
                    }
                }
            }
        }

        // Shocks the wearer each shock-tick: non-lethally until downed (mode 1) or lethally until dead (mode 2).
        private void HandleShockMode(Pawn p, PawnProfile prof)
        {
            if (p == null || p.Dead || !p.Spawned || !HarassmentEngine.WearingControlCollar(p))
            {
                prof.shockUntil = 0;
                return;
            }
            if (prof.shockUntil == 1)
            {
                // Until downed: stop the instant they collapse, and never apply a lethal jolt to them.
                if (p.Downed)
                {
                    prof.shockUntil = 0;
                    Messages.Message(p.LabelShort + " collapsed under the shocks.",
                        new LookTargets(p), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                HarassmentEngine.ShockTowardDowned(p);
            }
            else // until dead
            {
                HarassmentEngine.ApplyLethalShock(p);
            }
        }

        // Ensures every control-collared pawn carries the conditioning hediff, which then self-ramps and
        // grants traits over time. Covers collars applied outside LockControlCollar and pre-update saves.
        private void ConditioningUpkeep()
        {
            EnsureIndex();
            var pawns = _humanlikes;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (HarassmentEngine.IsCollared(p))   // our control collar OR a Simple Slavery Collars collar
                {
                    HarassmentEngine.ApplyConditioningHediff(p);
                    HarassmentEngine.IsolationConditioningTick(p); // kept away from allies -> the hold deepens
                }
                HarassmentEngine.BondageBedTick(p); // BondageBed Torture: strapped in deepens the hold
            }
        }

        // Fires due flavor lines as long as both pawns are still spawned, near, and not already mid-act.
        private void DrainScheduledActs(int now)
        {
            for (int i = scheduledActs.Count - 1; i >= 0; i--)
            {
                var (a, b, kind, tick) = scheduledActs[i];
                if (now < tick) continue;
                scheduledActs.RemoveAt(i);
                if (a == null || b == null || !a.Spawned || !b.Spawned || a.Dead || b.Dead) continue;
                if (kind == 2) HarassmentEngine.StartCasualSex(a, b);
                else if (kind == 1) HarassmentEngine.ForceForcedAct(a, b);
                else HarassmentEngine.BeginPhysicalOrForced(a, b);
            }
        }

        private void DrainScheduledLines(int now)
        {
            for (int i = scheduledLines.Count - 1; i >= 0; i--)
            {
                var (speaker, recipient, def, tick) = scheduledLines[i];
                if (now < tick) continue;
                scheduledLines.RemoveAt(i);
                if (speaker == null || recipient == null || !speaker.Spawned || !recipient.Spawned
                    || speaker.Dead || recipient.Dead) continue;
                if (speaker.Position.DistanceTo(recipient.Position) > 8f) continue;
                if (HarassmentEngine.IsBusyInAct(speaker) || HarassmentEngine.IsBusyInAct(recipient)) continue;
                HarassmentEngine.FireFlavorLine(speaker, recipient, def);
            }
        }

        // Bound/onahole-trapped victims periodically cry for help until they are freed (conditioned pawns don't).
        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            DragBubbleOverlay.OnGUIFor(map);
        }

        // Occasional consensual / content-pet affection during downtime (never interrupts work).
        private void AffectionUpkeep(int now)
        {
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;
            EnsureIndex();
            var pawns = _humanlikes;
            for (int i = 0; i < pawns.Count; i++)
            {
                var a = pawns[i];
                var ap = gc.GetProfileIfExists(a);
                if (ap != null && now < ap.affectionCooldownTick) continue;
                if (!HarassmentEngine.IsFreeForAffection(a)) continue;
                var b = HarassmentEngine.FindAffectionPartner(a);
                if (b == null) continue;
                float w = HarassmentEngine.AffectionWillingness(a, b);
                if (!Rand.Chance(w * 0.2f)) continue; // occasional even for lovers
                HarassmentEngine.TriggerAffection(a, b, Rand.Bool ? AffectionKind.Kiss : AffectionKind.HoldHands);
            }
        }

        private void BegUpkeep()
        {
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;
            var s = RimJobWorldSexualHarassmentMod.Settings;
            EnsureIndex();
            var pawns = _profiled;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (!p.RaceProps.Humanlike || p.Dead || !p.Awake() || p.Downed) continue;
                var prof = gc.GetProfileIfExists(p);
                if (prof == null) continue;

                bool onahole = HarassmentEngine.IsInOnaholeBed(p);
                bool bound = prof.boundInPublic;
                if (bound && !HarassmentEngine.WearingLockedHarassmentGear(p)) { prof.boundInPublic = false; bound = false; }

                // Captive begging (the "help / it hurts / let me go" cries) fires ONLY while in an onahole or
                // bound in public, and only from a pawn not yet conditioned into accepting it.
                if (onahole || bound)
                {
                    if (prof.IsConditioned) continue;                       // conditioned captives accept it - no begging
                    if (!onahole && HarassmentEngine.IsBusyInAct(p)) continue; // a bound pawn waits out an active act
                    HarassmentEngine.BegForHelp(p);
                    if (Rand.Chance(0.15f)) HarassmentEngine.TryDispatchRescuer(p); // a caring colonist may come over
                    if (Rand.Chance(0.15f)) HarassmentEngine.FireDisplayGlance(p);   // a passerby glances at the display
                    continue;
                }

                // Roaming collared/owned pet: occasional conditioning-aware self-talk, never the captive begging.
                bool ownedPet = prof.ownerId >= 0 || prof.relationshipOwnerId >= 0 || HarassmentEngine.WearingControlCollar(p);
                if (!ownedPet || HarassmentEngine.IsBusyInAct(p)) continue;
                if (s == null || !s.enableAmbientBanter) continue; // ambient pet chatter is pure flavor
                float bscale = s.ambientBanterScale;
                if (Rand.Chance(0.06f * bscale)) HarassmentEngine.FireFellowPetBanter(p, prof); // two pets commiserate
                else if (Rand.Chance(0.1f * bscale)) HarassmentEngine.FirePetSelfTalk(p, prof);
            }
        }

        // Routed through the per-tick index (was a linear AllPawnsSpawned scan, called per profiled pet).
        private Pawn FindPawnById(int id) => PawnById(id);

        private void DrainPending()
        {
            if (pending.Count == 0) return;
            var snapshot = pending.ToArray();
            pending.Clear();
            foreach (var (harasser, victim, kind) in snapshot)
            {
                if (harasser == null || victim == null) continue;
                if (!harasser.Spawned || !victim.Spawned || harasser.Dead || victim.Dead) continue;
                if (kind == 4) HarassmentEngine.BeginDirectedPhysical(harasser, victim);
                else if (kind == 3) HarassmentEngine.HandleSceneEnd(harasser, victim);
                else if (kind == 2) HarassmentEngine.StartCasualSex(harasser, victim);
                else if (kind == 1) HarassmentEngine.ForceForcedAct(harasser, victim);
                else HarassmentEngine.BeginPhysicalOrForced(harasser, victim);
            }
        }
    }
}
