using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Ледяная магия: подсветка руки перед броском и вспышка инея в точке попадания.
    ///
    /// Эффект чисто визуальный и процедурный — в проекте нет ни одного VFX-ассета,
    /// поэтому всё строится из примитивов и <see cref="ProceduralMaterials"/>.
    /// Урон считает сервер в момент жеста (hitscan), ледяной осколок только объясняет
    /// игроку, откуда и чем прилетело.
    /// </summary>
    public static class FrostVfx
    {
        private static readonly Color IceCore = new Color(0.62f, 0.90f, 1f);
        private static readonly Color IceDeep = new Color(0.24f, 0.55f, 0.95f);
        private static readonly Color IceRim = new Color(0.86f, 0.97f, 1f);

        /// <summary>
        /// Морозный ореол вокруг ладони: короткая вспышка перед вылетом осколка. Читается
        /// как «маг собрал лёд в руке» и связывает жест с появлением снаряда.
        /// </summary>
        public static void ChargeAt(Vector3 hand)
        {
            if (!Application.isPlaying || !IsFinite(hand))
            {
                return;
            }

            var root = new GameObject("Frost charge");
            root.transform.position = hand;

            var core = new GameObject("Charge core").transform;
            core.SetParent(root.transform, false);
            core.localScale = Vector3.one * 0.30f;
            core.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Sphere;
            AddRenderer(core.gameObject, ProceduralMaterials.Glow(IceCore));

            var halo = new GameObject("Charge halo").transform;
            halo.SetParent(root.transform, false);
            halo.localScale = Vector3.one * 0.52f;
            halo.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Sphere;
            AddRenderer(halo.gameObject, ProceduralMaterials.Glow(new Color(IceDeep.r, IceDeep.g, IceDeep.b, 0.55f)));

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = IceCore;
            light.range = 4.5f;
            light.intensity = 2.4f;

            var pulse = root.AddComponent<FrostPulse>();
            pulse.Configure(0.42f, 0.30f, 0.55f);
            Object.Destroy(root, 0.9f);
        }

        /// <summary>
        /// Вспышка инея в точке попадания: расходящееся кольцо, облако осколков и
        /// короткий холодный свет. Кольцо разлетается по земле — так удар читается
        /// даже когда снаряд прилетел сбоку и камера смотрит сверху.
        /// </summary>
        public static void Burst(Vector3 point)
        {
            if (!Application.isPlaying || !IsFinite(point))
            {
                return;
            }

            var root = new GameObject("Frost burst");
            root.transform.position = point;

            var ring = new GameObject("Frost ring").transform;
            ring.SetParent(root.transform, false);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            ring.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Ring;
            AddRenderer(ring.gameObject, ProceduralMaterials.Glow(IceRim));

            var expand = ring.gameObject.AddComponent<FrostRing>();
            expand.Configure(1.5f, 0.34f);

            var shards = root.AddComponent<ParticleSystem>();
            var main = shards.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 6.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(IceRim, IceDeep);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.55f;

            var emission = shards.emission;
            emission.enabled = false;

            var shape = shards.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ProceduralMeshLibrary.Cone;
            renderer.sharedMaterial = ProceduralMaterials.Glow(IceRim);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = IceCore;
            light.range = 6f;
            light.intensity = 3.2f;

            var fade = root.AddComponent<FrostPulse>();
            fade.Configure(1f, 0f, 0.4f);

            shards.Emit(26);
            Object.Destroy(root, 1.4f);
        }

        private static void AddRenderer(GameObject target, Material material)
        {
            var renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }

    /// <summary>Затухание света и масштаба за фиксированное время: вспышка не висит вечно.</summary>
    public sealed class FrostPulse : MonoBehaviour
    {
        private float _duration = 0.4f;
        private float _from = 1f;
        private float _to = 0f;
        private float _age;
        private Light _light;
        private Vector3 _baseScale;

        public void Configure(float duration, float from, float to)
        {
            _duration = Mathf.Max(0.05f, duration);
            _from = from;
            _to = to;
            _light = GetComponent<Light>();
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            var t = Mathf.Clamp01(_age / _duration);
            var value = Mathf.Lerp(_from, _to, t);
            if (_light != null)
            {
                _light.intensity = value * 3f;
            }

            // Ореол раздувается, пока не погаснет: лёд «схватывается» и рассыпается.
            transform.localScale = _baseScale * Mathf.Lerp(1f, 1.6f, t);
        }
    }

    /// <summary>Расходящееся кольцо инея: единственный элемент, который видно с любой камеры.</summary>
    public sealed class FrostRing : MonoBehaviour
    {
        private float _radius = 1.5f;
        private float _duration = 0.35f;
        private float _age;

        public void Configure(float radius, float duration)
        {
            _radius = Mathf.Max(0.1f, radius);
            _duration = Mathf.Max(0.05f, duration);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            var t = Mathf.Clamp01(_age / _duration);
            var scale = Mathf.Lerp(0.12f, _radius, t);
            transform.localScale = new Vector3(scale, scale, scale);

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                // Кольцо истончается к цвету неба: в конце от него не остаётся яркого пятна.
                // Материал клонируется (renderer.material), иначе общий кэш ProceduralMaterials
                // перекрасил бы все ледяные эффекты на экране разом.
                renderer.material.color = Color.Lerp(
                    new Color(0.86f, 0.97f, 1f), new Color(0.2f, 0.5f, 0.95f), t);
            }
        }
    }
}
