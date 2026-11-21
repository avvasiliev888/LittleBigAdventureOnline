using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Материалы ледяной магии. Отдельно от <see cref="ProceduralMaterials"/>, чтобы не
    /// смешивать непрозрачный toon-стиль персонажей с прозрачным льдом: прошлая версия
    /// красила лёд тем же <c>Glow</c>, чей <c>_EmissionColor = color * 2.4</c> при пороге
    /// блума 0,9 и ACES-тонмаппинге давал белое пятно вместо голубого льда.
    /// </summary>
    public static class IceMaterials
    {
        private const string ShaderName = "Albion/Ice Crystal";

        private static Material _shard;
        private static Material _burst;
        private static Material _spike;
        private static Material _mote;
        private static Material _ground;

        /// <summary>Материал самого осколка: глубокий центр, светящиеся грани.</summary>
        public static Material Shard => _shard != null ? _shard : _shard = Create("Ice shard", material =>
        {
            SetColor(material, "_DeepColor", new Color(0.08f, 0.28f, 0.68f, 1f));
            SetColor(material, "_SurfaceColor", new Color(0.52f, 0.84f, 1.00f, 1f));
            SetColor(material, "_RimColor", new Color(0.70f, 0.93f, 1.00f, 1f));
            SetFloat(material, "_Opacity", 0.62f);
            SetFloat(material, "_RefractionStrength", 0.18f);
            SetFloat(material, "_NoiseScale", 8f);
            SetFloat(material, "_NoiseStrength", 0.03f);
            SetFloat(material, "_RimStrength", 0.42f);
            SetFloat(material, "_Shimmer", 0.55f);
            SetFloat(material, "_Glow", 1.0f);
        });

        /// <summary>Кристаллы взрыва: плотнее и ярче, их много и они мелкие.</summary>
        public static Material Burst => _burst != null ? _burst : _burst = Create("Ice burst", material =>
        {
            SetColor(material, "_DeepColor", new Color(0.10f, 0.34f, 0.76f, 1f));
            SetColor(material, "_SurfaceColor", new Color(0.62f, 0.90f, 1.00f, 1f));
            SetColor(material, "_RimColor", new Color(0.82f, 0.97f, 1.00f, 1f));
            SetFloat(material, "_Opacity", 0.72f);
            SetFloat(material, "_RefractionStrength", 0.12f);
            SetFloat(material, "_NoiseScale", 14f);
            SetFloat(material, "_RimStrength", 0.55f);
            SetFloat(material, "_Shimmer", 0.9f);
            SetFloat(material, "_Glow", 1.05f);
        });

        /// <summary>Шипы из земли: полупрозрачные, с сильным преломлением у основания.</summary>
        public static Material Spike => _spike != null ? _spike : _spike = Create("Ice spike", material =>
        {
            SetColor(material, "_DeepColor", new Color(0.06f, 0.22f, 0.58f, 1f));
            SetColor(material, "_SurfaceColor", new Color(0.42f, 0.74f, 0.96f, 1f));
            SetColor(material, "_RimColor", new Color(0.60f, 0.88f, 1.00f, 1f));
            SetFloat(material, "_Opacity", 0.55f);
            SetFloat(material, "_RefractionStrength", 0.22f);
            SetFloat(material, "_NoiseScale", 6f);
            SetFloat(material, "_RimStrength", 0.38f);
            SetFloat(material, "_Shimmer", 0.35f);
            SetFloat(material, "_Glow", 0.95f);
        });

        /// <summary>Мелкие искры-осколки: без преломления, они слишком малы для него.</summary>
        public static Material Mote => _mote != null ? _mote : _mote = Create("Ice mote", material =>
        {
            SetColor(material, "_DeepColor", new Color(0.18f, 0.46f, 0.85f, 1f));
            SetColor(material, "_SurfaceColor", new Color(0.78f, 0.95f, 1.00f, 1f));
            SetColor(material, "_RimColor", new Color(0.88f, 0.98f, 1.00f, 1f));
            SetFloat(material, "_Opacity", 0.85f);
            SetFloat(material, "_RefractionStrength", 0.05f);
            SetFloat(material, "_NoiseScale", 18f);
            SetFloat(material, "_RimStrength", 0.60f);
            SetFloat(material, "_Shimmer", 1.2f);
            SetFloat(material, "_Glow", 1.1f);
        });

        /// <summary>Морозное пятно на земле: плоский диск, который тает до полной прозрачности.</summary>
        public static Material Ground => _ground != null ? _ground : _ground = Create("Ice ground", material =>
        {
            SetColor(material, "_DeepColor", new Color(0.20f, 0.44f, 0.78f, 1f));
            SetColor(material, "_SurfaceColor", new Color(0.66f, 0.88f, 1.00f, 1f));
            SetColor(material, "_RimColor", new Color(0.80f, 0.96f, 1.00f, 1f));
            SetFloat(material, "_Opacity", 0.30f);
            SetFloat(material, "_RefractionStrength", 0.26f);
            SetFloat(material, "_NoiseScale", 4f);
            SetFloat(material, "_RimStrength", 0.30f);
            SetFloat(material, "_Shimmer", 0.25f);
            SetFloat(material, "_Glow", 0.9f);
        });

        private static Material Create(string name, System.Action<Material> configure)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // Внятная ошибка вместо молчаливого розового: имя шейдера — единственное,
                // что связывает C# с .shader, и опечатка в нём ранее стоила захода.
                throw new System.InvalidOperationException("Ice shader missing: " + ShaderName);
            }

            var material = new Material(shader) { name = name };
            configure(material);
            return material;
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }
    }
}