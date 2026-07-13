using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Nemesis arc (#4): the world remembers pets who slipped the collar. A rescued or sold-off pet is
    /// recorded, and weeks later they can return - leading a raid, hostile, wearing the memory of who
    /// collared them. Personalized: the letter names them and their former owner.
    /// </summary>
    public static partial class HarassmentEngine
    {
        // ── Registration ─────────────────────────────────────────────────────
        /// <summary>Remember an escaped/sold pet as a potential future nemesis. Grudge scales inversely with
        /// how broken they were (a willful escapee hates you; a devoted sold-off pet less so).</summary>
        public static void RegisterNemesis(Pawn pet, int formerOwnerId, bool sold)
        {
            if (pet == null || pet.Dead) return;
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return;
            if (gc.nemeses == null) gc.nemeses = new List<Nemesis>();
            if (gc.nemeses.Exists(n => n != null && n.pawn == pet)) return;

            var prof = gc.GetProfileIfExists(pet);
            int grudge = Mathf.Clamp(100 - (int)(prof?.hypnosisLevel ?? 0f), 10, 100);
            if (sold) grudge = Mathf.RoundToInt(grudge * 0.6f);

            var owner = FindPawnByIdAnyMap(formerOwnerId);
            gc.nemeses.Add(new Nemesis
            {
                pawn = pet,
                formerOwnerId = formerOwnerId,
                formerOwnerName = owner?.LabelShortCap ?? "their former owner",
                escapeTick = Find.TickManager.TicksGame,
                grudge = grudge
            });
        }

        // ── Activation (driven on an MTB from GameComponent) ─────────────────
        /// <summary>Rolls whether a remembered nemesis returns to strike. Only fires for a nemesis whose
        /// escape is at least ~5 days old; higher grudge shortens the mean time between returns.</summary>
        public static void NemesisTick()
        {
            var gc = GameComponent_Harassment.Instance;
            if (gc?.nemeses == null || gc.nemeses.Count == 0) return;
            int now = Find.TickManager.TicksGame;

            // Prune dead/lost references.
            gc.nemeses.RemoveAll(n => n == null || n.returned || n.pawn == null || n.pawn.Dead || n.pawn.Destroyed);

            for (int i = 0; i < gc.nemeses.Count; i++)
            {
                var n = gc.nemeses[i];
                if (now - n.escapeTick < 300000) continue;          // ~5-day cooldown before they can return
                float mtb = Mathf.Lerp(24f, 6f, n.grudge / 100f);   // grudge 10 -> ~22 days, 100 -> ~6 days
                if (!Rand.MTBEventOccurs(mtb, 60000f, 60000f)) continue;
                if (ActivateNemesis(n)) { n.returned = true; break; } // one return per tick
            }
        }

        private static bool ActivateNemesis(Nemesis n)
        {
            var map = Find.RandomPlayerHomeMap;
            if (map == null) return false;
            var faction = PickRaidFaction(n.pawn);
            if (faction == null) return false;

            float points = Mathf.Max(StorytellerUtility.DefaultThreatPointsNow(map) * (0.5f + n.grudge / 200f), 250f);
            var owner = FindPawnByIdAnyMap(n.formerOwnerId);
            string ownerBit = owner != null && !owner.Dead ? owner.LabelShortCap : n.formerOwnerName;
            string label = n.pawn.LabelShortCap + " returns";
            string text = n.pawn.LabelShortCap + " has come back to the colony that once put a collar on them - "
                + "this time at the head of a raid, and they have not forgotten " + ownerBit + ".";

            bool ok = SpawnAssaultRaid(map, faction, points, n.pawn, label, text);
            if (ok)
            {
                Chronicle(n.pawn, "Returned at the head of a raid against " + ownerBit + ".", 1);
                if (owner != null && !owner.Dead) Chronicle(owner, n.pawn.LabelShortCap + " came back for revenge.", 1);
            }
            return ok;
        }

        // ── Shared assault-raid spawner (nemesis + blackmail punitive) ───────
        /// <summary>Spawns a hostile assault: a faction combat group at a map edge, optionally led by a
        /// specific ringleader pawn (re-spawned + re-factioned), under one AssaultColony lord + a letter.</summary>
        public static bool SpawnAssaultRaid(Map map, Faction faction, float points, Pawn ringleader, string label, string text)
        {
            if (map == null || faction == null) return false;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var entry, map, CellFinder.EdgeRoadChance_Hostile)) return false;

            var pawns = new List<Pawn>();
            if (faction.def?.pawnGroupMakers != null && faction.def.pawnGroupMakers.Count > 0)
            {
                try
                {
                    var parms = new PawnGroupMakerParms
                    {
                        groupKind = PawnGroupKindDefOf.Combat,
                        tile = map.Tile,
                        faction = faction,
                        points = points,
                    };
                    pawns.AddRange(PawnGroupMakerUtility.GeneratePawns(parms));
                }
                catch { }
            }
            foreach (var p in pawns)
            {
                var cell = CellFinder.RandomClosewalkCellNear(entry, map, 8);
                try { GenSpawn.Spawn(p, cell, map); } catch { }
            }

            if (ringleader != null && !ringleader.Dead && !ringleader.Destroyed)
            {
                try { ringleader.SetFaction(faction); } catch { }
                if (!ringleader.Spawned)
                { try { GenSpawn.Spawn(ringleader, entry, map); } catch { } }
                if (ringleader.Spawned) pawns.Add(ringleader);
            }

            if (pawns.Count == 0) return false;
            try { LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, pawns); }
            catch { return false; }

            try { Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, new LookTargets(pawns[0]), faction); }
            catch { }
            return true;
        }

        /// <summary>A hostile humanlike faction to raid under. Prefers the pawn's current faction if it is
        /// already hostile, then any hostile faction, then forces a random neutral one hostile.</summary>
        public static Faction PickRaidFaction(Pawn preferred = null)
        {
            var player = Faction.OfPlayer;
            if (preferred?.Faction != null && !preferred.Faction.IsPlayer && preferred.Faction.HostileTo(player)
                && preferred.Faction.def.pawnGroupMakers?.Count > 0)
                return preferred.Faction;

            Faction hostile = null, neutral = null;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.IsPlayer || f.defeated || f.Hidden || f.temporary) continue;
                if (f.def == null || !f.def.humanlikeFaction || (f.def.pawnGroupMakers?.Count ?? 0) == 0) continue;
                if (f.HostileTo(player)) { if (hostile == null || Rand.Bool) hostile = f; }
                else if (neutral == null || Rand.Bool) neutral = f;
            }
            if (hostile != null) return hostile;
            if (neutral != null)
            {
                try { neutral.TryAffectGoodwillWith(player, -200, false, false, null, null); } catch { }
                return neutral;
            }
            return null;
        }
    }

    /// <summary>A remembered escaped/sold pet who may return as a hostile raid leader.</summary>
    public class Nemesis : IExposable
    {
        public Pawn pawn;
        public int formerOwnerId = -1;
        public string formerOwnerName;
        public int escapeTick;
        public int grudge;
        public bool returned;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref formerOwnerId, "formerOwnerId", -1);
            Scribe_Values.Look(ref formerOwnerName, "formerOwnerName");
            Scribe_Values.Look(ref escapeTick, "escapeTick", 0);
            Scribe_Values.Look(ref grudge, "grudge", 50);
            Scribe_Values.Look(ref returned, "returned", false);
        }
    }
}
