using UnityEngine;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The clasped-hands graphic for the "hold hands" affection act. The texture is loaded at runtime from
    /// Show Me Your Hands' "Hand" texture (which Nice Hands Retexture overrides by load order), so we get that
    /// retexture for free and avoid bundling our own. If neither mod is installed the mote simply draws nothing
    /// and the affection act still plays (pose + interaction + mood).
    /// </summary>
    [StaticConstructorOnStartup] // has a static Material field - must load assets on the main thread
    public class Mote_Hands : Mote
    {
        // Const (not a literal at the call site) on purpose: these textures belong to Show Me Your Hands /
        // Nice Hands, so the asset scanner must NOT treat them as ours - no cs-assets entry, no shadowing stub.
        private const string HandTexName = "Hand";
        private const string HandCleanTexName = "HandClean";
        private static Material _mat;
        private static bool _tried;

        private static Material Mat()
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    var tex = ContentFinder<Texture2D>.Get(HandTexName, false)
                              ?? ContentFinder<Texture2D>.Get(HandCleanTexName, false);
                    if (tex != null) _mat = MaterialPool.MatFrom(new MaterialRequest(tex, ShaderDatabase.Transparent));
                }
                catch { }
            }
            return _mat;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var mat = Mat();
            if (mat == null) return;
            float a = Alpha;
            if (a <= 0.02f) return;
            Vector3 pos = drawLoc;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float size = def.graphicData != null ? def.graphicData.drawSize.x : 0.7f;
            var matrix = Matrix4x4.TRS(pos, Quaternion.AngleAxis(exactRotation, Vector3.up), new Vector3(size, 1f, size));
            var pb = new MaterialPropertyBlock();
            pb.SetColor(ShaderPropertyIDs.Color, new Color(1f, 1f, 1f, a));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0, null, 0, pb);
        }
    }
}
