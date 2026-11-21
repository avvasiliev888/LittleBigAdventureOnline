using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Летящий снаряд атаки: стрела лучника или магический шар посоха.
    /// Эффект чисто визуальный: сервер считает урон мгновенно (hitscan), а снаряд
    /// только объясняет игроку, откуда прилетело и через какой стиль атаки.
    /// </summary>
    public sealed class ProjectileVfx : MonoBehaviour
    {
        private const float ArrowSpeed = 34f;
        private const float OrbSpeed = 18f;

        /// <summary>
        /// Осколок летит заметно медленнее шара: на дистанции 10 м у игрока есть время
        /// увидеть бросок и среагировать. Урон всё равно наносится в момент жеста.
        /// </summary>
        private const float ShardSpeed = 24f;

        private Vector3 _from;
        private Vector3 _to;
        private float _speed = ArrowSpeed;
        private float _delay;
        private float _age;
        private float _duration = 1f;
        private Color _impactColor = Color.white;
        private bool _isShard;

        /// <summary>Запускает стрелу от руки стрелка к цели.</summary>
        public static void Arrow(Vector3 from, Vector3 to, float delay)
        {
            Spawn(from, to, delay, isOrb: false);
        }

        /// <summary>Запускает магический шар от посоха к цели.</summary>
        public static void Orb(Vector3 from, Vector3 to, float delay)
        {
            Spawn(from, to, delay, isOrb: true);
        }

        /// <summary>
        /// Ледяной осколок мага: гранёная глыба льда с морозным шлейфом. Задержка нужна,
        /// чтобы снаряд вылетал в момент броска, а не одновременно с началом замаха.
        /// </summary>
        public static void IceShard(Vector3 from, Vector3 to, float delay)
        {
            Spawn(from, to, delay, isOrb: true, isShard: true);
        }

        private static void Spawn(Vector3 from, Vector3 to, float delay, bool isOrb, bool isShard = false)
        {
            // В EditMode-проверках эффект не создаётся: он не должен попадать в редактируемую сцену.
            if (!Application.isPlaying)
            {
                return;
            }

            if (!IsFinite(from) || !IsFinite(to))
            {
                return;
            }

            var direction = to - from;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.forward;
            }

            var root = new GameObject(isShard ? "Ice shard" : isOrb ? "Magic orb" : "Arrow");
            root.transform.position = from;
            root.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

            var projectile = root.AddComponent<ProjectileVfx>();
            projectile._from = from;
            projectile._to = to;
            projectile._delay = Mathf.Max(0f, delay);
            projectile._speed = isShard ? ShardSpeed : isOrb ? OrbSpeed : ArrowSpeed;
            projectile._isShard = isShard;
            projectile._impactColor = isShard
                ? new Color(0.62f, 0.90f, 1f)
                : isOrb
                    ? new Color(0.55f, 0.45f, 1f)
                    : new Color(0.95f, 0.93f, 0.78f);

            // +Z — направление полёта, поэтому снаряд строится вдоль локальной оси Z.
            if (isShard)
            {
                BuildIceShard(root.transform, projectile._impactColor);
            }
            else if (isOrb)
            {
                BuildOrb(root.transform, projectile._impactColor);
            }
            else
            {
                BuildArrow(root.transform);
            }

            var distance = Vector3.Distance(from, to);
            projectile._duration = projectile._delay + (distance / Mathf.Max(1f, projectile._speed));
            Destroy(root, projectile._duration + 1.5f);
        }

        private static void BuildArrow(Transform parent)
        {
            var shaft = new GameObject("Shaft").transform;
            shaft.SetParent(parent, false);
            shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.localScale = new Vector3(0.035f, 0.62f, 0.035f);
            shaft.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cylinder;
            AddRenderer(shaft.gameObject, ProceduralMaterials.Lit(new Color(0.62f, 0.44f, 0.26f)));

            var tip = new GameObject("Tip").transform;
            tip.SetParent(parent, false);
            tip.localPosition = new Vector3(0f, 0f, 0.36f);
            tip.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tip.localScale = new Vector3(0.06f, 0.16f, 0.06f);
            tip.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cone;
            AddRenderer(tip.gameObject, ProceduralMaterials.Glow(new Color(0.85f, 0.88f, 0.92f)));

            for (var i = 0; i < 2; i++)
            {
                var feather = new GameObject("Fletching").transform;
                feather.SetParent(parent, false);
                feather.localPosition = new Vector3(0f, 0f, -0.28f);
                feather.localRotation = Quaternion.Euler(90f, i * 90f, 0f);
                feather.localScale = new Vector3(0.09f, 0.11f, 0.012f);
                feather.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Box;
                AddRenderer(feather.gameObject, ProceduralMaterials.Lit(new Color(0.93f, 0.93f, 0.96f)));
            }

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.startWidth = 0.035f;
            trail.endWidth = 0f;
            trail.sharedMaterial = ProceduralMaterials.Glow(new Color(0.85f, 0.9f, 1f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        private static void BuildOrb(Transform parent, Color color)
        {
            var sphere = new GameObject("Orb").transform;
            sphere.SetParent(parent, false);
            sphere.localScale = Vector3.one * 0.34f;
            sphere.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Sphere;
            AddRenderer(sphere.gameObject, ProceduralMaterials.Glow(color));
            var shell = new GameObject("Orb shell").transform;
            shell.SetParent(parent, false);
            shell.localScale = Vector3.one * 0.5f;
            shell.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Sphere;
            AddRenderer(shell.gameObject, ProceduralMaterials.Glow(new Color(0.3f, 0.2f, 0.75f, 0.55f)));

            var light = parent.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 5f;
            light.intensity = 1.6f;

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 0.22f;
            trail.endWidth = 0f;
            trail.sharedMaterial = ProceduralMaterials.Glow(color);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        /// <summary>
        /// Гранёная ледяная глыба: вытянутое ядро, две встречные пирамиды-шипа и морозный
        /// шлейф. Форма читается как лёд, а не как шар: у неё есть направление и острые грани.
        /// </summary>
        private static void BuildIceShard(Transform parent, Color color)
        {
            var core = new GameObject("Shard core").transform;
            core.SetParent(parent, false);
            core.localRotation = Quaternion.Euler(90f, 0f, 0f);
            core.localScale = new Vector3(0.16f, 0.44f, 0.16f);
            core.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cylinder;
            AddRenderer(core.gameObject, ProceduralMaterials.Glow(new Color(0.55f, 0.86f, 1f)));

            // Носок осколка: конус смотрит вперёд по +Z и задаёт направление полёта.
            var tip = new GameObject("Shard tip").transform;
            tip.SetParent(parent, false);
            tip.localPosition = new Vector3(0f, 0f, 0.26f);
            tip.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tip.localScale = new Vector3(0.20f, 0.34f, 0.20f);
            tip.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cone;
            AddRenderer(tip.gameObject, ProceduralMaterials.Glow(new Color(0.90f, 0.98f, 1f)));

            var tail = new GameObject("Shard tail").transform;
            tail.SetParent(parent, false);
            tail.localPosition = new Vector3(0f, 0f, -0.24f);
            tail.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            tail.localScale = new Vector3(0.15f, 0.26f, 0.15f);
            tail.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cone;
            AddRenderer(tail.gameObject, ProceduralMaterials.Glow(new Color(0.30f, 0.60f, 0.95f)));

            // Три ребра по кругу: без них осколок выглядит гладким цилиндром.
            for (var i = 0; i < 3; i++)
            {
                var rib = new GameObject("Shard rib").transform;
                rib.SetParent(parent, false);
                rib.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                rib.localPosition = Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0f, 0.06f);
                rib.localScale = new Vector3(0.05f, 0.30f, 0.05f);
                rib.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Box;
                AddRenderer(rib.gameObject, ProceduralMaterials.Glow(new Color(0.72f, 0.93f, 1f)));
            }

            var light = parent.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 4.5f;
            light.intensity = 2.2f;

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.30f;
            trail.startWidth = 0.26f;
            trail.endWidth = 0f;
            trail.sharedMaterial = ProceduralMaterials.Glow(new Color(0.45f, 0.80f, 1f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        private static void AddRenderer(GameObject target, Material material)
        {
            var renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age < _delay)
            {
                return;
            }

            var flight = Mathf.Clamp01((_age - _delay) / Mathf.Max(0.05f, _duration - _delay));
            // Небольшая дуга: снаряд не идёт по идеально прямой, но и не улетает вверх.
            // Ледяной осколок летит по прямой — тяжёлая глыба не должна планировать.
            var arc = _isShard
                ? 0f
                : 4f * flight * (1f - flight) * Mathf.Clamp(Vector3.Distance(_from, _to) * 0.05f, 0f, 0.6f);
            var position = Vector3.Lerp(_from, _to, flight) + (Vector3.up * arc);
            transform.position = position;

            var direction = _to - _from;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            // Осколок медленно вращается вокруг оси полёта: лёд блестит гранями.
            if (_isShard)
            {
                transform.Rotate(Vector3.forward, 260f * Time.deltaTime, Space.Self);
            }

            if (_age >= _delay + Mathf.Max(0.05f, _duration - _delay))
            {
                Impact();
                Destroy(gameObject);
            }
        }

        private void Impact()
        {
            GameObject impact = null;
            if (Application.isPlaying)
            {
                impact = new GameObject("Projectile impact");
                impact.transform.position = _to;
                var burst = impact.AddComponent<ParticleSystem>();
                var main = burst.main;
                main.duration = 0.3f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                main.startColor = new ParticleSystem.MinMaxGradient(_impactColor, Color.white);
                main.maxParticles = 24;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = burst.emission;
                emission.enabled = false;
                var shape = burst.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f;
                var renderer = impact.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = ProceduralMeshLibrary.Box;
                renderer.sharedMaterial = ProceduralMaterials.Glow(_impactColor);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                burst.Emit(16);
            }

            if (impact != null)
            {
                Destroy(impact, 1.2f);
            }

            // Ледяной осколок даёт отдельную вспышку инея: кольцо по земле и морозный свет
            // читаются с любой камеры, чего обычные искры не дают.
            if (_isShard)
            {
                FrostVfx.Burst(_to);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
