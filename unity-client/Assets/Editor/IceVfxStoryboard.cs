using System.Collections.Generic;
using System.IO;
using Albion.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Albion.Editor
{
    /// <summary>
    /// Раскадровка ледяной магии без сборки клиента и без Play Mode: строит сцену, проматывает
    /// время вручную и снимает кадры.
    ///
    /// Ручной проход возможен потому, что все компоненты эффекта имеют <c>Advance(deltaTime)</c>
    /// и <c>Update</c> лишь пробрасывает в него <c>Time.deltaTime</c>. Play Mode здесь был бы
    /// ненадёжен: в batchmode он входит не всегда, а <c>[InitializeOnLoad]</c> срабатывает
    /// раньше <c>-executeMethod</c> и флаг запуска ещё не выставлен.
    ///
    /// Запуск: -executeMethod Albion.Editor.IceVfxStoryboard.Render
    /// </summary>
    public static class IceVfxStoryboard
    {
        private const int Width = 900;
        private const int Height = 600;
        private const string Output = "Artifacts/ice-vfx";

        /// <summary>Момент попадания снаряда: заряд 0,58 с, полёт 24 м/с на 3,6 м.</summary>
        private const float ImpactTime = 0.73f;

        private static Vector3 _impactPoint;

        public static void Render()
        {
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Output));
            Directory.CreateDirectory(output);

            // Вне Play Mode эффект по умолчанию не создаётся — снимаем этот запрет только здесь.
            FrostVfx.AllowEditModeSpawn = true;
            ProjectileVfx.AllowEditModeSpawn = true;

            var animators = new List<IceAnimator>();
            var camera = BuildScene(animators);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Ice storyboard",
                useMipMap = false,
                antiAliasing = 1,
            };
            target.Create();

            var clock = 0f;
            var impact = _impactPoint;
            var burstDone = false;
            try
            {
                // Фазы: заряд, вылет, полёт, попадание, таяние.
                var shots = new[]
                {
                    (Name: "01-charge", Time: 0.30f),
                    (Name: "02-release", Time: 0.44f),
                    (Name: "03-flight", Time: 0.58f),
                    (Name: "04-impact", Time: 0.93f),
                    (Name: "05-settle", Time: 1.70f),
                };

                foreach (var shot in shots)
                {
                    while (clock < shot.Time)
                    {
                        // Попадание рождается в свой момент, а не при постройке сцены:
                        // иначе взрыв уже состарился к кадру заряда.
                        if (!burstDone && clock >= ImpactTime)
                        {
                            FrostVfx.Burst(impact);
                            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(
                                         FindObjectsSortMode.None))
                            {
                                if (behaviour is IceAnimator late && !animators.Contains(late))
                                {
                                    animators.Add(late);
                                }
                            }

                            burstDone = true;
                        }

                        foreach (var animator in animators)
                        {
                            if (animator != null)
                            {
                                animator.Advance(1f / 60f);
                            }
                        }

                        clock += 1f / 60f;
                    }

                    Save(camera, target, output, shot.Name);
                }
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
            }

            Debug.Log("[ice-vfx] раскадровка готова: " + output);
        }

        private static void Save(Camera camera, RenderTexture target, string output, string name)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                throw new System.InvalidOperationException("SRP не умеет StandardRequest");
            }

            // Прогрев объёма и рендереров, затем снимок устоявшегося кадра.
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);

            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                var pixels = image.GetPixels32();

                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());

                // Считаем, а не смотрим глазами: розовый — ошибка шейдера,
                // белые пиксели — мера того, не снова ли «белая хрень».
                var magenta = 0;
                var white = 0;
                var visible = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.r > 220 && pixel.b > 220 && pixel.g < 45) magenta++;
                    if (pixel.r > 230 && pixel.g > 230 && pixel.b > 230) white++;
                    if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8) visible++;
                }

                Debug.Log("[ice-vfx] " + name + " magenta=" + magenta + " white=" + white
                    + " whitePct=" + (100f * white / pixels.Length).ToString("F2")
                    + " visiblePct=" + (100f * visible / pixels.Length).ToString("F1"));

                if (magenta > pixels.Length / 100)
                {
                    Debug.LogError("[ice-vfx] " + name + ": ошибка шейдера, розовый");
                }
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null)
                {
                    Object.DestroyImmediate(image);
                }
            }
        }

        private static Camera BuildScene(List<IceAnimator> animators)
        {
            var cameraObject = new GameObject("Ice camera");
            var camera = cameraObject.AddComponent<Camera>();
            // Сбоку и выше, чтобы манекен не заслонял точку попадания.
            camera.transform.position = new Vector3(-4.6f, 3.1f, -3.4f);
            camera.transform.LookAt(new Vector3(0.4f, 0.9f, 2.6f));
            camera.fieldOfView = 48f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.06f, 0.10f);
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var sunObject = new GameObject("Sun");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.05f;
            sunObject.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
            RenderSettings.sun = sun;

            // Земля: большая плоскость, чтобы преломление было видно.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.GetComponent<Renderer>().material =
                ProceduralMaterials.Lit(new Color(0.20f, 0.22f, 0.25f));
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // Манекен вместо мага: тёмный силуэт задаёт масштаб.
            var mage = new GameObject("Mage stand-in");
            mage.transform.position = new Vector3(-0.9f, 0f, -0.4f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(mage.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            body.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            body.GetComponent<Renderer>().material =
                ProceduralMaterials.Lit(new Color(0.15f, 0.17f, 0.23f));
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var hand = new GameObject("Hand").transform;
            hand.SetParent(mage.transform, false);
            hand.localPosition = new Vector3(0.40f, 1.34f, 0.30f);

            var staff = IceStaff.Attach(null);
            if (staff != null)
            {
                staff.transform.SetParent(hand, false);
            }

            var gem = hand.position + new Vector3(0f, 0.30f, 0.18f);
            var impact = new Vector3(0.5f, 1.0f, 3.6f);

            FrostVfx.ChargeAt(gem, 0.58f);
            ProjectileVfx.IceShard(gem, impact, 0.40f);
            // Взрыв рождается по времени в цикле кадров, а не здесь: иначе он старится до кадра заряда.
            _impactPoint = impact;

            // Собираем всё, что умеет шагать по времени.
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsSortMode.None))
            {
                if (behaviour is IceAnimator animator)
                {
                    animators.Add(animator);
                }
            }

            Debug.Log("[ice-vfx] компонентов эффекта: " + animators.Count);
            LogGrounding();
            return camera;
        }

        /// <summary>
        /// Проверка привязки к земле числом, а не на глаз: основание шипа обязано быть на
        /// уровне земли, иначе шипы висят в воздухе.
        /// </summary>
        private static void LogGrounding()
        {
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            var count = 0;

            foreach (var transform in Object.FindObjectsByType<Transform>(
                         FindObjectsSortMode.None))
            {
                if (!transform.name.StartsWith("Ice spike", System.StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
                lowest = Mathf.Min(lowest, transform.position.y);
                highest = Mathf.Max(highest, transform.position.y);
            }

            Debug.Log("[ice-vfx] шипов=" + count + " baseY min=" + lowest.ToString("F3")
                + " max=" + highest.ToString("F3") + " (земля = 0.0)");
        }
    }
}