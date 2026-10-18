using System;
using System.Collections.Generic;
using System.Reflection;
using Arena.Core.State;

namespace Arena.Net.Diagnostics
{
    /// <summary>
    /// 64-bit FNV-1a over every field of a <see cref="MatchState"/> (Server-Spec §5.4, S3.2): all arrays in full (inactive
    /// slots included), nested structs field by field in declaration order (metadata token), floats bit for bit, bools as
    /// one byte, enums by value. Two runs give the same hash only if their states are identical — a short summary of a whole
    /// match for logs, the server self-test and determinism tests. Walks the fields by reflection, so a new state field is
    /// covered without code changes. Diagnostics only: it boxes values and allocates — never call it in the tick loop.
    /// </summary>
    public static class StateHash
    {
        public const ulong OffsetBasis = 14695981039346656037ul;
        public const ulong Prime = 1099511628211ul;

        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly object CacheLock = new object();

        public static ulong Compute(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ulong h = OffsetBasis;
            AddObject(ref h, state, typeof(MatchState));
            return h;
        }

        /// <summary>Hash as 16 hex digits.</summary>
        public static string Format(ulong hash) => hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

        private static void AddObject(ref ulong h, object value, Type type)
        {
            FieldInfo[] fields = FieldsOf(type);
            for (int i = 0; i < fields.Length; i++) AddValue(ref h, fields[i].GetValue(value), fields[i].FieldType);
        }

        private static void AddValue(ref ulong h, object value, Type type)
        {
            if (type.IsArray)
            {
                var array = (Array)value;
                if (array == null)
                {
                    AddUInt64(ref h, ulong.MaxValue, 4);
                    return;
                }
                AddUInt64(ref h, (ulong)array.Length, 4);
                Type element = type.GetElementType();
                for (int i = 0; i < array.Length; i++) AddValue(ref h, array.GetValue(i), element);
                return;
            }
            if (type.IsEnum)
            {
                AddUInt64(ref h, unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)), 8);
                return;
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: AddUInt64(ref h, (bool)value ? 1ul : 0ul, 1); return;
                case TypeCode.Byte: AddUInt64(ref h, (byte)value, 1); return;
                case TypeCode.SByte: AddUInt64(ref h, (byte)(sbyte)value, 1); return;
                case TypeCode.Int16: AddUInt64(ref h, (ushort)(short)value, 2); return;
                case TypeCode.UInt16: AddUInt64(ref h, (ushort)value, 2); return;
                case TypeCode.Int32: AddUInt64(ref h, (uint)(int)value, 4); return;
                case TypeCode.UInt32: AddUInt64(ref h, (uint)value, 4); return;
                case TypeCode.Int64: AddUInt64(ref h, unchecked((ulong)(long)value), 8); return;
                case TypeCode.UInt64: AddUInt64(ref h, (ulong)value, 8); return;
                case TypeCode.Single: AddUInt64(ref h, (uint)BitConverter.SingleToInt32Bits((float)value), 4); return;
                case TypeCode.Double: AddUInt64(ref h, unchecked((ulong)BitConverter.DoubleToInt64Bits((double)value)), 8); return;
            }
            if (type.IsValueType)
            {
                AddObject(ref h, value, type);
                return;
            }
            throw new NotSupportedException("StateHash: field of type " + type.FullName + " (the state must stay blittable, D-002)");
        }

        /// <summary>Little-endian bytes of <paramref name="value"/>, <paramref name="bytes"/> of them.</summary>
        private static void AddUInt64(ref ulong h, ulong value, int bytes)
        {
            for (int i = 0; i < bytes; i++)
            {
                h ^= (value >> (i * 8)) & 0xFFul;
                h *= Prime;
            }
        }

        private static FieldInfo[] FieldsOf(Type type)
        {
            lock (CacheLock)
            {
                if (FieldCache.TryGetValue(type, out FieldInfo[] cached)) return cached;
                FieldInfo[] fields = type.GetFields(InstanceFields);
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                FieldCache[type] = fields;
                return fields;
            }
        }
    }
}
