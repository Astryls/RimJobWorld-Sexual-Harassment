using System;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    public class RimJobWorldSexualHarassmentMod : Mod
    {
        public static HarassmentSettings Settings;

        private Vector2 _scroll;

        public RimJobWorldSexualHarassmentMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<HarassmentSettings>();
            Settings.EnsureDefaults();
        }

        public override string SettingsCategory() => "RimJobWorld - Sexual Harassment";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var viewRect = new Rect(0f, 0f, inRect.width - 24f, 3100f);
            Widgets.BeginScrollView(inRect, ref _scroll, viewRect);

            var l = new Listing_Standard();
            l.Begin(viewRect);

            l.CheckboxLabeled("Enable sexual harassment", ref Settings.masterEnabled,
                "Master switch. When off, no harassment events are generated.");
            l.CheckboxLabeled("Sound cues (collar lock, shock)", ref Settings.enableSounds,
                "Play short vanilla sound cues on collar lock and shocks. Ships no audio - reuses existing game sounds.");
            l.CheckboxLabeled("Interactive training sessions", ref Settings.interactiveTraining,
                "When on, the Command deck's Discipline button opens a short training session with choice prompts - read the pet's state and pick the right approach. When off, Discipline is immediate (classic).");
            l.GapLine();

            l.Label("Scan interval: " + Settings.scanIntervalTicks + " ticks");
            Settings.scanIntervalTicks = (int)l.Slider(Settings.scanIntervalTicks, 250f, 5000f);

            l.Label("Event chance per scan: " + Settings.eventChancePerScan.ToStringPercent());
            Settings.eventChancePerScan = l.Slider(Settings.eventChancePerScan, 0f, 1f);
            l.Label("Minimum ticks between events: " + Settings.minTicksBetweenEvents);
            Settings.minTicksBetweenEvents = (int)l.Slider(Settings.minTicksBetweenEvents, 0f, 15000f);
            l.Label("Control gizmo cooldown: " + Settings.gizmoCooldownTicks + " ticks");
            Settings.gizmoCooldownTicks = (int)l.Slider(Settings.gizmoCooldownTicks, 0f, 5000f);
            l.Label("Auto-service interval: " + Settings.autoServiceIntervalTicks + " ticks");
            Settings.autoServiceIntervalTicks = (int)l.Slider(Settings.autoServiceIntervalTicks, 600f, 15000f);
            l.GapLine();

            l.Label("Performance");
            l.CheckboxLabeled("Depth simulation (rivalry, pecking order, autonomy)", ref Settings.enableDepthSystems,
                "The per-pet depth layer: rivalry, pecking order, codependency, ongoing training focus, autonomous acts and addiction pulls. The heaviest optional upkeep. Turn off on large colonies to save TPS - core break-in progression and the visible needs/hediffs keep running either way.");
            l.CheckboxLabeled("Ambient pet chatter", ref Settings.enableAmbientBanter,
                "Idle flavor speech from owned and collared pets (self-talk, two pets commiserating, owner-slave lines). Pure flavor with no mechanical effect - the main source of idle speech-bubble work. Off = silent pets, lighter load.");
            if (Settings.enableAmbientBanter)
            {
                l.Label("Ambient chatter frequency: " + Settings.ambientBanterScale.ToStringPercent());
                Settings.ambientBanterScale = l.Slider(Settings.ambientBanterScale, 0.1f, 1f);
            }
            l.Label("Ambient affection interval: " + (Settings.affectionInterval > 0 ? Settings.affectionInterval + " ticks" : "off"));
            Settings.affectionInterval = (int)l.Slider(Settings.affectionInterval, 0f, 12000f);
            l.Label("Captive begging interval: " + (Settings.begInterval > 0 ? Settings.begInterval + " ticks" : "off"));
            Settings.begInterval = (int)l.Slider(Settings.begInterval, 0f, 3000f);
            l.GapLine();

            l.Label("Approach types");
            l.CheckboxLabeled("Catcalls (mild verbal)", ref Settings.enableCatcall);
            l.CheckboxLabeled("Propositions (demanding verbal)", ref Settings.enableProposition);
            l.CheckboxLabeled("Flirt (consensual, or coercive if refused)", ref Settings.enableFlirt,
                "A friendly approach. A willing target leads to consensual sex; a pushy harasser who is refused may turn coercive.");
            l.CheckboxLabeled("Fan / spiked drinks", ref Settings.enableSpikedDrink,
                "A pawn offers the target a drink. Decent pawns give a genuine treat; others may spike it to knock the target out.");
            l.Label("Fan drink spike chance: " + Settings.fanSpikeChance.ToStringPercent());
            Settings.fanSpikeChance = l.Slider(Settings.fanSpikeChance, 0f, 1f);
            l.CheckboxLabeled("Hypnosis (therapy session, conditioning)", ref Settings.enableHypnosis,
                "A pawn offers a hypnosis session. Repeated sessions condition the target; a fully conditioned pawn becomes compliant and can be commanded by the colony.");
            l.CheckboxLabeled("Blackmail (scandalous photos)", ref Settings.enableBlackmail,
                "Evil pawns may photograph sex acts they witness, then use the photo to coerce the subject. Find and destroy the photo to defuse it.");
            l.Label("Photo capture chance (per witnessed act): " + Settings.photoCaptureChance.ToStringPercent());
            Settings.photoCaptureChance = l.Slider(Settings.photoCaptureChance, 0f, 1f);
            l.CheckboxLabeled("Onlookers photograph the female", ref Settings.enableWitnessPhotos,
                "Each nearby pawn who can see a sex act has a chance to secretly photograph the female participant and pocket the copy. You get a warning when it happens to one of your colonists.");
            l.Label("Per-onlooker photo chance: " + Settings.witnessPhotoChance.ToStringPercent());
            Settings.witnessPhotoChance = l.Slider(Settings.witnessPhotoChance, 0f, 1f);
            l.CheckboxLabeled("Multi-line harassment dialogue", ref Settings.multiLineHarassment,
                "After a verbal approach, play a short back-and-forth of speech bubbles (victim reacts, harasser presses). Requires SpeakUp for the bubbles.");
            l.Label("Extra dialogue lines: " + Settings.harassmentExtraLines);
            Settings.harassmentExtraLines = (int)l.Slider(Settings.harassmentExtraLines, 0f, 6f);
            l.CheckboxLabeled("Scenes can extend (drag somewhere private)", ref Settings.enableSceneExtend,
                "After a forced act, a chance the attacker drags the victim somewhere private and continues, with more dialogue. Capped per scene.");
            l.Label("Scene extend chance: " + Settings.sceneExtendChance.ToStringPercent());
            Settings.sceneExtendChance = l.Slider(Settings.sceneExtendChance, 0f, 1f);
            l.Label("Max extensions per scene: " + Settings.maxSceneExtends);
            Settings.maxSceneExtends = (int)l.Slider(Settings.maxSceneExtends, 0f, 5f);
            l.CheckboxLabeled("Devious devices (target restrained pawns)", ref Settings.enableDeviousDevice,
                "Pawns wearing RimJobWorld bondage gear become prime targets. A decent pawn may free them; others exploit the helpless. Restrained pawns are far more likely to be harassed and cannot struggle effectively.");
            l.CheckboxLabeled("Lock a device on the victim after rape", ref Settings.enableDeviceLockAfterRape,
                "After a forced act, a chance the rapist locks an RimJobWorld bondage device onto the victim and walks off with the key.");
            l.Label("Device lock chance after rape: " + Settings.deviceLockChance.ToStringPercent());
            Settings.deviceLockChance = l.Slider(Settings.deviceLockChance, 0f, 1f);
            l.Label("Max devices locked on one victim: " + Settings.maxLockedDevices);
            Settings.maxLockedDevices = (int)l.Slider(Settings.maxLockedDevices, 1f, 6f);
            l.Label("Chance to add each extra device: " + Settings.extraDeviceChance.ToStringPercent());
            Settings.extraDeviceChance = l.Slider(Settings.extraDeviceChance, 0f, 1f);
            l.CheckboxLabeled("Bound in public (after rape)", ref Settings.enableBoundInPublic,
                "After a rape, a chance the attacker hauls the victim to a public spot and leaves them locked in a device. Works without the Onahole Extension. Free them with the key the captor kept.");
            l.Label("Bound-in-public chance after rape: " + Settings.boundInPublicChance.ToStringPercent());
            Settings.boundInPublicChance = l.Slider(Settings.boundInPublicChance, 0f, 1f);
            l.CheckboxLabeled("Public onahole capture (requires Onahole Extension)", ref Settings.enableOnaholeCapture,
                "After a rape, a chance the attacker drags the victim to a busy spot, spawns an onahole, and locks them inside for public use. Deconstruct the onahole to free them.");
            l.Label("Onahole capture chance after rape: " + Settings.onaholeCaptureChance.ToStringPercent());
            Settings.onaholeCaptureChance = l.Slider(Settings.onaholeCaptureChance, 0f, 1f);
            l.CheckboxLabeled("Pets may flee a beating", ref Settings.enableFleeBeating,
                "A disciplined pet with enough spirit and will may try to bolt from the beating. If they do, the owner escalates: a spell in an onahole, a beatdown until they drop, or an arranged gangbang.");
            l.CheckboxLabeled("Allow beating pets to death", ref Settings.enableBeatToDeath,
                "When a pet flees a beating, allow a low-chance outcome where the enraged owner beats them to death. Off by default.");
            if (Settings.enableBeatToDeath)
            {
                l.Label("Beat-to-death chance (on a flee): " + Settings.beatToDeathChance.ToStringPercent());
                Settings.beatToDeathChance = l.Slider(Settings.beatToDeathChance, 0f, 0.5f);
            }
            l.CheckboxLabeled("Hide control gizmos (use the Control tab + Pet Dashboard instead)", ref Settings.hideKeyHolderGizmos,
                "Hide the key-holder control gizmos from the pawn's command bar. All controls stay available on the pawn's Control inspect tab and in the Pet Dashboard.");
            l.CheckboxLabeled("Show the Control inspect tab on pets", ref Settings.showControlTab,
                "Adds a Control tab (styled like the vanilla Slave/Prisoner tab) to any collared or owned pawn.");
            l.CheckboxLabeled("Show the Sexuality tab / Profile panel", ref Settings.showSexualityTab,
                "Adds the standalone Sexuality inspect tab on humanlikes and the Profile tab in the Harem window's context panel. Off by default.");
            l.CheckboxLabeled("Controlled pawns lose free will", ref Settings.suppressFreeWillWhenControlled,
                "While a pawn is under active control, disable their free will in a detected free-will mod (e.g. Free Will) so their tasks are owner-directed, not self-chosen.");
            l.CheckboxLabeled("Gangbang group (MMF) animations", ref Settings.enableGangbangMMF,
                "Let extra aggressors join an ongoing gangbang act at the same time, so c0ffee Animations composes a group (MMF and larger) animation instead of one after another. Needs c0ffee Animations with matching group animation defs installed.");
            if (Settings.enableGangbangMMF)
            {
                l.Label("Max simultaneous aggressors: " + Settings.gangbangMaxActors);
                Settings.gangbangMaxActors = (int)l.Slider(Settings.gangbangMaxActors, 2f, 4f);
            }
            l.CheckboxLabeled("Groping (physical escalation)", ref Settings.enableGrope);
            l.CheckboxLabeled("Forced acts (hands off to RimJobWorld)", ref Settings.enableForced);
            l.GapLine();

            l.Label("Per-approach cooldowns (ticks; 2500 = 1 in-game hour, 0 = off)");
            l.Label("Catcall cooldown: " + Settings.cooldownCatcall);
            Settings.cooldownCatcall = (int)l.Slider(Settings.cooldownCatcall, 0f, 60000f);
            l.Label("Proposition cooldown: " + Settings.cooldownProposition);
            Settings.cooldownProposition = (int)l.Slider(Settings.cooldownProposition, 0f, 60000f);
            l.Label("Flirt cooldown: " + Settings.cooldownFlirt);
            Settings.cooldownFlirt = (int)l.Slider(Settings.cooldownFlirt, 0f, 60000f);
            l.Label("Fan / spiked drink cooldown: " + Settings.cooldownSpikedDrink);
            Settings.cooldownSpikedDrink = (int)l.Slider(Settings.cooldownSpikedDrink, 0f, 60000f);
            l.Label("Hypnosis cooldown: " + Settings.cooldownHypnosis);
            Settings.cooldownHypnosis = (int)l.Slider(Settings.cooldownHypnosis, 0f, 60000f);
            l.Label("Blackmail cooldown: " + Settings.cooldownBlackmail);
            Settings.cooldownBlackmail = (int)l.Slider(Settings.cooldownBlackmail, 0f, 60000f);
            l.Label("Devious device cooldown: " + Settings.cooldownDeviousDevice);
            Settings.cooldownDeviousDevice = (int)l.Slider(Settings.cooldownDeviousDevice, 0f, 60000f);
            l.GapLine();

            l.Label("Escalation");
            l.CheckboxLabeled("Allow escalation past verbal", ref Settings.allowEscalation,
                "When off, harassment stops at verbal comments. No groping or forced acts.");
            l.CheckboxLabeled("Carry victim somewhere private first", ref Settings.pullToPrivate,
                "Before groping, the harasser carries the victim to a private spot (their bedroom). When off, or when no private spot is reachable, it happens where they stand.");
            l.Label("Escalation to physical: " + Settings.baseEscalationChance.ToStringPercent());
            Settings.baseEscalationChance = l.Slider(Settings.baseEscalationChance, 0f, 1f);
            l.Label("Physical to forced act: " + Settings.baseForcedChance.ToStringPercent());
            Settings.baseForcedChance = l.Slider(Settings.baseForcedChance, 0f, 1f);
            l.GapLine();

            l.Label("Player intervention");
            l.CheckboxLabeled("Offer a chance to intervene", ref Settings.interveneGateEnabled,
                "Pauses and prompts you before things turn physical when one of your colonists is involved.");
            l.CheckboxLabeled("Also gate later-phase approaches", ref Settings.interveneGateLaterPhases,
                "Applies the intervene prompt to hypnosis, blackmail and devious-device approaches when added.");
            l.Label("Intervention success base: " + Settings.baseInterveneChance.ToStringPercent());
            Settings.baseInterveneChance = l.Slider(Settings.baseInterveneChance, 0f, 1f);
            l.GapLine();

            l.Label("Gender and orientation");
            l.CheckboxLabeled("Victims can fight back", ref Settings.allowVictimAggressors,
                "Lets visitors and raiders harass, rape, and (on an already-conditioned colonist) collar your colonists; onahole capture works at the normal chance. Raiders only reach downed colonists. If such an aggressor leaves the map carrying the key to a locked colonist, you get a letter and a warrant (with Simple Warrants) to hunt them down and recover it.");
            l.CheckboxLabeled("Evil pawns scavenge keys", ref Settings.enableKeyScavenging,
                "Cruel pawns (by morality, dark traits, bad karma, and Rimpsyche compassion) will walk over and pocket a Holokey found on the ground, keep it, and start ordering the conditioned, collared pawn it unlocks around.");
            l.CheckboxLabeled("Pawns scavenge scandalous photos", ref Settings.enablePhotoScavenging,
                "Colonists, visitors, and raiders will wander over and pocket a scandalous photo left on the ground (even in a stockpile). Cruel or greedy pawns (by morality, dark traits, bad karma, Rimpsyche compassion, predator gene) are far likelier to grab one, and a hostile can carry the blackmail off the map.");
            l.CheckboxLabeled("Conditioning installs quirks", ref Settings.conditioningInstallsQuirks,
                "When RJW Quirks is active, a pet that reaches conditioning milestones permanently gains fitting quirks (Cumslut, then Exhibitionist). Existing submissive quirks also make a pet break faster.");
            l.CheckboxLabeled("Conditioning installs a gene (heritable)", ref Settings.conditioningInstallsGene,
                "When Biotech and RJW Genes are active, a fully-broken pet gains a heritable hypersexual endogene. Off by default - this is permanent and passes to children.");
            l.CheckboxLabeled("Submission need", ref Settings.enableSubmissionNeed,
                "Conditioned or owned pets show a Submission need bar. It falls over time (low = a restless mood and more eager self-presentation) and is settled by serving, being rewarded, or being disciplined.");
            l.CheckboxLabeled("Conditioning marks the body", ref Settings.conditioningMarksBody,
                "Deeply submissive pets are permanently pierced, then branded, as their subDom passes thresholds. Off by default - these marks are permanent.");
            l.CheckboxLabeled("Rapists may tattoo their victim", ref Settings.rapistMayTattoo,
                "After a rape, the aggressor may permanently mark their victim with a degrading tattoo (face first, then body). Chance scales with the rapist's cruelty. Needs Ideology for the tattoo system.");
            l.CheckboxLabeled("Worn pawns give worse sex", ref Settings.wearReducesSexQuality,
                "A well-used pawn provides less satisfying sex - partner satisfaction drops with how worn the hole being used is. Female pawns (vaginal wear) lose quality faster. Wear starts at 0% and accrues through play.");
            l.CheckboxLabeled("Head girl enforces the harem", ref Settings.enableHeadGirl,
                "A pet marked as head girl (in the Harem tab's schedule panel) walks over and disciplines misbehaving or below-quota pets, enforcing the pecking order.");
            l.CheckboxLabeled("Auto-pick head girl", ref Settings.autoHeadGirl,
                "The head girl of each owner's harem is chosen automatically as the best-performing pet (conditioning, rapport, services rendered, earnings, dominance), re-evaluated over time. Off = set it manually in the Harem tab.");
            l.CheckboxLabeled("Key-holders refuse to drop keys", ref Settings.enableKeyRefuse,
                "A pawn autonomously controlling a collar will not give up the key (blocks the gear-tab / True RPG Inventory drop).");
            l.CheckboxLabeled("Controller behaviors (sadist/masochist/zoophile)", ref Settings.enableControllerBehaviors,
                "An AI controller makes the slave do what they like: a sadist beats the slave, a masochist has the slave strike them, a zoophile forces the slave onto an animal; others demand service. All controllers also talk down to the slave.");
            l.CheckboxLabeled("Owner/slave relationship on the social tab", ref Settings.enableOwnerRelationship,
                "Collared pawns and their key-holder gain a unique relationship shown on the vanilla Social tab. The slave sees their owner's title; the owner sees the slave's title.");
            if (Settings.enableOwnerRelationship)
            {
                if (l.RadioButton("   Owner / pet", Settings.relationScheme == RelationScheme.OwnerPet))
                    Settings.relationScheme = RelationScheme.OwnerPet;
                if (l.RadioButton("   Master (Mistress) / slave", Settings.relationScheme == RelationScheme.MasterSlave))
                    Settings.relationScheme = RelationScheme.MasterSlave;
                if (l.RadioButton("   Master (Mistress) / property", Settings.relationScheme == RelationScheme.MasterProperty))
                    Settings.relationScheme = RelationScheme.MasterProperty;
            }
            l.CheckboxLabeled("Slave will + breakouts", ref Settings.enableSlaveWill,
                "A controlled slave has a visible Conditioned/Will widget and periodically tries to break free. High will succeeds and ends the control; a failed attempt deepens conditioning and ends in a public onahole.");
            l.Label("Breakout chance factor: " + Settings.breakoutChanceFactor.ToStringPercent());
            Settings.breakoutChanceFactor = l.Slider(Settings.breakoutChanceFactor, 0f, 1f);
            l.CheckboxLabeled("Respect sexual orientation", ref Settings.respectOrientation,
                "A harasser must be attracted to the target's sex. Undetermined pawns default to opposite-sex.");
            l.CheckboxLabeled("Opposite-sex targeting only", ref Settings.enforceOppositeSex,
                "Hard override (on by default): harassers only ever target the opposite sex, ignoring any gay/bi orientation. Turn this OFF to allow same-sex harassment based on each pawn's orientation.");
            l.CheckboxLabeled("Heterosexual only (strict M<->F everywhere)", ref Settings.heterosexualOnly,
                "Master gate: EVERY pairing this mod drives - harassment, service, whoring, gangbangs - is forced to be male<->female only. Use this if same-sex acts still slip through from other flows.");
            l.CheckboxLabeled("Allow female harassers", ref Settings.allowFemaleHarassers);
            l.CheckboxLabeled("Allow male harassers", ref Settings.allowMaleHarassers);
            l.GapLine();

            l.Label("Age ranges (adults only)");
            l.Label("Harasser age: " + Settings.harasserMinAge + " to " + Settings.harasserMaxAge);
            Settings.harasserMinAge = (int)l.Slider(Settings.harasserMinAge, 18f, 99f);
            Settings.harasserMaxAge = (int)l.Slider(Settings.harasserMaxAge, 18f, 99f);
            if (Settings.harasserMaxAge < Settings.harasserMinAge) Settings.harasserMaxAge = Settings.harasserMinAge;
            l.Label("Victim age: " + Settings.victimMinAge + " to " + Settings.victimMaxAge);
            Settings.victimMinAge = (int)l.Slider(Settings.victimMinAge, 18f, 99f);
            Settings.victimMaxAge = (int)l.Slider(Settings.victimMaxAge, 18f, 99f);
            if (Settings.victimMaxAge < Settings.victimMinAge) Settings.victimMaxAge = Settings.victimMinAge;
            l.GapLine();

            l.End();

            // Who-harasses-whom matrix, drawn manually under the listing.
            float matrixTop = l.CurHeight + 4f;
            HarassmentSettingsUI.DrawMatrix(new Rect(0f, matrixTop, viewRect.width, 230f), Settings);

            Widgets.EndScrollView();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            Settings.EnsureDefaults();
            RelationLabels.Apply(Settings.relationScheme);
        }
    }

    [StaticConstructorOnStartup]
    public static class RJWSH_Startup
    {
        static RJWSH_Startup()
        {
            try
            {
                SoftDeps.Detect();
            }
            catch (Exception ex)
            {
                Log.Error("[RJW Sexual Harassment] startup detection failed: " + ex);
            }
        }
    }

    /// <summary>Draws the harasser x target permission grid.</summary>
    public static class HarassmentSettingsUI
    {
        private static readonly PawnCategory[] Cats =
        {
            PawnCategory.Colonist, PawnCategory.Slave, PawnCategory.Prisoner, PawnCategory.Visitor, PawnCategory.Other
        };

        public static void DrawMatrix(Rect rect, HarassmentSettings s)
        {
            s.EnsureDefaults();
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "Who harasses whom (rows are harasser, columns are target)");
            float top = rect.y + 28f;
            float labelW = 90f;
            float cellW = 90f;
            float cellH = 26f;

            // Column headers
            for (int c = 0; c < Cats.Length; c++)
            {
                var hr = new Rect(rect.x + labelW + c * cellW, top, cellW, cellH);
                Widgets.Label(hr, Cats[c].ToString());
            }

            for (int r = 0; r < Cats.Length; r++)
            {
                float rowY = top + cellH + r * cellH;
                Widgets.Label(new Rect(rect.x, rowY, labelW, cellH), Cats[r].ToString());
                for (int c = 0; c < Cats.Length; c++)
                {
                    string key = HarassmentSettings.PairKey(Cats[r], Cats[c]);
                    bool val = s.allowedPairs.Contains(key);
                    var cr = new Rect(rect.x + labelW + c * cellW, rowY, cellW, cellH);
                    bool nv = val;
                    Widgets.Checkbox(cr.x + 4f, cr.y, ref nv);
                    if (nv != val)
                    {
                        if (nv) s.allowedPairs.Add(key);
                        else s.allowedPairs.Remove(key);
                    }
                }
            }
        }
    }
}
