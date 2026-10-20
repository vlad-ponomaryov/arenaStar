using Arena.Core.Defs;
using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// What the match tells a fighter's animation (Art-Spec §3.2). Implemented by the procedural <see cref="FighterAnimator"/>;
    /// a fallback path with an Animator controller could stand behind the same interface. Directions are world space (x, 0, z).
    /// </summary>
    public interface IFighterAnimSink
    {
        /// <summary>Measured root speed (units/s) and move direction this frame; <paramref name="dt"/> — frame time, s.</summary>
        void OnMove(float speed, Vector3 worldDir, float dt);

        /// <summary>The fighter used an ability (attack / ult / trick); the clip style comes from the rig.</summary>
        void OnShoot(AbilitySlot slot, Vector3 aimDir);

        /// <summary>The fighter was hit from <paramref name="fromDir"/> (pointing from the fighter to the source).</summary>
        void OnHurt(Vector3 fromDir, bool heavy);

        void OnSpawn();
        void OnDeath();
        void OnMatchEnd(bool won);
        void SetStatus(bool stunned, bool dashing);

        /// <summary>Facing of the body (aim direction); the head and eyes turn toward the run turn from here.</summary>
        void SetLook(Vector3 worldDir);

        /// <summary>Back to the rest state: no clips, phases and springs zeroed.</summary>
        void ResetAll();
    }
}
