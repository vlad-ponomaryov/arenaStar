using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// "Game feel" parameters (GDD §5.3): hit flash, damage numbers, own health number, camera shake, ult hit stop
    /// (visual only, D-015), vibration, procedural sounds (D-028), area telegraph, ult button glow, FPS counter, aim magnet.
    /// Values are written by DataSeeder (D-010). View-only: nothing here changes the simulation.
    /// </summary>
    [CreateAssetMenu(fileName = "Feel", menuName = "Arena/Feel Config")]
    public sealed class FeelConfig : ScriptableObject
    {
        [Header("Hit flash")]
        [Tooltip("The target's body shows the flash material this long after a hit, seconds.")]
        [Min(0f)] public float FlashSeconds;

        [Header("Damage numbers")]
        [Tooltip("A number stays on screen this long, seconds.")]
        [Min(0.05f)] public float NumberSeconds;

        [Tooltip("Numbers rise this fast, world units per second.")]
        public float NumberRiseSpeed;

        [Tooltip("Height above the target's feet where a number appears, world units.")]
        public float NumberHeight;

        [Tooltip("Random sideways offset of a new number, world units (numbers of a burst do not overlap).")]
        [Min(0f)] public float NumberScatter;

        [Tooltip("Font size of a number, canvas units at the HUD reference resolution; ult hits use the larger size.")]
        [Min(1)] public int NumberFontSize;
        [Min(1)] public int UltNumberFontSize;

        [Tooltip("Number texts created at match start; the pool grows only if more are shown at once.")]
        [Min(0)] public int NumberPoolPrewarm;

        [Tooltip("Damage dealt to the other team / damage taken by the local team / healing.")]
        public Color DamageColor;
        public Color DamageTakenColor;
        public Color HealColor;

        [Tooltip("Canvas of the floating texts: reference resolution and width/height match (as the HUD canvas).")]
        public Vector2 TextReferenceResolution;

        [Range(0f, 1f)] public float TextMatchWidthOrHeight;

        [Header("Own health number")]
        [Tooltip("Height of the local fighter's health number above its feet, world units (above the health bar).")]
        public float OwnHealthHeight;

        [Min(1)] public int OwnHealthFontSize;
        public Color OwnHealthColor;

        [Header("Camera shake (ult hit)")]
        [Tooltip("Peak offset, world units.")]
        [Min(0f)] public float ShakeAmplitude;

        [Tooltip("Shake length, seconds (the offset fades out linearly).")]
        [Min(0f)] public float ShakeSeconds;

        [Tooltip("Shake oscillations per second.")]
        [Min(0f)] public float ShakeFrequency;

        [Header("Hit stop (ult hit, views only — D-015)")]
        [Tooltip("Views freeze this long, seconds (GDD §5.3: 40 ms).")]
        [Min(0f)] public float HitStopSeconds;

        [Header("Vibration (Android)")]
        [Tooltip("A hit of / on the local fighter, milliseconds.")]
        [Min(0)] public int HitVibrationMs;

        [Tooltip("An ult hit of / on the local fighter, milliseconds.")]
        [Min(0)] public int UltVibrationMs;

        [Tooltip("Shortest time between two vibrations, seconds (a burst of drops buzzes once).")]
        [Min(0f)] public float VibrationMinInterval;

        [Tooltip("Strength choices in the settings, percent (first = off).")]
        public int[] VibrationSteps = new int[0];

        [Header("Sounds (procedural, D-028)")]
        [Tooltip("Sample rate of the generated clips, Hz.")]
        [Min(8000)] public int SampleRate;

        public ProceduralSoundSettings HitSound;
        public ProceduralSoundSettings UltSound;
        public ProceduralSoundSettings UltReadySound;

        [Tooltip("Shortest time between two hit sounds, seconds.")]
        [Min(0f)] public float HitSoundMinInterval;

        [Header("Area telegraph (GDD §5.3)")]
        [Tooltip("An area attack shows its zone on the ground this long before it triggers, seconds (0.5).")]
        [Min(0f)] public float TelegraphSeconds;

        [Header("Ult button")]
        [Tooltip("Glow pulses per second while the ult is charged.")]
        [Min(0f)] public float UltGlowFrequency;

        [Tooltip("Extra scale of the ult stick at the peak of a pulse (0.1 = +10 %).")]
        [Min(0f)] public float UltGlowScale;

        [Header("FPS counter")]
        [Tooltip("The counter shows the average over this many seconds.")]
        [Min(0.05f)] public float FpsUpdateSeconds;

        [Header("Aim magnet (GDD §5.1)")]
        [Tooltip("A manual aim turns to a visible enemy if it is off by at most this many degrees (5).")]
        [Min(0f)] public float AimMagnetDegrees;

        [Header("Settings defaults (first start, GDD §5.1 — D-062)")]
        [Tooltip("Mirrored control layout (movement stick on the right).")]
        public bool DefaultMirroredLayout;

        [Tooltip("Vibration strength, percent (0 = off).")]
        [Range(0, 100)] public int DefaultVibrationPercent;

        [Tooltip("Aim magnet (GDD §5.1: on).")]
        public bool DefaultAimMagnet;

        [Tooltip("Colour-blind palette (enemies orange).")]
        public bool DefaultColorblindPalette;

        [Tooltip("Battery saver: frame rate capped at BatterySaverFrameRate.")]
        public bool DefaultBatterySaver;

        [Header("Frame rate (GDD §8.4, S2.13)")]
        [Tooltip("Application.targetFrameRate (vSync off).")]
        [Min(15)] public int TargetFrameRate;

        [Tooltip("Frame rate with the battery saver setting.")]
        [Min(15)] public int BatterySaverFrameRate;
    }
}
