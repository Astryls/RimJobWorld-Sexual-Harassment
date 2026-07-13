using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Phase 5 world layer: a scandalous photo in circulation spreads word of the colony's depravity.
    /// Notoriety swings up and the subject takes the humiliation mood hit (a thrill for masochists). Driven on an
    /// MTB from GameComponent_Harassment while any photo with a live subject is in circulation.</summary>
    public class IncidentWorker_ScandalLeak : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            var gc = GameComponent_Harassment.Instance;
            return gc?.circulatingPhotos != null && gc.circulatingPhotos.Any(cp => cp?.subject != null);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var gc = GameComponent_Harassment.Instance;
            if (gc?.circulatingPhotos == null) return false;
            var pick = gc.circulatingPhotos.Where(cp => cp?.subject != null).RandomElementWithFallback();
            var subject = pick?.subject;
            if (subject == null) return false;

            int swing = Rand.Range(3, 9);
            gc.AddNotoriety(swing, "rjwsh_scandal_photo");
            if (!subject.Dead)
            {
                HarassmentEngine.TryAddMoodThought(subject, "RJWSH_Humiliated");
                // The subject's own name spreads with the photos - personal infamy.
                ReputationBridge.AddReputation(subject, -12f, "rjwsh_scandal_photo");
                TaleHelper.Record("RJWSH_Tale_ScandalPhoto", subject);
                HarassmentEngine.Chronicle(subject, "Scandalous photos of " + subject.LabelShortCap + " spread through the region.", 3);
            }

            string text = "Word of " + subject.LabelShortCap + "'s circulating photos has spread through the region. "
                + "The colony's reputation for depravity grows (notoriety +" + swing + ").";
            Find.LetterStack.ReceiveLetter("Scandal spreads", text, LetterDefOf.NeutralEvent, new LookTargets(subject));
            return true;
        }
    }

    /// <summary>Phase 5: a rival slaver offers a premium for the colony's best-conditioned pet. A radio-mode choice
    /// letter - accept to sell them off (silver dropped, pet leaves the map, owner grieves a devoted loss) or decline.</summary>
    public class IncidentWorker_RivalSlaver : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => PickTarget(parms.target as Map) != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var pet = PickTarget(parms.target as Map);
            if (pet == null) return false;
            var ldef = DefDatabase<LetterDef>.GetNamedSilentFail("RJWSH_SlaverOffer");
            if (ldef == null) return false;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            int offer = 500 + (int)((prof?.hypnosisLevel ?? 0f) * 8f);
            if (prof?.sex != null) offer += (int)(prof.sex.sexAddiction * 2f + (100f - prof.sex.willpower));
            try { offer += (int)(pet.GetStatValue(StatDefOf.PawnBeauty) * 90f); } catch { }
            if (offer < 400) offer = 400;

            string text = "A slaver has heard of " + pet.LabelShortCap + ", one of the colony's best-conditioned pets, and "
                + "offers " + offer + " silver to take them off your hands. A broken, obedient pet fetches a fine price out in the wild.";
            var letter = (ChoiceLetter_RivalSlaver)LetterMaker.MakeLetter("Slaver's offer", text, ldef);
            letter.pet = pet;
            letter.offer = offer;
            letter.radioMode = true;
            letter.StartTimeout(60000);
            Find.LetterStack.ReceiveLetter(letter);
            return true;
        }

        private static Pawn PickTarget(Map map)
        {
            if (map == null) return null;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return null;
            Pawn best = null; float bestH = 69f;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed || !p.RaceProps.Humanlike) continue;
                if (!HarassmentEngine.IsPlayerOwned(p)) continue;
                var prof = gc.GetProfileIfExists(p);
                if (prof == null || (prof.ownerId < 0 && prof.relationshipOwnerId < 0)) continue;
                if (prof.hypnosisLevel > bestH) { bestH = prof.hypnosisLevel; best = p; }
            }
            return best;
        }
    }

    /// <summary>Radio-mode accept/decline letter for the rival slaver's buy-out offer.</summary>
    public class ChoiceLetter_RivalSlaver : ChoiceLetter
    {
        public Pawn pet;
        public int offer;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly) { yield return Option_Close; yield break; }
                var accept = new DiaOption("Accept".Translate());
                accept.action = delegate
                {
                    HarassmentEngine.DepthSellPet(HarassmentEngine.FindKeyHolderFor(pet), pet, offer);
                    GameComponent_Harassment.Instance?.AddNotoriety(4, "rjwsh_pet_sold");
                    Find.LetterStack.RemoveLetter(this);
                };
                accept.resolveTree = true;
                if (pet == null || !pet.Spawned || pet.Dead) accept.Disable("(the pet is no longer available)");
                yield return accept;
                yield return Option_Reject;
                yield return Option_Postpone;
            }
        }

        public override bool CanShowInLetterStack => base.CanShowInLetterStack && pet != null && pet.Spawned && !pet.Dead;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pet, "pet");
            Scribe_Values.Look(ref offer, "offer", 0);
        }
    }

    /// <summary>Phase 5: outsiders raid to free a specific collared/conditioned pet. If a raider reaches the pet
    /// (HarassmentEngine.RescueRaidTick) they strip the collar and take them; hold them off to keep your pet.</summary>
    public class IncidentWorker_RescueRaid : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            var map = parms.target as Map;
            return map != null && PickRescueTarget(map) != null && RescuerFaction() != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = parms.target as Map;
            if (map == null) return false;
            var pet = PickRescueTarget(map);
            var faction = RescuerFaction();
            if (pet == null || faction == null) return false;

            var raidParms = new IncidentParms
            {
                target = map,
                points = UnityEngine.Mathf.Max(StorytellerUtility.DefaultThreatPointsNow(map), 250f),
                faction = faction,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                forced = true,
            };
            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms)) return false;

            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            if (prof != null) prof.rescueRaidUntilTick = Find.TickManager.TicksGame + 40000;

            string text = faction.NameColored + " have come to free " + pet.LabelShortCap + " from the collar. "
                + "Keep the raiders away from " + pet.LabelShort + ", or they will strip the collar and take your pet.";
            Find.LetterStack.ReceiveLetter("Rescue raid", text, LetterDefOf.ThreatBig, new LookTargets(pet), faction);
            return true;
        }

        private static Pawn PickRescueTarget(Map map)
        {
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return null;
            var candidates = new List<Pawn>();
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.RaceProps.Humanlike) continue;
                if (!HarassmentEngine.IsPlayerOwned(p)) continue;
                var prof = gc.GetProfileIfExists(p);
                if (prof == null || prof.rescueRaidUntilTick > 0) continue;
                if (prof.ownerId < 0 && prof.relationshipOwnerId < 0) continue;
                if (prof.IsConditioned || HarassmentEngine.IsCollared(p)) candidates.Add(p);
            }
            return candidates.RandomElementWithFallback();
        }

        private static Faction RescuerFaction()
        {
            Faction best = null;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.IsPlayer || f.defeated) continue;
                if (f.def == null || !f.def.humanlikeFaction || f.def.pawnGroupMakers.NullOrEmpty()) continue;
                if (!f.HostileTo(Faction.OfPlayer)) continue;
                if (best == null || Rand.Bool) best = f;
            }
            return best;
        }
    }
}
