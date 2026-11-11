using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Cheap, allocation-light VFX for attack swings, gathering and hit feedback.
    /// The project deliberately avoids a large VFX graph: these are shared low-poly meshes and
    /// a real ParticleSystem, short-lived, with no physics or networking involvement.
    /// </summary>
    public static class CombatVfx
    {
        private static Material _particleMaterial;

        /// <summary>Shows a short crescent slash in front of a character.</summary>
        public static void AttackFlash(Vector3 position, Quaternion rotation, Color color)
        {
            // EditMode validation must not leak transient objects into the edited scene.
            if (!Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("Attack slash");
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * 0.85f;
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.SlashArc;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralMaterials.Glow(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var timed = go.AddComponent<TimedVfx>();
            timed.Lifetime = 0.28f;
            timed.StartScale = 0.85f;
            timed.EndScale = 1.25f;
            timed.SpinDegreesPerSecond = 120f;
        }

        /// <summary>Shows the shrinking target circle at a clicked ground point.</summary>
        public static void ClickMarker(Vector3 position)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("Click marker");
            go.transform.position = position + (Vector3.up * 0.025f);
            go.transform.localScale = Vector3.one * 1.2f;
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Ring;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralMaterials.Glow(new Color(0.45f, 0.95f, 0.7f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var timed = go.AddComponent<TimedVfx>();
            timed.Lifetime = 0.75f;
            timed.StartScale = 1.2f;
            timed.EndScale = 0.02f;
        }

        /// <summary>Bursts leaves/sparks when a resource is gathered.</summary>
        public static void GatherBurst(Vector3 position, Color color)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("Gather burst");
            go.transform.position = position + (Vector3.up * 0.45f);
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 0.45f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.3f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.35f));
            main.gravityModifier = 0.7f;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ProceduralMeshLibrary.Box;
            renderer.sharedMaterial = ParticleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            particles.Emit(22);
            UnityEngine.Object.Destroy(go, 1.6f);
        }

        /// <summary>Soft golden flash used by the smoke scene so the effect is visible in a still.</summary>
        public static void Showcase(Vector3 position)
        {
            AttackFlash(position + new Vector3(0f, 1.25f, 0.85f), Quaternion.identity,
                new Color(1f, 0.82f, 0.36f));
            GatherBurst(position, new Color(0.6f, 0.9f, 0.45f));
        }

        private static Material ParticleMaterial
        {
            get
            {
                if (_particleMaterial != null)
                {
                    return _particleMaterial;
                }

                _particleMaterial = ProceduralMaterials.Lit(Color.white);
                _particleMaterial.enableInstancing = true;
                return _particleMaterial;
            }
        }
    }

    /// <summary>Drives a short-lived mesh flash without a coroutine or per-frame lambda.</summary>
    public sealed class TimedVfx : MonoBehaviour
    {
        public float Lifetime = 0.3f;
        public float StartScale = 1f;
        public float EndScale = 1.2f;
        public float SpinDegreesPerSecond;

        private float _age;
        private Quaternion _baseRotation;

        private void Awake()
        {
            _baseRotation = transform.rotation;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            var t = Mathf.Clamp01(_age / Mathf.Max(0.01f, Lifetime));
            var scale = Mathf.Lerp(StartScale, EndScale, t);
            transform.localScale = Vector3.one * scale;
            if (Mathf.Abs(SpinDegreesPerSecond) > 0.01f)
            {
                transform.rotation = _baseRotation * Quaternion.Euler(0f, 0f, SpinDegreesPerSecond * _age);
            }

            if (_age >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
