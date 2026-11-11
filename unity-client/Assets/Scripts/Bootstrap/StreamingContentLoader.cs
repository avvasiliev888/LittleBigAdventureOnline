using System;
using System.Collections;
using System.IO;
using Client.Network;
using Shared;
using UnityEngine.Networking;

namespace Albion.Client
{
    /// <summary>Читает конфиги через UnityWebRequest, включая jar:file внутри Android APK.</summary>
    public static class StreamingContentLoader
    {
        /// <summary>
        /// Асинхронно читает все девять JSON без дискового кеша и передаёт их общему загрузчику.
        /// Ошибка любого файла прерывает загрузку; дефолтные игровые конфиги не подставляются.
        /// </summary>
        /// <param name="directory">Локальный каталог или URI каталога Config в StreamingAssets.</param>
        /// <param name="loaded">Вызывается после загрузки и валидации всех файлов.</param>
        /// <param name="failed">Вызывается с путём и причиной ошибки.</param>
        public static IEnumerator Load(string directory, Action<ClientContent> loaded, Action<string> failed)
        {
            var names = new[]
            {
                ConfigLoader.ActorsFileName, ConfigLoader.AbilitiesFileName,
                ConfigLoader.ZonesFileName, ConfigLoader.ItemsFileName,
                ConfigLoader.SkillTreeFileName, ConfigLoader.ResourcesFileName,
                ConfigLoader.RecipesFileName, ConfigLoader.TradersFileName,
                ConfigLoader.MobsFileName,
            };
            // Path.Combine/File.Exists cannot access Android's jar:file://...!/assets paths.
            var root = directory.Contains("://")
                ? directory.TrimEnd('/')
                : new Uri(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
            var json = new string[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var url = root + "/" + names[i];
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = 15;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        failed("Не удалось загрузить " + names[i] + ": " + request.error + " (" + url + ")");
                        yield break;
                    }

                    // Match File.ReadAllText's BOM handling so the server/client hashes agree.
                    json[i] = request.downloadHandler.text.TrimStart('\uFEFF');
                }
            }

            ClientContent content;
            try
            {
                content = ClientContent.LoadFromJson(json[0], json[1], json[2], json[3], directory,
                    json[4], json[5], json[6], json[7], json[8]);
            }
            catch (Exception exception)
            {
                failed(exception.Message);
                yield break;
            }

            loaded(content);
        }
    }
}
