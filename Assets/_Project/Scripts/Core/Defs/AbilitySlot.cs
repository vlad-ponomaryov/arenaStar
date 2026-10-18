namespace Arena.Core.Defs
{
    /// <summary>Ability of a fighter by role; with the fighter index forms an ability reference (<see cref="MatchDefs.MakeAbilityRef"/>).</summary>
    public enum AbilitySlot : byte
    {
        Attack = 0,
        Ult = 1,
        Trick = 2
    }
}
