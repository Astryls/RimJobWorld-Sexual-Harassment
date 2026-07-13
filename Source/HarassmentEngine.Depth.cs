using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    // Depth systems: breaking stages, autonomous conditioned behavior, possessive rivalry, pet pecking order,
    // trauma effects, addiction withdrawal, owner codependency, parading, specializations, romance corruption,
    // and a rival collector. Kept in a partial of HarassmentEngine so it can reuse the engine's helpers.
    public static partial class HarassmentEngine
    {
        public enum BreakStage { Defiant, Wavering, Compliant, Devoted, Broken }

        /// <summary>True if this pawn is a control target (collar, locked device, or an owner link) - used to
        /// show the Control tab.</summary>
        public static bool IsControllablePet(Pawn p)
        {
            if (p == null) return false;
            if (WearingControlCollar(p) || IsLockedPawn(p)) return true;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            return prof != null && (prof.ownerId >= 0 || prof.relationshipOwnerId >= 0);
        }

        /// <summary>Public wrapper for the cross-map pawn lookup by thingID.</summary>
        public static Pawn FindPawnByIdPublic(int id) => FindPawnByIdAnyMap(id);

        /// <summary>Gate for every mod-driven pairing: when heterosexualOnly is set, only strictly M&lt;-&gt;F
        /// pairs are allowed. Otherwise always true (RJW/orientation logic decides).</summary>
        public static bool GenderOk(Pawn a, Pawn b)
        {
            if (S == null || !S.heterosexualOnly) return true;
            if (a == null || b == null) return true;
            return (a.gender == Gender.Male && b.gender == Gender.Female)
                || (a.gender == Gender.Female && b.gender == Gender.Male);
        }

        // ── Breaking stages ──────────────────────────────────────────────────
        public static BreakStage GetBreakStage(Pawn p, PawnProfile prof)
        {
            if (prof == null) return BreakStage.Defiant;
            bool stockholm = false;
            try { stockholm = HasTraitNamed(p?.story?.traits, "RJWSH_StockholmSyndrome"); } catch { }
            float h = prof.hypnosisLevel;
            float will = prof.sex != null ? prof.sex.willpower : 50f;
            if (stockholm || (h >= 90f && will < 25f)) return BreakStage.Broken;
            if (h >= 90f) return BreakStage.Devoted;
            if (h >= 60f) return BreakStage.Compliant;
            if (h >= 30f) return BreakStage.Wavering;
            return BreakStage.Defiant;
        }

        private static string StageLabel(BreakStage s)
        {
            switch (s)
            {
                case BreakStage.Defiant: return "Defiant";
                case BreakStage.Wavering: return "Wavering";
                case BreakStage.Compliant: return "Compliant";
                case BreakStage.Devoted: return "Devoted";
                case BreakStage.Broken: return "Broken";
                default: return "";
            }
        }

        /// <summary>Detects breaking-stage transitions and sends a letter on the meaningful ones.</summary>
        public static void DepthStageTick(Pawn p, PawnProfile prof)
        {
            if (prof == null || p == null) return;
            var stage = GetBreakStage(p, prof);
            if (prof.breakStage == (int)stage) return;
            bool up = (int)stage > prof.breakStage;
            bool first = prof.breakStage < 0;
            prof.breakStage = (int)stage;
            if (first) return; // don't fire a letter for the initial classification
            if (!IsPlayerOwned(p) || (prof.ownerId < 0 && prof.relationshipOwnerId < 0)) return;
            if (stage == BreakStage.Devoted || stage == BreakStage.Broken || (!up && stage <= BreakStage.Wavering))
            {
                try
                {
                    Find.LetterStack.ReceiveLetter(p.LabelShortCap + ": " + StageLabel(stage),
                        p.LabelShortCap + " has become " + StageLabel(stage).ToLower() + ".",
                        up ? LetterDefOf.NeutralEvent : LetterDefOf.NegativeEvent, new LookTargets(p));
                }
                catch { }
            }
        }

        // ── Autonomous conditioned behavior ──────────────────────────────────
        /// <summary>A devoted/submissive pet, of its own accord, attends and presents itself to its owner.</summary>
        public static void DepthAutonomousTick(Pawn p, PawnProfile prof, Pawn owner)
        {
            if (p == null || owner == null || !p.Spawned || p.Downed || !owner.Spawned || owner.Map != p.Map) return;
            int now = Find.TickManager.TicksGame;
            if (now < prof.depthCooldownTick) return;
            var stage = GetBreakStage(p, prof);
            float subdom = prof.sex != null ? prof.sex.subDom : 0f;
            // Bodyguards attend their owner earlier; everyone else needs to be fully devoted.
            BreakStage min = prof.petRole == 3 ? BreakStage.Compliant : BreakStage.Devoted;
            if (stage < min || subdom > 10f) return;
            bool idle = p.CurJob == null || p.CurJobDef == JobDefOf.Wait_Wander || p.CurJobDef == JobDefOf.GotoWander || p.CurJobDef == JobDefOf.Wait;
            if (!idle || IsBusyInAct(p)) return;
            float d = p.Position.DistanceTo(owner.Position);
            float presentChance = prof.petRole == 4 ? 0.6f : 0.4f; // performers show off more
            var subNeed = Need_Submission.For(p);
            if (subNeed != null) presentChance += (1f - subNeed.CurLevel) * 0.3f; // craving submission -> presents more eagerly
            if (d > 6f && Rand.Chance(0.5f) && p.CanReach(owner, PathEndMode.Touch, Danger.Some))
            {
                p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, owner.Position), JobCondition.InterruptForced);
                prof.depthCooldownTick = now + Rand.Range(5000, 10000);
            }
            else if (d <= 8f && Rand.Chance(presentChance))
            {
                FireFlavorLine(p, owner, RJWSH_InteractionDefOf.RJWSH_Present);
                ThrowControlMote(p, "\u2665", new Color(1f, 0.6f, 0.8f));
                prof.depthCooldownTick = now + Rand.Range(7000, 13000);
            }
        }

        // ── Possessive rivalry + jealousy ────────────────────────────────────
        /// <summary>The owner feels a jealous pang when someone else uses their pet (unless they sent them to).</summary>
        public static void DepthOnPetUsed(Pawn user, Pawn pet)
        {
            if (pet == null || user == null || user == pet) return;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            if (prof == null) return;
            int oid = prof.ownerId >= 0 ? prof.ownerId : prof.relationshipOwnerId;
            if (oid < 0) return;
            var owner = FindPawnByIdAnyMap(oid);
            if (owner == null || owner == user || !IsPlayerOwned(owner)) return;
            if (prof.whoreOwnerId == owner.thingIDNumber || prof.autoService) return; // the owner arranged this
            TryAddMoodThought(owner, "RJWSH_Jealous");
        }

        /// <summary>A dominant/cruel non-owner nearby covets a desirable pet and may challenge the owner.</summary>
        public static void DepthRivalryTick(Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (pet?.Map == null || owner == null) return;
            int now = Find.TickManager.TicksGame;
            if (now < prof.depthCooldownTick) return;
            if (prof.hypnosisLevel < 60f) return; // only well-conditioned pets are worth coveting
            Pawn rival = null;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c == pet || c == owner || c.Dead || c.Downed || c.RaceProps == null || !c.RaceProps.Humanlike) continue;
                if (pet.Position.DistanceTo(c.Position) > 10f) continue;
                float cdom = GameComponent_Harassment.Instance?.GetProfileIfExists(c)?.sex?.subDom ?? 0f;
                bool covetous = cdom > 30f || Evilness(c) > 0.5f || IsSadist(c);
                if (!covetous) continue;
                rival = c; break;
            }
            if (rival == null || !Rand.Chance(0.35f)) return;
            FireFlavorLine(rival, owner, RJWSH_InteractionDefOf.RJWSH_Covet);
            prof.depthCooldownTick = now + Rand.Range(15000, 40000);
            if (Rand.Chance(0.25f) && owner.Spawned && !owner.Downed && !owner.HostileTo(Faction.OfPlayer))
                StartScuffle(owner, rival); // the rival squares up to the owner over the pet
        }

        // ── Pecking order among an owner's pets ──────────────────────────────
        public static void DepthPeckingTick(Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (pet?.Map == null || owner == null) return;
            int now = Find.TickManager.TicksGame;
            if (now < prof.depthCooldownTick) return;
            float myDom = prof.sex != null ? prof.sex.subDom : 0f;
            if (myDom < 15f) return; // only a dominant pet lords it over the others
            var gc = GameComponent_Harassment.Instance;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            Pawn under = null;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c == pet || c.Dead || c.Downed || c.RaceProps == null || !c.RaceProps.Humanlike) continue;
                if (pet.Position.DistanceTo(c.Position) > 8f) continue;
                var cp = gc?.GetProfileIfExists(c);
                if (cp == null) continue;
                int coid = cp.ownerId >= 0 ? cp.ownerId : cp.relationshipOwnerId;
                if (coid != owner.thingIDNumber) continue;
                float cdom = cp.sex != null ? cp.sex.subDom : 0f;
                if (cdom < myDom - 20f) { under = c; break; }
            }
            if (under == null || !Rand.Chance(0.4f)) return;
            FireFlavorLine(pet, under, RJWSH_InteractionDefOf.RJWSH_Assert);
            TryAddMoodThought(under, "RJWSH_Bullied");
            AttrDelta(under, esteem: -2f, subdom: -2f);
            AttrDelta(pet, subdom: 1f);
            prof.depthCooldownTick = now + Rand.Range(20000, 50000);
        }

        // ── Trauma effects ───────────────────────────────────────────────────
        /// <summary>Deep trauma jolts the pawn awake with a night terror. (The ongoing mood is the situational
        /// RJWSH_TraumatizedMood thought.)</summary>
        public static void DepthTraumaTick(Pawn p, PawnProfile prof)
        {
            var sx = prof?.sex;
            if (sx == null || sx.trauma < 65f) return;
            try { if (rjw.xxx.is_masochist(p)) return; } catch { }
            int now = Find.TickManager.TicksGame;
            if (now < prof.nightTerrorTick) return;
            if (!p.Awake() && p.CurJobDef == JobDefOf.LayDown && Rand.Chance(0.3f))
            {
                p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                ThrowControlMote(p, "!", new Color(0.7f, 0.3f, 0.3f));
                if (IsPlayerOwned(p))
                    Messages.Message(p.LabelShort + " woke screaming from a night terror.", new LookTargets(p), MessageTypeDefOf.NeutralEvent, false);
                prof.nightTerrorTick = now + Rand.Range(30000, 60000);
            }
        }

        // ── Sex-addiction withdrawal ─────────────────────────────────────────
        public static void DepthAddictionTick(Pawn p, PawnProfile prof)
        {
            var sx = prof?.sex;
            if (sx == null || sx.sexAddiction < 55f) return;
            try
            {
                var need = p.needs?.TryGetNeed<rjw.Need_Sex>();
                if (need != null && need.CurLevel < 0.3f)
                    TryAddMoodThought(p, "RJWSH_Withdrawal");
            }
            catch { }
        }

        // ── Ongoing conditioning (the Control tab's training focus, like slave resistance-reduction) ──
        /// <summary>While a training focus is set, the pet is continuously conditioned toward it: a small hourly
        /// drift on the chosen attribute (faster when deeply conditioned + high rapport, resisted by willpower),
        /// with the owner occasionally coming over for a full session.</summary>
        public static void DepthTrainingTick(Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (prof == null || prof.trainFocus.NullOrEmpty() || pet == null || !pet.Spawned || pet.Dead) return;
            var sx = prof.SexAttr(pet);
            if (sx == null) return;
            float rate = 0.5f + prof.hypnosisLevel / 100f * 1.5f + prof.rapport / 100f * 0.5f;
            rate *= Mathf01(1f - sx.willpower / 200f); // a strong will slows the conditioning
            rate *= GeneHelper.ConditioningGainFactor(pet) * ConditioningReceptivity(pet); // genes + quirks/lust bend it
            if (rate < 0.05f) rate = 0.05f;
            switch (prof.trainFocus)
            {
                case "willpower": sx.willpower = Clamp100(sx.willpower - rate); break;
                case "esteem": sx.selfEsteem = Clamp100(sx.selfEsteem - rate); break;
                case "spirit": sx.spirit = Clamp100(sx.spirit - rate); break;
                case "subdom": sx.subDom = UnityEngine.Mathf.Clamp(sx.subDom - rate, -100f, 100f); break;
                case "addiction": sx.sexAddiction = Clamp100(sx.sexAddiction + rate); break;
                default: return;
            }
            // The owner periodically comes over to run a proper session (bigger, flavored shift).
            if (owner != null && owner.Spawned && !owner.Downed && pet.Awake() && Find.TickManager.TicksGame >= prof.tendCooldownTick
                && owner.CanReach(pet, PathEndMode.Touch, Danger.Deadly) && Rand.Chance(0.3f))
            {
                StartTraining(owner, pet, prof.trainFocus);
                prof.tendCooldownTick = Find.TickManager.TicksGame + Rand.Range(10000, 20000); // auto sessions are hours apart
            }
        }

        // ── Owner codependency ───────────────────────────────────────────────
        public static void DepthCodependencyTick(Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (owner == null || !IsPlayerOwned(owner)) return;
            if (GetBreakStage(pet, prof) >= BreakStage.Devoted)
                TryAddMoodThought(owner, "RJWSH_DevotedPetComfort");
        }

        /// <summary>The owner grieves when they lose a devoted pet (freed, broke away, or died).</summary>
        public static void DepthOnPetLost(Pawn pet, int ownerId)
        {
            if (ownerId < 0) return;
            var owner = FindPawnByIdAnyMap(ownerId);
            if (owner == null || !IsPlayerOwned(owner)) return;
            TryAddMoodThought(owner, "RJWSH_LostPet");
        }

        // ── Parading ─────────────────────────────────────────────────────────
        /// <summary>The owner strips the pet bare and sends them on a humiliating parade around the colony -
        /// the pet walks a circuit past the colonists while onlookers react, draining self-worth and raising
        /// the colony's notoriety.</summary>
        public static void DepthStartParade(Pawn owner, Pawn pet)
        {
            if (owner == null || pet == null || !pet.Spawned || pet.Map == null || pet.Downed) return;
            StripAll(pet); // strip them naked for the spectacle
            ApplyThought(pet, owner, RJWSH_ThoughtDefOf.RJWSH_Humiliated);
            AttrDelta(pet, esteem: -6f, subdom: -3f, trauma: 1f);
            GameComponent_Harassment.Instance?.AddNotoriety(3, "rjwsh_parade");
            // The parading owner's name spreads as a slaver - personal infamy - and the moment becomes a tale.
            ReputationBridge.AddReputation(owner, -4f, "rjwsh_parade");
            TaleHelper.Record("RJWSH_Tale_Paraded", owner, pet);
            Chronicle(pet, "Paraded naked through the colony by " + owner.LabelShortCap + ".", 1);
            if (InvolvesPlayerPawn(owner, pet))
                Messages.Message(owner.LabelShort + " is parading " + pet.LabelShort + " naked through the colony.",
                    new LookTargets(pet), MessageTypeDefOf.NeutralEvent, false);
            try { pet.jobs?.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_Parade), JobCondition.InterruptForced); } catch { }
        }

        /// <summary>Cells for the parade circuit - spots near several free colonists so the pet is seen.</summary>
        public static List<IntVec3> ParadeStops(Pawn pet, int n)
        {
            var res = new List<IntVec3>();
            var map = pet?.Map; if (map == null) return res;
            var cols = new List<Pawn>(map.mapPawns.FreeColonistsSpawned);
            for (int i = 0; i < n && cols.Count > 0; i++)
            {
                var col = cols[Rand.Range(0, cols.Count)];
                if (CellFinder.TryFindRandomCellNear(col.Position, map, 4,
                        cell => cell.Standable(map) && !cell.Fogged(map) && pet.CanReach(cell, PathEndMode.OnCell, Danger.Some),
                        out var found))
                    res.Add(found);
            }
            if (res.Count == 0) res.Add(map.Center);
            return res;
        }

        /// <summary>Onlookers within earshot react to the paraded pet (leer or disgust by their nature).</summary>
        public static void DepthParadeReactAround(Pawn pet)
        {
            if (pet?.Map == null) return;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            int reacted = 0;
            for (int i = 0; i < pawns.Count && reacted < 3; i++)
            {
                var c = pawns[i];
                if (c == pet || c.Dead || c.RaceProps == null || !c.RaceProps.Humanlike) continue;
                if (pet.Position.DistanceTo(c.Position) > 10f) continue;
                FireFlavorLine(c, pet, Evilness(c) > 0.5f ? RJWSH_InteractionDefOf.RJWSH_WitnessLeer : RJWSH_InteractionDefOf.RJWSH_WitnessDisgust);
                reacted++;
            }
        }

        // ── Romance corruption ───────────────────────────────────────────────
        /// <summary>When a pawn is collared, their lover(s) feel the possessive sting of it.</summary>
        public static void DepthNotifyLoversOnCollar(Pawn pet)
        {
            if (pet?.relations == null) return;
            try
            {
                foreach (var rel in LovePartnerRelationUtility.ExistingLovePartners(pet, false))
                {
                    var lover = rel.otherPawn;
                    if (lover != null && lover.Spawned && !lover.Dead && lover != pet)
                        TryAddMoodThought(lover, "RJWSH_Jealous");
                }
            }
            catch { }
        }

        // ── Conditioning specializations (roles) ─────────────────────────────
        private static readonly string[] RoleNames = { "none", "pleasure pet", "house servant", "bodyguard", "performer" };
        public static string PetRoleLabel(int role) => (role >= 0 && role < RoleNames.Length) ? RoleNames[role] : "none";
        public static void SetPetRole(Pawn owner, Pawn pet, int role)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfile(pet);
            if (prof == null) return;
            prof.petRole = role;
            if (InvolvesPlayerPawn(owner, pet))
                Messages.Message(pet.LabelShort + " is being conditioned as a " + PetRoleLabel(role) + ".", new LookTargets(pet), MessageTypeDefOf.NeutralEvent, false);
        }

        // ── Selling a pet to a present trader ────────────────────────────────
        public static bool AnyActiveTrader(Map map)
        {
            if (map == null) return false;
            try { if (map.passingShipManager != null && map.passingShipManager.passingShips.Count > 0) return true; } catch { }
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i]?.TraderKind != null && !pawns[i].Dead) return true;
            return false;
        }

        public static void DepthSellPet(Pawn owner, Pawn pet, int priceOverride = -1)
        {
            if (pet == null || !pet.Spawned || pet.Map == null) return;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            int price = priceOverride;
            if (price < 0)
            {
                price = 200;
                if (prof != null)
                {
                    price += (int)(prof.hypnosisLevel * 4f);
                    if (prof.sex != null) price += (int)((100f - prof.sex.willpower) * 1.5f + prof.sex.sexAddiction);
                }
                try { price += (int)(pet.GetStatValue(StatDefOf.PawnBeauty) * 60f); } catch { }
            }
            if (price < 100) price = 100;
            var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = price;
            GenPlace.TryPlaceThing(silver, (owner?.Spawned == true ? owner : pet).Position, pet.Map, ThingPlaceMode.Near);
            int oid = prof != null ? prof.ownerId : -1;
            if (owner != null) { TaleHelper.Record("RJWSH_Tale_SoldPet", owner, pet); Chronicle(owner, "Sold " + pet.LabelShortCap + " for " + price + " silver.", 1); }
            Chronicle(pet, "Sold off to a trader for " + price + " silver.", 1);
            ReputationBridge.AddReputation(owner, -6f, "rjwsh_pet_sold");
            FreeCollared(pet);
            if (InvolvesPlayerPawn(owner, pet))
                Messages.Message(pet.LabelShort + " was sold to a trader for " + price + " silver.", new LookTargets(pet), MessageTypeDefOf.PositiveEvent, false);
            if (prof != null && GetBreakStage(pet, prof) >= BreakStage.Devoted) DepthOnPetLost(pet, oid);
            RegisterNemesis(pet, oid, true);   // a sold pet may one day come back for revenge
            try { pet.ExitMap(false, Rot4.Random); } catch { }
        }

        /// <summary>While a rescue raid is active (prof.rescueRaidUntilTick), a raider reaching the flagged pet strips
        /// the collar and spirits them off the map (the owner grieves a devoted loss). The window expiring = failed.</summary>
        public static void RescueRaidTick(Map map)
        {
            if (map == null) return;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;
            int now = Find.TickManager.TicksGame;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pet = pawns[i];
                if (pet == null || pet.Dead) continue;
                var prof = gc.GetProfileIfExists(pet);
                if (prof == null || prof.rescueRaidUntilTick <= 0) continue;
                if (now > prof.rescueRaidUntilTick) { prof.rescueRaidUntilTick = 0; continue; } // window closed - rescue failed
                var raider = NearbyRaider(pet, 2.9f);
                if (raider == null) continue;
                int oid = prof.ownerId >= 0 ? prof.ownerId : prof.relationshipOwnerId;
                FreeCollared(pet);
                prof.rescueRaidUntilTick = 0;
                try { if (raider.Faction != null) pet.SetFaction(raider.Faction); } catch { }
                if (oid >= 0) DepthOnPetLost(pet, oid);
                RegisterNemesis(pet, oid, false);   // a rescued pet remembers who collared them
                Chronicle(pet, "Freed from the collar and spirited away by raiders.", 2);
                if (InvolvesPlayerPawn(raider, pet))
                    Messages.Message(pet.LabelShortCap + " has been freed from the collar and spirited away by the raiders.",
                        new LookTargets(pet), MessageTypeDefOf.NegativeEvent, false);
                try { pet.ExitMap(false, Rot4.Random); } catch { }
            }
        }

        private static Pawn NearbyRaider(Pawn pet, float radius)
        {
            if (pet?.Map == null) return null;
            var pawns = pet.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var h = pawns[i];
                if (h == null || h.Dead || h.Downed || !h.RaceProps.Humanlike) continue;
                if (h.Faction == null || !h.HostileTo(Faction.OfPlayer)) continue;
                if (pet.Position.DistanceTo(h.Position) <= radius) return h;
            }
            return null;
        }

        // ── Pet market ───────────────────────────────────────────────────────
        /// <summary>Generates one market pet: a person aged 18-22 (mostly female), a random pet role, and a silver
        /// price plus an alternative faction-goodwill cost.</summary>
        public static MarketEntry MakeMarketEntry()
        {
            try
            {
                var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, faction: null,
                    context: PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false, allowFood: false,
                    fixedBiologicalAge: Rand.Range(18f, 22f),
                    fixedGender: Rand.Chance(0.72f) ? Gender.Female : Gender.Male);
                var pawn = PawnGenerator.GeneratePawn(req);
                if (pawn == null) return null;
                int role = Rand.RangeInclusive(1, 4);
                int price = 700 + Rand.Range(0, 500) + role * 120;
                try { price += (int)(pawn.GetStatValue(StatDefOf.PawnBeauty) * 120f); } catch { }
                var gw = MarketTradeFaction();
                int gwCost = UnityEngine.Mathf.Clamp(price / 30, 8, 45);
                return new MarketEntry { pawn = pawn, role = role, priceSilver = price, goodwillFaction = gw, goodwillCost = gwCost };
            }
            catch { return null; }
        }

        private static Faction MarketTradeFaction()
        {
            Faction best = null;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.IsPlayer || f.defeated) continue;
                if (f.def == null || !f.def.humanlikeFaction) continue;
                if (f.HostileTo(Faction.OfPlayer)) continue;
                if (best == null || Rand.Bool) best = f;
            }
            return best;
        }

        /// <summary>Picks who receives a purchased pet's key: the colony leader if present, else the strongest colonist.</summary>
        public static Pawn StrongestOrLeader(Map map)
        {
            if (map == null) return null;
            var leader = Faction.OfPlayer.leader;
            if (leader != null && leader.Spawned && leader.Map == map && !leader.Dead && !leader.Downed) return leader;
            Pawn best = null; float bestScore = -1f;
            var colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                var c = colonists[i];
                if (c == null || c.Dead || c.Downed) continue;
                float score = 0f;
                try
                {
                    if (c.skills != null) score = c.skills.GetSkill(SkillDefOf.Melee).Level + c.skills.GetSkill(SkillDefOf.Shooting).Level;
                    score += c.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation) * 2f;
                }
                catch { }
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>Finalizes a purchased pet once its drop pod has landed: makes them a colony slave, seeds partial
        /// conditioning, collars them and hands the key to the buyer, and applies the chosen role.</summary>
        public static void MarketDeliverPet(Pawn pawn, Pawn keyHolder, int role)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            try
            {
                if (pawn.Faction != Faction.OfPlayer) pawn.SetFaction(Faction.OfPlayer);
                pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
            }
            catch { }
            var prof = GameComponent_Harassment.Instance?.GetProfile(pawn);
            if (prof != null)
            {
                prof.hypnosisLevel = System.Math.Max(prof.hypnosisLevel, 65f); // arrives already broken in
                prof.SexAttr(pawn);
                if (prof.sex != null) prof.sex.subDom = UnityEngine.Mathf.Min(prof.sex.subDom, -25f);
            }
            if (keyHolder != null) LockControlCollar(pawn, keyHolder);
            if (role > 0 && keyHolder != null) SetPetRole(keyHolder, pawn, role);
        }

        /// <summary>Total colony silver on the map (all non-fogged spawned stacks). Unlike TradeUtility.ColonyHasEnoughSilver
        /// this is NOT limited to orbital-trade-beacon cells, so it works without a trade beacon.</summary>
        public static int ColonySilver(Map map)
        {
            if (map == null) return 0;
            int total = 0;
            var list = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null || t.Position.Fogged(map)) continue;
                total += t.stackCount;
            }
            return total;
        }

        /// <summary>Removes up to `amount` silver from the colony's spawned stacks.</summary>
        public static void SpendColonySilver(Map map, int amount)
        {
            if (map == null || amount <= 0) return;
            var list = new System.Collections.Generic.List<Thing>(map.listerThings.ThingsOfDef(ThingDefOf.Silver));
            for (int i = 0; i < list.Count && amount > 0; i++)
            {
                var t = list[i];
                if (t == null || t.Destroyed || t.Position.Fogged(map)) continue;
                int take = System.Math.Min(amount, t.stackCount);
                amount -= take;
                if (take >= t.stackCount) t.Destroy();
                else t.SplitOff(take).Destroy();
            }
        }

        // ── Rival collector (notoriety consequence) ──────────────────────────
        /// <summary>At high notoriety a nearby cruel non-colonist tries to make off with the best-conditioned
        /// pet - reusing the key-theft + kidnap flow.</summary>
        public static void DepthCollectorAttempt(Map map)
        {
            if (map == null) return;
            Pawn best = null; float bestCond = 60f;
            var owned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < owned.Count; i++)
            {
                var p = owned[i];
                var pr = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
                if (pr == null || !IsPlayerOwned(p)) continue;
                if (pr.hypnosisLevel > bestCond) { bestCond = pr.hypnosisLevel; best = p; }
            }
            if (best == null) return;
            Pawn thief = null;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var c = pawns[i];
                if (c.Dead || c.Downed || c.RaceProps == null || !c.RaceProps.Humanlike) continue;
                if (c.Faction != null && c.Faction.IsPlayer) continue;
                if (Evilness(c) > 0.55f || IsSadist(c)) { thief = c; break; }
            }
            if (thief == null) return;
            MoveHolokeyToHarasser(best, thief); // the collector pockets the key
            MarkAiControlled(best, thief);
            if (InvolvesPlayerPawn(thief, best))
                Messages.Message("A collector has taken an interest in " + best.LabelShort + " and pocketed their key.",
                    new LookTargets(new[] { best, thief }), MessageTypeDefOf.ThreatBig, false);
        }
    }
}
