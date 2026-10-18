using System;
using System.Reflection;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Guards D-002 for future steps: state structs stay blittable (no reference fields), and every field takes part in
    /// Equals — otherwise ContentEquals (snapshot/determinism tests) would silently ignore it.
    /// </summary>
    public sealed class StateEqualityTests
    {
        private static readonly Type[] StateStructs =
        {
            typeof(FighterState), typeof(MatchGlobals), typeof(DeterministicRng), typeof(Vec2), typeof(PlayerInput),
            typeof(FighterStats), typeof(ProjectileState), typeof(AreaState), typeof(StatusEffectState),
            typeof(PickupState), typeof(ModeState), typeof(BoxState)
        };

        [Test]
        public void StateStructs_HaveNoReferenceFields()
        {
            foreach (Type t in StateStructs) AssertBlittable(t, t.Name);
        }

        [Test]
        public void MatchState_FieldsAreBlittableStructsOrArraysOfThem()
        {
            foreach (FieldInfo f in typeof(MatchState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Type t = f.FieldType.IsArray ? f.FieldType.GetElementType() : f.FieldType;
                Assert.IsTrue(t.IsValueType, "MatchState." + f.Name + " must be a struct or an array of structs");
                AssertBlittable(t, "MatchState." + f.Name);
            }
        }

        [Test] public void FighterState_EveryFieldInEquals() => AssertEveryFieldMatters<FighterState>();
        [Test] public void MatchGlobals_EveryFieldInEquals() => AssertEveryFieldMatters<MatchGlobals>();
        [Test] public void DeterministicRng_EveryFieldInEquals() => AssertEveryFieldMatters<DeterministicRng>();
        [Test] public void Vec2_EveryFieldInEquals() => AssertEveryFieldMatters<Vec2>();
        [Test] public void PlayerInput_EveryFieldInEquals() => AssertEveryFieldMatters<PlayerInput>();
        [Test] public void FighterStats_EveryFieldInEquals() => AssertEveryFieldMatters<FighterStats>();
        [Test] public void ProjectileState_EveryFieldInEquals() => AssertEveryFieldMatters<ProjectileState>();
        [Test] public void AreaState_EveryFieldInEquals() => AssertEveryFieldMatters<AreaState>();
        [Test] public void StatusEffectState_EveryFieldInEquals() => AssertEveryFieldMatters<StatusEffectState>();
        [Test] public void PickupState_EveryFieldInEquals() => AssertEveryFieldMatters<PickupState>();
        [Test] public void ModeState_EveryFieldInEquals() => AssertEveryFieldMatters<ModeState>();
        [Test] public void BoxState_EveryFieldInEquals() => AssertEveryFieldMatters<BoxState>();

        private static void AssertEveryFieldMatters<T>() where T : struct, IEquatable<T>
        {
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                T a = default;
                object boxed = a;
                field.SetValue(boxed, NonDefault(field.FieldType));
                T b = (T)boxed;
                Assert.IsFalse(a.Equals(b), typeof(T).Name + "." + field.Name + " is not compared in Equals");
            }
        }

        private static object NonDefault(Type t)
        {
            if (t == typeof(bool)) return true;
            if (t == typeof(float)) return 1f;
            if (t == typeof(double)) return 1d;
            if (t.IsEnum) return Enum.ToObject(t, 1);
            if (t.IsPrimitive) return Convert.ChangeType(1, t);
            if (t.IsValueType)
            {
                object inner = Activator.CreateInstance(t);
                FieldInfo first = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
                first.SetValue(inner, NonDefault(first.FieldType));
                return inner;
            }
            throw new NotSupportedException(t.Name);
        }

        private static void AssertBlittable(Type t, string path)
        {
            if (t.IsPrimitive || t.IsEnum) return;
            Assert.IsTrue(t.IsValueType, path + " (" + t.Name + ") is a reference type");
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                AssertBlittable(f.FieldType, path + "." + f.Name);
        }
    }
}
