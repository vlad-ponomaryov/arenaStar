using System.Security.Cryptography;
using Arena.Net.Serialization;

namespace Arena.Net.Content
{
    /// <summary>
    /// Header of the content pack <c>ArenaContent.bytes</c> (Server-Spec §4.1, §5.4): magic, format version, body length and
    /// <see cref="ContentHash"/> — the first 8 bytes of SHA-256 of the body. The online client takes the hash from the header
    /// of its built-in pack (no SHA-256 on the phone); the server recomputes it when loading (D-083).
    /// Layout (little-endian, 18 bytes): u32 magic, u16 format version, u32 body length, u64 content hash.
    /// </summary>
    public struct ContentPackHeader
    {
        /// <summary>ASCII "ARNC" little-endian.</summary>
        public const uint MagicValue = 0x434E5241u;

        /// <summary>Format of the pack body; bump on any change of <see cref="ContentPackWriter"/> (D-091).</summary>
        public const ushort CurrentFormatVersion = 4;

        public const int Size = 18;

        /// <summary>Hash bytes taken from the SHA-256 digest.</summary>
        public const int HashBytes = 8;

        public uint Magic;
        public ushort FormatVersion;
        public int BodyLength;

        /// <summary>First 8 bytes of SHA-256 of the body, big-endian (its hex equals the start of the SHA-256 hex string).</summary>
        public ulong ContentHash;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(Magic);
            writer.WriteUInt16(FormatVersion);
            writer.WriteInt32(BodyLength);
            writer.WriteUInt64(ContentHash);
        }

        /// <summary>Reads the header; false if the data is too short or the magic is wrong (version is checked by the caller).</summary>
        public static bool TryRead(byte[] data, int length, out ContentPackHeader header)
        {
            header = default;
            if (data == null || length < Size || length > data.Length) return false;
            var reader = new BitReader(data, Size);
            header.Magic = reader.ReadUInt32();
            header.FormatVersion = reader.ReadUInt16();
            header.BodyLength = reader.ReadInt32();
            header.ContentHash = reader.ReadUInt64();
            return !reader.IsOverflow && header.Magic == MagicValue;
        }

        /// <summary>First 8 bytes of SHA-256 of <paramref name="count"/> bytes from <paramref name="offset"/>, big-endian.</summary>
        public static ulong ComputeHash(byte[] data, int offset, int count)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(data, offset, count);
            ulong hash = 0;
            for (int i = 0; i < HashBytes; i++) hash = (hash << 8) | digest[i];
            return hash;
        }

        /// <summary>Hash as 16 hex digits (logs, <c>--version</c>).</summary>
        public static string FormatHash(ulong hash) => hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
