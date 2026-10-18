using System;
using System.Reflection;

namespace Arena.Tests.EditMode.Net
{
    /// <summary>
    /// Reflection helpers for the content pack tests: fill every public instance field of a def graph with distinct
    /// non-default values (optionally changing one leaf), and compare two graphs field by field (floats by bits).
    /// A new field in any def class is covered automatically — the writer must handle it or the tests fail.
    /// </summary>
    public sealed class DefReflection
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public;
        private const int ArrayLength = 2;

        private readonly int _perturbLeaf;
        private int _leaf;

        /// <param name="perturbLeaf">Index of the leaf that gets another value (−1 = none).</param>
        private DefReflection(int perturbLeaf)
        {
            _perturbLeaf = perturbLeaf;
        }

        /// <summary>A new <typeparamref name="T"/> with every field filled; <paramref name="leafCount"/> = number of leaves.</summary>
        public static T Filled<T>(int perturbLeaf, out int leafCount) where T : class, new()
        {
            var filler = new DefReflection(perturbLeaf);
            var value = (T)filler.Create(typeof(T));
            leafCount = filler._leaf;
            return value;
        }

        private object Create(Type type)
        {
            if (type.IsArray)
            {
                Type element = type.GetElementType();
                Array array = Array.CreateInstance(element, ArrayLength);
                for (int i = 0; i < ArrayLength; i++) array.SetValue(Create(element), i);
                return array;
            }
            if (IsLeaf(type)) return NextLeaf(type);
            object obj = Activator.CreateInstance(type);
            foreach (FieldInfo f in SortedFields(type))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                f.SetValue(obj, Create(f.FieldType));
            }
            return obj;
        }

        private object NextLeaf(Type type)
        {
            int n = ++_leaf;
            bool perturb = n - 1 == _perturbLeaf;
            int value = perturb ? n + 1000 : n;
            if (type == typeof(bool)) return !perturb;
            if (type == typeof(int)) return value;
            if (type == typeof(float)) return value + 0.25f;
            if (type == typeof(string)) return "s" + value;
            if (type.IsEnum)
            {
                // Byte enums: 1..3 for the baseline, 7 for the changed leaf (both differ from the default 0).
                return Enum.ToObject(type, perturb ? 7 : 1 + n % 3);
            }
            throw new NotSupportedException("DefReflection: unsupported leaf type " + type.FullName);
        }

        public static bool IsLeaf(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string);

        public static FieldInfo[] SortedFields(Type type)
        {
            FieldInfo[] fields = type.GetFields(Fields);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return fields;
        }

        /// <summary>Null if the graphs are equal, otherwise the path of the first difference.</summary>
        public static string Difference(object a, object b, string path)
        {
            if (a == null || b == null) return a == null && b == null ? null : path + ": null vs value";
            Type type = a.GetType();
            if (type != b.GetType()) return path + ": type " + type.Name + " vs " + b.GetType().Name;
            if (type == typeof(float))
                return BitConverter.SingleToInt32Bits((float)a) == BitConverter.SingleToInt32Bits((float)b) ? null : path + ": " + a + " vs " + b;
            if (IsLeaf(type)) return a.Equals(b) ? null : path + ": " + a + " vs " + b;
            if (type.IsArray)
            {
                var x = (Array)a;
                var y = (Array)b;
                if (x.Length != y.Length) return path + ": length " + x.Length + " vs " + y.Length;
                for (int i = 0; i < x.Length; i++)
                {
                    string d = Difference(x.GetValue(i), y.GetValue(i), path + "[" + i + "]");
                    if (d != null) return d;
                }
                return null;
            }
            foreach (FieldInfo f in SortedFields(type))
            {
                string d = Difference(f.GetValue(a), f.GetValue(b), path + "." + f.Name);
                if (d != null) return d;
            }
            return null;
        }
    }
}
