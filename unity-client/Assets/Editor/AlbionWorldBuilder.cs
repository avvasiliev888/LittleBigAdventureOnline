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
        private static Mesh _rock, _cone, _canopy;

        [MenuItem("Albion/World/Rebuild Arena")]
        public static void BuildArenaScene()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();
            _rock = SaveMesh("FacetedRock", MakeRock());
            _cone = SaveMesh("TreeCrown", MakeCone());
            var blade = SaveMesh("GrassTuft", MakeGrass());
            var content = ClientContent.LoadFromDirectory(Path.Combine(Application.streamingAssetsPath, "Config"));
            var map = content.Config.Zones;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = new GameObject("Ground").transform;
            var outline = Material("ToonOutline", "Albion/Toon Outline");
            outline.SetColor("_OutlineColor", Hex("172C3C"));
            outline.SetFloat("_OutlineWidth", 0.012f);
            var bark = Lit("Bark", "63544A");
            var stone = Lit("ToonStone", "71868A");
            var path = Ground("Worn sandstone path", "C6B98E", "B0A57C");
            var wood = Lit("Bridge wood", "8D7051");
            var water = Material("River", "Albion/Stylized Water");
            water.SetColor("_BaseColor", Hex("4BA9AD"));
            water.SetColor("_DeepColor", Hex("286778"));
            water.SetColor("_FoamColor", Hex("C5EEE4"));
            water.SetFloat("_Opacity", 0.88f);
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
                var shade = Hex(red ? "4A2A36" : gold ? "5B4A2A" : "244A48");
                var soil = Ground("Zone " + zone.Key, red ? "8A5A60" : gold ? "A89E5C" : "4E8C80",
                    red ? "6A4048" : gold ? "8A7F45" : "3C706B");
                soil.SetColor("_ShadeColor", shade);
                var leaves = Lit("Leaves " + zone.Key, red ? "C97C78" : gold ? "D2A94A" : "2F6F66");
                var leavesLight = Lit("Leaf tips " + zone.Key, red ? "F2B49B" : gold ? "EBCB70" : "6DB09A");
                leaves.SetColor("_ShadeColor", shade);
                leavesLight.SetColor("_ShadeColor", shade);
                var rocks = Lit("Rock " + zone.Key, red ? "B0999E" : gold ? "8E876B" : "7C8F91");
                rocks.SetColor("_ShadeColor", shade);
                var parent = new GameObject("Region " + zone.Key).transform;
                parent.SetParent(ground, false);
                Part(parent, "Terrain " + zone.Key, PrimitiveType.Cube,
                    new Vector3((zone.MinX + zone.MaxX) / 2f, -0.3f, (zone.MinZ + zone.MaxZ) / 2f),
                    new Vector3(zone.MaxX - zone.MinX, 0.6f, zone.MaxZ - zone.MinZ), soil);
                Part(parent, "Open east-west trail", PrimitiveType.Cube,
                    new Vector3((zone.MinX + zone.MaxX) / 2f, 0.012f, 0f),
                    new Vector3(zone.MaxX - zone.MinX, 0.02f, 6.5f), path);
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
                            Tree(parent, new Vector3(x, 0f, z), Range(rng, 0.8f, 1.35f),
                                bark, leaves, leavesLight, rng.Next(3), rng);
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
                        Range(rng, 0.9f, 1.35f), bark, leaves, leavesLight, t % 3, rng);
                }

                var count = Mathf.CeilToInt((zone.MaxX - zone.MinX) * (zone.MaxZ - zone.MinZ) / 1500f);
                for (var i = 0; i < count; i++)
                {
                    var x = Range(rng, zone.MinX + 2.5f, zone.MaxX - 2.5f);
                    var z = Range(rng, zone.MinZ + 3f, zone.MaxZ - 3f);
                    if (Mathf.Abs(z) < 7f || NearBorder(borders, x) || (x * x + z * z) < 45f) continue;
                    if (i % 3 == 0)
                    {
                        Rock(parent, new Vector3(x, 0f, z), Range(rng, 0.6f, 1.4f), rocks, rng);
                    }
                    else
                    {
                        Tree(parent, new Vector3(x, 0f, z), Range(rng, 0.8f, 1.3f),
                            bark, leaves, leavesLight, rng.Next(3), rng);
                    }
                }
                var leftWall = zi == 0 ? 0f : zone.MinX;
                var rightWall = zi == map.Zones.Count - 1 ? 0f : zone.MaxX;
                var grass = Material("Meadow " + zone.Key, "Albion/Foliage");
                grass.SetColor("_BaseColor", Hex(red ? "6D4755" : gold ? "9A984D" : "438776"));
                grass.SetColor("_TipColor", Hex(red ? "BA7780" : gold ? "D9C572" : "8ABC94"));
                grass.SetFloat("_WindStrength", 0.16f);
                grass.enableInstancing = true;
                var meadow = parent.gameObject.AddComponent<InstancedMeadow>();
                meadow.Blade = blade; meadow.Material = grass; meadow.Seed = 610 + zi;
                meadow.BoundsXZ = new Vector4(zone.MinX, zone.MinZ, zone.MaxX, zone.MaxZ);
                meadow.Walls = new Vector2(leftWall, rightWall);
            }

            // Logical borders are walls: the only openings are the configured gates.
            for (var i = 0; i < map.Zones.Count - 1; i++)
            {
                BuildGateway(ground, map.Zones[i], map.Zones[i + 1], stone, wood, water, rng);
            }

            // Outer ridgelines sit outside authoritative bounds; they never block a passable route.
            for (var x = map.MinX; x <= map.MaxX; x += 9f)
            {
                Rock(ground, new Vector3(x, 0f, map.MinZ - 4f), Range(rng, 4f, 7f), stone, rng, false);
                Rock(ground, new Vector3(x, 0f, map.MaxZ + 4f), Range(rng, 4f, 7f), stone, rng, false);
            }

            for (var z = map.MinZ; z <= map.MaxZ; z += 9f)
            {
                Rock(ground, new Vector3(map.MinX - 4f, 0f, z), Range(rng, 4f, 7f), stone, rng, false);
                Rock(ground, new Vector3(map.MaxX + 4f, 0f, z), Range(rng, 4f, 7f), stone, rng, false);
            }
            foreach (var node in content.Config.ResourceNodes)
            {
                var position = new Vector3(node.Position.X, 0f, node.Position.Z);
                if (node.Key.Contains("tree")) Tree(ground, position, 0.85f, bark, Lit("Resource leaves", "689779"), Lit("Resource tips", "9DC496"), 1, rng);
                else
                {
                    Rock(ground, position, 0.8f, stone, rng);
                    var crystal = Lit("Ore glint", "84CED2");
                    for (var i = 0; i < 3; i++)
                        MeshPart(ground, "Ore crystal " + node.Id, _cone, position + new Vector3(i * 0.32f - 0.3f, 0.3f, 0f),
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
            Debug.Log("Stage6 procedural world: three 320x320 regions, walled border gateways, forest, ore, ridgelines, instanced meadow.");
        }

        private static void BuildGateway(
            Transform parent, ZoneConfig left, ZoneConfig right, Material stone, Material wood, Material water, System.Random rng)
        {
            var border = right.MinX;
            var gate = FindGate(left, right.Id) ?? FindGate(right, left.Id);
            var center = gate?.Z ?? 0f;
            var half = gate?.HalfWidth ?? 6f;
            var minZ = Mathf.Max(left.MinZ, right.MinZ) + 2f;
            var maxZ = Mathf.Min(left.MaxZ, right.MaxZ) - 2f;
            for (var z = minZ; z <= maxZ; z += 5f)
            {
                if (Mathf.Abs(z - center) < half + 2.2f) continue;
                Rock(parent, new Vector3(border, 0f, z), Range(rng, 3.4f, 5.8f), stone, rng, false);
                Rock(parent, new Vector3(border + Range(rng, -1.6f, 1.6f), 0f, z + 1.7f),
                    Range(rng, 2.2f, 3.4f), stone, rng, false);
            }

            for (var side = -1; side <= 1; side += 2)
            {
                var z = center + (side * (half + 1.1f));
                Part(parent, "Gate pillar", PrimitiveType.Cube, new Vector3(border, 2.3f, z),
                    new Vector3(2f, 4.6f, 1.8f), stone);
                Part(parent, "Gate pillar cap", PrimitiveType.Cube, new Vector3(border, 4.75f, z),
                    new Vector3(2.8f, 0.5f, 2.6f), stone);
            }

            Part(parent, "Gate lintel", PrimitiveType.Cube, new Vector3(border, 5.3f, center),
                new Vector3(2.4f, 0.9f, (2f * (half + 1.1f)) + 1.4f), stone);
            Part(parent, "Gate stream", PrimitiveType.Cube, new Vector3(border, 0.03f, center),
                new Vector3(2.6f, 0.03f, 2f * half), water);
            for (var z = center - half + 0.5f; z <= center + half - 0.3f; z += 0.85f)
            {
                Part(parent, "Bridge plank", PrimitiveType.Cube, new Vector3(border, 0.11f, z),
                    new Vector3(6.4f, 0.14f, 0.8f), wood);
            }

            Part(parent, "Gate threshold west", PrimitiveType.Cube, new Vector3(border - 3.4f, 0.02f, center),
                new Vector3(0.9f, 0.06f, 2f * half), stone);
            Part(parent, "Gate threshold east", PrimitiveType.Cube, new Vector3(border + 3.4f, 0.02f, center),
                new Vector3(0.9f, 0.06f, 2f * half), stone);
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
                if (Mathf.Abs(x - borders[i]) < 3.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Tree(Transform parent, Vector3 position, float scale, Material bark, Material leaf, Material tips, int kind, System.Random rng)
        {
            var root = new GameObject("Procedural tree " + kind).transform;
            root.SetParent(parent, false); root.localPosition = position; root.localScale = Vector3.one * scale;
            Part(root, "Cylinder trunk", PrimitiveType.Cylinder, new Vector3(0f, 1.2f, 0f), new Vector3(0.35f, 1.2f, 0.35f), bark);
            var high = new GameObject("Crown LOD0").transform; high.SetParent(root, false);
            if (kind == 0)
            {
                for (var i = 0; i < 3; i++) MeshPart(high, "Fir tier", _cone, new Vector3(0f, 1.7f + i * 0.65f, 0f), new Vector3(1.5f - i * 0.3f, 1.7f, 1.5f - i * 0.3f), i == 2 ? tips : leaf);
            }
            else
            {
                for (var i = 0; i < 4; i++) MeshPart(high, "Faceted canopy", _rock,
                    new Vector3((i % 2 - 0.5f) * 1.15f, 2.4f + (i / 2) * 0.7f, (i / 2 - 0.5f) * 0.6f),
                    new Vector3(1.5f, kind == 1 ? 1.1f : 1.65f, 1.35f), i % 2 == 0 ? leaf : tips);
            }
            var low = MeshPart(root, "Crown LOD1", _rock, new Vector3(0f, 2.7f, 0f), new Vector3(2f, 1.8f, 1.6f), leaf);
            var lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.12f, high.GetComponentsInChildren<Renderer>()), new LOD(0.025f, new[] { low.GetComponent<Renderer>() }) });
            lod.RecalculateBounds();
            root.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
        }

        private static void Rock(Transform parent, Vector3 position, float scale, Material mat, System.Random rng, bool castShadows = true)
        {
            var rock = MeshPart(parent, "Faceted rock", _rock, position + Vector3.up * scale * 0.45f,
                new Vector3(scale * Range(rng, 1f, 1.6f), scale, scale * Range(rng, 0.9f, 1.3f)), mat, castShadows);
            rock.transform.localRotation = Quaternion.Euler(Range(rng, -12f, 12f), Range(rng, 0f, 360f), 0f);
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
            camera.farClipPlane = 140f;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
    }
}
