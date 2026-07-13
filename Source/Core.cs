using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>How a pawn is classified for the who-harasses-whom matrix.</summary>
    public enum PawnCategory
    {
        Colonist,
        Slave,
        Prisoner,
        Visitor,
        Other
    }

    /// <summary>What an AI collar-holder makes their slave do, picked from the holder's personality.</summary>
    public enum ControllerBehavior
    {
        Service,        // default: the slave services the controller
        MakeThemHitMe,  // sadist controller: the slave strikes the controller (unarmed melee)
        BeatThem,       // masochist controller: the controller strikes the slave (unarmed melee)
        Bestiality,     // zoophile controller: the slave is forced onto a nearby animal
    }

    /// <summary>How the collared/owner relationship is titled on the vanilla social tab.</summary>
    public enum RelationScheme
    {
        OwnerPet,       // owner / pet (default)
        MasterSlave,    // master|mistress / slave
        MasterProperty, // master|mistress / property
    }

    /// <summary>Persistent per-pawn moral disposition. Decides how far a pawn is willing to push.</summary>
    public enum Morality
    {
        Decent,
        Questionable,
        Evil
    }

    /// <summary>The stages / flavours of harassment, ascending in severity.</summary>
    public enum ApproachType
    {
        Catcall,       // mild verbal, no escalation
        Proposition,   // demanding verbal -> escalation
        Grope,         // physical stage (internal)
        Forced,        // RJW act handoff (internal)
        SpikedDrink,   // fan: friendly buff OR spiked -> knockout -> forced
        Flirt,         // friendly verbal -> consensual OR coercive
        Hypnosis,      // therapy session -> conditioning -> compliance / command
        Blackmail,     // threaten with scandalous photo -> comply / refuse / intimidate
        DeviousDevice  // approach a restrained pawn -> free them (decent) or exploit (else)
    }

    /// <summary>How a victim is moved during a drag: carried (downed, despawned) or led (conscious, stays spawned).</summary>
    public enum DragMode
    {
        Failed,
        Carried,
        Led
    }

    /// <summary>A shared affection act between two willing pawns.</summary>
    public enum AffectionKind
    {
        Kiss,
        HoldHands
    }

    /// <summary>How a target reacted to escalation.</summary>
    public enum ReactionType
    {
        Submitted,
        Resisted,
        Intervened
    }

    [DefOf]
    public static class RJWSH_InteractionDefOf
    {
        public static InteractionDef RJWSH_Catcall;
        public static InteractionDef RJWSH_Proposition;
        public static InteractionDef RJWSH_Grope;
        public static InteractionDef RJWSH_Flirt;
        public static InteractionDef RJWSH_Fan;
        public static InteractionDef RJWSH_Hypnosis;
        public static InteractionDef RJWSH_Blackmail;
        public static InteractionDef RJWSH_DeviousApproach;
        public static InteractionDef RJWSH_HarassReply;
        public static InteractionDef RJWSH_HarassChatter;
        public static InteractionDef RJWSH_HypnosisDoubt;
        public static InteractionDef RJWSH_HypnosisYield;
        public static InteractionDef RJWSH_HypnosisRefuse;
        public static InteractionDef RJWSH_BegHelp;
        public static InteractionDef RJWSH_TalkDown;
        public static InteractionDef RJWSH_OwnerDirty;
        public static InteractionDef RJWSH_SlaveSubmit;
        public static InteractionDef RJWSH_PetDefiant;
        public static InteractionDef RJWSH_PetContent;
        public static InteractionDef RJWSH_PetDevoted;
        public static InteractionDef RJWSH_CatcallReply;
        public static InteractionDef RJWSH_CatcallPress;
        public static InteractionDef RJWSH_PropositionReply;
        public static InteractionDef RJWSH_PropositionPress;
        public static InteractionDef RJWSH_FlirtReply;
        public static InteractionDef RJWSH_FlirtPress;
        public static InteractionDef RJWSH_FanReply;
        public static InteractionDef RJWSH_FanPress;
        public static InteractionDef RJWSH_DeviousReply;
        public static InteractionDef RJWSH_DeviousPress;
        public static InteractionDef RJWSH_BlackmailReply;
        public static InteractionDef RJWSH_BlackmailPress;
        public static InteractionDef RJWSH_GropeReply;
        public static InteractionDef RJWSH_OnaholeBind;
        public static InteractionDef RJWSH_Kiss;
        public static InteractionDef RJWSH_HoldHands;
        public static InteractionDef RJWSH_DragTaunt;
        public static InteractionDef RJWSH_Discipline;
        public static InteractionDef RJWSH_Reward;
        public static InteractionDef RJWSH_Covet;
        public static InteractionDef RJWSH_Assert;
        public static InteractionDef RJWSH_Present;
        // Bystander reactions to a harassment event, branched by the witness's morality/relationship.
        public static InteractionDef RJWSH_WitnessDisgust;
        public static InteractionDef RJWSH_WitnessLeer;
        public static InteractionDef RJWSH_WitnessProtective;
        public static InteractionDef RJWSH_WitnessResigned;
        // Victim reacting on sight of a remembered tormentor (recoil) or a now-helpless one (vengeance).
        public static InteractionDef RJWSH_Recoil;
        public static InteractionDef RJWSH_Vengeance;
        // Owner-pet ambient check-ins (inspect the pet, test their obedience).
        public static InteractionDef RJWSH_Inspect;
        public static InteractionDef RJWSH_TestObedience;

        static RJWSH_InteractionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_InteractionDefOf));
        }
    }

    [DefOf]
    public static class RJWSH_RelationDefOf
    {
        public static PawnRelationDef RJWSH_RelPet;
        public static PawnRelationDef RJWSH_RelOwner;

        static RJWSH_RelationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_RelationDefOf));
        }
    }

    [DefOf]
    public static class RJWSH_JobDefOf
    {
        public static JobDef RJWSH_Harass;
        public static JobDef RJWSH_StripVictim;
        public static JobDef RJWSH_BeingStripped;
        public static JobDef RJWSH_DragToOnahole;
        public static JobDef RJWSH_DragToPublic;
        public static JobDef RJWSH_DragToPrivate;
        public static JobDef RJWSH_Follow;
        public static JobDef RJWSH_DisciplinePet;
        public static JobDef RJWSH_RewardPet;
        public static JobDef RJWSH_DressPet;
        public static JobDef RJWSH_TrainPet;
        public static JobDef RJWSH_Beatdown;
        public static JobDef RJWSH_PursueFlee;
        public static JobDef RJWSH_Parade;
        public static JobDef RJWSH_Whore;
        public static JobDef RJWSH_Affection;
        public static JobDef RJWSH_StayPut;
        public static JobDef RJWSH_BeingLed;
        public static JobDef RJWSH_Scuffle;
        public static JobDef RJWSH_DeliverKey;

        static RJWSH_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_JobDefOf));
        }
    }

    [DefOf]
    public static class RJWSH_ThoughtDefOf
    {
        public static ThoughtDef RJWSH_WasHarassed;
        public static ThoughtDef RJWSH_WasGroped;
        public static ThoughtDef RJWSH_ReceivedTreat;
        public static ThoughtDef RJWSH_Humiliated;
        public static ThoughtDef RJWSH_Shocked;
        public static ThoughtDef RJWSH_TenderMoment;

        static RJWSH_ThoughtDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_ThoughtDefOf));
        }
    }

    [DefOf]
    public static class RJWSH_HediffDefOf
    {
        public static HediffDef RJWSH_Hypnotized;
        public static HediffDef RJWSH_ShockLust;

        static RJWSH_HediffDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_HediffDefOf));
        }
    }

    [DefOf]
    public static class RJWSH_ThingDefOf
    {
        public static ThingDef RJWSH_ScandalousPhoto;
        public static ThingDef RJWSH_ControlCollar;
        public static ThingDef RJWSH_Mote_Hands;

        static RJWSH_ThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RJWSH_ThingDefOf));
        }
    }
}
