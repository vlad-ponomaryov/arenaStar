using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using LB = Arena.Tests.EditMode.Core.LastBeastTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Tap auto-aim target choice (D-007, D-129): tiers (enemy in the open → berry box in the open → enemy behind a wall),
    /// score (distance + health weight), stickiness of the last target, visibility, and the shot going where the preview points.
    /// </summary>
    public sealed class AutoAimTargetingTests
    {
        // 12×12, wall in column 8 over rows 2–9 (symmetric, so y = rows as well); a bush cell at (3, 0).
        private static readonly string[] WallMap =
        {
            "............",
            "............",
            "........#...",
            "........#...",
            "........#...",
            "........#...",
            "........#...",
            "........#...",
            "........#...",
            "........#...",
            "............",
            "...\"........"
        };

        private const float ShooterX = 6.5f, ShooterY = 5.5f;
        private const float HealthWeight = 1.5f;
        private const int StickyTicks = 60;
        private const float StickyMargin = 2f;

        private static void SetRules(CombatRules r, bool clear, bool boxes, float weight, int sticky)
        {
            r.AutoAimPreferClearShot = clear;
            r.AutoAimTargetsBoxes = boxes;
            r.AutoAimHealthWeightCells = weight;
            r.AutoAimStickyTicks = sticky;
            r.AutoAimStickyMarginCells = StickyMargin;
        }

        private static MatchSimulation OnWallMap(bool clear, float weight, int sticky, params PlayerSlotSetup[] enemies)
        {
            MatchDefs defs = F.CreateDefsOnMap(WallMap);
            SetRules(defs.Rules, clear, true, weight, sticky);
            var slots = new PlayerSlotSetup[enemies.Length + 1];
            slots[0] = F.Slot(0, 0, ShooterX, ShooterY);
            for (int i = 0; i < enemies.Length; i++) slots[i + 1] = enemies[i];
            return F.CreateWith(defs, slots);
        }

        private static int Target(MatchSimulation sim, out Vec2 point) =>
            AbilitySystem.AutoAimTarget(sim.State, sim.Defs.Rules, 0, sim.Defs.Fighters[sim.State.Fighters[0].DefIndex].Attack, out point);

        private static int Target(MatchSimulation sim) => Target(sim, out _);

        private static Vec2 FirstShotDir(MatchSimulation sim)
        {
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Zero, auto: true);
            return sim.State.Projectiles[0].Dir;
        }

        // ---------- enemies ----------

        [Test]
        public void EnemyInTheOpen_BeatsNearerEnemyBehindWall_AndTheShotGoesThere()
        {
            // Slot 1: 4 cells right, behind the wall; slot 2: 5 cells down, clear line.
            MatchSimulation sim = OnWallMap(true, 0f, 0, F.Slot(0, 1, 10.5f, ShooterY), F.Slot(0, 1, ShooterX, 0.5f));
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
            Assert.IsTrue(Vec2.Approximately(new Vec2(0f, -1f), FirstShotDir(sim), 1e-5f), "the shot goes where the preview pointed");
            Assert.AreEqual(AimTargetCode.Fighter(2), sim.State.Fighters[0].AimTargetCode, "the shot remembers its target");
        }

        [Test]
        public void EveryEnemyBehindWalls_TakesTheNearest()
        {
            MatchSimulation sim = OnWallMap(true, 0f, 0, F.Slot(0, 1, 11.5f, ShooterY), F.Slot(0, 1, 10.5f, ShooterY));
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        [Test]
        public void ClearShotOff_TakesTheNearest_EvenBehindWall()
        {
            MatchSimulation sim = OnWallMap(false, 0f, 0, F.Slot(0, 1, 10.5f, ShooterY), F.Slot(0, 1, ShooterX, 0.5f));
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim));
        }

        [Test]
        public void TwoEnemies_NearestWins_TiesToTheLowerSlot()
        {
            MatchSimulation sim = OnWallMap(true, HealthWeight, 0, F.Slot(0, 1, ShooterX, ShooterY + 5f), F.Slot(0, 1, ShooterX - 3f, ShooterY));
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim), "3 cells beats 5");

            MatchSimulation tie = OnWallMap(true, HealthWeight, 0, F.Slot(0, 1, ShooterX, ShooterY + 4f), F.Slot(0, 1, ShooterX - 4f, ShooterY));
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(tie));
        }

        [Test]
        public void WoundedEnemy_SlightlyFarther_IsPreferred_ButNotMuchFarther()
        {
            // Slot 1: 4 cells, full health (score 4 + 1.5); slot 2: 5 cells, 10 % health (5 + 0.15).
            MatchSimulation sim = OnWallMap(true, HealthWeight, 0, F.Slot(0, 1, ShooterX - 4f, ShooterY), F.Slot(0, 1, ShooterX, ShooterY + 5f));
            sim.State.Fighters[2].Health = sim.State.Fighters[2].MaxHealth / 10;
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim), "finish the wounded one");

            // 6 cells, 10 % health: 6.15 > 5.5 — the healthy near one.
            sim.State.Fighters[2].Position = new Vec2(ShooterX, ShooterY + 6f);
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim));
        }

        [Test]
        public void EnemyInBush_IsNeverPicked_EvenWithClearLine()
        {
            // Slot 2 sits in the bush ~5.8 cells away (farther than the reveal distance): only the walled enemy is visible.
            MatchSimulation sim = OnWallMap(true, HealthWeight, 0, F.Slot(0, 1, 10.5f, ShooterY), F.Slot(0, 1, 3.5f, 0.5f));
            Assert.IsFalse(sim.State.Fighters[2].IsVisibleTo(0), "the bush hides slot 2");
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim));
        }

        [Test]
        public void OutOfRange_NoTarget_FiresAlongFacing()
        {
            MatchSimulation sim = OnWallMap(true, HealthWeight, StickyTicks, F.Slot(0, 1, 0.5f, 11.5f));
            Assert.Greater(Vec2.Distance(sim.State.Fighters[1].Position, sim.State.Fighters[0].Position), F.ShooterRange, "8.49 cells away");
            Assert.AreEqual(AimTargetCode.None, Target(sim));
            sim.State.Fighters[0].Facing = Vec2.Right;
            Assert.AreEqual(Vec2.Right, FirstShotDir(sim));
        }

        // ---------- stickiness ----------

        /// <summary>Shooter aimed at slot 1 this tick; slot 1 at 4 cells, slot 2 placed by the test.</summary>
        private static MatchSimulation Sticky(Vec2 second)
        {
            MatchSimulation sim = OnWallMap(true, 0f, StickyTicks, F.Slot(0, 1, ShooterX - 4f, ShooterY), F.Slot(0, 1, second.X, second.Y));
            sim.State.Fighters[0].AimTargetCode = AimTargetCode.Fighter(1);
            sim.State.Fighters[0].AimTargetTick = sim.State.Globals.Tick;
            return sim;
        }

        [Test]
        public void Sticky_KeepsTheLastTarget_WhenAnotherIsOnlySlightlyBetter()
        {
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 3f));
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim), "3 vs 4 cells is within the 2-cell margin: no flicking");
        }

        [Test]
        public void Sticky_Switches_WhenAnotherIsMuchBetter()
        {
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 1.5f));
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim), "1.5 vs 4 cells is beyond the margin");
        }

        [Test]
        public void Sticky_Expires_AfterTheWindow()
        {
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 3f));
            sim.State.Fighters[0].AimTargetTick = sim.State.Globals.Tick - StickyTicks - 1;
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        [Test]
        public void Sticky_Switches_WhenTheTargetHidesInABush()
        {
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 3f));
            sim.State.Fighters[1].Position = new Vec2(3.5f, 0.5f);
            sim.Tick(F.Inputs());
            Assert.IsFalse(sim.State.Fighters[1].IsVisibleTo(0));
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        [Test]
        public void Sticky_Switches_WhenTheTargetGoesBehindAWall()
        {
            // The kept target drops to the "behind a wall" tier; an enemy in the open wins at once.
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 3f));
            sim.State.Fighters[1].Position = new Vec2(10.5f, ShooterY);
            sim.State.Fighters[2].Position = new Vec2(ShooterX, ShooterY + 5f);
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        [Test]
        public void Sticky_Switches_WhenTheTargetIsDefeated()
        {
            MatchSimulation sim = Sticky(new Vec2(ShooterX, ShooterY + 3f));
            sim.State.Fighters[1].Alive = false;
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        [Test]
        public void StickyOff_AlwaysTheBest()
        {
            MatchSimulation sim = OnWallMap(true, 0f, 0, F.Slot(0, 1, ShooterX - 4f, ShooterY), F.Slot(0, 1, ShooterX, ShooterY + 3f));
            sim.State.Fighters[0].AimTargetCode = AimTargetCode.Fighter(1);
            sim.State.Fighters[0].AimTargetTick = sim.State.Globals.Tick;
            Assert.AreEqual(AimTargetCode.Fighter(2), Target(sim));
        }

        // ---------- berry boxes (Last Beast) ----------

        /// <summary>Last Beast test map (boxes at (14, 10) — index 0 — and (10, 14) — index 1), shooter in the middle.</summary>
        private static MatchSimulation OnBoxMap(bool boxes, Vec2 enemy, params GridCell[] walls)
        {
            MatchDefs defs = LB.Defs();
            SetRules(defs.Rules, true, boxes, HealthWeight, StickyTicks);
            foreach (GridCell w in walls) defs.Map.Tiles[w.Row * LB.Size + w.Column] = TileType.Wall;
            return LB.Create(defs, new Vec2(10.5f, 10.5f), enemy);
        }

        private static readonly Vec2 FarAway = new Vec2(2.5f, 2.5f);

        [Test]
        public void EnemyInTheOpen_BeatsANearerBox()
        {
            // Boxes at 4 cells, the enemy at 6 cells straight down.
            MatchSimulation sim = OnBoxMap(true, new Vec2(10.5f, 4.5f));
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim));
        }

        [Test]
        public void NoEnemyInRange_ShootsTheNearestBox()
        {
            MatchSimulation sim = OnBoxMap(true, FarAway);
            Assert.AreEqual(AimTargetCode.Box(0), Target(sim, out Vec2 point), "both boxes at 4 cells: the lower index");
            Assert.IsTrue(Vec2.Approximately(new Vec2(14.5f, 10.5f), point, 1e-5f));
            Assert.IsTrue(Vec2.Approximately(Vec2.Right, FirstShotDir(sim), 1e-5f));
            Assert.AreEqual(AimTargetCode.Box(0), sim.State.Fighters[0].AimTargetCode);
        }

        [Test]
        public void BoxInTheOpen_BeatsAnEnemyBehindAWall()
        {
            // The enemy hides behind box 0 (the box stops projectiles); box 1 is open.
            MatchSimulation sim = OnBoxMap(true, new Vec2(16.5f, 10.5f));
            Assert.IsTrue(AimTargetCode.IsBox(Target(sim)), "a box, not the enemy behind it");
        }

        [Test]
        public void BoxBehindAWall_IsNotTaken()
        {
            // Wall between the shooter and box 0; box 1 is open.
            MatchSimulation sim = OnBoxMap(true, FarAway, new GridCell(12, 10));
            Assert.AreEqual(AimTargetCode.Box(1), Target(sim));

            MatchSimulation walled = OnBoxMap(true, FarAway, new GridCell(12, 10), new GridCell(10, 12));
            Assert.AreEqual(AimTargetCode.None, Target(walled), "both boxes behind walls: nothing to aim at");
        }

        [Test]
        public void BoxesOff_AreIgnored()
        {
            MatchSimulation sim = OnBoxMap(false, FarAway);
            Assert.AreEqual(AimTargetCode.None, Target(sim));
        }

        [Test]
        public void StickyBox_LosesToAnEnemyAtOnce()
        {
            // Stickiness keeps a target only within the best tier: an enemy in the open steps in — switch.
            MatchSimulation sim = OnBoxMap(true, FarAway);
            sim.State.Fighters[0].AimTargetCode = AimTargetCode.Box(0);
            sim.State.Fighters[0].AimTargetTick = sim.State.Globals.Tick;
            sim.State.Fighters[1].Position = new Vec2(10.5f, 4.5f);
            sim.Tick(F.Inputs());
            Assert.AreEqual(AimTargetCode.Fighter(1), Target(sim));
        }

        // ---------- deliveries and the S2 fallback ----------

        [Test]
        public void OnlyWallStoppedDeliveries_NeedAClearLine()
        {
            Assert.IsTrue(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.Projectile }));
            Assert.IsTrue(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.MeleeCone }));
            Assert.IsTrue(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.Dash }));
            Assert.IsTrue(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.Hook }));
            Assert.IsFalse(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.Lob }), "a lob flies over walls");
            Assert.IsFalse(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.SelfArea }));
            Assert.IsFalse(AbilitySystem.NeedsClearLine(new AbilityDef { Delivery = DeliveryKind.Projectile, IgnoresWalls = true }));
        }

        [Test]
        public void WithoutRules_IsTheNearestVisibleEnemy()
        {
            MatchSimulation sim = OnWallMap(true, 0f, 0, F.Slot(0, 1, 10.5f, ShooterY), F.Slot(0, 1, ShooterX, 0.5f));
            Assert.AreEqual(AimTargetCode.Fighter(1),
                TargetingSystem.FindAutoAimTarget(sim.State, null, 0, F.ShooterRange, true, 0, out _));
            var input = new PlayerInput(Vec2.Zero, Vec2.Zero, InputButtons.AutoAim);
            Assert.IsTrue(Vec2.Approximately(Vec2.Right, TargetingSystem.ResolveAim(sim.State, 0, in input, F.ShooterRange), 1e-5f));
        }
    }
}
