using Arena.Core.Math;
using Arena.Gameplay.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Arena.Gameplay.Input
{
    /// <summary>
    /// PC/editor controls polled directly from <see cref="Keyboard.current"/> / <see cref="Mouse.current"/> (D-017):
    /// WASD — move; hold LMB — aim at the mouse ray on the ground (y = 0), release — manual attack; Space — auto-aim attack;
    /// hold RMB — aim the ult, release — manual ult; E — auto-aim ult; Shift/Q — trick. Mouse presses that start over UI
    /// (virtual sticks) are ignored. Writes into the <see cref="InputChannel.KeyboardMouse"/> channel; no allocations.
    /// </summary>
    public sealed class KeyboardMouseInput
    {
        private static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);

        private bool _aimingAttack;
        private bool _aimingUlt;

        /// <param name="fighterPosition">Local fighter position (simulation space).</param>
        /// <param name="attackRange">Range in cells: the aim length is the fraction of it (D-006).</param>
        public void Poll(LocalInputCollector collector, Camera camera, Vec2 fighterPosition, float attackRange)
        {
            Keyboard kb = Keyboard.current;
            Vec2 move = Vec2.Zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.Y += 1f;
                if (kb.sKey.isPressed) move.Y -= 1f;
                if (kb.dKey.isPressed) move.X += 1f;
                if (kb.aKey.isPressed) move.X -= 1f;
                move = move.Normalized;

                if (kb.spaceKey.wasPressedThisFrame) collector.PressAttackSim(Vec2.Zero, true);
                if (kb.eKey.wasPressedThisFrame) collector.PressUltSim(Vec2.Zero, true);
                if (kb.qKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame) collector.PressTrick();
            }
            collector.SetMove(InputChannel.KeyboardMouse, move);

            Mouse mouse = Mouse.current;
            if (mouse == null || camera == null)
            {
                _aimingAttack = _aimingUlt = false;
                collector.SetAimSim(InputChannel.KeyboardMouse, Vec2.Zero);
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !PointerOverUi()) _aimingAttack = true;
            if (mouse.rightButton.wasPressedThisFrame && !PointerOverUi()) _aimingUlt = true;

            Vec2 aim = Vec2.Zero;
            if (_aimingAttack || _aimingUlt) aim = AimAtMouse(camera, mouse.position.ReadValue(), fighterPosition, attackRange);

            if (_aimingAttack && mouse.leftButton.wasReleasedThisFrame)
            {
                if (!aim.IsZero) collector.PressAttackSim(aim, false);
                _aimingAttack = false;
            }
            if (_aimingUlt && mouse.rightButton.wasReleasedThisFrame)
            {
                if (!aim.IsZero) collector.PressUltSim(aim, false);
                _aimingUlt = false;
            }

            collector.SetAimSim(InputChannel.KeyboardMouse, _aimingAttack || _aimingUlt ? aim : Vec2.Zero);
        }

        public void Reset() => _aimingAttack = _aimingUlt = false;

        /// <summary>Direction from the fighter to the mouse point on the ground, length = distance / range (≤ 1).</summary>
        public static Vec2 AimAtMouse(Camera camera, Vector2 screenPoint, Vec2 fighterPosition, float attackRange)
        {
            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!Ground.Raycast(ray, out float enter)) return Vec2.Zero;
            Vec2 offset = WorldCoords.ToSim(ray.GetPoint(enter)) - fighterPosition;
            return attackRange > 0f ? Vec2.ClampLength(offset / attackRange, 1f) : offset.Normalized;
        }

        private static bool PointerOverUi()
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }
}
