using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>Input</c> (unreliable, client → server, every prediction tick, Server-Spec §2.4): the newest input tick, the last
    /// <see cref="Count"/> inputs newest first (tick = <see cref="NewestTick"/> − i), flags and <c>ViewDelayTicks</c> for lag
    /// compensation (S3.13). An input: move 2 × int8, aim "present" bit + 2 × int16, buttons 4 bits (7 B); an input equal to the
    /// one written before it — 1 bit. Values are quantized on write (<see cref="Quantize.Input"/>). Preallocated, no allocations.
    /// </summary>
    public sealed class InputMessage
    {
        public int NewestTick;

        /// <summary>Inputs in the message, 1…<see cref="NetLimits.MaxInputsPerPacket"/>.</summary>
        public int Count;

        /// <summary>The client lost its snapshot baseline: send a key frame (§4.4).</summary>
        public bool RequestKeyframe;

        /// <summary>round(Tpred − renderTick) at the moment of the input (§2.8; used from S3.13).</summary>
        public int ViewDelayTicks;

        /// <summary>Inputs, newest first: <c>Inputs[i]</c> is for tick <see cref="NewestTick"/> − i.</summary>
        public readonly PlayerInput[] Inputs = new PlayerInput[NetLimits.MaxInputsPerPacket];

        public int TickAt(int index) => NewestTick - index;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.Input);
            w.WriteVarInt(NewestTick);
            int count = Count < 1 ? 1 : (Count > Inputs.Length ? Inputs.Length : Count);
            w.WriteBits((uint)(count - 1), NetProtocol.InputCountBits);
            w.WriteBool(RequestKeyframe);
            w.WriteVarUInt((uint)(ViewDelayTicks < 0 ? 0 : ViewDelayTicks));
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    bool same = Quantize.Input(Inputs[i]).Equals(Quantize.Input(Inputs[i - 1]));
                    w.WriteBool(same);
                    if (same) continue;
                }
                WriteInput(w, in Inputs[i]);
            }
        }

        /// <summary>Reads the body after the kind byte. False — malformed (the packet is dropped).</summary>
        public bool Read(BitReader r)
        {
            NewestTick = r.ReadVarInt();
            Count = (int)r.ReadBits(NetProtocol.InputCountBits) + 1;
            RequestKeyframe = r.ReadBool();
            ViewDelayTicks = (int)r.ReadVarUInt();
            if (r.IsOverflow || Count > Inputs.Length) return false;
            for (int i = 0; i < Count; i++)
            {
                if (i > 0 && r.ReadBool())
                {
                    Inputs[i] = Inputs[i - 1];
                    continue;
                }
                Inputs[i] = ReadInput(r);
            }
            return !r.IsOverflow;
        }

        private static void WriteInput(BitWriter w, in PlayerInput input)
        {
            Quantize.ToMove(input.Move, out sbyte mx, out sbyte my);
            w.WriteSByte(mx);
            w.WriteSByte(my);
            Quantize.ToAim(input.Aim, out short ax, out short ay);
            bool hasAim = ax != 0 || ay != 0;
            w.WriteBool(hasAim);
            if (hasAim)
            {
                w.WriteInt16(ax);
                w.WriteInt16(ay);
            }
            w.WriteBits((uint)(input.Buttons & InputButtons.All), NetProtocol.InputButtonBits);
        }

        private static PlayerInput ReadInput(BitReader r)
        {
            sbyte mx = r.ReadSByte();
            sbyte my = r.ReadSByte();
            Vec2 aim = Vec2.Zero;
            if (r.ReadBool())
            {
                short ax = r.ReadInt16();
                short ay = r.ReadInt16();
                aim = Quantize.FromAim(ax, ay);
            }
            var buttons = (InputButtons)r.ReadBits(NetProtocol.InputButtonBits);
            return new PlayerInput(Quantize.FromMove(mx, my), aim, buttons);
        }
    }
}
