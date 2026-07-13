using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    public static class RJWSH_DebugActions
    {
        private const string Cat = "RJW Sexual Harassment";

        [DebugAction(Cat, "Cycle face tattoo on selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CycleFaceTattoo() => CycleTattoo(TattooType.Face);

        [DebugAction(Cat, "Cycle body tattoo on selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CycleBodyTattoo() => CycleTattoo(TattooType.Body);

        private static int _faceTatIdx, _bodyTatIdx;
        private static void CycleTattoo(TattooType type)
        {
            if (!ModsConfig.IdeologyActive) { Messages.Message("Tattoos need the Ideology DLC.", MessageTypeDefOf.RejectInput, false); return; }
            var pool = DefDatabase<TattooDef>.AllDefsListForReading
                .Where(t => t.tattooType == type && !t.noGraphic && t.defName.StartsWith("RJWSH_"))
                .OrderBy(t => t.defName).ToList();
            if (pool.Count == 0) { Messages.Message("No RJWSH " + type + " tattoos loaded.", MessageTypeDefOf.RejectInput, false); return; }
            int idx = (type == TattooType.Face ? _faceTatIdx++ : _bodyTatIdx++) % pool.Count;
            var def = pool[idx];
            int n = 0;
            foreach (var p in Find.Selector.SelectedPawns)
            {
                if (p?.style == null) continue;
                if (type == TattooType.Face) p.style.FaceTattoo = def; else p.style.BodyTattoo = def;
                p.style.Notify_StyleItemChanged();
                n++;
            }
            Messages.Message("Applied " + def.defName + " to " + n + " pawn(s). Select a bare-headed pawn facing down.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Harass: selected -> nearest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void HarassNearest()
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select a harasser pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser);
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + " (check the who-harasses-whom matrix and distance).", MessageTypeDefOf.RejectInput, false); return; }
            HarassmentEngine.RunHarassment(harasser, target);
        }

        private static void ForceApproach(ApproachType type)
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select a harasser pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser);
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + ".", MessageTypeDefOf.RejectInput, false); return; }
            HarassmentEngine.RunHarassmentApproach(harasser, target, type);
        }

        [DebugAction(Cat, "Approach: Catcall (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachCatcall() => ForceApproach(ApproachType.Catcall);

        [DebugAction(Cat, "Approach: Proposition (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachProposition() => ForceApproach(ApproachType.Proposition);

        [DebugAction(Cat, "Approach: Flirt (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachFlirt() => ForceApproach(ApproachType.Flirt);

        [DebugAction(Cat, "Approach: Fan drink (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachFan() => ForceApproach(ApproachType.SpikedDrink);

        [DebugAction(Cat, "Approach: Hypnosis (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachHypnosis() => ForceApproach(ApproachType.Hypnosis);

        [DebugAction(Cat, "Approach: Blackmail (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachBlackmail() => ForceApproach(ApproachType.Blackmail);

        [DebugAction(Cat, "Approach: Devious device (selected -> nearest)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApproachDevious() => ForceApproach(ApproachType.DeviousDevice);

        [DebugAction(Cat, "Spawn scandalous photo of selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnPhoto()
        {
            var p = Find.Selector.SelectedPawns.FirstOrDefault();
            if (p == null) { Messages.Message("Select a pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var photo = ThingMaker.MakeThing(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
            var comp = photo.TryGetComp<CompScandalousPhoto>();
            if (comp != null)
            {
                comp.subject = p;
                // Exercise the real generator: random act + a nearby partner + random rape flag + this room.
                var partner = p.Map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(q => q != p && q.RaceProps != null && q.RaceProps.Humanlike);
                var types = System.Enum.GetValues(typeof(rjw.xxx.rjwSextype));
                var st = (rjw.xxx.rjwSextype)types.GetValue(Rand.RangeInclusive(1, types.Length - 1));
                string where = null;
                try { var room = p.GetRoom(); where = room == null ? null : (room.PsychologicallyOutdoors ? "open" : room.Role?.label); if (where == "none") where = null; } catch { }
                comp.loreDesc = HarassmentEngine.BuildPhotoLore(p, partner, st, Rand.Bool, where);
            }
            GenPlace.TryPlaceThing(photo, p.Position, p.Map, ThingPlaceMode.Near);
            Messages.Message("Spawned a scandalous photo of " + p.LabelShort + ".", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Nemesis: selected returns as a raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NemesisReturn()
        {
            var p = Find.Selector.SelectedPawns.FirstOrDefault();
            if (p == null) { Messages.Message("Select a pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var map = p.MapHeld ?? Find.CurrentMap;
            var faction = HarassmentEngine.PickRaidFaction(p);
            if (faction == null || map == null) { Messages.Message("No hostile faction / map available.", MessageTypeDefOf.RejectInput, false); return; }
            float pts = System.Math.Max(StorytellerUtility.DefaultThreatPointsNow(map), 300f);
            HarassmentEngine.SpawnAssaultRaid(map, faction, pts, p, p.LabelShortCap + " returns",
                p.LabelShortCap + " has come back to the colony that collared them, at the head of a raid.");
        }

        [DebugAction(Cat, "Lock control collar on selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LockCollar()
        {
            foreach (var p in Find.Selector.SelectedPawns)
                HarassmentEngine.LockControlCollar(p, NearestOtherColonist(p));
        }

        [DebugAction(Cat, "Calibrate control collar (selected)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CalibrateCollar()
        {
            var p = Find.Selector.SelectedPawns.FirstOrDefault(x => HarassmentEngine.WearingControlCollar(x));
            if (p == null) { Messages.Message("Select a pawn wearing the control collar first.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new Dialog_CollarCalibrate(p));
        }

        [DebugAction(Cat, "AI-control collar on selected (nearest colonist owns it)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AiControlCollar()
        {
            foreach (var p in Find.Selector.SelectedPawns)
            {
                var captor = NearestOtherColonist(p);
                HarassmentEngine.LockControlCollar(p, captor);
                if (captor != null) HarassmentEngine.MarkAiControlled(p, captor);
            }
        }

        [DebugAction(Cat, "Apply device to selected (random)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApplyDeviceRandom()
        {
            var p = Find.Selector.SelectedPawns.FirstOrDefault();
            if (p == null) { Messages.Message("Select a pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var captor = NearestOtherColonist(p);
            var def = HarassmentEngine.LockRJWDevice(p, captor);
            if (def != null)
                Messages.Message("Locked " + def.label + " onto " + p.LabelShort
                    + (captor != null ? " (key -> " + captor.LabelShort + ")" : " (key dropped)") + ".",
                    new LookTargets(p), MessageTypeDefOf.TaskCompletion, false);
            else
                Messages.Message("No applicable device for " + p.LabelShort + " (none fit, or all conflict with the collar).",
                    MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Cat, "Apply device to selected (choose)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ApplyDeviceChoose()
        {
            var p = Find.Selector.SelectedPawns.FirstOrDefault();
            if (p == null) { Messages.Message("Select a pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var captor = NearestOtherColonist(p);
            var options = new List<DebugMenuOption>();
            foreach (var def in HarassmentEngine.AllLockableDevices().OrderBy(d => d.label))
            {
                var local = def;
                options.Add(new DebugMenuOption(local.LabelCap, DebugMenuOptionMode.Action, delegate
                {
                    var applied = HarassmentEngine.ApplyAndLockDevice(p, local, captor);
                    if (applied == null)
                        Messages.Message("Could not apply " + local.label + " (conflicts with locked gear, or no body part).",
                            MessageTypeDefOf.RejectInput, false);
                    else
                        Messages.Message("Locked " + local.label + " onto " + p.LabelShort + ".",
                            new LookTargets(p), MessageTypeDefOf.TaskCompletion, false);
                }));
            }
            if (options.Count == 0) { Messages.Message("No lockable devices found in the loaded mods.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        private static Pawn NearestOtherColonist(Pawn p)
        {
            return p.Map?.mapPawns?.FreeColonistsSpawned?
                .Where(c => c != p)
                .OrderBy(c => c.Position.DistanceToSquared(p.Position))
                .FirstOrDefault();
        }

        [DebugAction(Cat, "Bound in public: selected drags -> nearest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceBoundInPublic()
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select the dragger pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser) ?? Find.Selector.SelectedPawns.Skip(1).FirstOrDefault();
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + ".", MessageTypeDefOf.RejectInput, false); return; }
            if (!HarassmentEngine.DoBoundInPublic(harasser, target))
                Messages.Message("Bound-in-public could not start (no reachable public cell?).", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Cat, "Onahole: drag selected -> nearest into public onahole", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceOnahole()
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select the dragger pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser) ?? Find.Selector.SelectedPawns.Skip(1).FirstOrDefault();
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + ".", MessageTypeDefOf.RejectInput, false); return; }
            if (!HarassmentEngine.DoOnaholeCapture(harasser, target))
                Messages.Message("Onahole capture could not start (Onahole Extension installed? valid public cell?).", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Cat, "Set selected fully conditioned (hypnosis 80)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SetConditioned()
        {
            foreach (var p in Find.Selector.SelectedPawns)
            {
                var prof = GameComponent_Harassment.Instance?.GetProfile(p);
                if (prof != null) prof.hypnosisLevel = 80f;
            }
            Messages.Message("Set conditioned. Select the pawn to see the command gizmo.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Harass: run map scan now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunScanNow()
        {
            HarassmentEngine.TryRunOnMap(Find.CurrentMap);
        }

        [DebugAction(Cat, "Stripping struggle: selected -> nearest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceStruggle()
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select a harasser pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser);
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + ".", MessageTypeDefOf.RejectInput, false); return; }
            HarassmentEngine.BeginPhysical(harasser, target, HarassmentEngine.InvolvesPlayerPawn(harasser, target));
        }

        [DebugAction(Cat, "Forced act: selected -> nearest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceForced()
        {
            var harasser = Find.Selector.SelectedPawns.FirstOrDefault();
            if (harasser == null) { Messages.Message("Select a harasser pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            var target = HarassmentEngine.FindTarget(harasser);
            if (target == null) { Messages.Message("No valid target near " + harasser.LabelShort + ".", MessageTypeDefOf.RejectInput, false); return; }
            HarassmentEngine.ForceForcedAct(harasser, target);
        }

        [DebugAction(Cat, "Toggle: struggle always fails (test)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleForceLose()
        {
            HarassmentEngine.DebugForceLoseStruggle = !HarassmentEngine.DebugForceLoseStruggle;
            Messages.Message("Struggle always fails: " + HarassmentEngine.DebugForceLoseStruggle,
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Log: profile of selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogProfile()
        {
            foreach (var p in Find.Selector.SelectedPawns)
            {
                var prof = GameComponent_Harassment.Instance?.GetProfile(p);
                if (prof == null) continue;
                Log.Message($"[RJWSH] {p.LabelShort}: morality={prof.morality}, confidence={prof.confidence:F0}, impression={prof.impression:F0}, category={HarassmentEngine.Categorize(p)}");
            }
        }
    }
}
