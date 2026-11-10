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
        /// <summary>Regenerate then exercise the saved scene through the real GPU path.</summary>
        public static void RepairAndVerify()
        {
            All();
            AlbionRenderProbe.Run();
        }

        /// <summary>Validation against reimported disk assets followed by actual GPU evidence.</summary>
        public static void VerifyRendering()
        {
            AlbionUrpSetup.ValidateUrp();
            AlbionCharacterSetup.Validate();
            AlbionRenderProbe.Run();
        }

        /// <summary>Полная генерация контента M10 (вызывается из -executeMethod).</summary>
        public static void All()
        {
            AlbionUrpSetup.SetupUrp();
            AlbionWorldBuilder.BuildArenaScene();
            AlbionCharacterSetup.Configure();
            AlbionUrpSetup.ValidateUrp();
            Debug.Log("M10: генерация контента завершена.");
        }
    }
}
