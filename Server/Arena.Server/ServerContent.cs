using System;
using System.IO;
using Arena.Net.Content;

namespace Arena.Server
{
    /// <summary>
    /// The content pack of the server (Server-Spec §5.4, D-083): <c>ArenaContent.bytes</c> next to the server dll (copied by
    /// the build), loaded once at start-up with the SHA-256 hash re-checked against the header and every def validated.
    /// </summary>
    public sealed class ServerContent
    {
        public const string FileName = "ArenaContent.bytes";

        public readonly ContentBundle Bundle;
        public readonly ContentPackHeader Header;
        public readonly string Path;

        private ServerContent(ContentBundle bundle, ContentPackHeader header, string path)
        {
            Bundle = bundle;
            Header = header;
            Path = path;
        }

        public ulong Hash => Header.ContentHash;

        public static string DefaultPath => System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>Loads and verifies the pack; throws <see cref="InvalidDataException"/> / <see cref="IOException"/> /
        /// <see cref="ArgumentException"/> on a missing, broken or invalid pack.</summary>
        public static ServerContent Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Content pack not found: " + path, path);
            byte[] pack = File.ReadAllBytes(path);
            ContentBundle bundle = ContentPackReader.Read(pack, true, out ContentPackHeader header);
            bundle.Validate();
            return new ServerContent(bundle, header, path);
        }
    }
}
