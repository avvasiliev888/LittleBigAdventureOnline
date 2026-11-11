using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>Deterministic cosmetic grass, submitted in spatially culled GPU-instanced cells.</summary>
    public sealed class InstancedMeadow : MonoBehaviour
    {
        public Mesh Blade;
        public Material Material;
        public Vector4 BoundsXZ;
        public int Seed;
        public Vector2 Walls;
        private readonly List<Matrix4x4[]> _cells = new List<Matrix4x4[]>();
        private readonly List<Vector3> _centres = new List<Vector3>();
        private Camera _camera;

        private void OnEnable()
        {
            _cells.Clear(); _centres.Clear();
            var random = new System.Random(Seed);
            for (var x = BoundsXZ.x; x < BoundsXZ.z; x += 8f)
            {
                for (var z = BoundsXZ.y; z < BoundsXZ.w; z += 8f)
                {
                    var matrices = new List<Matrix4x4>();
                    for (var i = 0; i < 130; i++)
                    {
                        var px = Mathf.Lerp(x, Mathf.Min(x + 8f, BoundsXZ.z), (float)random.NextDouble());
                        var pz = Mathf.Lerp(z, Mathf.Min(z + 8f, BoundsXZ.w), (float)random.NextDouble());
                        if (Mathf.Abs(pz) < 3.1f) continue;
                        if (Walls.x != 0f && Mathf.Abs(px - Walls.x) < 1.4f) continue;
                        if (Walls.y != 0f && Mathf.Abs(px - Walls.y) < 1.4f) continue;
                        var scale = 0.55f + (float)random.NextDouble() * 0.7f;
                        matrices.Add(Matrix4x4.TRS(new Vector3(px, 0.025f, pz),
                            Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(scale, scale, scale)));
                    }
                    _cells.Add(matrices.ToArray());
                    _centres.Add(new Vector3(x + 4f, 0f, z + 4f));
                }
            }
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            DrawFor(_camera);
        }

        /// <summary>Submit visible meadow cells for a specific camera, including explicit Editor probes.</summary>
        public void DrawFor(Camera camera)
        {
            if (camera == null || Blade == null || Material == null || !SystemInfo.supportsInstancing) return;
            var mobile = Application.isMobilePlatform;
            var distance = mobile ? 23f : 40f;
            for (var i = 0; i < _cells.Count; i++)
            {
                if ((_centres[i] - camera.transform.position).sqrMagnitude > distance * distance) continue;
                var instances = _cells[i];
                var count = mobile ? instances.Length / 2 : instances.Length;
                if (count == 0) continue;
                Graphics.DrawMeshInstanced(Blade, 0, Material, instances, count, null,
                    ShadowCastingMode.Off, true, gameObject.layer, camera, LightProbeUsage.Off);
            }
        }
    }
}
