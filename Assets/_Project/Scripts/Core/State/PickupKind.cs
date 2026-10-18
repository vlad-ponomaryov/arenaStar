namespace Arena.Core.State
{
    /// <summary>What a <see cref="PickupState"/> is. Append new kinds at the end; 0 = acorn keeps the Acorns mode unchanged.</summary>
    public enum PickupKind : byte
    {
        /// <summary>Acorn of the Acorns mode (worth <see cref="PickupState.Value"/> acorns).</summary>
        Acorn = 0,

        /// <summary>Power berry of the Last Beast mode (worth <see cref="PickupState.Value"/> berries).</summary>
        Berry = 1
    }
}
