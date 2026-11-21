using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Inventories every FBX in the project: rig type, animation stacks and their lengths.
    ///
    /// Why this exists: the clip inventory was being guessed from file names, and file names lie.
    /// "Ninjas.FBX" looks like a single model but may carry several stacks; "BaseMale@1HAttack"
    /// looks like a clip but the stacks are what Unity actually imports. Only Unity knows which
    /// stacks an FBX really contains, so the answer is measured once and written to a file that
    /// both the customer and the import pipeline can read.
    ///
    /// Output: docs/imports/clip-inventory.json plus a human summary in the log.
    ///
    /// Run:
    ///   Unity.exe -batchmode -quit -projectPath &lt;copy&gt; \
    ///     -executeMethod Albion.Editor.ClipInventory.Run -logFile &lt;log&gt;
    /// </summary>
    public static class ClipInventory
    {
        /// <summary>Written outside Assets on purpose: a JSON in Assets would be imported as an asset.</summary>
        private const string OutputPath = "F:/dsh-tmp/clip-inventory.json";

        [MenuItem("Albion/Clips/Inventory Every FBX")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("########## INVENTORY START ##########");

            try
            {
                var entries = new List<Entry>();

                foreach (var guid in AssetDatabase.FindAssets("t:Model"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)
                        || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var entry = Describe(path);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }

                entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

                var json = new StringBuilder();
                json.AppendLine("{");
                json.AppendLine("  \"models\": [");
                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    json.Append("    {\"path\": \"").Append(Escape(e.Path)).Append("\"");
                    json.Append(", \"rig\": \"").Append(e.Rig).Append("\"");
                    json.Append(", \"avatarValid\": ").Append(e.AvatarValid ? "true" : "false");
                    json.Append(", \"clipCount\": ").Append(e.Clips.Count);
                    json.Append(", \"clips\": [");
                    for (var c = 0; c < e.Clips.Count; c++)
                    {
                        if (c > 0)
                        {
                            json.Append(", ");
                        }

                        json.Append("{\"name\": \"").Append(Escape(e.Clips[c].Name)).Append("\"");
                        json.Append(", \"length\": ").Append(e.Clips[c].Length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        json.Append(", \"loop\": ").Append(e.Clips[c].Loop ? "true" : "false");
                        json.Append('}');
                    }

                    json.Append("]}");
                    json.AppendLine(i < entries.Count - 1 ? "," : string.Empty);
                }

                json.AppendLine("  ]");
                json.AppendLine("}");

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
                File.WriteAllText(OutputPath, json.ToString(), new UTF8Encoding(false));

                foreach (var entry in entries.Where(x => x.Clips.Count > 0))
                {
                    report.AppendLine($"{entry.Path} rig={entry.Rig} clips={entry.Clips.Count}");
                    foreach (var clip in entry.Clips)
                    {
                        report.AppendLine($"    {clip.Name} len={clip.Length:0.###} loop={(clip.Loop ? "Y" : "n")}");
                    }
                }

                report.AppendLine("models total: " + entries.Count);
                report.AppendLine("models with clips: " + entries.Count(x => x.Clips.Count > 0));
                report.AppendLine("written: " + OutputPath);
            }
            catch (Exception exception)
            {
                report.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                report.AppendLine("########## INVENTORY END ##########");
                Debug.Log(report.ToString());
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                return;
            }

            report.AppendLine("########## INVENTORY END ##########");
            Debug.Log(report.ToString());
        }

        private static Entry Describe(string path)
        {
            GameObject model = null;
            try
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            catch
            {
                return null;
            }

            if (model == null)
            {
                return null;
            }

            var entry = new Entry { Path = path };

            var animator = model.GetComponent<Animator>();
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null)
            {
                avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            }

            entry.AvatarValid = avatar != null && avatar.isValid;
            entry.Rig = avatar == null
                ? "none"
                : avatar.isHuman ? "humanoid" : "generic";

            // Every clip in the file, whether or not the importer split it out.
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            {
                // Unity synthesises a "__preview__" clip for the model preview; it is not content.
                if (clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                entry.Clips.Add(new ClipInfo
                {
                    Name = clip.name,
                    Length = clip.length,
                    Loop = clip.isLooping,
                });
            }

            return entry;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "/").Replace("\"", "\\\"");
        }

        private sealed class Entry
        {
            public string Path;
            public string Rig;
            public bool AvatarValid;
            public readonly List<ClipInfo> Clips = new List<ClipInfo>();
        }

        private sealed class ClipInfo
        {
            public string Name;
            public float Length;
            public bool Loop;
        }
    }
}
