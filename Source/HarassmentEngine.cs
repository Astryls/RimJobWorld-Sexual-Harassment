using System.Collections.Generic;
using System.Linq;
using RimWorld;
using rjw;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Core decision logic. Picks harassers and targets, fires the verbal stage as a real social
    /// interaction (so SpeakUp/RimTalk voice it and vanilla applies the thoughts), then rolls
    /// escalation through the intervene gate into a physical stage and finally an RJW forced act.
    /// </summary>
    public static partial class HarassmentEngine
    {
        private const float TargetRadius = 14f;
        private const int VictimCooldownTicks = 7500;   // ~3 in-game hours
        private const int HarasserCooldownTicks = 5000;

        private static HarassmentSettings S => RimJobWorldSexualHarassmentMod.Settings;

        // ── Entry point from the map scan ─────────────────────────────────────
        public static bool TryRunOnMap(Map map)
        {
            if (map?.mapPawns == null) return false;

            var pool = new List<Pawn>();
            var weights = new List<float>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (!CanHarass(p, out var profile)) continue;
                float w = HarasserWillingness(p, profile);
                if (w <= 0.05f) continue;
                pool.Add(p);
                weights.Add(w);
            }
            if (pool.Count == 0) return false;

            // Weighted pick of one harasser this scan.
            Pawn harasser = WeightedPick(pool, weights);
            if (harasser == null) return false;

            Pawn target = FindTarget(harasser);
            if (target == null) return false;

            RunHarassment(harasser, target);
            return true;
        }

        // ── Eligibility ───────────────────────────────────────────────────────
        public static bool CanHarass(Pawn p, out PawnProfile profile)
        {
            profile = null;
            if (p == null || !p.Spawned || p.Dead) return false;
            if (!p.RaceProps.Humanlike) return false;
            if (!xxx.is_human(p)) return false;
            if (p.Downed || !p.Awake()) return false;
            if (p.InMentalState) return false;
            if (p.Drafted) return false; // player took manual control; don't auto-harass
            if (p.ageTracker == null || !p.ageTracker.Adult) return false;
            int hAge = p.ageTracker.AgeBiologicalYears;
            if (hAge < S.harasserMinAge || hAge > S.harasserMaxAge) return false; // harasser age-range gate
            if (IsBusyInAct(p)) return false; // already mid-sex/animation

            // Gender gating
            if (xxx.is_female(p) && !S.allowFemaleHarassers) return false;
            if (xxx.is_male(p) && !S.allowMaleHarassers) return false;

            // Must be a valid harasser category for at least one allowed pair.
            var cat = Categorize(p);
            if (!CategoryHarassesAnything(cat)) return false;

            profile = GameComponent_Harassment.Instance?.GetProfile(p);
            if (profile == null) return false;
            return true;
        }

        private static bool CategoryHarassesAnything(PawnCategory harasser)
        {
            foreach (PawnCategory t in System.Enum.GetValues(typeof(PawnCategory)))
                if (HarassmentAllowed(harasser, t)) return true;
            return false;
        }

        /// <summary>Matrix permission, plus the victim-aggressor allowance: when enabled, only VISITORS and
        /// RAIDERS (not slaves/prisoners) may turn on the colonists. Raiders only reach downed colonists (the
        /// hostile-target filter still applies to standing pawns).</summary>
        private static bool HarassmentAllowed(PawnCategory h, PawnCategory t)
        {
            if (S.IsPairAllowed(h, t)) return true;
            return S.allowVictimAggressors && t == PawnCategory.Colonist
                && (h == PawnCategory.Visitor || h == PawnCategory.Other);
        }

        // Willingness doubles as the selection weight.
        private static float HarasserWillingness(Pawn p, PawnProfile profile)
        {
            // Cooldown
            if (Find.TickManager.TicksGame - profile.lastHarasserTick < HarasserCooldownTicks) return 0f;

            float w;
            switch (profile.morality)
            {
                case Morality.Evil: w = 1.0f; break;
                case Morality.Questionable: w = 0.4f; break;
                default: w = 0.05f; break;
            }

            w *= Mathf01(0.3f + profile.confidence / 100f);

            // Trait modifiers
            if (xxx.is_rapist(p)) w *= 3.0f;
            if (xxx.is_nympho(p)) w *= 1.8f;
            if (xxx.is_lecher(p)) w *= 1.6f;
            if (xxx.is_psychopath(p)) w *= 1.5f;
            if (xxx.is_kind(p)) w *= 0.3f;
            if (xxx.is_prude(p)) w *= 0.3f;
            if (xxx.is_ascetic(p)) w *= 0.5f;

            // Sex drive
            float drive = SafeSexDrive(p);
            w *= Mathf01(0.4f + drive * 0.6f);

            // Traits (Psychology Volatile etc.), ideology (domination/hedonist faiths), Biotech predator gene,
            // and Rimpsyche aggressiveness disposition.
            w *= TraitHooks.HarasserTraitFactor(p);
            w *= IdeologyHooks.HarasserPreceptFactor(p);
            if (GeneHelper.IsPredator(p)) w *= 2.2f;
            w *= 1f + RimpsycheBridge.Aggressiveness(p) * 0.5f;   // -1..1 -> x0.5 .. x1.5 (no-op without Rimpsyche)

            return w;
        }

        // ── Target selection ──────────────────────────────────────────────────
        public static Pawn FindTarget(Pawn harasser)
        {
            var harasserCat = Categorize(harasser);
            Map map = harasser.Map;
            if (map == null) return null;

            var pool = new List<Pawn>();
            var weights = new List<float>();

            foreach (var t in map.mapPawns.AllPawnsSpawned)
            {
                if (t == harasser) continue;
                if (!t.RaceProps.Humanlike || !xxx.is_human(t)) continue;
                if (t.Dead) continue;
                if (t.ageTracker == null || !t.ageTracker.Adult) continue;
                int vAge = t.ageTracker.AgeBiologicalYears;
                if (vAge < S.victimMinAge || vAge > S.victimMaxAge) continue; // victim age-range gate
                if (IsInOnaholeBed(t)) continue; // already captured in an onahole
                bool tDowned = t.Downed;
                // Standing enemies are out of scope, but a downed enemy is fair game for opportunistic rape.
                if (t.HostileTo(harasser) && !tDowned) continue;
                if (IsBusyInAct(t)) continue; // don't interrupt an ongoing act/animation
                if (harasser.Position.DistanceTo(t.Position) > TargetRadius) continue;
                // The who-harasses-whom matrix governs colony social targets; downed enemies bypass it.
                if (!t.HostileTo(harasser) && !HarassmentAllowed(harasserCat, Categorize(t))) continue;
                if (!xxx.can_do_loving(t) && !tDowned) continue; // must be a plausible victim
                if (!IsAttracted(harasser, t)) continue;

                var tp = GameComponent_Harassment.Instance?.GetProfile(t);
                if (tp == null) continue;
                if (Find.TickManager.TicksGame - tp.lastVictimTick < VictimCooldownTicks) continue;

                // Composite metric already folds in RJW vulnerability, BDSM gear, restraints, downed/asleep,
                // pushover reputation, conditioning, and injury. More locked gear -> much likelier target.
                float w = 1f + VulnerabilityScore(t) * 2.5f;

                // Age Gap Attraction (soft): inclined harassers prefer younger adults; averse ones avoid big gaps.
                w *= AgeGapBridge.WeightFactor(harasser, t);

                pool.Add(t);
                weights.Add(w);
            }

            return pool.Count == 0 ? null : WeightedPick(pool, weights);
        }

        public static bool IsAttracted(Pawn harasser, Pawn target)
        {
            if (harasser == null || target == null) return true;
            try { if (RJWSettings.RPG_hero_control) return true; } catch { }

            bool sameSex = (xxx.is_male(harasser) && xxx.is_male(target)) ||
                           (xxx.is_female(harasser) && xxx.is_female(target));
            // ABSOLUTE opposite-sex override: never same-sex, regardless of orientation, respect-orientation,
            // or a downed enemy. This is the reliable lever when Rimpsyche classes most pawns as bisexual.
            if ((S.enforceOppositeSex || S.heterosexualOnly) && sameSex) return false;
            // Opportunistic rape of a DOWNED ENEMY otherwise ignores orientation.
            if (target.Downed && target.HostileTo(harasser)) return true;
            if (!S.respectOrientation) return true;

            try
            {
                if (xxx.is_asexual(harasser)) return false;
                // RJW's discrete orientation (synced from Rimpsyche): a heterosexual never goes same-sex, a
                // homosexual never opposite-sex; a bisexual/pansexual goes either way.
                if (xxx.is_bisexual(harasser) || xxx.is_pansexual(harasser)) return true;
                if (xxx.is_homosexual(harasser)) return sameSex;
                if (xxx.is_heterosexual(harasser)) return !sameSex;
                // Truly undetermined -> heteronormative default (opposite sex only).
                return !sameSex;
            }
            catch { return !sameSex; }
        }


        /// <summary>True when RJW hero mode or a wild/hippie global is on, all of which ignore orientation.</summary>
        public static bool HeroOrWildModeActive()
        {
            try { return RJWSettings.RPG_hero_control || RJWSettings.WildMode || RJWSettings.HippieMode; }
            catch { return false; }
        }

        /// <summary>Abstract harassment during caravan travel (no map to animate on): occasionally one caravan
        /// member forces themselves on another, applying the moodlets, karma, memory, and a letter.</summary>
        public static void TryCaravanHarassment()
        {
            if (S == null || !S.masterEnabled) return;
            var caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                var car = caravans[i];
                if (car == null || !car.IsPlayerControlled) continue;
                var members = car.PawnsListForReading;
                if (members == null) continue;
                var pool = new List<Pawn>();
                for (int m = 0; m < members.Count; m++)
                {
                    var pm = members[m];
                    if (pm != null && pm.RaceProps.Humanlike && !pm.Dead && pm.ageTracker != null && pm.ageTracker.Adult && !pm.IsPrisoner)
                        pool.Add(pm);
                }
                if (pool.Count < 2 || !Rand.Chance(0.5f)) continue;

                pool.Shuffle();
                Pawn harasser = null, victim = null;
                for (int h = 0; h < pool.Count && harasser == null; h++)
                {
                    var hh = pool[h];
                    var hp = GameComponent_Harassment.Instance?.GetProfile(hh);
                    if (hp == null || (hp.morality == Morality.Decent && !Rand.Chance(0.12f))) continue;
                    if (xxx.is_female(hh) && !S.allowFemaleHarassers) continue;
                    if (xxx.is_male(hh) && !S.allowMaleHarassers) continue;
                    for (int vv = 0; vv < pool.Count; vv++)
                    {
                        var cand = pool[vv];
                        if (cand == hh) continue;
                        bool sameSex = (xxx.is_male(hh) && xxx.is_male(cand)) || (xxx.is_female(hh) && xxx.is_female(cand));
                        if (S.enforceOppositeSex && sameSex) continue;
                        victim = cand; harasser = hh; break;
                    }
                }
                if (harasser == null || victim == null) continue;

                ApplyThought(victim, harasser, RJWSH_ThoughtDefOf.RJWSH_WasGroped);
                RememberHarasser(victim, harasser);
                KarmaBridge.AddKarma(harasser, -8f, "rjw_caravan_harassment");
                Find.LetterStack.ReceiveLetter("Harassment on the road",
                    harasser.LabelShortCap + " forced themselves on " + victim.LabelShort + " during the journey. Cramped together with nowhere to run, it was a miserable stretch of road for " + victim.LabelShort + ".",
                    LetterDefOf.NegativeEvent, new LookTargets(car));
                return; // at most one caravan event per tick
            }
        }

        // ── Event flow ────────────────────────────────────────────────────────
        // Starts the approach job. The harasser walks to the victim; verbal, escalation, and the
        // optional carry-to-private happen inside JobDriver_Harass, which then hands the physical
        // stage back via the deferred queue (BeginPhysicalOrForced).
        public static void RunHarassment(Pawn harasser, Pawn target)
        {
            if (harasser?.jobs == null || target == null || !harasser.Spawned) return;

            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            var tp = GameComponent_Harassment.Instance.GetProfile(target);
            int now = Find.TickManager.TicksGame;
            hp.lastHarasserTick = now;
            tp.lastVictimTick = now;

            var approach = SelectApproach(harasser, target, hp);
            MapComponent_HarassmentScan.For(harasser.Map)?.RecordApproach(approach, now, S.ApproachCooldown(approach));
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Harass, target);
            job.count = (int)approach;
            harasser.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>Starts a harassment event with a specific approach (debug / scripted).</summary>
        /// <summary>Player-directed harassment (RJW hero control): order a colonist to walk over and run the
        /// chosen approach on a target, routed through the normal paced JobDriver_Harass.</summary>
        public static void StartDirectedHarass(Pawn actor, Pawn target, ApproachType type)
        {
            if (actor?.jobs == null || target == null || !actor.Spawned) return;
            var hp = GameComponent_Harassment.Instance?.GetProfileIfExists(actor);
            var tp = GameComponent_Harassment.Instance?.GetProfileIfExists(target);
            int now = Find.TickManager.TicksGame;
            if (hp != null) hp.lastHarasserTick = now;
            if (tp != null) tp.lastVictimTick = now;
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Harass, target);
            job.count = (int)type;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static void RunHarassmentApproach(Pawn harasser, Pawn target, ApproachType type)
        {
            if (harasser?.jobs == null || target == null || !harasser.Spawned) return;
            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            var tp = GameComponent_Harassment.Instance.GetProfile(target);
            int now = Find.TickManager.TicksGame;
            hp.lastHarasserTick = now;
            tp.lastVictimTick = now;
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Harass, target);
            job.count = (int)type;
            harasser.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>Picks the approach archetype for this event, weighted by morality, context, settings.</summary>
        public static ApproachType SelectApproach(Pawn harasser, Pawn target, PawnProfile hp)
        {
            bool bound = IsInBondage(target);
            bool vuln = bound || target.Downed || !target.Awake() || VulnerabilityScore(target) > 0.8f;
            float decent = hp.morality == Morality.Decent ? 1f : 0f;
            float quest = hp.morality == Morality.Questionable ? 1f : 0f;
            float evil = hp.morality == Morality.Evil ? 1f : 0f;

            var types = new List<ApproachType>();
            var weights = new List<float>();
            var mc = MapComponent_HarassmentScan.For(harasser?.Map);
            int nowTick = Find.TickManager.TicksGame;
            void Add(ApproachType t, float w) { if (w > 0f && (mc == null || mc.ApproachReady(t, nowTick))) { types.Add(t); weights.Add(w); } }

            if (S.enableCatcall) Add(ApproachType.Catcall, decent * 1.0f + quest * 0.6f + evil * 0.3f);
            if (S.enableFlirt) Add(ApproachType.Flirt, decent * 0.7f + quest * 0.7f + evil * 0.4f);
            if (S.enableProposition) Add(ApproachType.Proposition,
                (decent * 0.05f + quest * 0.5f + evil * 1.2f) * (vuln ? 1.8f : 1f) * (bound ? 1.6f : 1f));
            if (S.enableDeviousDevice && bound) Add(ApproachType.DeviousDevice, decent * 0.8f + quest * 0.7f + evil * 0.9f);
            if (S.enableSpikedDrink) Add(ApproachType.SpikedDrink, decent * 0.2f + quest * 0.45f + evil * 0.7f);
            if (S.enableHypnosis) Add(ApproachType.Hypnosis, quest * 0.4f + evil * 0.6f);
            if (S.enableBlackmail && HasPhotoOf(target))
            {
                float bm = quest * 0.5f + evil * 0.9f;
                if (HarasserCarriesPhotoOf(harasser, target)) bm += 2.5f; // they're holding the leverage - press it again
                Add(ApproachType.Blackmail, bm);
            }

            return types.Count == 0 ? ApproachType.Catcall : WeightedPick(types, weights);
        }

        /// <summary>Runs the chosen approach on arrival. Returns true to escalate into carry + physical.</summary>
        public static bool ResolveApproachOnArrival(Pawn harasser, Pawn target, ApproachType approach)
        {
            AnnounceApproach(harasser, target, approach);
            switch (approach)
            {
                case ApproachType.Catcall:
                    FireVerbal(harasser, target, RJWSH_InteractionDefOf.RJWSH_Catcall, -2f, ApproachType.Catcall);
                    return false;
                case ApproachType.Flirt:
                    return DoFlirt(harasser, target);
                case ApproachType.SpikedDrink:
                    return DoFan(harasser, target);
                case ApproachType.Hypnosis:
                    return DoHypnosis(harasser, target);
                case ApproachType.Blackmail:
                    return DoBlackmail(harasser, target);
                case ApproachType.DeviousDevice:
                    return DoDeviousDevice(harasser, target);
                case ApproachType.Grope:
                case ApproachType.Forced:
                    // Player-directed physical (no verbal stage): escalate straight to the physical/forced act.
                    return true;
                default: // Proposition
                {
                    bool fired = FireVerbal(harasser, target, RJWSH_InteractionDefOf.RJWSH_Proposition, -3f, ApproachType.Proposition);
                    return DecideEscalation(harasser, target, fired);
                }
            }
        }

        private static bool FireVerbal(Pawn harasser, Pawn target, InteractionDef def, float karma, ApproachType type)
        {
            bool vulnerable = target.Downed || !target.Awake();
            bool fired = def != null && !vulnerable && FireInteraction(harasser, target, def);
            if (fired)
            {
                if (karma != 0f) KarmaBridge.AddKarma(harasser, karma, "rjw_harassment_verbal");
                RimTalkBridge.NotifyHarassment(harasser, target, type);
                if (S.multiLineHarassment) ScheduleApproachExchange(harasser, target, type);
            }
            return fired;
        }

        // ── Flirt: consensual if willing, otherwise a coercive harasser may force it ───────────
        private static bool DoFlirt(Pawn harasser, Pawn target)
        {
            FireInteraction(harasser, target, RJWSH_InteractionDefOf.RJWSH_Flirt);
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Flirt);

            if (TargetWillingForFlirt(harasser, target))
            {
                // Consensual: let the flirty banter play out first, THEN hand off to RJW casual sex, so the
                // scene does not jump straight to sex. No karma penalty.
                if (S.multiLineHarassment)
                {
                    ScheduleApproachExchange(harasser, target, ApproachType.Flirt);
                    MapComponent_HarassmentScan.ScheduleAct(harasser, target, 2, ExchangeDuration());
                }
                else MapComponent_HarassmentScan.EnqueueCasualSex(harasser, target);
                return false;
            }

            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            bool pushy = hp.morality == Morality.Evil || (hp.morality == Morality.Questionable && hp.confidence > 60f);
            if (pushy && S.allowEscalation && (S.enableGrope || S.enableForced))
            {
                if (S.multiLineHarassment) ScheduleApproachExchange(harasser, target, ApproachType.Flirt);
                KarmaBridge.AddKarma(harasser, -3f, "rjw_harassment_coercive_flirt");
                return true;
            }
            return false; // backed off gracefully
        }

        private static bool TargetWillingForFlirt(Pawn harasser, Pawn target)
        {
            try
            {
                if (!xxx.can_do_loving(target)) return false;
                if (xxx.is_asexual(target) || xxx.is_prude(target)) return false;
                if (!IsAttracted(target, harasser)) return false; // target must fancy the harasser
                float w = 0.25f;
                w += (target.relations?.OpinionOf(harasser) ?? 0) / 200f;   // -0.5 .. +0.5
                w += SafeSexDrive(target) * 0.3f;
                if (xxx.is_nympho(target) || xxx.is_lecher(target)) w += 0.3f;
                if (xxx.is_whore(target)) w += 0.2f;
                return Rand.Chance(Mathf01(w));
            }
            catch { return false; }
        }

        // ── Fan: a drink offer; decent pawns give a treat, others may spike it ─────────────────
        private static bool DoFan(Pawn harasser, Pawn target)
        {
            FireInteraction(harasser, target, RJWSH_InteractionDefOf.RJWSH_Fan);
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.SpikedDrink);

            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            bool spike = hp.morality != Morality.Decent && Rand.Chance(S.fanSpikeChance);
            if (hp.morality == Morality.Evil && Rand.Chance(0.3f)) spike = true;

            if (spike && S.allowEscalation && (S.enableGrope || S.enableForced))
            {
                // Spiked: knock the target out, then escalate into the forced flow (downed -> auto-submit).
                try { HealthUtility.TryAnesthetize(target); } catch { }
                KarmaBridge.AddKarma(harasser, -6f, "rjw_harassment_spiked_drink");
                return true;
            }

            // Genuine friendly treat.
            ApplyThought(target, harasser, RJWSH_ThoughtDefOf.RJWSH_ReceivedTreat);
            return false;
        }

        // ── Hypnosis: conditioning loop; conditioned pawns become compliant + commandable ───────
        private static bool DoHypnosis(Pawn harasser, Pawn target)
        {
            FireInteraction(harasser, target, RJWSH_InteractionDefOf.RJWSH_Hypnosis);
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Hypnosis);

            var tp = GameComponent_Harassment.Instance.GetProfile(target);
            // The harasser is talking the target INTO a session - it can be refused.
            float convince = S.hypnosisBaseChance;
            try { convince += (target.GetStatValue(StatDefOf.PsychicSensitivity) - 1f) * 0.3f; } catch { }
            convince += tp.hypnosisLevel / 100f * 0.5f;          // prior conditioning makes them easier each time
            convince += (1f - SafeMood(target)) * 0.2f;          // low mood = more suggestible
            if (xxx.is_prude(target)) convince -= 0.15f;
            try { convince += ((harasser.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) - 5) / 40f; } catch { } // smooth talkers
            convince -= PsyResist.HypnosisResist(target);        // Royalty: psychic discipline resists conditioning
            bool success = Rand.Chance(Mathf01(convince));

            // Play it out as a persuasion: the harasser coaxes, the target wavers, then yields or snaps out of it.
            if (S.multiLineHarassment) ScheduleHypnosisExchange(harasser, target, success);

            // Each session deepens both the visible trance and the lasting conditioning. A success drives it
            // hard; a refusal still plants a seed (so persistence always pays off). The collar locks at the top.
            float gainFactor = GeneHelper.ConditioningGainFactor(target); // Biotech compliant/willful genes
            gainFactor *= 1f - RimpsycheBridge.WillStrength(target) * 0.4f; // Rimpsyche: tenacious pawns resist conditioning
            float _c0 = tp.hypnosisLevel;
            if (success)
                tp.hypnosisLevel = System.Math.Min(100f, tp.hypnosisLevel + Rand.Range(18f, 28f) * gainFactor);
            else
            {
                float seed = Rand.Range(7f, 11f) * gainFactor;
                if (tp.hypnosisLevel + seed <= 88f) tp.hypnosisLevel += seed;
                else if (tp.hypnosisLevel < 88f) tp.hypnosisLevel = 88f; // refusals approach but only a success crosses to the collar tier
            }
            tp.LogCondEvent(success ? "Conditioning session" : "Resisted conditioning", tp.hypnosisLevel - _c0, 0f);
            ApplyHypnotizedHediff(target); // severity now tracks conditioning -> the trance visibly deepens
            if (tp.hypnosisLevel >= 90f) LockControlCollar(target, harasser); // top tier -> collar (no-ops if already collared)

            if (success)
            {
                if (InvolvesPlayerPawn(harasser, target))
                    Messages.Message(target.LabelShort + " sinks deeper under " + harasser.LabelShort + "'s spell (conditioning " + (int)tp.hypnosisLevel + ").",
                        new LookTargets(target), MessageTypeDefOf.NegativeEvent, false);
                var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
                if (tp.IsConditioned && hp.morality == Morality.Evil)
                    return true; // compliant -> BeginPhysical auto-submit
                return false;
            }

            // Refused on the surface - but each session still plants a seed and deepens the trance a little.
            ApplyThought(target, harasser, RJWSH_ThoughtDefOf.RJWSH_WasHarassed);
            var hpr = GameComponent_Harassment.Instance.GetProfile(harasser);
            hpr.confidence = System.Math.Max(0f, hpr.confidence - 4f);
            if (InvolvesPlayerPawn(harasser, target))
                Messages.Message(target.LabelShort + " resisted, but the suggestion lingers (conditioning " + (int)tp.hypnosisLevel + ").",
                    new LookTargets(target), MessageTypeDefOf.NeutralEvent, false);
            return false;
        }

        private static void ScheduleHypnosisExchange(Pawn harasser, Pawn target, bool success)
        {
            const int d = 130;
            MapComponent_HarassmentScan.ScheduleLine(target, harasser, RJWSH_InteractionDefOf.RJWSH_HypnosisDoubt, d);
            MapComponent_HarassmentScan.ScheduleLine(harasser, target, RJWSH_InteractionDefOf.RJWSH_Hypnosis, d * 2);
            MapComponent_HarassmentScan.ScheduleLine(target, harasser, RJWSH_InteractionDefOf.RJWSH_HypnosisDoubt, d * 3);
            MapComponent_HarassmentScan.ScheduleLine(harasser, target, RJWSH_InteractionDefOf.RJWSH_Hypnosis, d * 4);
            MapComponent_HarassmentScan.ScheduleLine(target, harasser,
                success ? RJWSH_InteractionDefOf.RJWSH_HypnosisYield : RJWSH_InteractionDefOf.RJWSH_HypnosisRefuse, d * 5);
        }

        private static void ApplyHypnotizedHediff(Pawn p)
        {
            try
            {
                if (p?.health == null) return;
                var h = p.health.hediffSet.GetFirstHediffOfDef(RJWSH_HediffDefOf.RJWSH_Hypnotized);
                if (h == null)
                {
                    p.health.AddHediff(RJWSH_HediffDefOf.RJWSH_Hypnotized);
                    h = p.health.hediffSet.GetFirstHediffOfDef(RJWSH_HediffDefOf.RJWSH_Hypnotized);
                }
                if (h == null) return;
                // Severity tracks the conditioning level, so repeated hypnosis visibly deepens the trance
                // (suggestible -> entranced -> deeply entranced -> mind broken).
                var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
                float level = vp != null ? vp.hypnosisLevel : 50f;
                h.Severity = UnityEngine.Mathf.Clamp(level / 100f, 0.05f, 1f);
                var comp = h.TryGetComp<HediffComp_Disappears>();
                if (comp != null) comp.ticksToDisappear = Rand.Range(30000, 60000);
            }
            catch { }
        }

        public static bool HasHypnotizedHediff(Pawn p) =>
            p?.health?.hediffSet?.HasHediff(RJWSH_HediffDefOf.RJWSH_Hypnotized) ?? false;

        private static float SafeMood(Pawn p) => p?.needs?.mood?.CurLevelPercentage ?? 0.5f;

        // ── Control collar (final hypnosis tier) ──────────────────────────────
        public static bool WearingControlCollar(Pawn p)
        {
            if (p?.apparel == null) return false;
            var worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].def == RJWSH_ThingDefOf.RJWSH_ControlCollar) return true;
            return false;
        }

        // ── Phase 4: furniture / session compat ──
        private static ThingDef[] _slaveryCollarDefs;
        /// <summary>True if the pawn wears a Simple Slavery Collars collar (any tier). Recognized for CONDITIONING
        /// only - the key-holder control suite still needs our own Holokey-locked control collar.</summary>
        public static bool WearingSlaveryCollar(Pawn p)
        {
            if (!SoftDeps.SimpleSlaveryCollarsActive || p?.apparel == null) return false;
            if (_slaveryCollarDefs == null)
            {
                var names = new[] { "Apparel_SlaveCollar_Explosive", "Apparel_SlaveCollar_Electric", "Apparel_SlaveCollar_Crypto", "Apparel_SlaveCollar_Heavy", "Apparel_SlaveCollar_Tribal", "SlaveCollar" };
                var list = new System.Collections.Generic.List<ThingDef>();
                foreach (var n in names) { var d = DefDatabase<ThingDef>.GetNamedSilentFail(n); if (d != null) list.Add(d); }
                _slaveryCollarDefs = list.ToArray();
            }
            var worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
                for (int j = 0; j < _slaveryCollarDefs.Length; j++)
                    if (worn[i].def == _slaveryCollarDefs[j]) return true;
            return false;
        }

        /// <summary>Collared for conditioning purposes: our control collar OR a Simple Slavery Collars collar.</summary>
        public static bool IsCollared(Pawn p) => WearingControlCollar(p) || WearingSlaveryCollar(p);

        private static HediffDef _bondageBedHediff; private static bool _bbTried;
        /// <summary>BondageBed Torture: a pet strapped helpless in a bondage bed has its conditioning deepened and
        /// its will ground toward submission while bound. No-op without the mod / hediff.</summary>
        public static void BondageBedTick(Pawn p)
        {
            if (!SoftDeps.BondageBedActive || p?.health == null) return;
            if (!_bbTried) { _bbTried = true; _bondageBedHediff = DefDatabase<HediffDef>.GetNamedSilentFail("SR_Hediff_BondageBed"); }
            if (_bondageBedHediff == null || !p.health.hediffSet.HasHediff(_bondageBedHediff)) return;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof == null) return;
            prof.ApplyCond("Bound in a bondage bed", 0.5f, -0.2f);
            if (prof.sex != null && prof.sex.seeded) AttrDelta(p, subdom: -0.5f, trauma: 0.3f);
        }

        /// <summary>True only if this key unlocks the pawn's CONTROL COLLAR. A key for any other locked BDSM
        /// device (cuffs, gag, plug, etc.) does NOT grant the control suite - only the collar does.</summary>
        public static bool KeyMatchesControlCollar(rjw.CompHoloCryptoStamped keyComp, Pawn v)
        {
            if (keyComp == null || v?.apparel == null) return false;
            var worn = v.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def != RJWSH_ThingDefOf.RJWSH_ControlCollar) continue;
                var gc = worn[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                if (gc != null && keyComp.matches(gc)) return true;
            }
            return false;
        }

        /// <summary>Any sex act involving a control-collared pawn is experienced as forced: the collared
        /// participant gets RJW's rape mood debuff. Skips the case where RJW already counted them as the
        /// rapee (a genuine rape where they are the recipient), so it never double-applies.</summary>
        public static void ApplyCollarForcedDebuff(rjw.SexProps props)
        {
            if (props == null) return;
            rjw.xxx.rjwSextype st = rjw.xxx.rjwSextype.None;
            try { st = props.sexType; } catch { }
            if (st == rjw.xxx.rjwSextype.Masturbation) return;

            bool rape = false; try { rape = props.isRape; } catch { }
            Pawn a = props.initiator, b = props.recipient; // b (recipient) is RJW's rapee when isRape

            if (a != null && WearingControlCollar(a) && !(rape && a == b))
                GiveForcedRapeThought(a, b, st);
            if (b != null && b != a && WearingControlCollar(b) && !rape)
                GiveForcedRapeThought(b, a, st);
        }

        private static void GiveForcedRapeThought(Pawn victim, Pawn other, rjw.xxx.rjwSextype st)
        {
            var mem = victim?.needs?.mood?.thoughts?.memories;
            if (mem == null) return;
            bool maso = false; try { maso = xxx.is_masochist(victim); } catch { }
            bool anal = st == rjw.xxx.rjwSextype.Anal;
            ThoughtDef t = maso ? (anal ? xxx.masochist_got_anal_raped : xxx.masochist_got_raped)
                                : (anal ? xxx.got_anal_raped : xxx.got_raped);
            try { mem.TryGainMemory(t, other); } catch { }
        }

        public static void LockControlCollar(Pawn victim, Pawn controller)
        {
            if (victim?.apparel == null || victim.Dead || !victim.RaceProps.Humanlike) return;
            if (WearingControlCollar(victim)) return;
            if (!ApparelUtility.HasPartsToWear(victim, RJWSH_ThingDefOf.RJWSH_ControlCollar)) return;
            try
            {
                var collar = (Apparel)ThingMaker.MakeThing(RJWSH_ThingDefOf.RJWSH_ControlCollar);
                victim.apparel.Wear(collar, false, true); // RJW Wear patch locks it + spawns the Holokey
                PlaySoundClip("FlickSwitch", victim); // a satisfying mechanical lock click
                ConsolidateVictimKey(victim, collar, controller, true); // controller keeps the ONE key (dedup)
                if (controller != null)
                {
                    var ovp = GameComponent_Harassment.Instance?.GetProfile(victim);
                    if (ovp != null) ovp.ownerId = controller.thingIDNumber;
                    EnsureOwnerRelation(controller, victim);
                }
                ApplyConditioningHediff(victim);            // begin the conditioning clock
                DepthNotifyLoversOnCollar(victim);          // romance corruption: the pet's lover(s) feel the sting
                if (controller != null)
                {
                    TaleHelper.Record("RJWSH_Tale_Collared", controller, victim); // art can depict this day
                    Chronicle(victim, "Collared by " + controller.LabelShortCap + ".", 1);
                    Chronicle(controller, "Collared " + victim.LabelShortCap + ".", 0);
                }
                var lp = GameComponent_Harassment.Instance?.GetProfile(victim);
                if (lp != null && lp.latentHypnosis * 0.5f > lp.hypnosisLevel)
                    lp.hypnosisLevel = System.Math.Min(88f, lp.latentHypnosis * 0.5f); // re-collaring reawakens old conditioning
                // Royalty: a titled pawn collared loses face - their patron faction is displeased and they are humiliated.
                try
                {
                    if (victim.royalty != null && victim.royalty.AllTitlesForReading != null)
                    {
                        bool titled = false;
                        var titles = victim.royalty.AllTitlesForReading;
                        for (int ti = 0; ti < titles.Count; ti++)
                            if (titles[ti]?.faction != null && !titles[ti].faction.IsPlayer)
                            { titles[ti].faction.TryAffectGoodwillWith(Faction.OfPlayer, -5, false, false, null, null); titled = true; }
                        if (titled) TryAddMoodThought(victim, "RJWSH_Humiliated");
                    }
                }
                catch { }
                // Ideology: a harem-leader's ideological authority breaks resistance faster on collaring.
                if (controller != null && lp != null && IdeologyHooks.HasHaremMeme(controller))
                    lp.hypnosisLevel = System.Math.Min(100f, lp.hypnosisLevel + 15f);
                if (InvolvesPlayerPawn(controller, victim))
                    Messages.Message((controller != null ? controller.LabelShort : "Someone") + " locked a control collar onto " + victim.LabelShort + ".",
                        new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            }
            catch (System.Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] collar lock failed: " + ex.Message, 0x5A1330); }
        }

        // ── Attribute visibility: Health-tab hediffs + permanent marks + Submission need sync ──
        private static HediffDef _hdTrauma, _hdAddiction, _hdBrand, _hdPiercing;
        private static HediffDef HD(ref HediffDef cache, string defName) => cache ?? (cache = DefDatabase<HediffDef>.GetNamedSilentFail(defName));

        /// <summary>Surfaces a tracked pet's deep attributes in the Health tab (trauma + addiction mirror the
        /// SexAttributes floats), applies permanent ownership marks once subDom crosses thresholds (opt-in), and
        /// keeps the Submission need's presence in sync. Runs ~hourly for seeded pets.</summary>
        public static void SyncAttributeHediffs(Pawn p, PawnProfile prof)
        {
            if (p?.health == null || prof?.sex == null || !prof.sex.seeded || !p.RaceProps.Humanlike) return;
            var sx = prof.sex;

            SyncMirrorHediff(p, HD(ref _hdTrauma, "RJWSH_SexualTrauma"), sx.trauma, 20f);
            SyncMirrorHediff(p, HD(ref _hdAddiction, "RJWSH_SexAddiction"), sx.sexAddiction, 25f);

            // Permanent ownership marks tied to how submissive the pet has become (opt-in; never removed).
            if (S.conditioningMarksBody && (prof.ownerId >= 0 || prof.relationshipOwnerId >= 0 || prof.IsConditioned))
            {
                if (sx.subDom <= -30f) EnsurePermanentMark(p, HD(ref _hdPiercing, "RJWSH_OwnerPiercing"));
                if (sx.subDom <= -60f) EnsurePermanentMark(p, HD(ref _hdBrand, "RJWSH_OwnershipBrand"));
            }

            // Keep the Submission need's presence matched to the pet's state (reconcile only on mismatch).
            bool shouldHave = S.enableSubmissionNeed && (prof.IsConditioned || prof.ownerId >= 0 || prof.relationshipOwnerId >= 0);
            if (shouldHave != (Need_Submission.For(p) != null)) p.needs?.AddOrRemoveNeedsAsAppropriate();
        }

        private static void SyncMirrorHediff(Pawn p, HediffDef def, float value0to100, float onThreshold)
        {
            if (def == null) return;
            var h = p.health.hediffSet.GetFirstHediffOfDef(def);
            if (value0to100 >= onThreshold)
            {
                if (h == null)
                {
                    h = HediffMaker.MakeHediff(def, p);
                    h.Severity = value0to100 / 100f;
                    p.health.AddHediff(h);
                }
                else h.Severity = value0to100 / 100f;
            }
            else if (h != null) p.health.RemoveHediff(h);
        }

        private static void EnsurePermanentMark(Pawn p, HediffDef def)
        {
            if (def == null || p.health.hediffSet.HasHediff(def)) return;
            p.health.AddHediff(def);
            if (PawnUtility.ShouldSendNotificationAbout(p))
                Messages.Message(p.LabelShortCap + " has been permanently marked: " + def.label + ".",
                    new LookTargets(p), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>Raises a pet's Submission need - serving, being rewarded, and being disciplined settle it.
        /// No-op when the need isn't present.</summary>
        public static void SatisfySubmission(Pawn p, float amount)
        {
            var need = Need_Submission.For(p);
            if (need != null) need.CurLevel += amount;   // Need.CurLevel setter clamps 0..1
        }

        // ── Service quotas + head-girl pecking (Tranche D) ──
        public static void RollQuotaDay(PawnProfile prof, Pawn p)
        {
            if (prof == null || p == null) return;
            int day = GenLocalDate.DayOfYear(p);
            if (prof.quotaDay != day) { prof.quotaDay = day; prof.servicesToday = 0; }
        }

        /// <summary>Counts one service / whoring session toward the pet's daily quota.</summary>
        public static void NoteServiceRendered(Pawn p)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof == null) return;
            RollQuotaDay(prof, p);
            prof.servicesToday++;
        }

        /// <summary>Marks one pet as the sole head girl, clearing the flag on every other pet.</summary>
        public static void SetSoleHeadGirl(Pawn keep)
        {
            var gc = GameComponent_Harassment.Instance; if (gc == null) return;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.AllPawns;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (pawns[i] == keep) continue;
                    var prof = gc.GetProfileIfExists(pawns[i]);
                    if (prof != null && prof.isHeadGirl) prof.isHeadGirl = false;
                }
            }
        }

        /// <summary>How well a pet is serving its owner - drives the auto head-girl pick. Rewards conditioning,
        /// trust (rapport), a dominant streak (leads the others), services rendered today, and lifetime earnings.</summary>
        public static float HeadGirlScore(PawnProfile prof, Pawn p)
        {
            if (prof == null) return 0f;
            float score = prof.hypnosisLevel + prof.rapport * 0.8f;
            if (prof.sex != null && prof.sex.seeded && prof.sex.subDom > 0f) score += prof.sex.subDom * 0.4f;
            score += prof.servicesToday * 4f;
            score += prof.lifetimeEarnings * 0.02f;
            return score;
        }

        /// <summary>Auto head girl: for each owner, the best-performing pet (HeadGirlScore) becomes head girl and
        /// the rest are cleared. Dynamic - re-picks as performance shifts. Only runs when S.autoHeadGirl is on.</summary>
        public static void RecomputeHeadGirls(Map map)
        {
            if (map == null || S == null || !S.enableHeadGirl || !S.autoHeadGirl) return;
            var gc = GameComponent_Harassment.Instance; if (gc == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            var bestScore = new System.Collections.Generic.Dictionary<int, float>();
            var bestPet = new System.Collections.Generic.Dictionary<int, Pawn>();
            var tracked = new System.Collections.Generic.List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i]; var prof = gc.GetProfileIfExists(p);
                if (prof == null || p.Dead) continue;
                int oid = prof.ownerId >= 0 ? prof.ownerId : prof.relationshipOwnerId;
                if (!(oid >= 0 || WearingControlCollar(p))) continue;
                tracked.Add(p);
                float score = HeadGirlScore(prof, p);
                if (!bestScore.TryGetValue(oid, out float b) || score > b) { bestScore[oid] = score; bestPet[oid] = p; }
            }
            for (int i = 0; i < tracked.Count; i++)
            {
                var p = tracked[i]; var prof = gc.GetProfileIfExists(p); if (prof == null) continue;
                int oid = prof.ownerId >= 0 ? prof.ownerId : prof.relationshipOwnerId;
                prof.isHeadGirl = bestPet.TryGetValue(oid, out var bp) && bp == p && bestScore[oid] > 0f;
            }
        }

        /// <summary>The head girl enforces the pecking order: periodically she disciplines the worst-behaving
        /// (low-rapport or below-quota) other pet within reach. Needs a reasonably conditioned head girl.</summary>
        public static void HeadGirlTick(Map map)
        {
            if (map == null || S == null || !S.enableHeadGirl) return;
            var gc = GameComponent_Harassment.Instance; if (gc == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            Pawn hg = null; PawnProfile hgp = null;
            for (int i = 0; i < pawns.Count; i++)
            {
                var prof = gc.GetProfileIfExists(pawns[i]);
                if (prof != null && prof.isHeadGirl) { hg = pawns[i]; hgp = prof; break; }
            }
            if (hg == null || hgp == null || hg.Downed || !hg.Awake() || IsBusyInAct(hg) || hgp.hypnosisLevel < 50f) return;
            int now = Find.TickManager.TicksGame;
            if (now < hgp.tendCooldownTick) return;

            Pawn target = null; float worst = 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i]; if (c == hg || c.Downed) continue;
                var cp = gc.GetProfileIfExists(c); if (cp == null) continue;
                bool isPet = cp.ownerId >= 0 || cp.relationshipOwnerId >= 0 || WearingControlCollar(c);
                if (!isPet || IsBusyInAct(c) || !c.Awake()) continue;
                float misbehave = 0f;
                if (cp.rapport < 40f) misbehave += 40f - cp.rapport;
                RollQuotaDay(cp, c);
                if (cp.dailyQuota > 0 && cp.servicesToday < cp.dailyQuota) misbehave += (cp.dailyQuota - cp.servicesToday) * 5f;
                if (misbehave > worst && hg.Position.DistanceTo(c.Position) < 26f && hg.CanReach(c, PathEndMode.Touch, Danger.Deadly))
                { worst = misbehave; target = c; }
            }
            if (target != null && worst > 10f && Rand.Chance(0.5f))
            {
                StartDiscipline(hg, target);
                hgp.tendCooldownTick = now + Rand.Range(15000, 30000);
                if (InvolvesPlayerPawn(hg, target))
                    Messages.Message(hg.LabelShort + " disciplined " + target.LabelShort + " for stepping out of line.",
                        new LookTargets(hg, target), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // ── Rapist may brand their victim with a degrading tattoo ──────────────────────
        private static List<TattooDef> _faceTatPool, _bodyTatPool;

        private static List<TattooDef> BuildTatPool(TattooType type)
        {
            var all = DefDatabase<TattooDef>.AllDefsListForReading;
            var custom = new List<TattooDef>();
            var any = new List<TattooDef>();
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.tattooType != type || t.noGraphic) continue; // noGraphic excludes the NoTattoo_* defs
                any.Add(t);
                if (t.defName.StartsWith("RJWSH_")) custom.Add(t); // our own degradation marks take priority
            }
            return custom.Count > 0 ? custom : any;
        }

        private static List<TattooDef> TatPool(TattooType type) =>
            type == TattooType.Face ? (_faceTatPool ?? (_faceTatPool = BuildTatPool(TattooType.Face)))
                                    : (_bodyTatPool ?? (_bodyTatPool = BuildTatPool(TattooType.Body)));

        /// <summary>After a rape, the aggressor may permanently mark their victim with a degrading tattoo (face
        /// slot first, then body). Needs Ideology (the tattoo system); chance scales with the rapist's cruelty.
        /// Toggle S.rapistMayTattoo. Picks from our own RJWSH_ tattoos when present, else any vanilla/mod tattoo.</summary>
        public static void TryRapistTattoo(rjw.SexProps props)
        {
            if (props == null || !props.isRape || S == null || !S.rapistMayTattoo) return;
            if (!ModsConfig.IdeologyActive) return;
            Pawn rapist = props.initiator, victim = props.recipient;
            if (rapist == null || victim == null || rapist == victim) return;
            if (victim.Dead || !victim.Spawned || !victim.RaceProps.Humanlike || victim.style == null) return;

            if (!Rand.Chance(0.12f + 0.20f * Mathf01(Evilness(rapist) * 0.6f))) return;

            // Face is the more visible humiliation, so mark it first; only touch the body once the face is taken.
            bool faceOpen = victim.style.FaceTattoo == null || victim.style.FaceTattoo == TattooDefOf.NoTattoo_Face;
            bool bodyOpen = victim.style.BodyTattoo == null || victim.style.BodyTattoo == TattooDefOf.NoTattoo_Body;
            TattooType type;
            if (faceOpen) type = TattooType.Face;
            else if (bodyOpen) type = TattooType.Body;
            else return; // already marked on both slots

            var pool = TatPool(type);
            if (pool.Count == 0) return;
            var def = pool.RandomElement();
            if (type == TattooType.Face) victim.style.FaceTattoo = def; else victim.style.BodyTattoo = def;
            victim.style.Notify_StyleItemChanged();

            if (PawnUtility.ShouldSendNotificationAbout(victim))
                Messages.Message(rapist.LabelShortCap + " marked " + victim.LabelShort + " with a degrading tattoo.",
                    new LookTargets(victim, rapist), MessageTypeDefOf.NegativeEvent, false);
        }

        /// <summary>Ensures a control-collared pawn carries the conditioning hediff (which then self-ramps).</summary>
        public static void ApplyConditioningHediff(Pawn p)
        {
            if (p?.health == null || !p.RaceProps.Humanlike) return;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("RJWSH_Conditioning");
            if (def == null || p.health.hediffSet.HasHediff(def)) return;
            p.health.AddHediff(def);
        }

        public static void ShockCollar(Pawn wearer)
        {
            if (wearer == null || wearer.Dead || !wearer.Spawned) return;
            try
            {
                wearer.stances?.stunner?.StunFor(180, wearer, false);
                FABridge.PlayFace(wearer, "RJWSH_FA_Flinch"); // FA: jolt of pain from the collar
                ApplyThought(wearer, wearer, RJWSH_ThoughtDefOf.RJWSH_Shocked);
                var prof = GameComponent_Harassment.Instance?.GetProfile(wearer);
                if (prof != null) prof.ApplyCond("Shocked", 3f, -3f);
                OnShockApplied(wearer); // dedicated shock SoundDef + masochist shock-lust escalation
            }
            catch { }
        }

        /// <summary>Non-lethal collar shock for "shock until downed": stuns and applies small jolts to the
        /// limbs so pain builds toward a pain-shock collapse, and anesthetizes (never kills) if they get hurt.</summary>
        public static void ShockTowardDowned(Pawn wearer)
        {
            if (wearer == null || wearer.Dead || !wearer.Spawned) return;
            try
            {
                wearer.stances?.stunner?.StunFor(120, wearer, false);
                ApplyThought(wearer, wearer, RJWSH_ThoughtDefOf.RJWSH_Shocked);
                OnShockApplied(wearer);
                // If they are already badly hurt, put them under rather than risk a kill.
                if (wearer.health != null && wearer.health.summaryHealth.SummaryHealthPercent < 0.45f)
                {
                    HealthUtility.TryAnesthetize(wearer);
                    return;
                }
                // Small jolt to a non-vital limb (outside, not the head or torso) so pain rises without
                // destroying organs - the pawn pain-shocks down well before any lethal accumulation.
                BodyPartRecord part = wearer.health?.hediffSet?.GetNotMissingParts()
                    ?.Where(pp => pp.depth == BodyPartDepth.Outside && pp.height != BodyPartHeight.Top && pp.def != BodyPartDefOf.Torso)
                    .RandomElementWithFallback(null);
                wearer.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f, 0f, -1f, wearer, part));
            }
            catch { }
        }

        /// <summary>A damaging collar shock used by "shock until dead": stuns and burns the torso so repeated
        /// calls eventually kill the wearer.</summary>
        public static void ApplyLethalShock(Pawn wearer)
        {
            if (wearer == null || wearer.Dead || !wearer.Spawned) return;
            try
            {
                wearer.stances?.stunner?.StunFor(120, wearer, false);
                BodyPartRecord part = wearer.health?.hediffSet?.GetNotMissingParts()
                    ?.FirstOrDefault(pp => pp.def == BodyPartDefOf.Torso);
                wearer.TakeDamage(new DamageInfo(DamageDefOf.Burn, 8f, 0f, -1f, wearer, part));
                ApplyThought(wearer, wearer, RJWSH_ThoughtDefOf.RJWSH_Shocked);
                OnShockApplied(wearer);
            }
            catch { }
        }

        /// <summary>Fires on every collar shock: plays the dedicated shock SoundDef, and if the wearer is a
        /// masochist, escalates their shock-lust - hornier and more pliable with each jolt, doubled while they
        /// are locked in BDSM gear.</summary>
        public static void PlayShockSound(Pawn at) => PlaySoundClip("RJWSH_Shock", at);

        public static void OnShockApplied(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return;
            PlayShockSound(pawn);
            AttrDelta(pawn, will: -1f, trauma: 0.5f); // every jolt chips at will and adds a little trauma
            bool maso = false; try { maso = xxx.is_masochist(pawn); } catch { }
            if (!maso) return;
            try
            {
                float amp = IsInBondage(pawn) ? 2f : 1f; // BDSM gear amplifies the conditioning
                float sev = BumpShockLust(pawn, 0.10f * amp);
                // Lust: shocks leave the masochist achingly horny (lower the RJW sex need toward frustrated),
                // scaled by how deep the shock-lust conditioning already runs.
                BumpArousal(pawn, -(0.10f + 0.30f * sev) * amp);
                // Vulnerability (in this mod's terms): each jolt makes them more of a pushover, less willed.
                var prof = GameComponent_Harassment.Instance?.GetProfile(pawn);
                if (prof != null)
                {
                    prof.impression = UnityEngine.Mathf.Min(50f, prof.impression + 3f * amp);
                    prof.confidence = UnityEngine.Mathf.Max(0f, prof.confidence - 2f * amp);
                    prof.slaveWill = UnityEngine.Mathf.Max(0f, prof.slaveWill - 4f * amp * sev);
                    if (sev > 0.3f) prof.ApplyCond("Shock lust", 2f * amp, -1f); // the craving is its own conditioning
                }
                ThrowControlMote(pawn, "\u2665", new UnityEngine.Color(1f, 0.4f, 0.7f)); // a lustful flush, not pain
            }
            catch { }
        }

        /// <summary>Adds severity to (creating if absent) the shock-lust hediff; returns the new severity.</summary>
        private static float BumpShockLust(Pawn pawn, float delta)
        {
            if (pawn?.health == null || RJWSH_HediffDefOf.RJWSH_ShockLust == null) return 0f;
            var h = pawn.health.hediffSet.GetFirstHediffOfDef(RJWSH_HediffDefOf.RJWSH_ShockLust);
            if (h == null)
            {
                h = pawn.health.AddHediff(RJWSH_HediffDefOf.RJWSH_ShockLust);
                h.Severity = UnityEngine.Mathf.Clamp01(0.05f + delta);
            }
            else h.Severity = UnityEngine.Mathf.Clamp01(h.Severity + delta);
            return h.Severity;
        }

        /// <summary>Nudges the RJW sex need (lower = hornier/more frustrated). No-ops without RJW's need.</summary>
        private static void BumpArousal(Pawn p, float delta)
        {
            try
            {
                var need = p?.needs?.TryGetNeed<rjw.Need_Sex>();
                if (need != null) need.CurLevel = UnityEngine.Mathf.Clamp01(need.CurLevel + delta);
            }
            catch { }
        }

        // ── Key-holder control (the "remote") ──────────────────────────────
        /// <summary>Gizmos for a player pawn carrying RJW keys: each matched locked victim gets Command /
        /// Shock (if collared) / Unbind. Built in a try/catch by the GetGizmos patch.</summary>
        public static List<Gizmo> BuildKeyHolderGizmos(Pawn controller, Pawn onlyFor = null)
        {
            if (controller == null || !controller.Spawned || controller.inventory == null) return null;
            bool playerControl = (controller.Faction != null && controller.Faction.IsPlayer)
                                 || controller.IsPrisonerOfColony || controller.IsSlaveOfColony;
            if (!playerControl) return null;
            var inv = controller.inventory.innerContainer;
            if (inv == null || inv.Count == 0) return null;

            int now = Find.TickManager.TicksGame;
            List<Gizmo> list = null;
            for (int k = 0; k < inv.Count; k++)
            {
                var keyComp = inv[k].TryGetComp<rjw.CompHoloCryptoStamped>();
                if (keyComp == null) continue;
                var keyThing = inv[k];
                Pawn v = FindLockedVictimForKey(keyComp, controller.Map);
                if (v == null) continue;
                // Only the CONTROL COLLAR grants the key-holder control suite. A key for any other locked
                // device (cuffs, gag, etc.) shows no control gizmos - those are unlocked via the gear tab.
                if (!KeyMatchesControlCollar(keyComp, v)) continue;
                if (onlyFor != null && v != onlyFor) continue; // dashboard: only this pet's controls
                var vp = GameComponent_Harassment.Instance?.GetProfile(v);
                if (vp == null) continue;
                if (list == null) list = new List<Gizmo>();

                // Hide/show toggle: collapse this pawn's controls to keep the gizmo bar clean with many slaves.
                if (onlyFor == null && vp.hideControls)
                {
                    var capturedVp = vp;
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Show " + v.LabelShort,
                        defaultDesc = "Show the control gizmos for " + v.LabelShort + " again.",
                        icon = HarassmentTextures.ShowControls,
                        action = delegate { capturedVp.hideControls = false; }
                    });
                    continue;
                }
                if (onlyFor == null)   // the dashboard has its own layout; no hide/show clutter toggle
                {
                    var capturedVp = vp;
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Hide " + v.LabelShort,
                        defaultDesc = "Collapse all of " + v.LabelShort + "'s control gizmos to keep the UI clean. Use \"Show " + v.LabelShort + "\" to bring them back.",
                        icon = HarassmentTextures.HideControls,
                        action = delegate { capturedVp.hideControls = true; }
                    });
                }

                // An AI pawn is driving this collar - the player can watch but not give orders.
                if (vp.aiControlled)
                {
                    string by = ControllerLabel(v) ?? "Someone";
                    var locked = new Command_Action
                    {
                        defaultLabel = v.LabelShort + " (controlled)",
                        defaultDesc = by + " is controlling this collar on their own. You can see what they do, but cannot give orders.",
                        icon = HarassmentTextures.Command,
                        action = delegate { }
                    };
                    locked.Disable(by + " is controlling this");
                    list.Add(locked);
                    continue;
                }

                Pawn owner = controller;
                // Holding the matching Holokey makes you this pawn's owner for the social tab.
                EnsureOwnerRelation(owner, v);
                bool onCd = now < vp.controlCooldownTick;
                string cdReason = onCd ? "On cooldown" : null;

                // Command: self-targetable; opens RJW's sex-type menu for the chosen target.
                var cmd = new Command_Target
                {
                    defaultLabel = "Command " + v.LabelShort,
                    defaultDesc = "Use the remote to order " + v.LabelShort + " to service a pawn (target yourself to be serviced). You choose the act.",
                    icon = HarassmentTextures.Command,
                    targetingParams = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetAnimals = true,
                        canTargetBuildings = false,
                        validator = (TargetInfo t) => t.Thing is Pawn other && other != v && !other.Dead
                            && (other.RaceProps.Humanlike || (other.RaceProps.Animal && BestialityEnabled()))
                    },
                    action = (LocalTargetInfo t) => { if (t.Thing is Pawn other && other != v) CommandServe(v, other); }
                };
                if (onCd) { cmd.Disable(cdReason); }
                list.Add(cmd);

                if (WearingControlCollar(v))
                {
                    Pawn shockV = v;
                    var shock = new Command_Action
                    {
                        defaultLabel = "Shock " + v.LabelShort,
                        defaultDesc = "Trigger " + v.LabelShort + "'s shock collar: stun and deepen their conditioning.",
                        icon = HarassmentTextures.Shock,
                        action = delegate { ShockCollar(shockV); SetControlCooldown(shockV); }
                    };
                    if (onCd) shock.Disable(cdReason);
                    list.Add(shock);

                    // Continuous shock modes (no cooldown gate - they run until they finish or are toggled off).
                    list.Add(new Command_Toggle
                    {
                        defaultLabel = "Shock until downed: " + v.LabelShort,
                        defaultDesc = "Keep shocking " + v.LabelShort + " until they collapse. Stops on its own once they are downed.",
                        icon = HarassmentTextures.ShockDown,
                        isActive = () => vp.shockUntil == 1,
                        toggleAction = delegate { vp.shockUntil = vp.shockUntil == 1 ? 0 : 1; }
                    });
                    list.Add(new Command_Toggle
                    {
                        defaultLabel = "Shock until dead: " + v.LabelShort,
                        defaultDesc = "Keep shocking " + v.LabelShort + " until they die. This will kill them.",
                        icon = HarassmentTextures.ShockDead,
                        isActive = () => vp.shockUntil == 2,
                        toggleAction = delegate { vp.shockUntil = vp.shockUntil == 2 ? 0 : 2; }
                    });
                }

                // Follow toggle.
                Pawn followV = v;
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Follow: " + v.LabelShort,
                    defaultDesc = "Force " + v.LabelShort + " to follow you closely.",
                    icon = HarassmentTextures.Follow,
                    isActive = () => vp.followOwner && vp.ownerId == owner.thingIDNumber,
                    toggleAction = delegate
                    {
                        bool nowOn = !(vp.followOwner && vp.ownerId == owner.thingIDNumber);
                        vp.followOwner = nowOn;
                        if (nowOn) vp.stayCell = IntVec3.Invalid; // following and staying are mutually exclusive
                        vp.ownerId = nowOn ? owner.thingIDNumber : -1;
                        if (nowOn) EnsureOwnerRelation(owner, followV); else RemoveOwnerRelation(owner, followV);
                        if (!nowOn && followV.jobs?.curJob?.def == RJWSH_JobDefOf.RJWSH_Follow)
                            followV.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                });

                // Allow-needs toggle: free the collared pawn to sleep/eat/drink/bathe on their own.
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Allow needs: " + v.LabelShort,
                    defaultDesc = "Let " + v.LabelShort + " freely sleep, eat, drink, and use the bathroom. Follow and auto-service pause while this is on. They are also freed automatically whenever you (the key-holder) are asleep.",
                    icon = HarassmentTextures.Command,
                    isActive = () => vp.allowNeeds,
                    toggleAction = delegate { vp.allowNeeds = !vp.allowNeeds; }
                });

                // Keep-naked toggle: force the pawn to wear nothing but their locked devices.
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Keep naked: " + v.LabelShort,
                    defaultDesc = "Force " + v.LabelShort + " to wear nothing but their locked devices. Any other clothing is stripped off and forbidden.",
                    icon = HarassmentTextures.KeepNaked,
                    isActive = () => vp.forceNudity,
                    toggleAction = delegate { vp.forceNudity = !vp.forceNudity; if (vp.forceNudity) StripToBondage(followV); }
                });

                // Auto-service (auto-cast) toggle + its configuration.
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Auto-service: " + v.LabelShort,
                    defaultDesc = "Periodically order " + v.LabelShort + " to service the chosen group on their own (respects the cooldown).",
                    icon = HarassmentTextures.AutoService,
                    isActive = () => vp.autoService && vp.ownerId == owner.thingIDNumber,
                    toggleAction = delegate
                    {
                        bool nowOn = !(vp.autoService && vp.ownerId == owner.thingIDNumber);
                        vp.autoService = nowOn;
                        if (nowOn) { vp.ownerId = owner.thingIDNumber; EnsureOwnerRelation(owner, followV); }
                    }
                });
                if (vp.autoService && vp.ownerId == owner.thingIDNumber)
                {
                    var sp = vp;
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Serve: " + ServiceGroupLabel(sp.serviceTargetMode),
                        defaultDesc = "Choose who " + v.LabelShort + " auto-services: the owner, colonists, prisoners & slaves, guests, or anyone nearby.",
                        icon = HarassmentTextures.Command,
                        action = delegate { Find.WindowStack.Add(new FloatMenu(BuildServiceGroupMenu(sp))); }
                    });
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Act: " + ServiceActLabel(sp.serviceInteraction),
                        defaultDesc = "Choose how " + v.LabelShort + " services: a specific act, or the default quick service.",
                        icon = HarassmentTextures.AutoService,
                        action = delegate { Find.WindowStack.Add(new FloatMenu(BuildServiceActMenu(sp))); }
                    });
                }

                // --- Discipline / Reward: active carrot-and-stick levers on the conditioning meter ---
                Pawn dv = v;
                bool tendCd = now < vp.tendCooldownTick;
                var disc = new Command_Action
                {
                    defaultLabel = "Discipline " + v.LabelShort,
                    defaultDesc = "Strike " + v.LabelShort + " non-lethally to punish them. Deepens their conditioning through fear, but hurts their mood.",
                    icon = HarassmentTextures.Discipline,
                    action = delegate { StartDiscipline(owner, dv); }
                };
                if (tendCd) disc.Disable("On cooldown");
                list.Add(disc);

                var rew = new Command_Action
                {
                    defaultLabel = "Reward " + v.LabelShort,
                    defaultDesc = "Praise and reward " + v.LabelShort + ". Lifts their mood and reinforces their submission, eroding their will to break free.",
                    icon = HarassmentTextures.Reward,
                    action = delegate { StartReward(owner, dv); }
                };
                if (tendCd) rew.Disable("On cooldown");
                list.Add(rew);

                // --- Auto-cast toggles for reward / discipline ---
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Auto-reward: " + v.LabelShort,
                    defaultDesc = "Periodically reward " + v.LabelShort + " on your own (respects the cooldown). Builds trust and rapport over time.",
                    icon = HarassmentTextures.Reward,
                    isActive = () => vp.autoReward,
                    toggleAction = delegate { vp.autoReward = !vp.autoReward; }
                });
                list.Add(new Command_Toggle
                {
                    defaultLabel = "Auto-discipline: " + v.LabelShort,
                    defaultDesc = "Periodically discipline " + v.LabelShort + " on your own (respects the cooldown). Deepens conditioning through fear.",
                    icon = HarassmentTextures.Discipline,
                    isActive = () => vp.autoDiscipline,
                    toggleAction = delegate { vp.autoDiscipline = !vp.autoDiscipline; }
                });

                // (Conditioning / training focus + role are managed on the Control tab, not as a gizmo.)

                // --- Parade: show the pet off for notoriety + humiliation ---
                list.Add(new Command_Action
                {
                    defaultLabel = "Parade " + v.LabelShort,
                    defaultDesc = "Show " + v.LabelShort + " off to everyone nearby. Onlookers react by their nature, your notoriety grows, and " + v.LabelShort + " is humiliated (a masochist may relish it).",
                    icon = HarassmentTextures.Summon,
                    action = delegate { DepthStartParade(owner, dv); }
                });

                // --- Sell to a present trader (price scales with conditioning + attributes) ---
                if (AnyActiveTrader(owner.Map))
                {
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Sell " + v.LabelShort,
                        defaultDesc = "Sell " + v.LabelShort + " to the trader currently here. The price scales with how well-conditioned, compliant and attractive they are. They leave your control for good.",
                        icon = HarassmentTextures.HandKey,
                        action = delegate { DepthSellPet(owner, dv); }
                    });
                }

                // --- Dress up: open the loadout window (multi-select BDSM gear + live paperdoll preview) ---
                list.Add(new Command_Action
                {
                    defaultLabel = "Dress up " + v.LabelShort,
                    defaultDesc = "Open the dress-up window: pick any combination of locked devices and skimpy outfits, preview the look on a paperdoll, then have " + owner.LabelShort + " walk over and lock them all on.",
                    icon = HarassmentTextures.DressUp,
                    action = delegate { Find.WindowStack.Add(new Dialog_DressUp(dv, owner)); }
                });

                // --- Whore out: send the slave to service a nearby visitor for silver ---
                list.Add(new Command_Action
                {
                    defaultLabel = "Whore out: " + v.LabelShort,
                    defaultDesc = "Send " + v.LabelShort + " to service a nearby visitor. The fee goes to you.",
                    icon = HarassmentTextures.AutoService,
                    action = delegate { StartWhore(owner, dv); }
                });

                // --- Positioning: come / stay ---
                list.Add(new Command_Action
                {
                    defaultLabel = "Come here: " + v.LabelShort,
                    defaultDesc = "Order " + v.LabelShort + " to come to you right now (cancels any stay spot).",
                    icon = HarassmentTextures.Summon,
                    action = delegate { ComeHere(owner, dv); }
                });
                list.Add(new Command_Target
                {
                    defaultLabel = "Stay: " + v.LabelShort,
                    defaultDesc = "Order " + v.LabelShort + " to wait at a chosen spot until told otherwise (cancels follow).",
                    icon = HarassmentTextures.Stay,
                    targetingParams = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetSelf = false },
                    action = (LocalTargetInfo t) => { if (t.IsValid && dv.Map != null && t.Cell.InBounds(dv.Map)) SetStay(dv, t.Cell); }
                });

                // --- Hand over the key (transfers control; cruel recipients seize it as an AI controller) ---
                Thing keyThingC = keyThing;
                list.Add(new Command_Action
                {
                    defaultLabel = "Hand over key: " + v.LabelShort,
                    defaultDesc = "Give this collar's key to another pawn nearby, transferring control. You walk the key over to them. If they are cruel, they take over and run the collar themselves - locking your gizmos.",
                    icon = HarassmentTextures.HandKey,
                    action = delegate { OpenHandOverMenu(owner, dv, keyThingC); }
                });

                list.Add(new Command_Action
                {
                    defaultLabel = "Copy key: " + v.LabelShort,
                    defaultDesc = "Mint a second key to " + v.LabelShort + "'s collar and give it to another colonist nearby, so you both control them. You walk the copy over to them.",
                    icon = HarassmentTextures.HandKey,
                    action = delegate { OpenCopyKeyMenu(owner, dv, keyThingC); }
                });

                // --- Enslave: a fully-conditioned collared prisoner becomes a permanent colony slave ---
                if (SlaveryHooks.CanEnslave(v))
                {
                    Pawn ev = v;
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Enslave " + v.LabelShort,
                        defaultDesc = "This prisoner is fully conditioned and collared. Make them a permanent colony slave - the collar and conditioning keep them from rebelling.",
                        icon = HarassmentTextures.Command,
                        action = delegate { SlaveryHooks.Enslave(ev, owner); }
                    });
                }

                // --- Free: remove the collar and end control (conditioning persists) ---
                if (WearingControlCollar(v))
                {
                    list.Add(new Command_Action
                    {
                        defaultLabel = "Free " + v.LabelShort,
                        defaultDesc = "Remove " + v.LabelShort + "'s control collar and release them from your control. Their conditioning remains.",
                        icon = HarassmentTextures.Free,
                        action = delegate { FreeCollared(dv); }
                    });
                }

                Pawn unbindV = v;
                var keyCompC = keyComp;
                list.Add(new Command_Action
                {
                    defaultLabel = "Unbind " + v.LabelShort,
                    defaultDesc = "Use this key to unlock " + v.LabelShort + "'s restraints and release them.",
                    icon = HarassmentTextures.Unbind,
                    action = delegate { UnbindVictim(unbindV, keyCompC); }
                });
            }
            return list;
        }

        public static bool OnControlCooldown(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            return vp != null && Find.TickManager.TicksGame < vp.controlCooldownTick;
        }

        public static void SetControlCooldown(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp != null) vp.controlCooldownTick = Find.TickManager.TicksGame + S.gizmoCooldownTicks;
        }

        public static void SetCommandCooldown(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp != null) vp.controlCooldownTick = Find.TickManager.TicksGame + S.commandCooldownTicks;
        }

        public static bool BestialityEnabled()
        {
            try { return rjw.RJWSettings.bestiality_enabled; } catch { return false; }
        }

        // ---- Owner action helpers (Discipline / Reward / Come / Stay / Hand over / Free) ----

        // The owner walks over and physically disciplines/rewards their pet (Melee Animation animates strikes).
        public static void StartDiscipline(Pawn owner, Pawn victim) { SatisfySubmission(victim, 0.3f); StartOwnerInteract(owner, victim, RJWSH_JobDefOf.RJWSH_DisciplinePet); }
        public static void StartReward(Pawn owner, Pawn victim) => StartOwnerInteract(owner, victim, RJWSH_JobDefOf.RJWSH_RewardPet);

        private static void StartOwnerInteract(Pawn owner, Pawn victim, JobDef def)
        {
            if (owner?.jobs == null || victim == null || !victim.Spawned || owner == victim) return;
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            int now = Find.TickManager.TicksGame;
            if (vp != null) vp.tendCooldownTick = now + 1250;
            if (!owner.Spawned || !owner.CanReach(victim, PathEndMode.Touch, Danger.Deadly))
            {
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(owner.LabelShort + " cannot reach " + victim.LabelShort + ".", new LookTargets(victim), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool reward = def == RJWSH_JobDefOf.RJWSH_RewardPet;
            bool dress = def == RJWSH_JobDefOf.RJWSH_DressPet;
            bool discipline = def == RJWSH_JobDefOf.RJWSH_DisciplinePet;
            bool train = def == RJWSH_JobDefOf.RJWSH_TrainPet;
            // Dress-up, discipline AND conditioning sessions are things the owner does TO the pet - a pet cannot
            // opt out of them (a defiant pet just resists, handled by the training RNG). Only reward/command
            // interactions can be refused.
            bool obeys = dress || discipline || train || ObeysOwnerCommand(victim);

            if (!obeys)
            {
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(victim.LabelShort + " is refusing " + owner.LabelShort + "'s command.",
                        new LookTargets(new[] { owner, victim }), MessageTypeDefOf.RejectInput, false);
                if (reward) return;   // a defiant pet will not accept affection
                // A defiant pet being disciplined resists rather than submitting - they fight back instead of taking it.
                if (!victim.Downed && vp != null && now >= vp.resistCooldownTick) AttemptFightBack(victim);
                return;
            }

            // A disciplined pet may bolt rather than take the beating; if they do, the owner escalates and the
            // normal discipline is aborted here.
            if (discipline && TryFleeBeating(owner, victim)) return;

            // Obeying: the pet stops what it is doing and holds still so its owner can come to it for the interaction.
            if (!victim.Downed && victim.jobs != null && !(victim.jobs.curDriver is rjw.JobDriver_Sex))
            {
                var wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 600;
                victim.jobs.StartJob(wait, JobCondition.InterruptForced);
            }
            owner.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, victim), JobTag.Misc);
        }

        /// <summary>Whether a pet obeys a discipline/reward command right now. A conditioned pet always complies;
        /// an unconditioned one's compliance scales with rapport (fear) and conditioning, so a defiant, low-rapport
        /// pet frequently refuses.</summary>
        public static bool ObeysOwnerCommand(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp == null) return true;
            if (vp.IsConditioned) return true;
            float obey = 0.3f + vp.rapport / 200f + vp.hypnosisLevel / 200f; // ~0.3 .. 0.8
            return Rand.Chance(Mathf01(obey));
        }

        /// <summary>One non-lethal unarmed strike + a fear mote, called repeatedly during the discipline bout.</summary>
        public static void DisciplineStrike(Pawn owner, Pawn victim)
        {
            ThrowControlMote(victim, "!", new UnityEngine.Color(1f, 0.5f, 0.4f));
            FABridge.PlayFace(victim, "RJWSH_FA_Cry"); // FA: fearful cringe as the blow lands
            ForceMelee(owner, victim); // capped non-lethal
            // Guaranteed audible thud each strike - the real melee verb only plays a sound when it is off
            // cooldown, so beatings would otherwise fall silent between hits (Melee Animation adds the visual).
            PlaySoundClip("Pawn_Melee_Punch_HitPawn", victim);
        }

        /// <summary>Shifts a pawn's Sub/Dom attribute (clamped -100..100). Domineering acts push the actor
        /// toward dominant and the one on the receiving end toward submissive.</summary>
        public static void ShiftSubDom(Pawn p, float delta) => AttrDelta(p, subdom: delta);

        /// <summary>Central attribute-change entry point: every mechanic that should move a pawn's deep sexual
        /// attributes routes through here (clamped 0..100, subDom -100..100). See the effect table in the
        /// schematic for who moves what.</summary>
        public static void AttrDelta(Pawn p, float will = 0f, float esteem = 0f, float spirit = 0f,
            float addiction = 0f, float trauma = 0f, float subdom = 0f)
        {
            if (p == null) return;
            var sx = GameComponent_Harassment.Instance?.GetProfile(p)?.SexAttr(p);
            if (sx == null) return;
            if (will != 0f) sx.willpower = Clamp100(sx.willpower + will);
            if (esteem != 0f) sx.selfEsteem = Clamp100(sx.selfEsteem + esteem);
            if (spirit != 0f) sx.spirit = Clamp100(sx.spirit + spirit);
            if (addiction != 0f) sx.sexAddiction = Clamp100(sx.sexAddiction + addiction);
            if (trauma != 0f) sx.trauma = Clamp100(sx.trauma + trauma);
            if (subdom != 0f) sx.subDom = UnityEngine.Mathf.Clamp(sx.subDom + subdom, -100f, 100f);
        }

        public static void FinishDiscipline(Pawn owner, Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            vp.ApplyCond("Disciplined", 8f, -6f); // fear breaks them fast, but erodes trust -> volatile
            TryAddMoodThought(victim, "RJWSH_Disciplined");
            // Being beaten erodes will/esteem/spirit and breaks them toward submission; the owner grows dominant.
            AttrDelta(victim, will: -3f, esteem: -3f, spirit: -2f, subdom: -4f);
            ShiftSubDom(owner, 2f);
            // Owner side: a social-log entry for the bout + a mood memory. A cruel/sadist owner relishes it
            // (stage 1, positive); a normal owner is left with a sour taste (stage 0, negative).
            if (owner != null && owner != victim)
            {
                FireFlavorLine(owner, victim, RJWSH_InteractionDefOf.RJWSH_Discipline);
                bool relished = IsSadist(owner) || Evilness(owner) > 0.5f;
                TryAddMoodThoughtStaged(owner, "RJWSH_DisciplinedMyPet", relished ? 1 : 0);
            }
        }

        public static void FinishReward(Pawn owner, Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            SatisfySubmission(victim, 0.35f);
            ThrowControlMote(victim, "\u2665", new UnityEngine.Color(1f, 0.6f, 0.8f));
            FABridge.PlayFace(victim, "RJWSH_FA_Bliss"); // FA: a flush of relief/pleasure
            vp.ApplyCond("Rewarded", 4f, 8f); // kindness builds trust -> placid, harder to snap free
            TryAddMoodThought(victim, "RJWSH_Rewarded");
            // Kindness rebuilds self-worth and eases trauma, and softens the whip-broken a little.
            AttrDelta(victim, esteem: 4f, will: 1f, trauma: -2f);
            // Owner side: a social-log entry for the reward + a warm mood memory.
            if (owner != null && owner != victim)
            {
                FireFlavorLine(owner, victim, RJWSH_InteractionDefOf.RJWSH_Reward);
                TryAddMoodThought(owner, "RJWSH_RewardedMyPet");
            }
            // The reward IS an affection moment - the owner shares a kiss / holds hands with their pet.
            OnAffectionStart(owner, victim, Rand.Bool ? AffectionKind.Kiss : AffectionKind.HoldHands);
        }

        // ---- Conditioning sessions: an owner tries to reshape a pet's psychological attributes ----
        public static void OpenTrainMenu(Pawn owner, Pawn victim)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Break their will (lower willpower)", delegate { StartTraining(owner, victim, "willpower"); }),
                new FloatMenuOption("Humble them (lower self-esteem)", delegate { StartTraining(owner, victim, "esteem"); }),
                new FloatMenuOption("Crush their spirit", delegate { StartTraining(owner, victim, "spirit"); }),
                new FloatMenuOption("Train submission (more submissive)", delegate { StartTraining(owner, victim, "subdom"); }),
                new FloatMenuOption("Cultivate their addiction", delegate { StartTraining(owner, victim, "addiction"); }),
                new FloatMenuOption("Assign role: pleasure pet", delegate { SetPetRole(owner, victim, 1); }),
                new FloatMenuOption("Assign role: house servant", delegate { SetPetRole(owner, victim, 2); }),
                new FloatMenuOption("Assign role: bodyguard", delegate { SetPetRole(owner, victim, 3); }),
                new FloatMenuOption("Assign role: performer", delegate { SetPetRole(owner, victim, 4); }),
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        public static void StartTraining(Pawn owner, Pawn victim, string statKey)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            vp.pendingTrainStat = statKey;
            StartOwnerInteract(owner, victim, RJWSH_JobDefOf.RJWSH_TrainPet);
        }

        /// <summary>On-arrival: rolls RNG success (weighted by conditioning + rapport, resisted by willpower)
        /// and shifts the chosen attribute. A failed session lets the pet claw back a little willpower.</summary>
        public static void FinishTraining(Pawn owner, Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            var sx = vp.SexAttr(victim);
            string key = vp.pendingTrainStat; vp.pendingTrainStat = null;
            if (key.NullOrEmpty()) return;
            float cond = vp.hypnosisLevel / 100f;
            float rap = vp.rapport / 100f;
            float will = sx.willpower / 100f;
            float chance = UnityEngine.Mathf.Clamp01(0.35f + cond * 0.40f + rap * 0.10f - will * 0.35f);
            bool success = Rand.Chance(chance);
            float mag = success ? Rand.Range(7f, 13f) : Rand.Range(0f, 2f);
            string what;
            switch (key)
            {
                case "willpower": sx.willpower = Clamp100(sx.willpower - mag); what = "will"; break;
                case "esteem": sx.selfEsteem = Clamp100(sx.selfEsteem - mag); what = "self-esteem"; break;
                case "spirit": sx.spirit = Clamp100(sx.spirit - mag); what = "spirit"; break;
                case "subdom": sx.subDom = UnityEngine.Mathf.Clamp(sx.subDom - mag, -100f, 100f); what = "defiance"; break;
                case "addiction": sx.sexAddiction = Clamp100(sx.sexAddiction + mag); what = "restraint"; break;
                default: return;
            }
            if (success)
            {
                vp.ApplyCond("Conditioning session", 3f, -1f);
                ShiftSubDom(owner, 2f); // running a session over them reinforces the owner's dominance
                ThrowControlMote(victim, "*", new UnityEngine.Color(0.7f, 0.5f, 0.9f));
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(owner.LabelShort + " conditioned " + victim.LabelShort + ", chipping away at their " + what + ".",
                        new LookTargets(victim), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                sx.willpower = Clamp100(sx.willpower + 2f); // resisting the session hardens them a little
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(victim.LabelShort + " resisted the conditioning session.",
                        new LookTargets(victim), MessageTypeDefOf.RejectInput, false);
            }
        }

        public static void StartDressUp(Pawn owner, Pawn victim) => StartOwnerInteract(owner, victim, RJWSH_JobDefOf.RJWSH_DressPet);

        // Dress-up pool weighted toward fishnets / stockings / lingerie (repeated names = higher weight).
        private static readonly string[] DressUpWeighted = {
            "RJWSH_FN_FishnetA", "RJWSH_FN_FishnetB", "RJWSH_FN_FishnetC", "RJWSH_FN_FishnetD", "RJWSH_FN_FishnetE",
            "RJWSH_DZZBSW", "RJWSH_DZZBSW", "RJWSH_DZZLGS",
            "RJWSH_UZZLSH", "RJWSH_UZZLBS", "RJWSH_MZZLBW", "RJWSH_MZZMNK",
            "RJWSH_RShibari", "RJWSH_BShibari", "RJWSH_BLShibari",
            "RJWSH_Swimsuits_MicroBikini", "RJWSH_Swimsuits_SlingBikini", "RJWSH_Swimsuits_Vsling",
            "RJWSH_Ranchu_Bunnysuit", "RJWSH_Ranchu_ReverseBunnysuit"
        };

        // Owner dresses the pet in a random fitting skimpy piece (locked on).
        public static void DressUp(Pawn owner, Pawn victim)
        {
            if (victim?.apparel == null) return;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            // A combo queued from the dress-up window takes priority over the random pick.
            if (vp != null && vp.pendingDressUp != null && vp.pendingDressUp.Count > 0)
            {
                for (int i = 0; i < vp.pendingDressUp.Count; i++)
                {
                    var pd = DefDatabase<ThingDef>.GetNamedSilentFail(vp.pendingDressUp[i]);
                    if (pd != null && pd.IsApparel && ApparelUtility.HasPartsToWear(victim, pd) && !ConflictsWithLocked(victim, pd))
                        ApplyAndLockDevice(victim, pd, owner);
                }
                vp.pendingDressUp.Clear();
                return;
            }
            var pool = new List<ThingDef>(); var weights = new List<float>();
            for (int i = 0; i < DressUpWeighted.Length; i++)
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(DressUpWeighted[i]);
                if (d == null || !d.IsApparel) continue;
                if (!ApparelUtility.HasPartsToWear(victim, d)) continue;
                if (ConflictsWithLocked(victim, d)) continue;
                pool.Add(d); weights.Add(1f);
            }
            if (pool.Count == 0) return;
            var def = WeightedPick(pool, weights);
            if (def != null) ApplyAndLockDevice(victim, def, owner);
        }

        /// <summary>Queue a specific combo from the dress-up window, then send the owner over to apply it.</summary>
        public static void QueueDressUp(Pawn owner, Pawn slave, List<string> picks)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(slave);
            if (vp == null) return;
            vp.pendingDressUp = picks ?? new List<string>();
            TryAddMoodThought(slave, "RJWSH_DressedUp");
            if (owner != null && owner.Spawned) StartDressUp(owner, slave);
            else DressUp(owner, slave);
        }

        /// <summary>Equip a device on the slave for live preview only (lock/key/hediff suppressed). Returns the
        /// worn instance, or null if it does not fit.</summary>
        public static Apparel PreviewEquip(Pawn slave, ThingDef def)
        {
            if (slave?.apparel == null || def == null || !def.IsApparel) return null;
            if (!ApparelUtility.HasPartsToWear(slave, def)) return null;
            // Never preview-equip something that would force off an already-locked device (Wear destroys conflicts).
            if (ConflictsWithLocked(slave, def)) return null;
            try
            {
                var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                var app = (Apparel)ThingMaker.MakeThing(def, stuff);
                Dialog_DressUp.Previewing = true;
                slave.apparel.Wear(app, false, false);
                Dialog_DressUp.Previewing = false;
                DestroyKeysAt(slave); // belt-and-suspenders in case on_wear was not suppressed
                if (app.Wearer != slave) { try { if (!app.Destroyed) app.Destroy(); } catch { } return null; }
                return app;
            }
            catch { Dialog_DressUp.Previewing = false; return null; }
        }

        public static void PreviewRemove(Pawn slave, Apparel app)
        {
            if (slave?.apparel == null || app == null) return;
            try
            {
                if (slave.apparel.WornApparel.Contains(app)) slave.apparel.Remove(app);
                if (!app.Destroyed) app.Destroy();
            }
            catch { }
        }

        /// <summary>Detach a worn apparel without dropping/destroying it (bypasses RJW's drop-lock). The caller
        /// keeps the reference - used by the dress-up window to stash conflicting gear for restore-or-commit.</summary>
        public static bool BypassRemoveWorn(Pawn p, Apparel app)
        {
            if (p?.apparel == null || app == null) return false;
            try { if (p.apparel.WornApparel.Contains(app)) { p.apparel.Remove(app); return true; } } catch { }
            return false;
        }

        /// <summary>Re-wear a stashed apparel instance with RJW's on_wear suppressed, so a locked device keeps
        /// its existing stamp/key and no duplicate key is minted on restore.</summary>
        public static void RewearStashed(Pawn p, Apparel app)
        {
            if (p?.apparel == null || app == null || app.Destroyed) return;
            try
            {
                if (app.Wearer == p) return;
                Dialog_DressUp.Previewing = true;
                p.apparel.Wear(app, false, false);
                Dialog_DressUp.Previewing = false;
            }
            catch { Dialog_DressUp.Previewing = false; }
        }

        /// <summary>Drop a stashed (detached) apparel at the slave's feet so the player keeps it.</summary>
        public static void DropDisplacedApparel(Pawn slave, Apparel app)
        {
            if (app == null || app.Destroyed) return;
            try
            {
                if (app.Wearer != null) BypassRemoveWorn(app.Wearer, app);
                if (slave?.MapHeld != null && !app.Spawned)
                    GenPlace.TryPlaceThing(app, slave.PositionHeld, slave.MapHeld, ThingPlaceMode.Near);
            }
            catch { }
        }

        /// <summary>Destroy a displaced locked device and purge every Holokey that matches its stamp from all
        /// inventories on the map - so overriding a locked device leaves no orphan keys behind.</summary>
        public static void PurgeDeviceAndKey(Pawn slave, Apparel device)
        {
            if (device == null) return;
            try
            {
                if (device.Wearer != null) BypassRemoveWorn(device.Wearer, device);
                var comp = device.TryGetComp<rjw.CompHoloCryptoStamped>();
                var map = slave?.MapHeld;
                if (comp != null && map != null)
                {
                    var pawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        var inv = pawns[i].inventory?.innerContainer;
                        if (inv == null) continue;
                        for (int k = inv.Count - 1; k >= 0; k--)
                        {
                            var kc = inv[k].TryGetComp<rjw.CompHoloCryptoStamped>();
                            if (kc != null && kc.matches(comp)) inv[k].Destroy();
                        }
                    }
                }
                if (!device.Destroyed) device.Destroy();
            }
            catch { }
        }

        private static bool ConflictsWithLocked(Pawn victim, ThingDef newDef)
        {
            var worn = victim.apparel?.WornApparel;
            if (worn == null) return false;
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].TryGetComp<rjw.CompHoloCryptoStamped>() != null
                    && !ApparelUtility.CanWearTogether(newDef, worn[i].def, victim.RaceProps.body)) return true;
            return false;
        }

        // ── Whoring: the owner pimps the slave out to a visitor for silver ──
        public static void StartWhore(Pawn owner, Pawn slave)
        {
            if (owner == null || slave?.jobs == null || !slave.Spawned) return;
            var client = PickWhoreClient(slave);
            if (client == null)
            {
                if (InvolvesPlayerPawn(owner, slave))
                    Messages.Message("No willing visitor nearby for " + slave.LabelShort + " to whore to.", new LookTargets(slave), MessageTypeDefOf.RejectInput, false);
                return;
            }
            // Note who gets paid, then send the slave over to approach and proposition the client. The act AND
            // the payment only happen if the attempt succeeds (rolled on arrival, paid post-sex).
            var sp = GameComponent_Harassment.Instance?.GetProfile(slave);
            if (sp != null) sp.whoreOwnerId = owner.thingIDNumber;
            TryAddMoodThought(slave, "RJWSH_ForcedWhore");
            slave.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Whore, client), JobCondition.InterruptForced);
        }

        /// <summary>Rolled on arrival: the slave propositions the client. Success -> the service act plays out
        /// (the owner is paid afterward in Aftersex); failure -> the client declines and nobody is paid.</summary>
        public static void ResolveWhoreAttempt(Pawn slave, Pawn client)
        {
            if (slave == null || client == null) return;
            var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
            Pawn owner = (sp != null && sp.whoreOwnerId >= 0) ? FindPawnByIdAnyMap(sp.whoreOwnerId) : null;
            if (!client.Spawned || client.Dead || client.Downed)
            {
                if (sp != null) sp.whoreOwnerId = -1;
                return;
            }
            if (Rand.Chance(WhoreSuccessChance(slave, client)))
            {
                RunService(slave, client, null); // the act plays out; payment happens post-sex (whoreOwnerId stays set)
            }
            else
            {
                if (sp != null) sp.whoreOwnerId = -1;
                FireBegLine(slave, RJWSH_InteractionDefOf.RJWSH_Flirt, client); // a failed come-on
                if (InvolvesPlayerPawn(owner, slave))
                    Messages.Message(client.LabelShort + " turned down " + slave.LabelShort + ".",
                        new LookTargets(slave), MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>Odds a client takes the slave up on the offer, scaled by the slave's looks/social/experience
        /// and the client's own need.</summary>
        private static float WhoreSuccessChance(Pawn slave, Pawn client)
        {
            float c = 0.5f;
            try { c += slave.GetStatValue(StatDefOf.PawnBeauty) * 0.12f; } catch { }
            try { c += ((slave.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) - 5) / 40f; } catch { }
            try { if (xxx.is_whore(slave)) c += 0.15f; } catch { }
            try { if (xxx.need_some_sex(client) > 0f) c += 0.15f; } catch { }
            var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
            if (sp != null && sp.IsConditioned) c += 0.1f;
            return c < 0.1f ? 0.1f : (c > 0.9f ? 0.9f : c);
        }

        /// <summary>Post-sex: if either participant was whoring, pay their owner now that the act is finished.</summary>
        public static void TryPayWhore(rjw.SexProps props)
        {
            if (props == null) return;
            // Mark the client satisfied BEFORE payment clears the whoring flag - a happy guest talks the colony up.
            MarkSatisfiedClient(props.pawn, props.partner);
            MarkSatisfiedClient(props.partner, props.pawn);
            TryPayWhoreFor(props.pawn);
            TryPayWhoreFor(props.partner);
        }

        /// <summary>If `slave` was the whoring pet in this act, flag the `client` (a non-hostile visitor) as a
        /// satisfied customer who will spread word of the colony's "hospitality" when they leave.</summary>
        private static void MarkSatisfiedClient(Pawn slave, Pawn client)
        {
            var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
            if (sp == null || sp.whoreOwnerId < 0) return;               // this side was not the whoring pet
            if (client == null || client.Faction == null || client.Faction.IsPlayer) return;
            if (client.HostileTo(Faction.OfPlayer)) return;
            var cp = GameComponent_Harassment.Instance?.GetProfile(client);
            if (cp != null) cp.satisfiedClient = true;
        }

        /// <summary>On departure, a satisfied client may improve their faction's opinion of the colony - the
        /// brothel's reputation spreading by word of mouth. Called from the ExitMap patch.</summary>
        public static void TrySatisfiedClientGossip(Pawn leaver)
        {
            var cp = GameComponent_Harassment.Instance?.GetProfileIfExists(leaver);
            if (cp == null || !cp.satisfiedClient) return;
            cp.satisfiedClient = false;
            if (leaver.Faction == null || leaver.Faction.IsPlayer || leaver.HostileTo(Faction.OfPlayer)) return;
            if (!Rand.Chance(0.5f)) return;
            try
            {
                leaver.Faction.TryAffectGoodwillWith(Faction.OfPlayer, 2, false, false, null, null);
                Messages.Message(leaver.LabelShortCap + " left satisfied and will speak well of the colony's hospitality.",
                    new LookTargets(leaver), MessageTypeDefOf.PositiveEvent, false);
            }
            catch { }
        }
        private static void TryPayWhoreFor(Pawn slave)
        {
            var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
            if (sp == null || sp.whoreOwnerId < 0) return;
            var owner = FindPawnByIdAnyMap(sp.whoreOwnerId);
            sp.whoreOwnerId = -1;
            if (owner != null) PayOwner(owner, slave);
        }

        // ── Affection: consensual / content-pet kisses + hand-holding ─────────────
        public static bool AreLovers(Pawn a, Pawn b)
        {
            try
            {
                return a?.relations != null && b != null
                    && (a.relations.DirectRelationExists(PawnRelationDefOf.Lover, b)
                        || a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b));
            }
            catch { return false; }
        }

        public static bool IsAffectionOwner(Pawn owner, Pawn pet)
        {
            var pp = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            return owner != null && pp != null
                && (pp.relationshipOwnerId == owner.thingIDNumber || pp.ownerId == owner.thingIDNumber);
        }

        /// <summary>True when the two pawns are bound in an owner-pet relationship (either direction). Used to
        /// suppress right-click romance options - the power dynamic precludes courtship.</summary>
        public static bool AreOwnerPet(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b) return false;
            return IsAffectionOwner(a, b) || IsAffectionOwner(b, a);
        }

        /// <summary>Willingness (0..1) of `a` to share affection with `b`. Consensual romance is very likely; an
        /// owner-pet bond depends on the pet's conditioning (resentful pets rarely, devoted ones often); friends
        /// with high opinion occasionally.</summary>
        public static float AffectionWillingness(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b) return 0f;
            if (AreLovers(a, b)) return 0.9f;
            bool aOwnsB = IsAffectionOwner(a, b);
            bool bOwnsA = IsAffectionOwner(b, a);
            if (aOwnsB || bOwnsA)
            {
                var pet = aOwnsB ? b : a;
                var pp = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
                if (pp == null) return 0.05f;
                if (IsFullyConditioned(pet)) return 0.8f;   // devoted
                if (pp.IsConditioned) return 0.45f;          // content / resigned
                return 0.05f;                                // resentful - very rarely
            }
            int op = 0; try { op = a.relations?.OpinionOf(b) ?? 0; } catch { }
            if (op >= 20) return 0.3f;
            return 0f;
        }

        /// <summary>Only let affection start when a pawn is at leisure (wandering or joy), so it never interrupts work.</summary>
        public static bool IsFreeForAffection(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || !p.Awake() || p.Drafted) return false;
            try { if (p.InMentalState) return false; } catch { }
            if (IsBusyInAct(p)) return false;
            var jd = p.CurJobDef;
            if (jd == null) return true;
            return jd == JobDefOf.Wait || jd == JobDefOf.Wait_Wander || jd == JobDefOf.GotoWander
                || jd == JobDefOf.Wait_MaintainPosture
                || (p.CurJob != null && p.CurJob.def.joyKind != null);
        }

        /// <summary>Nearest reachable, at-leisure, off-cooldown pawn that `a` is in good enough standing with.</summary>
        public static Pawn FindAffectionPartner(Pawn a)
        {
            if (a?.Map == null) return null;
            int now = Find.TickManager.TicksGame;
            Pawn best = null; float bestW = 0.05f;
            var pawns = a.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var b = pawns[i];
                if (b == a || !b.RaceProps.Humanlike) continue;
                if (a.Position.DistanceTo(b.Position) > 8f) continue;
                if (!IsFreeForAffection(b)) continue;
                var bp = GameComponent_Harassment.Instance?.GetProfileIfExists(b);
                if (bp != null && now < bp.affectionCooldownTick) continue;
                float w = AffectionWillingness(a, b);
                if (w <= bestW) continue;
                if (!a.CanReach(b, PathEndMode.Touch, Danger.None)) continue;
                bestW = w; best = b;
            }
            return best;
        }

        public static void TriggerAffection(Pawn actor, Pawn partner, AffectionKind kind)
        {
            if (actor?.jobs == null || partner == null || !actor.Spawned || !partner.Spawned || actor.Map != partner.Map) return;
            int now = Find.TickManager.TicksGame;
            var ap = GameComponent_Harassment.Instance?.GetProfile(actor);
            var bp = GameComponent_Harassment.Instance?.GetProfile(partner);
            if (ap != null) ap.affectionCooldownTick = now + 12000;
            if (bp != null) bp.affectionCooldownTick = now + 12000;
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Affection, partner);
            job.count = (int)kind;
            actor.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public static void OnAffectionStart(Pawn a, Pawn b, AffectionKind kind)
        {
            if (a == null || b == null) return;
            var def = kind == AffectionKind.Kiss ? RJWSH_InteractionDefOf.RJWSH_Kiss : RJWSH_InteractionDefOf.RJWSH_HoldHands;
            try { FireFlavorLine(a, b, def); } catch { }
            try
            {
                ApplyThought(b, a, RJWSH_ThoughtDefOf.RJWSH_TenderMoment);
                ApplyThought(a, b, RJWSH_ThoughtDefOf.RJWSH_TenderMoment);
                FABridge.PlayFace(a, "RJWSH_FA_Bliss"); // FA: soft, happy expression for the tender beat
                FABridge.PlayFace(b, "RJWSH_FA_Bliss");
            }
            catch { }
            AffectionTick(a, b, kind);
        }

        public static void AffectionTick(Pawn a, Pawn b, AffectionKind kind)
        {
            if (a?.Map == null || b == null) return;
            try
            {
                if (kind == AffectionKind.HoldHands)
                {
                    var mid = (a.DrawPos + b.DrawPos) / 2f;
                    MoteMaker.MakeStaticMote(mid, a.Map, RJWSH_ThingDefOf.RJWSH_Mote_Hands, 0.7f);
                }
                else
                {
                    FleckMaker.ThrowMetaIcon(a.Position, a.Map, FleckDefOf.Heart);
                    FleckMaker.ThrowMetaIcon(b.Position, b.Map, FleckDefOf.Heart);
                }
            }
            catch { }
        }

        /// <summary>Post-sex hook: lovers / devoted pets may share a brief cuddle (kiss or hand-hold) afterward.</summary>
        public static void TryAfterSexCuddle(rjw.SexProps props)
        {
            if (props == null) return;
            Pawn a = props.pawn, b = props.partner;
            if (a == null || b == null || a == b || !a.Spawned || !b.Spawned || a.Map != b.Map) return;
            float w = AffectionWillingness(a, b);
            if (w <= 0.05f || !Rand.Chance(w * 0.6f)) return;
            TriggerAffection(a, b, Rand.Bool ? AffectionKind.Kiss : AffectionKind.HoldHands);
        }

        private static Pawn PickWhoreClient(Pawn slave)
        {
            var map = slave.Map; if (map == null) return null;
            Pawn best = null; float bestDist = 9999f;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == slave || p.Dead || p.Downed || !p.RaceProps.Humanlike) continue;
                if (Categorize(p) != PawnCategory.Visitor) continue; // a non-hostile guest
                if (IsBusyInAct(p)) continue;
                if (!GenderOk(slave, p)) continue; // heterosexual-only gate
                try { if (!xxx.can_fuck(p) && !xxx.can_be_fucked(p)) continue; } catch { continue; }
                float d = slave.Position.DistanceTo(p.Position);
                if (d < bestDist && slave.CanReach(p, PathEndMode.Touch, Danger.Some)) { bestDist = d; best = p; }
            }
            return best;
        }

        private static void PayOwner(Pawn owner, Pawn slave)
        {
            try
            {
                int amount = Rand.RangeInclusive(20, 45);
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = amount;
                bool given = owner?.inventory != null && owner.Spawned && !owner.Dead && owner.inventory.innerContainer.TryAdd(silver, true);
                if (!given) GenPlace.TryPlaceThing(silver, (owner?.Spawned == true ? owner : slave).Position, (owner?.Spawned == true ? owner : slave).Map, ThingPlaceMode.Near);
                AddEarnings(slave, amount);
                BrothelBridge.RecordWhoring(slave, amount); // surface forced sessions in Brothel Colony's whoring tab
                if (InvolvesPlayerPawn(owner, slave))
                    Messages.Message(owner.LabelShort + " earned " + amount + " silver pimping out " + slave.LabelShort + ".",
                        new LookTargets(slave), MessageTypeDefOf.PositiveEvent, false);
            }
            catch { }
        }

        // Victim fight-back: a collared/owned pawn tries to claw back willpower. Success lowers conditioning;
        // failure deepens it and the owner punishes them (a beating and maybe a timed spell in a public onahole).
        public static void AttemptFightBack(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null || victim == null) return;
            vp.resistCooldownTick = Find.TickManager.TicksGame + 2500;
            var owner = FindPawnByIdAnyMap(vp.ownerId);

            // The physical struggle: a real unarmed social-fight brawl with the owner (mental-break style;
            // Melee Animation animates it). Both pawns trade blows until one yields.
            bool scuffled = StartScuffle(victim, owner);

            float will = Mathf01(1f - vp.hypnosisLevel / 100f);
            float fear = Mathf01(1f - vp.rapport / 100f);   // whip-broken (low-rapport) pets stay volatile even when conditioned
            float grit = 0.08f + (victim.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) * 0.012f;
            float chance = victim.Downed ? 0.05f : Mathf01(will * 0.5f + fear * 0.22f + grit + EscapeWindowBonus(victim));

            if (Rand.Chance(chance))
            {
                vp.hypnosisLevel = System.Math.Max(0f, vp.hypnosisLevel - 15f);
                if (vp.hypnosisLevel <= 0f) vp.aiControlled = false; // fully resisted -> no longer AI-driven
                // Standing up to their owner rebuilds will, spirit and self-worth, and asserts dominance.
                AttrDelta(victim, will: 8f, spirit: 6f, esteem: 5f, subdom: 8f);
                ThrowControlMote(victim, "!", new UnityEngine.Color(1f, 0.85f, 0.3f));
                if (vp.hypnosisLevel <= 0f && GetBreakStage(victim, vp) >= BreakStage.Devoted) DepthOnPetLost(victim, vp.ownerId);
                if (vp.hypnosisLevel <= 0f && InvolvesPlayerPawn(owner, victim))
                    try { Find.LetterStack.ReceiveLetter(victim.LabelShortCap + " broke free",
                        victim.LabelShortCap + " found the will to throw off the conditioning entirely. They are their own person again.",
                        LetterDefOf.PositiveEvent, new LookTargets(victim)); }
                    catch { }
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(victim.LabelShort + " fought back and clawed back some willpower.",
                        new LookTargets(victim), MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                vp.ApplyCond("Punished defiance", 6f, -4f); // punished defiance breeds resentment
                TryAddMoodThought(victim, "RJWSH_Disciplined");
                // If no brawl broke out (owner away / already busy), the owner instead comes over to beat them.
                if (!scuffled && owner != null && owner.Spawned) StartDiscipline(owner, victim);
                if (owner != null && Rand.Chance(0.5f)) TimedOnaholePunish(owner, victim, 7500); // ~3 in-game hours
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(victim.LabelShort + "'s defiance was punished.",
                        new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            }
        }

        /// <summary>Situational fight-back modifier from colony state (the escape window). The chaos of an
        /// active raid and an absent/asleep captor give a real chance to break for it; isolation from every
        /// ally saps hope and makes fighting back harder.</summary>
        public static float EscapeWindowBonus(Pawn victim)
        {
            if (victim?.Map == null) return 0f;
            float bonus = 0f;
            try
            {
                if (RaidChaosActive(victim.Map)) bonus += 0.2f;              // captors are busy with the raid
                var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
                var owner = (vp != null && vp.ownerId >= 0) ? FindPawnByIdAnyMap(vp.ownerId) : null;
                if (owner != null && (owner.Downed || !owner.Awake())) bonus += 0.15f; // the captor can't react
                if (!AnyAllyNear(victim, 18f)) bonus -= 0.12f;                // no one to run to - hope fades
            }
            catch { }
            return bonus;
        }

        /// <summary>True while a hostile threat is actively rampaging on the map (a raid in progress).</summary>
        public static bool RaidChaosActive(Map map)
        {
            try { return map != null && GenHostility.AnyHostileActiveThreatToPlayer(map); } catch { return false; }
        }

        /// <summary>Applies a trait's attribute effect (sign +1 when gained, -1 when lost). Only shifts pawns
        /// whose sexual attributes already exist and are seeded - generation-time traits are handled by
        /// SeedFrom, so this fires only for genuine mid-game trait CHANGES on tracked pawns.</summary>
        public static void TraitAttributeEffect(Pawn pawn, string defName, float sign)
        {
            if (pawn == null || defName.NullOrEmpty()) return;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
            if (prof?.sex == null || !prof.sex.seeded) return;
            switch (defName)
            {
                case "Masochist": AttrDelta(pawn, trauma: -15f * sign, addiction: 15f * sign, subdom: -20f * sign); break;
                case "RJWSH_StockholmSyndrome": AttrDelta(pawn, will: -25f * sign, esteem: -10f * sign, subdom: -30f * sign); break;
                case "Nymphomaniac": AttrDelta(pawn, addiction: 40f * sign); break;
                case "Ascetic": AttrDelta(pawn, addiction: -25f * sign); break;
                case "Bloodlust": AttrDelta(pawn, subdom: 30f * sign); break;
                case "Psychopath": AttrDelta(pawn, subdom: 15f * sign, trauma: -10f * sign); break;
                case "Sadist":
                case "RJWSH_Sadist": AttrDelta(pawn, subdom: 40f * sign); break;
                case "Kind": AttrDelta(pawn, esteem: 8f * sign, subdom: -10f * sign); break;
                case "Wimp": AttrDelta(pawn, will: -15f * sign, esteem: -10f * sign); break;
                case "Tough": AttrDelta(pawn, will: 12f * sign, spirit: 8f * sign); break;
                case "Beautiful":
                case "Pretty": AttrDelta(pawn, esteem: 12f * sign); break;
                case "Ugly":
                case "Staggeringlyugly": AttrDelta(pawn, esteem: -12f * sign); break;
                case "Abrasive": AttrDelta(pawn, subdom: 8f * sign); break;
            }
        }

        /// <summary>Karma as an active driver: a pawn's karma slowly shapes their attributes over time. Good
        /// karma (respected/virtuous) buoys self-esteem and steadies will; bad karma (cruel/depraved) hardens
        /// them into dominants and breeds craving. Only nudges pawns whose attributes are already tracked.
        /// No-ops without Karma &amp; Reputation installed.</summary>
        public static void KarmaDriftTick(Pawn pawn)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
            if (prof?.sex == null || !prof.sex.seeded) return;
            if (!KarmaBridge.TryGetKarma(pawn, out float k)) return;
            float kn = UnityEngine.Mathf.Clamp(k / 100f, -1f, 1f);
            AttrDelta(pawn,
                esteem: kn * 0.25f,                       // respected -> secure; infamous -> eroded self-worth
                will: kn * 0.12f,                          // virtue steadies resolve
                subdom: -kn * 0.18f,                       // the cruel harden into dominants
                addiction: kn < 0f ? -kn * 0.15f : 0f);    // depravity breeds craving
        }

        /// <summary>During an active raid, the fear and violence quietly build trauma (and chip willpower) for
        /// the colony's pawns. Called on the slow scan cadence while a raid is in progress.</summary>
        public static void RaidTraumaTick(Map map)
        {
            if (map == null) return;
            var cols = map.mapPawns.FreeColonistsAndPrisonersSpawned;
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                if (c == null || c.Dead || c.RaceProps == null || !c.RaceProps.Humanlike) continue;
                // Those caught in the fighting (drafted / downed / hurt) take it harder.
                float t = (c.Downed || c.Drafted || (c.health != null && c.health.summaryHealth.SummaryHealthPercent < 0.85f)) ? 1.2f : 0.4f;
                AttrDelta(c, trauma: t, will: -0.2f);
            }
        }

        /// <summary>Any awake, standing free colonist within radius of the pawn - someone they could run to.</summary>
        public static bool AnyAllyNear(Pawn victim, float radius)
        {
            var map = victim?.Map;
            if (map == null) return false;
            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c == victim || c.Dead || c.Downed) continue;
                if (victim.Position.DistanceTo(c.Position) <= radius) return true;
            }
            return false;
        }

        /// <summary>A collared pawn kept away from every ally slowly conditions faster - isolation erodes hope.
        /// Called on the conditioning cadence.</summary>
        public static void IsolationConditioningTick(Pawn p)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (vp == null || vp.hypnosisLevel < 5f || vp.hypnosisLevel >= 100f) return;
            if (AnyAllyNear(p, 18f)) return;   // an ally within sight keeps hope alive; alone, the hold deepens
            vp.hypnosisLevel = System.Math.Min(100f, vp.hypnosisLevel + 1f);
        }

        /// <summary>Starts a brief unarmed social-fight brawl between the pawn and their owner - the physical
        /// side of fighting back. Returns true only if the fight actually broke out.</summary>
        private static bool StartScuffle(Pawn victim, Pawn owner)
        {
            if (victim?.jobs == null || owner == null || !owner.Spawned || !victim.Spawned || owner == victim) return false;
            if (owner.Map != victim.Map || owner.Downed || victim.Downed) return false;
            if (owner.InMentalState || IsBusyInAct(owner) || IsBusyInAct(victim)) return false;
            if (!victim.CanReach(owner, PathEndMode.Touch, Danger.Deadly)) return false;
            // A scripted 1v1 brawl job (NOT a social-fight mental state, which re-targets + drags in bystanders).
            victim.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Scuffle, owner), JobCondition.InterruptForced);
            return victim.CurJobDef == RJWSH_JobDefOf.RJWSH_Scuffle;
        }

        /// <summary>One capped, non-lethal unarmed strike for the scripted scuffle (driven by JobDriver_Scuffle).</summary>
        public static void ScuffleStrike(Pawn striker, Pawn target) => ForceMelee(striker, target);

        /// <summary>Force-ends a capped fight-back scuffle, recovering both pawns from the social fight.</summary>
        public static void EndScuffle(Pawn p)
        {
            try
            {
                if (p?.MentalState is Verse.AI.MentalState_SocialFighting sf)
                {
                    var other = sf.otherPawn;
                    p.MentalState.RecoverFromState();
                    if (other?.MentalState is Verse.AI.MentalState_SocialFighting) other.MentalState.RecoverFromState();
                }
            }
            catch { }
        }

        /// <summary>True once the slave is broken in: has Stockholm Syndrome, or conditioning is essentially maxed.</summary>
        public static bool IsFullyConditioned(Pawn p)
        {
            if (p?.story?.traits != null)
            {
                var st = DefDatabase<TraitDef>.GetNamedSilentFail("RJWSH_StockholmSyndrome");
                if (st != null && p.story.traits.HasTrait(st)) return true;
            }
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            return vp != null && vp.hypnosisLevel >= 90f;
        }

        /// <summary>Starts a forced RJW rape with the given rapist on the given victim. Returns false if it can't.</summary>
        private static bool TryForceRape(Pawn rapist, Pawn victim)
        {
            try
            {
                if (rapist?.jobs == null || victim == null || !victim.Spawned) return false;
                if (!RJWSettings.rape_enabled) return false;
                if (!xxx.can_rape(rapist, true)) return false;
                if (!xxx.can_be_fucked(victim)) return false;
                rapist.jobs.StartJob(JobMaker.MakeJob(xxx.RapeRandom, victim), JobCondition.InterruptForced);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Readout gizmo on an onahole-bound pawn showing time left before they beg to be let out.</summary>
        public static Gizmo BuildOnaholeTimerGizmo(Pawn p)
        {
            if (!IsInOnaholeBed(p)) return null;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            int now = Find.TickManager.TicksGame;
            string label = (vp != null && vp.onaholeReleaseTick > now)
                ? "In onahole: " + (vp.onaholeReleaseTick - now).ToStringTicksToPeriod() + " left"
                : "In onahole: begging to be let out";
            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = "How long " + p.LabelShort + " has in the onahole before they start begging their owner to be let out. Free them with the matching key.",
                icon = HarassmentTextures.AutoService,
                action = delegate { }
            };
        }

        /// <summary>An onahole-bound slave cries to be let out once their time is up.</summary>
        public static void BegOwnerForRelease(Pawn slave, Pawn owner)
        {
            if (slave == null || !slave.Spawned) return;
            // The plea is shown as a speech bubble (FireBegLine), not a top-left message.
            FireBegLine(slave, RJWSH_InteractionDefOf.RJWSH_BegHelp, owner);
        }

        /// <summary>Nearest awake humanlike within earshot to address a cry to (prefers a given pawn, e.g. the owner).</summary>
        private static Pawn NearestBegListener(Pawn victim, Pawn prefer)
        {
            if (victim?.Map == null) return null;
            if (prefer != null && prefer.Spawned && prefer.Map == victim.Map && !prefer.Dead && prefer.Awake()
                && victim.Position.DistanceTo(prefer.Position) <= 24f) return prefer;
            Pawn best = null; float bd = 24f;
            var pawns = victim.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c == victim || c.Dead || !c.RaceProps.Humanlike || !c.Awake()) continue;
                float dd = victim.Position.DistanceTo(c.Position);
                if (dd <= bd) { bd = dd; best = c; }
            }
            return best;
        }

        /// <summary>Makes the victim cry out as a real interaction so it shows a speech bubble. A normal
        /// TryInteractWith is tried first (SpeakUp voices it), but an onahole-bound pawn is lying in the bed
        /// and may be refused, so we then log the interaction entry directly - the bubble systems still render
        /// it. Falls back to a floating mote only if nobody is in earshot.</summary>
        public static void FireBegLine(Pawn victim, InteractionDef def, Pawn prefer = null)
        {
            if (victim?.Map == null || !victim.Spawned || def == null) return;
            var listener = NearestBegListener(victim, prefer);
            if (listener == null) { ThrowBegMote(victim); return; }
            try
            {
                AllowImmediateInteraction(victim);
                if (victim.interactions == null || !victim.interactions.TryInteractWith(listener, def))
                    Find.PlayLog.Add(new PlayLogEntry_Interaction(def, victim, listener, null));
            }
            catch { ThrowBegMote(victim); }
        }

        private static void TimedOnaholePunish(Pawn owner, Pawn victim, int durationTicks)
        {
            bool ok = (S.enableOnaholeCapture && SoftDeps.OnaholeActive && DoOnaholeCapture(owner, victim)) || DoBoundInPublic(owner, victim);
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (ok)
            {
                if (vp != null) vp.onaholeReleaseTick = Find.TickManager.TicksGame + durationTicks;
            }
            else
            {
                // Guaranteed fallback: neither onahole nor bound-in-public could start (mod not installed,
                // no valid public cell, etc.). Strip and humiliate the pet in-place — no job, no map cell,
                // no external deps required.
                try
                {
                    if (victim != null && victim.Spawned)
                    {
                        if (vp != null) vp.forceNudity = true;
                        StripToBondage(victim); // physically strip non-bondage clothing right now
                        TryAddMoodThought(victim, "RJWSH_Humiliated");
                        if (vp != null) vp.ApplyCond("Humiliation punishment (stripped)", 2f, -2f);
                        if (InvolvesPlayerPawn(owner, victim))
                            Messages.Message(
                                (owner != null ? owner.LabelShort : "The owner") +
                                " stripped " + victim.LabelShort + " bare as punishment for defiance.",
                                new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.WarningOnce("[RJW Sexual Harassment] humiliation fallback failed: " + ex.Message, 0x5A1349);
                }
            }
        }

        /// <summary>"Fight back" gizmo on a player-side collared/owned pawn.</summary>
        public static Gizmo BuildFightBackGizmo(Pawn p)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (vp == null) return null;
            if (!WearingControlCollar(p) && vp.ownerId < 0) return null;
            bool playerSide = (p.Faction != null && p.Faction.IsPlayer) || p.IsPrisonerOfColony || p.IsSlaveOfColony;
            if (!playerSide) return null;
            var cmd = new Command_Action
            {
                defaultLabel = "Fight back",
                defaultDesc = "Pick a fight with the owner to resist the conditioning and claw back willpower - a real unarmed scuffle breaks out. More likely to work the less conditioned they are; if it fails, conditioning deepens and they may be dragged to a public onahole.",
                icon = HarassmentTextures.FightBack,
                action = delegate { AttemptFightBack(p); }
            };
            if (Find.TickManager.TicksGame < vp.resistCooldownTick) cmd.Disable("Recovering");
            else if (p.Downed) cmd.Disable("Downed");
            return cmd;
        }

        /// <summary>Auto-resist toggle next to the fight-back gizmo: keep trying whenever the cooldown is ready.</summary>
        public static Gizmo BuildAutoResistGizmo(Pawn p)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (vp == null) return null;
            if (!WearingControlCollar(p) && vp.ownerId < 0) return null;
            bool playerSide = (p.Faction != null && p.Faction.IsPlayer) || p.IsPrisonerOfColony || p.IsSlaveOfColony;
            if (!playerSide) return null;
            return new Command_Toggle
            {
                defaultLabel = "Auto-resist",
                defaultDesc = "Automatically attempt to fight back whenever the cooldown is ready.",
                icon = HarassmentTextures.FightBack,
                isActive = () => vp.autoResist,
                toggleAction = delegate { vp.autoResist = !vp.autoResist; }
            };
        }

        public static void ComeHere(Pawn owner, Pawn victim)
        {
            if (owner == null || victim?.jobs == null || !victim.Spawned) return;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp != null) vp.stayCell = IntVec3.Invalid;
            victim.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, owner.Position), JobCondition.InterruptForced);
        }

        public static void SetStay(Pawn victim, IntVec3 cell)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null || victim?.jobs == null) return;
            vp.stayCell = cell;
            vp.followOwner = false;
            victim.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_StayPut, cell), JobCondition.InterruptForced);
        }

        public static void OpenHandOverMenu(Pawn owner, Pawn victim, Thing keyThing)
        {
            var opts = new List<FloatMenuOption>();
            var map = owner?.Map;
            if (map != null)
            {
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p == owner || p == victim || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike || p.inventory == null) continue;
                    if (owner.Position.DistanceTo(p.Position) > 14f) continue;
                    if (!owner.CanReach(p, PathEndMode.Touch, Danger.Deadly)) continue;
                    Pawn rp = p;
                    string tag = Evilness(p) > 0.7f ? " (cruel - takes over)" : "";
                    opts.Add(new FloatMenuOption(p.LabelShortCap + tag, delegate { StartDeliverKey(owner, rp, victim, keyThing, false); }));
                }
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No one within reach to take the key", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        /// <summary>Sends the owner walking to the recipient to physically deliver the key before control
        /// transfers. isCopy=true mints a duplicate on arrival; isCopy=false hands the actual key over.
        /// The delivery walk means collar control never teleports across the map.</summary>
        public static void StartDeliverKey(Pawn owner, Pawn recipient, Pawn victim, Thing keyThing, bool isCopy)
        {
            if (owner?.jobs == null || recipient == null || victim == null || keyThing == null) return;
            if (!owner.Spawned || !recipient.Spawned || !owner.CanReach(recipient, PathEndMode.Touch, Danger.Deadly))
            {
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(owner.LabelShort + " cannot reach " + recipient.LabelShort + " to hand over the key.",
                        new LookTargets(recipient), MessageTypeDefOf.RejectInput, false);
                return;
            }
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_DeliverKey, recipient, victim, keyThing);
            job.count = isCopy ? 1 : 0;
            job.playerForced = true;
            owner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            if (InvolvesPlayerPawn(owner, victim))
                Messages.Message(owner.LabelShort + " is taking " + victim.LabelShort + "'s " + (isCopy ? "copied key" : "key") + " to " + recipient.LabelShort + ".",
                    new LookTargets(new[] { owner, recipient }), MessageTypeDefOf.SilentInput, false);
        }

        /// <summary>On-arrival completion for a hand-over delivery: the actual key changes hands and control
        /// transfers (a cruel recipient seizes it as an AI controller).</summary>
        public static void CompleteHandOverKey(Pawn owner, Pawn recipient, Pawn victim, Thing keyThing)
        {
            if (owner?.inventory == null || recipient?.inventory == null || victim == null || keyThing == null) return;
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            if (owner.inventory.innerContainer.Contains(keyThing))
            {
                owner.inventory.innerContainer.Remove(keyThing);
                if (!recipient.inventory.innerContainer.TryAdd(keyThing, false))
                    GenPlace.TryPlaceThing(keyThing, recipient.Position, recipient.Map, ThingPlaceMode.Near);
            }
            RemoveOwnerRelation(owner, victim);
            if (Evilness(recipient) > 0.7f)
            {
                MarkAiControlled(victim, recipient);
                Messages.Message(recipient.LabelShort + " seized control of " + victim.LabelShort + "'s collar.",
                    new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            }
            else
            {
                vp.aiControlled = false;
                vp.ownerId = recipient.thingIDNumber;
                EnsureOwnerRelation(recipient, victim);
                Messages.Message(owner.LabelShort + " handed " + victim.LabelShort + "'s key to " + recipient.LabelShort + ".",
                    new LookTargets(victim), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>Offer to mint a duplicate collar key for another colonist, making them a co-owner (both then
        /// carry a matching key and both get the full control gizmo suite).</summary>
        public static void OpenCopyKeyMenu(Pawn owner, Pawn victim, Thing keyThing)
        {
            var opts = new List<FloatMenuOption>();
            var map = owner?.Map;
            if (map != null && keyThing != null)
            {
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p == owner || p == victim || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike || p.inventory == null) continue;
                    if (!IsPlayerOwned(p)) continue;
                    if (owner.Position.DistanceTo(p.Position) > 14f) continue;
                    if (!owner.CanReach(p, PathEndMode.Touch, Danger.Deadly)) continue;
                    Pawn rp = p;
                    opts.Add(new FloatMenuOption(p.LabelShortCap, delegate { StartDeliverKey(owner, rp, victim, keyThing, true); }));
                }
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No colonist within reach to receive a copy", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        /// <summary>On-arrival completion for a copy delivery: mint a duplicate key from the held key's stamp
        /// and give it to the recipient, making them a co-owner.</summary>
        public static void CompleteMintCopyKey(Pawn owner, Pawn recipient, Pawn victim, Thing keyThing)
        {
            try
            {
                var keyComp = keyThing?.TryGetComp<rjw.CompHoloCryptoStamped>();
                var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
                if (keyDef == null || recipient?.inventory == null || keyComp == null) return;
                var key = ThingMaker.MakeThing(keyDef);
                var kc = key.TryGetComp<rjw.CompHoloCryptoStamped>();
                if (kc != null) { kc.name = keyComp.name; kc.key = keyComp.key; }
                if (!recipient.inventory.innerContainer.TryAdd(key, true))
                    GenPlace.TryPlaceThing(key, recipient.Position, recipient.Map, ThingPlaceMode.Near);
                EnsureOwnerRelation(recipient, victim);
                Messages.Message(owner.LabelShort + " made " + recipient.LabelShort + " a co-owner of " + victim.LabelShort + ".",
                    new LookTargets(victim), MessageTypeDefOf.NeutralEvent, false);
            }
            catch { }
        }

        public static void FreeCollared(Pawn victim)
        {
            if (victim == null) return;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp != null)
            {
                // The owner grieves losing a devoted pet.
                if (GetBreakStage(victim, vp) >= BreakStage.Devoted) DepthOnPetLost(victim, vp.ownerId);
                vp.latentHypnosis = System.Math.Max(vp.latentHypnosis, vp.hypnosisLevel); // remember how broken they were
                // NOTE: relationshipOwnerId is deliberately KEPT so the owner/pet title persists after the collar
                // comes off; only the live control link (ownerId) is cleared.
                vp.ownerId = -1; vp.followOwner = false; vp.autoService = false;
                vp.aiControlled = false; vp.allowNeeds = false; vp.stayCell = IntVec3.Invalid;
                vp.cryptoName = null; vp.cryptoKey = null; // clear the lock stamp so a re-collar mints a fresh key
            }
            if (victim.apparel != null)
            {
                var worn = victim.apparel.WornApparel;
                for (int i = worn.Count - 1; i >= 0; i--)
                    if (worn[i].def == RJWSH_ThingDefOf.RJWSH_ControlCollar)
                    {
                        var col = worn[i];
                        victim.apparel.Remove(col);
                        if (!col.Destroyed) col.Destroy();
                    }
            }
            if (victim.jobs != null && victim.CurJobDef == RJWSH_JobDefOf.RJWSH_Follow)
                victim.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        // ---- Owner/slave relationship (vanilla social-tab title) ----

        public static void EnsureOwnerRelation(Pawn owner, Pawn slave)
        {
            if (S == null || !S.enableOwnerRelationship || owner == null || slave == null || owner == slave) return;
            if (owner.relations == null || slave.relations == null) return;
            var pet = RJWSH_RelationDefOf.RJWSH_RelPet;
            if (pet != null && !owner.relations.DirectRelationExists(pet, slave))
            {
                try { owner.relations.AddDirectRelation(pet, slave); } catch { }
            }
            // Persist the owner link so the social-tab title survives the collar coming off.
            var sp = GameComponent_Harassment.Instance?.GetProfile(slave);
            if (sp != null) sp.relationshipOwnerId = owner.thingIDNumber;
        }

        public static void RemoveOwnerRelation(Pawn owner, Pawn slave)
        {
            var pet = RJWSH_RelationDefOf.RJWSH_RelPet;
            if (pet == null || owner?.relations == null || slave == null) return;
            try { if (owner.relations.DirectRelationExists(pet, slave)) owner.relations.RemoveDirectRelation(pet, slave); } catch { }
        }

        /// <summary>Syncs the stored owner->pet relation with current ownership: strips stale ones and adds for
        /// current owners. The reverse "owner" label is implied live, so only the stored side needs reconciling.</summary>
        public static void ReconcileOwnerRelations(Map map)
        {
            if (map == null) return;
            var pet = RJWSH_RelationDefOf.RJWSH_RelPet;
            if (pet == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            // First, (re)establish ownership from key possession: whoever currently carries a locked pawn's
            // Holokey is their owner. AI-controlled collars keep their AI driver, so skip those.
            if (S != null && S.enableOwnerRelationship)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var v = pawns[i];
                    var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(v);
                    if (vp == null || vp.aiControlled || !IsLockedPawn(v)) continue;
                    var holder = FindKeyHolderFor(v);
                    if (holder != null && holder != v) EnsureOwnerRelation(holder, v);
                }
            }
            for (int i = 0; i < pawns.Count; i++)
            {
                var holder = pawns[i];
                if (holder.relations == null) continue;
                var rels = holder.relations.DirectRelations;
                for (int r = rels.Count - 1; r >= 0; r--)
                {
                    if (r >= rels.Count || rels[r].def != pet) continue;
                    var target = rels[r].otherPawn;
                    var tp = target != null ? GameComponent_Harassment.Instance?.GetProfileIfExists(target) : null;
                    bool valid = S != null && S.enableOwnerRelationship && tp != null && tp.relationshipOwnerId == holder.thingIDNumber;
                    if (!valid) { try { holder.relations.RemoveDirectRelation(pet, target); } catch { } }
                }
            }
            if (S == null || !S.enableOwnerRelationship) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                var slave = pawns[i];
                var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
                if (sp == null || sp.relationshipOwnerId < 0) continue;
                var owner = FindPawnByIdAnyMap(sp.relationshipOwnerId);
                if (owner != null && owner.relations != null && !owner.relations.DirectRelationExists(pet, slave))
                {
                    try { owner.relations.AddDirectRelation(pet, slave); } catch { }
                }
            }
        }

        /// <summary>Crude back-and-forth between an owner and their nearby pet: the owner degrades them and the
        /// pet submits a beat later (SpeakUp voices both as bubbles).</summary>
        public static void FireOwnerSlaveBanter(Pawn owner, Pawn slave)
        {
            if (S == null || !S.enableOwnerRelationship || owner == null || slave == null) return;
            if (!owner.Spawned || !slave.Spawned || owner.Map != slave.Map) return;
            if (owner.Position.DistanceTo(slave.Position) > 6f) return;
            if (IsBusyInAct(owner) || IsBusyInAct(slave)) return;
            var sp = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
            if (sp != null)
            {
                int nowt = Find.TickManager.TicksGame;
                if (nowt < sp.chatterCooldownTick) return;            // throttle ambient chatter (anti-spam)
                sp.chatterCooldownTick = nowt + Rand.Range(3000, 6000);
            }
            bool conditioned = sp != null && sp.IsConditioned;

            int pick = Rand.RangeInclusive(0, 2);
            if (pick == 1)
            {
                // Inspection: the owner looks the pet over; a broken pet averts their eyes, a defiant one glares.
                FireFlavorLine(owner, slave, RJWSH_InteractionDefOf.RJWSH_Inspect);
                MapComponent_HarassmentScan.ScheduleLine(slave, owner,
                    conditioned ? RJWSH_InteractionDefOf.RJWSH_SlaveSubmit : RJWSH_InteractionDefOf.RJWSH_PetDefiant, 140);
                FABridge.PlayFace(slave, conditioned ? "RJWSH_FA_Bliss" : "RJWSH_FA_Flinch");
                if (sp != null) sp.hypnosisLevel = System.Math.Min(100f, sp.hypnosisLevel + 0.5f);
            }
            else if (pick == 2)
            {
                // Test of obedience: a conditioned pet snaps to it; a volatile one may openly defy the order.
                FireFlavorLine(owner, slave, RJWSH_InteractionDefOf.RJWSH_TestObedience);
                if (conditioned)
                {
                    MapComponent_HarassmentScan.ScheduleLine(slave, owner, RJWSH_InteractionDefOf.RJWSH_SlaveSubmit, 130);
                    if (sp != null) sp.hypnosisLevel = System.Math.Min(100f, sp.hypnosisLevel + 0.5f);
                }
                else
                {
                    MapComponent_HarassmentScan.ScheduleLine(slave, owner, RJWSH_InteractionDefOf.RJWSH_PetDefiant, 130);
                    if (sp != null && sp.IsVolatile && Find.TickManager.TicksGame >= sp.resistCooldownTick && Rand.Chance(0.35f))
                        AttemptFightBack(slave); // pressured defiance from a fear-broken pet
                }
            }
            else
            {
                // The pet's reply tracks conditioning: a conditioned pet submits, an unconditioned one snaps back.
                FireFlavorLine(owner, slave, RJWSH_InteractionDefOf.RJWSH_OwnerDirty);
                MapComponent_HarassmentScan.ScheduleLine(slave, owner,
                    conditioned ? RJWSH_InteractionDefOf.RJWSH_SlaveSubmit : RJWSH_InteractionDefOf.RJWSH_PetDefiant, 140);
            }
        }

        /// <summary>Occasional ambient self-talk for a roaming collared/owned pet, chosen by conditioning:
        /// unconditioned pets seethe about escape/revenge, conditioned pets muse contentedly, fully conditioned
        /// pets gush devotion. Voiced toward a nearby pawn when one is in range, otherwise a short text mote.</summary>
        public static void FirePetSelfTalk(Pawn pet, PawnProfile prof)
        {
            if (pet == null || !pet.Spawned || prof == null) return;
            int nowt = Find.TickManager.TicksGame;
            if (nowt < prof.chatterCooldownTick) return;             // throttle ambient chatter (anti-spam)
            prof.chatterCooldownTick = nowt + Rand.Range(3000, 6000);
            bool full = IsFullyConditioned(pet);
            bool cond = prof.IsConditioned;
            InteractionDef def = full ? RJWSH_InteractionDefOf.RJWSH_PetDevoted
                               : (cond ? RJWSH_InteractionDefOf.RJWSH_PetContent
                                       : RJWSH_InteractionDefOf.RJWSH_PetDefiant);
            int ownerId = prof.relationshipOwnerId >= 0 ? prof.relationshipOwnerId : prof.ownerId;
            Pawn owner = ownerId >= 0 ? FindPawnByIdAnyMap(ownerId) : null;
            Pawn listener;
            if (cond || full)
            {
                bool ownerNear = owner != null && owner.Spawned && owner.Map == pet.Map
                                 && pet.Position.DistanceTo(owner.Position) <= 12f;
                listener = ownerNear ? owner : NearestListener(pet, null);
            }
            else
            {
                listener = NearestListener(pet, owner); // seethe to a peer, never to the owner's face
            }
            if (listener != null) FireFlavorLine(pet, listener, def);
            else ThrowSelfTalkMote(pet, full ? 2 : (cond ? 1 : 0));
        }

        /// <summary>Two collared/owned pets near each other commiserate: a conditioned one counsels acceptance,
        /// an unconditioned one shares defiance. A fully-broken "favorite" subtly deepens a resistant peer.</summary>
        public static void FireFellowPetBanter(Pawn petA, PawnProfile profA)
        {
            var map = petA?.Map;
            if (map == null || profA == null) return;
            int nowt = Find.TickManager.TicksGame;
            if (nowt < profA.chatterCooldownTick) return;            // throttle ambient chatter (anti-spam)
            profA.chatterCooldownTick = nowt + Rand.Range(3000, 6000);
            Pawn petB = null;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var b = pawns[i];
                if (b == petA || !b.RaceProps.Humanlike || b.Dead || !b.Awake() || b.Downed) continue;
                if (petA.Position.DistanceTo(b.Position) > 6f) continue;
                var bp = GameComponent_Harassment.Instance?.GetProfileIfExists(b);
                bool bOwned = bp != null && (bp.ownerId >= 0 || bp.relationshipOwnerId >= 0 || WearingControlCollar(b));
                if (!bOwned || IsBusyInAct(b)) continue;
                if (!petA.CanReach(b, PathEndMode.Touch, Danger.None)) continue;
                petB = b; break;
            }
            if (petB == null) return;
            FireFlavorLine(petA, petB, profA.IsConditioned ? RJWSH_InteractionDefOf.RJWSH_PetContent : RJWSH_InteractionDefOf.RJWSH_PetDefiant);
            var bp2 = GameComponent_Harassment.Instance?.GetProfileIfExists(petB);
            var reply = (bp2 != null && bp2.IsConditioned) ? RJWSH_InteractionDefOf.RJWSH_PetContent : RJWSH_InteractionDefOf.RJWSH_PetDefiant;
            MapComponent_HarassmentScan.ScheduleLine(petB, petA, reply, 130);
            if (IsFullyConditioned(petA) && bp2 != null && !bp2.IsConditioned)
                bp2.hypnosisLevel = System.Math.Min(100f, bp2.hypnosisLevel + 1f); // peer pressure from a devoted pet
        }

        /// <summary>A bound/onahole captive's cries may draw a decent, caring colonist over to comfort them (a
        /// protective bubble). They cannot free them without the key, but the colony visibly notices the cruelty.</summary>
        public static void TryDispatchRescuer(Pawn victim)
        {
            if (victim?.Map == null) return;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp == null || vp.IsConditioned) return;
            Pawn rescuer = null; float best = 20f;
            foreach (var c in victim.Map.mapPawns.FreeColonistsSpawned)
            {
                if (c == victim || c.Dead || c.Downed || !c.Awake() || c.Drafted) continue;
                try { if (c.InMentalState) continue; } catch { }
                if (IsBusyInAct(c)) continue;
                int op = 0; try { op = c.relations?.OpinionOf(victim) ?? 0; } catch { }
                if (op < 20 && !AreLovers(c, victim)) continue;   // only those who care
                if (Evilness(c) > 0.4f) continue;                 // a decent sort
                float d = victim.Position.DistanceTo(c.Position);
                if (d < best && c.CanReach(victim, PathEndMode.Touch, Danger.Some)) { best = d; rescuer = c; }
            }
            if (rescuer == null) return;
            rescuer.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, victim.Position), JobTag.Misc);
            FireFlavorLine(rescuer, victim, RJWSH_InteractionDefOf.RJWSH_WitnessProtective);
            if (InvolvesPlayerPawn(rescuer, victim))
                Messages.Message(rescuer.LabelShort + " heard " + victim.LabelShort + "'s cries and went to them.",
                    new LookTargets(new[] { rescuer, victim }), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>A passerby glances at a bound/displayed pet: a cruel one leers, a decent one pities. Rare and
        /// throttled by the glancer's chatter cooldown so it does not spam bubbles.</summary>
        public static void FireDisplayGlance(Pawn displayed)
        {
            var map = displayed?.Map;
            if (map == null) return;
            Pawn glancer = null; float bd = 8f;
            int now = Find.TickManager.TicksGame;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var w = pawns[i];
                if (w == displayed || !w.RaceProps.Humanlike || w.Dead || !w.Awake() || w.Downed) continue;
                if (IsBusyInAct(w)) continue;
                var wp = GameComponent_Harassment.Instance?.GetProfileIfExists(w);
                if (wp != null && now < wp.chatterCooldownTick) continue;
                float d = displayed.Position.DistanceTo(w.Position);
                if (d <= bd && GenSight.LineOfSight(displayed.Position, w.Position, map)) { bd = d; glancer = w; }
            }
            if (glancer == null) return;
            var gp = GameComponent_Harassment.Instance?.GetProfile(glancer);
            if (gp != null) gp.chatterCooldownTick = now + Rand.Range(3000, 6000);
            glancer.rotationTracker?.FaceTarget(displayed);
            FireFlavorLine(glancer, displayed, Evilness(glancer) > 0.5f
                ? RJWSH_InteractionDefOf.RJWSH_WitnessLeer : RJWSH_InteractionDefOf.RJWSH_WitnessProtective);
        }

        private static Pawn NearestListener(Pawn pet, Pawn exclude)
        {
            if (pet?.Map == null) return null;
            Pawn best = null; float bd = 14f;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c == pet || c == exclude || c.Dead || !c.RaceProps.Humanlike || !c.Awake()) continue;
                float dd = pet.Position.DistanceTo(c.Position);
                if (dd <= bd) { bd = dd; best = c; }
            }
            return best;
        }

        private static readonly string[][] SelfTalkMotes =
        {
            new[] { "Get this off me.", "I'll get free.", "Not a pet.", "Soon...", "They'll pay." },
            new[] { "Maybe it's okay.", "It's easier now.", "I'll be good.", "...calm." },
            new[] { "I love my collar.", "I'm yours.", "Good pet.", "Keep me." },
        };

        private static void ThrowSelfTalkMote(Pawn pet, int tier)
        {
            if (pet?.Map == null || !pet.Spawned) return;
            if (tier < 0) tier = 0; if (tier > 2) tier = 2;
            var arr = SelfTalkMotes[tier];
            var col = tier == 0 ? new UnityEngine.Color(1f, 0.7f, 0.7f)
                                : (tier == 2 ? new UnityEngine.Color(1f, 0.82f, 0.95f) : UnityEngine.Color.white);
            try { MoteMaker.ThrowText(pet.DrawPos + new UnityEngine.Vector3(0f, 0f, 0.7f), pet.Map,
                arr[Rand.Range(0, arr.Length)], col, 2.5f); } catch { }
        }

        /// <summary>Plays a short vanilla sound at the pawn (resolved by name so a missing DLC just no-ops).
        /// Ships no audio - reuses existing game sounds. Gated on the enableSounds setting.</summary>
        public static void PlaySoundClip(string defName, Pawn at)
        {
            try
            {
                if (S == null || !S.enableSounds || at == null || at.Map == null || !at.Spawned) return;
                var sd = DefDatabase<SoundDef>.GetNamedSilentFail(defName);
                if (sd == null) return;
                Verse.Sound.SoundStarter.PlayOneShot(sd, Verse.Sound.SoundInfo.InMap(new TargetInfo(at.Position, at.Map)));
            }
            catch { }
        }

        private static void ThrowControlMote(Pawn p, string text, UnityEngine.Color color)
        {
            if (p == null || !p.Spawned || p.Map == null) return;
            try { MoteMaker.ThrowText(p.DrawPos + new UnityEngine.Vector3(0f, 0f, 0.8f), p.Map, text, color, 3.0f); } catch { }
        }

        public static void TryAddMoodThought(Pawn p, string defName)
        {
            var td = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            var mem = p?.needs?.mood?.thoughts?.memories;
            if (td == null || mem == null) return;
            try { mem.TryGainMemory(td); } catch { }
        }

        /// <summary>Add a memory thought at a specific stage index (for thoughts whose stage is chosen by the
        /// pawn's temperament rather than a ThoughtWorker).</summary>
        private static void TryAddMoodThoughtStaged(Pawn p, string defName, int stage)
        {
            var td = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            var mem = p?.needs?.mood?.thoughts?.memories;
            if (td == null || mem == null) return;
            try { mem.TryGainMemory(ThoughtMaker.MakeThought(td, stage)); } catch { }
        }

        /// <summary>Command the slave to service a target: opens RJW's sex-type menu (bestiality menu for
        /// animals), falling back to quick sex for humanlikes.</summary>
        public static void CommandServe(Pawn slave, Pawn target)
        {
            if (slave == null || target == null) return;
            SetCommandCooldown(slave);
            DeepenConditioning(slave);

            if (target.RaceProps != null && target.RaceProps.Animal)
            {
                StartBestiality(slave, target);
                return;
            }

            var options = RmbSexOptions(slave, target);
            if (options != null && options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
            else
                StartCasualSex(slave, target);
        }

        /// <summary>Directly starts RJW's no-bed bestiality job (the AI fallback) so it reliably completes,
        /// bypassing the RMB menu's bed + attraction gates.</summary>
        public static void StartBestiality(Pawn slave, Pawn animal)
        {
            try
            {
                if (!BestialityEnabled() || slave?.jobs == null || animal == null || animal.Dead) return;
                var job = JobMaker.MakeJob(xxx.bestiality, animal);
                slave.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] bestiality command failed: " + ex.Message, 0x5A1350);
            }
        }

        private static System.Reflection.MethodInfo _rmbSex;
        private static bool _rmbTried;
        private static List<FloatMenuOption> RmbSexOptions(Pawn slave, Pawn target)
        {
            try
            {
                if (!_rmbTried)
                {
                    _rmbTried = true;
                    var t = GenTypes.GetTypeInAnyAssembly("rjw.RMB.RMB_Sex");
                    if (t != null)
                        _rmbSex = t.GetMethod("GetOptions",
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                            null, new[] { typeof(Pawn), typeof(Pawn) }, null);
                }
                if (_rmbSex == null) return null;
                var res = _rmbSex.Invoke(null, new object[] { slave, target }) as System.Collections.Generic.IEnumerable<FloatMenuOption>;
                return res?.Where(o => o != null && o.action != null).ToList();
            }
            catch { return null; }
        }

        private static System.Reflection.MethodInfo _rmbBest;
        private static bool _rmbBestTried;
        private static List<FloatMenuOption> RmbBestialityOptions(Pawn slave, Pawn animal)
        {
            try
            {
                if (!_rmbBestTried)
                {
                    _rmbBestTried = true;
                    var t = GenTypes.GetTypeInAnyAssembly("rjw.RMB.RMB_Bestiality");
                    if (t != null)
                        _rmbBest = t.GetMethod("GetOptions",
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                            null, new[] { typeof(Pawn), typeof(Pawn) }, null);
                }
                if (_rmbBest == null) return null;
                var res = _rmbBest.Invoke(null, new object[] { slave, animal }) as System.Collections.Generic.IEnumerable<FloatMenuOption>;
                return res?.Where(o => o != null && o.action != null).ToList();
            }
            catch { return null; }
        }

        // Per-frame cache: BuildKeyHolderGizmos calls this per key every GUI frame; a full pawn scan per key per
        // frame is wasteful. Cache the resolved victim per key comp, refreshed once per Unity frame.
        private static readonly Dictionary<rjw.CompHoloCryptoStamped, int> _lockedVictimFrame = new Dictionary<rjw.CompHoloCryptoStamped, int>();
        private static readonly Dictionary<rjw.CompHoloCryptoStamped, Pawn> _lockedVictimCache = new Dictionary<rjw.CompHoloCryptoStamped, Pawn>();
        private static Pawn FindLockedVictimForKey(rjw.CompHoloCryptoStamped keyComp, Map map)
        {
            if (map == null || keyComp == null) return null;
            int frame = UnityEngine.Time.frameCount;
            if (_lockedVictimFrame.TryGetValue(keyComp, out int f) && f == frame
                && _lockedVictimCache.TryGetValue(keyComp, out var cached))
            {
                if (cached == null || (cached.Spawned && cached.Map == map)) return cached;
            }
            Pawn found = null;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (!p.RaceProps.Humanlike || p.apparel == null) continue;
                var worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    var gc = worn[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                    if (gc != null && keyComp.matches(gc)) { found = p; break; }
                }
                if (found != null) break;
            }
            if (_lockedVictimCache.Count > 512) { _lockedVictimCache.Clear(); _lockedVictimFrame.Clear(); }
            _lockedVictimCache[keyComp] = found;
            _lockedVictimFrame[keyComp] = frame;
            return found;
        }

        private static void UnbindVictim(Pawn victim, rjw.CompHoloCryptoStamped keyComp)
        {
            if (victim?.apparel == null || keyComp == null) return;
            var toRemove = new List<Apparel>();
            var worn = victim.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                var gc = worn[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                if (gc != null && keyComp.matches(gc)) toRemove.Add(worn[i]);
            }
            for (int i = 0; i < toRemove.Count; i++)
                try { GameComponent_Harassment.Instance?.RemoveLockedExtra(toRemove[i]); victim.apparel.Remove(toRemove[i]); toRemove[i].Destroy(); } catch { }
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp != null) vp.boundInPublic = false; // freed - stop begging

            // If tucked into an onahole, get them up and out.
            try
            {
                var bed = victim.CurrentBed();
                if (bed != null && bed.GetType().FullName == "RJW_Onahole.Building_OnaholeBed")
                    victim.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
            catch { }
            if (InvolvesPlayerPawn(victim, victim))
                Messages.Message(victim.LabelShort + " was unbound.", new LookTargets(victim), MessageTypeDefOf.NeutralEvent, false);
        }

        // ── Auto-service configuration + execution ────────────────────────────
        private static readonly string[] ServiceGroups = { "Owner", "Colonists", "Prisoners & slaves", "Guests", "Anyone nearby" };

        public static string ServiceGroupLabel(int mode) => (mode >= 0 && mode < ServiceGroups.Length) ? ServiceGroups[mode] : ServiceGroups[0];

        public static List<FloatMenuOption> BuildServiceGroupMenu(PawnProfile prof)
        {
            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < ServiceGroups.Length; i++)
            {
                int mode = i;
                opts.Add(new FloatMenuOption(ServiceGroups[i], delegate { prof.serviceTargetMode = mode; }));
            }
            return opts;
        }

        public static string ServiceActLabel(string defName)
        {
            if (defName.NullOrEmpty()) return "Default";
            var def = DefDatabase<InteractionDef>.GetNamedSilentFail(defName);
            return def != null ? def.label.CapitalizeFirst() : "Default";
        }

        public static List<FloatMenuOption> BuildServiceActMenu(PawnProfile prof)
        {
            var opts = new List<FloatMenuOption>();
            opts.Add(new FloatMenuOption("Default (quick service)", delegate { prof.serviceInteraction = null; }));
            try
            {
                foreach (var def in rjw.SexUtility.SexInteractions)
                {
                    var si = new rjw.Modules.Interactions.SexInteraction(def);
                    if (si.Sextype == xxx.rjwSextype.None || si.Sextype == xxx.rjwSextype.Masturbation) continue;
                    if (!si.HasInteractionTag(rjw.SexInteractionTag.Consensual)) continue;
                    if (si.HasInteractionTag(rjw.SexInteractionTag.Reverse)) continue;
                    var d = def;
                    opts.Add(new FloatMenuOption(def.label.CapitalizeFirst(), delegate { prof.serviceInteraction = d.defName; }));
                }
            }
            catch { }
            return opts;
        }

        /// <summary>Picks a nearby pawn from the configured service group that the slave can actually service.</summary>
        public static Pawn PickServiceTarget(Pawn slave, PawnProfile prof, Pawn owner)
        {
            if (slave?.Map == null || prof == null) return null;
            if (prof.serviceTargetMode == 0)
                return (owner != null && IsServiceable(slave, owner) && slave.Position.DistanceTo(owner.Position) <= 16f) ? owner : null;

            Pawn best = null;
            float bestDist = 999f;
            foreach (var t in slave.Map.mapPawns.AllPawnsSpawned)
            {
                if (t == slave || !InServiceGroup(t, prof.serviceTargetMode)) continue;
                if (!IsServiceable(slave, t)) continue;
                float d = slave.Position.DistanceTo(t.Position);
                if (d > 16f) continue;
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }

        private static bool InServiceGroup(Pawn t, int mode)
        {
            switch (mode)
            {
                case 1: return t.IsFreeColonist;
                case 2: return t.IsPrisonerOfColony || t.IsSlaveOfColony;
                case 3: return t.Faction != null && !t.Faction.IsPlayer && !t.HostileTo(Faction.OfPlayer)
                               && !t.IsPrisonerOfColony && !t.IsSlaveOfColony;
                case 4: return !t.HostileTo(Faction.OfPlayer);
                default: return false;
            }
        }

        private static bool IsServiceable(Pawn slave, Pawn t)
        {
            if (t == null || !t.Spawned || t.Dead || !t.RaceProps.Humanlike) return false;
            if (t.ageTracker == null || !t.ageTracker.Adult) return false;
            if (t.Downed || !t.Awake() || IsBusyInAct(t)) return false;
            if (!GenderOk(slave, t)) return false; // heterosexual-only gate
            try { if (!xxx.can_be_fucked(t) && !xxx.can_fuck(t)) return false; } catch { }
            return slave.CanReach(t, PathEndMode.Touch, Danger.Some);
        }

        /// <summary>Venue service for an auto-servicing pet: if set up for Hospitality room service, solicit a
        /// guest via that mod's own job; else if employed as a Gastronomy waiter, serve a table. Both use the host
        /// mod's validated jobs. Returns true if a venue job was started, so auto-service skips normal servicing.</summary>
        public static bool TryVenueService(Pawn pet)
        {
            try
            {
                if (RoomServiceBridge.Active && RoomServiceBridge.CanSolicitGuests(pet))
                {
                    var guest = RoomServiceBridge.FindSolicitTarget(pet);
                    if (guest != null && RoomServiceBridge.SolicitGuest(pet, guest))
                    {
                        TryAddMoodThought(pet, "RJWSH_MadeToService");
                        return true;
                    }
                }
                if (GastronomyBridge.Active && GastronomyBridge.TryServe(pet)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Runs an auto-service: the chosen sex act if set (via RJW's HaveSex), else quick service.</summary>
        public static void RunService(Pawn slave, Pawn target, string interactionDefName)
        {
            try
            {
                SatisfySubmission(slave, 0.25f);
                NoteServiceRendered(slave);
                if (slave?.jobs == null || target == null) return;
                // "Made to service" moodlet for commanded / auto service - but NOT for a paid whore job, which
                // gets RJWSH_ForcedWhore instead (whoreOwnerId stays set through the act).
                var spSvc = GameComponent_Harassment.Instance?.GetProfileIfExists(slave);
                if (spSvc == null || spSvc.whoreOwnerId < 0) TryAddMoodThought(slave, "RJWSH_MadeToService");
                // An unconditioned slave does not consent: model service as the target using the unwilling slave
                // (slave = victim) so it logs as rape and Karma attributes the victim. Stockholm slaves serve willingly.
                if (!IsFullyConditioned(slave) && TryForceRape(target, slave)) return;
                if (!interactionDefName.NullOrEmpty())
                {
                    var def = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
                    // Only force the chosen act if it actually resolves for this pair; otherwise RJW logs
                    // "failed to resolve ... using empty interaction" and nothing meaningful happens.
                    if (def != null && CanResolveAct(slave, target, def))
                    {
                        rjw.RMB.RMB_Menu.HaveSex(slave, xxx.quick_sex, target, def);
                        return;
                    }
                }
                // The TARGET initiates (they are the one using the slave), so RJW's quickie driver makes the
                // controllable slave the partner-who-follows to the private spot. If the slave initiated, a flaky
                // visitor client would be told to follow and its guest AI would ignore the goto.
                StartCasualSex(target, slave); // fallback: RJW picks a valid act for the pair
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] service start failed: " + ex.Message, 0x5A1351);
                try { StartCasualSex(target, slave); } catch { }
            }
        }

        /// <summary>True if the given sex act can actually resolve for this initiator -> recipient pair.</summary>
        private static bool CanResolveAct(Pawn slave, Pawn target, InteractionDef def)
        {
            try
            {
                var props = new rjw.SexProps(slave, target);
                props.interaction = new rjw.Modules.Interactions.SexInteraction(def);
                return rjw.Modules.Interactions.SexInteractionHelper.ResolveInteraction(props) != null;
            }
            catch { return true; } // if the check itself fails, let RJW try and handle it
        }

        /// <summary>Starts RJW consensual quick sex (used by the flirt willing path), off the job stack.</summary>
        public static void StartCasualSex(Pawn harasser, Pawn target)
        {
            try
            {
                if (harasser?.jobs == null || target == null) return;
                if (!xxx.can_fuck(harasser) && !xxx.can_be_fucked(harasser)) return;
                var job = JobMaker.MakeJob(xxx.quick_sex, target);
                harasser.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] casual sex start failed: " + ex.Message, 0x5A12F0);
            }
        }

        /// <summary>Counts BDSM/bondage components on a pawn: HoloCrypto-stamped (locked) apparel plus any
        /// RJW bondage_gear_def pieces. Detected dynamically so it covers any mod using RJW's lock comp.</summary>
        public static int BdsmGearCount(Pawn p)
        {
            if (p?.apparel == null) return 0;
            int n = 0;
            try
            {
                var worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    var ap = worn[i];
                    if (ap == null) continue;
                    if (ap.TryGetComp<rjw.CompHoloCryptoStamped>() != null || ap.def is rjw.bondage_gear_def)
                        n++;
                }
            }
            catch { }
            return n;
        }

        /// <summary>Robust composite vulnerability metric (higher = easier prey): RJW base vulnerability plus
        /// helplessness from locked BDSM gear, restraints, downed/asleep state, pushover reputation,
        /// conditioning, and injury. Used to weight target selection and escalation/rape odds.</summary>
        public static float VulnerabilityScore(Pawn p)
        {
            if (p == null) return 0f;
            float v = SafeVulnerability(p);                 // RJW base 0..1+
            v += BdsmGearCount(p) * 0.20f;                  // each locked/bondage piece compounds helplessness
            if (IsInBondage(p)) v += 0.30f;                 // hands/legs/genitals blocked
            if (HandsBound(p)) v += 0.15f;
            if (p.Downed) v += 0.60f;
            else if (!p.Awake()) v += 0.40f;               // asleep
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof != null)
            {
                v += (prof.impression + 50f) / 200f;       // pushover reputation, 0..0.5
                if (prof.IsConditioned) v += 0.30f;        // hypnotised = very compliant
                else if (prof.IsSuggestible) v += 0.15f;
            }
            if (p.health?.summaryHealth != null && p.health.summaryHealth.SummaryHealthPercent < 0.5f) v += 0.20f;
            return v < 0f ? 0f : v;
        }

        // ── Devious devices (RJW bondage gear) ────────────────────────────────
        /// <summary>True if the pawn is wearing RJW bondage gear (locked or not) or has a part blocked by a device.</summary>
        public static bool IsInBondage(Pawn p)
        {
            if (p == null) return false;
            try { if (HasRestraintHediff(p)) return true; } catch { }
            if (p.apparel == null) return false;
            try
            {
                if (p.is_wearing_locked_apparel()) return true;
                var worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                    if (worn[i].def is rjw.bondage_gear_def) return true;
                if (rjw.Genital_Helper.hands_blocked(p) || rjw.Genital_Helper.genitals_blocked(p)) return true;
            }
            catch { }
            return false;
        }

        // Restraint hediffs from compatible mods (BondageBed Torture's bed/chains/chair, Simple Restraint Belt's
        // implant, plus our own worn belt) count as bondage so a strapped/belted pawn reads as restrained and is
        // a valid harassment target.
        private static readonly string[] _restraintHediffNames =
            { "SR_Hediff_BondageBed", "SR_Hediff_BondageChains", "SR_Hediff_ElectricChair", "Restraintbelt", "RJWSH_BeltRestrained" };
        private static HediffDef[] _restraintHediffDefs;
        public static bool HasRestraintHediff(Pawn p)
        {
            if (p?.health?.hediffSet == null) return false;
            if (_restraintHediffDefs == null)
            {
                var list = new List<HediffDef>();
                foreach (var n in _restraintHediffNames)
                {
                    var d = DefDatabase<HediffDef>.GetNamedSilentFail(n);
                    if (d != null) list.Add(d);
                }
                _restraintHediffDefs = list.ToArray();
            }
            for (int i = 0; i < _restraintHediffDefs.Length; i++)
                if (p.health.hediffSet.GetFirstHediffOfDef(_restraintHediffDefs[i]) != null) return true;
            return false;
        }

        public static bool HandsBound(Pawn p)
        {
            try { return rjw.Genital_Helper.hands_blocked(p); } catch { return false; }
        }

        private static bool DoDeviousDevice(Pawn harasser, Pawn target)
        {
            FireInteraction(harasser, target, RJWSH_InteractionDefOf.RJWSH_DeviousApproach);
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Proposition);
            if (S.multiLineHarassment) ScheduleApproachExchange(harasser, target, ApproachType.DeviousDevice);

            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            if (hp.morality == Morality.Decent)
            {
                // A decent pawn genuinely helps: removes any non-locked bondage gear.
                FreeUnlockedGear(target);
                if (InvolvesPlayerPawn(harasser, target))
                    Messages.Message(harasser.LabelShort + " helped " + target.LabelShort + " out of their restraints.",
                        new LookTargets(target), MessageTypeDefOf.PositiveEvent, false);
                return false;
            }

            // Others take advantage of the helpless, restrained pawn.
            KarmaBridge.AddKarma(harasser, -4f, "rjw_harassment_devious");
            return S.allowEscalation && (S.enableGrope || S.enableForced);
        }

        // ── Lock a device on the victim after a rape ──────────────────────────
        private static List<ThingDef> _lockableGear;
        private static List<ThingDef> LockableGear()
        {
            if (_lockableGear != null) return _lockableGear;
            _lockableGear = new List<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!d.IsApparel || !(d is rjw.bondage_gear_def) || d.comps == null) continue;
                if (d.defName == "RJWSH_ControlCollar") continue; // reserved for the hypnosis final tier
                for (int i = 0; i < d.comps.Count; i++)
                    if (d.comps[i] is rjw.CompProperties_HoloCryptoStamped) { _lockableGear.Add(d); break; }
            }
            if (DebugHarassmentVerbose)
            {
                var belt = DefDatabase<ThingDef>.GetNamedSilentFail("RJWSH_RestraintBelt");
                Log.Message("[RJWSH-DIAG] LockableGear built: " + _lockableGear.Count + " devices. RJWSH_RestraintBelt: "
                    + (belt == null ? "DEF NOT FOUND IN DATABASE"
                       : ("found IsApparel=" + belt.IsApparel + " isBondageGearDef=" + (belt is rjw.bondage_gear_def)
                          + " comps=" + (belt.comps?.Count.ToString() ?? "null")
                          + " hasHoloCrypto=" + (belt.comps != null && belt.comps.Any(c => c is rjw.CompProperties_HoloCryptoStamped))
                          + " inList=" + _lockableGear.Contains(belt))));
            }
            // Safety net: guarantee our own restraint belt is enrolled even if the comp scan missed it.
            var ownBelt = DefDatabase<ThingDef>.GetNamedSilentFail("RJWSH_RestraintBelt");
            if (ownBelt != null && ownBelt.IsApparel && !_lockableGear.Contains(ownBelt)) _lockableGear.Add(ownBelt);
            return _lockableGear;
        }

        // Extra non-bondage_gear_def apparel (Petifcation pet collars/masks, Sexperience restraints) made
        // lockable by injecting a HoloCrypto stamp onto the worn instance.
        private static List<ThingDef> _lockableExtra;
        // RJW Extension's bondage gear is already bondage_gear_def (caught by LockableGear); listing it here
        // also picks up any plain-apparel devices it ships, via injected-lock.
        private static readonly string[] DeviceMods = { "castle.petifcation", "rjw.sexperience", "rimworld.ekss.rjwex" };
        private static readonly string[] DeviceKeywords =
            { "collar", "mask", "gag", "binder", "cuff", "chastity", "harness", "blindfold", "shackle", "muzzle", "hood", "ears", "tail", "leash", "strait" };

        private static List<ThingDef> LockableExtraApparel()
        {
            if (_lockableExtra != null) return _lockableExtra;
            _lockableExtra = new List<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!d.IsApparel || d is rjw.bondage_gear_def) continue;
                if (d.defName == "RJWSH_ControlCollar") continue;
                var pid = d.modContentPack?.PackageId?.ToLowerInvariant();
                if (pid == null || !DeviceMods.Any(m => pid.Contains(m))) continue;
                string hay = (d.defName + " " + (d.label ?? "")).ToLowerInvariant();
                if (DeviceKeywords.Any(k => hay.Contains(k))) _lockableExtra.Add(d);
            }
            return _lockableExtra;
        }

        // Prefer a bondage-frame onahole (Cross/Pillory/Pole/Hanger/HorseMount/MetalFrame) over a plain one.
        private static readonly string[] BondageFrameDefs =
            { "OnaholeCross", "OnaholePillory", "OnaholePole", "OnaholeHanger", "OnaholeHorseMount", "OnaholeMetalFrame" };
        private static ThingDef PickOnaholeBed()
        {
            var beds = OnaholeBeds();
            if (beds.Count == 0) return null;
            var frames = beds.Where(b => BondageFrameDefs.Contains(b.defName)).ToList();
            return (frames.Count > 0 ? frames : beds).RandomElement();
        }

        /// <summary>Forces an onahole's CompBondage to show a bondage style (TextureIndex 1..N) instead of the
        /// default, when the victim is placed. Reflection-based (the Onahole assembly isn't referenced).</summary>
        public static void ForceBondageStyle(Thing bed)
        {
            try
            {
                var twc = bed as ThingWithComps;
                if (twc?.AllComps == null) return;
                foreach (var comp in twc.AllComps)
                {
                    if (comp.GetType().FullName != "RJW_Onahole.Comps.CompBondage") continue;
                    int count = 1;
                    try
                    {
                        var props = comp.props;
                        var rnpField = props?.GetType().GetField("renderNodeProperties");
                        if (rnpField?.GetValue(props) is System.Collections.IEnumerable rnp)
                            foreach (var prop in rnp)
                            {
                                var tpField = prop?.GetType().GetField("texPaths");
                                if (tpField?.GetValue(prop) is System.Collections.ICollection tp && tp.Count > 0)
                                { count = tp.Count; break; }
                            }
                    }
                    catch { }
                    int idx = Rand.RangeInclusive(1, System.Math.Max(1, count));
                    comp.GetType().GetProperty("TextureIndex")?.SetValue(comp, idx);
                    break;
                }
            }
            catch { }
        }

        /// <summary>Every device that can be locked on a pawn: RJW bondage gear + injected-lock extras.</summary>
        public static List<ThingDef> AllLockableDevices()
        {
            var list = new List<ThingDef>(LockableGear());
            list.AddRange(LockableExtraApparel());
            return list;
        }

        /// <summary>A real (non-player, visible) faction to hold a photo sold off the map, so "sold" photos point
        /// at an actual faction instead of a vague string.</summary>
        private static Faction PickPhotoFaction()
        {
            var fm = Find.FactionManager;
            if (fm == null) return null;
            Faction pick = null; int count = 0;
            foreach (var f in fm.AllFactionsVisible)
            {
                if (f == null || f.IsPlayer) continue;
                count++;
                if (Rand.Range(0, count) == 0) pick = f;   // reservoir sample -> uniform random known faction
            }
            return pick;
        }

        /// <summary>Picks a fitting device, never one that conflicts with the control collar or any other
        /// locked apparel - so applying a device can never knock the collar off.</summary>
        private static ThingDef PickDeviceFor(Pawn victim)
        {
            if (victim?.apparel == null) return null;
            var body = victim.RaceProps.body;
            bool Ok(ThingDef d)
            {
                if (!ApparelUtility.HasPartsToWear(victim, d)) return false;
                var worn = victim.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    var w = worn[i];
                    if (w.def == d) return false; // already wearing this exact device
                    bool wLocked = w.def == RJWSH_ThingDefOf.RJWSH_ControlCollar || w.has_lock();
                    if (wLocked && !ApparelUtility.CanWearTogether(d, w.def, body)) return false;
                }
                return true;
            }
            return AllLockableDevices().Where(Ok).RandomElementWithFallback(null);
        }

        /// <summary>Locks a random fitting device on the victim; the captor keeps the Holokey. Used by the
        /// post-rape lock, the onahole capture, and the debug "Apply device" tool.</summary>
        public static ThingDef LockRJWDevice(Pawn victim, Pawn captor)
        {
            if (victim?.apparel == null || victim.Dead || !victim.RaceProps.Humanlike) return null;
            var def = PickDeviceFor(victim);
            return def == null ? null : ApplyAndLockDevice(victim, def, captor);
        }

        /// <summary>Equips a specific device and locks it. bondage_gear_def auto-locks via RJW on wear;
        /// plain apparel is locked by injecting a HoloCrypto stamp and minting a matching Holokey.</summary>
        public static ThingDef ApplyAndLockDevice(Pawn victim, ThingDef def, Pawn captor)
        {
            if (victim?.apparel == null || def == null) return null;
            try
            {
                // Some gear (e.g. PrisonerChains) is madeFromStuff; supply a material or MakeThing errors.
                var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                var app = (Apparel)ThingMaker.MakeThing(def, stuff);
                victim.apparel.Wear(app, false, true);
                if (app.Wearer != victim) { try { if (!app.Destroyed) app.Destroy(); } catch { } return null; } // blocked by a locked conflict

                if (def is rjw.bondage_gear_def)
                    ConsolidateVictimKey(victim, app, captor, true);  // RJW on_wear locked it + spawned a key; dedup to one
                else
                    LockPlainApparel(app, victim, captor);  // inject the lock + (first time only) mint the key
                return def;
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] device lock failed: " + ex.Message, 0x5A1320);
                return null;
            }
        }

        // ── Injected lock for non-bondage_gear_def apparel ──────────────────────────
        private static System.Reflection.FieldInfo _compsField;
        private static System.Reflection.FieldInfo _compsByTypeField;

        private static void AddCompToInstance(ThingWithComps t, ThingComp comp)
        {
            if (_compsField == null)
                _compsField = typeof(ThingWithComps).GetField("comps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var comps = (List<ThingComp>)_compsField.GetValue(t);
            if (comps == null) { comps = new List<ThingComp>(); _compsField.SetValue(t, comps); }
            comps.Add(comp);
            // GetComp<T> resolves via the cached compsByType dictionary, so rebuild it or the comp is invisible.
            if (_compsByTypeField == null)
                _compsByTypeField = typeof(ThingWithComps).GetField("compsByType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = comps.GroupBy(c => c.GetType()).ToDictionary(g => g.Key, g => g.ToArray());
            _compsByTypeField.SetValue(t, dict);
        }

        private static rjw.CompHoloCryptoStamped InjectLock(Apparel app, string name = null, string key = null)
        {
            if (app == null) return null;
            var existing = app.TryGetComp<rjw.CompHoloCryptoStamped>();
            if (existing != null) return existing;
            var comp = new rjw.CompHoloCryptoStamped { parent = app };
            comp.Initialize(new rjw.CompProperties_HoloCryptoStamped()); // generates a random engraved name + key
            if (!name.NullOrEmpty()) comp.name = name;
            if (!key.NullOrEmpty()) comp.key = key;
            AddCompToInstance(app, comp);
            return comp;
        }

        private static void LockPlainApparel(Apparel app, Pawn victim, Pawn captor)
        {
            bool hasMaster = TryGetVictimStampExcluding(victim, app, out var mName, out var mKey);
            var comp = InjectLock(app, hasMaster ? mName : null, hasMaster ? mKey : null);
            if (comp == null) return;
            GameComponent_Harassment.Instance?.RecordLockedExtra(app, comp.name, comp.key);
            GiveMasterKey(victim, captor, comp.name, comp.key); // ensure the captor holds the one master key
        }

        private static void GiveOrDropKey(Thing key, Pawn victim, Pawn captor)
        {
            try
            {
                if (captor != null && captor.Spawned && !captor.Dead && captor.inventory != null
                    && captor.inventory.innerContainer.TryAdd(key, true)) return;
            }
            catch { }
            if (victim?.Map != null) GenPlace.TryPlaceThing(key, victim.Position, victim.Map, ThingPlaceMode.Near);
        }

        // ── One-key-per-victim consolidation ────────────────────────────────────────
        /// <summary>After locking gear, keep the victim to exactly ONE stamp + ONE key: the first lock defines
        /// the master stamp/key; later locks adopt that stamp and the freshly-spawned duplicate key is destroyed.</summary>
        private static void ConsolidateVictimKey(Pawn victim, Apparel gear, Pawn captor, bool rjwSpawnedKey)
        {
            var comp = gear?.TryGetComp<rjw.CompHoloCryptoStamped>();
            if (comp == null) { if (rjwSpawnedKey) MoveHolokeyToHarasser(victim, captor); return; }
            string mName, mKey;
            if (TryGetVictimStampExcluding(victim, gear, out mName, out mKey)) { comp.name = mName; comp.key = mKey; }
            else { mName = comp.name; mKey = comp.key; }
            if (rjwSpawnedKey) DestroyKeysAt(victim);     // remove RJW's auto-spawned key(s) - we manage keys ourselves
            GiveMasterKey(victim, captor, mName, mKey);   // GUARANTEE exactly one master key exists
        }

        // Destroys every Holokey lying on the victim's own cell (RJW auto-spawns one on each lock).
        private static void DestroyKeysAt(Pawn victim)
        {
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
            if (keyDef == null || victim?.Map == null) return;
            var things = victim.Position.GetThingList(victim.Map);
            for (int i = things.Count - 1; i >= 0; i--)
                if (things[i].def == keyDef) { try { things[i].Destroy(); } catch { } }
        }

        // Ensures one Holokey for the master stamp exists: if the captor already holds a matching key, done;
        // otherwise mint one and hand it to the captor (or drop it at the victim if there is no captor). This
        // both de-duplicates (no new key when the captor already has it) AND guarantees a key always exists.
        private static void GiveMasterKey(Pawn victim, Pawn captor, string mName, string mKey)
        {
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
            if (keyDef == null) return;
            if (captor?.inventory != null)
            {
                var inv = captor.inventory.innerContainer;
                for (int i = 0; i < inv.Count; i++)
                    if (inv[i].def == keyDef)
                    {
                        var c = inv[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                        if (c != null && c.name == mName && c.key == mKey) return; // captor already holds it
                    }
            }
            var key = ThingMaker.MakeThing(keyDef);
            var kc = key.TryGetComp<rjw.CompHoloCryptoStamped>();
            if (kc != null) { kc.name = mName; kc.key = mKey; }
            GiveOrDropKey(key, victim, captor);
        }

        private static bool TryGetVictimStampExcluding(Pawn victim, Apparel exclude, out string name, out string key)
        {
            name = null; key = null;
            // Master stamp = an OTHER locked piece this victim is ALREADY wearing. Reading it from worn gear
            // (instead of a cached profile field) means a freed pawn with nothing locked always gets a fresh
            // key - the cached stamp used to persist after freeing and silently destroy every new key.
            var worn = victim?.apparel?.WornApparel;
            if (worn != null)
                for (int i = 0; i < worn.Count; i++)
                {
                    if (worn[i] == exclude) continue;
                    var c = worn[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                    if (c != null && !c.name.NullOrEmpty()) { name = c.name; key = c.key; StoreVictimStamp(victim, name, key); return true; }
                }
            return false;
        }

        private static void StoreVictimStamp(Pawn victim, string name, string key)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (prof != null) { prof.cryptoName = name; prof.cryptoKey = key; }
        }

        private static void DestroyLooseDuplicateKeyAt(Pawn victim, string masterName, string masterKey)
        {
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
            if (keyDef == null || victim?.Map == null) return;
            var things = victim.Position.GetThingList(victim.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i].def != keyDef) continue;
                var c = things[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                if (c == null || c.name != masterName || c.key != masterKey) { try { things[i].Destroy(); } catch { } return; }
            }
        }

        private static void MintKeyForVictim(rjw.CompHoloCryptoStamped masterComp, Pawn victim, Pawn captor)
        {
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
            if (keyDef == null) return;
            var keyThing = ThingMaker.MakeThing(keyDef);
            keyThing.TryGetComp<rjw.CompHoloCryptoStamped>()?.copy_stamp_from(masterComp);
            GiveOrDropKey(keyThing, victim, captor);
        }

        /// <summary>True when t is locked apparel currently worn by pawn (removable only with the key).</summary>
        public static bool IsLockedWornApparel(Pawn pawn, Thing t)
        {
            var app = t as Apparel;
            if (app == null || pawn?.apparel == null) return false;
            return pawn.apparel.WornApparel.Contains(app) && app.TryGetComp<rjw.CompHoloCryptoStamped>() != null;
        }

        /// <summary>Strips everything except locked devices / bondage gear, forbidding the dropped items so the
        /// pawn does not immediately re-wear them. Used by the owner "keep naked" order.</summary>
        public static void StripToBondage(Pawn p)
        {
            if (p?.apparel == null) return;
            var worn = p.apparel.WornApparel;
            List<Apparel> toDrop = null;
            for (int i = 0; i < worn.Count; i++)
            {
                var app = worn[i];
                if (app.def is rjw.bondage_gear_def) continue;
                if (app.TryGetComp<rjw.CompHoloCryptoStamped>() != null) continue;
                if (toDrop == null) toDrop = new List<Apparel>();
                toDrop.Add(app);
            }
            if (toDrop == null) return;
            for (int i = 0; i < toDrop.Count; i++)
            {
                try
                {
                    Apparel dropped;
                    if (p.apparel.TryDrop(toDrop[i], out dropped) && dropped != null)
                        dropped.SetForbidden(true, false);
                }
                catch { }
            }
        }

        /// <summary>Re-applies injected HoloCrypto stamps after a save load (the comp is not in def.comps, so
        /// it would otherwise be gone). Prunes records whose apparel no longer exists.</summary>
        public static void ReinjectLockedExtras(List<LockedExtraRecord> records)
        {
            if (records == null) return;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                if (r?.apparel == null || r.apparel.Destroyed || r.apparel.Wearer == null) { records.RemoveAt(i); continue; }
                if (r.apparel.TryGetComp<rjw.CompHoloCryptoStamped>() == null)
                    InjectLock(r.apparel, r.stampName, r.stampKey);
            }
        }

        /// <summary>After a forced act, a chance the rapist locks an RJW device on the victim and keeps the key.</summary>
        public static void TryLockDeviceAfterRape(Pawn rapist, Pawn victim)
        {
            if (!S.enableDeviceLockAfterRape || victim == null || !victim.Spawned) return;
            if (!Rand.Chance(S.deviceLockChance)) return;
            try { if (victim.is_wearing_locked_apparel()) return; } catch { }
            int n = LockDevices(victim, rapist);
            if (n > 0 && InvolvesPlayerPawn(rapist, victim))
                Messages.Message(rapist.LabelShort + " locked " + (n == 1 ? "a device" : n + " devices") + " onto " + victim.LabelShort + " and kept the key" + (n > 1 ? "s" : "") + ".",
                    new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
        }

        /// <summary>Locks one device, then up to maxLockedDevices-1 more (each gated by extraDeviceChance).
        /// Each lock picks a non-conflicting device, so the pieces stack rather than replace each other.</summary>
        public static int LockDevices(Pawn victim, Pawn captor)
        {
            int locked = 0;
            int max = System.Math.Max(1, S.maxLockedDevices);
            for (int i = 0; i < max; i++)
            {
                if (i > 0 && !Rand.Chance(S.extraDeviceChance)) break;
                if (LockRJWDevice(victim, captor) == null) break; // no more fitting devices
                locked++;
            }
            return locked;
        }

        // ── Bound in public ─────────────────────────────────────────
        /// <summary>Deferred entry: a chance the rapist hauls the victim to a public spot and locks a device on them.</summary>
        public static void TryStartBoundInPublic(Pawn rapist, Pawn victim)
        {
            if (!S.enableBoundInPublic) return;
            if (!Rand.Chance(S.boundInPublicChance)) return;
            DoBoundInPublic(rapist, victim);
        }

        /// <summary>Sends the rapist to carry the victim to a public cell and leave them locked in a device.
        /// No chance roll (debug-callable). Returns true if started.</summary>
        public static bool DoBoundInPublic(Pawn rapist, Pawn victim)
        {
            if (rapist?.jobs == null || victim == null || !victim.Spawned || victim.Dead || !victim.RaceProps.Humanlike) return false;
            if (!TryFindPublicBedCell(victim.Map, null, victim.Position, out IntVec3 cell)) return false;
            try
            {
                var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_DragToPublic, victim, cell);
                rapist.jobs.StartJob(job, JobCondition.InterruptForced);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] bound-in-public failed: " + ex.Message, 0x5A1341);
                return false;
            }
        }

        public static void NotifyBoundInPublic(Pawn rapist, Pawn victim)
        {
            if (InvolvesPlayerPawn(rapist, victim))
                Messages.Message((rapist != null ? rapist.LabelShort : "Someone") + " left " + victim.LabelShort + " bound in a public spot.",
                    new LookTargets(victim), MessageTypeDefOf.ThreatBig, false);
        }

        private static void MoveHolokeyToHarasser(Pawn victim, Pawn harasser)
        {
            try
            {
                var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
                if (keyDef == null || victim.Map == null) return;
                var things = victim.Position.GetThingList(victim.Map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i].def != keyDef) continue;
                    var key = things[i];
                    // Only pocket the key if the captor is valid; otherwise leave it on the floor so the
                    // colony can still find it (never destroy it, or the device becomes unremovable).
                    if (harasser?.inventory != null && harasser.Spawned && !harasser.Dead)
                    {
                        key.DeSpawn();
                        harasser.inventory.innerContainer.TryAdd(key);
                    }
                    break;
                }
            }
            catch { }
        }

        private static void FreeUnlockedGear(Pawn p)
        {
            if (p?.apparel == null) return;
            var toRemove = new List<Apparel>();
            var worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                var app = worn[i];
                try { if (app.def is rjw.bondage_gear_def && !app.has_lock()) toRemove.Add(app); }
                catch { }
            }
            for (int i = 0; i < toRemove.Count; i++)
                try { p.apparel.TryDrop(toRemove[i], out _); } catch { }
        }

        // ── Onahole Extension compat: drag victim to a public onahole ─────────
        private static System.Type _onaholeBedType;
        private static List<ThingDef> _onaholeBeds;
        private static List<ThingDef> OnaholeBeds()
        {
            if (_onaholeBeds != null) return _onaholeBeds;
            _onaholeBeds = new List<ThingDef>();
            _onaholeBedType = GenTypes.GetTypeInAnyAssembly("RJW_Onahole.Building_OnaholeBed");
            if (_onaholeBedType == null)
            {
                Log.Warning("[RJW Sexual Harassment] onahole bed type RJW_Onahole.Building_OnaholeBed not found.");
                return _onaholeBeds;
            }
            foreach (var d in DefDatabase<ThingDef>.AllDefs)
                if (d.thingClass != null && !d.IsApparel && d.building != null && _onaholeBedType.IsAssignableFrom(d.thingClass))
                    _onaholeBeds.Add(d);
            if (_onaholeBeds.Count == 0)
                Log.Warning("[RJW Sexual Harassment] no concrete onahole bed defs found.");
            return _onaholeBeds;
        }

        /// <summary>Deferred entry: spawn an onahole at a public spot and send the rapist to drag the victim there.</summary>
        public static void TryStartOnaholeCapture(Pawn rapist, Pawn victim)
        {
            if (!S.enableOnaholeCapture || !SoftDeps.OnaholeActive) return;
            if (!Rand.Chance(S.onaholeCaptureChance)) return;
            DoOnaholeCapture(rapist, victim);
        }

        /// <summary>Spawns an onahole at a public spot and sends the rapist to drag the victim in. No chance roll (debug-callable). Returns true if started.</summary>
        public static bool DoOnaholeCapture(Pawn rapist, Pawn victim)
        {
            if (rapist?.jobs == null || victim == null || !victim.Spawned || victim.Dead) return false;
            if (!victim.RaceProps.Humanlike) return false;

            var def = PickOnaholeBed();
            if (def == null) return false;
            Map map = victim.Map;
            if (!TryFindPublicBedCell(map, def, victim.Position, out IntVec3 cell))
            {
                Log.Warning("[RJW Sexual Harassment] onahole capture: no free public cell found for " + def.defName + ".");
                return false;
            }

            try
            {
                var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                var bed = GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), cell, map, Rot4.North, WipeMode.Vanish);
                if (bed == null)
                {
                    Log.Warning("[RJW Sexual Harassment] onahole capture: GenSpawn.Spawn returned null for " + def.defName + " at " + cell + ".");
                    return false; // fall through to DoBoundInPublic
                }
                try { bed.SetFaction(Faction.OfPlayer); } catch { }
                var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_DragToOnahole, victim, bed);
                rapist.jobs.StartJob(job, JobCondition.InterruptForced);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] onahole capture failed: " + ex.Message, 0x5A1340);
                return false;
            }
        }

        public static void NotifyOnaholeCapture(Pawn rapist, Pawn victim, Thing bed)
        {
            if (victim == null) return;
            ApplyOnaholeBoundHediff(victim);
            // Social log entry: the captor seals them inside.
            if (rapist != null && rapist.Spawned && victim.Spawned)
                FireFlavorLine(rapist, victim, RJWSH_InteractionDefOf.RJWSH_OnaholeBind);
            if (InvolvesPlayerPawn(rapist, victim))
                Messages.Message((rapist != null ? rapist.LabelShort : "Someone") + " locked " + victim.LabelShort + " into a public " + (bed != null ? bed.def.label : "onahole") + ".",
                    new LookTargets(victim), MessageTypeDefOf.ThreatBig, false);
        }

        private static HediffDef _onaholeBoundDef;
        private static HediffDef OnaholeBoundDef
        {
            get
            {
                if (_onaholeBoundDef == null) _onaholeBoundDef = DefDatabase<HediffDef>.GetNamedSilentFail("RJWSH_OnaholeBound");
                return _onaholeBoundDef;
            }
        }

        /// <summary>Applies the "trapped in an onahole" hediff (a timer status), idempotent.</summary>
        public static void ApplyOnaholeBoundHediff(Pawn victim)
        {
            if (victim?.health == null || OnaholeBoundDef == null) return;
            if (victim.health.hediffSet.GetFirstHediffOfDef(OnaholeBoundDef) != null) return;
            try { victim.health.AddHediff(OnaholeBoundDef); } catch { }
        }

        public static void RemoveOnaholeBoundHediff(Pawn victim)
        {
            if (victim?.health == null || OnaholeBoundDef == null) return;
            var h = victim.health.hediffSet.GetFirstHediffOfDef(OnaholeBoundDef);
            if (h != null) try { victim.health.RemoveHediff(h); } catch { }
        }

        private static bool TryFindPublicBedCell(Map map, ThingDef bedDef, IntVec3 anchor, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null) return false;
            IntVec2 size = bedDef != null ? bedDef.size : IntVec2.One;
            bool Fits(IntVec3 c)
            {
                if (!c.InBounds(map) || c.Fogged(map)) return false;
                if (!map.reachability.CanReachColony(c)) return false;
                foreach (var cc in GenAdj.OccupiedRect(c, Rot4.North, size))
                    if (!cc.InBounds(map) || !cc.Standable(map) || cc.GetEdifice(map) != null || cc.GetFirstBuilding(map) != null)
                        return false;
                return true;
            }
            // Prefer a busy spot near the colony; otherwise settle for near where the act happened.
            if (CellFinder.TryFindRandomCellNear(ColonyCenter(map), map, 25, Fits, out cell)) return true;
            if (anchor.IsValid && CellFinder.TryFindRandomCellNear(anchor, map, 12, Fits, out cell)) return true;
            return false;
        }

        /// <summary>Called when a forced act finishes: a chance to extend the scene (drag the victim
        /// somewhere private and continue, capped per scene), otherwise resolve the post-rape restraint.</summary>
        public static void HandleSceneEnd(Pawn rapist, Pawn victim)
        {
            if (rapist == null || victim == null || !victim.Spawned || victim.Dead) return;
            var prof = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (S.enableSceneExtend && prof != null && prof.sceneExtendCount < S.maxSceneExtends
                && Rand.Chance(S.sceneExtendChance) && DragToPrivateAndContinue(rapist, victim))
            {
                prof.sceneExtendCount++;
                return;
            }
            if (prof != null) prof.sceneExtendCount = 0;
            HandlePostRapeRestraint(rapist, victim);
        }

        /// <summary>Sends the attacker to carry the victim to a private cell and continue the assault there.</summary>
        public static bool DragToPrivateAndContinue(Pawn rapist, Pawn victim)
        {
            if (rapist?.jobs == null || victim == null || !victim.Spawned || victim.Dead || !victim.RaceProps.Humanlike) return false;
            if (!FindPrivateCell(rapist, victim, out IntVec3 cell)) return false;
            try
            {
                var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_DragToPrivate, victim, cell);
                rapist.jobs.StartJob(job, JobCondition.InterruptForced);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] scene extend failed: " + ex.Message, 0x5A1342);
                return false;
            }
        }

        private static bool FindPrivateCell(Pawn rapist, Pawn victim, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Map map = victim.Map;
            if (map == null) return false;
            var bed = rapist.ownership?.OwnedBed ?? victim.ownership?.OwnedBed;
            if (bed != null && bed.Spawned && bed.Map == map && rapist.CanReach(bed, PathEndMode.OnCell, Danger.Some))
            { cell = bed.Position; return true; }
            return CellFinder.TryFindRandomCellNear(rapist.Position, map, 18, c =>
                c.InBounds(map) && c.Standable(map) && c.Roofed(map) && !c.Fogged(map)
                && rapist.CanReach(c, PathEndMode.OnCell, Danger.Some), out cell);
        }

        // ── Begging ─────────────────────────────────────────────
        private static readonly string[] BegMoteLines = { "Help!", "Let me go!", "No, please!", "Someone, help!", "Stop!", "Please!" };

        /// <summary>A floating cry for help over a pawn (used while carried, when SpeakUp bubbles can't show).</summary>
        // ── Drag plumbing: lead a conscious victim (stays spawned -> begs in real bubbles) or carry a downed one ──
        /// <summary>Grab the victim for a drag - always CARRIED (over-the-shoulder). The carried victim is
        /// despawned, so their begging shows via our own DragBubbleOverlay rather than the real bubble mods.</summary>
        public static DragMode BeginDragGrab(Pawn harasser, Pawn victim)
        {
            if (harasser?.carryTracker == null || victim == null || !victim.Spawned) return DragMode.Failed;
            victim.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            if (harasser.carryTracker.TryStartCarry(victim, 1, false) <= 0) return DragMode.Failed;
            return DragMode.Carried;
        }

        /// <summary>One pulse during the haul: the carried victim pleads via our custom over-the-carrier bubble
        /// (real bubble mods can't draw a despawned pawn), and the captor occasionally jeers via a real bubble.</summary>
        public static void DragBegTick(Pawn harasser, Pawn victim)
        {
            if (harasser == null || victim == null) return;
            bool carrying = harasser.carryTracker?.CarriedThing == victim;
            if (carrying)
                DragBubbleOverlay.Push(harasser, BegMoteLines[Rand.Range(0, BegMoteLines.Length)]);
            else if (victim.Spawned)
                FireBegLine(victim, RJWSH_InteractionDefOf.RJWSH_BegHelp, harasser);
            if (Rand.Chance(0.3f))
                FireFlavorLine(harasser, victim, RJWSH_InteractionDefOf.RJWSH_DragTaunt);
        }

        /// <summary>End of haul: drop a carried victim (and clear their lingering plea bubble) at the destination.</summary>
        public static void EndDrag(Pawn harasser, Pawn victim, IntVec3 dropCell)
        {
            DragBubbleOverlay.ClearFor(harasser);
            if (harasser?.carryTracker?.CarriedThing != null)
            {
                harasser.carryTracker.TryDropCarriedThing(dropCell.IsValid ? dropCell : harasser.Position, ThingPlaceMode.Near, out _);
            }
            else if (victim != null && victim.Spawned && victim.CurJobDef == RJWSH_JobDefOf.RJWSH_BeingLed)
            {
                victim.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
        }

        public static void ThrowBegMote(Pawn at)
        {
            try
            {
                if (at?.Map == null || !at.Spawned) return;
                MoteMaker.ThrowText(at.DrawPos + new UnityEngine.Vector3(0f, 0f, 0.7f), at.Map,
                    BegMoteLines[Rand.Range(0, BegMoteLines.Length)], UnityEngine.Color.white, 2.5f);
            }
            catch { }
        }

        /// <summary>A trapped/bound victim cries to the nearest free colonist (SpeakUp bubble), or throws a mote.</summary>
        public static void BegForHelp(Pawn victim)
        {
            FireBegLine(victim, RJWSH_InteractionDefOf.RJWSH_BegHelp);
        }

        public static bool WearingLockedHarassmentGear(Pawn p)
        {
            try { return p != null && p.is_wearing_locked_apparel(); } catch { return false; }
        }

        private static System.Type OnaholeBedTypeCached()
        {
            if (_onaholeBedType == null) _onaholeBedType = GenTypes.GetTypeInAnyAssembly("RJW_Onahole.Building_OnaholeBed");
            return _onaholeBedType;
        }

        private static System.Type _beOnaholeType;
        private static System.Type BeOnaholeJobType()
        {
            if (_beOnaholeType == null) _beOnaholeType = GenTypes.GetTypeInAnyAssembly("RJW_Onahole.Jobs.JobDriver_BeOnahole");
            return _beOnaholeType;
        }

        /// <summary>True if the pawn is bound in an onahole: running the BeOnahole job, or in/owning an onahole bed.</summary>
        public static bool IsInOnaholeBed(Pawn p)
        {
            if (p?.jobs == null) return false;
            try
            {
                var bt = BeOnaholeJobType();
                if (bt != null && p.jobs.curDriver != null && bt.IsInstanceOfType(p.jobs.curDriver)) return true;
                var ot = OnaholeBedTypeCached();
                if (ot == null) return false;
                var bed = p.CurrentBed();
                if (bed != null && ot.IsInstanceOfType(bed)) return true;
                var owned = p.ownership?.OwnedBed;
                return owned != null && ot.IsInstanceOfType(owned);
            }
            catch { return false; }
        }

        /// <summary>Reflection accessor for an onahole bed's BoundPawn (the onahole assembly isn't referenced).</summary>
        private static System.Reflection.PropertyInfo _boundPawnProp;
        public static Pawn OnaholeBoundPawn(Thing bed)
        {
            if (bed == null) return null;
            try
            {
                if (_boundPawnProp == null) _boundPawnProp = bed.GetType().GetProperty("BoundPawn");
                return _boundPawnProp?.GetValue(bed) as Pawn;
            }
            catch { return null; }
        }

        // ── Evil pawns scavenge keys + lord over collared pawns ───────────────────────
        /// <summary>How cruel/evil a pawn is (0+). Blends our morality, vile vanilla traits, bad karma, and
        /// Rimpsyche's Compassion facet.</summary>
        public static float Evilness(Pawn p)
        {
            if (p == null) return 0f;
            if (p.RaceProps != null && p.RaceProps.IsMechanoid) return 1f; // mechanoids have no compunction - always qualify as controllers
            float e = 0f;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof != null) e += prof.morality == Morality.Evil ? 1f : (prof.morality == Morality.Questionable ? 0.45f : 0f);
            var traits = p.story?.traits;
            if (traits != null)
            {
                if (HasTraitNamed(traits, "Psychopath")) e += 0.7f;
                if (HasTraitNamed(traits, "Bloodlust")) e += 0.5f;
                if (HasTraitNamed(traits, "Cannibal")) e += 0.3f;
                if (HasTraitNamed(traits, "Greedy")) e += 0.2f;
                if (HasTraitNamed(traits, "Sadist")) e += 0.5f; // RJW / other mods
            }
            if (KarmaBridge.TryGetKarma(p, out float k) && k < 0f) e += System.Math.Min(0.5f, -k / 100f);
            e += RimpsycheBridge.Cruelty(p) * 0.5f;
            if (GeneHelper.IsPredator(p)) e += 0.4f;   // Biotech predator gene
            return e;
        }

        /// <summary>Non-gene conditioning receptivity from soft-deps: RJW quirks (Cumslut/Buttslut/Exhibitionist)
        /// and accumulated Sexperience lust make a pet break faster. 1.0 when neither mod is present. Multiply
        /// onto conditioning gains alongside GeneHelper.ConditioningGainFactor.</summary>
        public static float ConditioningReceptivity(Pawn p)
        {
            if (p == null) return 1f;
            return QuirksBridge.ReceptivityFactor(p) * SexperienceBridge.LustReceptivity(p);
        }

        private static bool HasTraitNamed(RimWorld.TraitSet traits, string defName)
        {
            var td = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return td != null && traits.HasTrait(td);
        }

        /// <summary>Periodic: a sufficiently evil pawn near a loose Holokey that locks someone grabs it; and an
        /// evil pawn already holding the key to a conditioned, collared pawn starts ordering them around.</summary>
        public static void EvilKeyScavenge(Map map)
        {
            if (map == null || !S.enableKeyScavenging) return;
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail("Holokey");
            if (keyDef == null) return;

            // Phase A: grab a loose key off the ground.
            var ground = map.listerThings.ThingsOfDef(keyDef);
            for (int i = 0; i < ground.Count; i++)
            {
                var key = ground[i];
                if (key == null || !key.Spawned) continue;
                var kc = key.TryGetComp<rjw.CompHoloCryptoStamped>();
                if (kc == null || FindLockedVictimForKey(kc, map) == null) continue; // only keys that lock someone
                Pawn picker = null; float bestE = 0.7f;
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    var p = pawns[j];
                    if (p == null || p.Dead || p.Downed || !p.Awake() || p.inventory == null || !p.RaceProps.Humanlike) continue;
                    if (p.jobs?.curDriver is rjw.JobDriver_Sex) continue;
                    if (p.Position.DistanceTo(key.Position) > 20f) continue;
                    if (p.health?.capacities == null || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                    float e = Evilness(p);
                    if (e > bestE && p.CanReserveAndReach(key, PathEndMode.ClosestTouch, Danger.Some)) { bestE = e; picker = p; }
                }
                if (picker != null && Rand.Chance(Mathf01((bestE - 0.5f) * 0.6f)))
                {
                    var job = JobMaker.MakeJob(JobDefOf.TakeInventory, key);
                    job.count = 1;
                    picker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }

            // Phase B: an evil key-holder lords over the conditioned, collared pawn it can unlock.
            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                var holder = all[i];
                if (holder?.inventory == null || holder.Dead || Evilness(holder) < 0.7f) continue;
                var inv = holder.inventory.innerContainer;
                for (int k = 0; k < inv.Count; k++)
                {
                    if (inv[k].def != keyDef) continue;
                    var kc = inv[k].TryGetComp<rjw.CompHoloCryptoStamped>();
                    if (kc == null) continue;
                    var victim = FindLockedVictimForKey(kc, map);
                    if (victim == null || !WearingControlCollar(victim)) continue;
                    var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
                    if (vp == null || !vp.IsConditioned || vp.ownerId == holder.thingIDNumber) continue;
                    MarkAiControlled(victim, holder);
                    if (InvolvesPlayerPawn(holder, victim))
                        Messages.Message(holder.LabelShort + " seized the key and started ordering " + victim.LabelShort + " around.",
                            new LookTargets(holder, victim), MessageTypeDefOf.NegativeEvent, false);
                }
            }

            // Phase C: a hostile raider loots a slave's key off a DOWNED/DEAD colony key-holder, then takes
            // control of the slave - setting up a kidnapping if the raider escapes the map with the key.
            if (S.allowVictimAggressors)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var holder = all[i];
                    if (holder?.inventory == null || !IsPlayerOwned(holder) || (!holder.Downed && !holder.Dead)) continue;
                    var inv = holder.inventory.innerContainer;
                    for (int k = inv.Count - 1; k >= 0; k--)
                    {
                        if (inv[k].def != keyDef) continue;
                        var kc = inv[k].TryGetComp<rjw.CompHoloCryptoStamped>();
                        if (kc == null) continue;
                        var victim = FindLockedVictimForKey(kc, map);
                        if (victim == null) continue;
                        var raider = NearestHostileLooter(holder);
                        if (raider == null) continue;
                        var key = inv[k];
                        inv.Remove(key);
                        if (!raider.inventory.innerContainer.TryAdd(key, true)) { GenPlace.TryPlaceThing(key, raider.Position, map, ThingPlaceMode.Near); continue; }
                        MarkAiControlled(victim, raider);
                        Messages.Message(raider.LabelShort + " looted the key to " + victim.LabelShort + " off " + holder.LabelShort + " and now controls them.",
                            new LookTargets(raider, victim), MessageTypeDefOf.NegativeEvent, false);
                        break;
                    }
                }
            }
        }

        private static Pawn NearestHostileLooter(Pawn near)
        {
            var map = near?.Map; if (map == null) return null;
            Pawn best = null; float bestD = 12f;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed || !p.RaceProps.Humanlike || p.inventory == null) continue;
                if (!p.HostileTo(Faction.OfPlayer)) continue;
                if (p.health?.capacities == null || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                float d = p.Position.DistanceTo(near.Position);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Periodic: any nearby pawn may wander over and pocket a scandalous photo left on the ground
        /// (even sitting in a stockpile) - juicy leverage nobody leaves lying around. Everyone is a little
        /// curious, but cruel/greedy pawns (by the Evilness blend: morality, dark traits, bad karma, Rimpsyche
        /// compassion, predator gene) are far likelier to snatch one. The photo's own subject never grabs their
        /// own evidence here. A hostile who pockets a colonist's photo carries the blackmail off the map.</summary>
        public static void PhotoScavenge(Map map)
        {
            if (map == null || S == null || !S.enablePhotoScavenging) return;
            var photoDef = RJWSH_ThingDefOf.RJWSH_ScandalousPhoto;
            if (photoDef == null) return;

            var ground = map.listerThings.ThingsOfDef(photoDef);
            for (int i = 0; i < ground.Count; i++)
            {
                var photo = ground[i];
                if (photo == null || !photo.Spawned) continue;
                Pawn subject = photo.TryGetComp<CompScandalousPhoto>()?.subject;

                // Pick the pawn who wants it most: baseline curiosity for anyone, scaled hard by cruelty/greed.
                Pawn picker = null; float bestScore = 0f;
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    var p = pawns[j];
                    if (p == null || p == subject || p.Dead || p.Downed || !p.Awake()) continue;
                    if (!p.RaceProps.Humanlike || p.inventory == null || p.IsPrisoner) continue;
                    if (p.jobs?.curDriver is rjw.JobDriver_Sex || IsBusyInAct(p)) continue;
                    if (p.Position.DistanceTo(photo.Position) > 26f) continue;
                    if (p.health?.capacities == null || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                    float score = 0.12f + Evilness(p) * 0.55f + Rand.Value * 0.05f;
                    if (score > bestScore && p.CanReserveAndReach(photo, PathEndMode.ClosestTouch, Danger.Some))
                    { bestScore = score; picker = p; }
                }

                if (picker == null || !Rand.Chance(Mathf01(bestScore * 0.8f))) continue;

                var job = JobMaker.MakeJob(JobDefOf.TakeInventory, photo);
                job.count = 1;
                picker.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                // A hostile pocketing a colonist's photo is blackmail walking off the map - tell the player.
                if (subject != null && IsPlayerOwned(subject) && picker.HostileTo(Faction.OfPlayer))
                    Messages.Message(picker.LabelShort + " snatched a scandalous photo of " + subject.LabelShort
                        + " off the ground.", new LookTargets(picker, photo), MessageTypeDefOf.NegativeEvent, false);
            }
        }

        // ── AI collar control: behavior, slave will, key refusal ───────────────────────
        /// <summary>Flags a victim as controlled by an AI pawn (not the player): locks the player's gizmos,
        /// turns on follow + auto-service, and starts the will meter.</summary>
        public static void MarkAiControlled(Pawn victim, Pawn controller)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null || controller == null) return;
            vp.ownerId = controller.thingIDNumber;
            vp.aiControlled = true;
            vp.autoService = true;
            vp.followOwner = true;
            EnsureSlaveWillHediff(victim);
            EnsureOwnerRelation(controller, victim);
        }

        public static ControllerBehavior PickControllerBehavior(Pawn controller)
        {
            try
            {
                if (xxx.is_zoophile(controller)) return ControllerBehavior.Bestiality;
                if (IsSadist(controller)) return ControllerBehavior.BeatThem;            // sadist beats the slave
                if (xxx.is_masochist(controller)) return ControllerBehavior.MakeThemHitMe; // masochist: slave strikes them
            }
            catch { }
            return ControllerBehavior.Service;
        }

        private static TraitDef _sadistOwn, _sadistExternal;
        private static bool _sadistTried;
        /// <summary>Sadist if the pawn has our RJWSH_Sadist trait, or an external "Sadist" trait (RJW/other mods).</summary>
        public static bool IsSadist(Pawn p)
        {
            var traits = p?.story?.traits;
            if (traits == null) return false;
            if (!_sadistTried)
            {
                _sadistTried = true;
                _sadistExternal = DefDatabase<TraitDef>.GetNamedSilentFail("Sadist");
                _sadistOwn = DefDatabase<TraitDef>.GetNamedSilentFail("RJWSH_Sadist");
            }
            if (_sadistExternal != null && traits.HasTrait(_sadistExternal)) return true;
            return _sadistOwn != null && traits.HasTrait(_sadistOwn);
        }

        /// <summary>Runs the controller's preferred act on the slave (personality-driven).</summary>
        public static void RunControllerBehavior(Pawn controller, Pawn victim)
        {
            if (controller == null || victim == null || !controller.Spawned || !victim.Spawned) return;
            if (IsBusyInAct(controller) || IsBusyInAct(victim)) return;
            switch (PickControllerBehavior(controller))
            {
                case ControllerBehavior.MakeThemHitMe: ForceMelee(victim, controller); break;   // masochist: slave strikes them
                case ControllerBehavior.BeatThem: ForceMelee(controller, victim); break;         // sadist beats the slave
                case ControllerBehavior.Bestiality:
                    var animal = FindNearbyAnimal(victim);
                    if (animal != null && BestialityEnabled()) StartBestiality(victim, animal);
                    else RunService(victim, controller, null);
                    break;
                default: RunService(victim, controller, null); break;
            }
            DeepenConditioning(victim);
        }

        // Unarmed melee strike (Melee Animation auto-animates). Light, occasional - the control upkeep paces it.
        // Unarmed melee strike (Melee Animation auto-animates), capped non-lethal: never strike a downed or
        // badly-hurt target so the beatings can't kill.
        private static void ForceMelee(Pawn attacker, Pawn target)
        {
            try
            {
                if (attacker?.meleeVerbs == null || target == null || !target.Spawned || target.Dead || target.Downed) return;
                // Stop WELL before death: never beat a target below half health.
                if (target.health?.summaryHealth != null && target.health.summaryHealth.SummaryHealthPercent < 0.5f) return;
                if (attacker.Position.DistanceTo(target.Position) > 2f) return;
                attacker.meleeVerbs.TryMeleeAttack(target, GetUnarmedVerb(attacker)); // force fists, never an equipped weapon
            }
            catch { }
        }

        // The attacker's unarmed/body melee verb (fists). Forces discipline to be bare-handed, not with a
        // knife/club that would maim or kill.
        // The owner's equipped melee-weapon verb (if any), so Melee Animation can animate the swing/execution.
        private static Verb GetMeleeWeaponVerb(Pawn attacker)
        {
            try
            {
                var list = attacker.meleeVerbs.GetUpdatedAvailableVerbsList(false);
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i].verb;
                    if (v != null && v.EquipmentSource != null && v.IsMeleeAttack) return v;
                }
            }
            catch { }
            return null;
        }

        private static Verb GetUnarmedVerb(Pawn attacker)
        {
            try
            {
                var list = attacker.meleeVerbs.GetUpdatedAvailableVerbsList(false);
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i].verb;
                    if (v != null && v.EquipmentSource == null && v.IsMeleeAttack) return v;
                }
            }
            catch { }
            return null;
        }

        // ── Flee the beating + owner retaliation ──────────────────────────────
        /// <summary>Uncapped bare-fist strike for a beatdown (beats past the non-lethal cap). A non-lethal
        /// beatdown stops naturally once the target is downed; a lethal one keeps going until death.</summary>
        public static void ForceMeleeBeatdown(Pawn attacker, Pawn target, bool lethal)
        {
            try
            {
                if (attacker?.meleeVerbs == null || target == null || !target.Spawned || target.Dead) return;
                if (!lethal && target.Downed) return;
                if (attacker.Position.DistanceTo(target.Position) > 2f) return;
                // Lethal rage: swing the owner's equipped melee weapon if they have one, so Melee Animation (which
                // only animates WEAPON melee - it has no unarmed anims) plays its swing/execution. Non-lethal
                // discipline stays fists to avoid an accidental kill.
                Verb verb = lethal ? (GetMeleeWeaponVerb(attacker) ?? GetUnarmedVerb(attacker)) : GetUnarmedVerb(attacker);
                attacker.meleeVerbs.TryMeleeAttack(target, verb);
                PlaySoundClip("Pawn_Melee_Punch_HitPawn", target); // consistent thud even between real hits
            }
            catch { }
        }

        public static void FinishBeatdown(Pawn owner, Pawn victim, bool lethal)
        {
            if (victim == null) return;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp != null && !victim.Dead) vp.ApplyCond("Beaten down", 10f, -8f);
            // A beatdown crushes will/spirit/esteem, adds trauma, and breaks them toward submission.
            if (!victim.Dead) { AttrDelta(victim, will: -6f, spirit: -6f, esteem: -4f, trauma: 4f, subdom: -7f); ShiftSubDom(owner, 4f); }
            if (InvolvesPlayerPawn(owner, victim))
            {
                if (victim.Dead)
                    Messages.Message(owner.LabelShort + " beat " + victim.LabelShort + " to death.", new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
                else if (victim.Downed)
                    Messages.Message(owner.LabelShort + " beat " + victim.LabelShort + " down for trying to run.", new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            }
        }

        public static void StartBeatdown(Pawn owner, Pawn victim, bool lethal)
        {
            if (owner?.jobs == null || victim == null) return;
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Beatdown, victim);
            job.count = lethal ? 1 : 0;
            job.playerForced = true;
            owner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>A disciplined pet with enough spirit/will may bolt from the beating; if they do, the owner
        /// escalates. Returns true if the pet fled (so the normal discipline is aborted).</summary>
        public static bool TryFleeBeating(Pawn owner, Pawn victim)
        {
            if (!S.enableFleeBeating || owner == null || victim == null || !victim.Spawned) return false;
            if (victim.Downed || victim.health == null || !victim.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return false;
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return false;
            var sx = vp.SexAttr(victim);
            float chance = UnityEngine.Mathf.Clamp01(0.05f + sx.spirit / 100f * 0.35f + sx.willpower / 100f * 0.25f
                + (1f - vp.hypnosisLevel / 100f) * 0.20f + EscapeWindowBonus(victim));
            if (!Rand.Chance(chance)) return false;

            FleeFurther(victim, owner);
            ThrowControlMote(victim, "!", new UnityEngine.Color(1f, 0.9f, 0.3f));
            if (InvolvesPlayerPawn(owner, victim))
                Messages.Message(victim.LabelShort + " tried to flee the beating!", new LookTargets(victim), MessageTypeDefOf.ThreatSmall, false);
            StartPursue(owner, victim); // the owner gives chase; the retaliation is decided once they catch up
            return true;
        }

        /// <summary>Sends the pet running to a far cell away from the given pawn.</summary>
        public static void FleeFurther(Pawn victim, Pawn from)
        {
            if (victim?.jobs == null || from == null) return;
            if (TryFindFleeCell(victim, from, out IntVec3 cell))
                victim.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobCondition.InterruptForced);
        }

        /// <summary>Starts the owner chasing a fleeing pet. On catch, OnCaughtFlee fires the retaliation.</summary>
        public static void StartPursue(Pawn owner, Pawn victim)
        {
            if (owner?.jobs == null || victim == null) return;
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_PursueFlee, victim);
            job.playerForced = true;
            owner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>The owner has run the fleeing pet down - now the punishment lands.</summary>
        public static void OnCaughtFlee(Pawn owner, Pawn victim)
        {
            if (owner == null || victim == null || !victim.Spawned) return;
            if (InvolvesPlayerPawn(owner, victim))
                Messages.Message(owner.LabelShort + " ran " + victim.LabelShort + " down.", new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            DoFleeRetaliation(owner, victim);
        }

        private static void DoFleeRetaliation(Pawn owner, Pawn victim)
        {
            // Beat-to-death is a rare, config-gated outcome; otherwise a harsh punishment at random.
            if (S.enableBeatToDeath && Rand.Chance(S.beatToDeathChance))
            {
                if (InvolvesPlayerPawn(owner, victim))
                    Messages.Message(owner.LabelShort + " flew into a rage at " + victim.LabelShort + "'s defiance.", new LookTargets(victim), MessageTypeDefOf.ThreatBig, false);
                StartBeatdown(owner, victim, true);
                return;
            }
            switch (Rand.RangeInclusive(0, 2))
            {
                case 0: TimedOnaholePunish(owner, victim, 7500); break; // a spell in an onahole
                case 1: StartBeatdown(owner, victim, false); break;     // beat until downed
                default: ArrangeGangbang(owner, victim); break;         // arranged gangbang
            }
        }

        private static bool TryFindFleeCell(Pawn victim, Pawn owner, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var map = victim.Map; if (map == null) return false;
            IntVec3 from = owner?.Position ?? victim.Position;
            float baseDist = victim.Position.DistanceTo(from);
            return CellFinder.TryFindRandomCellNear(victim.Position, map, 18, c =>
                c.InBounds(map) && c.Standable(map) && !c.Fogged(map)
                && c.DistanceTo(from) > baseDist
                && victim.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out cell);
        }

        public static void ArrangeGangbang(Pawn owner, Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null || victim?.Map == null) return;
            vp.gangbangCount = Rand.RangeInclusive(3, 5);
            vp.gangbangUntil = Find.TickManager.TicksGame + 20000; // ~8h window
            if (InvolvesPlayerPawn(owner, victim))
                Messages.Message(owner.LabelShort + " arranged a gangbang to punish " + victim.LabelShort + "'s defiance.", new LookTargets(victim), MessageTypeDefOf.ThreatBig, false);
            GangbangTick(victim, vp); // kick it off immediately
        }

        /// <summary>Advances the gangbang while the window is open. If the victim is already being used and MMF
        /// group animations are enabled, pulls an EXTRA aggressor to join the same act and registers them on the
        /// receiver's partner list so c0ffee Animations composes a group (MMF+) animation. Otherwise starts the
        /// next aggressor once the victim is free (sequential fallback).</summary>
        public static void GangbangTick(Pawn victim, PawnProfile vp)
        {
            if (victim == null || !victim.Spawned || victim.Dead) { if (vp != null) vp.gangbangCount = 0; return; }
            if (vp == null || vp.gangbangCount <= 0 || Find.TickManager.TicksGame >= vp.gangbangUntil) { if (vp != null) vp.gangbangCount = 0; return; }

            // Is the victim currently in an RJW receiver (being-used) job?
            var receiver = victim.jobs?.curDriver as rjw.JobDriver_SexBaseReciever;
            if (receiver != null)
            {
                if (!S.enableGangbangMMF) return; // sequential mode: wait for the current aggressor to finish
                int active = 1; try { active = System.Math.Max(1, receiver.parteners.Count); } catch { }
                if (active >= System.Math.Max(2, S.gangbangMaxActors)) return; // group is already full
                var joiner = PickGangbangAggressor(victim);
                if (joiner != null && TryForceRape(joiner, victim))
                {
                    // Register the joiner as a partner of the ongoing receiver so the animation framework
                    // (which reads receiver.parteners on the initiator's Start) selects a group animation.
                    try { if (!receiver.parteners.Contains(joiner)) receiver.parteners.Add(joiner); } catch { }
                    vp.gangbangCount--;
                }
                return;
            }

            if (IsBusyInAct(victim)) return; // busy in an animation but not a receiver job - leave it be
            var aggressor = PickGangbangAggressor(victim);
            if (aggressor != null && TryForceRape(aggressor, victim)) vp.gangbangCount--;
        }

        private static Pawn PickGangbangAggressor(Pawn victim)
        {
            var map = victim.Map; if (map == null) return null;
            Pawn best = null; float bestDist = 9999f;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == victim || p.Dead || p.Downed || p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (p.IsPrisonerOfColony) continue;
                if (IsBusyInAct(p)) continue;
                if (!GenderOk(p, victim)) continue; // heterosexual-only gate
                try { if (!xxx.can_rape(p, true)) continue; } catch { continue; }
                float d = victim.Position.DistanceTo(p.Position);
                if (d < bestDist && d <= 30f && p.CanReach(victim, PathEndMode.Touch, Danger.Some)) { bestDist = d; best = p; }
            }
            return best;
        }

        /// <summary>Each use by the key-holder deepens conditioning, scaled by the victim's RJW vulnerability.</summary>
        public static void DeepenConditioning(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp == null) return;
            // Slower per-use gain, scaled by the pet's susceptibility so resistant pawns need many more uses to break.
            vp.hypnosisLevel = System.Math.Min(100f, vp.hypnosisLevel + (0.2f + VulnerabilityScore(victim) * 0.8f) * BreakSusceptibility(victim));
        }

        /// <summary>Per-pawn conditioning susceptibility, rolled once and cached on the profile. Most pawns are
        /// resistant (0.55-1.05) and need much more conditioning/abuse to break; a rare ~10% are highly susceptible
        /// (1.8-2.8) and progress fast. Traits nudge it (masochist/wimp/kind up, tough/psychopath/bloodlust down).
        /// Multiplies conditioning gains and eases the Masochist/Stockholm abuse gates.</summary>
        public static float BreakSusceptibility(Pawn p)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof == null) return 1f;
            if (prof.breakSusceptibility < 0f)
            {
                float s = Rand.Value < 0.10f ? Rand.Range(1.8f, 2.8f) : Rand.Range(0.55f, 1.05f);
                var tr = p?.story?.traits;
                if (tr != null)
                {
                    if (HasTraitNamed(tr, "Masochist")) s *= 1.4f;
                    if (HasTraitNamed(tr, "Wimp")) s *= 1.3f;
                    if (HasTraitNamed(tr, "Kind")) s *= 1.15f;
                    if (HasTraitNamed(tr, "Tough")) s *= 0.7f;
                    if (HasTraitNamed(tr, "Psychopath")) s *= 0.8f;
                    if (HasTraitNamed(tr, "Bloodlust")) s *= 0.85f;
                }
                s *= GeneHelper.SusceptibilityGeneFactor(p);   // Biotech conditioning genes bend the roll
                prof.breakSusceptibility = UnityEngine.Mathf.Clamp(s, 0.35f, 3f);
            }
            return prof.breakSusceptibility;
        }

        /// <summary>The collared pawn may freely attend its own needs when the owner allows it, or is asleep.</summary>
        public static bool NeedsAllowed(PawnProfile prof, Pawn owner)
        {
            if (prof != null && prof.allowNeeds) return true;
            return owner != null && owner.Spawned && !owner.Dead && !owner.Awake();
        }

        /// <summary>A psycast-style two-bar readout (conditioning + will) on a collared pawn.</summary>
        public static Gizmo BuildConditionedGizmo(Pawn p)
        {
            if (p?.apparel == null || !WearingControlCollar(p)) return null;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (vp == null) return null;
            return new Gizmo_Conditioning(p);
        }

        private static Pawn FindNearbyAnimal(Pawn near)
        {
            if (near?.Map == null) return null;
            Pawn best = null; float bd = 30f;
            var pawns = near.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var a = pawns[i];
                if (a == null || !a.RaceProps.Animal || a.Dead || a.Downed) continue;
                float d = near.Position.DistanceTo(a.Position);
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        public static void EnsureSlaveWillHediff(Pawn p)
        {
            if (p?.health == null) return;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("RJWSH_SlaveWill");
            if (def == null || p.health.hediffSet.HasHediff(def)) return;
            p.health.AddHediff(def);
        }

        public static void RemoveSlaveWillHediff(Pawn p)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("RJWSH_SlaveWill");
            var h = def != null ? p?.health?.hediffSet?.GetFirstHediffOfDef(def) : null;
            if (h != null) p.health.RemoveHediff(h);
        }

        /// <summary>Periodic breakout roll for an AI-controlled slave. Returns true if they broke free (control
        /// ended). High will -> likelier to break free; a failed attempt drops will and ends in public humiliation.</summary>
        public static bool SlaveWillBreakoutTick(Pawn victim, Pawn controller)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp == null || !S.enableSlaveWill) return false;
            // Conditioning suppresses the will to escape: low conditioning -> a real chance to break free.
            float will = Mathf01(1f - vp.hypnosisLevel / 100f);
            if (Rand.Chance(will * S.breakoutChanceFactor))
            {
                vp.aiControlled = false; vp.ownerId = -1; vp.autoService = false; vp.followOwner = false;
                if (InvolvesPlayerPawn(controller, victim))
                    Messages.Message(victim.LabelShort + " broke free of " + (controller != null ? controller.LabelShort : "their captor") + "'s control!",
                        new LookTargets(victim), MessageTypeDefOf.PositiveEvent, false);
                return true;
            }

            vp.ApplyCond("Breakout stopped", 6f, 0f); // failed -> conditioning deepens
            bool punished = controller != null &&
                ((S.enableOnaholeCapture && SoftDeps.OnaholeActive && DoOnaholeCapture(controller, victim))
                 || DoBoundInPublic(controller, victim));
            if (punished && InvolvesPlayerPawn(controller, victim))
                Messages.Message(victim.LabelShort + "'s escape attempt failed and they were made an example of.",
                    new LookTargets(victim), MessageTypeDefOf.NegativeEvent, false);
            return false;
        }

        /// <summary>Name of the pawn currently AI-controlling the victim, for the locked-gizmo label.</summary>
        public static string ControllerLabel(Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp == null || vp.ownerId < 0) return null;
            return FindPawnByIdAnyMap(vp.ownerId)?.LabelShort;
        }

        private static Pawn FindPawnByIdAnyMap(int id)
        {
            foreach (var map in Find.Maps)
            {
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                    if (pawns[i].thingIDNumber == id) return pawns[i];
            }
            return null;
        }

        /// <summary>True if dropping this Holokey should be refused (the holder is an AI controller using it).</summary>
        public static bool IsRefusedKeyDrop(Pawn holder, Thing t)
        {
            if (!S.enableKeyRefuse || holder == null || t == null) return false;
            // A Holokey is identified by its stamp comp, not its defName (robust vs. Simplekey/renames).
            var kc = t.TryGetComp<rjw.CompHoloCryptoStamped>();
            if (kc == null) return false;
            foreach (var map in Find.Maps)
            {
                var v = FindLockedVictimForKey(kc, map);
                if (v == null) continue;
                var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(v);
                if (vp != null && vp.aiControlled && vp.ownerId == holder.thingIDNumber) return true;
            }
            return false;
        }

        // ── Fleeing with the key: a non-colonist who locked a colonist leaves the map with the only key ──
        /// <summary>On any pawn leaving the map: if a non-colonist carries the key to a locked colony pawn,
        /// they become a fugitive - fire a recovery letter and (if Simple Warrants is active) post a warrant.</summary>
        public static void OnPawnLeftMap(Pawn leaver)
        {
            try
            {
                if (leaver == null) return;
                TrySatisfiedClientGossip(leaver); // a happy client talks the colony up as they go
                if (!S.allowVictimAggressors || leaver.IsColonist) return;
                var keys = leaver.inventory?.innerContainer;
                if (keys == null || keys.Count == 0) return;
                for (int i = 0; i < keys.Count; i++)
                {
                    var kc = keys[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                    if (kc == null) continue;
                    var victim = FindLockedColonyPawnForKey(kc);
                    if (victim != null) { TriggerKeyFugitive(leaver, victim); return; }
                }
            }
            catch { }
        }

        private static Pawn FindLockedColonyPawnForKey(rjw.CompHoloCryptoStamped kc)
        {
            foreach (var map in Find.Maps)
            {
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsPlayerOwned(p) || p.apparel == null) continue;
                    var worn = p.apparel.WornApparel;
                    for (int j = 0; j < worn.Count; j++)
                    {
                        var ac = worn[j].TryGetComp<rjw.CompHoloCryptoStamped>();
                        if (ac != null && kc.matches(ac)) return p;
                    }
                }
            }
            return null;
        }

        private static void TriggerKeyFugitive(Pawn fugitive, Pawn victim)
        {
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            // If the fugitive was controlling the slave (or the slave is broken in enough to be dragged), they
            // are kidnapped off the map with the key; a high-will, unconditioned slave is left behind.
            bool kidnap = victim.Spawned && vp != null && (vp.ownerId == fugitive.thingIDNumber || vp.hypnosisLevel >= 50f);
            if (kidnap)
            {
                try { victim.ExitMap(false, Rot4.Random); } catch { kidnap = false; }
            }
            if (kidnap)
            {
                Find.LetterStack.ReceiveLetter("Slave kidnapped",
                    fugitive.LabelShortCap + " dragged " + victim.LabelShort + " off the map by their leash, carrying the only key. Hunt " + fugitive.LabelShort + " down to get " + victim.LabelShort + " back.",
                    LetterDefOf.ThreatBig);
                TryIssueWarrant(fugitive, "kidnapping " + victim.LabelShort);
            }
            else
            {
                Find.LetterStack.ReceiveLetter("Key stolen",
                    fugitive.LabelShortCap + " has slipped away carrying the only key to " + victim.LabelShort +
                    "'s locked restraints. Track them down and capture them to recover it - their inventory still holds the key.",
                    LetterDefOf.ThreatBig, new LookTargets(victim));
                TryIssueWarrant(fugitive, "abducting the key to " + victim.LabelShort + "'s restraints");
            }
        }

        private static void TryIssueWarrant(Pawn fugitive, string reason)
        {
            try
            {
                var wmType = GenTypes.GetTypeInAnyAssembly("SimpleWarrants.WarrantsManager");
                if (wmType == null) return;
                var inst = wmType.GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null);
                if (inst == null) return;
                var m = wmType.GetMethod("PutWarrantOn", new[] { typeof(Pawn), typeof(string), typeof(Faction) });
                m?.Invoke(inst, new object[] { fugitive, reason, null });
            }
            catch (System.Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] warrant issue failed: " + ex.Message, 0x5A1343); }
        }

        /// <summary>True if the holder carries a Holokey matching one of the locked pieces on the bound pawn.</summary>
        public static bool HoldsKeyForLockedPawn(Pawn holder, Pawn locked)
        {
            var keys = holder?.inventory?.innerContainer;
            if (keys == null || locked?.apparel == null) return false;
            var worn = locked.apparel.WornApparel;
            for (int k = 0; k < keys.Count; k++)
            {
                var kc = keys[k].TryGetComp<rjw.CompHoloCryptoStamped>();
                if (kc == null) continue;
                for (int i = 0; i < worn.Count; i++)
                {
                    var ac = worn[i].TryGetComp<rjw.CompHoloCryptoStamped>();
                    if (ac != null && kc.matches(ac)) return true;
                }
            }
            return false;
        }

        /// <summary>True if the pawn wears the control collar or any locked device - i.e. someone holds a key over them.</summary>
        public static bool IsLockedPawn(Pawn p)
        {
            if (p?.apparel == null) return false;
            var worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].def == RJWSH_ThingDefOf.RJWSH_ControlCollar || worn[i].has_lock()) return true;
            return false;
        }

        /// <summary>The player-side pawn currently carrying a matching Holokey for this locked pawn, or null.</summary>
        public static Pawn FindKeyHolderFor(Pawn v)
        {
            var map = v?.Map;
            if (map == null) return null;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var h = pawns[i];
                if (h == v || h.inventory == null) continue;
                bool playerSide = (h.Faction != null && h.Faction.IsPlayer) || h.IsPrisonerOfColony || h.IsSlaveOfColony;
                if (!playerSide) continue;
                if (HoldsKeyForLockedPawn(h, v)) return h;
            }
            return null;
        }

        /// <summary>Picks ONE post-rape restraint by priority so the mechanics never fight over the rapist's
        /// job: public onahole capture, else bound-in-public, else a plain device lock in place.</summary>
        public static void HandlePostRapeRestraint(Pawn rapist, Pawn victim)
        {
            if (rapist == null || victim == null || !victim.Spawned || victim.Dead) return;
            if (S.enableOnaholeCapture && SoftDeps.OnaholeActive && Rand.Chance(S.onaholeCaptureChance)
                && DoOnaholeCapture(rapist, victim)) return;
            if (S.enableBoundInPublic && Rand.Chance(S.boundInPublicChance)
                && DoBoundInPublic(rapist, victim)) return;
            TryLockDeviceAfterRape(rapist, victim);
        }

        private static IntVec3 ColonyCenter(Map map)
        {
            var cols = map.mapPawns.FreeColonistsSpawned;
            if (cols != null && cols.Count > 0)
            {
                int x = 0, z = 0;
                for (int i = 0; i < cols.Count; i++) { x += cols[i].Position.x; z += cols[i].Position.z; }
                return new IntVec3(x / cols.Count, 0, z / cols.Count);
            }
            return map.Center;
        }

        // ── Deep attribute updates on sex acts (feedback: values must change on events) ──
        /// <summary>Sex-quality multiplier from how worn the partner's most-used hole is: a loose, well-used
        /// pawn gives less satisfying sex. Females lose quality faster (vaginal wear weighted). 1.0 fresh down to
        /// ~0.45 at 100% wear. Returns 1.0 (no-op) when the feature is off or attributes aren't seeded.</summary>
        public static float WornSexQualityFactor(Pawn partner)
        {
            if (partner == null || S == null || !S.wearReducesSexQuality) return 1f;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(partner);
            var sx = prof?.sex;
            if (sx == null || !sx.seeded) return 1f;
            float wear = UnityEngine.Mathf.Max(sx.wearOral, sx.wearAnal);
            if (SexAttributes.HasVagina(partner)) wear = UnityEngine.Mathf.Max(wear, sx.wearVaginal);
            float strength = (partner.gender == Gender.Female) ? 0.55f : 0.35f; // females (vaginal) lose quality faster
            return UnityEngine.Mathf.Clamp(1f - (wear / 100f) * strength, 0.35f, 1f);
        }

        /// <summary>Post-sex: wears the parts actually used, nudges sex addiction for both, and adds trauma /
        /// erodes self-worth for a rape victim. Called from the Aftersex postfix.</summary>
        public static void UpdateAttributesAfterSex(rjw.SexProps props)
        {
            if (props == null) return;
            Pawn a = props.initiator, b = props.recipient;
            if (a == null || b == null) return;
            rjw.xxx.rjwSextype st = rjw.xxx.rjwSextype.None; try { st = props.sexType; } catch { }
            bool rape = false; try { rape = props.isRape; } catch { }
            var gc = GameComponent_Harassment.Instance;
            var sa = gc?.GetProfile(a)?.SexAttr(a);
            var sb = gc?.GetProfile(b)?.SexAttr(b);
            ApplyActWear(a, sa, b, sb, st, 3.5f);
            if (sa != null) sa.sexAddiction = Clamp100(sa.sexAddiction + 1.2f);
            if (sb != null) sb.sexAddiction = Clamp100(sb.sexAddiction + 1.2f);
            if (rape && sb != null)
            {
                sb.trauma = Clamp100(sb.trauma + 5f);
                sb.selfEsteem = Clamp100(sb.selfEsteem - 3f);
                sb.willpower = Clamp100(sb.willpower - 2f);
                AttrDelta(b, spirit: -2f, subdom: -3f); // being taken by force also grinds down their spirit
                ShiftSubDom(a, 2f);  // the aggressor grows more dominant
            }
            // Possessive jealousy: an owner whose pet was used by someone else (that they didn't arrange).
            try { DepthOnPetUsed(a, b); DepthOnPetUsed(b, a); } catch { }
        }

        private static float Clamp100(float v) => UnityEngine.Mathf.Clamp(v, 0f, 100f);

        private static void ApplyActWear(Pawn a, SexAttributes sa, Pawn b, SexAttributes sb, rjw.xxx.rjwSextype st, float amt)
        {
            void Wear(SexAttributes s, Pawn p, int part)
            {
                if (s == null || p == null) return;
                switch (part)
                {
                    case 0: if (SexAttributes.HasMouth(p)) s.wearOral = Clamp100(s.wearOral + amt); break;
                    case 1: if (SexAttributes.HasVagina(p)) s.wearVaginal = Clamp100(s.wearVaginal + amt); break;
                    case 2: if (SexAttributes.HasAnus(p)) s.wearAnal = Clamp100(s.wearAnal + amt); break;
                    case 3: if (SexAttributes.HasPenis(p)) s.wearPenis = Clamp100(s.wearPenis + amt); break;
                }
            }
            switch (st)
            {
                case rjw.xxx.rjwSextype.Vaginal: Wear(sb, b, 1); Wear(sa, a, 3); break;
                case rjw.xxx.rjwSextype.Anal: Wear(sb, b, 2); Wear(sa, a, 3); break;
                case rjw.xxx.rjwSextype.Oral:
                case rjw.xxx.rjwSextype.Fellatio: Wear(sb, b, 0); Wear(sa, a, 3); break;
                case rjw.xxx.rjwSextype.Cunnilingus: Wear(sa, a, 0); Wear(sb, b, 1); break;
                case rjw.xxx.rjwSextype.Handjob:
                case rjw.xxx.rjwSextype.Boobjob:
                case rjw.xxx.rjwSextype.Footjob: Wear(sb, b, 3); break;
                case rjw.xxx.rjwSextype.DoublePenetration: Wear(sb, b, 1); Wear(sb, b, 2); Wear(sa, a, 3); break;
                case rjw.xxx.rjwSextype.Masturbation: Wear(sa, a, SexAttributes.HasPenis(a) ? 3 : 1); break;
                default: Wear(sb, b, 0); break;
            }
        }

        // ── Blackmail + scandalous photos ────────────────────────────────────
        /// <summary>Post-sex hook: an evil witness may photograph the act, spawning a blackmail item.</summary>
        public static void TryCapturePhoto(rjw.SexProps props)
        {
            if (!S.enableBlackmail || props == null) return;
            Pawn a = props.pawn, b = props.partner;
            if (a == null || !a.Spawned) return;

            // In a rape the photo must depict the VICTIM (the humiliated party), never the aggressor.
            // props.pawn is the actor; props.isRapist tells us whether the actor is the rapist.
            bool rape = false, actorIsRapist = true;
            try { rape = props.isRape; } catch { }
            try { actorIsRapist = props.isRapist; } catch { }
            Pawn subject;
            if (rape)
            {
                Pawn aggressor = actorIsRapist ? a : b;
                Pawn victim = aggressor == a ? b : a;
                subject = (victim != null && victim.Spawned && victim.RaceProps.Humanlike) ? victim : null;
            }
            else subject = FindPhotoSubject(a, b);
            if (subject == null || !subject.Spawned || subject.Map == null) return;
            if (HasPhotoOf(subject)) return; // one photo per subject is enough

            Pawn witness = FindPhotoWitness(subject, a, b);
            if (witness == null || !Rand.Chance(S.photoCaptureChance)) return;

            var photo = ThingMaker.MakeThing(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
            var comp = photo.TryGetComp<CompScandalousPhoto>();
            if (comp != null)
            {
                comp.subject = subject;
                rjw.xxx.rjwSextype st = rjw.xxx.rjwSextype.None;
                try { st = props.sexType; } catch { }
                Pawn other = subject == a ? b : a;
                comp.loreDesc = BuildPhotoLore(subject, other, st, props.isRape, RoomLabel(subject), subject == props.initiator);
            }
            GenPlace.TryPlaceThing(photo, witness.Position, subject.Map, ThingPlaceMode.Near);
        }

        /// <summary>Each nearby onlooker who can see the act has a chance to secretly photograph the female
        /// participant, keeping the copy in their own inventory (feeds circulation + repeat blackmail).</summary>
        public static void TryWitnessPhotos(rjw.SexProps props)
        {
            if (!S.enableWitnessPhotos || props == null) return;
            Pawn a = props.initiator, b = props.recipient;
            Pawn female = (a != null && a.Spawned && a.gender == Gender.Female) ? a
                        : (b != null && b.Spawned && b.gender == Gender.Female) ? b : null;
            if (female == null || female.Map == null) return;
            Pawn other = female == a ? b : a;
            rjw.xxx.rjwSextype st = rjw.xxx.rjwSextype.None; try { st = props.sexType; } catch { }
            bool rape = false; try { rape = props.isRape; } catch { }
            string where = RoomLabel(female);
            Map map = female.Map;

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var w = pawns[i];
                if (w == a || w == b) continue;
                if (!w.RaceProps.Humanlike || w.Dead || w.Downed || !w.Awake() || w.inventory == null) continue;
                if (female.Position.DistanceTo(w.Position) > 10f) continue;
                if (!GenSight.LineOfSight(female.Position, w.Position, map)) continue;
                if (!Rand.Chance(S.witnessPhotoChance)) continue;

                var photo = ThingMaker.MakeThing(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
                var comp = photo.TryGetComp<CompScandalousPhoto>();
                if (comp != null)
                {
                    comp.subject = female;
                    comp.distributed = false;
                    comp.loreDesc = BuildPhotoLore(female, other, st, rape, where, female == props.initiator);
                }
                if (!w.inventory.innerContainer.TryAdd(photo, true)) { try { if (!photo.Destroyed) photo.Destroy(); } catch { } continue; }
                if (IsPlayerOwned(female) || IsPlayerOwned(w))
                    Messages.Message(w.LabelShort + " secretly took a photo of " + female.LabelShort + " during the act.",
                        new LookTargets(w, female), MessageTypeDefOf.NegativeEvent, false);
            }
        }

        private static Pawn FindPhotoSubject(Pawn a, Pawn b)
        {
            if (a != null && IsPlayerOwned(a) && a.RaceProps.Humanlike) return a;
            if (b != null && IsPlayerOwned(b) && b.RaceProps.Humanlike) return b;
            if (a != null && a.RaceProps.Humanlike) return a;
            return (b != null && b.RaceProps.Humanlike) ? b : null;
        }

        private static Pawn FindPhotoWitness(Pawn subject, Pawn a, Pawn b)
        {
            Map map = subject.Map;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == subject || p == a || p == b) continue;
                if (!p.RaceProps.Humanlike || p.Dead || p.Downed || !p.Awake()) continue;
                if (subject.Position.DistanceTo(p.Position) > 12f) continue;
                if (p.HostileTo(subject)) continue;
                var prof = GameComponent_Harassment.Instance?.GetProfile(p);
                if (prof == null || prof.morality == Morality.Decent) continue;
                if (prof.morality == Morality.Questionable && !Rand.Chance(0.4f)) continue;
                if (!GenSight.LineOfSight(subject.Position, p.Position, map)) continue;
                return p;
            }
            return null;
        }

        public static bool HasPhotoOf(Pawn target) => FindPhotoThingOf(target) != null;

        /// <summary>Counts scandalous photos depicting this pawn across every map (ground + inventories), and how
        /// many of them are marked as distributed (in circulation).</summary>
        public static void CountPhotosOf(Pawn target, out int total, out int circulating)
        {
            total = 0; circulating = 0;
            if (target == null) return;
            var gc = GameComponent_Harassment.Instance;
            if (gc?.circulatingPhotos != null)
                for (int i = 0; i < gc.circulatingPhotos.Count; i++)
                    if (gc.circulatingPhotos[i]?.subject == target) { total++; circulating++; }
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var map = maps[m];
                var ground = map.listerThings.ThingsOfDef(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
                for (int i = 0; i < ground.Count; i++) TallyPhoto(ground[i], target, ref total, ref circulating);
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var inv = pawns[i].inventory?.innerContainer;
                    if (inv == null) continue;
                    for (int j = 0; j < inv.Count; j++)
                        if (inv[j].def == RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) TallyPhoto(inv[j], target, ref total, ref circulating);
                }
            }
        }

        private static void TallyPhoto(Thing t, Pawn target, ref int total, ref int circulating)
        {
            var c = t.TryGetComp<CompScandalousPhoto>();
            if (c == null || c.subject != target) return;
            total++;
            if (c.distributed) circulating++;
        }

        /// <summary>One known photo of a pawn plus who currently controls it (for the gallery popout).</summary>
        public struct PhotoRecord { public Thing photo; public CompScandalousPhoto comp; public string holder; public Pawn holderPawn; public string loreOverride; }

        /// <summary>Gathers every known scandalous photo of a pawn across all maps, with who holds it.</summary>
        public static System.Collections.Generic.List<PhotoRecord> GatherPhotosOf(Pawn target)
        {
            var list = new System.Collections.Generic.List<PhotoRecord>();
            if (target == null) return list;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var map = maps[m];
                var ground = map.listerThings.ThingsOfDef(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
                for (int i = 0; i < ground.Count; i++)
                {
                    var c = ground[i].TryGetComp<CompScandalousPhoto>();
                    if (c != null && c.subject == target)
                        list.Add(new PhotoRecord { photo = ground[i], comp = c, holder = "On the ground", holderPawn = null });
                }
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var inv = pawns[i].inventory?.innerContainer;
                    if (inv == null) continue;
                    for (int j = 0; j < inv.Count; j++)
                    {
                        if (inv[j].def != RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) continue;
                        var c = inv[j].TryGetComp<CompScandalousPhoto>();
                        if (c != null && c.subject == target)
                            list.Add(new PhotoRecord { photo = inv[j], comp = c, holder = "Held by " + pawns[i].LabelShortCap, holderPawn = pawns[i] });
                    }
                }
            }
            // World-circulating photos (sold/handed off the map) - still listed.
            var gc = GameComponent_Harassment.Instance;
            if (gc?.circulatingPhotos != null)
                for (int i = 0; i < gc.circulatingPhotos.Count; i++)
                {
                    var cp = gc.circulatingPhotos[i];
                    if (cp != null && cp.subject == target)
                        list.Add(new PhotoRecord { photo = null, comp = null, holder = cp.holder ?? "In circulation", holderPawn = null, loreOverride = cp.lore });
                }
            return list;
        }

        /// <summary>A qualitative label + notoriety value for how likely the colony's depravity is to draw
        /// curious visitors. Ties to the notoriety meter and any pending curious-visitor arrival.</summary>
        public static string VisitorLikelihoodLabel()
        {
            var gc = GameComponent_Harassment.Instance;
            int n = gc?.notoriety ?? 0;
            string band = n <= 0 ? "none" : n < 10 ? "faint" : n < 30 ? "growing" : n < 60 ? "notable" : "infamous";
            string incoming = (gc != null && gc.CuriousVisitorsPending) ? ", visitors incoming" : "";
            return band + " (notoriety " + n + incoming + ")";
        }

        /// <summary>World reputation for a pawn - soft-tied to Karma &amp; Reputation when installed, otherwise
        /// derived from colony notoriety. Negative karma reads as a growing depraved reputation.</summary>
        public static string WorldReputationLabel(Pawn p)
        {
            if (p != null && KarmaBridge.TryGetKarma(p, out float k))
            {
                string band = k <= -60f ? "infamous" : k <= -20f ? "disreputable" : k < 20f ? "unremarkable"
                    : k < 60f ? "well regarded" : "renowned";
                return band + " (karma " + UnityEngine.Mathf.RoundToInt(k) + ")";
            }
            int n = GameComponent_Harassment.Instance?.notoriety ?? 0;
            if (n <= 0) return "unremarkable";
            return (n < 30 ? "whispered about" : n < 60 ? "disreputable" : "infamous") + " (by rumor)";
        }

        public static Thing FindPhotoThingOf(Pawn target)
        {
            Map map = target?.Map;
            if (map == null) return null;
            // On the ground.
            var list = map.listerThings.ThingsOfDef(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i].TryGetComp<CompScandalousPhoto>();
                if (c != null && c.subject == target) return list[i];
            }
            // Carried in a pawn's inventory (circulating copies).
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var inv = pawns[i].inventory?.innerContainer;
                if (inv == null) continue;
                for (int j = 0; j < inv.Count; j++)
                {
                    if (inv[j].def != RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) continue;
                    var c = inv[j].TryGetComp<CompScandalousPhoto>();
                    if (c != null && c.subject == target) return inv[j];
                }
            }
            return null;
        }

        /// <summary>True if the harasser is personally carrying a scandalous photo of the target.</summary>
        public static bool HarasserCarriesPhotoOf(Pawn harasser, Pawn target)
        {
            var inv = harasser?.inventory?.innerContainer;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                if (inv[i].def != RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) continue;
                var c = inv[i].TryGetComp<CompScandalousPhoto>();
                if (c != null && c.subject == target) return true;
            }
            return false;
        }

        /// <summary>On first spawn, a chance a pawn already carries a circulating photo of a colony pawn.</summary>
        public static void TrySpawnCirculationPhoto(Pawn pawn)
        {
            if (!S.enableBlackmail || pawn?.inventory == null) return;
            if (!pawn.RaceProps.Humanlike || pawn.ageTracker == null || !pawn.ageTracker.Adult) return;
            var prof = GameComponent_Harassment.Instance?.GetProfile(pawn);
            if (prof == null || prof.checkedCirculation) return;
            prof.checkedCirculation = true;
            if (!Rand.Chance(S.circulationPhotoChance)) return;

            var subject = PickCirculationSubject(pawn);
            if (subject == null || HasPhotoOf(subject)) return; // skip if a copy already exists

            var photo = ThingMaker.MakeThing(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
            var comp = photo.TryGetComp<CompScandalousPhoto>();
            if (comp != null)
            {
                comp.subject = subject;
                comp.distributed = false;
                var st = (rjw.xxx.rjwSextype)Rand.RangeInclusive(1, 3); // vaginal / anal / oral
                comp.loreDesc = BuildPhotoLore(subject, null, st, Rand.Chance(0.4f), null);
            }
            if (!pawn.inventory.innerContainer.TryAdd(photo, true))
                try { if (!photo.Destroyed) photo.Destroy(); } catch { }
        }

        private static Pawn PickCirculationSubject(Pawn carrier)
        {
            var pool = new List<Pawn>();
            foreach (var map in Find.Maps)
            {
                var cps = map.mapPawns.FreeColonistsAndPrisonersSpawned;
                for (int i = 0; i < cps.Count; i++)
                {
                    var c = cps[i];
                    if (c != carrier && c.RaceProps.Humanlike && c.ageTracker != null && c.ageTracker.Adult) pool.Add(c);
                }
                var slaves = map.mapPawns.SlavesOfColonySpawned;
                for (int i = 0; i < slaves.Count; i++)
                    if (slaves[i] != carrier && !pool.Contains(slaves[i])) pool.Add(slaves[i]);
            }
            if (pool.Count == 0) return null;
            var victims = pool.Where(p =>
            {
                var pr = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
                return pr != null && pr.lastVictimTick > -999999;
            }).ToList();
            var src = (victims.Count > 0 && Rand.Chance(0.7f)) ? victims : pool;
            return src.RandomElement();
        }

        private static string RoomLabel(Pawn p)
        {
            try
            {
                var room = p.GetRoom();
                if (room == null) return null;
                if (room.PsychologicallyOutdoors) return "open"; // -> "in the open"
                var role = room.Role;
                if (role == null || role == RoomRoleDefOf.None) return null; // roleless indoor -> no location clause
                var lbl = role.label;
                return (lbl.NullOrEmpty() || lbl == "none") ? null : lbl;
            }
            catch { return null; }
        }

        // Three tonal registers for a captured act: forced (rape), passionate (willing + aroused), tender
        // (willing, gentle). Drives every fragment pool so consensual photos read warm, not humiliating.
        private enum PhotoTone { Forced, Dominant, Passionate, Tender }

        // Procedural explicit photo lore. Each photo bakes its own randomized 5-6 sentence description at
        // capture, so no two read alike. Fictional, adults-only (harassment is age-gated to 18-99) flavor text.
        public static string BuildPhotoLore(Pawn subject, Pawn other, rjw.xxx.rjwSextype st, bool rape, string where, bool subjectActive = false)
        {
            try
            {
                bool solo = st == rjw.xxx.rjwSextype.Masturbation;
                var pr = Pronouns(subject);
                bool male = false; try { male = subject.gender == Gender.Male; } catch { }
                bool aroused = SubjectIsAroused(subject);
                // Tone respects BOTH consent and the subject's role: a rape reads as distress for the victim but
                // cold dominance for the aggressor; a willing act reads passionate or tender.
                PhotoTone tone = rape ? (subjectActive ? PhotoTone.Dominant : PhotoTone.Forced)
                                      : (aroused ? PhotoTone.Passionate : PhotoTone.Tender);
                string partner = solo ? null : ((other != null && other != subject) ? other.LabelShort : "a stranger");
                string loc = where.NullOrEmpty() ? "" : (where == "open" ? " out in the open" : " in the " + where);
                string state = SubjectStatePhrase(subject);
                string statePart = state.NullOrEmpty() ? "" : ", " + state + ",";

                string open = OpeningClause(tone);
                string act = ExplicitActClause(st, pr, partner, male, subjectActive);
                string manner = MannerClause(pr, partner, tone, solo);
                string detail = PhysicalDetail(st, pr, tone);
                string demeanor = DemeanorClause(pr, tone);
                string when = WhenClause(subject.Map);
                string closer =
                    tone == PhotoTone.Forced ? "If it got out, " + subject.LabelShort + " would be humiliated."
                    : tone == PhotoTone.Dominant ? "If it got out, " + subject.LabelShort + " would be exposed for the predator they are."
                    : OneOf("If it got out, " + subject.LabelShort + " would be mortified to be seen like this.",
                            "If it got out, " + subject.LabelShort + " would never live it down.");

                var sb = new System.Text.StringBuilder();
                sb.Append(open).Append(" ").Append(subject.LabelShort).Append(statePart)
                  .Append(" ").Append(act).Append(loc).Append(".");
                if (!manner.NullOrEmpty()) sb.Append(" ").Append(manner);
                if (!detail.NullOrEmpty()) sb.Append(" ").Append(detail);
                if (!demeanor.NullOrEmpty()) sb.Append(" ").Append(demeanor);
                if (!when.NullOrEmpty()) sb.Append(" ").Append(when);
                sb.Append(" ").Append(closer);
                return sb.ToString();
            }
            catch { return "A compromising photo of " + (subject != null ? subject.LabelShort : "someone") + "."; }
        }

        private static string OpeningClause(PhotoTone tone)
        {
            switch (tone)
            {
                case PhotoTone.Forced: return OneOf("A lewd close-up of", "A humiliating shot of",
                    "An explicit photo of", "A grainy, damning photo of");
                case PhotoTone.Dominant: return OneOf("A damning photo of", "An explicit photo of",
                    "A lewd close-up of", "A chilling, candid shot of");
                case PhotoTone.Passionate: return OneOf("A steamy photo of", "An explicit photo of",
                    "A candid, heated shot of", "A sweat-slicked close-up of");
                default: return OneOf("An intimate photo of", "A tender, candid photo of",
                    "A private, quiet photo of", "A soft-lit photo of");
            }
        }

        /// <summary>The "how" sentence: position, pace and roughness, coloured by tone.</summary>
        private static string MannerClause(PronounSet pr, string partner, PhotoTone tone, bool solo)
        {
            string by = partner ?? "someone";
            if (solo)
            {
                switch (tone)
                {
                    case PhotoTone.Forced: return "Even alone, " + pr.poss + " movements look frantic and desperate.";
                    case PhotoTone.Passionate: return pr.PossCap + " fingers work faster and faster, hips lifting off the surface.";
                    default: return pr.PossCap + " touch is slow, indulgent, in no hurry at all.";
                }
            }
            switch (tone)
            {
                case PhotoTone.Forced: return OneOf(
                    pr.PossCap + " wrists are pinned and " + pr.poss + " legs forced apart.",
                    "The fist knotted in " + pr.poss + " hair leaves no room to pull away.",
                    pr.PossCap + " body is bent roughly over the nearest surface, held down hard.",
                    "There is nowhere for " + pr.obj + " to go as " + by + " keeps going.");
                case PhotoTone.Dominant: return OneOf(
                    pr.PossCap + " grip holds " + by + " pinned and helpless.",
                    by + " has nowhere to go as " + pr.subj + " takes what " + pr.subj + " wants.",
                    "A fist knotted in " + by + "'s hair holds them exactly where " + pr.subj + " wants.");
                case PhotoTone.Passionate: return OneOf(
                    pr.PossCap + " legs are wrapped eagerly around " + by + ".",
                    pr.PossCap + " fingers claw at " + by + "'s back, pulling " + by + " deeper.",
                    pr.PossCap + " hips grind back to meet every thrust.",
                    pr.PossCap + " body is bent willingly over the nearest surface, begging for more.");
                default: return OneOf(
                    pr.PossCap + " fingers are laced tightly with " + by + "'s.",
                    pr.PossCap + " body moves in a slow, unhurried rhythm with " + by + ".",
                    "Every touch between " + pr.obj + " and " + by + " looks unhurried and wanted.");
            }
        }

        /// <summary>The "when" sentence: time of day read off the map clock, with a light/ambient tell.</summary>
        private static string WhenClause(Map map)
        {
            if (map == null) return "";
            int h;
            try { h = GenLocalDate.HourInteger(map); } catch { return ""; }
            string t;
            if (h < 5) t = "in the dead of night";
            else if (h < 8) t = "in the grey light before dawn";
            else if (h < 12) t = "in the full light of morning";
            else if (h < 17) t = "in broad daylight";
            else if (h < 20) t = "in the fading evening light";
            else t = "late into the night";
            return OneOf("By the light, it was taken " + t + ".", "The shot was caught " + t + ".");
        }

        private static string DemeanorClause(PronounSet pr, PhotoTone tone)
        {
            switch (tone)
            {
                case PhotoTone.Forced: return OneOf(
                    "Tears stream down " + pr.poss + " face, " + pr.poss + " mouth open in a silent cry.",
                    pr.PossCap + " eyes are screwed shut, " + pr.poss + " expression pure misery.",
                    "Every muscle in " + pr.poss + " body is rigid with distress.",
                    pr.PossCap + " face is a wrecked mess of tears, clearly used against " + pr.poss + " will.");
                case PhotoTone.Dominant: return OneOf(
                    "A cruel, satisfied smile plays across " + pr.poss + " lips.",
                    pr.PossCap + " eyes gleam with cold control.",
                    pr.PossCap + " face is a mask of pure dominance.");
                case PhotoTone.Passionate: return OneOf(
                    pr.PossCap + " face is slack with pleasure, eyes rolled back.",
                    "A dazed, blissful expression is frozen on " + pr.poss + " face.",
                    pr.PossCap + " body arches into it, utterly lost in the moment.",
                    pr.PossCap + " mouth is open in an unmistakable moan.");
                default: return OneOf(
                    "A quiet, contented smile rests on " + pr.poss + " face.",
                    pr.PossCap + " eyes are half-closed, warm and unguarded.",
                    "There is nothing but soft trust in " + pr.poss + " expression.");
            }
        }

        /// <summary>Firm-detail tell for photo lore: whether the subject was naked and/or bound when caught.</summary>
        private static string SubjectStatePhrase(Pawn s)
        {
            try
            {
                bool bound = false;
                int realClothes = 0;
                var worn = s?.apparel?.WornApparel;
                if (worn != null)
                    for (int i = 0; i < worn.Count; i++)
                    {
                        if (worn[i].def is rjw.bondage_gear_def) { bound = true; continue; }
                        realClothes++;
                    }
                bool bare = realClothes == 0;
                if (bound && bare) return "stripped bare and bound";
                if (bound) return "bound";
                if (bare) return "stripped naked";
                return null;
            }
            catch { return null; }
        }

        /// <summary>True if the subject looks visibly aroused at capture (adds a firmer tell to the lore).</summary>
        private static bool SubjectIsAroused(Pawn s)
        {
            try { return rjw.xxx.is_horny(s); } catch { return false; }
        }

        // ── Photo lore text helpers ───────────────────────────────────────────
        private struct PronounSet
        {
            public string subj, obj, poss;
            public string SubjCap => (subj ?? "they").CapitalizeFirst();
            public string PossCap => (poss ?? "their").CapitalizeFirst();
        }

        private static PronounSet Pronouns(Pawn p)
        {
            try
            {
                if (p.gender == Gender.Female) return new PronounSet { subj = "she", obj = "her", poss = "her" };
                if (p.gender == Gender.Male) return new PronounSet { subj = "he", obj = "him", poss = "his" };
            }
            catch { }
            return new PronounSet { subj = "they", obj = "them", poss = "their" };
        }

        private static string OneOf(params string[] xs) => (xs == null || xs.Length == 0) ? "" : xs[Rand.Range(0, xs.Length)];

        /// <summary>The explicit act clause from the photo subject's (receiver's) point of view, with gendered
        /// pronouns and a named partner. Never starts a sentence, so no subject-verb agreement issues.</summary>
        private static string ExplicitActClause(rjw.xxx.rjwSextype t, PronounSet pr, string partner, bool male, bool active = false)
        {
            string by = partner ?? "a stranger";
            // Active = the subject is the one DOING it (initiator/top). Frame the act from that side so a female
            // aggressor or a willing top never reads as a passive victim.
            if (active)
            {
                switch (t)
                {
                    case rjw.xxx.rjwSextype.Vaginal: return OneOf("pounding " + by, "railing " + by + " hard", "fucking " + by + " senseless");
                    case rjw.xxx.rjwSextype.Anal: return OneOf("reaming " + by + "'s ass", "fucking " + by + " in the ass");
                    case rjw.xxx.rjwSextype.Oral:
                    case rjw.xxx.rjwSextype.Fellatio: return OneOf("fucking " + by + "'s throat", "using " + by + "'s mouth");
                    case rjw.xxx.rjwSextype.Cunnilingus: return "eating " + by + " out";
                    case rjw.xxx.rjwSextype.Masturbation: return male
                        ? OneOf("working " + pr.poss + " cock", "stroking " + pr.poss + " cock furiously")
                        : OneOf("fingering " + pr.poss + " dripping pussy", "furiously rubbing " + pr.poss + " clit");
                    case rjw.xxx.rjwSextype.MutualMasturbation: return "getting each other off with " + by;
                    case rjw.xxx.rjwSextype.Boobjob: return "thrusting between " + by + "'s tits";
                    case rjw.xxx.rjwSextype.Handjob: return "stroking " + by + " off";
                    case rjw.xxx.rjwSextype.Footjob: return "working " + by + "'s cock with " + pr.poss + " feet";
                    case rjw.xxx.rjwSextype.Fingering: return "fingering " + by;
                    case rjw.xxx.rjwSextype.Scissoring: return "grinding " + pr.poss + " pussy against " + by + "'s";
                    case rjw.xxx.rjwSextype.Fisting: return "fisting " + by;
                    case rjw.xxx.rjwSextype.Rimming: return "with " + pr.poss + " tongue buried in " + by + "'s ass";
                    case rjw.xxx.rjwSextype.DoublePenetration: return "double-teaming " + by + " with another";
                    case rjw.xxx.rjwSextype.Sixtynine: return "tangled in a sixty-nine with " + by;
                    default: return "using " + by;
                }
            }
            switch (t)
            {
                case rjw.xxx.rjwSextype.Vaginal: return OneOf(
                    "getting " + pr.poss + " pussy pounded by " + by,
                    "spread open and bred by " + by,
                    "getting fucked senseless by " + by);
                case rjw.xxx.rjwSextype.Anal: return OneOf(
                    "getting " + pr.poss + " ass reamed by " + by,
                    "taking " + by + "'s cock deep in " + pr.poss + " ass");
                case rjw.xxx.rjwSextype.Oral:
                case rjw.xxx.rjwSextype.Fellatio: return OneOf(
                    "getting " + pr.poss + " throat fucked by " + by,
                    "choking on " + by + "'s cock",
                    "with " + pr.poss + " mouth stuffed full of " + by + "'s cock");
                case rjw.xxx.rjwSextype.Cunnilingus: return "getting " + pr.poss + " pussy eaten out by " + by;
                case rjw.xxx.rjwSextype.Masturbation: return male
                    ? OneOf("working " + pr.poss + " cock", "stroking " + pr.poss + " cock furiously")
                    : OneOf("fingering " + pr.poss + " dripping pussy", "furiously rubbing " + pr.poss + " clit");
                case rjw.xxx.rjwSextype.MutualMasturbation: return "getting each other off with " + by;
                case rjw.xxx.rjwSextype.Boobjob: return "squeezing " + pr.poss + " tits around " + by + "'s cock";
                case rjw.xxx.rjwSextype.Handjob: return "stroking " + by + "'s cock";
                case rjw.xxx.rjwSextype.Footjob: return "working " + pr.poss + " feet along " + by + "'s cock";
                case rjw.xxx.rjwSextype.Fingering: return "getting fingered by " + by;
                case rjw.xxx.rjwSextype.Scissoring: return "grinding " + pr.poss + " pussy against " + by + "'s";
                case rjw.xxx.rjwSextype.Fisting: return "getting fisted by " + by;
                case rjw.xxx.rjwSextype.Rimming: return "with " + pr.poss + " tongue buried in " + by + "'s ass";
                case rjw.xxx.rjwSextype.DoublePenetration: return "getting double-stuffed and spitroasted between two partners including " + by;
                case rjw.xxx.rjwSextype.Sixtynine: return "tangled in a sixty-nine with " + by;
                default: return "getting used by " + by;
            }
        }

        /// <summary>A randomized physical-detail sentence keyed to the act group (oral / penetrative / other)
        /// and softened for the tender register.</summary>
        private static string PhysicalDetail(rjw.xxx.rjwSextype t, PronounSet pr, PhotoTone tone)
        {
            bool oral = t == rjw.xxx.rjwSextype.Oral || t == rjw.xxx.rjwSextype.Fellatio
                     || t == rjw.xxx.rjwSextype.Cunnilingus || t == rjw.xxx.rjwSextype.Sixtynine;
            bool pen = t == rjw.xxx.rjwSextype.Vaginal || t == rjw.xxx.rjwSextype.Anal
                    || t == rjw.xxx.rjwSextype.DoublePenetration;
            if (tone == PhotoTone.Dominant)
                return OneOf(pr.PossCap + " body glistens with the sweat of exertion.",
                    pr.PossCap + " skin is flecked with their victim's mess.", "");
            if (tone == PhotoTone.Tender)
            {
                if (oral) return OneOf(
                    pr.PossCap + " lips glisten, " + pr.poss + " movements slow and deliberate.",
                    "A soft flush colours " + pr.poss + " cheeks.");
                if (pen) return OneOf(
                    "A faint sheen of sweat covers " + pr.poss + " skin.",
                    pr.PossCap + " body rocks gently, warm and unhurried.");
                return OneOf(pr.PossCap + " skin is warm and lightly flushed.", "");
            }
            if (oral) return OneOf(
                pr.PossCap + " face is streaked with tears, drool, and cum.",
                "Thick ropes of semen drip from " + pr.poss + " chin and cheeks.",
                pr.PossCap + " chin is slick with spit and cum.");
            if (pen) return OneOf(
                "Semen leaks out of " + pr.obj + " and runs down " + pr.poss + " thighs.",
                pr.PossCap + " body is coated in sweat and cum.",
                "Fresh cum drips from " + pr.poss + " used hole.");
            return OneOf(
                pr.PossCap + " body glistens with sweat.",
                pr.PossCap + " flushed skin is marked with fresh handprints.",
                "");
        }

        private static bool DoBlackmail(Pawn harasser, Pawn target)
        {
            FireInteraction(harasser, target, RJWSH_InteractionDefOf.RJWSH_Blackmail);
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Blackmail);
            if (S.multiLineHarassment) ScheduleApproachExchange(harasser, target, ApproachType.Blackmail);

            if (S.interveneGateEnabled && InvolvesPlayerPawn(harasser, target))
            {
                Find.WindowStack.Add(new Dialog_Blackmail(harasser, target));
                return false; // dialog drives the outcome
            }
            KarmaBridge.AddKarma(harasser, -5f, "rjw_harassment_blackmail");
            return true; // NPC victim: coerced into compliance
        }

        /// <summary>Blackmail "comply": coerced strip + act in submit mode. Called from the dialog (off-job).</summary>
        public static void BlackmailComply(Pawn harasser, Pawn target)
        {
            KarmaBridge.AddKarma(harasser, -5f, "rjw_harassment_blackmail");
            if (harasser?.jobs == null || target == null) return;
            StartStripJob(harasser, target, submitted: true);
        }

        /// <summary>Blackmail "refuse": the photos get distributed -> humiliation + a colony alert.</summary>
        public static void BlackmailRefuse(Pawn harasser, Pawn target)
        {
            var comp = FindPhotoThingOf(target)?.TryGetComp<CompScandalousPhoto>();
            if (comp != null) comp.distributed = true;
            ApplyThought(target, harasser, RJWSH_ThoughtDefOf.RJWSH_Humiliated);
            Messages.Message(harasser.LabelShort + " distributed scandalous photos of " + target.LabelShort + ".",
                new LookTargets(target), MessageTypeDefOf.NegativeEvent, false);
            if (IsPlayerOwned(target)) TryIssueWarrant(harasser, "distributing scandalous photos of " + target.LabelShort);
        }

        /// <summary>Sell a scandalous photo to passing traders: silver now, humiliation for the subject, and -
        /// if the subject is a collared pet - curious visitors drawn to see them plus a bump to colony notoriety.</summary>
        public static void AuctionPhoto(Pawn subject, Thing photo)
        {
            if (photo == null) return;
            try
            {
                var map = photo.MapHeld ?? subject?.MapHeld;
                int amount = Rand.RangeInclusive(150, 400);
                try { if (subject != null) amount += (int)(subject.GetStatValue(StatDefOf.PawnBeauty) * 40f); } catch { }
                bool collared = subject != null && WearingControlCollar(subject);
                if (collared) amount = (int)(amount * 1.4f);
                if (amount < 50) amount = 50;
                AddEarnings(subject, amount);
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = amount;
                if (map != null) GenPlace.TryPlaceThing(silver, photo.PositionHeld, map, ThingPlaceMode.Near);

                if (subject != null)
                {
                    ApplyThought(subject, null, RJWSH_ThoughtDefOf.RJWSH_Humiliated);
                    GameComponent_Harassment.Instance?.AddNotoriety(2, "rjwsh_photo_auction");
                    ReputationBridge.AddReputation(subject, -8f, "rjwsh_photo_auction");
                    if (collared) GameComponent_Harassment.Instance?.ScheduleCuriousVisitors();
                }
                // Instead of vanishing, the photo goes into circulation: handed to a visitor on the map if one
                // is present, otherwise sold off into the wider world. Either way it stays tracked in the gallery.
                var comp = photo.TryGetComp<CompScandalousPhoto>();
                if (comp != null) comp.distributed = true;
                AttrDelta(subject, esteem: -5f, trauma: 2f); // being sold off as smut is humiliating
                Pawn buyer = PickAuctionBuyer(map);
                Faction offWorld = buyer == null ? PickPhotoFaction() : null;
                string holder = buyer != null ? "Bought by " + buyer.LabelShortCap + " (leaving with them)"
                    : (offWorld != null ? "Sold to " + offWorld.Name : "Sold off into the wider world");
                GameComponent_Harassment.Instance?.AddCirculatingPhoto(subject, comp?.loreDesc, holder, offWorld);
                Messages.Message("Auctioned a scandalous photo" + (subject != null ? " of " + subject.LabelShort : "")
                    + " for " + amount + " silver. " + (buyer != null ? buyer.LabelShortCap + " carried it off." : "It is out in the world now.")
                    + (collared ? " Word spreads - expect curious visitors." : ""),
                    new LookTargets(subject ?? photo), MessageTypeDefOf.PositiveEvent, false);
                if (!photo.Destroyed) photo.Destroy(DestroyMode.Vanish);
            }
            catch (System.Exception ex) { Log.WarningOnce("[RJW Sexual Harassment] photo auction failed: " + ex.Message, 0x5A1360); }
        }

        /// <summary>A random non-hostile guest on the map to buy/carry off an auctioned photo, or null.</summary>
        private static Pawn PickAuctionBuyer(Map map)
        {
            if (map == null) return null;
            var pawns = map.mapPawns.AllPawnsSpawned;
            var pool = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (p.Faction == null || p.Faction.IsPlayer || p.HostileTo(Faction.OfPlayer)) continue;
                pool.Add(p);
            }
            return pool.Count > 0 ? pool.RandomElement() : null;
        }

        /// <summary>Fires a vanilla visitor group - guests drawn to the colony by an auctioned photo.</summary>
        public static void FireCuriousVisitors()
        {
            try
            {
                Map map = null;
                foreach (var m in Find.Maps) if (m.IsPlayerHome) { map = m; break; }
                if (map == null) return;
                var inc = DefDatabase<IncidentDef>.GetNamedSilentFail("VisitorGroup");
                if (inc == null) return;
                var parms = StorytellerUtility.DefaultParmsNow(inc.category, map);
                if (inc.Worker.CanFireNow(parms)) inc.Worker.TryExecute(parms);
            }
            catch { }
        }

        /// <summary>A random non-hostile faction hears of the colony's depravity and thinks less of it.</summary>
        public static void NotorietyConsequence()
        {
            try
            {
                Faction pick = null; int seen = 0;
                foreach (var f in Find.FactionManager.AllFactionsVisible)
                {
                    if (f == null || f.IsPlayer || f.defeated || f.def.hidden || f.HostileTo(Faction.OfPlayer)) continue;
                    seen++;
                    if (Rand.Chance(1f / seen)) pick = f;   // reservoir pick
                }
                if (pick == null) return;
                pick.TryAffectGoodwillWith(Faction.OfPlayer, -3, false, false, null, null);
                Find.LetterStack.ReceiveLetter("Colony's reputation",
                    "Word of what happens to people in this colony has reached " + pick.Name + ". The stories unsettle them, and they think a little less of you.",
                    LetterDefOf.NegativeEvent);
                // Rival collector: high notoriety occasionally draws someone who tries to make off with your best pet.
                var noto = GameComponent_Harassment.Instance;
                if (noto != null && noto.notoriety >= 30 && Rand.Chance(0.35f))
                {
                    Map m = null;
                    foreach (var map in Find.Maps) if (map.IsPlayerHome) { m = map; break; }
                    if (m != null) DepthCollectorAttempt(m);
                }
            }
            catch { }
        }

        /// <summary>Blackmail "intimidate": chance to scare the blackmailer off and destroy the photo.</summary>
        public static bool BlackmailIntimidate(Pawn harasser, Pawn target)
        {
            float social = target.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            float melee = target.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            float chance = 0.3f + (social + melee) / 50f;
            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            chance -= (hp.confidence - 50f) / 200f;
            if (Rand.Chance(Mathf01(chance)))
            {
                var photo = FindPhotoThingOf(target);
                if (photo != null && !photo.Destroyed) photo.Destroy(DestroyMode.Vanish);
                hp.confidence = System.Math.Max(0f, hp.confidence - 12f);
                Messages.Message(target.LabelShort + " faced down " + harasser.LabelShort + " and destroyed the photo.",
                    new LookTargets(target), MessageTypeDefOf.PositiveEvent, false);
                return true;
            }
            Messages.Message("The blackmailer was not intimidated.",
                new LookTargets(new[] { harasser, target }), MessageTypeDefOf.NegativeEvent, false);
            return false;
        }

        /// <summary>Escalation roll, run by the job driver after the verbal stage.</summary>
        public static bool DecideEscalation(Pawn harasser, Pawn target, bool firedVerbal)
        {
            if (!S.allowEscalation || !(S.enableGrope || S.enableForced)) return false;

            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            var tp = GameComponent_Harassment.Instance.GetProfile(target);
            bool vulnerable = target.Downed || !target.Awake();

            float escChance = S.baseEscalationChance;
            escChance *= hp.morality == Morality.Evil ? 1.6f : (hp.morality == Morality.Questionable ? 1.0f : 0.4f);
            escChance *= 1f + SafeVulnerability(target) * 0.8f;
            escChance *= 1f + (tp.impression + 50f) / 100f;
            if (xxx.is_rapist(harasser)) escChance *= 1.5f;
            if (vulnerable) escChance += 0.25f;
            if (IsInBondage(target)) escChance *= 1.6f;     // restrained victims rarely escape escalation
            int bdsm = BdsmGearCount(target);
            if (bdsm > 0) escChance *= 1f + bdsm * 0.25f;   // each locked BDSM piece raises the odds of being taken

            // If no verbal landed (awake target, bad position) only proceed for compromised targets.
            if (!firedVerbal && !vulnerable) return false;
            return Rand.Chance(Mathf01(escChance));
        }

        /// <summary>Physical-stage entry used by the deferred queue and debug actions.</summary>
        public static void BeginPhysicalOrForced(Pawn harasser, Pawn target)
        {
            bool interactive = S.interveneGateEnabled && InvolvesPlayerPawn(harasser, target);
            if (S.enableGrope)
            {
                BeginPhysical(harasser, target, interactive);
            }
            else if (S.enableForced)
            {
                float quickSubmit = 0.35f + VulnerabilityScore(target) * 0.25f;
                if (Rand.Chance(Mathf01(quickSubmit)))
                    StartForcedAct(harasser, target);
            }
        }

        /// <summary>Player-directed physical stage (from the hero-mode right-click menu). Clears any stale
        /// in-progress guard and runs NON-interactively - the player already chose to force it, so there is
        /// no intervene prompt; it goes straight to the grope/strip/act.</summary>
        public static void BeginDirectedPhysical(Pawn harasser, Pawn target)
        {
            if (target != null) EndPhysical(target); // override any stale _activePhysical entry from a prior attempt
            BeginPhysical(harasser, target, false);
        }

        /// <summary>Finds a private spot to carry the victim to: harasser's bed, then the victim's.</summary>
        public static bool TryFindPrivateCell(Pawn harasser, Pawn victim, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Map map = harasser.Map;
            if (map == null) return false;

            IntVec3 c = BedCell(harasser);
            if (!c.IsValid) c = BedCell(victim);
            if (c.IsValid && c != harasser.Position && harasser.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
            {
                cell = c;
                return true;
            }
            return false;
        }

        private static IntVec3 BedCell(Pawn p)
        {
            var bed = p?.ownership?.OwnedBed;
            return (bed != null && bed.Spawned) ? bed.Position : IntVec3.Invalid;
        }

        // ── Physical stage: grope, then a stripping struggle ──────────────────
        // Victims currently in a physical stage, so a second event can't start another on the same pawn.
        private static readonly HashSet<int> _activePhysical = new HashSet<int>();

        public static void BeginPhysical(Pawn harasser, Pawn target, bool interactive)
        {
            if (harasser == null || target == null || harasser.Dead || target.Dead) return;
            if (!harasser.Spawned || !target.Spawned) return;
            if (!_activePhysical.Add(target.thingIDNumber)) { DiagFA(harasser, target, "BeginPhysical SKIP: target already mid-physical"); return; }
            DiagFA(harasser, target, "BeginPhysical interactive=" + interactive);

            ApplyGrope(harasser, target);

            // Comfort-marked pawns, and downed/unconscious (helpless) victims, always submit with no
            // prompt and proceed to the forced act.
            bool autoSubmit = target.Downed || !target.Awake();
            try { autoSubmit |= target.IsDesignatedComfort(); } catch { }
            // Conditioned or actively entranced pawns are compliant.
            var tprof = GameComponent_Harassment.Instance.GetProfileIfExists(target);
            if ((tprof != null && tprof.IsConditioned) || HasHypnotizedHediff(target)) autoSubmit = true;
            if (autoSubmit)
            {
                StartStripJob(harasser, target, submitted: true);
                return;
            }

            if (interactive)
            {
                // One upfront choice; the strip then plays out in-world over time.
                Find.WindowStack.Add(new Dialog_StruggleStrip(harasser, target));
            }
            else
            {
                // NPC victim: decide submit vs resist from their state, then run the timed strip job.
                float submitChance = 0.35f + SafeVulnerability(target) * 0.4f;
                if (target.Downed || !target.Awake()) submitChance += 0.4f;
                if (xxx.is_masochist(target)) submitChance += 0.2f;
                if (xxx.is_brawler(target)) submitChance -= 0.2f;
                StartStripJob(harasser, target, Rand.Chance(Mathf01(submitChance)));
            }
        }

        /// <summary>Starts the harasser's timed strip job. submitted = faster, no damage, no break-free.</summary>
        public static void StartStripJob(Pawn harasser, Pawn victim, bool submitted)
        {
            if (harasser?.jobs == null || victim == null) { EndPhysical(victim); return; }
            var job = JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_StripVictim, victim);
            job.count = submitted ? 1 : 0;
            harasser.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>Clears the in-progress guard for a victim.</summary>
        public static void EndPhysical(Pawn victim)
        {
            if (victim != null) _activePhysical.Remove(victim.thingIDNumber);
        }

        /// <summary>Forcibly removes the outermost worn apparel item. Returns false if nothing was worn.</summary>
        public static bool StripOneLayer(Pawn victim)
        {
            if (victim?.apparel == null || victim.apparel.WornApparel.Count == 0) return false;
            var ap = victim.apparel.WornApparel[0];
            try { victim.apparel.TryDrop(ap, out _); } catch { return false; }
            return true;
        }

        /// <summary>A real unarmed melee strike the offender lands while forcing clothes off a resisting
        /// victim, so Melee Animation (and similar) animate the beating. Falls back to a blunt hit if the
        /// melee verb is unavailable.</summary>
        public static void DealStripDamage(Pawn harasser, Pawn victim)
        {
            if (harasser == null || victim == null || victim.Dead) return;
            try
            {
                if (harasser.meleeVerbs != null && harasser.meleeVerbs.TryMeleeAttack(victim))
                    return;
                var dinfo = new DamageInfo(DamageDefOf.Blunt, Rand.Range(1f, 4f), 0f, -1f, harasser);
                victim.TakeDamage(dinfo);
            }
            catch { }
        }

        /// <summary>Called by the strip job when the victim is fully stripped: hand off to the forced act.</summary>
        public static void CompleteStripToForced(Pawn harasser, Pawn victim)
        {
            EndPhysical(victim);
            if (harasser == null || victim == null) return;
            GameComponent_Harassment.Instance?.GetProfile(victim)?.RecordSubmitted();
            StripAll(victim); // catch any locked-skip leftovers so the act animates on a bare pawn
            if (DebugHarassmentVerbose)
                Log.Message("[RJWSH-DIAG] CompleteStripToForced " + harasser.LabelShort + " -> " + victim.LabelShort
                    + " enableForced=" + S.enableForced + " vDowned=" + victim.Downed + " vAwake=" + victim.Awake());
            if (S.enableForced)
                MapComponent_HarassmentScan.EnqueueForcedAct(harasser, victim);
        }

        /// <summary>Drops every worn apparel item in-world. Used so the act always begins on a stripped pawn.</summary>
        public static void StripAll(Pawn victim)
        {
            if (victim?.apparel == null) return;
            var worn = victim.apparel.WornApparel;
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                try { victim.apparel.TryDrop(worn[i], out _); } catch { }
            }
        }

        public static void ApplyGrope(Pawn harasser, Pawn target)
        {
            if (harasser == null || target == null) return;
            ApplyThought(target, harasser, RJWSH_ThoughtDefOf.RJWSH_WasGroped);
            MakeBubble(harasser, target, RJWSH_InteractionDefOf.RJWSH_Grope);
            // Force a physical gesture so the grope actually animates (Melee Animation hooks the melee verb).
            // ForceMelee is capped non-lethal (stops above 50% health) and no-ops on a downed/distant target.
            ForceMelee(harasser, target);
            FABridge.PlayFace(target, "RJWSH_FA_Flinch"); // [NL] Facial Animation: a sharp flinch (no-op without FA)
            if (!target.Downed && target.Awake())
                MapComponent_HarassmentScan.ScheduleLine(target, harasser, RJWSH_InteractionDefOf.RJWSH_GropeReply, 50);
            KarmaBridge.AddKarma(harasser, -8f, "rjw_harassment_grope");
            RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Grope);
            RememberHarasser(target, harasser);          // the victim remembers this hands-on violation
            FireBystanderReactions(harasser, target);    // onlookers in sight react
            if (InvolvesPlayerPawn(harasser, target))
                Messages.Message(harasser.LabelShort + " is forcing themselves on " + target.LabelShort + ".",
                    new LookTargets(new[] { harasser, target }), MessageTypeDefOf.NegativeEvent, false);
        }

        // ── Victim memory: pawns remember who violated them ──────────────────
        /// <summary>Records that `harasser` physically violated `victim`, feeding the on-sight recoil/vengeance.</summary>
        public static void RememberHarasser(Pawn victim, Pawn harasser)
        {
            if (victim == null || harasser == null || victim == harasser) return;
            var vp = GameComponent_Harassment.Instance?.GetProfile(victim);
            if (vp == null) return;
            if (vp.harasserMemory == null) vp.harasserMemory = new Dictionary<int, int>();
            vp.harasserMemory.TryGetValue(harasser.thingIDNumber, out int c);
            vp.harasserMemory[harasser.thingIDNumber] = System.Math.Min(c + 1, 99);
            if (vp.harasserMemory.Count > 24) PruneMemory(vp);
        }

        private static void PruneMemory(PawnProfile vp)
        {
            int lowK = 0, lowV = int.MaxValue; bool any = false;
            foreach (var kv in vp.harasserMemory) if (kv.Value < lowV) { lowV = kv.Value; lowK = kv.Key; any = true; }
            if (any) vp.harasserMemory.Remove(lowK);
        }

        /// <summary>Periodic: a pawn who spots a remembered tormentor in sight recoils in fear (or, if that
        /// tormentor is now downed/imprisoned and the pawn is free, gets a flash of vengeance). Cheap: only
        /// pawns that actually carry harasser memories do any work.</summary>
        public static void MemoryReactionScan(Map map)
        {
            if (map == null) return;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;
            int now = Find.TickManager.TicksGame;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var v = pawns[i];
                if (!v.RaceProps.Humanlike || v.Dead || !v.Awake() || v.Downed) continue;
                var vp = gc.GetProfileIfExists(v);
                if (vp == null || vp.harasserMemory == null || vp.harasserMemory.Count == 0) continue;
                if (now < vp.recoilCooldownTick || IsBusyInAct(v)) continue;

                Pawn tormentor = null; int bestCount = 1;
                foreach (var kv in vp.harasserMemory)
                {
                    if (kv.Value < 2) continue;
                    var h = FindPawnByIdAnyMap(kv.Key);
                    if (h == null || h == v || h.Dead || h.Map != map) continue;
                    if (v.Position.DistanceTo(h.Position) > 9f) continue;
                    if (!GenSight.LineOfSight(v.Position, h.Position, map)) continue;
                    if (kv.Value > bestCount) { bestCount = kv.Value; tormentor = h; }
                }
                if (tormentor == null) continue;
                vp.recoilCooldownTick = now + 7500;

                bool helpless = tormentor.Downed || tormentor.IsPrisonerOfColony;
                if (helpless && !WearingControlCollar(v) && Rand.Chance(0.5f))
                {
                    FireFlavorLine(v, tormentor, RJWSH_InteractionDefOf.RJWSH_Vengeance);
                }
                else
                {
                    FireFlavorLine(v, tormentor, RJWSH_InteractionDefOf.RJWSH_Recoil);
                    FABridge.PlayFace(v, "RJWSH_FA_Flinch");
                    TryAddMoodThought(v, "RJWSH_Recoiled");
                }
            }
        }

        // ── Bystander reactions: the colony notices ───────────────────────────
        /// <summary>When a harassment event fires in sight of others, up to two witnesses react as real
        /// interactions (bubbles), branched by how they feel about it: decent pawns object, cruel pawns leer,
        /// friends/lovers of the victim confront the harasser, and fellow collared pets look away in resignation.</summary>
        public static void FireBystanderReactions(Pawn harasser, Pawn victim)
        {
            var map = harasser?.Map;
            if (map == null || victim == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            int reacted = 0;
            for (int i = 0; i < pawns.Count && reacted < 2; i++)
            {
                var w = pawns[i];
                if (w == harasser || w == victim) continue;
                if (!w.RaceProps.Humanlike || w.Dead || !w.Awake() || w.Downed) continue;
                if (w.InMentalState || IsBusyInAct(w)) continue;
                if (harasser.Position.DistanceTo(w.Position) > 10f) continue;
                if (!GenSight.LineOfSight(harasser.Position, w.Position, map)) continue;

                bool caresForVictim = AreLovers(w, victim)
                    || (w.relations != null && !w.HostileTo(victim) && w.relations.OpinionOf(victim) >= 20);
                var wp = GameComponent_Harassment.Instance?.GetProfileIfExists(w);
                bool ownedPet = wp != null && (wp.ownerId >= 0 || WearingControlCollar(w));

                if (caresForVictim)
                {
                    FireFlavorLine(w, harasser, RJWSH_InteractionDefOf.RJWSH_WitnessProtective);
                }
                else if (ownedPet)
                {
                    FireFlavorLine(w, victim, RJWSH_InteractionDefOf.RJWSH_WitnessResigned);
                    if (wp != null) wp.hypnosisLevel = System.Math.Min(100f, wp.hypnosisLevel + 1f); // learned helplessness
                }
                else if (Evilness(w) > 0.5f)
                {
                    FireFlavorLine(w, victim, RJWSH_InteractionDefOf.RJWSH_WitnessLeer);
                }
                else if (IdeologyHooks.ApprovesOfCruelty(w))
                {
                    // Their faith celebrates this - they leer instead of objecting, and take no mood hit.
                    FireFlavorLine(w, victim, RJWSH_InteractionDefOf.RJWSH_WitnessLeer);
                }
                else
                {
                    FireFlavorLine(w, harasser, RJWSH_InteractionDefOf.RJWSH_WitnessDisgust);
                    TryAddMoodThought(w, "RJWSH_SawSomethingWrong");
                }
                reacted++;
            }
        }

        /// <summary>The victim's chance to successfully resist a grope being forced into an act. Vulnerable,
        /// conditioned, bound, or masochist victims resist poorly; brawlers and high-melee victims resist well.</summary>
        public static float GropeResistChance(Pawn harasser, Pawn victim)
        {
            if (victim == null) return 0f;
            if (victim.Downed || !victim.Awake()) return 0.05f;
            float resist = 0.55f;
            resist -= SafeVulnerability(victim) * 0.45f;
            var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(victim);
            if (vp != null && vp.IsConditioned) resist -= 0.3f;
            if (IsInBondage(victim)) resist -= 0.25f;
            try { if (xxx.is_brawler(victim)) resist += 0.2f; } catch { }
            try { if (xxx.is_masochist(victim)) resist -= 0.2f; } catch { }
            try { if (xxx.is_rapist(harasser)) resist -= 0.1f; } catch { }
            try { resist += (victim.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) * 0.012f; } catch { }
            return UnityEngine.Mathf.Clamp(resist, 0.05f, 0.95f);
        }

        /// <summary>The victim resists a grope: they shove the groper off and it goes no further.</summary>
        public static void GropeFoughtOff(Pawn harasser, Pawn victim)
        {
            if (harasser == null || victim == null) return;
            ForceMelee(victim, harasser); // the victim shoves the groper away
            if (InvolvesPlayerPawn(harasser, victim))
                Messages.Message(victim.LabelShort + " fought off " + harasser.LabelShort + "'s grope.",
                    new LookTargets(new Pawn[] { victim, harasser }), MessageTypeDefOf.NeutralEvent, false);
        }

        public static int WornCount(Pawn p) => p?.apparel?.WornApparel?.Count ?? 0;

        /// <summary>Dev toggle: forces every struggle roll to fail so the forced act always fires.</summary>
        public static bool DebugForceLoseStruggle = false;

        public static float StruggleChance(Pawn harasser, Pawn victim)
        {
            if (DebugForceLoseStruggle) return 0f;
            if (victim == null) return 0f;
            if (victim.Downed || !victim.Awake()) return 0.05f;
            float c = 0.5f;
            c += (MeleeLevel(victim) - MeleeLevel(harasser)) / 40f;
            float manip = 1f;
            try { manip = victim.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation); } catch { }
            c += (manip - 1f) * 0.2f;
            c -= SafeVulnerability(victim) * 0.4f;
            if (xxx.is_brawler(victim)) c += 0.1f;
            if (xxx.is_masochist(victim)) c -= 0.15f;
            if (HandsBound(victim)) c *= 0.3f;              // bound hands can barely struggle
            return c < 0.05f ? 0.05f : (c > 0.95f ? 0.95f : c);
        }

        private static int MeleeLevel(Pawn p) => p?.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;

        public static void OnRepelled(Pawn harasser, Pawn victim)
        {
            if (victim != null) _activePhysical.Remove(victim.thingIDNumber);
            if (harasser == null || victim == null) return;
            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            var tp = GameComponent_Harassment.Instance.GetProfile(victim);
            tp.RecordResisted();
            hp.confidence = System.Math.Max(0f, hp.confidence - 10f);
            if (InvolvesPlayerPawn(harasser, victim))
                Messages.Message(victim.LabelShort + " struggled free from " + harasser.LabelShort + ".",
                    new LookTargets(new[] { harasser, victim }), MessageTypeDefOf.NeutralEvent, false);
        }

        // Public wrapper for debug actions.
        public static void ForceForcedAct(Pawn harasser, Pawn target) => StartForcedAct(harasser, target);

        // TEMP diagnostics for the strip->forced-act handoff. Remove once the standing-victim path is verified.
        public static bool DebugHarassmentVerbose = false;
        private static void DiagFA(Pawn h, Pawn t, string msg)
        {
            if (DebugHarassmentVerbose)
                Log.Message("[RJWSH-DIAG] StartForcedAct " + (h?.LabelShort ?? "?") + " -> " + (t?.LabelShort ?? "?") + ": " + msg);
        }

        /// <summary>Posts a top-left notice naming the approach when a player pawn is involved, so the player
        /// can see what kind of harassment is unfolding (SpeakUp bubbles alone are easy to miss).</summary>
        private static void AnnounceApproach(Pawn harasser, Pawn target, ApproachType approach)
        {
            if (harasser == null || target == null || !InvolvesPlayerPawn(harasser, target)) return;
            string verb;
            switch (approach)
            {
                case ApproachType.Catcall: verb = " is catcalling "; break;
                case ApproachType.Proposition: verb = " is propositioning "; break;
                case ApproachType.Flirt: verb = " is making a pass at "; break;
                case ApproachType.SpikedDrink: verb = " is offering a spiked drink to "; break;
                case ApproachType.Hypnosis: verb = " is trying to entrance "; break;
                case ApproachType.Blackmail: verb = " is blackmailing "; break;
                case ApproachType.DeviousDevice: verb = " is forcing a device onto "; break;
                default: verb = " is harassing "; break;
            }
            Messages.Message(harasser.LabelShortCap + verb + target.LabelShort + ".",
                new LookTargets(new[] { harasser, target }), MessageTypeDefOf.NeutralEvent, false);
        }

        private static void StartForcedAct(Pawn harasser, Pawn target)
        {
            try
            {
                // Mirror RJW's player-directed rape checks (RMB_Rape.DoBasicChecks). Our struggle loss
                // is the gate, so we deliberately SKIP RJW's AI vulnerability threshold (can_get_raped),
                // which would otherwise bail for any healthy, non-downed target (min vuln is 1.2).
                if (!RJWSettings.rape_enabled) { DiagFA(harasser, target, "rape_enabled=false"); return; }
                // NOTE: do NOT gate on xxx.can_rape(harasser). That helper rejects healthy non-futa women (a
                // non-futa woman only passes while she herself is vulnerable, which never applies to an
                // aggressor), so it silently killed every female-harasser assault. RJW's own directed rape
                // (RMB_Rape) gates on the TARGET, not the aggressor, and SexUtility.SelectSextype builds a
                // valid act for any initiator/receiver pairing - so the minimal gate is: harasser can engage
                // in sex at all, and the target can be used.
                if (!xxx.can_do_loving(harasser)) { DiagFA(harasser, target, "harasser cannot do loving"); return; }
                if (!xxx.can_be_fucked(target)) { DiagFA(harasser, target, "can_be_fucked(target)=false"); return; }
                if (target.HostileTo(harasser) && !target.Downed) { DiagFA(harasser, target, "hostile+standing"); return; }
                // No reserve pre-check: stripping is our gate, downed victims are valid (RJW handles
                // downed receivers), and RJW's own rape job does its own reservation.

                var job = JobMaker.MakeJob(xxx.RapeRandom, target);
                harasser.jobs.StartJob(job, JobCondition.InterruptForced);
                DiagFA(harasser, target, "STARTED RapeRandom; h.curJob=" + (harasser.CurJobDef?.defName ?? "null")
                    + " v.curJob=" + (target.CurJobDef?.defName ?? "null") + " vDowned=" + target.Downed
                    + " dist=" + harasser.Position.DistanceTo(target.Position).ToString("F1"));
                KarmaBridge.AddKarma(harasser, -15f, "rjw_harassment_forced");
                RimTalkBridge.NotifyHarassment(harasser, target, ApproachType.Forced);
                RememberHarasser(target, harasser);
                if (IsPlayerOwned(target)) GameComponent_Harassment.Instance?.AddNotoriety(1, "rjwsh_public_depravity"); // depravity in the colony gets around
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] forced act handoff failed: " + ex.Message, 0x5A12D1);
            }
        }

        /// <summary>One-shot intervention attempt invoked from the struggle dialog. Returns true if stopped.</summary>
        public static bool TryIntervene(Pawn harasser, Pawn victim)
        {
            Pawn rescuer = FindIntervener(harasser, victim);
            float chance = S.baseInterveneChance;
            if (rescuer != null)
            {
                float social = rescuer.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                float melee = rescuer.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                chance += (social + melee) / 60f; // up to +~0.33
            }
            var hp = GameComponent_Harassment.Instance.GetProfile(harasser);
            chance -= (hp.confidence - 50f) / 200f;

            if (Rand.Chance(Mathf01(chance)))
            {
                hp.confidence = System.Math.Max(0f, hp.confidence - 15f);
                string who = rescuer != null ? rescuer.LabelShort : "Someone";
                Messages.Message(who + " stepped in and stopped " + harasser.LabelShort + ".",
                    new LookTargets(new[] { harasser, victim }), MessageTypeDefOf.PositiveEvent, false);
                return true;
            }
            Messages.Message("An attempt to intervene failed.",
                new LookTargets(new[] { harasser, victim }), MessageTypeDefOf.NegativeEvent, false);
            return false;
        }

        private static Pawn FindIntervener(Pawn harasser, Pawn target)
        {
            Map map = harasser.Map;
            if (map == null) return null;
            Pawn best = null;
            float bestDist = 9999f;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == harasser || p == target) continue;
                if (p.Downed || !p.Awake() || p.InMentalState) continue;
                float d = p.Position.DistanceTo(harasser.Position);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        // ── Interaction / thought helpers ─────────────────────────────────────
        private static System.Reflection.FieldInfo _lastInteractField;
        /// <summary>Rewinds the tracker's private 120-tick interaction cooldown so a scripted line can fire
        /// immediately. CanInteractNowWith enforces that cooldown BEFORE ignoreTimeSinceLastInteraction is
        /// ever checked, which otherwise silently blocks our interactions (no log entry -> no SpeakUp bubble).
        /// SpeakUp does the same rewind in its own FireStatement.</summary>
        private static void AllowImmediateInteraction(Pawn p)
        {
            var it = p?.interactions;
            if (it == null) return;
            try
            {
                if (_lastInteractField == null)
                    _lastInteractField = typeof(Pawn_InteractionsTracker).GetField("lastInteractionTime",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _lastInteractField?.SetValue(it, Find.TickManager.TicksGame - 1000);
            }
            catch { }
        }

        /// <summary>Public flavor-line entry (used by the scheduled multi-line exchange): fires an interaction
        /// purely so SpeakUp shows its bubble. Use defs without a recipientThought to avoid stacking moods.</summary>
        public static bool FireFlavorLine(Pawn speaker, Pawn recipient, InteractionDef def) => FireInteraction(speaker, recipient, def);

        private static bool FireInteraction(Pawn harasser, Pawn target, InteractionDef def)
        {
            if (def == null || harasser?.interactions == null || target == null) return false;
            try
            {
                AllowImmediateInteraction(harasser);
                return harasser.interactions.TryInteractWith(target, def);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[RJW Sexual Harassment] interaction failed: " + ex.Message, 0x5A12E0);
                return false;
            }
        }

        /// <summary>When multi-line harassment is on, queues a short back-and-forth of flavor bubbles after a
        /// verbal approach: the victim reacts, the harasser presses, alternating, spaced a couple seconds apart.</summary>
        private static void ScheduleApproachExchange(Pawn harasser, Pawn target, ApproachType approach)
        {
            int n = System.Math.Max(0, S.harassmentExtraLines);
            if (n <= 0) return;
            int pace = System.Math.Max(60, S.harassmentLineSpacing);
            var (reply, press) = ExchangePools(approach);
            for (int i = 0; i < n; i++)
            {
                int delay = (i + 1) * pace;
                if (i % 2 == 0)
                    MapComponent_HarassmentScan.ScheduleLine(target, harasser, reply, delay);   // victim reacts
                else
                    MapComponent_HarassmentScan.ScheduleLine(harasser, target, press, delay);    // harasser presses
            }
        }

        /// <summary>The victim-reaction and harasser-press dialogue pools themed to a given approach.</summary>
        private static (InteractionDef reply, InteractionDef press) ExchangePools(ApproachType t)
        {
            switch (t)
            {
                case ApproachType.Catcall:       return (RJWSH_InteractionDefOf.RJWSH_CatcallReply, RJWSH_InteractionDefOf.RJWSH_CatcallPress);
                case ApproachType.Proposition:   return (RJWSH_InteractionDefOf.RJWSH_PropositionReply, RJWSH_InteractionDefOf.RJWSH_PropositionPress);
                case ApproachType.Flirt:         return (RJWSH_InteractionDefOf.RJWSH_FlirtReply, RJWSH_InteractionDefOf.RJWSH_FlirtPress);
                case ApproachType.SpikedDrink:   return (RJWSH_InteractionDefOf.RJWSH_FanReply, RJWSH_InteractionDefOf.RJWSH_FanPress);
                case ApproachType.DeviousDevice: return (RJWSH_InteractionDefOf.RJWSH_DeviousReply, RJWSH_InteractionDefOf.RJWSH_DeviousPress);
                case ApproachType.Blackmail:     return (RJWSH_InteractionDefOf.RJWSH_BlackmailReply, RJWSH_InteractionDefOf.RJWSH_BlackmailPress);
                case ApproachType.Hypnosis:      return (RJWSH_InteractionDefOf.RJWSH_HypnosisDoubt, RJWSH_InteractionDefOf.RJWSH_Hypnosis);
                default:                         return (RJWSH_InteractionDefOf.RJWSH_HarassReply, RJWSH_InteractionDefOf.RJWSH_HarassChatter);
            }
        }

        /// <summary>How long a paced verbal exchange runs, so the harasser can linger and let it play out
        /// before the physical stage begins. Zero when multi-line harassment is off.</summary>
        public static int ExchangeDuration()
        {
            if (S == null || !S.multiLineHarassment) return 0;
            int n = System.Math.Max(0, S.harassmentExtraLines);
            if (n <= 0) return 0;
            int pace = System.Math.Max(60, S.harassmentLineSpacing);
            return (n + 1) * pace; // +1 so the last line lands just before escalation
        }

        private static void ApplyThought(Pawn target, Pawn about, ThoughtDef def)
        {
            if (def == null || target?.needs?.mood?.thoughts?.memories == null) return;
            try { target.needs.mood.thoughts.memories.TryGainMemory(def, about); }
            catch { }
        }

        private static void MakeBubble(Pawn initiator, Pawn recipient, InteractionDef def)
        {
            if (def == null) return;
            try
            {
                MoteMaker.MakeInteractionBubble(initiator, recipient, def.interactionMote,
                    def.GetSymbol(initiator.Faction, initiator.Ideo), def.GetSymbolColor(initiator.Faction));
            }
            catch { }
        }

        // ── Classification ────────────────────────────────────────────────────
        public static PawnCategory Categorize(Pawn p)
        {
            if (p == null) return PawnCategory.Other;
            if (p.IsPrisonerOfColony) return PawnCategory.Prisoner;
            if (p.IsSlaveOfColony) return PawnCategory.Slave;
            if (p.Faction != null && p.Faction.IsPlayer) return PawnCategory.Colonist;
            if (p.Faction != null && !p.HostileTo(Faction.OfPlayer)) return PawnCategory.Visitor;
            return PawnCategory.Other;
        }

        public static bool InvolvesPlayerPawn(Pawn a, Pawn b) => IsPlayerOwned(a) || IsPlayerOwned(b);

        public static bool IsPlayerOwned(Pawn p) =>
            p != null && ((p.Faction != null && p.Faction.IsPlayer) || p.IsPrisonerOfColony || p.IsSlaveOfColony);

        // ── Math / RJW safe wrappers ──────────────────────────────────────────
        /// <summary>True if the pawn is in an RJW sex act or a c0ffee animation - don't start approaches then.</summary>
        public static bool IsBusyInAct(Pawn p)
        {
            if (p?.jobs?.curDriver is rjw.JobDriver_Sex) return true;
            return CoffeeIsAnimating(p);
        }

        private static bool _coffeeTried;
        private static System.Type _coffeeCompType;
        private static System.Reflection.PropertyInfo _coffeeIsAnimating;
        // Cache the c0ffee comp reference per pawn so IsBusyInAct (a hot path) does not LINQ-scan AllComps every call.
        private sealed class CoffeeCache { public ThingComp comp; public bool resolved; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pawn, CoffeeCache> _coffeeCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Pawn, CoffeeCache>();
        private static bool CoffeeIsAnimating(Pawn p)
        {
            if (p == null) return false;
            try
            {
                if (!_coffeeTried)
                {
                    _coffeeTried = true;
                    _coffeeCompType = GenTypes.GetTypeInAnyAssembly("Rimworld_Animations.CompExtendedAnimator");
                    if (_coffeeCompType != null)
                        _coffeeIsAnimating = _coffeeCompType.GetProperty("IsAnimating");
                }
                if (_coffeeCompType == null || _coffeeIsAnimating == null) return false;
                var entry = _coffeeCache.GetValue(p, _ => new CoffeeCache());
                if (!entry.resolved)
                {
                    entry.comp = p.AllComps?.FirstOrDefault(c => _coffeeCompType.IsInstanceOfType(c));
                    entry.resolved = true;
                }
                if (entry.comp == null) return false;
                return (bool)_coffeeIsAnimating.GetValue(entry.comp);
            }
            catch { return false; }
        }

        private static float SafeVulnerability(Pawn p)
        {
            try { return Mathf01(xxx.get_vulnerability(p)); } catch { return 0.5f; }
        }

        private static float SafeSexDrive(Pawn p)
        {
            try { return Mathf01(xxx.get_sex_drive(p)); } catch { return 0.5f; }
        }

        // ── Market-preview reads (no profile side effects) ──
        public static float SexDrive01(Pawn p) => SafeSexDrive(p);

        private static System.Reflection.MethodInfo _isVirginMethod;
        private static bool _isVirginResolved;
        /// <summary>RJW virginity, resolved by reflection so it is version-safe. Null when RJW has no such API.</summary>
        public static bool? IsVirgin(Pawn p)
        {
            if (!_isVirginResolved)
            {
                _isVirginResolved = true;
                try { _isVirginMethod = typeof(xxx).GetMethod("is_virgin", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static); } catch { }
            }
            if (_isVirginMethod == null || p == null) return null;
            try { return (bool)_isVirginMethod.Invoke(null, new object[] { p }); } catch { return null; }
        }

        /// <summary>Deterministic conditionability factor (genes + traits, no random base) for market previews.</summary>
        public static float PreviewSusceptibility(Pawn p)
        {
            float f = GeneHelper.SusceptibilityGeneFactor(p);
            var tr = p?.story?.traits;
            if (tr != null)
            {
                if (HasTraitNamed(tr, "Masochist")) f *= 1.4f;
                if (HasTraitNamed(tr, "Wimp")) f *= 1.3f;
                if (HasTraitNamed(tr, "Kind")) f *= 1.15f;
                if (HasTraitNamed(tr, "Tough")) f *= 0.7f;
                if (HasTraitNamed(tr, "Psychopath")) f *= 0.8f;
                if (HasTraitNamed(tr, "Bloodlust")) f *= 0.85f;
            }
            return f;
        }

        /// <summary>The act a pawn is best suited to, from degradation quirks then genitalia.</summary>
        public static string BestSexType(Pawn p)
        {
            try { if (QuirksBridge.HasQuirk(p, "Buttslut")) return "anal"; } catch { }
            try { if (QuirksBridge.HasQuirk(p, "Cumslut")) return "oral"; } catch { }
            if (SexAttributes.HasVagina(p)) return "vaginal";
            if (SexAttributes.HasPenis(p)) return "penetration";
            if (SexAttributes.HasMouth(p)) return "oral";
            return "any";
        }

        private static float Mathf01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Local night at the pawn's location (22:00-06:00). False on error.</summary>
        public static bool IsNightFor(Pawn p)
        {
            try { int h = GenLocalDate.HourOfDay(p); return h >= 22 || h < 6; } catch { return false; }
        }

        /// <summary>Local daytime at the pawn's location (08:00-19:00). True on error.</summary>
        public static bool IsDaytimeFor(Pawn p)
        {
            try { int h = GenLocalDate.HourOfDay(p); return h >= 8 && h <= 19; } catch { return true; }
        }

        /// <summary>Credits a pet's lifetime earnings (whoring payouts + being auctioned) for the harem income view.</summary>
        public static void AddEarnings(Pawn pet, int amount)
        {
            if (pet == null || amount <= 0) return;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            if (prof != null) prof.lifetimeEarnings += amount;
        }

        private static bool IsVitalJob(Pawn p)
        {
            var jd = p?.CurJobDef;
            return jd == JobDefOf.LayDown || jd == JobDefOf.Ingest || jd == RJWSH_JobDefOf.RJWSH_BeingStripped
                   || (p?.jobs?.curDriver is rjw.JobDriver_Sex);
        }

        private static WorkTypeDef _haremWork;
        /// <summary>True unless the pet is a worker whose "Harem" work type is turned off in the Work tab (priority
        /// 0). Lets the player pause a pet's schedule from the Work tab. Non-workers (prisoners, etc.) always run.</summary>
        public static bool HaremWorkEnabled(Pawn p)
        {
            try
            {
                if (_haremWork == null) _haremWork = DefDatabase<WorkTypeDef>.GetNamedSilentFail("RJWSH_Harem");
                if (_haremWork == null || p?.workSettings == null || !p.workSettings.EverWork) return true;
                return p.workSettings.GetPriority(_haremWork) > 0;
            }
            catch { return true; }
        }

        /// <summary>Where a "Confined" pet should be: inside the ROOM containing its quarters cell (wandering to a
        /// random spot within when already there), walking to the quarters cell when outside it, or the owner's side
        /// when no quarters are set. This makes "Set quarters" confine to the whole room, not a single tile.</summary>
        public static IntVec3 ConfinementDest(Pawn p, PawnProfile prof, Pawn owner)
        {
            var map = p?.Map;
            if (prof != null && prof.quartersCell.IsValid && map != null && prof.quartersCell.InBounds(map))
            {
                Room room = prof.quartersCell.GetRoom(map);
                if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
                {
                    if (p.Position.GetRoom(map) == room && TryRandomRoomCell(room, p, out IntVec3 rc)) return rc;
                    return prof.quartersCell;   // outside the room -> walk back to the anchor
                }
                return prof.quartersCell;   // outdoors / no proper room -> the single spot
            }
            return (owner != null && owner.Spawned) ? owner.Position : IntVec3.Invalid;
        }

        private static bool TryRandomRoomCell(Room room, Pawn p, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var map = p.Map;
            int count = 0;
            foreach (var c in room.Cells)
            {
                if (!c.Standable(map) || c.IsForbidden(p)) continue;
                count++;
                if (Rand.Range(0, count) == 0) cell = c;   // reservoir sampling -> uniform random standable cell
            }
            return cell.IsValid;
        }

        /// <summary>Drives a pet by its 24-hour schedule (when one is set): the current hour's assignment sends
        /// them to Serve / Train / Parade / Confined, or Free/Rest (no override). Cooldown-gated so it does not
        /// spam jobs. Overrides the simple auto-parade + curfew toggles while a schedule exists, and is paused
        /// when the pet's "Harem" work type is disabled in the Work tab.</summary>
        public static void RunScheduleTick(Pawn p, PawnProfile prof, Pawn owner)
        {
            if (p == null || prof?.schedule == null || prof.schedule.Count != 24 || p.Downed || IsBusyInAct(p) || !p.Awake()) return;
            if (!HaremWorkEnabled(p)) return;
            int now = Find.TickManager.TicksGame;
            int a = prof.schedule[GenLocalDate.HourOfDay(p)];
            switch (a)
            {
                case 5: // Confined -> the quarters ROOM (wander within), or the owner's side
                {
                    IntVec3 dest = ConfinementDest(p, prof, owner);
                    if (dest.IsValid && p.CurJobDef != RJWSH_JobDefOf.RJWSH_StayPut && !IsVitalJob(p)
                        && p.CanReach(dest, PathEndMode.OnCell, Danger.Deadly))
                        p.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_StayPut, dest), JobCondition.InterruptForced);
                    break;
                }
                case 3: // Parade
                    if (owner != null && now >= prof.paradeCooldownTick && IsDaytimeFor(p))
                    { DepthStartParade(owner, p); prof.paradeCooldownTick = now + Rand.Range(30000, 50000); }
                    break;
                case 1: // Serve
                    if (owner != null && now >= prof.controlCooldownTick && !(p.jobs?.curDriver is rjw.JobDriver_Sex))
                    {
                        if (!TryVenueService(p)) { var tgt = PickServiceTarget(p, prof, owner); if (tgt != null) RunService(p, tgt, prof.serviceInteraction); }
                        prof.controlCooldownTick = now + (S?.autoServiceIntervalTicks ?? 2500);
                    }
                    break;
                case 2: // Train
                    if (owner != null && now >= prof.scheduleCooldownTick && !owner.Downed
                        && owner.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                    { StartTraining(owner, p, string.IsNullOrEmpty(prof.trainFocus) ? "willpower" : prof.trainFocus); prof.scheduleCooldownTick = now + Rand.Range(15000, 25000); }
                    break;
                // 0 Free, 4 Rest -> no override (their own think tree; Rest lets them sleep freely)
            }
        }

        private static T WeightedPick<T>(List<T> pool, List<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            if (total <= 0f) return pool.Count > 0 ? pool[0] : default;
            float r = Rand.Value * total;
            for (int i = 0; i < pool.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}
