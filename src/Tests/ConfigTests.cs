using System;
using System.IO;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Загрузчик и валидация конфигов (M3, ADR-005): нет дефолтов, ошибки объяснимы.</summary>
    public sealed class ConfigTests
    {
        private const string ValidActors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";

        private const string ValidAbilities = @"[
  { ""id"": 1, ""damage"": 25, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

        [Fact]
        public void LoadFromJson_ValidConfig_BuildsSimulationConfig()
        {
            var config = LoadConfig(ValidActors, ValidAbilities);

            Assert.Single(config.Actors);
            Assert.Single(config.Abilities);
            Assert.Equal(4f, config.GetActor(1).MoveSpeed);
            Assert.Equal(25, config.GetAbility(1).Damage);
        }

        [Fact]
        public void LoadFromJson_ActorWithoutResourceRegen_ThrowsWithFieldName()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig(actors, ValidAbilities));

            Assert.Contains("resourceRegenPerSecond", exception.Message);
        }

        [Fact]
        public void LoadFromJson_CommentsAndTrailingCommas_AreAllowed()
        {
            const string actors = @"[
  // комментарий допустим
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1, },
]";

            var config = LoadConfig(actors, ValidAbilities);

            Assert.Single(config.Actors);
        }

        [Fact]
        public void LoadFromJson_MissingDamage_ThrowsWithFieldName()
        {
            const string abilities = @"[ { ""id"": 1, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 } ]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig(ValidActors, abilities));

            Assert.Contains("damage", exception.Message);
        }

        [Fact]
        public void LoadFromJson_DuplicateAbilityId_Throws()
        {
            const string abilities = @"[
  { ""id"": 1, ""damage"": 25, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 },
  { ""id"": 1, ""damage"": 30, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig(ValidActors, abilities));

            Assert.Contains("Дубликат", exception.Message);
        }

        [Fact]
        public void LoadFromJson_MissingAbilityReference_Throws()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 99 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig(actors, ValidAbilities));

            Assert.Contains("99", exception.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidJson_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig("{ not json", ValidAbilities));

            Assert.Contains("невалидный JSON", exception.Message);
        }

        [Fact]
        public void LoadFromJson_NonArray_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => LoadConfig(@"{ ""actors"": [] }", ValidAbilities));

            Assert.Contains("JSON-массив", exception.Message);
        }

        [Fact]
        public void Load_MissingFile_Throws()
        {
            var missing = Path.Combine(Path.GetTempPath(), "albion-missing-" + Guid.NewGuid().ToString("N"), "actors.json");

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.Load(missing, missing, missing, missing, missing, missing));

            Assert.Contains("не найден", exception.Message);
        }

        [Fact]
        public void LoadFromDirectory_MissingFile_Throws()
        {
            var missing = Path.Combine(Path.GetTempPath(), "albion-missing-" + Guid.NewGuid().ToString("N"));

            var exception = Assert.Throws<ConfigValidationException>(() => ConfigLoader.LoadFromDirectory(missing));

            Assert.Contains("не найден", exception.Message);
        }

        [Fact]
        public void LoadFromDirectory_RepoConfig_Loads()
        {
            var config = ConfigLoader.LoadFromDirectory(RepoConfigDirectory());

            Assert.NotEmpty(config.Actors);
            Assert.NotEmpty(config.Abilities);
            Assert.Equal(3, config.Zones.Zones.Count);
            Assert.NotEmpty(config.Items);
        }

        [Fact]
        public void ZonesAndItems_AreParsedFromJson()
        {
            var config = ConfigLoader.LoadFromJson(
                ValidActors,
                ValidAbilities,
                @"[
  { ""id"": 7, ""key"": ""crimson"", ""kind"": ""red"", ""minX"": -5.0, ""minZ"": -5.0, ""maxX"": 5.0, ""maxZ"": 5.0, ""pvpEnabled"": true, ""fullLootOnDeath"": true, ""durabilityLossFraction"": 0.0, ""hostileFlagSeconds"": 30.0, ""resourceTierMin"": 3, ""resourceTierMax"": 4 }
]",
                @"[
  { ""id"": 10, ""key"": ""sword"", ""kind"": ""equipment"", ""tier"": 2, ""maxDurability"": 90, ""slot"": ""mainHand"", ""archetype"": ""warrior"", ""damageBonus"": 4 }
]",
                "config",
                @"{
  ""archetypes"": [
    { ""key"": ""warrior"", ""name"": ""Воин"", ""nodes"": [
      { ""id"": 1, ""name"": ""Рассечение"", ""fameThreshold"": 0, ""kind"": ""active"", ""abilityId"": 1 }
    ] }
  ]
}");

            var zone = config.Zones.GetZone(new System.Numerics.Vector3(0f, 0f, 0f));
            var item = config.GetItem(10);

            Assert.Equal(ZoneKind.Red, zone.Kind);
            Assert.True(zone.FullLootOnDeath);
            Assert.Equal(30f, zone.HostileFlagSeconds);
            Assert.Equal(ItemKind.Equipment, item.ItemKind);
            Assert.Equal(EquipmentSlot.MainHand, item.Slot);
            Assert.Equal(90, item.MaxDurability);
            Assert.Equal("warrior", item.Archetype);
            Assert.Equal(4, item.DamageBonus);
            Assert.Equal(1, config.SkillTree.Count);
        }

        [Fact]
        public void SkillTreeAndResources_AreParsedFromJson()
        {
            var config = ConfigLoader.LoadFromJson(
                ValidActors,
                ValidAbilities,
                TestWorld.ZonesJson,
                TestWorld.ItemsJson,
                "config",
                TestWorld.SkillTreeJson,
                TestWorld.WoodResourceJson);

            Assert.Equal(1, config.SkillTree.Count);
            Assert.Equal(3, config.SkillTree.NodeCount);

            var warrior = config.SkillTree.Get("warrior");
            Assert.Equal("Воин", warrior.Name);
            Assert.Equal(3, warrior.Nodes.Count);
            Assert.Equal(SkillNodeKind.Active, warrior.Nodes[0].Kind);
            Assert.Equal(1u, warrior.Nodes[0].AbilityId);
            Assert.Equal(SkillNodeKind.Passive, warrior.Nodes[1].Kind);
            Assert.Equal(0.10f, warrior.Nodes[1].DamageBonus, 5);
            Assert.Equal(10, warrior.Nodes[1].FameThreshold);

            var node = Assert.Single(config.ResourceNodes);
            Assert.Equal(7u, node.Id);
            Assert.Equal(1u, node.ItemId);
            Assert.Equal(2, node.Amount);
            Assert.Equal(3, node.FamePerGather);
            Assert.Equal(2f, node.Position.X);
            Assert.Equal(3f, node.Range);
        }

        [Fact]
        public void ItemArchetypeWithoutSkillBranch_Throws()
        {
            const string items = @"[
  { ""id"": 10, ""key"": ""sword"", ""kind"": ""equipment"", ""tier"": 1, ""maxDurability"": 100, ""slot"": ""mainHand"", ""archetype"": ""mage"" }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, TestWorld.ZonesJson, items));

            Assert.Contains("отсутствующую ветку", exception.Message);
            Assert.Contains("mage", exception.Message);
        }

        [Fact]
        public void SkillTreeActiveNodeWithoutAbility_Throws()
        {
            const string tree = @"{
  ""archetypes"": [
    { ""key"": ""warrior"", ""name"": ""Воин"", ""nodes"": [
      { ""id"": 1, ""name"": ""Рассечение"", ""fameThreshold"": 0, ""kind"": ""active"", ""abilityId"": 9 }
    ] }
  ]
}";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(
                    ValidActors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson, "config", tree));

            Assert.Contains("отсутствующую способность", exception.Message);
        }

        [Fact]
        public void DuplicateSkillNodeId_Throws()
        {
            const string tree = @"{
  ""archetypes"": [
    { ""key"": ""a"", ""name"": ""А"", ""nodes"": [
      { ""id"": 1, ""name"": ""База"", ""fameThreshold"": 0, ""kind"": ""active"", ""abilityId"": 1 }
    ] },
    { ""key"": ""b"", ""name"": ""Б"", ""nodes"": [
      { ""id"": 1, ""name"": ""База"", ""fameThreshold"": 0, ""kind"": ""active"", ""abilityId"": 1 }
    ] }
  ]
}";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(
                    ValidActors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson, "config", tree));

            Assert.Contains("дважды во всём дереве", exception.Message);
        }

        [Fact]
        public void ResourceNodeNotMaterial_Throws()
        {
            const string items = @"[
  { ""id"": 11, ""key"": ""chest"", ""kind"": ""equipment"", ""tier"": 1, ""maxDurability"": 100, ""slot"": ""chest"", ""maxHpBonus"": 20 }
]";
            const string resources = @"[
  { ""id"": 7, ""key"": ""vein"", ""itemId"": 11, ""x"": 2.0, ""z"": 0.0, ""amount"": 1, ""gatherSeconds"": 1.0, ""range"": 3.0 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(
                    ValidActors, ValidAbilities, TestWorld.ZonesJson, items, "config", ConfigLoader.EmptySkillTreeJson, resources));

            Assert.Contains("не является сырьём", exception.Message);
        }

        [Fact]
        public void ResourceNodeOutsideZones_Throws()
        {
            const string resources = @"[
  { ""id"": 7, ""key"": ""vein"", ""itemId"": 1, ""x"": 5000.0, ""z"": 0.0, ""amount"": 1, ""gatherSeconds"": 1.0, ""range"": 3.0 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(
                    ValidActors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson, "config", ConfigLoader.EmptySkillTreeJson, resources));

            Assert.Contains("вне зон", exception.Message);
        }

        [Fact]
        public void LoadFromJson_MissingZoneField_ThrowsWithFieldName()
        {
            const string zones = @"[
  { ""id"": 1, ""key"": ""a"", ""kind"": ""blue"", ""minX"": 0.0, ""minZ"": 0.0, ""maxX"": 10.0, ""maxZ"": 10.0, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 0.0, ""resourceTierMin"": 1, ""resourceTierMax"": 2 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, zones, TestWorld.ItemsJson));

            Assert.Contains("pvpEnabled", exception.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownZoneKind_Throws()
        {
            const string zones = @"[
  { ""id"": 1, ""key"": ""a"", ""kind"": ""purple"", ""minX"": 0.0, ""minZ"": 0.0, ""maxX"": 10.0, ""maxZ"": 10.0, ""pvpEnabled"": true, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 30.0, ""resourceTierMin"": 1, ""resourceTierMax"": 2 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, zones, TestWorld.ItemsJson));

            Assert.Contains("purple", exception.Message);
        }

        [Fact]
        public void LoadFromJson_OverlappingZones_Throw()
        {
            const string zones = @"[
  { ""id"": 1, ""key"": ""a"", ""kind"": ""yellow"", ""minX"": 0.0, ""minZ"": 0.0, ""maxX"": 10.0, ""maxZ"": 10.0, ""pvpEnabled"": true, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 30.0, ""resourceTierMin"": 1, ""resourceTierMax"": 2 },
  { ""id"": 2, ""key"": ""b"", ""kind"": ""red"", ""minX"": 5.0, ""minZ"": 5.0, ""maxX"": 15.0, ""maxZ"": 15.0, ""pvpEnabled"": true, ""fullLootOnDeath"": true, ""durabilityLossFraction"": 0.0, ""hostileFlagSeconds"": 30.0, ""resourceTierMin"": 3, ""resourceTierMax"": 4 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, zones, TestWorld.ItemsJson));

            Assert.Contains("пересекаются", exception.Message);
        }

        [Fact]
        public void LoadFromJson_ZonesWithGap_Throw()
        {
            const string zones = @"[
  { ""id"": 1, ""key"": ""a"", ""kind"": ""blue"", ""minX"": -10.0, ""minZ"": 0.0, ""maxX"": 0.0, ""maxZ"": 10.0, ""pvpEnabled"": false, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 0.0, ""resourceTierMin"": 1, ""resourceTierMax"": 2 },
  { ""id"": 2, ""key"": ""b"", ""kind"": ""yellow"", ""minX"": 1.0, ""minZ"": 0.0, ""maxX"": 10.0, ""maxZ"": 10.0, ""pvpEnabled"": true, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 30.0, ""resourceTierMin"": 1, ""resourceTierMax"": 2 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, zones, TestWorld.ItemsJson));

            Assert.Contains("не покрывают мир", exception.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownStarterItem_Throws()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1, ""starterItems"": [ { ""itemId"": 404, ""quantity"": 1 } ] }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(actors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson));

            Assert.Contains("404", exception.Message);
        }

        [Fact]
        public void LoadFromJson_EquipmentAsStarterItem_Throws()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1, ""starterEquipment"": [ { ""itemId"": 1 } ] }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(actors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson));

            Assert.Contains("не является снаряжением", exception.Message);
        }

        [Fact]
        public void LoadFromJson_MaterialWithSlot_Throws()
        {
            const string items = @"[
  { ""id"": 1, ""key"": ""wood"", ""kind"": ""material"", ""tier"": 1, ""maxDurability"": 0, ""slot"": ""chest"" }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, ValidAbilities, TestWorld.ZonesJson, items));

            Assert.Contains("не может занимать слот", exception.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownActorKind_Throws()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""npc"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(actors, ValidAbilities, TestWorld.ZonesJson, TestWorld.ItemsJson));

            Assert.Contains("npc", exception.Message);
        }

        [Fact]
        public void ActorConfig_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(0, 100, 4f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 0, 4f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 0f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, float.NaN, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, -1, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 0f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 0));
        }

        [Fact]
        public void ActorConfig_RejectsInvalidResourceRegen()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, float.PositiveInfinity));
        }

        [Fact]
        public void AbilityConfig_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(0, 25, 3f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, -1, 3f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, -1f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, 3f, -1f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, 3f, 2f, -1));
        }

        [Fact]
        public void SimulationConfig_RejectsMissingReference()
        {
            var actor = new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, abilityId: 7);
            var ability = new Simulation.Core.AbilityConfig(1, 25, 3f, 2f, 10);

            var exception = Assert.Throws<ArgumentException>(
                () => new Simulation.Core.SimulationConfig(
                    new[] { actor },
                    new[] { ability },
                    TestWorld.DefaultZones,
                    TestWorld.DefaultItems,
                    TestWorld.DefaultSkillTree));

            Assert.Contains("7", exception.Message);
        }

        private static Simulation.Core.SimulationConfig LoadConfig(string actorsJson, string abilitiesJson)
        {
            return ConfigLoader.LoadFromJson(actorsJson, abilitiesJson, TestWorld.ZonesJson, TestWorld.ItemsJson);
        }

        private static string RepoConfigDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "config");
        }
    }
}
