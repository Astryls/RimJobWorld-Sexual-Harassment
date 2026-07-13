using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Harasser's timed, layer-by-layer forced strip. job.count == 1 means the victim submitted
    /// (faster, no damage, no break-free); otherwise it is a resisted strip (slower, occasional
    /// unarmed melee blows, a per-layer chance for the victim to break free). On full strip it hands
    /// off to the RJW forced act via the deferred queue.
    /// </summary>
    public class JobDriver_HarassStrip : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;

        private int ticksToNextLayer;
        private int layersDone;
        private int layersTotal;
        private int faceTick;
        private bool repelled;
        private int beatLeft;   // pause after the strip, before the act, so the scene has a beat to watch

        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;
        private bool Submitted => job.count == 1;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Reserve(job.GetTarget(VictimInd), job, 10, 0, null, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(VictimInd);
            this.FailOn(() => Victim == null || Victim.Dead);

            yield return Toils_Goto.GotoThing(VictimInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(VictimInd);

            int interval = Submitted ? 75 : 150;

            var strip = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                handlingFacing = true
            };
            strip.initAction = delegate
            {
                var v = Victim;
                if (v == null) { EndJobWith(JobCondition.Incompletable); return; }
                layersTotal = HarassmentEngine.WornCount(v);
                layersDone = 0;
                ticksToNextLayer = interval;

                // Pin the victim beside the harasser for the duration.
                if (v.Spawned && v.jobs != null && v.CurJobDef != RJWSH_JobDefOf.RJWSH_BeingStripped)
                    v.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_BeingStripped, pawn),
                        JobCondition.InterruptForced);

                // Facial Animation: both pawns get the "Strip" blush look during the strip.
                FABridge.PlayFace(v, "Strip");
                FABridge.PlayFace(pawn, "Strip");
            };
            strip.tickAction = delegate
            {
                var v = Victim;
                if (v == null || v.Dead || !v.Spawned) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker?.FaceTarget(v);

                // Re-assert the blush so it persists across the whole strip.
                if (++faceTick >= 180) { faceTick = 0; FABridge.PlayFace(v, "Strip"); FABridge.PlayFace(pawn, "Strip"); }

                // Downed/unconscious victims can't resist (and could be hauled off by a rescuer mid-strip),
                // so strip them at once and go straight to the act. Also avoids the beating killing them.
                if (v.Downed || !v.Awake())
                {
                    HarassmentEngine.StripAll(v);
                    ReadyForNextToil();
                    return;
                }

                // Nothing (left) to remove: finish immediately.
                if (layersTotal <= 0 || HarassmentEngine.WornCount(v) == 0)
                {
                    ReadyForNextToil();
                    return;
                }

                if (--ticksToNextLayer > 0) return;
                ticksToNextLayer = interval;

                HarassmentEngine.StripOneLayer(v);
                layersDone++;

                if (!Submitted)
                {
                    if (Rand.Chance(0.4f)) HarassmentEngine.DealStripDamage(pawn, v);
                    if (Rand.Chance(HarassmentEngine.StruggleChance(pawn, v)))
                    {
                        repelled = true;
                        ReadyForNextToil();
                        return;
                    }
                }

                if (layersDone >= layersTotal || HarassmentEngine.WornCount(v) == 0)
                    ReadyForNextToil();
            };
            yield return strip;

            // Beat: the offender stands over the stripped victim for a moment before the act begins (the
            // victim stays held because this is still the strip job). Skipped if the victim broke free.
            var beat = new Toil { defaultCompleteMode = ToilCompleteMode.Never, handlingFacing = true };
            beat.initAction = delegate
            {
                beatLeft = repelled ? 0 : System.Math.Max(0, RimJobWorldSexualHarassmentMod.Settings.preActBeatTicks);
            };
            beat.tickAction = delegate
            {
                var v = Victim;
                if (v == null || v.Dead || !v.Spawned || beatLeft <= 0) { ReadyForNextToil(); return; }
                beatLeft--;
                pawn.rotationTracker?.FaceTarget(v);
            };
            yield return beat;

            var finish = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            finish.initAction = delegate
            {
                var v = Victim;
                if (repelled) HarassmentEngine.OnRepelled(pawn, v);
                else HarassmentEngine.CompleteStripToForced(pawn, v);
            };
            yield return finish;

            // Safety net: if the job fails before resolving, clear the in-progress guard.
            AddFinishAction(delegate { HarassmentEngine.EndPhysical(Victim); });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksToNextLayer, "ticksToNextLayer", 0);
            Scribe_Values.Look(ref layersDone, "layersDone", 0);
            Scribe_Values.Look(ref layersTotal, "layersTotal", 0);
            Scribe_Values.Look(ref repelled, "repelled", false);
            Scribe_Values.Look(ref beatLeft, "beatLeft", 0);
        }
    }
}
