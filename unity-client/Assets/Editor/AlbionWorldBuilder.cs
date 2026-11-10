using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Albion.Editor
{
    /// <summary>
    /// M10: собирает арену — статичную геометрию с LOD-группами, запечённым occlusion culling,
    /// тёплым светом, туманом и глобальным volume. Сцена коммитится в репозиторий.
    /// Запуск: меню <c>Albion/World/Rebuild Arena</c> или
    /// <c>-executeMethod Albion.Editor.AlbionWorldBuilder.BuildArenaScene</c>.
    /// </summary>
    public static class AlbionWorldBuilder
    {
        /// <summary>Путь к сцене арены.</summary>
        public const string ScenePath = "Assets/Scenes/Arena.unity";

        private const string MaterialsFolder = "Assets/Resources/Materials";
        private const string GroundMaterialPath = MaterialsFolder + "/ToonGround.mat";
        private const string StoneMaterialPath = MaterialsFolder + "/ToonStone.mat";

        /// <summary>Создаёт/пересоздаёт сцену арены и назначает её в Build Settings.</summary>
        [MenuItem("Albion/World/Rebuild Arena")]
        public static void BuildArenaScene()
        {
            var groundMaterial = CreateGroundMaterial();
            var stoneMaterial = CreateStoneMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            MarkStatic(ground, occluder: true);

            CreatePillars(stoneMaterial);
            CreateRocks(stoneMaterial);
            CreateCrystals();

            CreateLighting();
            CreateVolume();
            CreateCamera();

            Albion.Client.ArenaEnvironment.ApplyAmbientAndFog();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            BakeOcclusion();
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log("M10: сцена арены собрана — " + ScenePath);
        }

        private static void CreatePillars(Material material)
        {
            const int count = 8;
            const float radius = 11f;
            for (var i = 0; i < count; i++)
            {
                var angle = (Mathf.PI * 2f / count) * i;
                var position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var pillar = new GameObject("Pillar " + i);
                pillar.transform.position = position;

                var high = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                high.name = "High";
                high.transform.SetParent(pillar.transform, false);
                high.transform.localPosition = new Vector3(0f, 2.4f, 0f);
                high.transform.localScale = new Vector3(0.9f, 2.4f, 0.9f);
                high.GetComponent<Renderer>().sharedMaterial = material;
                RemoveCollider(high);
                MarkStatic(high, occluder: true);

                var low = GameObject.CreatePrimitive(PrimitiveType.Cube);
                low.name = "Low";
                low.transform.SetParent(pillar.transform, false);
                low.transform.localPosition = new Vector3(0f, 2.4f, 0f);
                low.transform.localScale = new Vector3(1.2f, 4.8f, 1.2f);
                low.GetComponent<Renderer>().sharedMaterial = material;
                RemoveCollider(low);
                MarkStatic(low, occluder: true);

                var lodGroup = pillar.AddComponent<LODGroup>();
                lodGroup.SetLODS(new[]
                {
                    new LOD(0.55f, new[] { high.GetComponent<Renderer>() }),
                    new LOD(0.15f, new[] { low.GetComponent<Renderer>() }),
                });
                lodGroup.RecalculateBounds();
            }
        }

        private static void CreateRocks(Material material)
        {
            var random = new System.Random(1234);
            for (var i = 0; i < 6; i++)
            {
                var angle = (float)(random.NextDouble() * Math.PI * 2.0);
                var radius = 6f + (float)random.NextDouble() * 10f;
                var rock = new GameObject("Rock " + i);
                rock.transform.position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                rock.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

                var high = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                high.name = "High";
                high.transform.SetParent(rock.transform, false);
                high.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                high.transform.localScale = new Vector3(1.6f, 1.1f, 1.4f);
                high.GetComponent<Renderer>().sharedMaterial = material;
                RemoveCollider(high);
                MarkStatic(high, occluder: true);

                var low = GameObject.CreatePrimitive(PrimitiveType.Cube);
                low.name = "Low";
                low.transform.SetParent(rock.transform, false);
                low.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                low.transform.localScale = new Vector3(1.5f, 1.1f, 1.3f);
                low.GetComponent<Renderer>().sharedMaterial = material;
                RemoveCollider(low);
                MarkStatic(low, occluder: true);

                var lodGroup = rock.AddComponent<LODGroup>();
                lodGroup.SetLODS(new[]
                {
                    new LOD(0.4f, new[] { high.GetComponent<Renderer>() }),
                    new LOD(0.1f, new[] { low.GetComponent<Renderer>() }),
                });
                lodGroup.RecalculateBounds();
            }
        }

        private static void CreateCrystals()
        {
            var material = CreateCrystalMaterial();
            for (var i = 0; i < 3; i++)
            {
                var angle = Mathf.PI * 2f * i / 3f + 0.4f;
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystal.name = "Crystal " + i;
                crystal.transform.position = new Vector3(Mathf.Cos(angle) * 4.5f, 1.1f, Mathf.Sin(angle) * 4.5f);
                crystal.transform.rotation = Quaternion.Euler(0f, 45f, 45f);
                crystal.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
                crystal.GetComponent<Renderer>().sharedMaterial = material;
                RemoveCollider(crystal);
                MarkStatic(crystal, occluder: false);
            }
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            Albion.Client.ArenaEnvironment.ConfigureSun(light);
        }

        private static void CreateVolume()
        {
            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AlbionUrpSetup.VolumeProfilePath);
            if (profile != null)
            {
                volume.sharedProfile = profile;
            }
            else
            {
                Debug.LogWarning("M10: профиль пост-обработки не найден; сначала выполните Setup URP.");
            }
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.46f, 0.57f, 0.64f);
            camera.allowHDR = true;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = 1;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camera.farClipPlane = 120f;
            camera.transform.position = new Vector3(0f, 10f, -8f);
            camera.transform.LookAt(Vector3.zero);
        }

        private static Material CreateGroundMaterial()
        {
            EnsureMaterialsFolder();
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Albion/Toon Ground");
                if (shader == null)
                {
                    throw new InvalidOperationException("M10: шейдер Albion/Toon Ground не найден.");
                }

                material = new Material(shader) { name = "ToonGround" };
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }

            material.shader = Shader.Find("Albion/Toon Ground");
            material.SetColor("_ColorA", new Color(0.39f, 0.52f, 0.40f));
            material.SetColor("_ColorB", new Color(0.34f, 0.46f, 0.37f));
            material.SetFloat("_TileSize", 2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateStoneMaterial()
        {
            EnsureMaterialsFolder();
            var material = AssetDatabase.LoadAssetAtPath<Material>(StoneMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Albion/Toon Lit");
                if (shader == null)
                {
                    throw new InvalidOperationException("M10: шейдер Albion/Toon Lit не найден.");
                }

                material = new Material(shader) { name = "ToonStone" };
                AssetDatabase.CreateAsset(material, StoneMaterialPath);
            }

            material.shader = Shader.Find("Albion/Toon Lit");
            material.SetFloat("_RimStrength", 0.16f);
            material.SetColor("_BaseColor", new Color(0.65f, 0.66f, 0.68f));
            material.SetColor("_ShadeColor", new Color(0.25f, 0.29f, 0.36f));
            material.SetColor("_RimColor", new Color(0.80f, 0.86f, 0.95f));
            material.SetFloat("_OutlineWidth", 0.012f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateCrystalMaterial()
        {
            const string path = MaterialsFolder + "/ToonCrystal.mat";
            EnsureMaterialsFolder();
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Albion/Toon Lit");
                material = new Material(shader) { name = "ToonCrystal" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find("Albion/Toon Lit");
            material.SetColor("_BaseColor", new Color(0.35f, 0.75f, 0.95f));
            material.SetColor("_ShadeColor", new Color(0.08f, 0.20f, 0.35f));
            material.SetColor("_RimColor", new Color(0.75f, 0.95f, 1f));
            material.SetFloat("_RimStrength", 0.3f);
            material.SetColor("_EmissionColor", new Color(0.15f, 1.0f, 1.8f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BakeOcclusion()
        {
            try
            {
                // В batchmode Umbra может быть недоступна — тогда сцена остаётся с включённым
                // occlusion culling и статическими флагами; запекание оператор повторит в редакторе.
                StaticOcclusionCulling.Compute();
                if (StaticOcclusionCulling.umbraDataSize > 0)
                {
                    Debug.Log("M10: occlusion culling запечён (" + StaticOcclusionCulling.umbraDataSize + " байт).");
                }
                else
                {
                    Debug.LogWarning("M10: occlusion culling не запечён в batchmode — повторите Window/Rendering/Occlusion Culling в редакторе.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("M10: occlusion culling не запечён (" + exception.Message + "); сцена сохранит статические флаги.");
            }
        }

        private static void MarkStatic(GameObject target, bool occluder)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
            if (occluder)
            {
                flags |= StaticEditorFlags.OccluderStatic;
            }

            GameObjectUtility.SetStaticEditorFlags(target, flags);
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void EnsureMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }

                AssetDatabase.CreateFolder("Assets/Resources", "Materials");
            }
        }
    }
}
