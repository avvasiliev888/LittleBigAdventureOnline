using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Конфиг мобов (ADR-019): репозиторные данные масштабируются по зонам, загрузчик читает
    /// <c>mobs.json</c> и падает на неразрешённых ссылках и точках спавна вне зоны.
    /// </summary>
    public sealed class MobConfigTests
    {
        [Fact]
        public void RepoConfig_HasThreeMobsScaledByZoneDanger()
        {
            var config = ConfigLoader.LoadFromDirectory(RepoConfigDirectory());

            Assert.Equal(3, config.MobTypes.Count);
            Assert.Equal(new[] { "blue", "yellow", "red" }, config.MobTypes.Select(mob => mob.ZoneKey).ToArray());

            var blue = config.MobTypes[0];
            var yellow = config.MobTypes[1];
            var red = config.MobTypes[2];

            Assert.True(blue.Damage < yellow.Damage);
            Assert.True(yellow.Damage < red.Damage);
            Assert.True(blue.AggroRange < yellow.AggroRange);
            Assert.True(yellow.AggroRange < red.AggroRange);
            Assert.True(blue.MaxHp < yellow.MaxHp);
            Assert.True(yellow.MaxHp < red.MaxHp);
            Assert.True(blue.FameOnKill < red.FameOnKill);

            Assert.Equal(2, blue.Spawns.Count);
            Assert.Equal(2, yellow.Spawns.Count);
            Assert.Equal(2, red.Spawns.Count);

            foreach (var mob in config.MobTypes)
            {
                Assert.Equal(ActorKind.Mob, mob.Actor.Kind);
                Assert.False(config.Actors.ContainsKey(mob.ActorId));
                Assert.True(config.Zones.TryGetZoneByKey(mob.ZoneKey, out var zone));
                Assert.NotNull(zone);
                foreach (var spawn in mob.Spawns)
                {
                    Assert.True(zone!.Contains(new Vector3(spawn.X, 0f, spawn.Y)));
                }

                foreach (var entry in mob.Loot)
                {
                    Assert.True(config.Items.ContainsKey(entry.ItemId));
                }
            }
        }

        [Fact]
        public void Loader_ParsesMobsJson()
        {
            var mobsJson = @"[
  {
    ""id"": 1, ""key"": ""blue_scavenger"", ""name"": ""Падальщик"", ""actorId"": 101,
    ""zoneKey"": ""test"", ""abilityId"": 1, ""maxHp"": 60, ""damage"": 4, ""moveSpeed"": 2.2,
    ""attackRange"": 1.6, ""attackSeconds"": 2.0, ""aggroRange"": 5.0, ""leashRange"": 10.0,
    ""respawnSeconds"": 12.0, ""fameOnKill"": 4, ""silverMin"": 3, ""silverMax"": 8,
    ""lootRange"": 2.0, ""corpseSeconds"": 60.0, ""patrolPauseSeconds"": 2.0,
    ""patrol"": [ { ""x"": 3.0, ""z"": 0.0 } ],
    ""spawns"": [ { ""x"": 0.0, ""z"": 0.0 } ],
    ""loot"": [ { ""itemId"": 1, ""minQuantity"": 1, ""maxQuantity"": 2, ""chance"": 0.5 } ]
  }
]";

            var config = TestWorld.LoadFromJson(mobsJson: mobsJson);

            var mob = Assert.Single(config.MobTypes);
            Assert.Equal("blue_scavenger", mob.Key);
            Assert.Equal(101u, mob.ActorId);
            Assert.Equal(60, mob.MaxHp);
            Assert.Equal(2.2f, mob.MoveSpeed, 3);
            Assert.Equal(10f, mob.LeashRange, 3);
            Assert.Single(mob.Spawns);
            Assert.Single(mob.PatrolPoints);
            var loot = Assert.Single(mob.Loot);
            Assert.Equal(0.5f, loot.Chance, 3);
        }

        [Fact]
        public void MobWithoutSpawns_IsRejected()
        {
            var mobsJson = MobJson("\"spawns\": []");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("точка спавна", exception.Message);
        }

        [Fact]
        public void MobWithUnknownLootItem_IsRejected()
        {
            var mobsJson = MobJson(
                "\"spawns\": [ { \"x\": 0.0, \"z\": 0.0 } ], "
                + "\"loot\": [ { \"itemId\": 987, \"minQuantity\": 1, \"maxQuantity\": 1 } ]");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("отсутствующий предмет лута", exception.Message);
        }

        [Fact]
        public void MobSpawnOutsideZone_IsRejected()
        {
            var mobsJson = MobJson("\"spawns\": [ { \"x\": 5000.0, \"z\": 0.0 } ]");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("вне зоны", exception.Message);
        }

        [Fact]
        public void MobPatrolOutsideZone_IsRejected()
        {
            var mobsJson = MobJson(
                "\"spawns\": [ { \"x\": 900.0, \"z\": 0.0 } ], "
                + "\"patrol\": [ { \"x\": 200.0, \"z\": 0.0 } ]");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("патруля", exception.Message);
        }

        [Fact]
        public void MobActorIdCollidingWithActor_IsRejected()
        {
            var mobsJson = MobJson("\"actorId\": 1", "\"spawns\": [ { \"x\": 0.0, \"z\": 0.0 } ]");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("совпадает с актором", exception.Message);
        }

        [Fact]
        public void DuplicateMobIds_AreRejected()
        {
            var one = MobJson("\"spawns\": [ { \"x\": 0.0, \"z\": 0.0 } ]");
            var two = MobJson("\"spawns\": [ { \"x\": 1.0, \"z\": 0.0 } ]");
            var mobsJson = "[" + one.Trim('[', ']') + "," + two.Trim('[', ']') + "]";

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("Дубликат Id моба", exception.Message);
        }

        [Fact]
        public void MobWithLeashShorterThanAggro_IsRejected()
        {
            var mobsJson = MobJson(
                "\"aggroRange\": 10.0, \"leashRange\": 5.0",
                "\"spawns\": [ { \"x\": 0.0, \"z\": 0.0 } ]");

            var exception = Assert.Throws<ConfigValidationException>(() => TestWorld.LoadFromJson(mobsJson: mobsJson));

            Assert.Contains("LeashRange", exception.Message);
        }

        private static string MobJson(params string[] overrides)
        {
            var fields = string.Join(
                ", ",
                new[]
                {
                    "\"id\": 1", "\"key\": \"mob_1\"", "\"name\": \"Mob\"", "\"actorId\": 101",
                    "\"zoneKey\": \"test\"", "\"abilityId\": 1", "\"maxHp\": 60", "\"damage\": 4",
                    "\"moveSpeed\": 2.0", "\"attackRange\": 1.5", "\"attackSeconds\": 2.0",
                    "\"aggroRange\": 5.0", "\"leashRange\": 10.0", "\"respawnSeconds\": 12.0",
                    "\"fameOnKill\": 4", "\"silverMin\": 0", "\"silverMax\": 0",
                    "\"lootRange\": 2.0", "\"corpseSeconds\": 60.0",
                    "\"spawns\": [ { \"x\": 0.0, \"z\": 0.0 } ]",
                });

            var extra = overrides.Length == 0 ? string.Empty : ", " + string.Join(", ", overrides);
            return "[ { " + fields + extra + " } ]";
        }

        private static string RepoConfigDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "config");
        }
    }
}
