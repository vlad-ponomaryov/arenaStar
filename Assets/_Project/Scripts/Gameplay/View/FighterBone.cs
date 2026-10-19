namespace Arena.Gameplay.View
{
    /// <summary>
    /// Bones of the "toy on hinges" fighter rig (Art-Spec §1.4, §6.2); the value is the index in <see cref="FighterRig.Bones"/>
    /// and in the skinned mesh's bone array. New bones go to the end.
    /// </summary>
    public enum FighterBone
    {
        /// <summary>Rig root on the ground under the fighter (feet, legs).</summary>
        Root = 0,
        /// <summary>Torso; carries the head, arms, tail and prop.</summary>
        Body = 1,
        Head = 2,
        ArmL = 3,
        ArmR = 4,
        LegL = 5,
        LegR = 6,
        Tail = 7,
        /// <summary>Weapon or carried item; scale 0 hides it.</summary>
        Prop = 8,
        /// <summary>Secondary piece with its own spring (scarf, rope coil); child of Head or Body.</summary>
        Extra = 9,
        EyeL = 10,
        EyeR = 11,
        /// <summary>Eyelids: scale 0 = open, 1 = closed (blink, squint on hit).</summary>
        LidL = 12,
        LidR = 13
    }
}
