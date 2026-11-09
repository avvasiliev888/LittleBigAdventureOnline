using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Камера следит за игроком с экспоненциальным сглаживанием (M6).
    /// Сглаживание камеры не влияет на авторитетную позицию — только на картинку.
    /// </summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        /// <summary>Цель слежения (трансформ игрока).</summary>
        public Transform Target;

        /// <summary>Смещение камеры относительно цели.</summary>
        public Vector3 Offset = new Vector3(0f, 10f, -8f);

        /// <summary>Скорость сглаживания; больше — жёстче следование.</summary>
        public float Smoothing = 8f;

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            var desired = Target.position + Offset;
            var factor = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, factor);

            var lookDirection = Target.position - transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }
        }
    }
}
