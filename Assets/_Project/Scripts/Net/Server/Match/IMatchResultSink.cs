namespace Arena.Net.Server.Match
{
    /// <summary>
    /// Receives the result of every match once, on the game thread, when its room enters <see cref="MatchPhase.Ended"/>
    /// (Server-Spec §5.3): stage 3 writes it to <c>logs/matches.jsonl</c> (<c>MatchResultLog</c>), stage 4 sends it to the backend
    /// through the same interface. The room is valid only during the call (<see cref="MatchRoom.Result"/>, slots, state).
    /// </summary>
    public interface IMatchResultSink
    {
        void OnMatchEnded(MatchRoom room);
    }
}
