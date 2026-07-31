using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using rjw;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The dress-up loadout window: pick any combination of lockable devices / skimpy outfits, preview the
    /// result live on a paperdoll, then lock the whole combo on at once. The selection is authoritative -
    /// anything it conflicts with (even an already-locked device, or the pawn's clothing) is taken off:
    /// conflicting clothing is dropped at their feet, an overridden locked device is destroyed and its key
    /// purged. The control collar is never disturbed.
    ///
    /// Currently-locked devices stay real and untouched while you browse; only new picks are equipped as
    /// previews (RJW on_wear suppressed via Patch_BondageOnWear_Preview, so they stay unlocked until Apply).
    /// Cancel restores everything exactly as it was.
    /// </summary>
    public class Dialog_DressUp : Window
    {
        // True only while a preview piece is being equipped, so the RJW on_wear patch skips lock + key + hediff.
        public static bool Previewing = false;

        private readonly Pawn slave;
        private readonly Pawn owner;
        private readonly BodyDef body;
        private readonly List<ThingDef> devices;
        private readonly HashSet<ThingDef> managedSet;
        private readonly ThingDef collarDef = RJWSH_ThingDefOf.RJWSH_ControlCollar;

        private readonly HashSet<ThingDef> selected = new HashSet<ThingDef>();
        private readonly HashSet<ThingDef> originalReal = new HashSet<ThingDef>();
        private readonly Dictionary<ThingDef, Apparel> worn = new Dictionary<ThingDef, Apparel>();   // managed devices on the pawn now
        private readonly Dictionary<ThingDef, Apparel> removedReals = new Dictionary<ThingDef, Apparel>(); // real devices taken off
        private readonly List<Apparel> displacedClothing = new List<Apparel>();                       // clothing taken off
        private Vector2 scroll;
        private bool applied;

        public override Vector2 InitialSize => new Vector2(780f, 620f);
        protected override float Margin => 0f;   // we draw our own Modern-Suite chrome

        public Dialog_DressUp(Pawn slave, Pawn owner)
        {
            this.slave = slave;
            this.owner = owner;
            body = slave?.RaceProps?.body;
            forcePause = true;
            draggable = true;
            doCloseX = true;
            doWindowBackground = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;

            devices = HarassmentEngine.AllLockableDevices()
                .Where(d => d != null && d != collarDef)
                .Distinct()
                .OrderBy(d => d.label ?? d.defName)
                .ToList();
            managedSet = new HashSet<ThingDef>(devices);

            // Currently-locked managed devices start checked and stay real until Apply.
            if (slave?.apparel != null)
            {
                foreach (var w in slave.apparel.WornApparel.ToList())
                {
                    if (managedSet.Contains(w.def))
                    {
                        worn[w.def] = w;
                        selected.Add(w.def);
                        originalReal.Add(w.def);
                    }
                }
            }
        }

        private static bool GrayBtn(Rect r, string label, bool enabled = true)
        {
            Color fill = !enabled ? ModernStyle.PanelBG : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.14f) : ModernStyle.BGL;
            Widgets.DrawBoxSolid(r, fill);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = enabled ? new Color(0.9f, 0.9f, 0.9f) : ModernStyle.TextDim;
            Widgets.Label(r, label);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            return enabled && Widgets.ButtonInvisible(r);
        }

        // Modern-Suite checkbox row: accent-left + faint fill when selected, a filled accent check box.
        private bool CheckRow(Rect row, string label, bool sel)
        {
            if (sel) { Widgets.DrawBoxSolid(row, new Color(ModernStyle.Accent.r, ModernStyle.Accent.g, ModernStyle.Accent.b, 0.12f)); Widgets.DrawBoxSolid(new Rect(row.x, row.y, 2f, row.height), ModernStyle.Accent); }
            else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.05f));
            var box = new Rect(row.x + 8f, row.center.y - 8f, 16f, 16f);
            Widgets.DrawBoxSolid(box, sel ? ModernStyle.Accent : new Color(0f, 0f, 0f, 0.25f));
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(box, 1); GUI.color = Color.white;
            if (sel) { GUI.color = ModernStyle.BGD; Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; Widgets.Label(box, "\u2713"); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(box.xMax + 8f, row.y, row.xMax - box.xMax - 10f, row.height), label);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            return Widgets.ButtonInvisible(row);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, ModernStyle.BGD);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(inRect, 1); GUI.color = Color.white;
            var content = inRect.ContractedBy(16f);

            Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(content.x, content.y, content.width - 30f, 34f), "Tailor - " + slave.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(content.x, content.y + 32f, content.width, 22f),
                "Your selection wins: anything it conflicts with is taken off (clothes dropped, a locked device removed).");
            GUI.color = Color.white;

            const float bottom = 44f;
            var bodyRect = new Rect(content.x, content.y + 62f, content.width, content.height - 62f - bottom);
            const float dollW = 250f;
            var listRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width - dollW - 12f, bodyRect.height);
            DrawList(listRect);
            var dollRect = new Rect(listRect.xMax + 12f, bodyRect.y, dollW, bodyRect.height);
            DrawPaperdoll(dollRect);

            float by = content.yMax - 32f;
            if (GrayBtn(new Rect(content.x, by, 130f, 32f), "Clear all")) ClearAll();
            string applyLabel = selected.Count > 0 ? ("Apply (" + selected.Count + ")") : "Apply";
            if (GrayBtn(new Rect(content.xMax - 290f, by, 140f, 32f), applyLabel)) Apply();
            if (GrayBtn(new Rect(content.xMax - 140f, by, 140f, 32f), "Cancel")) Close();
        }

        public int SelectedCount => selected.Count;

        public void DrawList(Rect rect)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(6f);
            const float rowH = 30f;
            var view = new Rect(0f, 0f, inner.width - 16f, devices.Count * rowH + 4f);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(inner, ref scroll, view);
            float y = 2f;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                var row = new Rect(2f, y, view.width - 4f, rowH - 2f);
                string label = (d.label ?? d.defName).CapitalizeFirst();
                if (originalReal.Contains(d)) label += "  (locked on)";
                bool sel = selected.Contains(d);
                if (CheckRow(row, label, sel)) Toggle(d, !sel);
                if (Mouse.IsOver(row) && !d.description.NullOrEmpty())
                    TooltipHandler.TipRegion(row, d.description);
                y += rowH;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        private void Toggle(ThingDef d, bool on)
        {
            if (on) Select(d); else Deselect(d);
            PortraitsCache.SetDirty(slave);
        }

        private void Select(ThingDef def)
        {
            if (selected.Contains(def)) return;
            if (ConflictsWithCollar(def))
            {
                Messages.Message((def.label ?? def.defName).CapitalizeFirst() +
                    " cannot be worn over the control collar.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            // Auto-remove any selected device this one is mutually exclusive with.
            foreach (var m in selected.Where(s => s != def && !ApparelUtility.CanWearTogether(def, s, body)).ToList())
                Deselect(m);
            // Take off (and stash for drop) any non-device clothing in the way.
            foreach (var w in slave.apparel.WornApparel.ToList())
            {
                if (w.def == collarDef || managedSet.Contains(w.def)) continue;
                if (!ApparelUtility.CanWearTogether(def, w.def, body) && HarassmentEngine.BypassRemoveWorn(slave, w))
                    displacedClothing.Add(w);
            }
            // Equip: re-use a stashed real if we own one, otherwise a fresh preview copy.
            if (removedReals.TryGetValue(def, out var realInst))
            {
                HarassmentEngine.RewearStashed(slave, realInst);
                worn[def] = realInst;
                removedReals.Remove(def);
            }
            else if (!worn.ContainsKey(def))
            {
                var app = HarassmentEngine.PreviewEquip(slave, def);
                if (app == null)
                {
                    Messages.Message((def.label ?? def.defName).CapitalizeFirst() +
                        " cannot be worn (no body part for it).", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                worn[def] = app;
            }
            selected.Add(def);
        }

        private void Deselect(ThingDef def)
        {
            selected.Remove(def);
            if (!worn.TryGetValue(def, out var inst)) return;
            worn.Remove(def);
            HarassmentEngine.BypassRemoveWorn(slave, inst);
            if (originalReal.Contains(def)) removedReals[def] = inst; // real -> destroy on apply / restore on cancel
            else HarassmentEngine.PreviewRemove(slave, inst);          // preview copy -> discard
        }

        private bool ConflictsWithCollar(ThingDef def)
        {
            if (collarDef == null || slave?.apparel == null) return false;
            for (int i = 0; i < slave.apparel.WornApparel.Count; i++)
                if (slave.apparel.WornApparel[i].def == collarDef)
                    return !ApparelUtility.CanWearTogether(def, collarDef, body);
            return false;
        }

        private void ClearAll()
        {
            foreach (var d in selected.ToList()) Deselect(d);
            PortraitsCache.SetDirty(slave);
        }

        private Portraits.Sizer _dollSizer;
        private void DrawPaperdoll(Rect rect)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(8f);
            float s = Mathf.Min(inner.width, inner.height - 24f);
            var portraitRect = new Rect(inner.center.x - s / 2f, inner.y, s, s);
            Widgets.DrawBoxSolid(portraitRect, new Color(0.04f, 0.045f, 0.06f));
            // `s` tracks the host rect and therefore the window size - must go through the sizer. See Portraits.
            if (slave != null && !slave.Destroyed)
                Portraits.Body(portraitRect, slave, _dollSizer.Request(new Vector2(s, s)), 1f, 0f);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(inner.x, portraitRect.yMax + 2f, inner.width, 22f), "Preview");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public void Apply()
        {
            applied = true;
            // New picks (preview copies) get removed here and re-equipped for real + locked below.
            var newPicks = worn.Keys.Where(d => !originalReal.Contains(d)).ToList();
            foreach (var d in newPicks) HarassmentEngine.PreviewRemove(slave, worn[d]);
            // Overridden / unchecked locked devices: destroy them and purge their keys.
            foreach (var kv in removedReals) HarassmentEngine.PurgeDeviceAndKey(slave, kv.Value);
            // Displaced clothing: drop at the slave's feet so the player keeps it.
            foreach (var c in displacedClothing) HarassmentEngine.DropDisplacedApparel(slave, c);
            // Lock the new picks on for real (kept locked devices stay as they were).
            foreach (var d in newPicks) HarassmentEngine.ApplyAndLockDevice(slave, d, owner);

            removedReals.Clear();
            displacedClothing.Clear();
            PortraitsCache.SetDirty(slave);
            if (newPicks.Count > 0 || selected.Count > 0)
                MoteMaker.ThrowText(slave.DrawPos, slave.Map, "Dressed up", 2.5f);
            Close();
        }

        // Restore the pawn exactly as it was: drop preview copies, re-wear stashed reals + clothing.
        public void Cancel()
        {
            foreach (var kv in worn.ToList())
                if (!originalReal.Contains(kv.Key)) HarassmentEngine.PreviewRemove(slave, kv.Value);
            foreach (var kv in removedReals) HarassmentEngine.RewearStashed(slave, kv.Value);
            foreach (var c in displacedClothing) HarassmentEngine.RewearStashed(slave, c);
            removedReals.Clear();
            displacedClothing.Clear();
            PortraitsCache.SetDirty(slave);
        }

        public override void PreClose()
        {
            base.PreClose();
            if (!applied) Cancel();
        }
    }
}
