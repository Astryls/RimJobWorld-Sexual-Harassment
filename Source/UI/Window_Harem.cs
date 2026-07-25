using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The pet dashboard: a two-pane control panel for every collared pet / owned slave. Left = roster grouped
    /// by owner (situation labels + mini conditioning/rapport bars, click to select). Right = detail for the
    /// selected pet: bigger bars, a live command cluster (discipline/reward/come/keep-naked/auto-service/dress/
    /// free) and a scribed conditioning+rapport history graph. Cute paw-print border throughout.
    /// </summary>
    public class Window_Harem : Window
    {
        private Vector2 _scroll;
        private Vector2 _logScroll;
        private Pawn _selected;
        private int _detailTab;    // detail page tab: 0 Overview, 1 Attributes, 2 Conditioning, 3 Schedule, 4 Social
        private Vector2 _attrScroll;
        private Vector2 _moodScroll, _intScroll;
        private int _view;         // 0 = Pets (detail), 1 = Harem (colony-wide table)
        private Vector2 _haremScroll;
        private Vector2 _overviewScroll;
        private Vector2 _galleryScroll, _eventsScroll;
        private int _ctxTab;   // Concept 3 ops-column context card: Feed / Graph / History / Profile / Social
        private static readonly string[] CtxTabs = { "Feed", "Graph", "History", "Profile", "Social" };
        private static readonly string[] CtxTabsNoProfile = { "Feed", "Graph", "History", "Social" };
        private Vector2 _chronScroll;
        private static readonly Color StageBg = new Color(0.055f, 0.062f, 0.078f);
        // Inline third-column editor mode: 0 Ops, 1 Tailor, 2 Stylist, 3 Photos. Held (not on the WindowStack) and drawn inline.
        private int _opsMode;
        private Dialog_DressUp _tailorInline;
        private Dialog_Stylist _stylistInline;
        private Vector2 _photoInlineScroll;
        private Pawn _opsPetTag;
        private float _stageZoom = 0.82f, _stageOff = 0.30f;

        // Measured per-frame font heights. RimWorld's "Use tiny text in some UIs" option (Accessibility), when OFF,
        // renders GameFont.Tiny much taller (~Small), so hardcoded 14-18px rects clip descenders. Always size off these.
        private static int _fhFrame = -1; private static float _tinyHv, _smallHv;
        private static void EnsureFontH()
        {
            if (Time.frameCount == _fhFrame) return;
            _fhFrame = Time.frameCount;
            var f = Text.Font;
            Text.Font = GameFont.Tiny; _tinyHv = Mathf.Ceil(Text.LineHeight) + 2f;
            Text.Font = GameFont.Small; _smallHv = Mathf.Ceil(Text.LineHeight) + 2f;
            Text.Font = f;
        }
        private static float TinyH { get { EnsureFontH(); return _tinyHv; } }
        private static float SmallH { get { EnsureFontH(); return _smallHv; } }
        private static readonly string[] ViewNames = { "Pets", "Harem" };
        private static readonly string[] SortNames = { "Name", "Cond", "Rapport", "Owner" };
        private string _haremFilter = "";
        private int _sortMode;     // 0 name, 1 cond, 2 rapport, 3 owner
        private bool _sortDesc;
        private static readonly string[] DetailTabs = { "Conditioning", "Role", "Graph", "Moodlets", "Interactions" };
        private static readonly string[] PetTabs = { "Overview", "Attributes", "Conditioning", "Schedule", "Social" };
        private int _paintAssign = 1;   // schedule painter: which assignment a click paints
        private static readonly (string label, Color col)[] SchedAssigns =
        {
            ("Free",     new Color(0.30f, 0.32f, 0.36f)),
            ("Serve",    new Color(0.55f, 0.45f, 0.75f)),
            ("Train",    new Color(0.62f, 0.36f, 0.70f)),
            ("Parade",   new Color(0.90f, 0.55f, 0.35f)),
            ("Rest",     new Color(0.35f, 0.55f, 0.75f)),
            ("Confined", new Color(0.80f, 0.35f, 0.35f)),
        };

        private static readonly Color CondColor = new Color(0.62f, 0.36f, 0.70f);
        private static readonly Color TrustColor = new Color(0.90f, 0.74f, 0.32f);
        private static readonly Color FearColor = new Color(0.80f, 0.28f, 0.28f);
        private static readonly Color EmptyColor = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color BranchColor = new Color(0.5f, 0.42f, 0.5f, 0.7f);
        private static readonly Color PawTint = new Color(0.50f, 0.53f, 0.58f, 0.28f);
        private static readonly Color SelTint = new Color(0.72f, 0.75f, 0.82f, 0.12f);
        // Modern Suite palette (matches Modern Needs Tab): near-black window, dark panels, teal-gray borders.
        private static readonly Color PanelBg = ModernStyle.BGD;
        private static readonly Color CardBg = ModernStyle.PanelBG;
        private static readonly Color CardBorder = ModernStyle.BGL;
        private static readonly Color FrameBorder = ModernStyle.BGL;

        private const float OwnerRowH = 44f;
        private const float PetRowH = 46f;
        private const float Indent = 36f;

        // Auto-fit floating size (Modern-tab convention): preferred 1020x720, clamped to the live screen.
        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(1200f, UI.screenWidth - 40f),
            Mathf.Min(800f, UI.screenHeight - 40f));
        protected override float Margin => 0f;   // we draw our own flat panel edge-to-edge

        private struct Group { public Pawn owner; public List<Pawn> pets; }

        public Window_Harem()
        {
            // Floating tool window (not menu-bar-bound): draggable, resizable, non-pausing, map stays live.
            doWindowBackground = false;    // no vanilla window frame - we draw a modern flat panel instead
            draggable = true;
            resizeable = true;
            doCloseX = true;
            forcePause = false;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            onlyOneOfTypeAllowed = true;
            drawShadow = true;
        }

        // ── Concept 3 "Command deck": compact roster (left) + paperdoll stage (center) + ops column (right). ──
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, PanelBg);
            DrawThinBorder(inRect, FrameBorder);
            DrawBorderIcons(inRect);

            // Content lives INSIDE the paw+collar border (the close X sits top-right).
            Rect content = inRect.ContractedBy(40f);
            if (_selected != null && (_selected.Dead || !_selected.Spawned)) _selected = null;
            if (_selected != _opsPetTag) { EndOpsEditors(); _opsPetTag = _selected; }

            const float gutter = 12f, bulkH = 30f;
            var body = new Rect(content.x, content.y, content.width, content.height - bulkH);
            var pets = AllPets();

            float leftW = 244f;
            float rightW = Mathf.Clamp(body.width * 0.36f, 384f, 452f);
            float centerX = body.x + leftW + gutter;
            var leftRect = new Rect(body.x, body.y, leftW, body.height);
            var centerRect = new Rect(centerX, body.y, body.xMax - rightW - gutter - centerX, body.height);
            var rightRect = new Rect(body.xMax - rightW, body.y, rightW, body.height);

            DrawRosterColumn(leftRect, pets);

            var prof = _selected != null ? Prof(_selected) : null;
            Pawn owner = _selected != null ? HarassmentEngine.FindKeyHolderFor(_selected) : null;
            if (_selected != null)
            {
                DrawStage(centerRect, _selected, prof, owner);
                DrawOpsColumn(rightRect, _selected, prof, owner);
            }
            else
            {
                DrawMarketStage(centerRect);
                DrawColonyOps(rightRect);
            }

            DrawBulkBar(new Rect(content.x, content.yMax - bulkH + 4f, content.width, 26f), pets);
        }

        // ── Left column: search + sort, compact roster cards, colony summary card ──────────────
        private void DrawRosterColumn(Rect rect, List<Pawn> pets)
        {
            var si = new Rect(rect.x, rect.y + 5f, 16f, 16f);
            GUI.color = ModernStyle.TextDim; GUI.DrawTexture(si, HarassmentTextures.Search); GUI.color = Color.white;
            var sortBtn = new Rect(rect.xMax - 28f, rect.y, 28f, 26f);
            var fb = new Rect(si.xMax + 4f, rect.y + 1f, sortBtn.x - si.xMax - 10f, 24f);
            _haremFilter = Widgets.TextField(fb, _haremFilter ?? "");
            DrawSortDropdown(sortBtn);

            float summaryH = 6f * TinyH + 72f;
            var listRect = new Rect(rect.x, rect.y + 32f, rect.width, rect.height - 32f - summaryH - 8f);
            if (pets.Count == 0)
            {
                Empty(listRect, "No collared pets yet. Condition a pawn to 90+ and lock a control collar on them.");
            }
            else
            {
                var shown = FilterSortPets(pets);
                float rowH = Mathf.Max(48f, SmallH + TinyH + 12f); float gap = 4f;
                float availW = listRect.width;   // cards fit the column edge; the 8px flat scrollbar overlays only when scrolling
                var view = new Rect(0f, 0f, availW, shown.Count * (rowH + gap));
                ModernStyle.PushScroll();
                Widgets.BeginScrollView(listRect, ref _scroll, view);
                for (int i = 0; i < shown.Count; i++)
                    DrawCompactRow(new Rect(0f, i * (rowH + gap), availW, rowH), shown[i]);
                Widgets.EndScrollView();
                ModernStyle.PopScroll();
            }
            DrawSummaryCard(new Rect(rect.x, rect.yMax - summaryH, rect.width, summaryH), pets);
        }

        // 46px roster card: portrait (+ head-girl star), name, situation, dual mini bars, risk dot. Accent-left when selected.
        private void DrawCompactRow(Rect card, Pawn pet)
        {
            var prof = Prof(pet);
            bool sel = _selected == pet;
            Widgets.DrawBoxSolid(card, sel ? new Color(ModernStyle.Accent.r, ModernStyle.Accent.g, ModernStyle.Accent.b, 0.10f) : ModernStyle.PanelBG);
            if (sel) Widgets.DrawBoxSolid(new Rect(card.x, card.y, 2f, card.height), ModernStyle.Accent);
            else if (Mouse.IsOver(card)) Widgets.DrawBoxSolid(card, new Color(1f, 1f, 1f, 0.05f));
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(card, 1); GUI.color = Color.white;

            var port = new Rect(card.x + 5f, card.y + 6f, 34f, 34f);
            DrawPortrait(port, pet);
            if (prof != null && prof.isHeadGirl && HarassmentTextures.Star != null)
            {
                var st = new Rect(port.x - 2f, port.y - 2f, 12f, 12f);
                Widgets.DrawBoxSolid(st.ExpandedBy(1f), new Color(0f, 0f, 0f, 0.55f));
                GUI.DrawTexture(st, HarassmentTextures.Star);
            }

            float tx = port.xMax + 6f, tw = card.xMax - tx - 16f;
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(tx, card.y + 3f, tw, SmallH), pet.LabelShortCap.Truncate(tw));
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.72f, 0.66f, 0.5f);
            Widgets.Label(new Rect(tx, card.y + 2f + SmallH, tw, TinyH), SituationLabel(pet, prof).Truncate(tw));
            Text.Font = GameFont.Small; GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rap = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;
            Widgets.FillableBar(new Rect(tx, card.yMax - 10f, tw * 0.66f, 5f), cond, SolidBar(CondColor), SolidBar(EmptyColor), false);
            Widgets.FillableBar(new Rect(tx, card.yMax - 5f, tw * 0.66f, 4f), rap, SolidBar(rap < 0.4f ? FearColor : TrustColor), SolidBar(EmptyColor), false);

            float risk = RiskScore(pet);
            var dot = new Rect(card.xMax - 12f, card.y + 6f, 7f, 7f);
            Widgets.DrawBoxSolid(dot, risk > 15f ? new Color(0.85f, 0.3f, 0.3f) : risk > 5f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.35f, 0.7f, 0.42f));
            TooltipHandler.TipRegion(new Rect(dot.x - 4f, dot.y - 4f, 15f, 15f), risk > 15f ? "At risk - may rebel or flee." : risk > 5f ? "Some instability." : "Stable.");

            if (Widgets.ButtonInvisible(card)) { _selected = sel ? null : pet; _ctxTab = 0; }
        }

        private void DrawSummaryCard(Rect card, List<Pawn> all)
        {
            ModernStyle.DrawCard(card);
            var inner = card.ContractedBy(8f);
            float y = MiniHeader(inner, inner.y, "Colony");
            int n = all.Count; float ac = 0f, ar = 0f; int risk = 0, income = 0;
            for (int i = 0; i < n; i++) { var pr = Prof(all[i]); if (pr != null) { ac += pr.hypnosisLevel; ar += pr.rapport; income += pr.lifetimeEarnings; if (RiskScore(all[i]) > 15f) risk++; } }
            if (n > 0) { ac /= n; ar /= n; }
            int noto = GameComponent_Harassment.Instance?.notoriety ?? 0;
            void Row(string k, string v, Color vc)
            {
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
                Widgets.Label(new Rect(inner.x, y, inner.width * 0.6f, TinyH), k);
                GUI.color = vc; Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(inner.x, y, inner.width, TinyH), v);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; y += TinyH;
            }
            Row("Pets", n.ToString(), ModernStyle.Body);
            Row("Avg conditioning", ac.ToString("0") + "%", CondColor);
            Row("Avg rapport", ar.ToString("0") + "%", TrustColor);
            Row("Notoriety", noto.ToString(), new Color(0.82f, 0.52f, 0.25f));
            if (income > 0) Row("Earned", income + "s", new Color(0.72f, 0.66f, 0.5f));
            Row("At risk", risk.ToString(), risk > 0 ? new Color(0.85f, 0.35f, 0.35f) : ModernStyle.Body);
            var btn = new Rect(inner.x, inner.yMax - 24f, inner.width, 22f);
            if (GrayButton(btn, _selected != null ? "Back to colony / market" : "Colony / market view")) { _selected = null; _marketPreview = null; }
        }

        // ── Center: the paperdoll stage - full-body render, interactive hotspots, name, equipment strip ──
        private void DrawStage(Rect rect, Pawn pet, PawnProfile prof, Pawn owner)
        {
            Widgets.DrawBoxSolid(rect, StageBg);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            DrawStageCorners(rect, ModernStyle.Accent);

            var close = new Rect(rect.xMax - 26f, rect.y + 4f, 22f, 22f);
            if (GrayButton(close, "\u00d7", true, "Deselect (back to colony view).")) { _selected = null; return; }
            var jump = new Rect(close.x - 26f, rect.y + 4f, 22f, 22f);
            if (IconButton(jump, HarassmentTextures.GoTo, "Jump to and select this pawn.")) CameraJumper.TryJumpAndSelect(pet);

            const float equipH = 74f, nameH = 46f;
            var dollArea = new Rect(rect.x, rect.y + 8f, rect.width, rect.height - equipH - nameH - 14f);
            Rect frame;
            if (_opsMode == 2)   // Stylist: a wider, roomier frame so hair/pigtails aren't cropped at the same zoom.
            {
                float fw = Mathf.Min(rect.width * 0.82f, dollArea.height * 0.66f);
                float fh = Mathf.Min(dollArea.height, fw / 0.72f);
                frame = new Rect(dollArea.center.x - fw / 2f, dollArea.y + (dollArea.height - fh) / 2f, fw, fh);
            }
            else
            {
                float frameW = Mathf.Min(rect.width * 0.5f, dollArea.height * 0.58f);
                frame = new Rect(dollArea.center.x - frameW / 2f, dollArea.y, frameW, dollArea.height);
            }
            // Animate the doll zoom: head-and-shoulders while styling (Stylist), full-body otherwise. Offset fixed for headroom.
            float tz = _opsMode == 2 ? 1.7f : 0.82f, to = 0.30f;
            if (Event.current.type == EventType.Repaint)
            {
                float k = 1f - Mathf.Pow(0.0004f, Time.deltaTime);
                _stageZoom = Mathf.Lerp(_stageZoom, tz, k); _stageOff = Mathf.Lerp(_stageOff, to, k);
                if (Mathf.Abs(_stageZoom - tz) < 0.01f) { _stageZoom = tz; _stageOff = to; }
            }
            DrawStageDoll(frame, pet, _stageZoom, _stageOff);

            float cond = prof != null ? prof.hypnosisLevel : 0f;
            float rap = prof != null ? prof.rapport : 50f;
            int hr = GenLocalDate.HourOfDay(pet);
            string schedLbl = (prof?.schedule != null && prof.schedule.Count == 24)
                ? SchedAssigns[Mathf.Clamp(prof.schedule[hr], 0, SchedAssigns.Length - 1)].label : "free";
            if (_opsMode != 2)   // hide hotspots while zoomed for styling
            {
                DrawHotspot(rect, frame, 0.50f, 0.46f, true, CondColor, "Conditioning", (int)cond + "%", () => _ctxTab = 1);
                DrawHotspot(rect, frame, 0.52f, 0.51f, false, HeaderCol, "Collar", HarassmentEngine.WearingControlCollar(pet) ? "locked" : "none", () => _ctxTab = 0);
                DrawHotspot(rect, frame, 0.48f, 0.66f, true, rap < 0.4f ? FearColor : TrustColor, "Rapport", (int)rap + "%", () => _ctxTab = 3);
                DrawHotspot(rect, frame, 0.50f, 0.80f, false, new Color(0.55f, 0.60f, 0.66f), "Schedule", schedLbl, () => _ctxTab = 0);
            }

            float ny = dollArea.yMax + 4f;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(rect.x, ny, rect.width, 30f), pet.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = ModernStyle.TextDim;
            string sub = (owner != null ? "Owned by " + owner.LabelShortCap : "No active owner") + "  \u2022  " + SituationLabel(pet, prof);
            Widgets.Label(new Rect(rect.x, ny + 28f, rect.width, SmallH), sub);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            DrawEquipmentStrip(new Rect(rect.x + 8f, rect.yMax - equipH + 6f, rect.width - 16f, equipH - 12f), pet, owner);
        }

        private static void DrawStageCorners(Rect r, Color c)
        {
            Color k = new Color(c.r, c.g, c.b, 0.65f); const float L = 14f, t = 2f;
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, L, t), k); Widgets.DrawBoxSolid(new Rect(r.x, r.y, t, L), k);
            Widgets.DrawBoxSolid(new Rect(r.xMax - L, r.y, L, t), k); Widgets.DrawBoxSolid(new Rect(r.xMax - t, r.y, t, L), k);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - t, L, t), k); Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - L, t, L), k);
            Widgets.DrawBoxSolid(new Rect(r.xMax - L, r.yMax - t, L, t), k); Widgets.DrawBoxSolid(new Rect(r.xMax - t, r.yMax - L, t, L), k);
        }

        // Full-body pawn render (wider aspect, lower zoom than the headshot). Mobile override so downed pets stand upright.
        private static void DrawStageDoll(Rect frame, Pawn p, float zoom = 0.82f, float offZ = 0.30f)
        {
            Widgets.DrawBoxSolid(frame, new Color(0.04f, 0.045f, 0.06f));
            Widgets.DrawBoxSolid(new Rect(frame.x + frame.width * 0.22f, frame.yMax - 9f, frame.width * 0.56f, 6f), new Color(0f, 0f, 0f, 0.35f));
            if (p == null) return;
            try
            {
                var tex = PortraitsCache.Get(p, new Vector2(frame.width, frame.height), Rot4.South,
                    new Vector3(0f, 0f, offZ), Mathf.Round(zoom * 50f) / 50f, healthStateOverride: PawnHealthState.Mobile);
                if (tex != null) GUI.DrawTexture(frame, tex);
            }
            catch { Widgets.ThingIcon(frame, p); }
        }

        // Inline third-column editor (Tailor / Stylist / Photos) with a Back header. Replaces the popup dialogs.
        private void DrawOpsEditor(Rect rect, Pawn pet)
        {
            const float hH = 32f;
            bool backClicked = GrayButton(new Rect(rect.x, rect.y, 66f, 26f), "\u2039 Back");
            string title = _opsMode == 1 ? "Tailor" : _opsMode == 2 ? "Stylist" : "Photos";
            Text.Anchor = TextAnchor.MiddleCenter; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(rect.x + 70f, rect.y, rect.width - 240f, 26f), title);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            var body = new Rect(rect.x, rect.y + hH, rect.width, rect.yMax - rect.y - hH);

            if (_opsMode == 1 && _tailorInline != null)
            {
                if (GrayButton(new Rect(rect.xMax - 92f, rect.y, 92f, 26f), "Apply (" + _tailorInline.SelectedCount + ")")) { _tailorInline.Apply(); _tailorInline = null; _opsMode = 0; return; }
                if (backClicked) { _tailorInline.Cancel(); _tailorInline = null; _opsMode = 0; return; }
                _tailorInline.DrawList(body);
            }
            else if (_opsMode == 2 && _stylistInline != null)
            {
                if (GrayButton(new Rect(rect.xMax - 162f, rect.y, 92f, 26f), "Randomize")) _stylistInline.Randomize();
                if (GrayButton(new Rect(rect.xMax - 66f, rect.y, 66f, 26f), "Reset")) _stylistInline.Revert();
                if (backClicked) { _stylistInline.Commit(); _stylistInline = null; _opsMode = 0; return; }
                _stylistInline.DrawList(body);
            }
            else if (_opsMode == 3)
            {
                if (backClicked) { _opsMode = 0; return; }
                DrawPhotosInline(body, pet);
            }
            else { _opsMode = 0; }
        }

        // Per-pet photo gallery drawn inline in the ops column. Each row is MEASURED from its (variable-length) lore
        // so nothing clips - the user runs Accessibility "Use tiny text" OFF, so Tiny renders tall.
        private void DrawPhotosInline(Rect rect, Pawn pet)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(8f);
            var photos = HarassmentEngine.GatherPhotosOf(pet);
            if (photos == null || photos.Count == 0) { Empty(inner, "No known photos of " + pet.LabelShort + "."); return; }
            var pdef = RJWSH_ThingDefOf.RJWSH_ScandalousPhoto;
            float viewW = inner.width - 16f;
            float textW = viewW - 12f - 40f - 8f;   // card pad + icon + gap
            var lores = new string[photos.Count];
            var heights = new float[photos.Count];
            Text.Font = GameFont.Tiny;
            float total = 0f;
            for (int i = 0; i < photos.Count; i++)
            {
                var rec = photos[i];
                lores[i] = rec.comp?.loreDesc ?? rec.loreOverride ?? "A scandalous photo.";
                float lh = Text.CalcHeight(lores[i], textW);
                heights[i] = Mathf.Max(40f, lh) + TinyH + 18f;   // lore + holder line + paddings
                total += heights[i] + 6f;
            }
            Text.Font = GameFont.Small;
            var view = new Rect(0f, 0f, viewW, total);
            Thing burn = null;
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(inner, ref _photoInlineScroll, view);
            float y = 0f;
            for (int i = 0; i < photos.Count; i++)
            {
                var rec = photos[i];
                var row = new Rect(0f, y, view.width, heights[i]); y += heights[i] + 6f;
                ModernStyle.DrawCard(row);
                var ri = row.ContractedBy(6f);
                var icon = new Rect(ri.x, ri.y, 40f, 40f);
                var tex = (rec.photo != null && rec.photo.def.uiIcon != null) ? rec.photo.def.uiIcon : pdef?.uiIcon;
                if (tex != null) { GUI.color = rec.photo == null ? new Color(1f, 1f, 1f, 0.4f) : Color.white; GUI.DrawTexture(icon, tex); GUI.color = Color.white; }
                else Widgets.DrawBoxSolid(icon, ModernStyle.PanelBG);
                float tx = icon.xMax + 8f, tw = ri.xMax - tx;
                Text.Font = GameFont.Tiny; GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(tx, ri.y, tw, ri.height - TinyH - 4f), lores[i]);
                var target = rec.holderPawn ?? (rec.photo != null && rec.photo.Spawned ? (Thing)rec.photo : null);
                bool canBurn = rec.photo != null && !rec.photo.Destroyed;
                int nb = (target != null ? 1 : 0) + (canBurn ? 1 : 0);
                GUI.color = ModernStyle.Accent; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(tx, ri.yMax - TinyH, Mathf.Max(40f, tw - nb * 26f - 6f), TinyH), rec.holder ?? "");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
                float bx = ri.xMax - 24f;
                if (canBurn) { var dr = new Rect(bx, ri.yMax - 24f, 22f, 22f); if (Widgets.ButtonImage(dr, HarassmentTextures.BurnPhoto ?? BaseContent.WhiteTex)) burn = rec.photo; TooltipHandler.TipRegion(dr, "Burn this photo"); bx -= 26f; }
                if (target != null) { var jr = new Rect(bx, ri.yMax - 24f, 22f, 22f); if (Widgets.ButtonImage(jr, HarassmentTextures.GoTo ?? BaseContent.WhiteTex)) CameraJumper.TryJumpAndSelect(target); TooltipHandler.TipRegion(jr, "Jump to"); }
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
            if (burn != null && !burn.Destroyed) burn.Destroy(DestroyMode.Vanish);
        }

        private void EndOpsEditors()
        {
            if (_tailorInline != null) { _tailorInline.Cancel(); _tailorInline = null; }
            if (_stylistInline != null) { _stylistInline.Commit(); _stylistInline = null; }
            _opsMode = 0;
        }

        public override void PreClose()
        {
            base.PreClose();
            EndOpsEditors();
        }

        // One interactive hotspot: a tinted ring on the figure, a connector line, and a readout chip pinned to the stage edge.
        private void DrawHotspot(Rect stage, Rect frame, float fx, float fy, bool leftSide, Color col, string title, string value, System.Action onClick)
        {
            Vector2 anchor = new Vector2(frame.x + frame.width * fx, frame.y + frame.height * fy);
            float chipW = Mathf.Min(150f, stage.width * 0.42f), chipH = 2f * TinyH + 8f;
            Rect chip = leftSide
                ? new Rect(stage.x + 4f, anchor.y - chipH / 2f, chipW, chipH)
                : new Rect(stage.xMax - chipW - 4f, anchor.y - chipH / 2f, chipW, chipH);
            Vector2 chipEdge = new Vector2(leftSide ? chip.xMax : chip.x, chip.center.y);
            Widgets.DrawLine(chipEdge, anchor, new Color(col.r, col.g, col.b, 0.55f), 1f);

            Widgets.DrawBoxSolid(chip, new Color(ModernStyle.PanelBG.r, ModernStyle.PanelBG.g, ModernStyle.PanelBG.b, 0.92f));
            Widgets.DrawBoxSolid(new Rect(leftSide ? chip.x : chip.xMax - 2f, chip.y, 2f, chip.height), col);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(chip, 1); GUI.color = Color.white;
            if (onClick != null && Mouse.IsOver(chip)) Widgets.DrawBoxSolid(chip, new Color(1f, 1f, 1f, 0.05f));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(chip.x + 8f, chip.y + 3f, chipW - 12f, TinyH), title);
            GUI.color = Color.Lerp(col, Color.white, 0.4f);
            Widgets.Label(new Rect(chip.x + 8f, chip.y + 3f + TinyH, chipW - 12f, TinyH), value);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;

            const float ns = 16f; var node = new Rect(anchor.x - ns / 2f, anchor.y - ns / 2f, ns, ns);
            GUI.color = col; if (HarassmentTextures.Node != null) GUI.DrawTexture(node, HarassmentTextures.Node); GUI.color = Color.white;
            if (onClick != null)
            {
                var hit = new Rect(node.x - 4f, node.y - 4f, ns + 8f, ns + 8f);
                if (Widgets.ButtonInvisible(chip) || Widgets.ButtonInvisible(hit)) onClick();
            }
        }

        // Equipment slots below the figure. Click a slot to open the matching dialog (collar calibrate / dress-up).
        private void DrawEquipmentStrip(Rect rect, Pawn pet, Pawn owner)
        {
            var slots = new System.ValueTuple<Texture2D, string, bool, System.Action>[]
            {
                new System.ValueTuple<Texture2D, string, bool, System.Action>(HarassmentTextures.CollarIcon, "Collar", HarassmentEngine.WearingControlCollar(pet), () => Find.WindowStack.Add(new Dialog_CollarCalibrate(pet))),
                new System.ValueTuple<Texture2D, string, bool, System.Action>(HarassmentTextures.Tailor, "Tailor", pet.apparel != null && pet.apparel.WornApparelCount > 0, () => { if (owner != null) { EndOpsEditors(); _tailorInline = new Dialog_DressUp(pet, owner); _opsMode = 1; } }),
                new System.ValueTuple<Texture2D, string, bool, System.Action>(HarassmentTextures.Stylist, "Stylist", false, () => { EndOpsEditors(); _stylistInline = new Dialog_Stylist(pet); _opsMode = 2; }),
                new System.ValueTuple<Texture2D, string, bool, System.Action>(HarassmentTextures.Photos, "Photos", false, () => { EndOpsEditors(); _opsMode = 3; }),
            };
            float gap = 6f; float sw = (rect.width - gap * (slots.Length - 1)) / slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                var r = new Rect(rect.x + i * (sw + gap), rect.y, sw, rect.height);
                Widgets.DrawBoxSolid(r, ModernStyle.PanelBG);
                GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.05f));
                float isz = Mathf.Min(28f, r.height - TinyH - 8f);
                GUI.color = s.Item3 ? new Color(0.98f, 0.82f, 0.35f) : new Color(0.70f, 0.70f, 0.74f);
                if (s.Item1 != null) GUI.DrawTexture(new Rect(r.center.x - isz / 2f, r.y + 6f, isz, isz), s.Item1);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim;
                Widgets.Label(new Rect(r.x, r.yMax - TinyH - 2f, r.width, TinyH), s.Item2);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(r, s.Item2 + (owner == null && s.Item2 == "Tailor" ? " (needs a key holder)" : ""));
                if (Widgets.ButtonInvisible(r)) s.Item4();
            }
        }

        // ── Right: ops column - command cluster, schedule & quota, context card (Feed/Graph/Profile/Social) ──
        private void DrawOpsColumn(Rect rect, Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (_opsMode != 0) { DrawOpsEditor(rect, pet); return; }
            const float gap = 8f, cmdH = 116f, schedH = 214f;
            var cmdCard = new Rect(rect.x, rect.y, rect.width, cmdH);
            var schedCard = new Rect(rect.x, cmdCard.yMax + gap, rect.width, schedH);
            var ctxCard = new Rect(rect.x, schedCard.yMax + gap, rect.width, rect.yMax - (schedCard.yMax + gap));

            ModernStyle.DrawCard(cmdCard);
            var ci = cmdCard.ContractedBy(8f);
            float cy = MiniHeader(ci, ci.y, "Commands");
            DrawCommandCluster(new Rect(ci.x, cy, ci.width, ci.yMax - cy), pet, prof, owner);

            ModernStyle.DrawCard(schedCard);
            var siR = schedCard.ContractedBy(8f);
            float sy = MiniHeader(siR, siR.y, "Schedule & quota");
            DrawSchedule(new Rect(siR.x, sy, siR.width, siR.yMax - sy), pet, prof);

            ModernStyle.DrawCard(ctxCard);
            var xi = ctxCard.ContractedBy(8f);
            bool showSex = RimJobWorldSexualHarassmentMod.Settings?.showSexualityTab ?? false;
            string[] tabs = showSex ? CtxTabs : CtxTabsNoProfile;
            if (_ctxTab >= tabs.Length) _ctxTab = 0;
            DrawAccentTabs(new Rect(xi.x, xi.y, xi.width, 24f), tabs, ref _ctxTab);
            var area = new Rect(xi.x, xi.y + 28f, xi.width, xi.yMax - (xi.y + 28f));
            switch (tabs[_ctxTab])
            {
                case "Graph": DrawHistoryGraph(area, prof); break;
                case "History": DrawChronicle(area, prof); break;
                case "Profile": SexualityPanelDrawer.Draw(area, pet, ref _attrScroll); break;
                case "Social":
                    float half = (area.height - 8f) / 2f;
                    float ty = MiniHeader(area, area.y, "Interactions");
                    DrawInteractions(new Rect(area.x, ty, area.width, area.y + half - ty), pet);
                    float bY = MiniHeader(area, area.y + half + 8f, "Moodlets");
                    DrawMoodlets(new Rect(area.x, bY, area.width, area.yMax - bY), pet);
                    break;
                default: DrawEventLog(area, prof); break;   // Feed
            }
        }

        // Icon-only gray button (command cluster). Tooltip carries the label + description.
        private static bool GrayIconBtn(Rect r, Texture2D icon, string tip, bool enabled = true)
        {
            Color fill = !enabled ? ModernStyle.PanelBG : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.16f) : ModernStyle.BGL;
            Widgets.DrawBoxSolid(r, fill);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            float s = Mathf.Min(22f, r.height - 8f);
            GUI.color = enabled ? new Color(0.88f, 0.88f, 0.92f) : ModernStyle.TextDim;
            if (icon != null) GUI.DrawTexture(new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s), icon);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        // Icon-only gray toggle: accent bar + accent-tinted icon when on.
        private static bool GrayIconToggle(Rect r, Texture2D icon, string tip, bool on)
        {
            Color fill = on ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.22f) : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.10f) : ModernStyle.PanelBG;
            Widgets.DrawBoxSolid(r, fill);
            if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 2f, r.height), ModernStyle.Accent);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            float s = Mathf.Min(20f, r.height - 8f);
            GUI.color = on ? ModernStyle.Accent : new Color(0.82f, 0.82f, 0.86f);
            if (icon != null) GUI.DrawTexture(new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s), icon);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // Command cluster: one row of 8 action icons + one row of 6 toggle icons (icon-only; tooltips carry the labels).
        private void DrawCommandCluster(Rect rect, Pawn pet, PawnProfile prof, Pawn owner)
        {
            if (prof == null) { Empty(rect, "No pet data."); return; }
            bool hasOwner = owner != null && owner.Spawned && !owner.Dead;
            bool collared = HarassmentEngine.WearingControlCollar(pet);
            const float gap = 4f;
            // Six action icons. Tailor lives in the equipment strip below the stage, not here.
            float aw = (rect.width - gap * 5f) / 6f; float ah = 34f; float y = rect.y;
            Rect A(int i) => new Rect(rect.x + i * (aw + gap), y, aw, ah);
            if (GrayIconBtn(A(0), HarassmentTextures.Discipline, "Discipline - punish; deepens conditioning through fear, hurts mood. (Interactive session if enabled in settings.)", hasOwner))
            {
                if (RimJobWorldSexualHarassmentMod.Settings?.interactiveTraining ?? false)
                    Find.WindowStack.Add(new Window_TrainingSession(owner, pet, prof));
                else HarassmentEngine.StartDiscipline(owner, pet);
            }
            if (GrayIconBtn(A(1), HarassmentTextures.Reward, "Reward - lifts mood, reinforces submission.", hasOwner)) HarassmentEngine.StartReward(owner, pet);
            if (GrayIconBtn(A(2), HarassmentTextures.Summon, "Come here now.", hasOwner)) HarassmentEngine.ComeHere(owner, pet);
            if (GrayIconBtn(A(3), HarassmentTextures.Parade, "Parade - grows notoriety, humiliates them.", hasOwner)) HarassmentEngine.DepthStartParade(owner, pet);
            if (GrayIconBtn(A(4), HarassmentTextures.Shock, collared ? "Shock collar - stun and deepen conditioning." : "Shock (needs a control collar).", collared)) { HarassmentEngine.ShockCollar(pet); HarassmentEngine.SetControlCooldown(pet); }
            if (GrayIconBtn(A(5), HarassmentTextures.AutoService, "Whore out - service a nearby visitor for silver.", hasOwner)) HarassmentEngine.StartWhore(owner, pet);
            y += ah + gap + 3f;

            float tw = (rect.width - gap * 5f) / 6f; float th = 30f;
            Rect T(int i) => new Rect(rect.x + i * (tw + gap), y, tw, th);
            if (GrayIconToggle(T(0), HarassmentTextures.Follow, "Follow: keep " + pet.LabelShort + " at the owner's side.", prof.followOwner && hasOwner) && hasOwner)
            { prof.followOwner = !prof.followOwner; if (prof.followOwner) { prof.stayCell = IntVec3.Invalid; prof.ownerId = owner.thingIDNumber; HarassmentEngine.EnsureOwnerRelation(owner, pet); } }
            if (GrayIconToggle(T(1), HarassmentTextures.KeepNaked, "Keep naked: strip to just locked devices.", prof.forceNudity))
            { prof.forceNudity = !prof.forceNudity; if (prof.forceNudity) HarassmentEngine.StripToBondage(pet); }
            var asR = T(2);
            if (hasOwner && Event.current.button == 1 && Event.current.type == EventType.MouseDown && Mouse.IsOver(asR))
            {
                Event.current.Use();
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Who to serve: " + HarassmentEngine.ServiceGroupLabel(prof.serviceTargetMode), () => Find.WindowStack.Add(new FloatMenu(HarassmentEngine.BuildServiceGroupMenu(prof)))),
                    new FloatMenuOption("Act: " + HarassmentEngine.ServiceActLabel(prof.serviceInteraction), () => Find.WindowStack.Add(new FloatMenu(HarassmentEngine.BuildServiceActMenu(prof)))),
                }));
            }
            if (GrayIconToggle(asR, HarassmentTextures.AutoService, "Auto-service periodically (needs Allow needs off). Right-click to choose who and which act.", prof.autoService && hasOwner) && hasOwner)
            { prof.autoService = !prof.autoService; if (prof.autoService) { prof.ownerId = owner.thingIDNumber; HarassmentEngine.EnsureOwnerRelation(owner, pet); prof.controlCooldownTick = Find.TickManager.TicksGame; } }
            if (GrayIconToggle(T(3), HarassmentTextures.Command, "Allow needs: sleep, eat and drink freely.", prof.allowNeeds))
                prof.allowNeeds = !prof.allowNeeds;
            if (GrayIconToggle(T(4), HarassmentTextures.Reward, "Auto-reward periodically - builds rapport.", prof.autoReward))
                prof.autoReward = !prof.autoReward;
            if (GrayIconToggle(T(5), HarassmentTextures.Discipline, "Auto-discipline periodically - deepens conditioning.", prof.autoDiscipline))
                prof.autoDiscipline = !prof.autoDiscipline;
        }

        // Nothing selected: the stage becomes a market podium (a clicked market pawn), else a prompt.
        private void DrawMarketStage(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, StageBg);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            DrawStageCorners(rect, new Color(0.72f, 0.66f, 0.5f));
            var gc = GameComponent_Harassment.Instance;
            if (_marketPreview != null && (gc?.market == null || !gc.market.Contains(_marketPreview))) _marketPreview = null;
            if (_marketPreview == null) { Empty(rect, "Select a pet on the left, or browse the market \u2192"); return; }

            var e = _marketPreview; var pawn = e.pawn;
            if (pawn == null) { _marketPreview = null; return; }
            var close = new Rect(rect.xMax - 26f, rect.y + 4f, 22f, 22f);
            if (GrayButton(close, "\u00d7", true, "Back to the market.")) { _marketPreview = null; return; }

            const float nameH = 46f, buyH = 30f;
            var dollArea = new Rect(rect.x, rect.y + 8f, rect.width, rect.height - nameH - buyH - 16f);
            float frameW = Mathf.Min(rect.width * 0.5f, dollArea.height * 0.58f);
            var frame = new Rect(dollArea.center.x - frameW / 2f, dollArea.y, frameW, dollArea.height);
            DrawStageDoll(frame, pawn);
            DrawHotspot(rect, frame, 0.50f, 0.47f, true, new Color(0.72f, 0.66f, 0.5f), "Virgin", VirginStr(pawn), null);
            DrawHotspot(rect, frame, 0.52f, 0.62f, false, TrustColor, "Sex drive", (int)(HarassmentEngine.SexDrive01(pawn) * 100f) + "%", null);
            DrawHotspot(rect, frame, 0.48f, 0.78f, true, CondColor, "Conditioning", SusceptLabel(HarassmentEngine.PreviewSusceptibility(pawn)), null);

            float ny = dollArea.yMax + 4f;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(rect.x, ny, rect.width, 30f), pawn.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(rect.x, ny + 28f, rect.width, SmallH), (int)pawn.ageTracker.AgeBiologicalYears + "y " + pawn.gender.GetLabel() + "  \u2022  " + (e.role >= 0 && e.role < RoleNames.Length ? RoleNames[e.role] : "pet"));
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            var bS = new Rect(rect.x + rect.width * 0.14f, rect.yMax - buyH + 2f, rect.width * 0.34f, 24f);
            if (PriceButton(bS, ThingDefOf.Silver.uiIcon, e.priceSilver + " silver") && gc != null) { gc.BuyMarketPawn(e, false); _marketPreview = null; return; }
            if (e.goodwillFaction != null)
            {
                var bG = new Rect(rect.x + rect.width * 0.52f, rect.yMax - buyH + 2f, rect.width * 0.34f, 24f);
                if (PriceButton(bG, e.goodwillFaction.def.FactionIcon, e.goodwillCost + " goodwill", "Goodwill with " + e.goodwillFaction.Name) && gc != null) { gc.BuyMarketPawn(e, true); _marketPreview = null; return; }
            }
        }

        // Nothing selected: the right column shows the market list + circulating photos + colony events.
        private void DrawColonyOps(Rect rect)
        {
            const float gap = 8f;
            float marketH = rect.height * 0.5f;
            DrawMarketPanel(new Rect(rect.x, rect.y, rect.width, marketH));
            float restTop = rect.y + marketH + gap;
            float half = (rect.yMax - restTop - gap) / 2f;
            DrawPhotoGallery(new Rect(rect.x, restTop, rect.width, half));
            DrawColonyEvents(new Rect(rect.x, rect.yMax - half, rect.width, half));
        }

        // Card-boxed tab strip with the suite accent as a 2px LEFT bar on the active tab (deferred switch = IMGUI-safe).
        private void DrawAccentTabs(Rect row, string[] names, ref int sel)
        {
            float w = row.width / names.Length; int clicked = -1;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(row.x + i * w, row.y, w - 4f, row.height);
                bool s = sel == i;
                Widgets.DrawBoxSolid(r, s ? ModernStyle.PanelBG : new Color(ModernStyle.BGD.r, ModernStyle.BGD.g, ModernStyle.BGD.b, 0.65f));
                if (s) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 2f, r.height), ModernStyle.Accent);
                GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                if (!s && Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.05f));
                Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny;
                GUI.color = s ? new Color(0.90f, 0.90f, 0.92f) : ModernStyle.TextDim;
                Widgets.Label(r, names[i]);
                GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(r)) clicked = i;
            }
            if (clicked >= 0) sel = clicked;
        }

        // Flat gray Modern-Suite button: BGL fill, accent-tinted hover, near-white centered label.
        private static bool GrayButton(Rect r, string label, bool enabled = true, string tip = null)
        {
            Color fill = !enabled ? ModernStyle.PanelBG
                : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.14f) : ModernStyle.BGL;
            Widgets.DrawBoxSolid(r, fill);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny;
            GUI.color = enabled ? new Color(0.89f, 0.89f, 0.89f) : ModernStyle.TextDim;
            Widgets.Label(r, (label ?? "").Truncate(r.width - 8f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        // Gray toggle: 2px accent LEFT bar + faint accent fill when on.
        private static bool GrayToggle(Rect r, string label, bool on, string tip = null)
        {
            Color fill = on ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.16f)
                : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.10f) : ModernStyle.PanelBG;
            Widgets.DrawBoxSolid(r, fill);
            if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 2f, r.height), ModernStyle.Accent);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Tiny;
            GUI.color = on ? ModernStyle.Accent : new Color(0.82f, 0.82f, 0.86f);
            Widgets.Label(new Rect(r.x + 8f, r.y, r.width - 10f, r.height), (label ?? "").Truncate(r.width - 12f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // Gray button with a left-aligned icon (command cluster style).
        private static bool GrayButton(Rect r, Texture2D icon, string label, bool enabled = true, string tip = null)
        {
            Color fill = !enabled ? ModernStyle.PanelBG
                : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.14f) : ModernStyle.BGL;
            Widgets.DrawBoxSolid(r, fill);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            float ix = r.x + 5f;
            if (icon != null)
            {
                GUI.color = enabled ? new Color(0.86f, 0.86f, 0.9f) : ModernStyle.TextDim;
                GUI.DrawTexture(new Rect(ix, r.center.y - 8f, 16f, 16f), icon);
                GUI.color = Color.white; ix += 20f;
            }
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Tiny;
            GUI.color = enabled ? new Color(0.89f, 0.89f, 0.89f) : ModernStyle.TextDim;
            Widgets.Label(new Rect(ix, r.y, r.xMax - ix - 3f, r.height), (label ?? "").Truncate(r.xMax - ix - 3f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        // Gray toggle with a left-aligned icon: accent bar + accent icon/label when on.
        private static bool GrayToggle(Rect r, Texture2D icon, string label, bool on, string tip = null)
        {
            Color fill = on ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.16f)
                : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.10f) : ModernStyle.PanelBG;
            Widgets.DrawBoxSolid(r, fill);
            if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 2f, r.height), ModernStyle.Accent);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            float ix = r.x + 6f;
            Color fg = on ? ModernStyle.Accent : new Color(0.82f, 0.82f, 0.86f);
            if (icon != null)
            {
                GUI.color = fg;
                GUI.DrawTexture(new Rect(ix, r.center.y - 8f, 16f, 16f), icon);
                GUI.color = Color.white; ix += 20f;
            }
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Tiny; GUI.color = fg;
            Widgets.Label(new Rect(ix, r.y, r.xMax - ix - 4f, r.height), (label ?? "").Truncate(r.xMax - ix - 4f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // ── Left: the pet-card list (filter + sort dropdown + scrollable cards). Always visible. ──
        private void DrawPetList(Rect rect)
        {
            var pets = AllPets();

            var si = new Rect(rect.x, rect.y + 5f, 16f, 16f);
            GUI.color = ModernStyle.TextDim; GUI.DrawTexture(si, HarassmentTextures.Search); GUI.color = Color.white;
            var sortBtn = new Rect(rect.xMax - 28f, rect.y, 28f, 26f);
            var fb = new Rect(si.xMax + 4f, rect.y + 1f, sortBtn.x - si.xMax - 10f, 24f);
            _haremFilter = Widgets.TextField(fb, _haremFilter ?? "");
            DrawSortDropdown(sortBtn);

            var listRect = new Rect(rect.x, rect.y + 32f, rect.width, rect.yMax - (rect.y + 32f));
            if (pets.Count == 0) { Empty(listRect, "No collared pets or owned slaves yet."); return; }

            var shown = FilterSortPets(pets);
            const float cardH = 114f, gap = 8f;
            float availW = listRect.width - 18f;
            var view = new Rect(0f, 0f, availW, shown.Count * (cardH + gap));
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listRect, ref _overviewScroll, view);
            for (int i = 0; i < shown.Count; i++)
                DrawPetCard(new Rect(0f, i * (cardH + gap), availW, cardH), shown[i]);
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // ── Right (nothing selected): colony view - summary, pet market, photos, recent events, bulk bar. ──
        private void DrawColonyPanel(Rect rect)
        {
            var pets = AllPets();
            DrawHaremSummary(new Rect(rect.x, rect.y, rect.width, 22f), pets);
            const float bulkH = 32f;
            float y = rect.y + 30f;
            var bodyRect = new Rect(rect.x, y, rect.width, rect.yMax - y - bulkH);
            var gc = GameComponent_Harassment.Instance;
            if (_marketPreview != null && (gc?.market == null || !gc.market.Contains(_marketPreview))) _marketPreview = null;
            if (_marketPreview != null)
            {
                // A clicked market pawn opens full-width, just like an owned pet's detail.
                DrawMarketPreview(bodyRect, _marketPreview);
            }
            else
            {
                // Single full-width column: market on top, circulating photos + recent events stacked below.
                float marketH = bodyRect.height * 0.56f;
                DrawMarketPanel(new Rect(bodyRect.x, bodyRect.y, bodyRect.width, marketH));
                float restTop = bodyRect.y + marketH + 10f;
                float half = (bodyRect.yMax - restTop - 10f) / 2f;
                DrawPhotoGallery(new Rect(bodyRect.x, restTop, bodyRect.width, half));
                DrawColonyEvents(new Rect(bodyRect.x, bodyRect.yMax - half, bodyRect.width, half));
            }
            DrawBulkBar(new Rect(rect.x, rect.yMax - bulkH + 4f, rect.width, 26f), pets);
        }

        private Vector2 _marketScroll;
        private Vector2 _previewBarScroll;
        private MarketEntry _marketPreview;
        private MarketEntry _previewSxEntry;
        private SexAttributes _previewSxCached;
        // Pet market: buy pets with silver or faction goodwill; they arrive by drop pod, key to the strongest/leader.
        private void DrawMarketPanel(Rect card)
        {
            ModernStyle.DrawCard(card);
            var inner = card.ContractedBy(8f);
            var gc = GameComponent_Harassment.Instance;
            float hy = MiniHeader(inner, inner.y, "Pet market");
            if (gc != null && gc.marketRefreshTick > 0)
            {
                int left = System.Math.Max(0, gc.marketRefreshTick - Find.TickManager.TicksGame);
                Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim; Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, Mathf.Max(18f, TinyH)), "restocks in " + left.ToStringTicksToPeriod());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            var listRect = new Rect(inner.x, hy, inner.width, inner.yMax - hy);
            var market = gc?.market;
            if (market == null || market.Count == 0) { Empty(listRect, "No pets for sale. The market restocks weekly."); return; }

            float rowH = Mathf.Max(60f, 28f + 2f * TinyH);
            var view = new Rect(0f, 0f, listRect.width - 16f, market.Count * rowH);
            MarketEntry buy = null; bool buyGw = false;
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listRect, ref _marketScroll, view);
            float ry = 0f;
            for (int i = 0; i < market.Count; i++)
            {
                var e = market[i];
                var row = new Rect(0f, ry, view.width, rowH - 4f); ry += rowH;
                if (e?.pawn == null) continue;
                if (i % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                var port = new Rect(row.x + 2f, row.y + 2f, 42f, 42f);
                DrawPortrait(port, e.pawn);
                float tx = port.xMax + 6f, tw = row.width - (port.xMax + 6f) - 86f;
                Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(tx, row.y + 3f, tw, SmallH), e.pawn.LabelShortCap.Truncate(tw));
                Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim;
                string sub = (int)e.pawn.ageTracker.AgeBiologicalYears + "y " + e.pawn.gender.GetLabel()
                    + "  \u2022  " + (e.role >= 0 && e.role < RoleNames.Length ? RoleNames[e.role] : "pet");
                Widgets.Label(new Rect(tx, row.y + 2f + SmallH, tw, TinyH), sub.Truncate(tw));
                GUI.color = new Color(0.72f, 0.66f, 0.5f);
                Widgets.Label(new Rect(tx, row.y + 2f + SmallH + TinyH, tw, TinyH), (VirginStr(e.pawn) == "yes" ? "virgin  \u2022  " : "") + "drive " + (int)(HarassmentEngine.SexDrive01(e.pawn) * 100f) + "%");
                Text.Font = GameFont.Small; GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(row, MarketTip(e));
                if (PriceButton(new Rect(row.xMax - 82f, row.y + 4f, 80f, 22f), ThingDefOf.Silver.uiIcon, e.priceSilver.ToString(), e.priceSilver + " silver")) { buy = e; buyGw = false; }
                if (e.goodwillFaction != null && PriceButton(new Rect(row.xMax - 82f, row.y + 30f, 80f, 22f), e.goodwillFaction.def.FactionIcon, e.goodwillCost.ToString(), e.goodwillCost + " goodwill with " + e.goodwillFaction.Name)) { buy = e; buyGw = true; }
                if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 86f, row.height))) _marketPreview = e;   // click the row to preview
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
            if (buy != null) gc.BuyMarketPawn(buy, buyGw);
        }

        private static string MarketTip(MarketEntry e)
        {
            if (e?.pawn == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(e.pawn.LabelShortCap + " - " + (int)e.pawn.ageTracker.AgeBiologicalYears + " years old");
            sb.AppendLine("Arrives by drop pod already collared and broken in; the key goes to your strongest colonist or leader.");
            sb.Append("Buy with " + e.priceSilver + " silver, or " + e.goodwillCost + " goodwill with "
                + (e.goodwillFaction != null ? e.goodwillFaction.Name : "a faction") + ".");
            return sb.ToString();
        }

        private SexAttributes PreviewSx(MarketEntry e)
        {
            if (e != _previewSxEntry || _previewSxCached == null)
            {
                _previewSxEntry = e;
                _previewSxCached = new SexAttributes();
                try { _previewSxCached.SeedFrom(e.pawn); } catch { }
            }
            return _previewSxCached;
        }

        // Market-pawn preview: replaces the photos/events panels with the pawn's sex info + attributes.
        private void DrawMarketPreview(Rect rect, MarketEntry e)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(10f);
            var pawn = e?.pawn;
            if (pawn == null) { Empty(inner, "Pet no longer available."); return; }
            var gc = GameComponent_Harassment.Instance;

            var port = new Rect(inner.x, inner.y, 64f, 64f);
            DrawPortrait(port, pawn);
            var close = new Rect(inner.xMax - 24f, inner.y, 22f, 22f);
            TooltipHandler.TipRegion(close, "Back to the market.");
            if (Widgets.ButtonText(close, "\u00d7")) { _marketPreview = null; return; }
            Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(port.xMax + 10f, inner.y, close.x - port.xMax - 14f, 28f), pawn.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(port.xMax + 10f, inner.y + 30f, inner.width - port.width - 20f, 20f),
                (int)pawn.ageTracker.AgeBiologicalYears + "y " + pawn.gender.GetLabel() + "  \u2022  " + (e.role >= 0 && e.role < RoleNames.Length ? RoleNames[e.role] : "pet"));
            GUI.color = Color.white;

            var bS = new Rect(port.xMax + 10f, inner.y + 52f, 108f, 22f);
            if (PriceButton(bS, ThingDefOf.Silver.uiIcon, e.priceSilver + " silver") && gc != null) { gc.BuyMarketPawn(e, false); _marketPreview = null; return; }
            if (e.goodwillFaction != null)
            {
                var bG = new Rect(bS.xMax + 6f, inner.y + 52f, 130f, 22f);
                if (PriceButton(bG, e.goodwillFaction.def.FactionIcon, e.goodwillCost + " goodwill", "Goodwill with " + e.goodwillFaction.Name) && gc != null) { gc.BuyMarketPawn(e, true); _marketPreview = null; return; }
            }

            float y = inner.y + 84f;
            y = MiniHeader(inner, y, "At a glance");
            if (ModsConfig.BiotechActive && pawn.genes != null) DrawGlance(inner, ref y, "Xenotype", pawn.genes.XenotypeLabelCap);
            DrawGlance(inner, ref y, "Top skills", TopSkillsStr(pawn));
            DrawGlance(inner, ref y, "Virgin", VirginStr(pawn));
            DrawGlance(inner, ref y, "Sex drive", (int)(HarassmentEngine.SexDrive01(pawn) * 100f) + "%");
            DrawGlance(inner, ref y, "Best sex type", HarassmentEngine.BestSexType(pawn));
            DrawGlance(inner, ref y, "Genitals", GenitalStr(pawn));
            DrawGlance(inner, ref y, "Conditioning", SusceptLabel(HarassmentEngine.PreviewSusceptibility(pawn)));
            y += 6f;
            y = MiniHeader(inner, y, "Sexual profile");
            var barsOuter = new Rect(inner.x, y, inner.width, inner.yMax - y);
            var barsView = new Rect(0f, 0f, barsOuter.width - 16f, 310f);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(barsOuter, ref _previewBarScroll, barsView);
            SexualityPanelDrawer.DrawCompact(new Rect(0f, 0f, barsView.width, 310f), pawn, PreviewSx(e));
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        private static void DrawGlance(Rect inner, ref float y, string label, string val)
        {
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(inner.x, y, 110f, TinyH), label);
            GUI.color = ModernStyle.Body; Widgets.Label(new Rect(inner.x + 112f, y, inner.width - 112f, TinyH), val);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += TinyH + 1f;
        }

        private static string VirginStr(Pawn p) { var v = HarassmentEngine.IsVirgin(p); return v == null ? "unknown" : (v.Value ? "yes" : "no"); }

        private static string GenitalStr(Pawn p)
        {
            var parts = new List<string>();
            if (SexAttributes.HasVagina(p)) parts.Add("vagina");
            if (SexAttributes.HasPenis(p)) parts.Add("penis");
            if (SexAttributes.HasAnus(p)) parts.Add("anus");
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        private static string SusceptLabel(float f) =>
            f >= 1.8f ? "breaks very easily" : f >= 1.3f ? "breaks easily" : f >= 0.85f ? "average" : f >= 0.6f ? "resistant" : "very resistant";

        // A framed button with a small icon (silver coin / faction crest) on the left and text on the right.
        private static bool PriceButton(Rect r, Texture2D icon, string text, string tip = null)
        {
            Widgets.DrawBoxSolid(r, ModernStyle.PanelBG);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            if (icon != null) GUI.DrawTexture(new Rect(r.x + 3f, r.y + (r.height - 16f) / 2f, 16f, 16f), icon);
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Tiny; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(r.x + 22f, r.y, r.width - 24f, r.height), text);
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // Top 3 non-disabled vanilla skills by level, e.g. "Shooting 8, Melee 6, Crafting 5".
        private static string TopSkillsStr(Pawn p)
        {
            if (p?.skills?.skills == null) return "unknown";
            var skills = new List<SkillRecord>(p.skills.skills);
            skills.Sort((a, b) => b.Level.CompareTo(a.Level));
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < skills.Count && n < 3; i++)
            {
                if (skills[i].TotallyDisabled) continue;
                if (n > 0) sb.Append(", ");
                sb.Append(skills[i].def.skillLabel.CapitalizeFirst() + " " + skills[i].Level);
                n++;
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        // ── Colony sidebar: circulating-photo gallery (top) + aggregated recent events (bottom) ──
        private void DrawColonySidebar(Rect rect)
        {
            float half = (rect.height - 12f) / 2f;
            DrawPhotoGallery(new Rect(rect.x, rect.y, rect.width, half));
            DrawColonyEvents(new Rect(rect.x, rect.yMax - half, rect.width, half));
        }

        // Every scandalous photo in circulation. Click a row to jump to the subject; burn to pull it out of circulation.
        private struct PhotoEntry { public Pawn subject; public string holder; public string lore; public Thing thing; public int circIdx; }

        // Every known scandalous photo: physical copies (ground + inventories, across maps) PLUS off-map circulating rumors.
        private void DrawPhotoGallery(Rect card)
        {
            ModernStyle.DrawCard(card);
            var inner = card.ContractedBy(8f);
            float y = MiniHeader(inner, inner.y, "Photos");
            var gc = GameComponent_Harassment.Instance;
            var listRect = new Rect(inner.x, y, inner.width, inner.yMax - y);

            var photos = new List<PhotoEntry>();
            foreach (var map in Find.Maps)
            {
                var ground = map.listerThings.ThingsOfDef(RJWSH_ThingDefOf.RJWSH_ScandalousPhoto);
                for (int i = 0; i < ground.Count; i++)
                {
                    var c = ground[i].TryGetComp<CompScandalousPhoto>();
                    if (c != null) photos.Add(new PhotoEntry { subject = c.subject, holder = "On the ground", thing = ground[i], circIdx = -1 });
                }
                var mpawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < mpawns.Count; i++)
                {
                    var invc = mpawns[i].inventory?.innerContainer; if (invc == null) continue;
                    for (int j = 0; j < invc.Count; j++)
                    {
                        if (invc[j].def != RJWSH_ThingDefOf.RJWSH_ScandalousPhoto) continue;
                        var c = invc[j].TryGetComp<CompScandalousPhoto>();
                        if (c != null) photos.Add(new PhotoEntry { subject = c.subject, holder = "Held by " + mpawns[i].LabelShortCap, thing = invc[j], circIdx = -1 });
                    }
                }
            }
            if (gc?.circulatingPhotos != null)
                for (int i = 0; i < gc.circulatingPhotos.Count; i++)
                {
                    var cp = gc.circulatingPhotos[i]; if (cp == null) continue;
                    photos.Add(new PhotoEntry { subject = cp.subject, holder = cp.holder ?? "In circulation", lore = cp.lore, thing = null, circIdx = i });
                }
            if (photos.Count == 0) { Empty(listRect, "No photos exist yet."); return; }

            float rowH = Mathf.Max(42f, SmallH + TinyH + 8f);
            var view = new Rect(0f, 0f, listRect.width - 16f, photos.Count * rowH);
            int burnCirc = -1; Thing burnThing = null;
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listRect, ref _galleryScroll, view);
            float ry = 0f;
            for (int i = 0; i < photos.Count; i++)
            {
                var pe = photos[i];
                var row = new Rect(0f, ry, view.width, rowH - 3f); ry += rowH;
                if (i % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                var port = new Rect(row.x + 3f, row.y + 4f, 31f, 31f);
                if (pe.subject != null) DrawPortrait(port, pe.subject); else Widgets.DrawBoxSolid(port, new Color(0.2f, 0.2f, 0.22f));
                float tx = port.xMax + 6f, tw = row.xMax - tx - 26f;
                Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(tx, row.y + 2f, tw, SmallH), (pe.subject != null ? pe.subject.LabelShortCap : "unknown").Truncate(tw));
                Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim;
                Widgets.Label(new Rect(tx, row.y + 1f + SmallH, tw, TinyH), (pe.holder ?? "unknown").Truncate(tw));
                Text.Font = GameFont.Small; GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                if (!string.IsNullOrEmpty(pe.lore)) TooltipHandler.TipRegion(row, pe.lore);
                var jumpRect = new Rect(row.x, row.y, row.width - 26f, row.height);
                if (Widgets.ButtonInvisible(jumpRect) && pe.subject != null && pe.subject.Spawned) CameraJumper.TryJumpAndSelect(pe.subject);
                if (IconButton(new Rect(row.xMax - 22f, row.y + 6f, 18f, 18f), HarassmentTextures.BurnPhoto, "Destroy this photo."))
                { if (pe.thing != null) burnThing = pe.thing; else burnCirc = pe.circIdx; }
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
            if (burnThing != null && !burnThing.Destroyed) burnThing.Destroy();
            if (burnCirc >= 0 && gc?.circulatingPhotos != null && burnCirc < gc.circulatingPhotos.Count)
            { gc.circulatingPhotos.RemoveAt(burnCirc); gc.notoriety = Mathf.Max(0, gc.notoriety - 1); }
        }

        // Recent conditioning events across the whole harem, newest first. Click a row to open that pet.
        private void DrawColonyEvents(Rect card)
        {
            ModernStyle.DrawCard(card);
            var inner = card.ContractedBy(8f);
            float y = MiniHeader(inner, inner.y, "Recent harem events");
            var listRect = new Rect(inner.x, y, inner.width, inner.yMax - y);

            var rows = new List<System.ValueTuple<Pawn, CondEvent>>();
            var pets = AllPets();
            for (int i = 0; i < pets.Count; i++)
            {
                var pr = Prof(pets[i]);
                if (pr?.condEvents == null) continue;
                for (int k = 0; k < pr.condEvents.Count; k++) rows.Add(new System.ValueTuple<Pawn, CondEvent>(pets[i], pr.condEvents[k]));
            }
            if (rows.Count == 0) { Empty(listRect, "No conditioning events yet."); return; }
            rows.Sort((a, b) => b.Item2.tick.CompareTo(a.Item2.tick));
            if (rows.Count > 60) rows.RemoveRange(60, rows.Count - 60);

            int now = Find.TickManager.TicksGame;
            const float rowH = 40f;
            var view = new Rect(0f, 0f, listRect.width - 16f, rows.Count * rowH);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listRect, ref _eventsScroll, view);
            float ry = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var pet = rows[i].Item1; var e = rows[i].Item2;
                var row = new Rect(0f, ry, view.width, rowH - 2f);
                if (i % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.DrawBoxSolid(new Rect(row.x + 4f, row.y + 7f, 7f, 7f), e.condDelta >= 0f ? new Color(0.45f, 0.85f, 0.5f) : new Color(0.9f, 0.42f, 0.42f));
                Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(row.x + 16f, row.y + 1f, row.width - 74f, 18f), (pet.LabelShortCap + ": " + e.label).Truncate(row.width - 74f));
                Text.Font = GameFont.Tiny; GUI.color = new Color(1f, 1f, 1f, 0.5f); Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(row.xMax - 60f, row.y + 3f, 56f, 16f), AgoStr(now - e.tick) + " ago");
                Text.Anchor = TextAnchor.UpperLeft;
                float dx = row.x + 16f;
                if (e.condDelta != 0f) { GUI.color = CondColor; Widgets.Label(new Rect(dx, row.y + 20f, 110f, 16f), "cond " + Signed(e.condDelta)); dx += 92f; }
                if (e.rapDelta != 0f) { GUI.color = TrustColor; Widgets.Label(new Rect(dx, row.y + 20f, 100f, 16f), "rapport " + Signed(e.rapDelta)); }
                GUI.color = Color.white; Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { _selected = pet; _detailTab = 0; }
                ry += rowH;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // Sort as an icon dropdown (click the current field again to flip direction).
        private void DrawSortDropdown(Rect r)
        {
            string cur = SortNames[_sortMode] + (_sortDesc ? " \u25be" : " \u25b4");
            if (IconButton(r, HarassmentTextures.Sort, "Sort by: " + cur))
            {
                var opts = new List<FloatMenuOption>();
                for (int i = 0; i < SortNames.Length; i++)
                {
                    int si = i;
                    string tick = _sortMode == i ? (_sortDesc ? "\u25be " : "\u25b4 ") : "    ";
                    opts.Add(new FloatMenuOption(tick + SortNames[i], delegate
                    {
                        if (_sortMode == si) _sortDesc = !_sortDesc; else { _sortMode = si; _sortDesc = false; }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        // One pet card (vertical list row): portrait (+ star), pin toggle, name/owner/role/situation, icon bars, risk dot.
        private void DrawPetCard(Rect card, Pawn pet)
        {
            var prof = Prof(pet);
            bool selected = _selected == pet;
            ModernStyle.DrawCard(card);
            if (selected) { GUI.color = HeaderCol; Widgets.DrawBox(card, 2); GUI.color = Color.white; }
            else if (Mouse.IsOver(card)) Widgets.DrawHighlight(card);
            var inner = card.ContractedBy(8f);

            var port = new Rect(inner.x, inner.y, 44f, 44f);
            DrawPortrait(port, pet);
            if (prof != null && prof.isHeadGirl && HarassmentTextures.Star != null)
            {
                var star = new Rect(port.x - 3f, port.y - 3f, 15f, 15f);
                Widgets.DrawBoxSolid(star.ExpandedBy(1f), new Color(0f, 0f, 0f, 0.55f));
                GUI.DrawTexture(star, HarassmentTextures.Star);
                TooltipHandler.TipRegion(star, "Head girl: enforces this owner's harem.");
            }

            // Pin toggle (top-right) with the risk dot below it.
            var pin = new Rect(inner.xMax - 18f, inner.y, 18f, 18f);
            bool pinned = prof != null && prof.dashboardPinned;
            GUI.color = pinned ? HeaderCol : new Color(0.5f, 0.5f, 0.55f);
            if (HarassmentTextures.Pin != null) GUI.DrawTexture(pin, HarassmentTextures.Pin);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(pin, pinned ? "Unpin from the top of the list." : "Pin to the top of the list.");
            bool pinClicked = Widgets.ButtonInvisible(pin);
            if (pinClicked && prof != null) prof.dashboardPinned = !prof.dashboardPinned;

            float risk = RiskScore(pet);
            var dot = new Rect(inner.xMax - 13f, inner.y + 24f, 8f, 8f);
            Widgets.DrawBoxSolid(dot, risk > 15f ? new Color(0.85f, 0.3f, 0.3f) : risk > 5f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.35f, 0.7f, 0.42f));
            TooltipHandler.TipRegion(new Rect(dot.x - 4f, dot.y - 4f, 16f, 16f), risk > 15f ? "At risk - may rebel or flee." : risk > 5f ? "Some instability." : "Stable.");

            float tx = port.xMax + 8f, tw = inner.xMax - (port.xMax + 8f) - 22f;
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(tx, inner.y, tw, 22f), pet.LabelShortCap.Truncate(tw));
            Pawn owner = HarassmentEngine.FindKeyHolderFor(pet);
            string role = (prof != null && prof.petRole > 0 && prof.petRole < RoleNames.Length) ? RoleNames[prof.petRole] : null;
            Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(tx, inner.y + 23f, tw, 16f), ((owner != null ? owner.LabelShortCap : "no owner") + (role != null ? "  \u2022  " + role : "")).Truncate(tw));
            GUI.color = new Color(0.72f, 0.66f, 0.5f);
            Widgets.Label(new Rect(tx, inner.y + 40f, tw, 16f), SituationLabel(pet, prof).Truncate(tw));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;

            float by = inner.y + 64f;
            float cond = prof != null ? prof.hypnosisLevel / 100f : 0f;
            float rap = prof != null ? prof.rapport / 100f : 0.5f;
            DrawMiniBar(new Rect(inner.x, by, inner.width, 13f), HarassmentTextures.CondIcon, cond, CondColor);
            DrawMiniBar(new Rect(inner.x, by + 18f, inner.width, 13f), HarassmentTextures.RapportIcon, rap, rap < 0.4f ? FearColor : TrustColor);

            bool cardClicked = Widgets.ButtonInvisible(card);
            if (!pinClicked && cardClicked) { _selected = selected ? null : pet; _detailTab = 0; }
        }

        // Compact bar with a tinted icon label (Cond / Rapport) instead of clipped text.
        private static void DrawMiniBar(Rect r, Texture2D icon, float pct, Color fill)
        {
            if (icon != null)
            {
                GUI.color = fill;
                GUI.DrawTexture(new Rect(r.x, r.y + (r.height - 13f) / 2f, 13f, 13f), icon);
                GUI.color = Color.white;
            }
            var bar = new Rect(r.x + 18f, r.y, r.width - 18f, r.height);
            Widgets.FillableBar(bar, Mathf.Clamp01(pct), SolidBar(fill), SolidBar(EmptyColor), false);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(1f, 1f, 1f, 0.92f);
            Widgets.Label(bar, ((int)(pct * 100f)) + "%");
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
        }

        // Right (a pet selected): header + card-style tabs. The list stays on the left; the X (or re-clicking the
        // pet's card) deselects.
        private void DrawDetailPanel(Rect body, Pawn pet)
        {
            var prof = GameComponent_Harassment.Instance?.GetProfile(pet);
            Pawn owner = HarassmentEngine.FindKeyHolderFor(pet);

            var port = new Rect(body.x, body.y, 56f, 56f);
            DrawPortrait(port, pet);
            if (prof != null && prof.isHeadGirl && HarassmentTextures.Star != null)
            {
                var star = new Rect(port.x - 3f, port.y - 3f, 18f, 18f);
                Widgets.DrawBoxSolid(star.ExpandedBy(1f), new Color(0f, 0f, 0f, 0.55f));
                GUI.DrawTexture(star, HarassmentTextures.Star);
                TooltipHandler.TipRegion(star, "Head girl: enforces this owner's harem.");
            }
            var close = new Rect(body.xMax - 26f, body.y + 2f, 24f, 24f);
            TooltipHandler.TipRegion(close, "Close this pet (back to the colony view).");
            if (Widgets.ButtonText(close, "\u00d7")) { _selected = null; return; }
            var jump = new Rect(close.x - 32f, body.y + 4f, 26f, 26f);
            TooltipHandler.TipRegion(jump, "Jump to and select this pawn.");
            if (Widgets.ButtonImage(jump, HarassmentTextures.GoTo)) CameraJumper.TryJumpAndSelect(pet);

            Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(port.xMax + 12f, body.y + 2f, jump.x - port.xMax - 18f, 30f), pet.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Widgets.Label(new Rect(port.xMax + 12f, body.y + 32f, body.xMax - port.xMax - 20f, 22f),
                (owner != null ? "Owned by " + owner.LabelShortCap : "No active owner") + "  \u2022  " + SituationLabel(pet, prof));
            GUI.color = Color.white;

            // Tab strip + content card.
            float ty = body.y + 64f;
            DrawPetTabs(new Rect(body.x, ty, body.width, 26f));
            var card = new Rect(body.x, ty + 30f, body.width, Mathf.Max(120f, body.yMax - (ty + 30f)));
            ModernStyle.DrawCard(card);
            var area = card.ContractedBy(12f);
            switch (_detailTab)
            {
                case 1: SexualityPanelDrawer.Draw(area, pet, ref _attrScroll); break;
                case 2: DrawConditioningTab(area, pet, prof); break;
                case 3: DrawScheduleTab(area, pet, prof); break;
                case 4: DrawSocialTab(area, pet); break;
                default: DrawOverviewTab(area, pet, prof, owner); break;
            }
        }

        private void DrawPetTabs(Rect row)
        {
            float w = row.width / PetTabs.Length;
            for (int i = 0; i < PetTabs.Length; i++)
            {
                var r = new Rect(row.x + i * w, row.y, w - 3f, row.height);
                bool sel = _detailTab == i;
                Widgets.DrawBoxSolid(r, sel ? ModernStyle.PanelBG : new Color(ModernStyle.BGD.r, ModernStyle.BGD.g, ModernStyle.BGD.b, 0.6f));
                if (sel) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 2f, r.width, 2f), HeaderCol);
                GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = sel ? HeaderCol : ModernStyle.TextDim;
                Widgets.Label(r, PetTabs[i]);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                if (Widgets.ButtonInvisible(r)) _detailTab = i;
            }
        }

        // Overview tab: big cond+rapport bars, a status line, then colony/world info (photos, circulation, visitor, reputation).
        private void DrawOverviewTab(Rect area, Pawn pet, PawnProfile prof, Pawn owner)
        {
            float y = area.y;
            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rap = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;
            DrawLabeledBar(new Rect(area.x, y, area.width, 24f), "Conditioned", cond, CondColor, CondTooltip(prof)); y += 28f;
            DrawLabeledBar(new Rect(area.x, y, area.width, 24f), "Rapport", rap, rap < 0.4f ? FearColor : TrustColor, RapportTooltip(prof)); y += 34f;

            string role = (prof != null && prof.petRole >= 0 && prof.petRole < RoleNames.Length) ? RoleNames[prof.petRole] : "None";
            string quota = (prof != null && prof.dailyQuota > 0) ? prof.servicesToday + "/" + prof.dailyQuota + " today" : "no quota";
            string earn = (prof != null && prof.lifetimeEarnings > 0) ? prof.lifetimeEarnings + "s earned" : "no earnings";
            Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(area.x, y, area.width, 18f), "Role: " + role + "      Quota: " + quota + "      " + earn
                + (prof != null && prof.isHeadGirl ? "      Head girl" : ""));
            GUI.color = Color.white; Text.Font = GameFont.Small; y += 26f;

            y = MiniHeader(area, y, "World");
            DrawWorldInfo(area, y, pet);
        }

        // Conditioning tab: focus + role radios (left) and the scribed conditioning/rapport history graph (right).
        private void DrawConditioningTab(Rect area, Pawn pet, PawnProfile prof)
        {
            if (prof == null) { Empty(area, "No pet data."); return; }
            float leftW = area.width * 0.44f;
            var leftR = new Rect(area.x, area.y, leftW, area.height);
            var rightR = new Rect(area.x + leftW + 16f, area.y, area.width - leftW - 16f, area.height);
            DrawConditioning(leftR, leftR.y, pet, prof);
            float ry = MiniHeader(rightR, rightR.y, "Conditioning history");
            DrawHistoryGraph(new Rect(rightR.x, ry, rightR.width, rightR.yMax - ry), prof);
        }

        // Schedule tab: DrawSchedule renders the 24h grid, type palette, quarters, daily service quota and head-girl toggle.
        private void DrawScheduleTab(Rect area, Pawn pet, PawnProfile prof)
        {
            if (prof == null) { Empty(area, "No pet data."); return; }
            DrawSchedule(new Rect(area.x, area.y, area.width, 150f), pet, prof);
            float hy = area.y + 156f;
            DrawSexHistory(new Rect(area.x, hy, area.width, area.yMax - hy), pet);
        }

        // Social tab: Interactions on the top half, Moodlets on the bottom half (no sub-tabs).
        private void DrawSocialTab(Rect area, Pawn pet)
        {
            float half = (area.height - 12f) / 2f;
            var top = new Rect(area.x, area.y, area.width, half);
            var bot = new Rect(area.x, area.yMax - half, area.width, half);
            float ty = MiniHeader(top, top.y, "Interactions");
            DrawInteractions(new Rect(top.x, ty, top.width, top.yMax - ty), pet);
            float by = MiniHeader(bot, bot.y, "Moodlets");
            DrawMoodlets(new Rect(bot.x, by, bot.width, bot.yMax - by), pet);
        }

        // Sex history summary (Schedule tab). Built from RJW's own data; a dedicated Sex History mod is not required.
        private void DrawSexHistory(Rect rect, Pawn pet)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(10f);
            float y = MiniHeader(inner, inner.y, "Sex history");
            var data = SexHistoryBridge.Read(pet);   // RJW Sexperience per-pawn history (null without it)
            if (data != null)
            {
                DrawGlance(inner, ref y, "Virgin", VirginStr(pet));
                DrawGlance(inner, ref y, "Total sex", data.totalSex.ToString());
                DrawGlance(inner, ref y, "Partners", data.partners.ToString());
                DrawGlance(inner, ref y, "Best sex type", string.IsNullOrEmpty(data.bestSextype) ? "none" : data.bestSextype);
                DrawGlance(inner, ref y, "Avg satisfaction", data.avgSat.ToString("0.0"));
                DrawGlance(inner, ref y, "Raped / been raped", data.raped + " / " + data.beenRaped);
                if (data.virginsTaken > 0) DrawGlance(inner, ref y, "Virgins taken", data.virginsTaken.ToString());
                if (!string.IsNullOrEmpty(data.recentPartner)) DrawGlance(inner, ref y, "Recent partner", data.recentPartner);
                if (!string.IsNullOrEmpty(data.mostPartner)) DrawGlance(inner, ref y, "Favorite partner", data.mostPartner);
                if (!string.IsNullOrEmpty(data.firstPartner)) DrawGlance(inner, ref y, "First partner", data.firstPartner);
            }
            else
            {
                var prof = Prof(pet);
                DrawGlance(inner, ref y, "Virgin", VirginStr(pet));
                DrawGlance(inner, ref y, "Sex drive", (int)(HarassmentEngine.SexDrive01(pet) * 100f) + "%");
                DrawGlance(inner, ref y, "Most used", MostUsedPart(pet, prof?.sex));
                DrawGlance(inner, ref y, "Logged encounters", EncounterCount(pet).ToString());
                y += 4f;
                Text.Font = GameFont.Tiny; GUI.color = ModernStyle.TextDim; Text.WordWrap = true;
                Widgets.Label(new Rect(inner.x, y, inner.width, 30f), "Install RJW Sexperience for detailed sex history.");
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
        }

        private static int EncounterCount(Pawn pet)
        {
            int n = 0;
            try
            {
                var all = Find.PlayLog.AllEntries;
                for (int i = all.Count - 1; i >= 0; i--)
                    if (all[i] != null && all[i].Concerns(pet) && IsRelevantInteraction(all[i], pet)) n++;
            }
            catch { }
            return n;
        }

        private static string MostUsedPart(Pawn pet, SexAttributes sx)
        {
            if (sx == null) return "unknown";
            float best = -1f; string label = "none yet";
            if (SexAttributes.HasVagina(pet) && sx.wearVaginal > best) { best = sx.wearVaginal; label = "vagina"; }
            if (SexAttributes.HasAnus(pet) && sx.wearAnal > best) { best = sx.wearAnal; label = "anus"; }
            if (SexAttributes.HasMouth(pet) && sx.wearOral > best) { best = sx.wearOral; label = "mouth"; }
            if (SexAttributes.HasPenis(pet) && sx.wearPenis > best) { best = sx.wearPenis; label = "penis"; }
            return best <= 0f ? "none yet" : label + " (" + (int)best + "% worn)";
        }

        private void DrawViewToggle(Rect row)
        {
            float w = row.width / ViewNames.Length;
            for (int i = 0; i < ViewNames.Length; i++)
            {
                var r = new Rect(row.x + i * w, row.y, w - 2f, row.height);
                bool sel = _view == i;
                Widgets.DrawBoxSolid(r, sel ? ModernStyle.PanelBG : new Color(ModernStyle.BGD.r, ModernStyle.BGD.g, ModernStyle.BGD.b, 0.6f));
                if (sel) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 2f, r.width, 2f), HeaderCol);
                GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = sel ? HeaderCol : ModernStyle.TextDim;
                Widgets.Label(r, ViewNames[i]);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                if (Widgets.ButtonInvisible(r)) _view = i;
            }
        }

        // ── Harem: colony-wide table with bulk role/focus assignment + schedule ──
        private List<Pawn> AllPets()
        {
            var list = new List<Pawn>();
            var groups = BuildGroups();
            for (int i = 0; i < groups.Count; i++)
                for (int j = 0; j < groups[i].pets.Count; j++)
                    if (!list.Contains(groups[i].pets[j])) list.Add(groups[i].pets[j]);
            return list;
        }

        private void DrawHarem(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, CardBg);
            DrawThinBorder(rect, CardBorder);
            var inner = rect.ContractedBy(8f);

            var pets = AllPets();
            if (pets.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(inner, "No collared pets or owned slaves yet. Condition a pawn to 90+ and lock a control collar on them, and they will appear here.");
                GUI.color = Color.white;
                return;
            }

            float y = inner.y;
            DrawHaremSummary(new Rect(inner.x, y, inner.width, 22f), pets);
            y += 26f;
            DrawHaremHeader(new Rect(inner.x, y, inner.width, 20f));
            y += 22f;

            var shown = FilterSortPets(pets);
            const float baseH = 40f, schedH = 160f, bulkH = 32f, rowH = baseH + schedH;
            var listRect = new Rect(inner.x, y, inner.width, inner.yMax - y - bulkH);
            var view = new Rect(0f, 0f, listRect.width - 18f, shown.Count * rowH);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listRect, ref _haremScroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                var pet = shown[i];
                float ry = i * rowH;
                var baseRow = new Rect(0f, ry, view.width, baseH - 2f);
                if (i % 2 == 1) Widgets.DrawBoxSolid(baseRow, new Color(1f, 1f, 1f, 0.03f));
                DrawHaremRow(baseRow, pet);
                // Schedule is always shown, one per pet (list scrolls when there are many).
                var sch = new Rect(2f, ry + baseH, view.width - 6f, schedH - 6f);
                Widgets.DrawBoxSolid(sch, new Color(1f, 1f, 1f, 0.05f));
                DrawSchedule(sch.ContractedBy(8f), pet, Prof(pet));
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();

            DrawBulkBar(new Rect(inner.x, inner.yMax - bulkH + 4f, inner.width, 26f), pets);
        }

        // ── Tranche A: summary + filter + sort + risk ──
        private static PawnProfile Prof(Pawn p) => GameComponent_Harassment.Instance?.GetProfileIfExists(p);

        // Higher = likelier to rebel/flee: fear-broken (low rapport), weakly conditioned, or AI-controlled.
        private static float RiskScore(Pawn p)
        {
            var pr = Prof(p); if (pr == null) return 0f;
            float risk = 0f;
            if (pr.rapport < 30f) risk += 30f - pr.rapport;
            if (pr.hypnosisLevel < 40f) risk += (40f - pr.hypnosisLevel) * 0.5f;
            if (pr.aiControlled) risk += 10f;
            return risk;
        }

        private void DrawHaremSummary(Rect rect, List<Pawn> all)
        {
            int n = all.Count; float ac = 0f, ar = 0f; int risk = 0, income = 0;
            for (int i = 0; i < n; i++) { var pr = Prof(all[i]); if (pr != null) { ac += pr.hypnosisLevel; ar += pr.rapport; income += pr.lifetimeEarnings; if (RiskScore(all[i]) > 15f) risk++; } }
            if (n > 0) { ac /= n; ar /= n; }
            int noto = GameComponent_Harassment.Instance?.notoriety ?? 0;
            string s = n + " pets    avg cond " + ac.ToString("0") + "%    avg rapport " + ar.ToString("0") + "%    notoriety " + noto
                       + (income > 0 ? "    earned " + income + "s" : "")
                       + (risk > 0 ? "    <color=#e06b6b>" + risk + " at risk</color>" : "");
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, rect.height), s);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        private List<Pawn> FilterSortPets(List<Pawn> pets)
        {
            var res = new List<Pawn>(pets);
            var f = _haremFilter?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(f))
                res = res.FindAll(p => p.LabelShortCap.ToLowerInvariant().Contains(f)
                    || (HarassmentEngine.FindKeyHolderFor(p)?.LabelShortCap.ToLowerInvariant().Contains(f) ?? false));
            System.Comparison<Pawn> cmp;
            switch (_sortMode)
            {
                case 1: cmp = (a, b) => (Prof(a)?.hypnosisLevel ?? 0f).CompareTo(Prof(b)?.hypnosisLevel ?? 0f); break;
                case 2: cmp = (a, b) => (Prof(a)?.rapport ?? 50f).CompareTo(Prof(b)?.rapport ?? 50f); break;
                case 3: cmp = (a, b) => string.Compare(HarassmentEngine.FindKeyHolderFor(a)?.LabelShortCap ?? "", HarassmentEngine.FindKeyHolderFor(b)?.LabelShortCap ?? "", System.StringComparison.OrdinalIgnoreCase); break;
                default: cmp = (a, b) => string.Compare(a.LabelShortCap, b.LabelShortCap, System.StringComparison.OrdinalIgnoreCase); break;
            }
            res.Sort(cmp);
            if (_sortDesc) res.Reverse();
            // Pinned pets float to the top (stable within each group), independent of sort + head girl.
            var pinned = new List<Pawn>(); var rest = new List<Pawn>();
            for (int i = 0; i < res.Count; i++) { if (Prof(res[i])?.dashboardPinned == true) pinned.Add(res[i]); else rest.Add(res[i]); }
            pinned.AddRange(rest);
            return pinned;
        }

        private void OpenPresetMenu(List<Pawn> pets)
        {
            var gc = GameComponent_Harassment.Instance;
            var opts = new List<FloatMenuOption>();
            if (_selected != null && Prof(_selected) != null)
                opts.Add(new FloatMenuOption("Save " + _selected.LabelShortCap + "'s setup as a preset", () => SavePresetFrom(_selected)));
            if (gc?.haremPresets != null)
                foreach (var preset in gc.haremPresets)
                {
                    var pr = preset;
                    opts.Add(new FloatMenuOption("Apply \"" + pr.name + "\" to all pets", () => { foreach (var p in pets) ApplyPreset(pr, p); }));
                    opts.Add(new FloatMenuOption("Delete \"" + pr.name + "\"", () => gc.haremPresets.Remove(pr)));
                }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("(select a pet in the Pets view to save a preset)", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void SavePresetFrom(Pawn pet)
        {
            var prof = Prof(pet); var gc = GameComponent_Harassment.Instance;
            if (prof == null || gc == null) return;
            if (gc.haremPresets == null) gc.haremPresets = new List<HaremPreset>();
            gc.haremPresets.Add(new HaremPreset
            {
                name = pet.LabelShortCap + "'s regimen",
                role = prof.petRole,
                focus = prof.trainFocus,
                schedule = prof.schedule != null ? new List<int>(prof.schedule) : null,
            });
            Messages.Message("Saved a harem preset from " + pet.LabelShortCap + ".", MessageTypeDefOf.TaskCompletion, false);
        }

        private static void ApplyPreset(HaremPreset pr, Pawn pet)
        {
            var prof = Prof(pet); if (prof == null || pr == null) return;
            HarassmentEngine.SetPetRole(HarassmentEngine.FindKeyHolderFor(pet), pet, pr.role);
            prof.trainFocus = pr.focus;
            prof.schedule = pr.schedule != null ? new List<int>(pr.schedule) : null;
        }

        private void DrawBulkBar(Rect rect, List<Pawn> pets)
        {
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(rect.x, rect.y, 88f, rect.height), "Bulk:");
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            Widgets.CheckboxLabeled(new Rect(rect.x + 44f, rect.y + 1f, 168f, rect.height - 2f), "Auto head girl", ref RimJobWorldSexualHarassmentMod.Settings.autoHeadGirl);
            TooltipHandler.TipRegion(new Rect(rect.x + 44f, rect.y, 168f, rect.height), "Automatically make the best-performing pet in each owner's harem the head girl. Off = set it manually per pet.");

            const float bs = 26f, gap = 6f;
            float bx = rect.xMax - bs;   // laid out right-to-left
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.Presets, "Presets: save the selected pet's setup, or apply / delete a saved regimen across the harem."))
                OpenPresetMenu(pets);
            bx -= bs + gap;
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.Needs, "Toggle allow-needs for every pet."))
            {
                bool anyOff = pets.Exists(p => { var pr = Prof(p); return pr != null && !pr.allowNeeds; });
                foreach (var p in pets) { var pr = Prof(p); if (pr != null) pr.allowNeeds = anyOff; }
            }
            bx -= bs + gap;
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.Parade, "Parade every pet now."))
                foreach (var p in pets) { var o = HarassmentEngine.FindKeyHolderFor(p); if (o != null) HarassmentEngine.DepthStartParade(o, p); }
            bx -= bs + gap;
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.Focus, "Set the conditioning focus for all pets."))
                OpenFocusMenu(key => { foreach (var p in pets) { var pr = Prof(p); if (pr != null) pr.trainFocus = key; } });
            bx -= bs + gap;
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.CollarIcon, "Set the role for all pets."))
                OpenRoleMenu(role => { foreach (var p in pets) HarassmentEngine.SetPetRole(HarassmentEngine.FindKeyHolderFor(p), p, role); });
            bx -= bs + gap;
            if (IconButton(new Rect(bx, rect.y, bs, bs), HarassmentTextures.DressUp, "Lock a device on every pet (bulk gear)."))
                OpenDeviceMenu(pets);
        }

        // Column fractions shared by the header and rows.
        private static readonly float[] ColX = { 0f, 0.20f, 0.335f, 0.455f, 0.575f, 0.66f, 0.79f, 0.895f, 0.945f };
        private void DrawHaremHeader(Rect r)
        {
            float W = r.width;
            Text.Font = GameFont.Tiny;
            // sortKey >= 0 makes the column header a sort toggle (click again to reverse).
            void H(float fx, float fw, string s, TextAnchor a, int sortKey)
            {
                var cell = new Rect(r.x + W * fx, r.y, W * fw, r.height);
                bool active = sortKey >= 0 && _sortMode == sortKey;
                GUI.color = active ? HeaderCol : ModernStyle.TextDim;
                Text.Anchor = a;
                Widgets.Label(cell, active ? s + (_sortDesc ? " v" : " ^") : s);
                GUI.color = Color.white;
                if (sortKey >= 0)
                {
                    if (Mouse.IsOver(cell)) Widgets.DrawHighlight(cell);
                    if (Widgets.ButtonInvisible(cell)) { if (_sortMode == sortKey) _sortDesc = !_sortDesc; else { _sortMode = sortKey; _sortDesc = false; } }
                }
            }
            H(ColX[0], 0.20f, "Pet", TextAnchor.MiddleLeft, 0);
            H(ColX[1], 0.13f, "Owner", TextAnchor.MiddleLeft, 3);
            H(ColX[2], 0.11f, "Cond", TextAnchor.MiddleCenter, 1);
            H(ColX[3], 0.11f, "Rapport", TextAnchor.MiddleCenter, 2);
            H(ColX[4], 0.08f, "Subm", TextAnchor.MiddleCenter, -1);
            H(ColX[5], 0.12f, "Role", TextAnchor.MiddleCenter, -1);
            H(ColX[6], 0.10f, "Focus", TextAnchor.MiddleCenter, -1);
            H(ColX[7], 0.05f, "Prd", TextAnchor.MiddleCenter, -1);
            H(ColX[8], 0.05f, "Curf", TextAnchor.MiddleCenter, -1);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
        }

        private void DrawHaremRow(Rect r, Pawn pet)
        {
            float W = r.width;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);

            // Risk strip at the far left (red at-risk / amber watch / green stable).
            float risk = RiskScore(pet);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y + 3f, 3f, r.height - 6f),
                risk > 15f ? new Color(0.85f, 0.3f, 0.3f) : risk > 5f ? new Color(0.85f, 0.7f, 0.3f) : new Color(0.35f, 0.7f, 0.42f));
            TooltipHandler.TipRegion(new Rect(r.x, r.y, 6f, r.height), () => risk > 15f
                ? "At risk: low rapport / weak conditioning (or AI-controlled) - may rebel or flee."
                : risk > 5f ? "Some instability - watch this pet." : "Stable.", pet.thingIDNumber ^ 0x37);

            var port = new Rect(r.x + 7f, r.y + 5f, 30f, 30f);
            DrawPortrait(port, pet);
            var nameRect = new Rect(port.xMax + 4f, r.y, W * 0.20f - 41f, r.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, pet.LabelShortCap.Truncate(nameRect.width));
            if (prof != null && prof.lifetimeEarnings > 0) TooltipHandler.TipRegion(nameRect, "Earned " + prof.lifetimeEarnings + " silver for the colony.");
            if (Widgets.ButtonInvisible(nameRect)) CameraJumper.TryJumpAndSelect(pet);

            Pawn owner = HarassmentEngine.FindKeyHolderFor(pet);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(r.x + W * ColX[1], r.y, W * 0.13f - 4f, r.height), (owner != null ? owner.LabelShortCap : "-").Truncate(W * 0.13f - 4f));
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rap = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;
            Widgets.FillableBar(new Rect(r.x + W * ColX[2], r.y + 13f, W * 0.10f, 12f), cond, SolidBar(CondColor), SolidBar(EmptyColor), true);
            Widgets.FillableBar(new Rect(r.x + W * ColX[3], r.y + 13f, W * 0.10f, 12f), rap, SolidBar(rap < 0.4f ? FearColor : TrustColor), SolidBar(EmptyColor), true);

            var subBar = new Rect(r.x + W * ColX[4], r.y + 13f, W * 0.075f, 12f);
            var sub = Need_Submission.For(pet);
            if (sub != null) Widgets.FillableBar(subBar, sub.CurLevel, SolidBar(new Color(0.55f, 0.45f, 0.75f)), SolidBar(EmptyColor), true);
            else { GUI.color = ModernStyle.TextDim; Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(subBar, "-"); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }

            if (SmallButton(new Rect(r.x + W * ColX[5], r.y + 8f, W * 0.115f, 24f), HarassmentEngine.PetRoleLabel(prof?.petRole ?? 0)))
                OpenRoleMenu(role => HarassmentEngine.SetPetRole(HarassmentEngine.FindKeyHolderFor(pet), pet, role));

            if (SmallButton(new Rect(r.x + W * ColX[6], r.y + 8f, W * 0.10f, 24f), FocusLabel(prof?.trainFocus)) && prof != null)
                OpenFocusMenu(key => prof.trainFocus = key);

            if (prof != null)
            {
                float iy = r.y + (r.height - 22f) / 2f;
                var par = new Rect(r.x + W * 0.92f - 11f, iy, 22f, 22f);   // centered under the "Prd" header
                if (IconToggle(par, HarassmentTextures.Parade, prof.autoParade, "Auto-parade: parade this pet around the colony during the day.")) prof.autoParade = !prof.autoParade;
                var cur = new Rect(r.x + W * 0.97f - 11f, iy, 22f, 22f);   // centered under the "Curf" header
                if (IconToggle(cur, HarassmentTextures.Moon, prof.curfew, "Curfew: keep this pet at its owner's side through the night.")) prof.curfew = !prof.curfew;
            }
        }

        private static bool SmallButton(Rect r, string label)
        {
            Widgets.DrawBoxSolid(r, ModernStyle.PanelBG);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny; GUI.color = ModernStyle.Body;
            Widgets.Label(r, (label ?? "").Truncate(r.width - 6f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            return Widgets.ButtonInvisible(r);
        }

        // An icon that acts as an on/off toggle (gold when on, dim when off). Returns true when clicked.
        private static bool IconToggle(Rect r, Texture2D icon, bool on, string tip)
        {
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            GUI.color = on ? new Color(0.98f, 0.82f, 0.35f) : new Color(0.80f, 0.80f, 0.84f);
            GUI.DrawTexture(r, icon);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // A framed icon button (no toggle state). Returns true when clicked.
        private static bool IconButton(Rect r, Texture2D icon, string tip)
        {
            Widgets.DrawBoxSolid(r, ModernStyle.PanelBG);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            GUI.color = new Color(0.86f, 0.86f, 0.9f);
            GUI.DrawTexture(r.ContractedBy(4f), icon);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        private void OpenRoleMenu(System.Action<int> act)
        {
            var opts = new List<FloatMenuOption>();
            for (int i = 0; i < RoleNames.Length; i++) { int ri = i; opts.Add(new FloatMenuOption(RoleNames[ri], () => act(ri))); }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void OpenFocusMenu(System.Action<string> act)
        {
            var opts = new List<FloatMenuOption>();
            foreach (var f in Focuses) { var key = f.key; opts.Add(new FloatMenuOption(f.label, () => act(key))); }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Bulk gear: pick a lockable device and lock it on every pet.
        private void OpenDeviceMenu(List<Pawn> pets)
        {
            var opts = new List<FloatMenuOption>();
            var devices = HarassmentEngine.AllLockableDevices();
            for (int i = 0; i < devices.Count; i++)
            {
                var def = devices[i];
                if (def == null) continue;
                opts.Add(new FloatMenuOption("Lock " + def.label + " on all pets", () =>
                {
                    foreach (var p in pets)
                        if (p?.apparel != null && ApparelUtility.HasPartsToWear(p, def))
                            HarassmentEngine.ApplyAndLockDevice(p, def, HarassmentEngine.FindKeyHolderFor(p));
                }));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("(no lockable devices found)", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static string FocusLabel(string key)
        {
            foreach (var f in Focuses) if (f.key == key) return f.label;
            return "No conditioning";
        }

        // 24-hour schedule painter. Pick an assignment, then click/drag across the hour cells to paint it.
        private static Texture2D SchedIcon(int i)
        {
            switch (i)
            {
                case 0: return HarassmentTextures.SchedFree;
                case 1: return HarassmentTextures.SchedServe;
                case 2: return HarassmentTextures.SchedTrain;
                case 3: return HarassmentTextures.Parade;
                case 4: return HarassmentTextures.SchedRest;
                case 5: return HarassmentTextures.SchedConfined;
                default: return null;
            }
        }

        private void DrawSchedule(Rect area, Pawn pet, PawnProfile prof)
        {
            if (prof == null) { Empty(area, "No pet data."); return; }
            if (prof.schedule == null || prof.schedule.Count != 24) prof.schedule = new List<int>(new int[24]);

            float x = area.x;
            float cw = area.width;   // full pawn-row width
            float y = area.y;

            // 24-hour grid on TOP (current hour outlined gold). Click/drag to paint.
            float cellW = cw / 24f;
            const float gridH = 24f;
            int nowH = GenLocalDate.HourOfDay(pet);
            for (int h = 0; h < 24; h++)
            {
                var cell = new Rect(x + h * cellW, y, cellW - 1f, gridH);
                int a = prof.schedule[h]; if (a < 0 || a >= SchedAssigns.Length) a = 0;
                Widgets.DrawBoxSolid(cell, SchedAssigns[a].col);
                if (h == nowH) { GUI.color = HeaderCol; Widgets.DrawBox(cell, 1); GUI.color = Color.white; }
                if (Mouse.IsOver(cell) && Event.current.button == 0 &&
                    (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag))
                { prof.schedule[h] = _paintAssign; Event.current.Use(); }
            }
            y += gridH + 7f;   // clear gap between the grid and the hour numbers

            // Hour ticks under the bar - full opacity, vertically centered, edge labels clamped so nothing is cut off.
            Text.Font = GameFont.Tiny; GUI.color = Color.white;
            for (int h = 0; h <= 24; h += 3)
            {
                float lx = x + h * cellW - 12f;
                var anch = TextAnchor.MiddleCenter;
                if (h == 0) { lx = x + 1f; anch = TextAnchor.MiddleLeft; }
                else if (h == 24) { lx = x + cw - 25f; anch = TextAnchor.MiddleRight; }
                Text.Anchor = anch;
                Widgets.Label(new Rect(lx, y, 24f, 16f), h.ToString());
            }
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f;   // clear gap below the hour numbers, before the type palette

            // Assignment types BELOW the bar (icon + label). Click one, then click/drag across the hours above.
            float pw = cw / SchedAssigns.Length;
            for (int i = 0; i < SchedAssigns.Length; i++)
            {
                var pr = new Rect(x + i * pw, y, pw - 3f, 20f);
                Widgets.DrawBoxSolid(pr, SchedAssigns[i].col);
                if (_paintAssign == i) { GUI.color = HeaderCol; Widgets.DrawBox(pr, 2); GUI.color = Color.white; }
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; Text.WordWrap = false;
                Widgets.Label(new Rect(pr.x + 1f, pr.y, pr.width - 2f, pr.height), SchedAssigns[i].label);
                Text.WordWrap = true; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(pr)) _paintAssign = i;
            }
            y += 24f;

            // Quarters + clear.
            var qBtn = new Rect(x, y, cw * 0.60f, 20f);
            if (SmallButton(qBtn, prof.quartersCell.IsValid ? "Quarters room set - click to move" : "Set quarters room (for Confined)..."))
            {
                var pp = prof;
                Find.MainTabsRoot.EscapeCurrentTab(false);
                Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false },
                    (LocalTargetInfo t) => { if (t.Cell.IsValid) pp.quartersCell = t.Cell; });
            }
            var cBtn = new Rect(qBtn.xMax + 6f, y, cw * 0.34f, 20f);
            if (SmallButton(cBtn, "Clear schedule")) { prof.schedule = null; prof.quartersCell = IntVec3.Invalid; }
            y += 24f;

            // Head girl toggle + daily service quota.
            HarassmentEngine.RollQuotaDay(prof, pet);
            bool autoHG = RimJobWorldSexualHarassmentMod.Settings.autoHeadGirl;
            bool wasHG = prof.isHeadGirl;
            Widgets.CheckboxLabeled(new Rect(x, y, 118f, 20f), "Head girl", ref prof.isHeadGirl, disabled: autoHG);
            if (!autoHG && prof.isHeadGirl && !wasHG) HarassmentEngine.SetSoleHeadGirl(pet);
            TooltipHandler.TipRegion(new Rect(x, y, 118f, 20f), autoHG
                ? "Chosen automatically (best performer for the owner). Turn off 'Auto head girl' to set it manually."
                : "The head girl walks over and disciplines misbehaving or below-quota pets, per owner.");
            float qx = x + 130f;
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(qx, y, 46f, 20f), "Quota"); GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            qx += 46f;
            if (SmallButton(new Rect(qx, y, 20f, 20f), "-")) prof.dailyQuota = Mathf.Max(0, prof.dailyQuota - 1);
            qx += 22f;
            Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(new Rect(qx, y, 34f, 20f), prof.dailyQuota == 0 ? "off" : prof.dailyQuota.ToString()); Text.Anchor = TextAnchor.UpperLeft;
            qx += 36f;
            if (SmallButton(new Rect(qx, y, 20f, 20f), "+")) prof.dailyQuota++;
            qx += 26f;
            if (prof.dailyQuota > 0)
            {
                GUI.color = prof.servicesToday >= prof.dailyQuota ? new Color(0.45f, 0.85f, 0.5f) : new Color(0.9f, 0.6f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(qx, y, 160f, 20f), "served " + prof.servicesToday + " / " + prof.dailyQuota + " today");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
        }

        // ── Left: roster ─────────────────────────────────────────────────────
        private void DrawRoster(Rect rect)
        {
            var groups = BuildGroups();
            if (groups.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(rect, "No collared pets or owned slaves yet. Condition a pawn to 90+ and lock a control collar on them, and they will appear here.");
                GUI.color = Color.white;
                return;
            }

            float total = 0f;
            for (int i = 0; i < groups.Count; i++)
                total += OwnerRowH + groups[i].pets.Count * PetRowH + 8f;

            var view = new Rect(0f, 0f, rect.width - 18f, total);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(rect, ref _scroll, view);
            float y = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                y = DrawGroup(view, y, groups[i]);
                y += 8f;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        private float DrawGroup(Rect view, float y, Group g)
        {
            var ownerRow = new Rect(0f, y, view.width, OwnerRowH);
            Widgets.DrawBoxSolid(ownerRow, new Color(1f, 1f, 1f, 0.04f));
            var portRect = new Rect(ownerRow.x + 3f, ownerRow.y + 4f, 36f, 36f);
            DrawPortrait(portRect, g.owner);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(portRect.xMax + 8f, ownerRow.y, 220f, OwnerRowH), "<b>" + (g.owner != null ? g.owner.LabelShortCap : "No owner") + "</b>");
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(ownerRow.xMax - 70f, ownerRow.y, 64f, OwnerRowH), g.pets.Count + (g.pets.Count == 1 ? " pet" : " pets"));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (g.owner != null && Widgets.ButtonInvisible(portRect)) CameraJumper.TryJumpAndSelect(g.owner);
            y += OwnerRowH;

            float spineX = portRect.x + 16f;
            float spineTop = y;
            for (int i = 0; i < g.pets.Count; i++)
            {
                var pet = g.pets[i];
                float rowY = y + i * PetRowH;
                float midY = rowY + PetRowH / 2f;
                Widgets.DrawLine(new Vector2(spineX, spineTop - 4f), new Vector2(spineX, midY), BranchColor, 1.5f);
                Widgets.DrawLine(new Vector2(spineX, midY), new Vector2(spineX + Indent - 16f, midY), BranchColor, 1.5f);
                DrawRosterRow(new Rect(Indent, rowY, view.width - Indent, PetRowH), pet);
            }
            return y + g.pets.Count * PetRowH;
        }

        private void DrawRosterRow(Rect row, Pawn pet)
        {
            if (pet == _selected) Widgets.DrawBoxSolid(row, SelTint);
            else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

            var port = new Rect(row.x + 2f, row.y + 6f, 34f, 34f);
            DrawPortrait(port, pet);

            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pet);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(port.xMax + 6f, row.y + 1f, row.width * 0.42f, 22f), pet.LabelShortCap);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(port.xMax + 6f, row.y + 22f, row.width * 0.42f, 20f), SituationLabel(pet, prof));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rapport = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;
            float barX = port.xMax + row.width * 0.44f;
            float barW = row.xMax - barX - 8f;
            if (barW > 40f)
            {
                var condBar = new Rect(barX, row.y + 8f, barW, 12f);
                var rapBar = new Rect(barX, row.y + 24f, barW, 12f);
                Widgets.FillableBar(condBar, cond, SolidBar(CondColor), SolidBar(EmptyColor), true);
                Widgets.FillableBar(rapBar, rapport, SolidBar(rapport < 0.4f ? FearColor : TrustColor), SolidBar(EmptyColor), true);
                TooltipHandler.TipRegion(condBar, () => CondTooltip(prof), pet.thingIDNumber ^ 0x1);
                TooltipHandler.TipRegion(rapBar, () => RapportTooltip(prof), pet.thingIDNumber ^ 0x2);
            }

            if (Widgets.ButtonInvisible(row)) _selected = pet;
        }

        // ── Right: detail + commands + graph ─────────────────────────────────
        private void DrawDetail(Rect rect)
        {
            if (_selected == null || _selected.Dead || !_selected.Spawned)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "Select a pet on the left to manage them.");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            var pet = _selected;
            var prof = GameComponent_Harassment.Instance?.GetProfile(pet);

            // Header: portrait + name + jump.
            var port = new Rect(rect.x, rect.y, 56f, 56f);
            DrawPortrait(port, pet);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(port.xMax + 10f, rect.y + 2f, rect.width - port.width - 40f, 30f), pet.LabelShortCap);
            Text.Font = GameFont.Small;
            var jump = new Rect(rect.xMax - 30f, rect.y + 4f, 26f, 26f);
            TooltipHandler.TipRegion(jump, "Jump to and select this pawn.");
            if (Widgets.ButtonImage(jump, HarassmentTextures.GoTo)) CameraJumper.TryJumpAndSelect(pet);

            Pawn owner = null;
            if (prof != null)
            {
                if (prof.ownerId >= 0) owner = FindPawnById(prof.ownerId);
                if (owner == null && prof.relationshipOwnerId >= 0) owner = FindPawnById(prof.relationshipOwnerId);
            }
            bool hasOwner = owner != null && owner.Spawned && !owner.Dead;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(port.xMax + 10f, rect.y + 30f, rect.width - port.width - 12f, 22f),
                (hasOwner ? "Owned by " + owner.LabelShortCap : "No active owner") + "  \u2022  " + SituationLabel(pet, prof));
            GUI.color = Color.white;

            float y = rect.y + 66f;

            // Bars.
            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rapport = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;
            DrawLabeledBar(new Rect(rect.x, y, rect.width, 22f), "Conditioned", cond, CondColor, CondTooltip(prof));
            y += 26f;
            DrawLabeledBar(new Rect(rect.x, y, rect.width, 22f), "Rapport", rapport, rapport < 0.4f ? FearColor : TrustColor, RapportTooltip(prof));
            y += 30f;

            // World info: scandalous photos, circulation, visitor draw, and reputation (soft Karma tie).
            y = DrawWorldInfo(rect, y, pet) + 4f;

            // Tabbed detail: Conditioning / Role / Graph / Moodlets / Interactions, each in a Modern card.
            var tabRow = new Rect(rect.x, y, rect.width, 24f);
            DrawDetailTabs(tabRow);
            y += 28f;
            var card = new Rect(rect.x, y, rect.width, Mathf.Max(90f, rect.yMax - y - 6f));
            ModernStyle.DrawCard(card);
            var area = card.ContractedBy(8f);
            switch (_detailTab)
            {
                case 0: DrawFocusRadios(area, prof); break;
                case 1: DrawRoleRadios(area, pet, prof); break;
                case 3: DrawMoodlets(area, pet); break;
                case 4: DrawInteractions(area, pet); break;
                default: DrawHistoryGraph(area, prof); break;
            }
        }

        private static bool RadioRow(Rect r, string label, bool selected)
        {
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(r.x + 6f, r.y, r.width - 32f, r.height), label);
            GUI.color = selected ? OnCircle : Color.white;
            Widgets.RadioButton(new Vector2(r.xMax - 28f, r.y + 2f), selected);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return Widgets.ButtonInvisible(r);
        }

        // Ongoing conditioning focus (single-select, worked on over time).
        private void DrawFocusRadios(Rect area, PawnProfile prof)
        {
            if (prof == null) { Empty(area, "No pet data."); return; }
            float y = area.y + 2f;
            foreach (var f in Focuses)
            {
                if (RadioRow(new Rect(area.x, y, area.width, 26f), f.label, prof.trainFocus == f.key)) prof.trainFocus = f.key;
                y += 28f;
            }
        }

        // Pet role / specialization (single-select).
        private void DrawRoleRadios(Rect area, Pawn pet, PawnProfile prof)
        {
            if (prof == null) { Empty(area, "No pet data."); return; }
            float y = area.y + 2f;
            for (int i = 0; i < RoleNames.Length; i++)
            {
                if (RadioRow(new Rect(area.x, y, area.width, 26f), RoleNames[i], prof.petRole == i))
                    HarassmentEngine.SetPetRole(HarassmentEngine.FindKeyHolderFor(pet), pet, i);
                y += 28f;
            }
        }

        private void DrawDetailTabs(Rect row)
        {
            Text.Font = GameFont.Tiny; // five tabs - keep labels compact so they fit
            float w = row.width / DetailTabs.Length;
            for (int i = 0; i < DetailTabs.Length; i++)
            {
                var r = new Rect(row.x + i * w, row.y, w - 2f, row.height);
                bool sel = _detailTab == i;
                Widgets.DrawBoxSolid(r, sel ? ModernStyle.PanelBG : new Color(ModernStyle.BGD.r, ModernStyle.BGD.g, ModernStyle.BGD.b, 0.6f));
                if (sel) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 2f, r.width, 2f), HeaderCol);
                GUI.color = ModernStyle.BGL; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = sel ? HeaderCol : ModernStyle.TextDim;
                Widgets.Label(r, DetailTabs[i]);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                if (Widgets.ButtonInvisible(r)) _detailTab = i;
            }
            Text.Font = GameFont.Small;
        }

        // Filter for the Moodlets + Interactions tabs: only sex / collar / harassment / sex-attribute content -
        // our own RJWSH defs, the whole RJW ecosystem (by modContentPack), and vanilla sex/lovin' thoughts.
        private static bool IsRelevantDef(Verse.Def def)
        {
            if (def == null) return false;
            string dn = def.defName ?? "";
            if (dn.StartsWith("RJWSH")) return true;
            var pid = def.modContentPack?.PackageId;   // lowercased by RimWorld
            if (pid != null && (pid.Contains("rjw") || pid.Contains("rim.job.world"))) return true;
            return ContainsSexKeyword(dn);
        }

        private static bool IsRelevantThought(Thought t) => t != null && IsRelevantDef(t.def);

        private static System.Reflection.FieldInfo _intDefField;
        private static bool IsRelevantInteraction(LogEntry e, Pawn pet)
        {
            if (e == null) return false;
            if (e is Verse.PlayLogEntry_Interaction pli)
            {
                if (_intDefField == null)
                    _intDefField = typeof(Verse.PlayLogEntry_Interaction).GetField("intDef",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_intDefField?.GetValue(pli) is RimWorld.InteractionDef idef) return IsRelevantDef(idef);
            }
            // Other entry types (e.g. RJW's own sex-act log entries) - fall back to the rendered text.
            return ContainsSexKeyword(SafeLogText(e, pet));
        }

        private static bool ContainsSexKeyword(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string l = s.ToLowerInvariant();
            return l.Contains("sex") || l.Contains("rape") || l.Contains("collar") || l.Contains("naked")
                || l.Contains("whore") || l.Contains("breed") || l.Contains("cum") || l.Contains("slave")
                || l.Contains("harass") || l.Contains("submiss") || l.Contains("bondage") || l.Contains("orgasm")
                || l.Contains("lovin") || l.Contains("fuck") || l.Contains("aphrodisiac") || l.Contains("fondle")
                || l.Contains("grope") || l.Contains("masturbat") || l.Contains("servic");
        }

        // Current mood thoughts (memories + situational), grouped, with their mood offset.
        private void DrawMoodlets(Rect area, Pawn pet)
        {
            var mood = pet.needs?.mood;
            if (mood == null) { Empty(area, "No mood."); return; }
            var groups = new List<Thought>();
            try { mood.thoughts.GetDistinctMoodThoughtGroups(groups); } catch { }
            groups.RemoveAll(t => !IsRelevantThought(t)); // sex / collar / sex-attribute moodlets only
            if (groups.Count == 0) { Empty(area, "No sex or collar moodlets."); return; }
            const float rowH = 26f;
            var view = new Rect(0f, 0f, area.width - 16f, groups.Count * rowH);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(area, ref _moodScroll, view);
            float ry = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                var t = groups[i];
                float offset = 0f;
                try { offset = mood.thoughts.MoodOffsetOfGroup(t); } catch { }
                var r = new Rect(0f, ry, view.width, rowH - 2f);
                if (i % 2 == 1) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(r.x + 4f, r.y, r.width - 60f, r.height), t.LabelCap.Truncate(r.width - 60f));
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = offset > 0f ? new Color(0.45f, 0.85f, 0.5f) : offset < 0f ? new Color(0.9f, 0.42f, 0.42f) : ModernStyle.TextDim;
                Widgets.Label(new Rect(r.xMax - 56f, r.y, 52f, r.height), offset.ToString("+0;-0;0"));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                ry += rowH;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // Recent social-log interactions involving this pet, newest first.
        private void DrawInteractions(Rect area, Pawn pet)
        {
            var entries = new List<LogEntry>();
            try
            {
                var all = Find.PlayLog.AllEntries;
                for (int i = all.Count - 1; i >= 0 && entries.Count < 30; i--)
                    if (all[i] != null && all[i].Concerns(pet) && IsRelevantInteraction(all[i], pet)) entries.Add(all[i]);
            }
            catch { }
            if (entries.Count == 0) { Empty(area, "No recent sex or collar interactions."); return; }
            float wrapW = area.width - 46f;   // leave room for the type icon on the left
            var texts = new string[entries.Count];
            var heights = new float[entries.Count];
            float total = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                texts[i] = SafeLogText(entries[i], pet);
                heights[i] = Mathf.Max(24f, Text.CalcHeight(texts[i], wrapW) + 6f);
                total += heights[i];
            }
            var view = new Rect(0f, 0f, area.width - 16f, total);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(area, ref _intScroll, view);
            float ry = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                var r = new Rect(0f, ry, view.width, heights[i]);
                if (i % 2 == 1) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
                var icon = InteractionIcon(texts[i]);
                if (icon != null) { GUI.color = new Color(0.82f, 0.82f, 0.88f); GUI.DrawTexture(new Rect(r.x + 4f, r.y + 4f, 16f, 16f), icon); GUI.color = Color.white; }
                GUI.color = ModernStyle.Body;
                Widgets.Label(new Rect(r.x + 26f, r.y + 2f, wrapW, heights[i] - 4f), texts[i]);
                GUI.color = Color.white;
                ry += heights[i];
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // A small icon for an interaction log line, matched by keyword (discipline/reward/shock/parade/grope/sex).
        private static Texture2D InteractionIcon(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return null;
            string l = txt.ToLowerInvariant();
            if (l.Contains("disciplin") || l.Contains("struck") || l.Contains("beat") || l.Contains("punish")) return HarassmentTextures.Discipline;
            if (l.Contains("reward") || l.Contains("praise") || l.Contains("pamper")) return HarassmentTextures.Reward;
            if (l.Contains("shock") || l.Contains("collar")) return HarassmentTextures.Shock;
            if (l.Contains("parade")) return HarassmentTextures.Parade;
            if (l.Contains("grope") || l.Contains("fondle") || l.Contains("harass")) return HarassmentTextures.Command;
            if (l.Contains("sex") || l.Contains("rape") || l.Contains("lovin") || l.Contains("breed") || l.Contains("fuck") || l.Contains("orgasm") || l.Contains("cum") || l.Contains("servic") || l.Contains("whore")) return HarassmentTextures.RapportIcon;
            return null;
        }

        private static string SafeLogText(LogEntry e, Pawn pet)
        {
            try { return e.ToGameStringFromPOV(pet, false); } catch { return "(interaction)"; }
        }

        private static void Empty(Rect area, string msg)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ModernStyle.TextDim;
            Widgets.Label(area, msg);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Compact world-info strip: photos of this pet, colony photos in circulation, visitor likelihood,
        // and world reputation (soft Karma tie). Returns the new y.
        private static readonly Color HeaderCol = new Color(0.90f, 0.74f, 0.32f);
        private static readonly Color OnCircle = new Color(0.42f, 0.86f, 0.46f);
        private static readonly (string key, string label)[] Focuses =
        {
            (null, "No conditioning"),
            ("willpower", "Break their will"),
            ("esteem", "Humble them"),
            ("spirit", "Crush their spirit"),
            ("subdom", "Train submission"),
            ("addiction", "Cultivate addiction"),
        };
        private static readonly string[] RoleNames = { "None", "Pleasure pet", "House servant", "Bodyguard", "Performer" };

        // Ongoing conditioning focus + role, as single-select radio lists (moved here from the Control tab).
        private static float DrawConditioning(Rect rect, float y, Pawn pet, PawnProfile prof)
        {
            if (prof == null) return y;
            y = MiniHeader(rect, y, "Conditioning");
            foreach (var f in Focuses)
            {
                var r = new Rect(rect.x, y, rect.width, 22f);
                if (Mouse.IsOver(r)) Widgets.DrawHighlightIfMouseover(r);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(r.x + 4f, r.y, r.width - 30f, r.height), f.label);
                Text.Anchor = TextAnchor.UpperLeft;
                bool sel = prof.trainFocus == f.key;
                GUI.color = sel ? OnCircle : Color.white;
                Widgets.RadioButton(new Vector2(r.xMax - 26f, r.y), sel);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r)) prof.trainFocus = f.key;
                y += 23f;
            }
            y += 4f;
            y = MiniHeader(rect, y, "Role");
            for (int i = 0; i < RoleNames.Length; i++)
            {
                var r = new Rect(rect.x, y, rect.width, 22f);
                if (Mouse.IsOver(r)) Widgets.DrawHighlightIfMouseover(r);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(r.x + 4f, r.y, r.width - 30f, r.height), RoleNames[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                bool sel = prof.petRole == i;
                GUI.color = sel ? OnCircle : Color.white;
                Widgets.RadioButton(new Vector2(r.xMax - 26f, r.y), sel);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r)) HarassmentEngine.SetPetRole(HarassmentEngine.FindKeyHolderFor(pet), pet, i);
                y += 23f;
            }
            return y;
        }

        private static float MiniHeader(Rect rect, float y, string title)
        {
            GUI.color = HeaderCol;
            Widgets.Label(new Rect(rect.x, y, rect.width, 20f), title);
            GUI.color = ModernStyle.BGL;
            Widgets.DrawLineHorizontal(rect.x, y + 19f, rect.width);
            GUI.color = Color.white;
            return y + 24f;
        }

        private static float DrawWorldInfo(Rect rect, float y, Pawn pet)
        {
            HarassmentEngine.CountPhotosOf(pet, out int photos, out int circ);
            var gc = GameComponent_Harassment.Instance;
            int notoriety = gc?.notoriety ?? 0;
            int visLeft = gc?.NextCuriousVisitorTicksLeft ?? -1;

            Text.Font = GameFont.Tiny;
            const float iconSz = 18f;

            // Photos - a clickable photo icon opens the gallery popout.
            var photoIcon = new Rect(rect.x, y, iconSz, iconSz);
            var pdef = RJWSH_ThingDefOf.RJWSH_ScandalousPhoto;
            if (pdef?.uiIcon != null) GUI.DrawTexture(photoIcon, pdef.uiIcon);
            else Widgets.DrawBoxSolid(photoIcon, new Color(0.25f, 0.25f, 0.28f));
            string photoTxt = photos == 0 ? "no known photos"
                : photos + " photo" + (photos == 1 ? "" : "s") + (circ > 0 ? "  (" + circ + " circulating)" : "");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(photoIcon.xMax + 6f, y, rect.width - iconSz - 6f, iconSz), photoTxt);
            Text.Anchor = TextAnchor.UpperLeft;
            var photoRow = new Rect(rect.x, y, rect.width, iconSz);
            if (photos > 0)
            {
                Widgets.DrawHighlightIfMouseover(photoRow);
                TooltipHandler.TipRegion(photoRow, "Click to see every known photo of " + pet.LabelShort + " and who controls it.");
                if (Widgets.ButtonInvisible(photoRow)) Find.WindowStack.Add(new Window_PhotoGallery(pet));
            }
            else TooltipHandler.TipRegion(photoRow, "No scandalous photos of " + pet.LabelShort + " are known to exist.");
            y += 22f;

            const float labelW = 72f;
            // Visitor draw - a notoriety bar with the value right-aligned after it (no clipping at the edge).
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(rect.x, y, labelW, iconSz), "Visitor draw");
            GUI.color = Color.white;
            string vtxt = visLeft >= 0 ? "next in " + visLeft.ToStringTicksToPeriod() : "notoriety " + notoriety;
            float vtW = Mathf.Min(rect.width * 0.4f, Text.CalcSize(vtxt).x + 6f);
            var vbar = new Rect(rect.x + labelW + 4f, y + 3f, Mathf.Max(20f, rect.width - labelW - 8f - vtW), 12f);
            Widgets.FillableBar(vbar, Mathf.Clamp01(notoriety / 100f), SolidBar(new Color(0.82f, 0.52f, 0.25f)), SolidBar(EmptyColor), false);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(rect.xMax - vtW, y, vtW, iconSz), vtxt);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            var vrow = new Rect(rect.x, y, rect.width, iconSz);
            Widgets.DrawHighlightIfMouseover(vrow);
            TooltipHandler.TipRegion(vrow, "Colony notoriety (" + notoriety + "/100) - how widely your depravity is known. The higher it climbs, the sooner curious visitors arrive to see your collared pets in person."
                + (visLeft >= 0 ? "\n\nNext curious visitors arrive in " + visLeft.ToStringTicksToPeriod() + "." : "\n\nNo visit currently scheduled."));
            y += 20f;

            // Reputation - soft Karma tie (truncated to the row so it never clips).
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(rect.x, y, labelW, iconSz), "Reputation");
            GUI.color = Color.white;
            string rep = HarassmentEngine.WorldReputationLabel(pet);
            var repRect = new Rect(rect.x + labelW + 4f, y, rect.width - labelW - 8f, iconSz);
            Text.WordWrap = false;
            Widgets.Label(repRect, rep.Truncate(repRect.width));
            Text.WordWrap = true;
            var rrow = new Rect(rect.x, y, rect.width, iconSz);
            Widgets.DrawHighlightIfMouseover(rrow);
            TooltipHandler.TipRegion(rrow, "This pawn's sexual reputation in the world. With Karma & Reputation installed it tracks their karma; otherwise it is derived from colony notoriety and rumor.");
            y += 20f;

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y;
        }

        private static void DrawLabeledBar(Rect r, string label, float pct, Color fill, string tooltip = null)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x, r.y, 92f, r.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            var bar = new Rect(r.x + 96f, r.y, r.width - 96f, r.height);
            Widgets.FillableBar(bar, Mathf.Clamp01(pct), SolidBar(fill), SolidBar(EmptyColor), true);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, ((int)(pct * 100f)) + "%");
            Text.Anchor = TextAnchor.UpperLeft;
            // Mousing over the whole labeled row (label + bar) explains what the stat means and the
            // thresholds that unlock behavior changes.
            if (!tooltip.NullOrEmpty())
            {
                Widgets.DrawHighlightIfMouseover(r);
                TooltipHandler.TipRegion(r, tooltip);
            }
        }

        // ── Stat tooltips: explain the bar + the thresholds that gate behavior ─────────
        private static string CondTooltip(PawnProfile prof)
        {
            float lvl = prof != null ? prof.hypnosisLevel : 0f;
            string Mark(bool hit) => hit ? "\u2713 " : "\u2022 ";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Conditioning: " + Mathf.RoundToInt(lvl) + " / 100");
            sb.AppendLine();
            sb.AppendLine("How deeply this pet has been broken to the collar. It climbs with discipline, conditioning sessions, punishment and time spent collared, and fades slowly while the collar is off.");
            sb.AppendLine();
            sb.AppendLine("Behavior thresholds:");
            sb.AppendLine(Mark(lvl >= 30f) + "30  suggestible - begins to comply with commands");
            sb.AppendLine(Mark(lvl >= 60f) + "60  conditioned - obeys, serves and accepts affection willingly");
            sb.AppendLine(Mark(lvl >= 90f) + "90  fully conditioned - the control collar locks on and they turn devoted");
            sb.AppendLine();
            sb.AppendLine("Once collared, continued wear deepens hidden conditioning that grants the Masochist trait (starts to crave pain and degradation), then Stockholm Syndrome (utterly devoted, permanent).");
            sb.AppendLine();
            if (lvl < 30f) sb.Append("Needs " + Mathf.CeilToInt(30f - lvl) + " more to become suggestible.");
            else if (lvl < 60f) sb.Append("Needs " + Mathf.CeilToInt(60f - lvl) + " more to become conditioned.");
            else if (lvl < 90f) sb.Append("Needs " + Mathf.CeilToInt(90f - lvl) + " more to be fully conditioned and collared.");
            else sb.Append("Fully conditioned.");
            return sb.ToString();
        }

        private static string RapportTooltip(PawnProfile prof)
        {
            float r = prof != null ? prof.rapport : 50f;
            string band = r < 25f ? "desperate" : r < 40f ? "fearful" : r < 70f ? "settled" : "trusting";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Rapport: " + Mathf.RoundToInt(r) + " / 100  (" + band + ")");
            sb.AppendLine();
            sb.AppendLine("The pet's trust and will. High rapport is a pet that trusts its owner and obeys willingly; low rapport is fear-broken and volatile, quicker to resist, fight back or bolt for freedom. Rewards and affection raise it; discipline, shocks, punishment and force lower it.");
            sb.AppendLine();
            sb.AppendLine("Behavior bands:");
            sb.AppendLine((r < 25f ? "\u25B8 " : "   ") + "below 25  desperate - periodically attempts a breakout (once conditioned)");
            sb.AppendLine((r >= 25f && r < 40f ? "\u25B8 " : "   ") + "25 to 40  fearful and volatile - resists more, easier to make fight back");
            sb.AppendLine((r >= 40f ? "\u25B8 " : "   ") + "40 and up  settled - trusts and complies willingly");
            return sb.ToString();
        }

        private void DrawHistoryGraph(Rect rect, PawnProfile prof)
        {
            Widgets.DrawBoxSolid(rect, ModernStyle.BGD);
            var _bc = GUI.color; GUI.color = Color.black; Widgets.DrawBox(rect, 1); GUI.color = _bc;

            // Plot area, inset on the left to make room for the Y axis.
            Rect plot = new Rect(rect.x + 30f, rect.y + 8f, rect.width - 38f, rect.height - 16f);

            // Y axis: 0/25/50/75/100 labels + gridlines.
            Text.Font = GameFont.Tiny;
            for (int q = 0; q <= 4; q++)
            {
                float gy = plot.yMax - plot.height * (q / 4f);
                Widgets.DrawLine(new Vector2(plot.x, gy), new Vector2(plot.xMax, gy),
                    new Color(1f, 1f, 1f, q == 0 ? 0.16f : 0.06f), 1f);
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(rect.x + 2f, gy - 8f, 26f, 14f), (q * 25).ToString());
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;

            var cond = prof?.condHistory;
            var rap = prof?.rapportHistory;
            if (cond == null || cond.Count < 2)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(plot, "Collecting history... (samples hourly)");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            DrawSeries(plot, cond, CondColor);
            if (rap != null && rap.Count >= 2) DrawSeries(plot, rap, TrustColor);

            DrawGraphLegend(plot);
            DrawGraphHover(plot, cond, rap);
        }

        private static void DrawSeries(Rect r, List<float> vals, Color c)
        {
            int n = vals.Count;
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = r.x + r.width * (i / (float)(n - 1));
                float x1 = r.x + r.width * ((i + 1) / (float)(n - 1));
                float y0 = r.yMax - r.height * Mathf.Clamp01(vals[i] / 100f);
                float y1 = r.yMax - r.height * Mathf.Clamp01(vals[i + 1] / 100f);
                Widgets.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), c, 1.6f);
            }
        }

        private static void DrawGraphLegend(Rect plot)
        {
            var box = new Rect(plot.xMax - 128f, plot.y + 2f, 126f, 34f);
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.55f));
            Text.Font = GameFont.Tiny;
            GUI.color = CondColor; Widgets.Label(new Rect(box.x + 5f, box.y + 1f, box.width - 8f, 16f), "\u25A0 conditioning");
            GUI.color = TrustColor; Widgets.Label(new Rect(box.x + 5f, box.y + 17f, box.width - 8f, 16f), "\u25A0 rapport");
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        // Social-moodlet-log-style list of conditioning events, newest first, with per-event deltas and time-ago.
        private void DrawEventLog(Rect rect, PawnProfile prof)
        {
            Widgets.DrawBoxSolid(rect, ModernStyle.BGD);
            var _bc = GUI.color; GUI.color = Color.black; Widgets.DrawBox(rect, 1); GUI.color = _bc;

            var evs = prof?.condEvents;
            if (evs == null || evs.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "No conditioning events yet.");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            int now = Find.TickManager.TicksGame;
            const float rowH = 42f;
            var inner = rect.ContractedBy(4f);
            var view = new Rect(0f, 0f, inner.width - 16f, evs.Count * rowH);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(inner, ref _logScroll, view);
            float ry = 0f;
            int shown = 0;
            for (int k = evs.Count - 1; k >= 0; k--, shown++)   // newest first
            {
                var e = evs[k];
                var row = new Rect(0f, ry, view.width, rowH - 2f);
                if (shown % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                Widgets.DrawBoxSolid(new Rect(row.x + 5f, row.y + 8f, 8f, 8f),
                    e.condDelta >= 0f ? new Color(0.45f, 0.85f, 0.5f) : new Color(0.9f, 0.42f, 0.42f));
                Widgets.Label(new Rect(row.x + 20f, row.y + 3f, row.width - 92f, 20f), e.label);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(row.xMax - 74f, row.y + 5f, 70f, 17f), AgoStr(now - e.tick) + " ago");
                Text.Anchor = TextAnchor.UpperLeft;
                float dx = row.x + 20f;
                if (e.condDelta != 0f) { GUI.color = CondColor; Widgets.Label(new Rect(dx, row.y + 23f, 128f, 17f), "conditioning " + Signed(e.condDelta)); dx += 126f; }
                if (e.rapDelta != 0f) { GUI.color = TrustColor; Widgets.Label(new Rect(dx, row.y + 23f, 116f, 17f), "rapport " + Signed(e.rapDelta)); }
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                ry += rowH;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // Prose life-story ledger, newest first: a colored kind-dot, the line, and time-ago.
        private void DrawChronicle(Rect rect, PawnProfile prof)
        {
            Widgets.DrawBoxSolid(rect, ModernStyle.BGD);
            var _bc = GUI.color; GUI.color = Color.black; Widgets.DrawBox(rect, 1); GUI.color = _bc;

            var ch = prof?.chronicle;
            if (ch == null || ch.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "No life events recorded yet.");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            int now = Find.TickManager.TicksGame;
            var inner = rect.ContractedBy(4f);
            Text.Font = GameFont.Tiny;
            float lineH = Mathf.Ceil(Text.LineHeight);
            float textW = inner.width - 16f - 18f - 52f;   // scrollbar + dot gutter + time column

            // Measure wrapped heights (newest first).
            float total = 0f;
            var heights = new float[ch.Count];
            for (int k = ch.Count - 1, idx = 0; k >= 0; k--, idx++)
            {
                float h = Mathf.Max(lineH + 6f, Text.CalcHeight(ch[k].text ?? "", textW) + 8f);
                heights[idx] = h; total += h;
            }

            var view = new Rect(0f, 0f, inner.width - 16f, total);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(inner, ref _chronScroll, view);
            float ry = 0f;
            for (int k = ch.Count - 1, idx = 0; k >= 0; k--, idx++)
            {
                var e = ch[k];
                float h = heights[idx];
                var row = new Rect(0f, ry, view.width, h - 2f);
                if (idx % 2 == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                Widgets.DrawBoxSolid(new Rect(row.x + 5f, row.y + 6f, 8f, 8f), ChronicleKindColor(e.kind));
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 18f, row.y + 2f, textW, h - 6f), e.text ?? "");
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(row.xMax - 52f, row.y + 2f, 50f, lineH + 2f), AgoStr(now - e.tick) + " ago");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                ry += h;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
            Text.Font = GameFont.Small;
        }

        private static Color ChronicleKindColor(int kind)
        {
            switch (kind)
            {
                case 1:  return new Color(0.90f, 0.42f, 0.42f); // dark
                case 2:  return new Color(0.45f, 0.85f, 0.5f);  // bright
                case 3:  return new Color(0.82f, 0.52f, 0.85f); // world
                default: return new Color(0.6f, 0.63f, 0.68f);  // neutral
            }
        }

        private void DrawGraphHover(Rect plot, List<float> cond, List<float> rap)
        {
            if (!Mouse.IsOver(plot)) return;
            int n = cond.Count;
            if (n < 2) return;
            float mx = Event.current.mousePosition.x;
            float f = Mathf.Clamp01((mx - plot.x) / plot.width);
            int i = Mathf.Clamp(Mathf.RoundToInt(f * (n - 1)), 0, n - 1);
            float xat = plot.x + plot.width * (i / (float)(n - 1));
            Widgets.DrawLine(new Vector2(xat, plot.y), new Vector2(xat, plot.yMax), new Color(1f, 1f, 1f, 0.25f), 1f);
            int cv = (int)cond[i];
            int rv = (rap != null && i < rap.Count) ? (int)rap[i] : 0;
            int hoursAgo = n - 1 - i;
            var mp = Event.current.mousePosition;
            var sz = new Vector2(114f, 48f);
            var b = new Rect(Mathf.Min(mp.x + 14f, plot.xMax - sz.x), Mathf.Min(mp.y + 8f, plot.yMax - sz.y), sz.x, sz.y);
            Widgets.DrawBoxSolid(b, new Color(0f, 0f, 0f, 0.88f));
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(b, 1); GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = CondColor; Widgets.Label(new Rect(b.x + 5f, b.y + 3f, sz.x - 8f, 14f), "conditioning " + cv + "%");
            GUI.color = TrustColor; Widgets.Label(new Rect(b.x + 5f, b.y + 17f, sz.x - 8f, 14f), "rapport " + rv + "%");
            GUI.color = new Color(1f, 1f, 1f, 0.6f); Widgets.Label(new Rect(b.x + 5f, b.y + 31f, sz.x - 8f, 14f), hoursAgo == 0 ? "now" : hoursAgo + "h ago");
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        private static string Signed(float d) => (d >= 0f ? "+" : "") + d.ToString("0.#");
        private static string AgoStr(int ticks)
        {
            int hours = ticks / 2500;
            if (hours < 24) return hours + "h";
            return (hours / 24) + "d " + (hours % 24) + "h";
        }

        // ── situation ────────────────────────────────────────────────────────
        private static string SituationLabel(Pawn pet, PawnProfile prof)
        {
            try
            {
                if (pet.Dead) return "dead";
                if (pet.Downed) return "downed";
                if (pet.jobs?.curDriver is rjw.JobDriver_Sex) return "being used";
                if (HarassmentEngine.IsInOnaholeBed(pet))
                    return (prof != null && prof.onaholeReleaseTick > 0 && Find.TickManager.TicksGame > prof.onaholeReleaseTick)
                        ? "begging to be let out" : "locked in an onahole";
                var jd = pet.CurJobDef;
                if (jd == RJWSH_JobDefOf.RJWSH_Scuffle) return "fighting back";
                if (prof != null)
                {
                    if (prof.boundInPublic) return "bound in public";
                    if (jd == RJWSH_JobDefOf.RJWSH_StayPut) return "staying put";
                    if (jd == RJWSH_JobDefOf.RJWSH_Follow) return "following owner";
                    if (Find.TickManager.TicksGame < prof.controlCooldownTick)
                        return "resting " + (prof.controlCooldownTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
                }
                if (HarassmentEngine.IsFullyConditioned(pet)) return "devoted";
                if (prof != null && prof.IsVolatile) return "volatile - may lash out";
                if (prof != null && prof.IsConditioned) return "conditioned";
                if (prof != null && prof.IsSuggestible) return "wavering";
                return "resisting";
            }
            catch { return "-"; }
        }

        // ── data ─────────────────────────────────────────────────────────────
        private List<Group> BuildGroups()
        {
            var byOwner = new Dictionary<int, Group>();
            var noOwner = new List<Pawn>();
            var gc = GameComponent_Harassment.Instance;
            if (gc == null) return new List<Group>();

            foreach (var map in Find.Maps)
            {
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null || !p.RaceProps.Humanlike || p.Dead) continue;
                    var prof = gc.GetProfileIfExists(p);
                    bool owned = prof != null && (prof.ownerId >= 0 || prof.relationshipOwnerId >= 0);
                    if (!owned && !HarassmentEngine.WearingControlCollar(p)) continue;

                    int ownerId = prof != null ? (prof.relationshipOwnerId >= 0 ? prof.relationshipOwnerId : prof.ownerId) : -1;
                    Pawn owner = ownerId >= 0 ? FindPawnById(ownerId) : null;
                    if (owner == null) { noOwner.Add(p); continue; }
                    if (!byOwner.TryGetValue(owner.thingIDNumber, out var g))
                    {
                        g = new Group { owner = owner, pets = new List<Pawn>() };
                        byOwner[owner.thingIDNumber] = g;
                    }
                    g.pets.Add(p);
                }
            }

            var result = new List<Group>(byOwner.Values);
            if (noOwner.Count > 0) result.Add(new Group { owner = null, pets = noOwner });
            return result;
        }

        // Routed through the shared per-tick pawn index (was a linear cross-map AllPawnsSpawned scan).
        private static Pawn FindPawnById(int id) => PawnLookup.AnyMap(id);

        private static void DrawPortrait(Rect rect, Pawn p)
        {
            if (p == null) { GUI.DrawTexture(rect, BaseContent.GreyTex); return; }
            try
            {
                var tex = PortraitsCache.Get(p, new Vector2(rect.width, rect.height), Rot4.South, default, 1.2f);
                GUI.DrawTexture(rect, tex);
            }
            catch { Widgets.ThingIcon(rect, p); }
        }

        // Border decoration: small alternating paw/collar icons in a footprint trail - each staggered slightly
        // in/out of the edge. No rotation (rotating GUI content leaks past the window's group clip rect).
        private static void DrawBorderIcons(Rect r)
        {
            var paw = HarassmentTextures.Paw;
            var collar = HarassmentTextures.CollarIcon;
            if (paw == null) return;
            GUI.color = PawTint;
            const float s = 17f; const float step = 40f; const float off = 6f;
            // Each edge alternates paw/collar every other icon (offset phase on opposite edges), so the pattern
            // weaves along all four sides rather than one type per side. Stagger tied to the same parity.
            int i;
            i = 0; for (float x = r.x + 12f; x < r.xMax - s - 10f; x += step, i++)
                GUI.DrawTexture(new Rect(x, r.y + 7f + (i & 1) * off, s, s), Ico(paw, collar, i));
            i = 1; for (float x = r.x + 12f; x < r.xMax - s - 10f; x += step, i++)
                GUI.DrawTexture(new Rect(x, r.yMax - s - 7f - (i & 1) * off, s, s), Ico(paw, collar, i));
            i = 0; for (float y = r.y + 7f + step; y < r.yMax - s - 7f - step * 0.6f; y += step, i++)
                GUI.DrawTexture(new Rect(r.x + 7f + (i & 1) * off, y, s, s), Ico(paw, collar, i));
            i = 1; for (float y = r.y + 7f + step; y < r.yMax - s - 7f - step * 0.6f; y += step, i++)
                GUI.DrawTexture(new Rect(r.xMax - s - 7f - (i & 1) * off, y, s, s), Ico(paw, collar, i));
            GUI.color = Color.white;
        }

        private static Texture2D Ico(Texture2D paw, Texture2D collar, int i)
            => (collar != null && i % 2 == 1) ? collar : paw;

        private static void DrawThinBorder(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            Widgets.DrawBox(r, 1);
            GUI.color = old;
        }

        private static readonly Dictionary<Color, Texture2D> _barCache = new Dictionary<Color, Texture2D>();
        private static Texture2D SolidBar(Color c)
        {
            if (!_barCache.TryGetValue(c, out var t)) { t = SolidColorMaterials.NewSolidColorTexture(c); _barCache[c] = t; }
            return t;
        }
    }
}
