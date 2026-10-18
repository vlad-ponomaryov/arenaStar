using Arena.Net.Client;
using Arena.Tests.EditMode.Net.Client;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Load
{
    /// <summary>
    /// The long test of Server-Spec §11.1 (category <c>Long</c>, S3.15): 10 full Acorns matches in a row, 6 bot clients each,
    /// over the "bad" network (RTT 250 ms, ≈ 5 % loss in bursts) — every match ends with the server's result at every client,
    /// prediction p99 &lt; 0.25 cells, no teleport over 1 cell. Not part of the usual run: <c>Tools/test.ps1 -Category Long</c>.
    /// </summary>
    [Category("Long")]
    public sealed class LongOnlineTests
    {
        private const int MatchesInARow = 10;
        private const float MaxP99CellsBad = 0.25f;

        [Test]
        public void TenFullBotMatchesInARow_BadConditions_EndWithTheServersResult()
        {
            for (int m = 0; m < MatchesInARow; m++)
            {
                MatchClientRig rig = OnlineMatchTests.RunBotMatch(6, OnlineMatchTests.BadConditions(), 101 + m * 13, out double seconds);
                float worstP99 = 0f;
                for (int k = 0; k < rig.Sessions.Length; k++)
                {
                    MatchClientSession s = rig.Sessions[k];
                    OnlineMatchTests.AssertResult(rig, s);
                    float p99 = s.Predictor.Errors.Percentile(0.99);
                    if (p99 > worstP99) worstP99 = p99;
                    Assert.Less(p99, MaxP99CellsBad, "match " + m + " client " + k + ": p99 of the prediction error");
                    Assert.AreEqual(0, s.NetStats.LargeTeleports, "match " + m + " client " + k + ": no teleport over 1 cell");
                }
                TestContext.WriteLine("match " + m + ": " + seconds.ToString("F0") + " s, winner " + rig.Room.Result.WinnerTeam + ", worst p99 "
                    + worstP99.ToString("F3") + ", missing inputs " + rig.Host.Metrics.MissingInputShare.ToString("P2"));
            }
        }
    }
}
