using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Albion.Client;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Albion/Mobs/Import Body (Wizard): одно окно — один импорт. Своей логики импорта здесь нет:
    /// окно собирает аргументы и вызывает MobBodyImport.ImportBody (T-004).
    /// </summary>
    public sealed class BodyImportWindow : EditorWindow
    {
        private const string MobVisualsPath = "Assets/Resources/Characters/MobVisuals.asset";
        private const string SetsPath = "Assets/StreamingAssets/Config/animation-sets.json";

        private string[] mobKeys = Array.Empty<string>();
        private string[] setKeys = Array.Empty<string>();
        private int mobIndex;
        private int setIndex;
        private string bodyName = "";
        private GameObject sourceFbx;
        private string packageRoot = "";
        private float height = 2.45f;
        private string report = "";
        private Vector2 scroll;

        [MenuItem("Albion/Mobs/Import Body (Wizard)")]
        private static void Open()
        {
            var window = GetWindow<BodyImportWindow>(false, "Импорт тела", true);
            window.minSize = new Vector2(460f, 440f);
            window.Reload();
            window.Show();
        }

        private void OnEnable()
        {
            Reload();
        }

        private static MobVisualSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobVisualsPath);
        }

        private void Reload()
        {
            var settings = LoadSettings();
            mobKeys = settings != null && settings.Entries != null
                ? settings.Entries
                    .Select(entry => entry.MobKey)
                    .Where(key => !string.IsNullOrEmpty(key))
                    .ToArray()
                : Array.Empty<string>();
            mobIndex = Mathf.Clamp(mobIndex, 0, Mathf.Max(0, mobKeys.Length - 1));

            setKeys = ReadSetKeys();
            setIndex = Mathf.Clamp(setIndex, 0, Mathf.Max(0, setKeys.Length - 1));
        }

        /// <summary>Ключи наборов анимаций: тот же файл, что читает MobBodyImport.LoadSets.</summary>
        private static string[] ReadSetKeys()
        {
            if (!File.Exists(SetsPath))
            {
                return Array.Empty<string>();
            }

            var parsed = MiniJson.Parse(File.ReadAllText(SetsPath)) as Dictionary<string, object>;
            if (parsed == null
                || !parsed.TryGetValue("sets", out var setsValue)
                || !(setsValue is List<object> list))
            {
                return Array.Empty<string>();
            }

            var keys = new List<string>();
            foreach (var item in list)
            {
                if (item is Dictionary<string, object> entry
                    && entry.TryGetValue("key", out var key)
                    && key is string name)
                {
                    keys.Add(name);
                }
            }

            return keys.ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Импорт тела моба", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Окно только вызывает MobBodyImport.ImportBody: префаб, контроллер и запись "
                + "в MobVisuals.asset делает он.",
                UnityEditor.MessageType.None);

            if (mobKeys.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "В MobVisuals.asset нет ни одного моба — импорт недоступен.",
                    UnityEditor.MessageType.Warning);
            }

            mobIndex = EditorGUILayout.Popup("Моб (ключ)", mobIndex, mobKeys);
            
            var sourceFbxPath = sourceFbx != null ? AssetDatabase.GetAssetPath(sourceFbx) : "";
            if (sourceFbxPath.EndsWith(".prefab") || sourceFbxPath.EndsWith(".mat"))
            {
                EditorGUILayout.HelpBox("Выберите модель (.fbx/.obj), а не префаб или материал.", UnityEditor.MessageType.Error);
            }
            sourceFbx = (GameObject)EditorGUILayout.ObjectField("FBX тела", sourceFbx, typeof(GameObject), false);

            if (sourceFbx != null)
            {
                var fbxPath = AssetDatabase.GetAssetPath(sourceFbx);
                if (string.IsNullOrEmpty(bodyName))
                {
                    bodyName = Path.GetFileNameWithoutExtension(fbxPath);
                }

                if (string.IsNullOrEmpty(packageRoot))
                {
                    packageRoot = FindMaterialFolder(fbxPath);
                }
            }

            bodyName = EditorGUILayout.TextField("Имя тела", bodyName);
            packageRoot = EditorGUILayout.TextField("Папка пакета", packageRoot);
            setIndex = EditorGUILayout.Popup("Набор анимаций", setIndex, setKeys);
            height = EditorGUILayout.FloatField("Высота", height);
            EditorGUILayout.HelpBox(
                "Подгонка под collisionRadius: см. Jobs, WizardBody = 2.45.",
                UnityEditor.MessageType.None);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(mobKeys.Length == 0 || sourceFbx == null)) 
            { 
                if (GUILayout.Button("Импорт", GUILayout.Height(28f))) RunImport(); 
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180f));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string FindMaterialFolder(string fbxPath)
        {
            var current = Path.GetDirectoryName(fbxPath);
            for (int i = 0; i < 3; i++)
            {
                if (string.IsNullOrEmpty(current)) break;
                if (AssetDatabase.IsValidFolder(current + "/Materials")) return current;
                if (Directory.GetFiles(Path.GetFullPath(current), "*.mat").Length > 0) return current;
                current = Path.GetDirectoryName(current);
            }
            return Path.GetDirectoryName(fbxPath);
        }

        private void RunImport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("########## MOB IMPORT (WINDOW) ##########");

            try
            {
                var settings = LoadSettings();
                if (settings == null)
                {
                    throw new InvalidOperationException("MobVisuals.asset not found: " + MobVisualsPath);
                }

                if (mobKeys.Length == 0)
                {
                    throw new InvalidOperationException(
                        "В MobVisuals.asset нет мобов: сначала импортируйте моба.");
                }

                if (sourceFbx == null)
                {
                    throw new InvalidOperationException("Не выбран FBX тела.");
                }

                if (string.IsNullOrEmpty(bodyName))
                {
                    throw new InvalidOperationException("Не задано имя тела.");
                }

                if (string.IsNullOrEmpty(packageRoot))
                {
                    throw new InvalidOperationException("Не задана папка пакета.");
                }

                var setKey = setKeys.Length > 0 ? setKeys[setIndex] : "";
                if (string.IsNullOrEmpty(setKey))
                {
                    throw new InvalidOperationException("Не выбран набор анимаций.");
                }

                var mobKey = mobKeys[mobIndex];
                builder.AppendLine("mob=" + mobKey + " set=" + setKey + " height=" + height);

                var entries = settings.Entries != null
                    ? settings.Entries.ToList()
                    : new List<MobVisualSettings.Entry>();

                if (!MobBodyImport.ImportBody(
                        mobKey,
                        AssetDatabase.GetAssetPath(sourceFbx),
                        packageRoot,
                        bodyName,
                        setKey,
                        height,
                        builder,
                        entries,
                        out var prefab))
                {
                    throw new InvalidOperationException("ImportBody вернул false, см. отчёт выше.");
                }

                // Тот же порядок, что в MobBodyImport.Run (T-004): записать реестр, сохранить,
                // затем провалидировать материалы — валидатор ловит белый emission и отсутствие альбедо.
                settings.Entries = entries.ToArray();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                settings.ValidateMaterials();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                builder.AppendLine("PASS: prefab "
                    + (prefab != null ? AssetDatabase.GetAssetPath(prefab) : "?"));
            }
            catch (Exception exception)
            {
                builder.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
            }

            report = builder.ToString();
            Reload();
            Repaint();
        }
    }
}
