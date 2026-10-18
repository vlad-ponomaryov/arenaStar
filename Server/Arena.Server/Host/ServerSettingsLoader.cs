using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Arena.Server.Host
{
    /// <summary>
    /// Reads and writes <c>server.json</c> (Server-Spec §5.5) with System.Text.Json — numbers are culture-free by design, so a
    /// ru-RU PC reads and writes "0.5", not "0,5". Every JSON key maps onto a public field of the same name (case-insensitive)
    /// of the <see cref="ServerConfig"/> sections; nested objects (<c>"Snapshots"</c>) fill nested settings. Keys left out keep
    /// the defaults; an unknown key or a wrong type is an error (a typo must not silently fall back to a default).
    /// Comments (<c>//</c>) and trailing commas are allowed. Start-up only — reflection allocates.
    /// </summary>
    public static class ServerSettingsLoader
    {
        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Loads <paramref name="path"/> over the defaults and validates; throws <see cref="ArgumentException"/> / <see cref="IOException"/> naming the key.</summary>
        public static ServerConfig LoadFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Config not found: " + path, path);
            ServerConfig config = Parse(File.ReadAllText(path, Encoding.UTF8));
            config.Directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return config;
        }

        /// <summary>Parses JSON text over the defaults and validates.</summary>
        public static ServerConfig Parse(string json)
        {
            var config = ServerConfig.CreateDefault();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, ReadOptions);
            }
            catch (JsonException e)
            {
                throw new ArgumentException("server.json: " + e.Message, e);
            }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("server.json: the root must be an object");
                Fill(config, doc.RootElement, "");
            }
            config.Validate();
            return config;
        }

        /// <summary>The effective config as JSON (<c>--print-config</c>, self-test round trip); <see cref="ServerConfig.Directory"/> is not written.</summary>
        public static string ToJson(ServerConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    WriteObject(writer, config);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void Fill(object target, JsonElement element, string prefix)
        {
            Type type = target.GetType();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string key = prefix + property.Name;
                FieldInfo field = type.GetField(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null || field.IsInitOnly || field.Name == nameof(ServerConfig.Directory))
                    throw new ArgumentException("server.json: unknown key " + key);
                Type ft = field.FieldType;
                JsonElement value = property.Value;
                if (IsSection(ft))
                {
                    if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("server.json: " + key + " must be an object");
                    object nested = field.GetValue(target) ?? Activator.CreateInstance(ft);
                    Fill(nested, value, key + ".");
                    field.SetValue(target, nested);
                    continue;
                }
                field.SetValue(target, Convert(value, ft, key));
            }
        }

        private static bool IsSection(Type t) => t.IsClass && t != typeof(string);

        private static object Convert(JsonElement value, Type t, string key)
        {
            try
            {
                if (t == typeof(string))
                {
                    if (value.ValueKind == JsonValueKind.Null) return null;
                    if (value.ValueKind != JsonValueKind.String) throw new FormatException();
                    return value.GetString();
                }
                if (t == typeof(bool))
                {
                    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) throw new FormatException();
                    return value.GetBoolean();
                }
                if (t.IsEnum)
                {
                    if (value.ValueKind != JsonValueKind.String) throw new FormatException();
                    object parsed = Enum.Parse(t, value.GetString(), true);
                    if (!Enum.IsDefined(t, parsed)) throw new FormatException();
                    return parsed;
                }
                if (value.ValueKind != JsonValueKind.Number) throw new FormatException();
                if (t == typeof(int)) return value.GetInt32();
                if (t == typeof(long)) return value.GetInt64();
                if (t == typeof(ushort)) return value.GetUInt16();
                if (t == typeof(byte)) return value.GetByte();
                if (t == typeof(float))
                {
                    float f = value.GetSingle();
                    if (float.IsNaN(f) || float.IsInfinity(f)) throw new FormatException();
                    return f;
                }
                if (t == typeof(double)) return value.GetDouble();
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is ArgumentException || e is OverflowException)
            {
                throw new ArgumentException("server.json: " + key + " = " + value.GetRawText() + " is not a valid " + t.Name, e);
            }
            throw new ArgumentException("server.json: " + key + " has an unsupported type " + t.Name);
        }

        private static void WriteObject(Utf8JsonWriter w, object target)
        {
            w.WriteStartObject();
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || field.Name == nameof(ServerConfig.Directory)) continue;
                object v = field.GetValue(target);
                Type t = field.FieldType;
                if (IsSection(t))
                {
                    if (v == null) continue;
                    w.WritePropertyName(field.Name);
                    WriteObject(w, v);
                    continue;
                }
                if (t == typeof(string)) w.WriteString(field.Name, (string)v);
                else if (t == typeof(bool)) w.WriteBoolean(field.Name, (bool)v);
                else if (t.IsEnum) w.WriteString(field.Name, v.ToString());
                else if (t == typeof(int)) w.WriteNumber(field.Name, (int)v);
                else if (t == typeof(long)) w.WriteNumber(field.Name, (long)v);
                else if (t == typeof(ushort)) w.WriteNumber(field.Name, (ushort)v);
                else if (t == typeof(byte)) w.WriteNumber(field.Name, (byte)v);
                else if (t == typeof(float)) w.WriteNumber(field.Name, (float)v);
                else if (t == typeof(double)) w.WriteNumber(field.Name, (double)v);
                else throw new ArgumentException("ServerSettingsLoader: unsupported field type " + t.Name + " of " + field.Name);
            }
            w.WriteEndObject();
        }
    }
}
