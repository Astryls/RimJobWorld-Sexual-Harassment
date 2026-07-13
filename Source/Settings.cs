using System.Collections.Generic;
using Verse;

namespace RJWSexualHarassment
{
    public class HarassmentSettings : ModSettings
    {
        // ── Master ────────────────────────────────────────────────────────────
        public bool masterEnabled = true;
        // Play short vanilla sound cues on collar lock / shock (uses existing game sounds, ships no audio).
        public bool enableSounds = true;

        // How often the per-map scan runs, in ticks (lower = more frequent checks).
        public int scanIntervalTicks = 1250;
        // Chance, per scan, that the map attempts one harassment event. 0..1.
        public float eventChancePerScan = 0.25f;

        // ── Approach-type enables ─────────────────────────────────────────────
        public bool enableCatcall = true;
        public bool enableProposition = true;
        public bool enableFlirt = true;
        public bool enableGrope = true;
        public bool enableForced = true;
        public bool enableSpikedDrink = true;
        // Chance an evil/questionable fan approach spikes the drink (vs a genuine friendly gesture).
        public float fanSpikeChance = 0.5f;
        public float hypnosisBaseChance = 0.45f;  // base odds a hypnosis session takes (before sensitivity/conditioning/mood)
        public bool enableHypnosis = true;
        public bool enableBlackmail = true;
        public bool enableDeviousDevice = true;
        // Chance a rapist locks an RJW bondage device onto the victim after a forced act.
        public bool enableDeviceLockAfterRape = true;
        public float deviceLockChance = 0.25f;
        // How many devices can be locked on one victim, and the chance to add each extra one.
        public int maxLockedDevices = 1;
        public float extraDeviceChance = 0.35f;
        // Onahole Extension compat: chance a rapist drags the victim to a public spot and locks them in an onahole.
        public bool enableOnaholeCapture = true;
        public float onaholeCaptureChance = 0.2f;
        // Bound in public: chance a rapist hauls the victim to a public spot and leaves them locked in a device.
        public bool enableBoundInPublic = true;
        public float boundInPublicChance = 0.15f;
        // Flee-the-beating: a disciplined pet may bolt; the owner then escalates (onahole / beatdown / gangbang,
        // or - only if enabled - a low-chance beating to death).
        public bool enableFleeBeating = true;
        public bool enableBeatToDeath = false;
        public float beatToDeathChance = 0.08f;
        // UI: hide the key-holder control gizmos and drive control from the Pet Dashboard + Control tab instead.
        public bool hideKeyHolderGizmos = false;
        public bool showControlTab = true;
        // Off by default: the standalone Sexuality inspect tab + the Harem window's "Profile" context tab.
        public bool showSexualityTab = false;
        // A pawn under active control loses free will (integrates with a detected free-will mod).
        public bool suppressFreeWillWhenControlled = true;
        // Gangbang: let extra aggressors join an ongoing act so c0ffee Animations composes a group (MMF+) animation.
        public bool enableGangbangMMF = true;
        public int gangbangMaxActors = 2; // max simultaneous aggressors on the victim (2 = MMF)

        // Per-approach cooldowns (ticks): after an approach type fires on a map, it cannot be re-picked there
        // until the cooldown elapses. 0 = no extra cooldown. 2500 ticks = 1 in-game hour.
        public int cooldownCatcall = 0;
        public int cooldownProposition = 0;
        public int cooldownFlirt = 0;
        public int cooldownSpikedDrink = 0;
        public int cooldownHypnosis = 0;
        public int cooldownBlackmail = 0;
        public int cooldownDeviousDevice = 0;

        public int ApproachCooldown(ApproachType t)
        {
            switch (t)
            {
                case ApproachType.Catcall: return cooldownCatcall;
                case ApproachType.Proposition: return cooldownProposition;
                case ApproachType.Flirt: return cooldownFlirt;
                case ApproachType.SpikedDrink: return cooldownSpikedDrink;
                case ApproachType.Hypnosis: return cooldownHypnosis;
                case ApproachType.Blackmail: return cooldownBlackmail;
                case ApproachType.DeviousDevice: return cooldownDeviousDevice;
                default: return 0;
            }
        }

        // Cooldowns (anti-spam):
        public int gizmoCooldownTicks = 600;        // single-Shock per-victim cooldown (~10s)
        public int commandCooldownTicks = 2500;     // Command per-victim cooldown (~1 in-game hour)
        public int autoServiceIntervalTicks = 15000; // auto-cast service interval (~6 in-game hours)
        public float circulationPhotoChance = 0.04f; // chance a newly-spawned adult carries a photo already in circulation
        public int minTicksBetweenEvents = 2500;    // per-map minimum gap between harassment events
        // Chance an evil witness photographs a sex act they see.
        public float photoCaptureChance = 0.35f;
        // Witness photos: each nearby onlooker who can see the act has a chance to snap their own photo of
        // the female participant, kept in their inventory (feeds circulation/blackmail).
        public bool enableWitnessPhotos = true;
        public float witnessPhotoChance = 0.10f;
        // Multi-line harassment: after a verbal approach, fire a short back-and-forth of SpeakUp bubbles.
        public bool multiLineHarassment = true;
        public int harassmentExtraLines = 4;
        public int harassmentLineSpacing = 150; // ticks between each line of a multi-line exchange
        public int preActBeatTicks = 150;       // pause after the strip, before the sex act begins (a beat to watch)
        // Scene extension: after a forced act, a chance the attacker drags the victim somewhere private for
        // another round (capped per scene), generating more dialogue.
        public bool enableSceneExtend = true;
        public float sceneExtendChance = 0.35f;
        public int maxSceneExtends = 2;

        // When escalating, the harasser carries the victim to a private spot (their bedroom) first.
        public bool pullToPrivate = true;

        // Master toggle: allow escalation past verbal into physical/forced at all.
        public bool allowEscalation = true;
        // Base chance that a verbal event escalates toward physical (modified by morality/vuln).
        public float baseEscalationChance = 0.35f;
        // Base chance that a physical event escalates into a forced RJW act.
        public float baseForcedChance = 0.4f;

        // ── Intervene gate ────────────────────────────────────────────────────
        // Fire the pause popup before physical/forced when a player colonist is involved.
        public bool interveneGateEnabled = true;
        // Also gate the later-phase approaches (hypnosis / blackmail / devious devices) when added.
        public bool interveneGateLaterPhases = true;
        // Base success chance for an intervention attempt (modified by intervener stats).
        public float baseInterveneChance = 0.55f;

        // ── Gender / orientation gating ───────────────────────────────────────
        // Victims fight back: lets visitors and raiders (NOT slaves/prisoners) harass, rape, and
        // collar/onahole colonists - and flee the map with the key, spawning a recovery letter + warrant.
        public bool allowVictimAggressors = true;
        // Evil pawns autonomously grab a Holokey found on the ground and lord over a conditioned, collared pawn.
        public bool enableKeyScavenging = true;
        // Any pawn (colonists, visitors, raiders) may wander over and pocket a scandalous photo left on the
        // ground, even in storage; cruel/greedy pawns are far likelier to grab one.
        public bool enablePhotoScavenging = true;
        // Manual Discipline from the Command deck opens an interactive training session (choice prompts).
        public bool interactiveTraining = true;
        // Conditioning permanently installs RJW quirks (Cumslut, Exhibitionist) at its milestones.
        public bool conditioningInstallsQuirks = true;
        // Full-break conditioning etches a heritable RJW Genes gene (hypersexual) - needs Biotech + RJW Genes.
        public bool conditioningInstallsGene = false;
        // Deeply submissive pets get permanent piercings/brand (subDom thresholds). Permanent - off by default.
        public bool conditioningMarksBody = false;
        // Show + drive the Submission need bar on conditioned/owned pets.
        public bool enableSubmissionNeed = true;
        // After a rape, the aggressor may brand their victim with a degrading tattoo (needs Ideology).
        public bool rapistMayTattoo = true;
        // A worn-out pawn gives less satisfying sex (partner satisfaction drops with the used hole's wear).
        public bool wearReducesSexQuality = true;
        // A pet marked head girl disciplines misbehaving / below-quota pets, enforcing the pecking order.
        public bool enableHeadGirl = true;
        // Auto-pick the head girl per owner as the best-performing pet (re-evaluated over time).
        public bool autoHeadGirl = true;
        // Evil key-holders refuse to drop the key (blocks player drop via gear tab / True RPG Inventory).
        public bool enableKeyRefuse = true;
        // Slave will: a controlled slave has a will meter and periodically attempts a breakout (fail -> public onahole).
        public bool enableSlaveWill = true;
        public float breakoutChanceFactor = 0.3f;
        // Controllers make the slave do what they like (sadist/masochist/zoophile/talk-down) based on their traits.
        public bool enableControllerBehaviors = true;
        // Owner/slave relationship shown on the vanilla social tab + its naming scheme.
        public bool enableOwnerRelationship = true;
        public RelationScheme relationScheme = RelationScheme.OwnerPet;

        public bool respectOrientation = true;   // harasser must be attracted to target's sex
        public bool enforceOppositeSex = true;   // hard override: only ever target the opposite sex (default on)
        public bool heterosexualOnly = false;    // master: every mod-driven pairing is strictly M<->F only
        public bool allowFemaleHarassers = false; // only men harass by default
        public bool allowMaleHarassers = true;
        // Age ranges (biological years, adults only). Harasser and victim must both fall within their bounds.
        public int harasserMinAge = 18;
        public int harasserMaxAge = 99;
        public int victimMinAge = 18;
        public int victimMaxAge = 99;

        // ── Who-harasses-whom matrix ──────────────────────────────────────────
        // Keys are "HarasserCategory>TargetCategory" (e.g. "Colonist>Prisoner").
        public HashSet<string> allowedPairs;

        public static string PairKey(PawnCategory harasser, PawnCategory target) => harasser + ">" + target;

        public bool IsPairAllowed(PawnCategory harasser, PawnCategory target)
        {
            EnsureDefaults();
            return allowedPairs.Contains(PairKey(harasser, target));
        }

        public void EnsureDefaults()
        {
            if (allowedPairs == null)
            {
                allowedPairs = new HashSet<string>
                {
                    PairKey(PawnCategory.Colonist, PawnCategory.Colonist),
                    PairKey(PawnCategory.Colonist, PawnCategory.Slave),
                    PairKey(PawnCategory.Colonist, PawnCategory.Prisoner),
                    PairKey(PawnCategory.Visitor,  PawnCategory.Colonist),
                };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref masterEnabled, "masterEnabled", true);
            Scribe_Values.Look(ref enableSounds, "enableSounds", true);
            Scribe_Values.Look(ref scanIntervalTicks, "scanIntervalTicks", 1250);
            Scribe_Values.Look(ref eventChancePerScan, "eventChancePerScan", 0.25f);

            Scribe_Values.Look(ref enableCatcall, "enableCatcall", true);
            Scribe_Values.Look(ref enableProposition, "enableProposition", true);
            Scribe_Values.Look(ref enableFlirt, "enableFlirt", true);
            Scribe_Values.Look(ref enableGrope, "enableGrope", true);
            Scribe_Values.Look(ref enableForced, "enableForced", true);
            Scribe_Values.Look(ref enableSpikedDrink, "enableSpikedDrink", true);
            Scribe_Values.Look(ref fanSpikeChance, "fanSpikeChance", 0.5f);
            Scribe_Values.Look(ref hypnosisBaseChance, "hypnosisBaseChance", 0.45f);
            Scribe_Values.Look(ref enableHypnosis, "enableHypnosis", true);
            Scribe_Values.Look(ref enableBlackmail, "enableBlackmail", true);
            Scribe_Values.Look(ref enableDeviousDevice, "enableDeviousDevice", true);
            Scribe_Values.Look(ref enableDeviceLockAfterRape, "enableDeviceLockAfterRape", true);
            Scribe_Values.Look(ref deviceLockChance, "deviceLockChance", 0.25f);
            Scribe_Values.Look(ref maxLockedDevices, "maxLockedDevices", 1);
            Scribe_Values.Look(ref extraDeviceChance, "extraDeviceChance", 0.35f);
            Scribe_Values.Look(ref enableOnaholeCapture, "enableOnaholeCapture", true);
            Scribe_Values.Look(ref onaholeCaptureChance, "onaholeCaptureChance", 0.2f);
            Scribe_Values.Look(ref enableFleeBeating, "enableFleeBeating", true);
            Scribe_Values.Look(ref enableBeatToDeath, "enableBeatToDeath", false);
            Scribe_Values.Look(ref beatToDeathChance, "beatToDeathChance", 0.08f);
            Scribe_Values.Look(ref hideKeyHolderGizmos, "hideKeyHolderGizmos", false);
            Scribe_Values.Look(ref showControlTab, "showControlTab", true);
            Scribe_Values.Look(ref showSexualityTab, "showSexualityTab", false);
            Scribe_Values.Look(ref suppressFreeWillWhenControlled, "suppressFreeWillWhenControlled", true);
            Scribe_Values.Look(ref enableGangbangMMF, "enableGangbangMMF", true);
            Scribe_Values.Look(ref gangbangMaxActors, "gangbangMaxActors", 2);
            Scribe_Values.Look(ref enableBoundInPublic, "enableBoundInPublic", true);
            Scribe_Values.Look(ref boundInPublicChance, "boundInPublicChance", 0.15f);
            Scribe_Values.Look(ref cooldownCatcall, "cooldownCatcall", 0);
            Scribe_Values.Look(ref cooldownProposition, "cooldownProposition", 0);
            Scribe_Values.Look(ref cooldownFlirt, "cooldownFlirt", 0);
            Scribe_Values.Look(ref cooldownSpikedDrink, "cooldownSpikedDrink", 0);
            Scribe_Values.Look(ref cooldownHypnosis, "cooldownHypnosis", 0);
            Scribe_Values.Look(ref cooldownBlackmail, "cooldownBlackmail", 0);
            Scribe_Values.Look(ref cooldownDeviousDevice, "cooldownDeviousDevice", 0);
            Scribe_Values.Look(ref gizmoCooldownTicks, "gizmoCooldownTicks", 600);
            Scribe_Values.Look(ref commandCooldownTicks, "commandCooldownTicks", 2500);
            Scribe_Values.Look(ref autoServiceIntervalTicks, "autoServiceIntervalTicks", 15000);
            Scribe_Values.Look(ref circulationPhotoChance, "circulationPhotoChance", 0.04f);
            Scribe_Values.Look(ref minTicksBetweenEvents, "minTicksBetweenEvents", 2500);
            Scribe_Values.Look(ref photoCaptureChance, "photoCaptureChance", 0.35f);
            Scribe_Values.Look(ref enableWitnessPhotos, "enableWitnessPhotos", true);
            Scribe_Values.Look(ref witnessPhotoChance, "witnessPhotoChance", 0.10f);
            Scribe_Values.Look(ref multiLineHarassment, "multiLineHarassment", true);
            Scribe_Values.Look(ref harassmentExtraLines, "harassmentExtraLines", 4);
            Scribe_Values.Look(ref harassmentLineSpacing, "harassmentLineSpacing", 150);
            Scribe_Values.Look(ref preActBeatTicks, "preActBeatTicks", 150);
            Scribe_Values.Look(ref enableSceneExtend, "enableSceneExtend", true);
            Scribe_Values.Look(ref sceneExtendChance, "sceneExtendChance", 0.35f);
            Scribe_Values.Look(ref maxSceneExtends, "maxSceneExtends", 2);

            Scribe_Values.Look(ref pullToPrivate, "pullToPrivate", true);
            Scribe_Values.Look(ref allowEscalation, "allowEscalation", true);
            Scribe_Values.Look(ref baseEscalationChance, "baseEscalationChance", 0.35f);
            Scribe_Values.Look(ref baseForcedChance, "baseForcedChance", 0.4f);

            Scribe_Values.Look(ref interveneGateEnabled, "interveneGateEnabled", true);
            Scribe_Values.Look(ref interveneGateLaterPhases, "interveneGateLaterPhases", true);
            Scribe_Values.Look(ref baseInterveneChance, "baseInterveneChance", 0.55f);

            Scribe_Values.Look(ref allowVictimAggressors, "allowVictimAggressors", true);
            Scribe_Values.Look(ref enableKeyScavenging, "enableKeyScavenging", true);
            Scribe_Values.Look(ref enablePhotoScavenging, "enablePhotoScavenging", true);
            Scribe_Values.Look(ref interactiveTraining, "interactiveTraining", true);
            Scribe_Values.Look(ref conditioningInstallsQuirks, "conditioningInstallsQuirks", true);
            Scribe_Values.Look(ref conditioningInstallsGene, "conditioningInstallsGene", false);
            Scribe_Values.Look(ref conditioningMarksBody, "conditioningMarksBody", false);
            Scribe_Values.Look(ref enableSubmissionNeed, "enableSubmissionNeed", true);
            Scribe_Values.Look(ref rapistMayTattoo, "rapistMayTattoo", true);
            Scribe_Values.Look(ref wearReducesSexQuality, "wearReducesSexQuality", true);
            Scribe_Values.Look(ref enableHeadGirl, "enableHeadGirl", true);
            Scribe_Values.Look(ref autoHeadGirl, "autoHeadGirl", true);
            Scribe_Values.Look(ref enableKeyRefuse, "enableKeyRefuse", true);
            Scribe_Values.Look(ref enableSlaveWill, "enableSlaveWill", true);
            Scribe_Values.Look(ref breakoutChanceFactor, "breakoutChanceFactor", 0.3f);
            Scribe_Values.Look(ref enableControllerBehaviors, "enableControllerBehaviors", true);
            Scribe_Values.Look(ref enableOwnerRelationship, "enableOwnerRelationship", true);
            Scribe_Values.Look(ref relationScheme, "relationScheme", RelationScheme.OwnerPet);
            Scribe_Values.Look(ref respectOrientation, "respectOrientation", true);
            Scribe_Values.Look(ref enforceOppositeSex, "enforceOppositeSex", true);
            Scribe_Values.Look(ref heterosexualOnly, "heterosexualOnly", false);
            Scribe_Values.Look(ref allowFemaleHarassers, "allowFemaleHarassers", false);
            Scribe_Values.Look(ref allowMaleHarassers, "allowMaleHarassers", true);
            Scribe_Values.Look(ref harasserMinAge, "harasserMinAge", 18);
            Scribe_Values.Look(ref harasserMaxAge, "harasserMaxAge", 99);
            Scribe_Values.Look(ref victimMinAge, "victimMinAge", 18);
            Scribe_Values.Look(ref victimMaxAge, "victimMaxAge", 99);

            Scribe_Collections.Look(ref allowedPairs, "allowedPairs", LookMode.Value);
            EnsureDefaults();
        }
    }
}
