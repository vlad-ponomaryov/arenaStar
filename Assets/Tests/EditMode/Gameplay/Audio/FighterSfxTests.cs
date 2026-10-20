using System.Collections.Generic;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data.Audio;
using Arena.Gameplay.Audio;
using NUnit.Framework;
using UnityEngine;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Gameplay.Audio
{
    public sealed class FighterSfxTests
    {
        private const float Now = 10f;
        private const int Seed = 7;

        private sealed class Recorder : ISfxOutput
        {
            public readonly List<AudioClip> Clips = new List<AudioClip>(64);
            public readonly List<float> Volumes = new List<float>(64);
            public readonly List<float> Pans = new List<float>(64);
            public readonly List<SfxChannel> Channels = new List<SfxChannel>(64);

            public bool Play(AudioClip clip, SfxChannel channel, float volume, float pitch, float pan, int priority)
            {
                Clips.Add(clip);
                Volumes.Add(volume);
                Pans.Add(pan);
                Channels.Add(channel);
                return true;
            }

            public int CountOf(AudioClip clip)
            {
                int n = 0;
                for (int i = 0; i < Clips.Count; i++)
                    if (Clips[i] == clip) n++;
                return n;
            }
        }

        private static AudioClip Clip(string name) => AudioClip.Create(name, 64, 1, 22050, false);

        private static SfxCue Cue(float cooldown, SfxChannel channel = SfxChannel.Sfx, bool localOnly = false, int variants = 1)
        {
            var clips = new AudioClip[variants];
            for (int i = 0; i < variants; i++) clips[i] = Clip("c" + i);
            return new SfxCue { Clips = clips, Volume = 1f, Cooldown = cooldown, Channel = channel, LocalOnly = localOnly };
        }

        private static FighterSoundSet Set()
        {
            var s = ScriptableObject.CreateInstance<FighterSoundSet>();
            s.FighterId = "pix";
            s.StepDistance = 1f;
            s.StepTeleportDistance = 2f;
            s.Step = Cue(0f, variants: 4);
            s.Shot = Cue(0.35f);
            s.Hit = Cue(0.05f);
            s.Ult = Cue(0.5f);
            s.Hurt = Cue(0.1f);
            s.VoEffort = Cue(1f, SfxChannel.Voice);
            s.VoHurt = Cue(1f, SfxChannel.Voice, true);
            s.VoWin = Cue(0f, SfxChannel.Voice, true);
            s.VoLose = Cue(0f, SfxChannel.Voice, true);
            return s;
        }

        private static AudioConfig Config()
        {
            var c = ScriptableObject.CreateInstance<AudioConfig>();
            c.MaxVoices = 8;
            c.SfxVolume = 1f;
            c.VoiceVolume = 1f;
            c.FullVolumeDistance = 3f;
            c.SilentDistance = 14f;
            c.PanStrength = 0.6f;
            c.PanDistance = 6f;
            return c;
        }

        private static FighterSfx Create(Recorder rec, FighterSoundSet set, int setSlot, int localSlot, int localTeam, int slots = 2)
        {
            var sets = new FighterSoundSet[slots];
            sets[setSlot] = set;
            return new FighterSfx(rec, Config(), sets, localSlot, localTeam, Seed);
        }

        [Test]
        public void Attack_PlaysOneShotPerBurst_AndTheHitOnTheVictim()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            FighterSoundSet set = Set();
            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 0, 0, 0);

            inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack);
            float t = Now;
            for (int i = 0; i < 30; i++)
            {
                sim.Tick(inputs);
                inputs[0] = default;
                sfx.Process(sim.Events, sim.State, t);
                t += SimTime.Dt;
            }

            Assert.AreEqual(1, sfx.PlayedCount(FighterSoundKind.Shot), "the burst's drops share one shot sound (cooldown)");
            Assert.Greater(sfx.PlayedCount(FighterSoundKind.Hit), 0, "Theo's drops hitting the enemy");
            Assert.AreEqual(0, sfx.PlayedCount(FighterSoundKind.Hurt), "the dummy has no sound set");
            Assert.IsTrue(sfx.HasSet(0));
            Assert.IsFalse(sfx.HasSet(1));
        }

        [Test]
        public void Ult_PlaysUltAndEffortVoice()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            FighterSoundSet set = Set();
            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 0, 0, 0);
            A.ChargeUlt(sim, 0);
            inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Ult);
            sim.Tick(inputs);
            sfx.Process(sim.Events, sim.State, Now);

            Assert.AreEqual(1, sfx.PlayedCount(FighterSoundKind.Ult));
            Assert.AreEqual(1, sfx.PlayedCount(FighterSoundKind.VoEffort));
            Assert.AreEqual(1, rec.Channels.FindAll(c => c == SfxChannel.Voice).Count, "the effort goes to the voice channel");
        }

        [Test]
        public void Damage_ToTheLocalTheo_PlaysHurtAndPainVoice_ButAnEnemyTheoHasNoPainVoice()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 16f));
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.Damaged, 1, 1, 0, 100, new Vec2(10f, 10f), Vec2.Zero, -1));

            var own = new Recorder();
            FighterSfx local = Create(own, set, 0, 0, 0);
            local.Process(events, sim.State, Now);
            Assert.AreEqual(1, local.PlayedCount(FighterSoundKind.Hurt));
            Assert.AreEqual(1, local.PlayedCount(FighterSoundKind.VoHurt));

            var other = new Recorder();
            FighterSfx enemyView = Create(other, set, 0, 1, 1);
            sim.State.Fighters[0].VisibleToTeams = 0b11u;
            enemyView.Process(events, sim.State, Now);
            Assert.AreEqual(1, enemyView.PlayedCount(FighterSoundKind.Hurt), "a seen enemy Theo groans");
            Assert.AreEqual(0, enemyView.PlayedCount(FighterSoundKind.VoHurt), "pain voice is local only");
            Assert.Less(other.Volumes[0], 1f, "6 units away: faded a little");
        }

        [Test]
        public void HiddenEnemy_IsSilent()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Dummy, 0, 10f, 10f), A.Slot(A.Pix, 1, 10f, 13f));
            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 1, 0, 0);
            sim.State.Fighters[1].VisibleToTeams = 1u << 1;
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 1, -1, 0, new Vec2(10f, 13f), Vec2.Up));
            sfx.Process(events, sim.State, Now);
            Assert.AreEqual(0, rec.Clips.Count, "an enemy in a bush makes no sound");
        }

        [Test]
        public void Steps_FollowTheDistanceRun_AndIgnoreTeleports()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 20f, 20f));
            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 0, 0, 0);
            var events = new SimEventBuffer();
            MatchState state = sim.State;
            float t = Now;
            for (int i = 0; i <= 10; i++)
            {
                state.Fighters[0].Position = new Vec2(10f + i * 0.25f, 10f);
                sfx.Process(events, state, t += 0.1f);
            }
            Assert.AreEqual(2, sfx.PlayedCount(FighterSoundKind.Step), "2.5 units at 1 unit per step");

            state.Fighters[0].Position = new Vec2(2f, 2f);
            sfx.Process(events, state, t += 0.1f);
            Assert.AreEqual(2, sfx.PlayedCount(FighterSoundKind.Step), "respawn jump is not a step");
        }

        [Test]
        public void StepVariants_NeverRepeatTwiceInARow()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 20f, 20f));
            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 0, 0, 0);
            var events = new SimEventBuffer();
            for (int i = 0; i <= 40; i++)
            {
                sim.State.Fighters[0].Position = new Vec2(10f + i, 10f);
                sfx.Process(events, sim.State, Now + i);
            }
            Assert.AreEqual(40, rec.Clips.Count);
            for (int i = 1; i < rec.Clips.Count; i++) Assert.AreNotSame(rec.Clips[i - 1], rec.Clips[i]);
        }

        [Test]
        public void MatchEnd_PlaysWinOrLoseOnce()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 20f, 20f));
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.MatchEnded, 1, -1, -1, 0, Vec2.Zero, Vec2.Zero));

            var rec = new Recorder();
            FighterSfx sfx = Create(rec, set, 0, 0, 0);
            sfx.Process(events, sim.State, Now);
            sfx.Process(events, sim.State, Now + 1f);
            Assert.AreEqual(1, sfx.PlayedCount(FighterSoundKind.VoWin));
            Assert.AreEqual(0, sfx.PlayedCount(FighterSoundKind.VoLose));

            var lost = new SimEventBuffer();
            lost.Add(new SimEvent(SimEventType.MatchEnded, 1, -1, -1, 1, Vec2.Zero, Vec2.Zero));
            FighterSfx sfx2 = Create(new Recorder(), set, 0, 0, 0);
            sfx2.Process(lost, sim.State, Now);
            Assert.AreEqual(1, sfx2.PlayedCount(FighterSoundKind.VoLose));
        }

        [Test]
        public void FarSounds_AreSilent_AndPanToTheirSide()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Dummy, 0, 10f, 10f), A.Slot(A.Pix, 0, 14f, 10f), A.Slot(A.Pix, 0, 40f, 10f));
            var rec = new Recorder();
            var sets = new FighterSoundSet[3];
            sets[1] = set;
            sets[2] = set;
            var sfx = new FighterSfx(rec, Config(), sets, 0, 0, Seed);
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 1, -1, 0, new Vec2(14f, 10f), Vec2.Up));
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 2, -1, 0, new Vec2(40f, 10f), Vec2.Up));
            sfx.Process(events, sim.State, Now);
            Assert.AreEqual(1, rec.Clips.Count, "30 units away: beyond the silent distance");
            Assert.Greater(rec.Pans[0], 0f, "to the right");

            var rotated = new FighterSfx(rec, Config(), sets, 0, 0, Seed);
            rotated.SetScreenRight(-Vec2.Right);
            rotated.Process(events, sim.State, Now);
            Assert.Less(rec.Pans[1], 0f, "rotated view: the same sound is on the left");
        }

        [Test]
        public void Processing_DoesNotAllocate()
        {
            FighterSoundSet set = Set();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 0, -1, 0, new Vec2(10f, 10f), Vec2.Up));
            events.Add(new SimEvent(SimEventType.Damaged, 1, 0, 1, 100, new Vec2(10f, 13f), Vec2.Zero, 0));
            events.Add(new SimEvent(SimEventType.Damaged, 1, 1, 0, 100, new Vec2(10f, 10f), Vec2.Zero, -1));
            var sfx = new FighterSfx(null, Config(), new[] { set, null }, 0, 0, Seed);
            MatchState state = sim.State;
            sfx.Process(events, state, Now);
            Arena.Tests.EditMode.Core.AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    state.Fighters[0].Position = new Vec2(10f + i * 0.3f, 10f);
                    sfx.Process(events, state, Now + i);
                }
            });
        }
    }
}
