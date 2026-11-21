using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Посох мага. Существует потому, что <see cref="UnityView.ApplyWeapon"/> начинается с
    /// <c>if (state.IsMob ...) return;</c> — мобам оружие не выдаётся вообще, и маг делал жест
    /// пустой рукой. Строится процедурно: готового меша посоха в проекте нет.
    /// </summary>
    public static class IceStaff
    {
        /// <summary>Создаёт посох и вешает его в правую руку.</summary>
        public static GameObject Attach(CharacterVisuals character)
        {
            if (character == null)
            {
                return null;
            }

            var socket = character.RightHandSocket;
            if (socket == null)
            {
                return null;
            }

            var root = new GameObject("Ice staff");
            root.transform.SetParent(socket, false);
            // Древко вдоль руки: рукоять в ладони, кристалл вверху и вперёд.
            root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            root.transform.localPosition = Vector3.zero;

            BuildHaft(root.transform);
            var gem = BuildGem(root.transform);

            // Свет на конце посоха: кристалл разгорается к моменту броска.
            var light = gem.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.58f, 0.85f, 1f);
            light.range = 3.4f;
            light.intensity = 0.55f;

            return root;
        }

        /// <summary>Древко: тёмное дерево с обмоткой и навершием-держателем.</summary>
        private static void BuildHaft(Transform parent)
        {
            var shaft = new GameObject("Haft").transform;
            shaft.SetParent(parent, false);
            shaft.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            shaft.localScale = new Vector3(0.045f, 0.46f, 0.045f);
            shaft.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cylinder;
            shaft.gameObject.AddComponent<MeshRenderer>().sharedMaterial =
                ProceduralMaterials.Lit(new Color(0.26f, 0.20f, 0.16f));

            // Обмотка рукояти: тёмный цилиндр чуть шире древка.
            var grip = new GameObject("Grip").transform;
            grip.SetParent(parent, false);
            grip.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            grip.localPosition = new Vector3(0f, -0.12f, 0f);
            grip.localScale = new Vector3(0.055f, 0.09f, 0.055f);
            grip.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cylinder;
            grip.gameObject.AddComponent<MeshRenderer>().sharedMaterial =
                ProceduralMaterials.Lit(new Color(0.16f, 0.13f, 0.12f));

            // Навершие-держатель кристалла: короткий конус вверх.
            var cap = new GameObject("Gem holder").transform;
            cap.SetParent(parent, false);
            cap.localPosition = new Vector3(0f, -0.46f, 0f);
            cap.localScale = new Vector3(0.075f, 0.10f, 0.075f);
            cap.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Cone;
            cap.gameObject.AddComponent<MeshRenderer>().sharedMaterial =
                ProceduralMaterials.Lit(new Color(0.32f, 0.34f, 0.38f));
        }

        /// <summary>Кристалл на конце: то же преломляющее стекло, что и у снаряда.</summary>
        private static Transform BuildGem(Transform parent)
        {
            var gem = new GameObject("Staff gem").transform;
            gem.SetParent(parent, false);
            gem.localPosition = new Vector3(0f, -0.62f, 0f);
            gem.localScale = Vector3.one * 0.16f;
            gem.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.IceCrystal;

            var renderer = gem.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = IceMaterials.Shard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var spin = gem.gameObject.AddComponent<StaffGem>();
            spin.Configure(38f);
            return gem;
        }
    }

    /// <summary>
    /// Медленное вращение и пульсация кристалла на посохе. Даже когда маг просто стоит,
    /// посох живёт: иначе он читается как палка с камешком.
    /// </summary>
    public sealed class StaffGem : MonoBehaviour
    {
        private float _speed;
        private float _baseIntensity;
        private float _phase;
        private Light _light;

        public void Configure(float speed)
        {
            _speed = speed;
            _light = GetComponent<Light>();
            if (_light != null)
            {
                _baseIntensity = _light.intensity;
            }
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, _speed * Time.deltaTime, Space.Self);

            if (_light == null)
            {
                return;
            }

            _phase += Time.deltaTime * 1.7f;
            _light.intensity = _baseIntensity * (0.82f + (Mathf.Sin(_phase) * 0.18f));
        }
    }
}