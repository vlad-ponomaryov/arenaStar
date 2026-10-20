using System.Collections;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using Arena.Gameplay.View.Anim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// The procedural fighter animation runs in the real Match scene (brief A-TheoAnim AN2): fighters with a rig get an animator,
    /// bones move away from the rest pose while the match plays. Off-screen culling is turned off — in batch mode without
    /// graphics no renderer is ever "visible".
    /// </summary>
    public sealed class FighterAnimSmokeTests
    {
        private const float RunSeconds = 4f;
        private const float MinBoneDegrees = 1f;

        [UnityTest]
        public IEnumerator MatchScene_RiggedFighters_BonesMove()
        {
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Match, LoadSceneMode.Single);
            yield return null;

            var runner = Object.FindFirstObjectByType<MatchRunner>();
            Assert.IsNotNull(runner, "MatchRunner in the scene");
            FighterAnimDriver anim = runner.AnimDriver;
            Assert.IsNotNull(anim, "animation driver created at match start");
            anim.CullOffscreen = false;

            FighterView[] views = Object.FindObjectsByType<FighterView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int rigged = 0;
            for (int i = 0; i < views.Length; i++)
                if (views[i].Rig != null) rigged++;
            int animators = 0;
            for (int i = 0; i < anim.Capacity; i++)
                if (anim.Animator(i) != null) animators++;
            Assert.AreEqual(rigged, animators, "one animator per rigged view");
            if (rigged == 0) Assert.Inconclusive("no rigged fighter model in the line-up");

            float maxDegrees = 0f;
            int applied = anim.AppliedCount;
            float end = Time.realtimeSinceStartup + RunSeconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                for (int v = 0; v < views.Length; v++)
                {
                    FighterRig rig = views[v].Rig;
                    if (rig == null || !views[v].gameObject.activeInHierarchy) continue;
                    for (int b = 0; b < rig.Bones.Length; b++)
                    {
                        Transform t = rig.Bones[b];
                        if (t == null) continue;
                        maxDegrees = Mathf.Max(maxDegrees, Quaternion.Angle(t.localRotation, rig.RestRotations[b]));
                    }
                }
            }
            applied = anim.AppliedCount - applied;
            Debug.Log("ARENA_ANIM_PROBE rigged=" + rigged + " applied=" + applied + " maxBoneDeg=" + maxDegrees.ToString("0.0"));
            Assert.Greater(applied, 0, "bones were written");
            Assert.Greater(maxDegrees, MinBoneDegrees, "some bone turned away from its rest pose");
            Assert.IsTrue(runner.enabled, "no exception");
        }
    }
}
