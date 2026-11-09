using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// M10: одна batchmode-точка входа — создать URP-ассеты, собрать арену, провалидировать профили.
    /// Порядок важен: материалы арены используют шейдеры, а валидация — уже созданные ассеты.
    /// </summary>
    public static class AlbionBatch
    {
        /// <summary>Полная генерация контента M10 (вызывается из -executeMethod).</summary>
        public static void All()
        {
            AlbionUrpSetup.SetupUrp();
            AlbionWorldBuilder.BuildArenaScene();
            AlbionUrpSetup.ValidateUrp();
            Debug.Log("M10: генерация контента завершена.");
        }
    }
}
