using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    [StaticConstructorOnStartup]
    public static class HarassmentTextures
    {
        public static readonly Texture2D Command =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Command", true);

        public static readonly Texture2D Shock =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Shock", true);

        public static readonly Texture2D ShockDown =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_ShockDown", true);

        public static readonly Texture2D ShockDead =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_ShockDead", true);

        public static readonly Texture2D Follow =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Follow", true);

        public static readonly Texture2D AutoService =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_AutoService", true);

        public static readonly Texture2D Unbind =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Unbind", true);

        public static readonly Texture2D BurnPhoto =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_BurnPhoto", true);

        public static readonly Texture2D Discipline =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Discipline", true);

        public static readonly Texture2D Reward =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Reward", true);

        public static readonly Texture2D Summon =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Summon", true);

        public static readonly Texture2D Stay =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Stay", true);

        public static readonly Texture2D HandKey =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_HandKey", true);

        public static readonly Texture2D Free =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_Free", true);

        public static readonly Texture2D KeepNaked =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_KeepNaked", true);

        public static readonly Texture2D FightBack =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_FightBack", true);

        public static readonly Texture2D DressUp =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_DressUp", true);
        public static readonly Texture2D HideControls =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_HideControls", true);
        public static readonly Texture2D ShowControls =
            ContentFinder<Texture2D>.Get("UI/Commands/RJWSH_ShowControls", true);
        public static readonly Texture2D Paw =
            ContentFinder<Texture2D>.Get("UI/RJWSH_Paw", true);
        public static readonly Texture2D GoTo =
            ContentFinder<Texture2D>.Get("UI/RJWSH_GoTo", true);
        public static readonly Texture2D CollarIcon =
            ContentFinder<Texture2D>.Get("UI/RJWSH_Collar", true);
        public static readonly Texture2D GraphToggle =
            ContentFinder<Texture2D>.Get("UI/RJWSH_Graph", true);

        public static readonly Texture2D Moon =
            ContentFinder<Texture2D>.Get("UI/RJWSH_Moon", true);

        public static readonly Texture2D Search =
            ContentFinder<Texture2D>.Get("UI/RJWSH_Search", true);

        public static readonly Texture2D SchedFree = ContentFinder<Texture2D>.Get("UI/RJWSH_SchedFree", true);
        public static readonly Texture2D SchedServe = ContentFinder<Texture2D>.Get("UI/RJWSH_SchedServe", true);
        public static readonly Texture2D SchedTrain = ContentFinder<Texture2D>.Get("UI/RJWSH_SchedTrain", true);
        public static readonly Texture2D SchedRest = ContentFinder<Texture2D>.Get("UI/RJWSH_SchedRest", true);
        public static readonly Texture2D SchedConfined = ContentFinder<Texture2D>.Get("UI/RJWSH_SchedConfined", true);
        public static readonly Texture2D Focus = ContentFinder<Texture2D>.Get("UI/RJWSH_Focus", true);
        public static readonly Texture2D Needs = ContentFinder<Texture2D>.Get("UI/RJWSH_Needs", true);
        public static readonly Texture2D Presets = ContentFinder<Texture2D>.Get("UI/RJWSH_Presets", true);
        public static readonly Texture2D Parade = ContentFinder<Texture2D>.Get("UI/RJWSH_Parade", true);
        public static readonly Texture2D Star = ContentFinder<Texture2D>.Get("UI/RJWSH_Star", true);
        public static readonly Texture2D CondIcon = ContentFinder<Texture2D>.Get("UI/RJWSH_CondIcon", true);
        public static readonly Texture2D RapportIcon = ContentFinder<Texture2D>.Get("UI/RJWSH_RapportIcon", true);
        public static readonly Texture2D Sort = ContentFinder<Texture2D>.Get("UI/RJWSH_Sort", true);
        public static readonly Texture2D Pin = ContentFinder<Texture2D>.Get("UI/RJWSH_Pin", true);
        // Concept 3 stage: hotspot ring marker + equipment-slot icons.
        public static readonly Texture2D Node = ContentFinder<Texture2D>.Get("UI/RJWSH_Node", true);
        public static readonly Texture2D Ring = ContentFinder<Texture2D>.Get("UI/RJWSH_Ring", true);
        public static readonly Texture2D Outfit = ContentFinder<Texture2D>.Get("UI/RJWSH_Outfit", true);
        public static readonly Texture2D Restraints = ContentFinder<Texture2D>.Get("UI/RJWSH_Restraints", true);
        public static readonly Texture2D Lock = ContentFinder<Texture2D>.Get("UI/RJWSH_Lock", true);
        public static readonly Texture2D Tailor = ContentFinder<Texture2D>.Get("UI/RJWSH_Tailor", true);
        public static readonly Texture2D Photos = ContentFinder<Texture2D>.Get("UI/RJWSH_Photos", true);
        public static readonly Texture2D Stylist = ContentFinder<Texture2D>.Get("UI/RJWSH_Stylist", true);
    }

    // NOTE: the dark resize grip used to live here as a Postfix on Verse.Window.WindowOnGUI - a method the
    // game invokes for EVERY window on the stack on EVERY IMGUI event, so every other mod's windows paid the
    // Harmony stub just so this one window could draw three lines. WindowOnGUI is public virtual, so the grip
    // is now drawn by Window_Harem's own override instead. Do not reintroduce a global Window patch for UI chrome.

    [StaticConstructorOnStartup]
    public static class RJWSH_HarmonyInit
    {
        static RJWSH_HarmonyInit()
        {
            try
            {
                var h = new Harmony("astryl.RJWSexualHarassment");
                h.PatchAll();
                TryPatchOnaholeUnbind(h);
                TryPatchTRPGDrops(h);
                TryPatchBondagePreview(h);
                TryPatchBondageBedRelease(h);
                TryPatchRmbOpinion(h);
                TryPatchRomanceSuppress(h);
            }
            catch (Exception e)
            {
                Log.Error("[RJW Sexual Harassment] Harmony PatchAll failed: " + e);
            }
        }

        // Gate the onahole's own unbind behind our key: only a pawn carrying the matching Holokey can free a
        // victim we locked in. Reflection-patched because the Onahole assembly is a soft dep (not referenced).
        private static void TryPatchOnaholeUnbind(Harmony h)
        {
            try
            {
                var bedType = GenTypes.GetTypeInAnyAssembly("RJW_Onahole.Building_OnaholeBed");
                if (bedType == null) return;
                var target = AccessTools.Method(bedType, "GetFloatMenuOptions", new[] { typeof(Pawn) });
                if (target == null) return;
                var post = typeof(Patch_Onahole_Unbind).GetMethod("Postfix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                h.Patch(target, postfix: new HarmonyMethod(post));
                Log.Message("[RJW Sexual Harassment] onahole unbind is now key-gated.");
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] onahole unbind patch failed: " + e.Message); }
        }

        // A control-collared pawn is fully controlled, so RJW's right-click sex menu should never block the act
        // for "low opinion" / "unappealing" reasons - patch RMB_Sex.DoChecks to accept when a collared pawn is involved.
        private static void TryPatchRmbOpinion(Harmony h)
        {
            try
            {
                var t = GenTypes.GetTypeInAnyAssembly("rjw.RMB.RMB_Sex");
                if (t == null) return;
                var m = AccessTools.Method(t, "DoChecks");
                if (m == null) return;
                var post = typeof(Patch_RMB_Sex_DoChecks).GetMethod("Postfix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (post != null) h.Patch(m, postfix: new HarmonyMethod(post));
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] RMB opinion patch failed: " + e.Message); }
        }

        // Suppress the right-click "Romance" options between an owner and their pet (the power dynamic precludes
        // courtship). Patches both the vanilla romance provider and RJW's RMB romance/marriage entries.
        private static void TryPatchRomanceSuppress(Harmony h)
        {
            try
            {
                // Specify the parameter types: FloatMenuOptionProvider has multiple GetSingleOptionFor overloads,
                // so a name-only lookup throws "Ambiguous match".
                var m = AccessTools.Method(typeof(FloatMenuOptionProvider_Romance), "GetSingleOptionFor",
                    new[] { typeof(Pawn), typeof(FloatMenuContext) });
                var post = typeof(Patch_VanillaRomanceSuppress).GetMethod("Postfix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (m != null && post != null) h.Patch(m, postfix: new HarmonyMethod(post));
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] vanilla romance suppress patch failed: " + e.Message); }

            try
            {
                var t = GenTypes.GetTypeInAnyAssembly("rjw.RMB_Socialize");
                if (t != null)
                {
                    var m = AccessTools.Method(t, "GenerateSocialOptions");
                    var post = typeof(Patch_RjwRomanceSuppress).GetMethod("Postfix",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (m != null && post != null) h.Patch(m, postfix: new HarmonyMethod(post));
                }
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] RJW romance suppress patch failed: " + e.Message); }
        }

        // Key-gate BondageBed Torture's release jobs: a locked prisoner (wearing a HoloCrypto device/collar) can
        // only be freed from the bed/chains by a pawn carrying the matching Holokey - so locking our gear on them
        // before strapping them in puts the release under our key system.
        private static void TryPatchBondageBedRelease(Harmony h)
        {
            try
            {
                var pre = typeof(Patch_BondageBedRelease).GetMethod("Prefix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (pre == null) return;
                bool any = false;
                foreach (var typeName in new[] { "SR.DA.Job.JobDriver_ReleaseBondageBed", "SR.DA.Job.JobDriver_ReleaseBondageChains" })
                {
                    var t = GenTypes.GetTypeInAnyAssembly(typeName);
                    if (t == null) continue;
                    var m = AccessTools.Method(t, "MakeNewToils");
                    if (m != null) { h.Patch(m, prefix: new HarmonyMethod(pre)); any = true; }
                }
                if (any) Log.Message("[RJW Sexual Harassment] BondageBed release is now key-gated for locked prisoners.");
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] bondage bed release patch failed: " + e.Message); }
        }

        // While the dress-up window previews gear, suppress RJW's on_wear so it does not auto-lock the device,
        // spawn a Holokey, or apply the bound hediff - the preview pieces must stay unlocked and removable.
        private static void TryPatchBondagePreview(Harmony h)
        {
            try
            {
                var soul = GenTypes.GetTypeInAnyAssembly("rjw.bondage_gear_soul");
                if (soul == null) return;
                var target = AccessTools.Method(soul, "on_wear");
                if (target == null) return;
                var pre = typeof(Patch_BondageOnWear_Preview).GetMethod("Prefix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                h.Patch(target, prefix: new HarmonyMethod(pre));
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] bondage preview patch failed: " + e.Message); }
        }

        // True RPG Inventory drops items via GearCommands (not the vanilla gear tab), so block those too.
        private static void TryPatchTRPGDrops(Harmony h)
        {
            try
            {
                var gc = GenTypes.GetTypeInAnyAssembly("TrueRPGInventory.GearCommands");
                if (gc == null) return;
                var pre = typeof(Patch_TRPG_Drop).GetMethod("Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                // Include UnequipToInventory: it removes worn apparel via apparel.Remove(), which RJW's
                // TryDrop lock patch does NOT cover, so a locked collar could be taken off without the key.
                foreach (var name in new[] { "DropAtFeet", "DropNearby", "DropAt", "UnequipToInventory" })
                {
                    var m = AccessTools.Method(gc, name);
                    if (m != null) h.Patch(m, prefix: new HarmonyMethod(pre));
                }
            }
            catch (Exception e) { Log.Warning("[RJW Sexual Harassment] True RPG Inventory drop patch failed: " + e.Message); }
        }
    }

    /// <summary>A control-collared pawn is fully controlled: RJW's right-click sex menu skips the opinion/
    /// attraction "willingness" gate for them (it already does this for hero-mode pawns).</summary>
    public static class Patch_RMB_Sex_DoChecks
    {
        public static void Postfix(ref AcceptanceReport __result, Pawn pawn, Pawn target)
        {
            try
            {
                if (__result.Accepted) return;
                if (HarassmentEngine.WearingControlCollar(target) || HarassmentEngine.WearingControlCollar(pawn))
                    __result = true; // forced -> opinion/attraction never blocks
            }
            catch { }
        }
    }

    /// <summary>Key-gates BondageBed Torture release jobs: refuses to free a locked prisoner without the key.</summary>
    public static class Patch_BondageBedRelease
    {
        public static bool Prefix(ref IEnumerable<Verse.AI.Toil> __result, Verse.AI.JobDriver __instance)
        {
            try
            {
                var prisoner = __instance?.job?.GetTarget(Verse.AI.TargetIndex.B).Thing as Pawn;
                if (prisoner == null) return true;
                if (HarassmentEngine.IsLockedPawn(prisoner) && !HarassmentEngine.HoldsKeyForLockedPawn(__instance.pawn, prisoner))
                {
                    if (HarassmentEngine.InvolvesPlayerPawn(__instance.pawn, prisoner))
                        Messages.Message(__instance.pawn.LabelShort + " needs " + prisoner.LabelShort + "'s key to free them from the restraints.",
                            new LookTargets(prisoner), MessageTypeDefOf.RejectInput, false);
                    __result = new List<Verse.AI.Toil>();
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    /// <summary>During dress-up preview, skip RJW's bondage on_wear (no lock, no key, no hediff).</summary>
    public static class Patch_BondageOnWear_Preview
    {
        public static bool Prefix() => !Dialog_DressUp.Previewing;
    }

    /// <summary>An evil key-holder refuses to drop the key via the vanilla gear tab.</summary>
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "InterfaceDrop")]
    public static class Patch_GearTab_InterfaceDrop
    {
        private static System.Reflection.PropertyInfo _selPawn;
        static bool Prefix(Thing t, ITab_Pawn_Gear __instance)
        {
            try
            {
                if (_selPawn == null)
                    _selPawn = typeof(ITab_Pawn_Gear).GetProperty("SelPawnForGear",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var pawn = _selPawn?.GetValue(__instance) as Pawn;
                if (pawn != null && HarassmentEngine.IsRefusedKeyDrop(pawn, t))
                {
                    Messages.Message(pawn.LabelShort + " refuses to give up the key.", new LookTargets(pawn), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    /// <summary>Prefix shared by True RPG Inventory's GearCommands drop methods to refuse a controlled key.</summary>
    public static class Patch_TRPG_Drop
    {
        public static bool Prefix(Pawn pawn, Thing t)
        {
            try
            {
                if (pawn != null && HarassmentEngine.IsRefusedKeyDrop(pawn, t))
                {
                    Messages.Message(pawn.LabelShort + " refuses to give up the key.", new LookTargets(pawn), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                // Locked apparel (the control collar, bondage gear) can only come off with the key.
                if (HarassmentEngine.IsLockedWornApparel(pawn, t))
                {
                    Messages.Message(((t as Apparel)?.Label ?? "That") + " is locked and can only be removed with its key.", new LookTargets(pawn), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    /// <summary>Replaces a locked onahole's float-menu options with a disabled "needs the key" entry unless
    /// the selecting pawn carries the matching Holokey.</summary>
    public static class Patch_Onahole_Unbind
    {
        public static void Postfix(Thing __instance, Pawn selectedPawn, ref System.Collections.Generic.IEnumerable<FloatMenuOption> __result)
        {
            try
            {
                var bound = HarassmentEngine.OnaholeBoundPawn(__instance);
                if (bound == null || !HarassmentEngine.WearingLockedHarassmentGear(bound)) return;
                if (HarassmentEngine.HoldsKeyForLockedPawn(selectedPawn, bound)) return;
                __result = new System.Collections.Generic.List<FloatMenuOption>
                {
                    new FloatMenuOption("Locked - " + (selectedPawn != null ? selectedPawn.LabelShort : "someone") + " needs the key", null)
                };
            }
            catch { }
        }
    }

    /// <summary>
    /// Adds the "command hypnotized" gizmo to fully conditioned, player-controlled pawns. Extras are
    /// built in a try/catch and yielded from a list so a throw here can never suppress the whole grid.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        // GetGizmos is called EVERY FRAME for every selected pawn, and building our suite from scratch each
        // time allocated a List plus a stream of Command_Action / Command_Toggle / Command_Target objects -
        // each with its own label concatenation, closure, and (for Command_Target) a fresh TargetingParameters
        // with a validator lambda. With several pawns selected that is dozens of allocations per frame.
        // The built list is therefore memoised per (pawn, Unity frame): gizmos are consumed immediately after
        // construction, so a one-frame cache is safe, and any state change is picked up on the next frame.
        // Keyed on thingIDNumber (never a Pawn reference) so the static cache cannot root a dead Game graph.
        private static readonly Dictionary<int, List<Gizmo>> _cache = new Dictionary<int, List<Gizmo>>(8);
        private static int _cacheFrame = -1;

        private static List<Gizmo> BuildFor(Pawn pawn)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != _cacheFrame) { _cacheFrame = frame; _cache.Clear(); }
            if (_cache.TryGetValue(pawn.thingIDNumber, out var cached)) return cached;

            var list = new List<Gizmo>();
            // When the player has chosen to hide the control gizmos, the whole key-holder suite (and the
            // conditioned readout) is driven from the Pet Dashboard + Control tab instead.
            bool hide = RimJobWorldSexualHarassmentMod.Settings != null && RimJobWorldSexualHarassmentMod.Settings.hideKeyHolderGizmos;
            if (!hide)
            {
                try
                {
                    var extras = HarassmentEngine.BuildKeyHolderGizmos(pawn);
                    if (extras != null) list.AddRange(extras);
                }
                catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] gizmo build failed: " + e.Message, 0x5A1300); }

                try { var cond = HarassmentEngine.BuildConditionedGizmo(pawn); if (cond != null) list.Add(cond); }
                catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] conditioned gizmo failed: " + e.Message, 0x5A1345); }
            }

            try { var fb = HarassmentEngine.BuildFightBackGizmo(pawn); if (fb != null) list.Add(fb); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] fight-back gizmo failed: " + e.Message, 0x5A1346); }

            try { var ar = HarassmentEngine.BuildAutoResistGizmo(pawn); if (ar != null) list.Add(ar); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] auto-resist gizmo failed: " + e.Message, 0x5A1347); }

            try { var ona = HarassmentEngine.BuildOnaholeTimerGizmo(pawn); if (ona != null) list.Add(ona); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] onahole gizmo failed: " + e.Message, 0x5A1348); }

            _cache[pawn.thingIDNumber] = list;
            return list;
        }

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            if (__instance == null) yield break;

            List<Gizmo> ours = null;
            try { ours = BuildFor(__instance); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] gizmo assembly failed: " + e.Message, 0x5A1349); }
            if (ours == null) yield break;
            for (int i = 0; i < ours.Count; i++) yield return ours[i];
        }
    }

    /// <summary>When a pawn leaves the map, check whether it is fleeing with the key to a locked colonist.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_Pawn_ExitMap
    {
        static void Postfix(Pawn __instance)
        {
            try { HarassmentEngine.OnPawnLeftMap(__instance); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] exit-map key check failed: " + e.Message, 0x5A1344); }
        }
    }

    /// <summary>On a fresh spawn (not a load), a pawn may already carry a circulating scandalous photo of
    /// a colony pawn - so blackmail material can arrive with raiders, visitors, and new colonists.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_Pawn_SpawnSetup_Circulation
    {
        static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            try { HarassmentEngine.TrySpawnCirculationPhoto(__instance); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] circulation photo failed: " + e.Message, 0x5A1340); }
        }
    }

    /// <summary>The Submission need appears only on tracked conditioned/owned pets (and only when enabled).
    /// ShouldHaveNeed is consulted by Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate to decide the need list.</summary>
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class Patch_ShouldHaveSubmissionNeed
    {
        static void Postfix(NeedDef nd, ref bool __result, Pawn ___pawn)
        {
            if (nd == null || nd.defName != "RJWSH_Submission") return;
            if (!RimJobWorldSexualHarassmentMod.Settings.enableSubmissionNeed) { __result = false; return; }
            var p = ___pawn;
            if (p == null || !p.RaceProps.Humanlike || p.Dead) { __result = false; return; }
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            __result = prof != null && (prof.IsConditioned || prof.ownerId >= 0 || prof.relationshipOwnerId >= 0);
        }
    }

    /// <summary>Gaining a trait mid-game shifts a tracked pawn's deep sexual attributes (Masochist, Nympho,
    /// Sadist, Kind, Wimp, ...). Guarded so it only fires on a genuine gain (GainTrait no-ops when the pawn
    /// already has it) and only for pawns whose attributes are already seeded.</summary>
    [HarmonyPatch(typeof(TraitSet), "GainTrait")]
    public static class Patch_TraitSet_GainTrait
    {
        static void Prefix(TraitSet __instance, Trait trait, out bool __state)
        {
            __state = trait?.def != null && __instance.HasTrait(trait.def);
        }
        static void Postfix(Pawn ___pawn, Trait trait, bool __state)
        {
            if (__state || trait?.def == null) return; // already had it - not a real gain
            try { HarassmentEngine.TraitAttributeEffect(___pawn, trait.def.defName, 1f); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] trait-gain attr effect failed: " + e.Message, 0x5A1361); }
        }
    }

    /// <summary>Losing a trait reverses its attribute effect.</summary>
    [HarmonyPatch(typeof(TraitSet), "RemoveTrait")]
    public static class Patch_TraitSet_RemoveTrait
    {
        static void Prefix(TraitSet __instance, Trait trait, out bool __state)
        {
            __state = trait?.def != null && __instance.HasTrait(trait.def);
        }
        static void Postfix(Pawn ___pawn, Trait trait, bool __state)
        {
            if (!__state || trait?.def == null) return; // it didn't actually have it
            try { HarassmentEngine.TraitAttributeEffect(___pawn, trait.def.defName, -1f); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] trait-remove attr effect failed: " + e.Message, 0x5A1362); }
        }
    }

    /// <summary>A worn-out pawn gives less satisfying sex: scale the pawn's satisfaction down by how worn their
    /// PARTNER's used hole is (females lose quality faster). Prefix runs at low priority so it lands after
    /// Sexperience's own SatisfyPersonal prefix. rjw.SexUtility is a hard-dep type.</summary>
    [HarmonyPatch(typeof(rjw.SexUtility), "SatisfyPersonal")]
    public static class Patch_SexUtility_SatisfyPersonal_Wear
    {
        [HarmonyPriority(Priority.Low)]
        static void Prefix(rjw.SexProps props, ref float satisfaction)
        {
            try { if (props?.partner != null) satisfaction *= HarassmentEngine.WornSexQualityFactor(props.partner); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] wear sex-quality failed: " + e.Message, 0x5A1352); }
        }
    }

    /// <summary>After any RJW act, an evil witness may photograph it for blackmail. RJW is a hard dep,
    /// so the typed target is safe.</summary>
    [HarmonyPatch(typeof(rjw.SexUtility), "Aftersex")]
    public static class Patch_SexUtility_Aftersex_Photo
    {
        static void Postfix(rjw.SexProps props)
        {
            try { HarassmentEngine.TryCapturePhoto(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] photo capture failed: " + e.Message, 0x5A1310); }

            try { HarassmentEngine.TryPayWhore(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] whore payout failed: " + e.Message, 0x5A1345); }

            try { HarassmentEngine.TryAfterSexCuddle(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] after-sex cuddle failed: " + e.Message, 0x5A1346); }

            try { HarassmentEngine.TryWitnessPhotos(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] witness photos failed: " + e.Message, 0x5A1325); }

            try { HarassmentEngine.UpdateAttributesAfterSex(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] attribute update failed: " + e.Message, 0x5A1350); }

            try { HarassmentEngine.ApplyCollarForcedDebuff(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] collar forced-debuff failed: " + e.Message, 0x5A1323); }

            try { HarassmentEngine.TryRapistTattoo(props); }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] rapist tattoo failed: " + e.Message, 0x5A1351); }

            try
            {
                if (props != null && props.isRape)
                    MapComponent_HarassmentScan.EnqueueSceneEnd(props.initiator, props.recipient);
            }
            catch (Exception e) { Log.WarningOnce("[RJW Sexual Harassment] post-rape restraint enqueue failed: " + e.Message, 0x5A1321); }
        }
    }

    /// <summary>Removes the vanilla (Biotech) right-click "Romance" option when the initiator and target are in
    /// an owner-pet relationship. Registered manually via TryPatchRomanceSuppress.</summary>
    public static class Patch_VanillaRomanceSuppress
    {
        public static void Postfix(Pawn clickedPawn, FloatMenuContext context, ref FloatMenuOption __result)
        {
            if (__result == null) return;
            try
            {
                var init = context?.FirstSelectedPawn;
                if (init != null && HarassmentEngine.AreOwnerPet(init, clickedPawn)) __result = null;
            }
            catch { }
        }
    }

    /// <summary>Filters RJW's RMB romance-attempt and marriage-proposal entries out of the socialize sub-menu for
    /// owner-pet pairs. Registered manually via TryPatchRomanceSuppress.</summary>
    public static class Patch_RjwRomanceSuppress
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Pawn pawn, LocalTargetInfo target)
        {
            Pawn tgt = target.Pawn;
            bool suppress = false;
            try { suppress = tgt != null && HarassmentEngine.AreOwnerPet(pawn, tgt); } catch { }
            if (!suppress)
            {
                foreach (var o in __result) yield return o;
                yield break;
            }
            string romance = null, marriage = null;
            try { romance = "RJW_RMB_RomanceAttempt".Translate(); } catch { }
            try { marriage = "RJW_RMB_MarriageProposal".Translate(); } catch { }
            foreach (var o in __result)
            {
                if (o != null && o.Label != null && (o.Label == romance || o.Label == marriage)) continue;
                yield return o;
            }
        }
    }
}
