using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The harasser's job: walk to the victim, deliver the verbal stage, roll escalation, and (when
    /// escalating and a private spot is reachable) carry the victim there. The physical stage is then
    /// handed off via the map component's deferred queue so it runs outside this job's context.
    /// </summary>
    public class JobDriver_Harass : JobDriver
    {
        private const TargetIndex VictimInd = TargetIndex.A;
        private const TargetIndex DestInd = TargetIndex.B;

        private bool escalate;
        private bool relocate;
        private bool carrying;
        private int lingerTicks; // remaining ticks to let the verbal exchange play out before escalating

        private Pawn Victim => job.GetTarget(VictimInd).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Soft reservation: never fail the job on a contested victim, just claim loosely.
            pawn.Reserve(job.GetTarget(VictimInd), job, 10, 0, null, false);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(VictimInd);
            this.FailOn(() => Victim == null || Victim.Dead);
            // Re-validate sex/orientation continuously through the approach: if the victim's sex changes
            // mid-walk (surgery, gene, RJW sex-change) so the pairing no longer matches the harasser's
            // orientation, abort rather than harassing the now-mismatched sex. IsAttracted already returns
            // true for downed-enemy opportunism and when orientation-respect is off, so those are unaffected.
            // Player-directed orders (hero-mode grope/force) bypass this - the player chose the target.
            if (!job.playerForced)
                this.FailOn(() => Victim != null && !HarassmentEngine.IsAttracted(pawn, Victim));

            // 1) Approach the victim.
            yield return Toils_Goto.GotoThing(VictimInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(VictimInd);

            // 2) Verbal stage + escalation decision + private-spot lookup.
            var decide = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                socialMode = RandomSocialMode.Off
            };
            decide.initAction = delegate
            {
                var v = Victim;
                if (v == null) { EndJobWith(JobCondition.Incompletable); return; }

                // Backstop the walk-time FailOn: sex/orientation may have flipped right as we arrived, so
                // re-check before firing the verbal/escalation stage on a now-mismatched target.
                if (!job.playerForced && !HarassmentEngine.IsAttracted(pawn, v))
                { EndJobWith(JobCondition.Incompletable); return; }

                // Wake a sleeping victim so they actually experience the harassment. WakeUp no-ops on a downed
                // or anesthetized pawn (it guards !Downed internally), so the spiked-drink knockout path is kept.
                if (!v.Awake()) RestUtility.WakeUp(v);

                var approach = (ApproachType)job.count;
                escalate = HarassmentEngine.ResolveApproachOnArrival(pawn, v, approach);

                // Verbal approaches get the full paced exchange (harasser AND victim held together) whether or
                // not it escalates; a downed victim (spiked drink) and player-directed grope/force skip to it.
                bool verbal = approach != ApproachType.Grope && approach != ApproachType.Forced;
                lingerTicks = (verbal && !v.Downed && v.Awake()) ? HarassmentEngine.ExchangeDuration() : 0;

                if (escalate && RimJobWorldSexualHarassmentMod.Settings.pullToPrivate &&
                    HarassmentEngine.TryFindPrivateCell(pawn, v, out var cell))
                {
                    job.SetTarget(DestInd, cell);
                    relocate = true;
                }
            };
            yield return decide;

            // Linger so the paced, approach-themed exchange plays out: the harasser shadows the victim
            // (staying adjacent so the bubbles keep firing) until the exchange finishes, then escalates.
            var linger = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                socialMode = RandomSocialMode.Off,
                handlingFacing = true
            };
            linger.initAction = delegate
            {
                // Hold the victim in place beside the harasser for the exchange so the two stay together.
                // The hold persists through escalation and releases shortly after the harasser moves on.
                var v = Victim;
                if (v != null && v.Spawned && !v.Dead && lingerTicks > 0 && v.jobs != null
                    && v.CurJobDef != RJWSH_JobDefOf.RJWSH_BeingStripped
                    && !(v.jobs.curDriver is rjw.JobDriver_Sex))
                {
                    v.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_BeingStripped, pawn), JobCondition.InterruptForced);
                }
            };
            linger.tickAction = delegate
            {
                var v = Victim;
                if (v == null || !v.Spawned || v.Dead) { ReadyForNextToil(); return; }
                if (lingerTicks <= 0) { ReadyForNextToil(); return; }
                lingerTicks--;
                if (pawn.Position.DistanceTo(v.Position) > 2.9f)
                {
                    if (pawn.pather != null && !pawn.pather.Moving) pawn.pather.StartPath(v, PathEndMode.Touch);
                }
                else
                {
                    pawn.pather?.StopDead();
                    pawn.rotationTracker?.FaceTarget(v);
                }
            };
            yield return linger;

            // A non-escalating approach (a catcall, a failed hypnosis) ends here - after the exchange has
            // played out with both pawns held together rather than the harasser wandering off mid-conversation.
            var endJob = Toils_General.Label();

            // Player-directed Grope is a discrete, visible grope: the gesture plus a held beat while the victim
            // reacts, with NO automatic strip/act escalation (that is what the Force option does). Emergent
            // gropes are unaffected - they run as the first step of the physical stage via BeginPhysical.
            var skipGrope = Toils_General.Label();
            yield return Toils_Jump.JumpIf(skipGrope, () => (ApproachType)job.count != ApproachType.Grope);
            var grope = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 160,
                handlingFacing = true,
                socialMode = RandomSocialMode.Off
            };
            grope.initAction = delegate
            {
                var v = Victim;
                if (v == null) return;
                HarassmentEngine.ApplyGrope(pawn, v);
                // Pin the victim beside the harasser so the grope reads as a moment, not an instant blip.
                if (v.Spawned && v.jobs != null && !v.Downed && v.Awake()
                    && v.CurJobDef != RJWSH_JobDefOf.RJWSH_BeingStripped
                    && !(v.jobs.curDriver is rjw.JobDriver_Sex))
                    v.jobs.StartJob(JobMaker.MakeJob(RJWSH_JobDefOf.RJWSH_BeingStripped, pawn), JobCondition.InterruptForced);
                // Offer to push the grope into a forced act, showing the victim's resist odds. If they fail the
                // roll it escalates (a strip job replaces this one); resist or back off leaves it at a grope.
                if (RimJobWorldSexualHarassmentMod.Settings.enableForced && v.Spawned && !v.Dead)
                    Find.WindowStack.Add(new Dialog_GropeEscalate(pawn, v));
            };
            grope.tickAction = delegate { var v = Victim; if (v != null) pawn.rotationTracker?.FaceTarget(v); };
            yield return grope;
            yield return Toils_Jump.Jump(endJob);
            yield return skipGrope;

            yield return Toils_Jump.JumpIf(endJob, () => !escalate);

            // Jump target placed at the end of the relocation block.
            var afterRelocate = Toils_General.Label();

            // Skip relocation entirely if no private spot was chosen.
            yield return Toils_Jump.JumpIf(afterRelocate, () => !relocate);

            // 3a) Grab the victim.
            var grab = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            grab.initAction = delegate
            {
                var v = Victim;
                if (v == null || !v.Spawned) { relocate = false; return; }
                v.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                // Use the (Thing,count) overload: it despawns the pawn via SplitOff before adding to
                // the carry container. The single-arg overload does a bare TryAdd and refuses a pawn
                // that is still parented to the map ThingOwner.
                carrying = pawn.carryTracker.TryStartCarry(v, 1, false) > 0;
                if (!carrying) relocate = false;
            };
            yield return grab;

            // If the grab failed, skip the move/drop and harass where we stand.
            yield return Toils_Jump.JumpIf(afterRelocate, () => !carrying);

            // 3b) Carry the victim to the private spot.
            yield return Toils_Goto.GotoCell(DestInd, PathEndMode.OnCell);

            // 3c) Put the victim down.
            var drop = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            drop.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return drop;

            yield return afterRelocate;

            // 4) Hand the physical stage off to next tick (outside this job) to avoid reentrancy.
            var handoff = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            handoff.initAction = delegate
            {
                var v = Victim;
                if (escalate && v != null && v.Spawned && !v.Dead)
                {
                    // Player-ordered (hero-mode menu) harassment forces the act non-interactively; emergent
                    // harassment keeps the intervene gate (lets the player react when their pawn is the victim).
                    if (job.playerForced)
                        MapComponent_HarassmentScan.EnqueueDirectedPhysical(pawn, v);
                    else
                        MapComponent_HarassmentScan.EnqueuePhysical(pawn, v);
                }
            };
            yield return handoff;

            yield return endJob;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref escalate, "escalate", false);
            Scribe_Values.Look(ref relocate, "relocate", false);
            Scribe_Values.Look(ref carrying, "carrying", false);
            Scribe_Values.Look(ref lingerTicks, "lingerTicks", 0);
        }
    }
}
