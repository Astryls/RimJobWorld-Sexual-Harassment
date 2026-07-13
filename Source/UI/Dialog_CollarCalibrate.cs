using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Live calibration tool for the control collar's worn size + position. Drag the sliders and the collar on
    /// the previewed pawn updates in real time (mutates the shared def's graphicData.drawSize + apparel.drawData
    /// offset, then refreshes graphics). "Copy values" puts the final numbers on the clipboard / in the log so
    /// they can be baked permanently into the def.
    /// </summary>
    public class Dialog_CollarCalibrate : Window
    {
        private readonly Pawn pawn;
        private float scale = 1f;
        private float offX = 0f;
        private float offZ = 0f;
        private float lastScale = -999f, lastX = -999f, lastZ = -999f;

        public override Vector2 InitialSize => new Vector2(460f, 320f);

        public Dialog_CollarCalibrate(Pawn pawn)
        {
            this.pawn = pawn;
            forcePause = false;
            draggable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
            doCloseX = true;
            absorbInputAroundWindow = false;

            var def = RJWSH_ThingDefOf.RJWSH_ControlCollar;
            if (def?.apparel?.drawData != null) scale = def.apparel.drawData.scale;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Small;
            l.Label("Control collar calibration");
            Text.Font = GameFont.Tiny;
            l.Label("Live preview on " + (pawn != null ? pawn.LabelShort : "?") + ". Changes apply to every control collar.");
            Text.Font = GameFont.Small;
            l.GapLine(6f);

            l.Label("Size: " + scale.ToString("F2"));
            scale = l.Slider(scale, 0.2f, 1.5f);
            l.Label("Offset left / right: " + offX.ToString("F3"));
            offX = l.Slider(offX, -0.35f, 0.35f);
            l.Label("Offset up / down: " + offZ.ToString("F3"));
            offZ = l.Slider(offZ, -0.35f, 0.35f);
            l.Gap(8f);

            if (l.ButtonText("Copy values (clipboard + log)")) CopyValues();
            if (l.ButtonText("Reset")) { scale = 1f; offX = 0f; offZ = 0f; }
            l.End();

            if (!Mathf.Approximately(scale, lastScale) || !Mathf.Approximately(offX, lastX) || !Mathf.Approximately(offZ, lastZ))
            {
                lastScale = scale; lastX = offX; lastZ = offZ;
                Apply();
            }
        }

        private void Apply()
        {
            var def = RJWSH_ThingDefOf.RJWSH_ControlCollar;
            if (def?.apparel == null) return;
            if (def.apparel.drawData == null)
                def.apparel.drawData = DrawData.NewWithData(new DrawData.RotationalData(null, 48f), new DrawData.RotationalData(Rot4.North, 68f));
            var dd = def.apparel.drawData;
            dd.scale = scale; // read every frame by PawnRenderNodeWorker.ScaleFor -> live size change
            SetRotOffset(dd, "defaultData", nonNullable: true);
            SetRotOffset(dd, "dataNorth", nonNullable: false);
            try { pawn?.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
        }

        // The per-rotation offsets live in private RotationalData fields; mutate them in place via reflection so
        // the existing drawData object (referenced by the live render node) updates without a full rebuild.
        private void SetRotOffset(DrawData dd, string fieldName, bool nonNullable)
        {
            var fld = typeof(DrawData).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fld == null) return;
            var off = new Vector3(offX, 0f, offZ);
            try
            {
                if (nonNullable)
                {
                    var rd = (DrawData.RotationalData)fld.GetValue(dd);
                    rd.offset = off;
                    fld.SetValue(dd, rd);
                }
                else
                {
                    var cur = (DrawData.RotationalData?)fld.GetValue(dd);
                    var rd = cur ?? new DrawData.RotationalData(Rot4.North, 68f);
                    rd.offset = off;
                    fld.SetValue(dd, (DrawData.RotationalData?)rd);
                }
            }
            catch { }
        }

        private void CopyValues()
        {
            string s = "drawData scale " + scale.ToString("F2")
                + " | offset (" + offX.ToString("F3") + ", 0, " + offZ.ToString("F3") + ")";
            GUIUtility.systemCopyBuffer = s;
            Log.Message("[RJWSH collar calibration] " + s);
            Messages.Message("Collar values copied: " + s, MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
