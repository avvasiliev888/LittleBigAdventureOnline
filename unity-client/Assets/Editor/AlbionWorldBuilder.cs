using System;
using System.Collections.Generic;
using System.IO;
using Albion.Client;
using Client.Network;
using Simulation.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Albion.Editor
{
    /// <summary>Reproducible procedural fantasy world; all playable bounds come from the shared JSON config.</summary>
    public static class AlbionWorldBuilder
    {
        public const string ScenePath = "Assets/Scenes/Arena.unity";
        private const string Folder = "Assets/Resources/Materials";
        private const string MeshFolder = "Assets/Resources/World";
        private static Mesh _rock, _rockLow, _trunk, _trunkLow, _crownLow, _crystal;
        private static Mesh[] _crowns;
        private static readonly int[] CrownSeeds = { 17, 41, 73 };

        [MenuItem("Albion/World/Rebuild Arena")]
        public static void BuildArenaScene()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();
            _rock = SaveMesh("FacetedRock", MakeRock());
            _rockLow = SaveMesh("FacetedRockLow", MakeRockLow());
            _trunk = SaveMesh("TreeTrunk", MakeTrunk(false));
            _trunkLow = SaveMesh("TreeTrunkLow", MakeTrunk(true));
            _crowns = new Mesh[CrownSeeds.Length];
            for (var i = 0; i < CrownSeeds.Length; i++)
            {
                _crowns[i] = SaveMesh("TreeCrown" + (char)('A' + i),
                    MakeCrown(CrownSeeds[i], 8, 10, 0.32f + i * 0.03f, 0.9f + i * 0.12f));
            }

            _crownLow = SaveMesh("TreeCrownLow", MakeCrown(91, 4, 6, 0.18f, 1f));
            _crystal = SaveMesh("OreCrystal", MakeCone());
            var blade = SaveMesh("GrassTuft", MakeGrass());
            var content = ClientContent.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "Config"));
            var map = content.Config.Zones;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = new GameObject("Ground").transform;
            var outline = Material("ToonOutline", "Albion/Toon Outline");
            outline.SetColor("_OutlineColor", Hex("172C3C"));
            outline.SetFloat("_OutlineWidth", 0.012f);
            var bark = Lit("Bark", "5A4A40");
            var barkCharred = Lit("Charred bark", "1D1A1C");
            var stone = Lit("ToonStone", "71868A");
            var mountain = Lit("Mountain rock", "4A4C52");
            mountain.SetColor("_ShadeColor", Hex("24262E"));
            mountain.SetFloat("_RimStrength", 0.18f);
            var path = Ground("Worn sandstone path", "C6B98E", "B0A57C");
            var wood = Lit("Bridge wood", "8D7051");
            var borders = new List<float>();
            for (var i = 1; i < map.Zones.Count; i++)
            {
                borders.Add(map.Zones[i].MinX);
            }

            var rng = new System.Random(6102026);
            for (var zi = 0; zi < map.Zones.Count; zi++)
            {
                var zone = map.Zones[zi];
                var red = zone.Key == "red";
                var gold = zone.Key == "yellow";
                var shade = Hex(red ? "202426" : gold ? "5B4A2A" : "244A48");
                var soil = Ground("Zone " + zone.Key, red ? "34383A" : gold ? "A89E5C" : "4E8C80",
                    red ? "40464A" : gold ? "8A7F45" : "3C706B");
                soil.SetColor("_ShadeColor", shade);
                var leaves = Lit("Leaves " + zone.Key, red ? "303436" : gold ? "D2A94A" : "2F6F66");
                var leavesLight = Lit("Leaf tips " + zone.Key, red ? "5A6266" : gold ? "EBCB70" : "6DB09A");
                leaves.SetColor("_ShadeColor", shade);
                leavesLight.SetColor("_ShadeColor", shade);
                var rocks = Lit("Rock " + zone.Key, red ? "40464A" : gold ? "8E876B" : "7C8F91");
                rocks.SetColor("_ShadeColor", shade);
                var parent = new GameObject("Region " + zone.Key).transform;
                parent.SetParent(ground, false);
                Part(parent, "Terrain " + zone.Key, PrimitiveType.Cube,
                    new Vector3((zone.MinX + zone.MaxX) / 2f, -0.3f, (zone.MinZ + zone.MaxZ) / 2f),
                    new Vector3(zone.MaxX - zone.MinX, 0.6f, zone.MaxZ - zone.MinZ), soil);
                var trail = red ? Ground("Ash trail", "6A5E58", "554A45") : path;
                Part(parent, "Open east-west trail", PrimitiveType.Cube,
                    new Vector3((zone.MinX + zone.MaxX) / 2f, 0.012f, 0f),
                    new Vector3(zone.MaxX - zone.MinX, 0.02f, 6.5f), trail);

                // Dense groves read as woodland from a distance; scattered props fill the meadows.
                for (var c = 0; c < 9; c++)
                {
                    var placed = false;
                    for (var attempt = 0; attempt < 8 && !placed; attempt++)
                    {
                        var cx = Range(rng, zone.MinX + 28f, zone.MaxX - 28f);
                        var cz = Range(rng, zone.MinZ + 28f, zone.MaxZ - 28f);
                        if (Mathf.Abs(cz) < 20f || NearBorder(borders, cx) || (cx * cx + cz * cz) < 900f) continue;
                        placed = true;
                        var groveRadius = Range(rng, 11f, 19f);
                        var trees = 9 + rng.Next(7);
                        for (var t = 0; t < trees; t++)
                        {
                            var angle = Range(rng, 0f, Mathf.PI * 2f);
                            var radius = groveRadius * Mathf.Sqrt((float)rng.NextDouble());
                            var x = cx + (Mathf.Cos(angle) * radius);
                            var z = cz + (Mathf.Sin(angle) * radius);
                            if (Mathf.Abs(z) < 7f || NearBorder(borders, x)) continue;
                            Tree(parent, new Vector3(x, 0f, z), 1f, bark, leaves, leavesLight, rng.Next(3), rng);
                        }
                    }
                }

                // One guaranteed landmark grove per region: the player sees woodland from the main trail.
                var landmarkX = (zone.MinX + zone.MaxX) * 0.5f;
                for (var t = 0; t < 14; t++)
                {
                    var angle = (t / 14f) * Mathf.PI * 2f;
                    var radius = 7f + (t % 4) * 2.5f;
                    Tree(parent, new Vector3(landmarkX + (Mathf.Cos(angle) * radius), 0f, 40f + (Mathf.Sin(angle) * radius)),
                        1f, bark, leaves, leavesLight, t % 3, rng);
                }

                var count = Mathf.CeilToInt((zone.MaxX - zone.MinX) * (zone.MaxZ - zone.MinZ) / 1500f);
                for (var i = 0; i < count; i++)
                {
                    var x = Range(rng, zone.MinX + 4f, zone.MaxX - 4f);
                    var z = Range(rng, zone.MinZ + 3f, zone.MaxZ - 3f);
                    if (Mathf.Abs(z) < 7f || NearBorder(borders, x) || (x * x + z * z) < 45f) continue;
                    if (i % 3 == 0)
                    {
                        Rock(parent, new Vector3(x, 0f, z), Range(rng, 0.6f, 1.5f), rocks, rng);
                    }
                    else
                    {
                        Tree(parent, new Vector3(x, 0f, z), 1f, bark, leaves, leavesLight, rng.Next(3), rng);
                    }
                }
                var leftWall = zi == 0 ? 0f : zone.MinX;
                var rightWall = zi == map.Zones.Count - 1 ? 0f : zone.MaxX;
                var grass = Material("Meadow " + zone.Key, "Albion/Foliage");
                grass.SetColor("_BaseColor", Hex(red ? "363A3C" : gold ? "9A984D" : "438776"));
                grass.SetColor("_TipColor", Hex(red ? "5A6266" : gold ? "D9C572" : "8ABC94"));
                grass.SetFloat("_WindStrength", 0.16f);
                grass.enableInstancing = true;
                var meadow = parent.gameObject.AddComponent<InstancedMeadow>();
                meadow.Blade = blade; meadow.Material = grass; meadow.Seed = 610 + zi;
                meadow.BoundsXZ = new Vector4(zone.MinX, zone.MinZ, zone.MaxX, zone.MaxZ);
                meadow.Walls = new Vector2(leftWall, rightWall);
            }

            // Natural ridges follow the logical borders; the only opening is the configured central pass.
            for (var i = 0; i < map.Zones.Count - 1; i++)
            {
                var left = map.Zones[i];
                var right = map.Zones[i + 1];
                var gate = FindGate(left, right.Id) ?? FindGate(right, left.Id);
                var border = right.MinX;
                Ridge(ground, "border_" + left.Key, border, true, map.MinZ - 8f, map.MaxZ + 8f,
                    gate?.Z ?? 0f, gate?.HalfWidth ?? 6f, mountain, 100 + i);
                BuildGateway(ground, left, right, stone, wood, rng);
            }

            // Outer ridgelines sit outside authoritative bounds; they never block a passable route.
            Ridge(ground, "north_edge", map.MinZ - 5f, false, map.MinX - 8f, map.MaxX + 8f, float.NaN, 0f, mountain, 201);
            Ridge(ground, "south_edge", map.MaxZ + 5f, false, map.MinX - 8f, map.MaxX + 8f, float.NaN, 0f, mountain, 202);
            Ridge(ground, "west_edge", map.MinX - 5f, true, map.MinZ - 8f, map.MaxZ + 8f, float.NaN, 0f, mountain, 203);
            Ridge(ground, "east_edge", map.MaxX + 5f, true, map.MinZ - 8f, map.MaxZ + 8f, float.NaN, 0f, mountain, 204);

            foreach (var node in content.Config.ResourceNodes)
            {
                var position = new Vector3(node.Position.X, 0f, node.Position.Z);
                if (node.Key.Contains("tree")) Tree(ground, position, 0.85f, bark, Lit("Resource leaves", "689779"), Lit("Resource tips", "9DC496"), 1, rng);
                else
                {
                    Rock(ground, position, 0.8f, stone, rng);
                    var crystal = Lit("Ore glint", "84CED2");
                    for (var i = 0; i < 3; i++)
                        MeshPart(ground, "Ore crystal " + node.Id, _crystal, position + new Vector3(i * 0.32f - 0.3f, 0.3f, 0f),
                            new Vector3(0.25f, 0.8f + i * 0.15f, 0.25f), crystal);
                }
            }
            // Ruined waystone frames the spawn without enclosing it.
            for (var i = 0; i < 3; i++)
            {
                var p = new Vector3(-5.5f + i * 2.7f, 0f, 8f);
                Part(ground, "Ancient waystone", PrimitiveType.Cube, p + Vector3.up * 1.5f, new Vector3(0.65f, 3f - i * 0.4f, 0.8f), stone);
                Part(ground, "Waystone cap", PrimitiveType.Cube, p + Vector3.up * (3f - i * 0.2f), new Vector3(1.1f, 0.3f, 1.1f), stone);
            }
            CreateLightingAndCamera();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Stage6 procedural world: three 320x320 regions, Perlin mountain borders, natural pass, varied volumetric trees, charred red zone, instanced meadow.");
        }

        private static void BuildGateway(
            Transform parent, ZoneConfig left, ZoneConfig right, Material stone, Material wood, System.Random rng)
        {
            var border = right.MinX;
            var gate = FindGate(left, right.Id) ?? FindGate(right, left.Id);
            var center = gate?.Z ?? 0f;
            var half = gate?.HalfWidth ?? 6f;
            for (var side = -1; side <= 1; side += 2)
            {
                var z = center + (side * (half + 2.2f));
                Part(parent, "Pass pillar", PrimitiveType.Cube, new Vector3(border, 2.6f, z),
                    new Vector3(2.4f, 5.2f, 2.2f), stone);
                Part(parent, "Pass pillar cap", PrimitiveType.Cube, new Vector3(border, 5.4f, z),
                    new Vector3(3.2f, 0.6f, 3f), stone);
                Rock(parent, new Vector3(border + side * 0.4f, 0f, z + side * 1.6f), Range(rng, 1.4f, 2.2f), stone, rng, false);
            }

            Part(parent, "Pass lintel", PrimitiveType.Cube, new Vector3(border, 5.9f, center),
                new Vector3(2.8f, 1f, (2f * (half + 2.2f)) + 2f), stone);
            Part(parent, "Pass threshold west", PrimitiveType.Cube, new Vector3(border - 3.6f, 0.02f, center),
                new Vector3(0.9f, 0.06f, 2f * half), stone);
            Part(parent, "Pass threshold east", PrimitiveType.Cube, new Vector3(border + 3.6f, 0.02f, center),
                new Vector3(0.9f, 0.06f, 2f * half), stone);
            for (var z = center - half + 0.5f; z <= center + half - 0.3f; z += 1.4f)
            {
                Part(parent, "Pass flagstone", PrimitiveType.Cube, new Vector3(border, 0.11f, z),
                    new Vector3(5.6f, 0.12f, 1.2f), wood);
            }
        }

        private static ZoneGate? FindGate(ZoneConfig zone, uint toZoneId)
        {
            for (var i = 0; i < zone.Gates.Count; i++)
            {
                if (zone.Gates[i].ToZoneId == toZoneId)
                {
                    return zone.Gates[i];
                }
            }

            return null;
        }

        private static bool NearBorder(List<float> borders, float x)
        {
            for (var i = 0; i < borders.Count; i++)
            {
                if (Mathf.Abs(x - borders[i]) < 8f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Tree(Transform parent, Vector3 position, float scale, Material bark, Material leaf, Material tips, int kind, System.Random rng)
        {
            var height = Range(rng, 1.45f, 2.75f) * scale;
            var thickness = Range(rng, 0.72f, 1.35f) * scale;
            var crownWidth = Range(rng, 1.25f, 2.25f) * scale;
            var crownHeight = Range(rng, 0.95f, 1.65f) * scale;
            var root = new GameObject("Procedural tree " + kind).transform;
            root.SetParent(parent, false); root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);

            var high = new GameObject("Crown LOD0").transform; high.SetParent(root, false);
            MeshPart(high, "Tapered trunk", _trunk, Vector3.zero, new Vector3(thickness, height, thickness), bark);
            var crownSeed = kind % _crowns.Length;
            MeshPart(high, "Volumetric crown", _crowns[crownSeed],
                new Vector3(0f, height * 0.82f, 0f), new Vector3(crownWidth, crownHeight, crownWidth),
                kind == 2 ? tips : leaf);
            MeshPart(high, "Crown shoulder", _crowns[(crownSeed + 1) % _crowns.Length],
                new Vector3(crownWidth * 0.38f, height * 0.72f, crownWidth * 0.18f),
                new Vector3(crownWidth * 0.62f, crownHeight * 0.6f, crownWidth * 0.62f), leaf);

            var mid = new GameObject("Crown LOD1").transform; mid.SetParent(root, false);
            MeshPart(mid, "Low trunk", _trunkLow, Vector3.zero, new Vector3(thickness * 1.05f, height, thickness * 1.05f), bark);
            MeshPart(mid, "Low crown", _crownLow, new Vector3(0f, height * 0.8f, 0f),
                new Vector3(crownWidth * 1.05f, crownHeight * 1.05f, crownWidth * 1.05f), leaf);

            var far = new GameObject("Crown LOD2").transform; far.SetParent(root, false);
            MeshPart(far, "Distant crown", _crownLow, new Vector3(0f, height * 0.78f, 0f),
                new Vector3(crownWidth * 0.8f, crownHeight * 0.8f, crownWidth * 0.8f), leaf);

            var lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.14f, high.GetComponentsInChildren<Renderer>()),
                new LOD(0.045f, mid.GetComponentsInChildren<Renderer>()),
                new LOD(0.012f, far.GetComponentsInChildren<Renderer>()),
            });
            lod.RecalculateBounds();
        }

        private static void Rock(Transform parent, Vector3 position, float scale, Material mat, System.Random rng, bool castShadows = true)
        {
            var root = new GameObject("Faceted rock").transform;
            root.SetParent(parent, false); root.localPosition = position;
            root.localRotation = Quaternion.Euler(Range(rng, -12f, 12f), Range(rng, 0f, 360f), 0f);
            var stretch = new Vector3(scale * Range(rng, 1f, 1.6f), scale, scale * Range(rng, 0.9f, 1.3f));
            MeshPart(root, "Rock LOD0", _rock, Vector3.up * scale * 0.45f, stretch, mat, castShadows);
            MeshPart(root, "Rock LOD1", _rockLow, Vector3.up * scale * 0.45f, stretch * 1.04f, mat, false);
            var lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.08f, root.Find("Rock LOD0").GetComponentsInChildren<Renderer>()),
                new LOD(0.02f, root.Find("Rock LOD1").GetComponentsInChildren<Renderer>()),
            });
            lod.RecalculateBounds();
        }

        /// <summary>One jagged Perlin ridge chunk, optionally tapered to zero around a central gate.</summary>
        private static void Ridge(
            Transform parent, string name, float fixedCoord, bool alongZ, float from, float to,
            float gateCenter, float gateHalf, Material material, int seed)
        {
            const float chunkLength = 80f;
            const float step = 3f;
            var chunks = Mathf.Max(1, Mathf.CeilToInt((to - from) / chunkLength));
            for (var c = 0; c < chunks; c++)
            {
                var start = from + c * chunkLength;
                var end = Mathf.Min(start + chunkLength, to);
                var mesh = MakeRidgeMesh(fixedCoord, alongZ, start, end, step, gateCenter, gateHalf, seed + c * 31);
                var asset = SaveMesh("Ridge_" + name + "_" + c, mesh);
                MeshPart(parent, "Ridge " + name + " " + c, asset, Vector3.zero, Vector3.one, material);
            }
        }

        private static Mesh MakeRidgeMesh(
            float fixedCoord, bool alongZ, float from, float to, float step,
            float gateCenter, float gateHalf, int seed)
        {
            var profile = new[] { 0f, 0.56f, 1f, 0.62f, 0f };
            var lateral = new[] { -1f, -0.55f, 0f, 0.55f, 1f };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var rings = Mathf.Max(2, Mathf.CeilToInt((to - from) / step) + 1);
            for (var i = 0; i < rings; i++)
            {
                var w = Mathf.Lerp(from, to, i / (float)(rings - 1));
                var gateMask = float.IsNaN(gateCenter)
                    ? 1f
                    : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(w - gateCenter) - (gateHalf + 1f)) / 5f));
                var broad = Mathf.PerlinNoise((w + seed * 13.1f) * 0.028f, 0.37f);
                var medium = Mathf.PerlinNoise((w + seed * 7.7f) * 0.09f, 0.71f);
                var detail = Mathf.PerlinNoise((w + seed * 3.3f) * 0.31f, 0.19f);
                var height = (7.5f + broad * 7f + medium * 3.2f + detail * 1.4f) * gateMask;
                var halfWidth = 5.2f + medium * 2.6f + detail * 1.1f;
                var peakShift = (Mathf.PerlinNoise(w * 0.07f, seed * 0.13f) - 0.5f) * 2.4f;
                var jag = (detail - 0.5f) * 1.8f;
                for (var j = 0; j < profile.Length; j++)
                {
                    var t = lateral[j];
                    var offset = t * halfWidth;
                    var y = profile[j] * height;
                    if (j == 2) offset += peakShift;
                    if (j == 1 || j == 3) offset += peakShift * 0.4f;
                    if (j > 0 && j < 4) y += jag * (j == 2 ? 1f : 0.45f);
                    var x = alongZ ? fixedCoord + offset : w;
                    var z = alongZ ? w : fixedCoord + offset;
                    vertices.Add(new Vector3(x, Mathf.Max(0.02f, y), z));
                    uvs.Add(new Vector2(t * 0.5f + 0.5f, w * 0.08f));
                }
            }

            var triangles = new List<int>();
            var stride = profile.Length;
            for (var i = 0; i < rings - 1; i++)
            {
                for (var j = 0; j < stride - 1; j++)
                {
                    var a = i * stride + j;
                    var b = (i + 1) * stride + j;
                    var c = (i + 1) * stride + j + 1;
                    var d = i * stride + j + 1;
                    if (alongZ)
                    {
                        triangles.AddRange(new[] { a, b, c, a, c, d });
                    }
                    else
                    {
                        triangles.AddRange(new[] { a, c, b, a, d, c });
                    }
                }
            }

            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray(), uv = uvs.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }
        private static GameObject MeshPart(
            Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Material material, bool castShadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }
        private static Material Material(string name, string shader)
        {
            var path = Folder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find(shader)) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find(shader); mat.enableInstancing = true; EditorUtility.SetDirty(mat); return mat;
        }
        private static Material Lit(string name, string color)
        {
            var mat = Material(name, "Albion/Toon Lit"); mat.SetColor("_BaseColor", Hex(color));
            mat.SetColor("_ShadeColor", Hex("344956")); mat.SetFloat("_RimStrength", 0.12f); return mat;
        }
        private static Material Ground(string name, string a, string b)
        {
            var mat = Material(name, "Albion/Toon Ground"); mat.SetColor("_ColorA", Hex(a)); mat.SetColor("_ColorB", Hex(b));
            mat.SetFloat("_TileSize", 3f); return mat;
        }
        private static Color Hex(string value) => ZoneAtmosphere.Hex(value);
        private static float Range(System.Random rng, float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());
        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            var path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { mesh.name = name; AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); return existing;
        }
        private static Mesh MakeRock()
        {
            var points = new[] { new Vector3(0f,1f,0f), new Vector3(-0.8f,0.25f,-0.7f), new Vector3(0.7f,0.2f,-0.65f), new Vector3(0.85f,0.1f,0.55f), new Vector3(-0.65f,0.15f,0.75f), new Vector3(0f,-0.7f,0f) };
            var faces = new[] { 0,2,1, 0,3,2, 0,4,3, 0,1,4, 5,1,2, 5,2,3, 5,3,4, 5,4,1 };
            return FlatMesh(points, faces);
        }
        private static Mesh MakeRockLow()
        {
            var points = new[] { new Vector3(0f,1f,0f), new Vector3(-0.8f,0.1f,-0.7f), new Vector3(0.8f,0.05f,0.65f), new Vector3(-0.1f,-0.7f,0.2f), new Vector3(0.2f,0.2f,-0.8f) };
            var faces = new[] { 0,2,1, 0,3,2, 0,1,4, 4,1,2, 3,4,2, 3,1,4 };
            return FlatMesh(points, faces);
        }
        private static Mesh MakeTrunk(bool low)
        {
            var sides = low ? 5 : 7;
            var rings = low ? 2 : 3;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var r = 0; r <= rings; r++)
            {
                var y = r / (float)rings;
                var radius = Mathf.Lerp(0.13f, 0.045f, y);
                var bend = Mathf.Sin(y * Mathf.PI) * 0.035f;
                for (var s = 0; s < sides; s++)
                {
                    var angle = s * Mathf.PI * 2f / sides;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius + bend, y, Mathf.Sin(angle) * radius));
                }
            }

            for (var r = 0; r < rings; r++)
            {
                for (var s = 0; s < sides; s++)
                {
                    var a = r * sides + s;
                    var b = r * sides + (s + 1) % sides;
                    var c = (r + 1) * sides + (s + 1) % sides;
                    var d = (r + 1) * sides + s;
                    triangles.AddRange(new[] { a, c, b, a, d, c });
                }
            }

            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh MakeCrown(int seed, int latitude, int longitude, float bump, float squash)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            vertices.Add(new Vector3(0f, squash, 0f));
            for (var lat = 1; lat < latitude; lat++)
            {
                var phi = Mathf.PI * lat / latitude;
                for (var lon = 0; lon < longitude; lon++)
                {
                    var theta = Mathf.PI * 2f * lon / longitude;
                    var normal = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    var noise = Mathf.PerlinNoise(0.5f + normal.x * 1.7f + seed * 0.11f, 0.5f + normal.y * 1.9f + normal.z * 1.3f);
                    var radius = 1f + (noise - 0.5f) * bump * 2f + Mathf.PerlinNoise(normal.z * 3.1f + seed, normal.x * 2.7f) * 0.1f;
                    vertices.Add(new Vector3(normal.x * radius, normal.y * radius * squash, normal.z * radius));
                }
            }

            var bottom = vertices.Count;
            vertices.Add(new Vector3(0f, -squash, 0f));
            for (var lon = 0; lon < longitude; lon++)
            {
                triangles.AddRange(new[] { 0, 1 + (lon + 1) % longitude, 1 + lon });
            }

            for (var lat = 1; lat < latitude - 1; lat++)
            {
                for (var lon = 0; lon < longitude; lon++)
                {
                    var a = 1 + (lat - 1) * longitude + lon;
                    var b = 1 + (lat - 1) * longitude + (lon + 1) % longitude;
                    var c = 1 + lat * longitude + (lon + 1) % longitude;
                    var d = 1 + lat * longitude + lon;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
            }

            var last = 1 + (latitude - 2) * longitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                triangles.AddRange(new[] { bottom, last + lon, last + (lon + 1) % longitude });
            }

            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh MakeCone()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            vertices.Add(Vector3.up);
            for (var i = 0; i < 7; i++) { var angle = i * Mathf.PI * 2f / 7; vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))); }
            vertices.Add(Vector3.zero);
            for (var i = 0; i < 7; i++) { var a = i + 1; var b = (i + 1) % 7 + 1; triangles.AddRange(new[] { 0, b, a, 8, a, b }); }
            return FlatMesh(vertices.ToArray(), triangles.ToArray());
        }
        private static Mesh MakeGrass()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var q = Quaternion.Euler(0f, i * 60f, 0f); var n = v.Count;
                v.Add(q * new Vector3(-0.14f, 0f, 0f)); v.Add(q * new Vector3(0.12f, 0f, 0f)); v.Add(q * new Vector3(0.04f, 0.52f, 0f));
                t.AddRange(new[] { n, n + 2, n + 1 });
            }
            return FlatMesh(v.ToArray(), t.ToArray());
        }
        private static Mesh FlatMesh(Vector3[] points, int[] faces)
        {
            var v = new Vector3[faces.Length]; var t = new int[faces.Length]; var uv = new Vector2[faces.Length];
            for (var i = 0; i < faces.Length; i++) { v[i] = points[faces[i]]; t[i] = i; uv[i] = new Vector2(v[i].x, v[i].y); }
            var mesh = new Mesh { vertices = v, triangles = t, uv = uv }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static void CreateLightingAndCamera()
        {
            var sun = new GameObject("Sun").AddComponent<Light>(); ArenaEnvironment.ConfigureSun(sun);
            ArenaEnvironment.ApplyAmbientAndFog();
            var sky = Material("GradientSky", "Albion/Gradient Sky");
            sky.SetColor("_ZenithColor", Hex("477E9C")); sky.SetColor("_HorizonColor", Hex("BDDCD6")); RenderSettings.skybox = sky;
            var volume = new GameObject("Global Volume").AddComponent<Volume>(); volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AlbionUrpSetup.VolumeProfilePath);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>(); camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.position = new Vector3(0f, 10f, -8f); camera.transform.LookAt(Vector3.zero);
            camera.farClipPlane = 180f;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
    }
}
