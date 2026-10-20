using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Procedural "puppet" animation of the fighter models (Art-Spec §3.1–3.2, D-109, brief A-TheoAnim §4): idle, run, shot, ult,
    /// trick, hurt, spawn, defeat, victory, blinking, head look, bone springs, muzzle flash. Shared by all fighters — what differs
    /// per fighter (styles, springs, model height) comes from the rig. Values are written by DataSeeder (D-010). View only:
    /// nothing here changes the simulation and nothing goes into the content pack.
    /// </summary>
    [CreateAssetMenu(fileName = "FighterAnim", menuName = "Arena/Fighter Anim Config")]
    public sealed class FighterAnimConfig : ScriptableObject
    {
        [Header("Blending")]
        [Tooltip("Crossfade between the base states (idle ↔ run), seconds.")]
        [Min(0.001f)] public float BlendSeconds;

        [Header("Idle")]
        [Tooltip("Breaths per second.")]
        [Min(0f)] public float BreathHz;

        [Tooltip("Body scale change of a breath, fraction (0.02 = ±2 %).")]
        [Min(0f)] public float BreathScale;

        [Tooltip("Head sway while idle, degrees.")]
        [Min(0f)] public float IdleSwayDeg;

        [Header("Run")]
        [Tooltip("Above this smoothed speed the run state takes over, units per second.")]
        [Min(0f)] public float RunThreshold;

        [Tooltip("Speed at which the run swings reach full amplitude, units per second.")]
        [Min(0.001f)] public float RunSpeedRef;

        [Tooltip("Smoothing rate of the measured velocity, 1/s (higher = follows faster).")]
        [Min(0f)] public float SpeedSmoothing;

        [Tooltip("One step cycle (left + right leg) covers this many model heights of path — the feet do not slide.")]
        [Min(0.001f)] public float StrideLengthPerHeight;

        [Tooltip("Leg swing forward/back, degrees.")]
        [Min(0f)] public float LegSwingDeg;

        [Tooltip("Arm swing (the arm holding the prop swings half as much), degrees.")]
        [Min(0f)] public float ArmSwingDeg;

        [Tooltip("Root hop per step, units.")]
        [Min(0f)] public float BounceHeight;

        [Tooltip("Forward lean of the body at full run speed, degrees.")]
        [Min(0f)] public float LeanForwardDeg;

        [Tooltip("Sideways lean into a turn at LeanTurnRateRef, degrees.")]
        [Min(0f)] public float LeanTurnDeg;

        [Tooltip("Turn rate of the move direction that gives the full turn lean, degrees per second.")]
        [Min(0.001f)] public float LeanTurnRateRef;

        [Tooltip("A position jump longer than this in one frame is a teleport / correction: the step phase does not move, units.")]
        [Min(0f)] public float TeleportDistance;

        [Header("Shot")]
        [Min(0.001f)] public float ShotSeconds;

        [Tooltip("Arm and prop kick up/back, degrees.")]
        [Min(0f)] public float RecoilDeg;

        [Tooltip("Prop pushed back along its own axis, units.")]
        [Min(0f)] public float RecoilDistance;

        [Tooltip("Body rocks back, degrees.")]
        [Min(0f)] public float BodyRecoilDeg;

        [Tooltip("Weight of the recoil over the shot, 0–1 time → 0–1 weight (starts and ends at 0).")]
        public AnimationCurve ShotEnvelope = new AnimationCurve();

        [Header("Ult")]
        [Min(0.001f)] public float UltSeconds;

        [Tooltip("Part of the ult spent crouching before the jump, 0–1.")]
        [Range(0.05f, 0.95f)] public float UltCrouchFraction;

        [Tooltip("Crouch depth / jump height of the root, units.")]
        [Min(0f)] public float UltCrouch;
        [Min(0f)] public float UltJump;

        [Tooltip("Spin of the body during the jump, degrees.")]
        public float UltSpinDeg;

        [Tooltip("The shot recoil at the end of the ult is this many times stronger.")]
        [Min(0f)] public float UltShotScale;

        [Header("Trick")]
        [Min(0.001f)] public float TrickSeconds;

        [Tooltip("Body turn of the Roll trick, degrees (360 = one somersault).")]
        public float TrickRollDeg;

        [Header("Stunned")]
        [Tooltip("Dizzy wobble of the body and head while stunned, degrees.")]
        [Min(0f)] public float StunWobbleDeg;

        [Tooltip("Wobbles per second.")]
        [Min(0f)] public float StunWobbleHz;

        [Tooltip("Eyelids half closed while stunned, 0–1.")]
        [Range(0f, 1f)] public float StunLidClosure;

        [Header("Hurt")]
        [Min(0.001f)] public float HurtSeconds;

        [Tooltip("Body squash of a hit, fraction of height.")]
        [Min(0f)] public float HurtSquash;

        [Tooltip("Body tilts away from the hit, degrees.")]
        [Min(0f)] public float HurtTiltDeg;

        [Tooltip("A heavy hit (ult) is this many times stronger.")]
        [Min(0f)] public float HurtHeavyScale;

        [Header("Spawn, defeat, victory")]
        [Tooltip("The model grows from 0 to full size, seconds.")]
        [Min(0.001f)] public float SpawnSeconds;

        [Tooltip("Defeat clip (backward tumble + shrink to 0); the view hides after it, seconds.")]
        [Min(0.001f)] public float DefeatSeconds;

        [Tooltip("Backward tumble of the body during defeat, degrees.")]
        public float DefeatTumbleDeg;

        [Tooltip("Victory hops per second and their height, units.")]
        [Min(0f)] public float VictoryHopHz;
        [Min(0f)] public float VictoryHopHeight;

        [Tooltip("Arms raised at victory, degrees.")]
        public float VictoryArmDeg;

        [Header("Face")]
        [Tooltip("One blink (close + open), seconds.")]
        [Min(0.001f)] public float BlinkSeconds;

        [Tooltip("Time between blinks (start to start) is random in [min, max], seconds.")]
        [Min(0.01f)] public float BlinkIntervalMin;
        [Min(0.01f)] public float BlinkIntervalMax;

        [Tooltip("Chance that a blink is followed right away by a second one.")]
        [Range(0f, 1f)] public float DoubleBlinkChance;

        [Tooltip("Eye turn toward the look target, degrees.")]
        [Min(0f)] public float LookMaxDeg;

        [Tooltip("Head turn toward the look target (when the eyes are painted in the texture), degrees.")]
        [Min(0f)] public float HeadLookDeg;

        [Header("Springs and limits")]
        [Tooltip("A spring bone swings at most this far, degrees.")]
        [Min(0f)] public float SpringMaxDegrees;

        [Tooltip("Longest frame step used by the springs and smoothing, seconds (a frame spike does not blow them up).")]
        [Min(0.001f)] public float MaxDt;

        [Header("Muzzle flash")]
        [Min(0f)] public float MuzzleSeconds;

        [Tooltip("Flash position along the prop's +Z, units.")]
        public float MuzzleOffset;

        [Min(0f)] public float MuzzleScale;

        [Tooltip("Flashes created at match start (all fighters share them).")]
        [Min(1)] public int MuzzlePoolSize;

        [Header("Culling")]
        [Tooltip("Off-screen fighters advance only timers and phases; bones are not written.")]
        public bool CullOffscreen;
    }
}
