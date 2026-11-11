#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using Albion.Client;
using Client.Network;
using Shared;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>Регрессия URI-загрузки: девять файлов, BOM, пробелы, битый/отсутствующий JSON.</summary>
    internal static class StreamingContentSmokeChecks
    {
        public static IEnumerator Run(Action<string> failed, Action passed)
        {
            var source = Path.Combine(Application.streamingAssetsPath, "Config");
            var temp = Path.Combine(Path.GetTempPath(), "albion content smoke " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var name in new[]
                {
                    ConfigLoader.ActorsFileName, ConfigLoader.AbilitiesFileName,
                    ConfigLoader.ZonesFileName, ConfigLoader.ItemsFileName,
                    ConfigLoader.SkillTreeFileName, ConfigLoader.ResourcesFileName,
                    ConfigLoader.RecipesFileName, ConfigLoader.TradersFileName,
                    ConfigLoader.MobsFileName,
                })
                {
                    File.Copy(Path.Combine(source, name), Path.Combine(temp, name));
                }

                var actors = Path.Combine(temp, ConfigLoader.ActorsFileName);
                File.WriteAllText(actors, File.ReadAllText(actors), new UTF8Encoding(true));
                var uri = new Uri(temp + Path.DirectorySeparatorChar).AbsoluteUri;
                ClientContent content = null;
                string error = null;
                yield return StreamingContentLoader.Load(uri, value => content = value, value => error = value);
                if (content == null || error != null || content.Hash != ConfigLoader.ComputeHash(source))
                {
                    failed("URI/BOM/all-nine-file hash mismatch: " + error);
                    yield break;
                }

                File.WriteAllText(actors, "{not valid json");
                content = null;
                error = null;
                yield return StreamingContentLoader.Load(uri, value => content = value, value => error = value);
                if (content != null || string.IsNullOrEmpty(error))
                {
                    failed("Malformed JSON did not fail fast");
                    yield break;
                }

                File.Delete(actors);
                error = null;
                yield return StreamingContentLoader.Load(uri, value => content = value, value => error = value);
                if (content != null || error == null || !error.Contains(ConfigLoader.ActorsFileName))
                {
                    failed("Missing JSON did not report filename");
                    yield break;
                }

                Debug.Log("[agent-smoke] content URI/BOM/hash/malformed/missing checks PASS");
                passed();
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif
