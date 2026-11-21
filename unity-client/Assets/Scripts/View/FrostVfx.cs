using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Эффект, умеющий шагать по времени явным шагом. Нужен для раскадровки без Play Mode:
    /// вне игры Unity не вызывает <c>Update</c>, а проверить эффект до сборки клиента надо.
    /// </summary>
    public interface IceAnimator
    {
        void Advance(float deltaTime);
    }

    /// <summary>
    /// Ледяная магия мага. Отличие от прошлой версии принципиальное: раньше всё строилось
    /// из гладкой сферы и тора, покрашенных в непрозрачный белый glow. Теперь это гранёные
    /// кристаллы с преломляющим шейдером, шипы из земли и тающее пятно.
    ///
    /// Урон считает сервер в момент жеста (hitscan). Весь эффект — только объяснение
    /// игроку, откуда и чем прилетело.
    /// </summary>
    public static class FrostVfx
    {
        /// <summary>Сколько кристаллов разлетается при попадании.</summary>
        private const int BurstShardCount = 16;

        /// <summary>Сколько шипов вырастает из земли.</summary>
        private const int SpikeCount = 7;

        /// <summary>
        /// Разрешить создание эффекта вне Play Mode. Включает только раскадровка
        /// <c>IceVfxStoryboard</c>: в обычном редактировании сцены лёд не нужен.
        /// </summary>
        public static bool AllowEditModeSpawn;

        /// <summary>
        /// Заряд в руке. Вместо шара — спираль мелких осколков, сходящихся к ладони, и
        /// растущий кристалл: видно, что маг СОБИРАЕТ лёд, а не просто светит лампочкой.
        /// </summary>
        public static void ChargeAt(Vector3 hand, float duration)
        {
            if ((!Application.isPlaying && !AllowEditModeSpawn) || !IsFinite(hand))
            {
                return;
            }

            var root = new GameObject("Ice charge");
            root.transform.position = hand;

            // Растущий кристалл в ладони.
            var core = new GameObject("Charge crystal").transform;
            core.SetParent(root.transform, false);
            core.localRotation = Quaternion.Euler(0f, 0f, 15f);
            core.localScale = Vector3.one * 0.18f;
            core.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.IceCrystal;
            AddRenderer(core.gameObject, IceMaterials.Shard);

            var grow = core.gameObject.AddComponent<IceGrow>();
            grow.Configure(0.10f, 0.30f, duration, spin: 220f);

            // Спираль осколков, стягивающаяся к центру: главный признак «магия собирается».
            for (var i = 0; i < 9; i++)
            {
                var angle = (i / 9f) * Mathf.PI * 2f;
                var offset = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f)
                    * new Vector3(0.28f, Mathf.Sin(angle * 2f) * 0.10f, 0f);

                var mote = new GameObject("Converging shard").transform;
                mote.SetParent(root.transform, false);
                mote.localScale = Vector3.one * 0.055f;
                mote.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    ProceduralMeshLibrary.CrystalVariant(i + 1);
                AddRenderer(mote.gameObject, IceMaterials.Mote);

                var pull = mote.gameObject.AddComponent<IceConverge>();
                pull.Configure(offset, duration * 0.85f, i * 0.05f);
            }

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.55f, 0.82f, 1f);
            light.range = 4.2f;
            light.intensity = 0f;

            var pulse = root.AddComponent<IcePulse>();
            pulse.Configure(duration, 0f, 3.4f, 1f, 1.5f);

            Object.Destroy(root, duration + 0.6f);
        }

        /// <summary>
        /// Попадание: разлёт кристаллов с физикой, шипы из земли, тающее пятно и морозный свет.
        /// Разлёт с гравитацией и вращением — это то, что отличает удар от «точечек».
        /// </summary>
        public static void Burst(Vector3 point)
        {
            if ((!Application.isPlaying && !AllowEditModeSpawn) || !IsFinite(point))
            {
                return;
            }

            var root = new GameObject("Ice burst");
            root.transform.position = point;

            // Шипы и пятно живут на земле, а попадание приходит на высоте груди: у корня
            // эффекта они систематически висели в воздухе. Опускаем их отдельным якорем.
            var groundAnchor = new GameObject("Ground anchor").transform;
            groundAnchor.SetParent(root.transform, false);
            groundAnchor.localPosition = new Vector3(0f, -point.y + 0.02f, 0f);

            SpawnShards(root.transform, point);
            SpawnSpikes(groundAnchor);
            SpawnGround(groundAnchor);
            SpawnMist(root.transform);
            SpawnBurstLight(root.transform);

            Object.Destroy(root, 4.5f);
        }

        /// <summary>Разлёт: каждый осколок летит своей скоростью, вращается и падает.</summary>
        private static void SpawnShards(Transform parent, Vector3 origin)
        {
            for (var i = 0; i < BurstShardCount; i++)
            {
                var angle = (i / (float)BurstShardCount) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                var rise = Random.Range(0.35f, 1.0f);
                var speed = Random.Range(3.2f, 8.0f);

                var direction = new Vector3(Mathf.Cos(angle), rise, Mathf.Sin(angle)).normalized;

                var shard = new GameObject("Burst shard").transform;
                shard.SetParent(parent, false);
                shard.position = origin + (direction * 0.12f);
                shard.localScale = Vector3.one * Random.Range(0.05f, 0.15f);
                shard.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    ProceduralMeshLibrary.CrystalVariant(i * 3 + 1);
                AddRenderer(shard.gameObject, IceMaterials.Burst);

                var fly = shard.gameObject.AddComponent<IceShardFly>();
                fly.Configure(direction * speed, Random.Range(-420f, 420f), 1.1f);
            }
        }

        /// <summary>
        /// Шипы из земли: классический приём ледяной магии. Вырастают из земли вокруг точки
        /// удара, наклонены наружу, растут с перекрытием по времени.
        /// </summary>
        private static void SpawnSpikes(Transform parent)
        {
            for (var i = 0; i < SpikeCount; i++)
            {
                var angle = (i / (float)SpikeCount) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                var radius = Random.Range(1.0f, 2.2f);
                var height = Random.Range(0.7f, 1.5f);

                var spike = new GameObject("Ice spike").transform;
                spike.SetParent(parent, false);
                spike.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                // Наклон наружу: шипы не частокол, а раскрытая корона.
                spike.localRotation = Quaternion.Euler(
                    Random.Range(-22f, -8f),
                    angle * Mathf.Rad2Deg,
                    Random.Range(-14f, 14f));
                spike.localScale = new Vector3(
                    Random.Range(0.28f, 0.46f),
                    0.01f,
                    Random.Range(0.28f, 0.46f));
                spike.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.IceSpike;
                AddRenderer(spike.gameObject, IceMaterials.Spike);

                var rise = spike.gameObject.AddComponent<IceSpikeRise>();
                rise.Configure(height, 0.16f + (i * 0.025f));
            }
        }

        /// <summary>Морозное пятно на земле: тает постепенно, а не исчезает щелчком.</summary>
        private static void SpawnGround(Transform parent)
        {
            var ground = new GameObject("Frost ground").transform;
            ground.SetParent(parent, false);
            ground.localPosition = new Vector3(0f, 0.03f, 0f);
            ground.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ground.localScale = new Vector3(1.6f, 1.6f, 1f);
            // Плоский диск, а не Ring: тор давал белые дуги-«полосочки» по краям пятна.
            ground.gameObject.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Disc;
            AddRenderer(ground.gameObject, IceMaterials.Ground);

            var melt = ground.gameObject.AddComponent<IceMelt>();
            melt.Configure(0.4f, 1f, 2.6f, 1.1f);
        }

        /// <summary>
        /// Морозный туман. Particles Lit вместо простых точек: у мягких частиц есть затухание
        /// на пересечении с геометрией, иначе дымка режется о пол прямой линией.
        /// </summary>
        /// <summary>
        /// Морозная дымка у земли. Раньше это было 34 билборда размером до 1,1 м с
        /// непрозрачной заливкой <c>Glow</c>: у шейдера нет UV-формы, поэтому каждая частица
        /// рисовалась КВАДРАТОМ, а вместе они накрывали персонажа белым пятном. Оставлен
        /// один слой мелких осколков — у них есть геометрия, они не закрывают модель.
        /// </summary>
        private static void SpawnMist(Transform parent)
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = (i / 10f) * Mathf.PI * 2f;
                var radius = Random.Range(0.3f, 1.1f);

                var mote = new GameObject("Frost mote").transform;
                mote.SetParent(parent, false);
                mote.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Random.Range(0.05f, 0.5f),
                    Mathf.Sin(angle) * radius);
                mote.localScale = Vector3.one * Random.Range(0.02f, 0.05f);
                mote.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    ProceduralMeshLibrary.CrystalVariant(i + 41);

                var renderer = mote.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = IceMaterials.Mote;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var fade = mote.gameObject.AddComponent<IceFadeMote>();
                fade.Configure(0.4f + (i * 0.04f), 0.9f);
            }
        }

        /// <summary>Холодный свет вспышки: короткий, но он освещает пол и лицо игрока.</summary>
        private static void SpawnBurstLight(Transform parent)
        {
            var lightObject = new GameObject("Frost light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.62f, 0.88f, 1f);
            light.range = 9f;
            light.intensity = 0f;

            var pulse = lightObject.AddComponent<IcePulse>();
            pulse.Configure(1.4f, 0f, 6.5f, 1f, 2.2f);
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

    /// <summary>Кристалл растёт из zero в заданный масштаб и вращается до конца жеста.</summary>
    public sealed class IceGrow : MonoBehaviour, IceAnimator
    {
        private float _from;
        private float _to;
        private float _duration;
        private float _spin;
        private float _age;

        public void Configure(float from, float to, float duration, float spin)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.05f, duration);
            _spin = spin;
            transform.localScale = Vector3.one * from;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            var t = Mathf.Clamp01(_age / _duration);
            // Быстрый рост в начале, замедление к концу: магия «схватывается».
            var eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.localScale = Vector3.one * Mathf.Lerp(_from, _to, eased);
            transform.Rotate(Vector3.up, _spin * deltaTime, Space.Self);
        }
    }

    /// <summary>Осколок летит по кругу внутрь, к ладони: спираль стягивается в точку.</summary>
    public sealed class IceConverge : MonoBehaviour, IceAnimator
    {
        private Vector3 _start;
        private float _duration;
        private float _delay;
        private float _age;

        public void Configure(Vector3 start, float duration, float delay)
        {
            _start = start;
            _duration = Mathf.Max(0.05f, duration);
            _delay = Mathf.Max(0f, delay);
            transform.localPosition = start;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            if (_age < _delay)
            {
                return;
            }

            var t = Mathf.Clamp01((_age - _delay) / _duration);
            var eased = t * t;

            // Спираль: угол растёт, радиус падает — осколок закручивается внутрь.
            var angle = eased * Mathf.PI * 2.4f;
            var radius = Vector3.Magnitude(_start) * (1f - eased);
            var flat = new Vector3(_start.x, _start.y, _start.z).normalized;
            var rotated = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f) * flat * radius;

            transform.localPosition = rotated;
            transform.localScale = Vector3.one * 0.055f * (1f - eased * 0.7f);
            transform.Rotate(Vector3.up, 320f * deltaTime, Space.Self);
        }
    }

    /// <summary>Осколок взрыва: скорость, гравитация, вращение, затухание.</summary>
    public sealed class IceShardFly : MonoBehaviour, IceAnimator
    {
        private Vector3 _velocity;
        private float _spin;
        private float _lifetime;
        private float _age;

        public void Configure(Vector3 velocity, float spin, float lifetime)
        {
            _velocity = velocity;
            _spin = spin;
            _lifetime = Mathf.Max(0.1f, lifetime);
            transform.rotation = Random.rotation;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            _velocity += Physics.gravity * deltaTime;
            transform.position += _velocity * deltaTime;
            transform.Rotate(Vector3.up, _spin * deltaTime, Space.Self);

            // Осколок уменьшается к концу жизни, вместо резкого исчезновения.
            var shrink = Mathf.Clamp01(1f - (_age / _lifetime));
            transform.localScale *= Mathf.Lerp(1f, 0.985f, shrink);
        }
    }

    /// <summary>Шип вырастает из земли, затем уходит обратно вниз.</summary>
    public sealed class IceSpikeRise : MonoBehaviour, IceAnimator
    {
        private float _height;
        private float _delay;
        private float _age;
        private float _baseX;
        private float _baseZ;

        public void Configure(float height, float delay)
        {
            _height = Mathf.Max(0.1f, height);
            _delay = Mathf.Max(0f, delay);
            _baseX = transform.localScale.x;
            _baseZ = transform.localScale.z;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            if (_age < _delay)
            {
                return;
            }

            var t = (_age - _delay) / 1.8f;
            // Рост за 0,25 с, удержание, затем медленное утопление.
            float scaleY;
            if (t < 0.14f)
            {
                scaleY = Mathf.SmoothStep(0f, _height, t / 0.14f);
            }
            else if (t < 0.65f)
            {
                scaleY = _height;
            }
            else
            {
                scaleY = Mathf.SmoothStep(_height, 0f, Mathf.Clamp01((t - 0.65f) / 0.35f));
            }

            transform.localScale = new Vector3(_baseX, Mathf.Max(0.01f, scaleY), _baseZ);
        }
    }

    /// <summary>Морозное пятно тает: растёт, затем гаснет в прозрачность.</summary>
    public sealed class IceMelt : MonoBehaviour, IceAnimator
    {
        private float _startScale;
        private float _endScale;
        private float _meltDuration;
        private float _holdDuration;
        private float _age;
        private Material _material;

        public void Configure(float startScale, float endScale, float meltDuration, float holdDuration)
        {
            _startScale = startScale;
            _endScale = endScale;
            _meltDuration = Mathf.Max(0.1f, meltDuration);
            _holdDuration = Mathf.Max(0f, holdDuration);
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Копия материала: иначе затухание одного пятна гасило бы все пятна на карте.
                _material = renderer.material;
            }
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;

            var grow = Mathf.Clamp01(_age / 0.35f);
            var scale = Mathf.Lerp(_startScale, _endScale, 1f - Mathf.Pow(1f - grow, 2f));
            transform.localScale = new Vector3(scale, scale, 1f);

            if (_material == null)
            {
                return;
            }

            // Таяние: держит яркость, затем плавно уходит в ноль.
            var meltStart = _holdDuration;
            var t = Mathf.Clamp01((_age - meltStart) / _meltDuration);
            var fade = 1f - t;
            _material.SetColor("_SurfaceColor", new Color(0.66f, 0.88f, 1f) * fade);
            _material.SetColor("_RimColor", new Color(0.80f, 0.96f, 1f) * fade);
            _material.SetFloat("_Opacity", 0.30f * fade);
        }
    }

    /// <summary>Мелкий осколок дымки: всплывает, вращается и сжимается в ноль.</summary>
    public sealed class IceFadeMote : MonoBehaviour, IceAnimator
    {
        private float _lifetime;
        private float _scale;
        private float _age;

        public void Configure(float lifetime, float scale)
        {
            _lifetime = Mathf.Max(0.1f, lifetime);
            _scale = scale;
            transform.localScale = Vector3.one * scale * 0.6f;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            var t = Mathf.Clamp01(_age / _lifetime);

            transform.localPosition += Vector3.up * (0.35f * deltaTime);
            transform.Rotate(Vector3.up, 200f * deltaTime, Space.Self);

            // Сжимается к нулю: осколок исчезает, а не пропадает щелчком.
            transform.localScale = Vector3.one * Mathf.Lerp(_scale * 0.6f, 0f, t * t);
        }
    }

    /// <summary>Плавный разгон и затухание света: вспышка не висит вечно.</summary>
    public sealed class IcePulse : MonoBehaviour, IceAnimator
    {
        private float _duration;
        private float _from;
        private float _to;
        private float _scaleFrom;
        private float _scaleTo;
        private float _age;
        private Light _light;
        private Vector3 _baseScale;

        public void Configure(float duration, float from, float to, float scaleFrom, float scaleTo)
        {
            _duration = Mathf.Max(0.05f, duration);
            _from = from;
            _to = to;
            _scaleFrom = scaleFrom;
            _scaleTo = scaleTo;
            _light = GetComponent<Light>();
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _age += deltaTime;
            var t = Mathf.Clamp01(_age / _duration);
            // Быстрый взлёт за первые 15%, затем спад.
            var value = t < 0.15f
                ? Mathf.Lerp(_from, _to, t / 0.15f)
                : Mathf.Lerp(_to, _from, (t - 0.15f) / 0.85f);

            if (_light != null)
            {
                _light.intensity = Mathf.Max(0f, value);
            }

            transform.localScale = _baseScale * Mathf.Lerp(_scaleFrom, _scaleTo, t);
        }
    }
}