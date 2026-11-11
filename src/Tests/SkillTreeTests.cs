using System;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Дерево навыков (M13.2): линейные ветки, пороги Славы и производные бонусы.
    /// Разблокировка не хранится отдельно — она выводится из накопленной Славы.
    /// </summary>
    public sealed class SkillTreeTests
    {
        [Fact]
        public void Branch_ResolvesAbilityId_FromFameThresholds()
        {
            var warrior = ProgressionFixture.Tree().Get("warrior");

            Assert.Equal(ProgressionFixture.WarriorAbilityId, warrior.ResolveAbilityId(0));
            Assert.Equal(ProgressionFixture.WarriorAbilityId, warrior.ResolveAbilityId(79));
            Assert.Equal(ProgressionFixture.WarriorHeavyAbilityId, warrior.ResolveAbilityId(80));
            Assert.Equal(ProgressionFixture.WarriorHeavyAbilityId, warrior.ResolveAbilityId(5000));
        }

        [Fact]
        public void Branch_SumsOnlyUnlockedPassives()
        {
            var warrior = ProgressionFixture.Tree().Get("warrior");

            Assert.Equal(0f, warrior.GetDamageBonus(0));
            Assert.Equal(0f, warrior.GetMaxHpBonus(39));
            Assert.Equal(0.10f, warrior.GetDamageBonus(40), 5);
            Assert.Equal(0f, warrior.GetMaxHpBonus(59));
            Assert.Equal(10, warrior.GetMaxHpBonus(60));
            Assert.Equal(0.10f, warrior.GetDamageBonus(60), 5);
        }

        [Fact]
        public void Branch_ReportsUnlockedCountAndNextNode()
        {
            var warrior = ProgressionFixture.Tree().Get("warrior");

            Assert.Equal(1, warrior.GetUnlockedCount(0));
            Assert.Equal(2, warrior.GetUnlockedCount(40));
            Assert.Equal(3, warrior.GetUnlockedCount(60));
            Assert.Equal(4, warrior.GetUnlockedCount(80));

            Assert.Equal(40, warrior.GetNextNode(0)!.FameThreshold);
            Assert.Equal(60, warrior.GetNextNode(40)!.FameThreshold);
            Assert.Null(warrior.GetNextNode(80));
        }

        [Fact]
        public void Branch_RequiresActiveNodeAtZeroFame()
        {
            var exception = Assert.Throws<ArgumentException>(() => new SkillArchetype("bad", "Плохая", new[]
            {
                new SkillNode(1, "Пассив", 0, SkillNodeKind.Passive, damageBonus: 0.1f),
            }));

            Assert.Contains("способност", exception.Message);
        }

        [Fact]
        public void Branch_RequiresStrictlyIncreasingThresholds()
        {
            Assert.Throws<ArgumentException>(() => new SkillArchetype("bad", "Плохая", new[]
            {
                new SkillNode(1, "База", 0, SkillNodeKind.Active, 1),
                new SkillNode(2, "Повтор", 10, SkillNodeKind.Passive, damageBonus: 0.1f),
                new SkillNode(3, "Ниже", 10, SkillNodeKind.Passive, maxHpBonus: 5),
            }));
        }

        [Fact]
        public void Tree_RejectsDuplicateNodeIdsAcrossBranches()
        {
            var exception = Assert.Throws<ArgumentException>(() => new SkillTree(new[]
            {
                new SkillArchetype("a", "А", new[] { new SkillNode(1, "База", 0, SkillNodeKind.Active, 1) }),
                new SkillArchetype("b", "Б", new[] { new SkillNode(1, "База", 0, SkillNodeKind.Active, 1) }),
            }));

            Assert.Contains("дважды во всём дереве", exception.Message);
        }

        [Fact]
        public void EmptyTree_IsAllowedAndEmpty()
        {
            Assert.Equal(0, SkillTree.Empty.Count);
            Assert.Equal(0, SkillTree.Empty.NodeCount);
            Assert.False(SkillTree.Empty.Has("warrior"));
            Assert.False(SkillTree.Empty.TryGet("warrior", out var branch));
            Assert.Null(branch);
        }

        [Fact]
        public void Node_ValidatesKindAgainstAbilityAndBonuses()
        {
            Assert.Throws<ArgumentException>(
                () => new SkillNode(1, "Актив без способности", 0, SkillNodeKind.Active));
            Assert.Throws<ArgumentException>(
                () => new SkillNode(1, "Актив с пассивом", 0, SkillNodeKind.Active, 5, damageBonus: 0.1f));
            Assert.Throws<ArgumentException>(
                () => new SkillNode(1, "Пассив со способностью", 0, SkillNodeKind.Passive, 5));
            Assert.Throws<ArgumentException>(
                () => new SkillNode(1, "Пассив без бонусов", 0, SkillNodeKind.Passive));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SkillNode(1, "Кривой порог", -1, SkillNodeKind.Active, 5));
        }
    }
}
