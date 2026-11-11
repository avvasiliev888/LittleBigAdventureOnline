using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Экспортирует статические препятствия из уже собранной сцены Arena в общий JSON-конфиг.
    /// Сервер и клиентское предсказание читают этот файл; физика Unity в движении не участвует.
    /// </summary>
    public static class WorldColliderExporter
    {
        private const string ArenaScene = "Assets/Scenes/Arena.unity";
        private const string RepoConfig = "../../config/world-colliders.json";

        private sealed class Entry
        {
            public string Kind;
            public string Id;
            public float X;
            public float Z;
            public float Radius;
        }

        /// <summary>Пункт меню для ручного экспорта после пересборки мира.</summary>
        [MenuItem("Albion/World/Export World Colliders")]
        public static void ExportArenaColliders()
        {
            EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            ExportFromOpenScene();
        }

        /// <summary>
        /// Экспортирует препятствия из уже открытой сцены. Вызывается генератором после SaveScene,
        /// чтобы JSON не мог устареть относительно только что созданного мира.
        /// </summary>
        /// <returns>Количество записанных препятствий.</returns>
        public static int ExportFromOpenScene()
        {
            var ground = GameObject.Find("Ground");
            if (ground == null)
            {
                throw new InvalidOperationException("В сцене Arena нет корневого объекта Ground.");
            }

            var entries = new List<Entry>();
            var treeIndex = 0;
            var rockIndex = 0;
            var columnIndex = 0;
            foreach (var transform in ground.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == "Procedural tree" || transform.name.StartsWith("Procedural tree ", StringComparison.Ordinal))
                {
                    var trunk = transform.Find("Crown LOD0/Tapered trunk");
                    var thickness = trunk == null ? 1f : trunk.localScale.x;
                    entries.Add(new Entry
                    {
                        Kind = "tree",
                        Id = "tree-" + (++treeIndex).ToString("0000", CultureInfo.InvariantCulture),
                        X = transform.position.x,
                        Z = transform.position.z,
                        Radius = Mathf.Clamp(thickness * 0.45f, 0.3f, 0.65f),
                    });
                }
                else if (transform.name == "Faceted rock")
                {
                    var rock = transform.Find("Rock LOD0");
                    var scale = rock == null ? Vector3.one : rock.localScale;
                    entries.Add(new Entry
                    {
                        Kind = "rock",
                        Id = "rock-" + (++rockIndex).ToString("0000", CultureInfo.InvariantCulture),
                        X = transform.position.x,
                        Z = transform.position.z,
                        Radius = Mathf.Clamp(Mathf.Max(scale.x, scale.z) * 0.7f, 0.45f, 1.8f),
                    });
                }
                else if (transform.name == "Pass pillar" || transform.name == "Ancient waystone")
                {
                    var scale = transform.localScale;
                    entries.Add(new Entry
                    {
                        Kind = "column",
                        Id = "column-" + (++columnIndex).ToString("0000", CultureInfo.InvariantCulture),
                        X = transform.position.x,
                        Z = transform.position.z,
                        Radius = Mathf.Clamp(Mathf.Max(scale.x, scale.z) * 0.6f, 0.55f, 1.6f),
                    });
                }
            }

            entries.Sort((left, right) =>
            {
                var kind = string.CompareOrdinal(left.Kind, right.Kind);
                if (kind != 0) return kind;
                var x = left.X.CompareTo(right.X);
                return x != 0 ? x : left.Z.CompareTo(right.Z);
            });

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, RepoConfig));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, BuildJson(entries), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("[world-colliders] exported " + entries.Count + " obstacles to " + path);
            return entries.Count;
        }

        private static string BuildJson(List<Entry> entries)
        {
            var builder = new StringBuilder();
            builder.Append("[\n");
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                builder.Append("  { \"id\": \"").Append(entry.Id)
                    .Append("\", \"kind\": \"").Append(entry.Kind)
                    .Append("\", \"x\": ").Append(entry.X.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(", \"z\": ").Append(entry.Z.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(", \"radius\": ").Append(entry.Radius.ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(" }");
                if (i < entries.Count - 1) builder.Append(',');
                builder.Append('\n');
            }

            builder.Append("]\n");
            return builder.ToString();
        }
    }
}
