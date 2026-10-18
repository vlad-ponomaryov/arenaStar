using Arena.Core.Input;
using Arena.Net.Serialization;

namespace Arena.Net.Client
{
    /// <summary>
    /// The single point where the client turns a collected input into what the server will simulate (Server-Spec §2.4):
    /// move 2 × int8, aim 2 × int16, 4 button bits — the same value goes into the own prediction and onto the wire, otherwise
    /// every tick would give a systematic correction. Thin wrapper over <see cref="Quantize.Input"/> (D-090: only
    /// <c>Quantize.ToAim</c> / <c>ToMove</c> quantize input).
    /// </summary>
    public static class InputQuantizer
    {
        public static PlayerInput Apply(in PlayerInput raw) => Quantize.Input(in raw);
    }
}
