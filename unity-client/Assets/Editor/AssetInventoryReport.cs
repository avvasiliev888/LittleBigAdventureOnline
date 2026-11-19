#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Albion.Editor
{
    /// <summary>Один файл внутри .unitypackage: путь, который он получит в проекте.</summary>
    public sealed class AssetFile
    {
        public string Path;
        public string Name;
        public string Extension;

        /// <summary>Размер preview.png, если у ассета есть иконка. 0 — иконки нет.</summary>
        public long Bytes;

        /// <summary>GUID-папка внутри .unitypackage; по ней находится preview.png.</summary>
        public string Guid;

        /// <summary>
        /// Содержимое preview.png. Заполняется только для показа в галерее и только когда
        /// пакет решено показать; для тысяч остальных файлов остаётся null.
        /// </summary>
        public byte[] IconBytes;

        public bool HasIcon => Bytes > 0;
    }

    /// <summary>Разобранный пакет: состав файлов плюс счётчики, нужные для сводки.</summary>
    public sealed class AssetPackage
    {
        public string Path;
        public string Label;
        public string Vendor;
        public long Bytes;
        public List<AssetFile> Files = new List<AssetFile>();

        /// <summary>Пользовательская категория из Assets/Art/Categories/&lt;ключ&gt;.txt.</summary>
        public string Category;

        public int ModelCount { get; private set; }
        public int AnimationCount { get; private set; }
        public int IconCount { get; private set; }
        public int SceneCount { get; private set; }
        public int PrefabCount { get; private set; }

        public void DetectCategories()
        {
            ModelCount = Count(".fbx", ".obj", ".blend", ".dae");
            AnimationCount = Count(".anim", ".controller", ".overridecontroller", ".playable");
            IconCount = Files.Count(file => file.HasIcon);
            SceneCount = Count(".unity");
            PrefabCount = Count(".prefab");
        }

        private int Count(params string[] extensions)
        {
            var set = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            return Files.Count(file => set.Contains(file.Extension));
        }

        public double Megabytes => Bytes / (1024.0 * 1024.0);
    }

    /// <summary>Сводный отчёт по всем найденным пакетам.</summary>
    public sealed class InventoryReport
    {
        public string Root;
        public List<AssetPackage> Packages = new List<AssetPackage>();

        public int FileCount => Packages.Sum(package => package.Files.Count);
        public int ModelCount => Packages.Sum(package => package.ModelCount);
        public int AnimationCount => Packages.Sum(package => package.AnimationCount);
        public int IconCount => Packages.Sum(package => package.IconCount);
        public double Megabytes => Packages.Sum(package => package.Megabytes);

        /// <summary>
        /// Применяет ручные категории поверх эвристики. Файл
        /// <c>Assets/Art/Categories/&lt;КлючПакета&gt;.txt</c> переопределяет категорию пакета:
        /// эвристика по вендору и содержимому ошибается на смешанных наборах вроде UMA.
        /// </summary>
        public void BuildCategories()
        {
            foreach (var package in Packages)
            {
                package.Category = Classify(package);
                var overridePath = System.IO.Path.Combine(
                    CategoriesRoot(), package.Label + ".txt");
                if (!File.Exists(overridePath)) continue;
                var text = File.ReadAllText(overridePath).Trim();
                if (text.Length > 0) package.Category = text;
            }
        }

        private static string CategoriesRoot()
        {
            // Application.dataPath заканчивается на /Assets — отрезаем один сегмент.
            var assets = UnityEngine.Application.dataPath;
            var project = System.IO.Path.GetDirectoryName(assets) ?? assets;
            return System.IO.Path.Combine(project, "Assets", "Art", "Categories");
        }

        private static string Classify(AssetPackage package)
        {
            if (package.Vendor.IndexOf("UMA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "UMA — конструктор персонажей (рантайм-генерация)";
            }

            if (package.ModelCount >= 10 && package.AnimationCount == 0)
            {
                return "Готовые модели (анимации не входят в пакет)";
            }

            if (package.AnimationCount > 0)
            {
                return "Готовые модели с анимациями";
            }

            return "Прочее";
        }
    }

    /// <summary>Сборка самодостаточного HTML-отчёта с base64-иконками и CSV-выгрузки.</summary>
    public static class AssetInventoryHtml
    {
        public static string RenderCsv(InventoryReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("package,vendor,project_path,has_icon,icon_bytes");
            foreach (var package in report.Packages.OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var file in package.Files)
                {
                    builder.Append(Csv(package.Label)).Append(',')
                        .Append(Csv(package.Vendor)).Append(',')
                        .Append(Csv(file.Path)).Append(',')
                        .Append(file.HasIcon ? "1" : "0").Append(',')
                        .Append(file.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .AppendLine();
                }
            }
            return builder.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0;
            var escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? "\"" + escaped + "\"" : escaped;
        }

        public static string Render(InventoryReport report)
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\">");
            html.AppendLine("<title>Инвентарь скачанных ассетов</title>");
            html.AppendLine("<style>");
            html.AppendLine(Css);
            html.AppendLine("</style></head><body>");
            html.AppendLine("<h1>Скачанные ассеты (Asset Store)</h1>");

            html.Append("<p class=\"lede\">Кэш: <code>").Append(Escape(report.Root)).AppendLine("</code></p>");

            html.AppendLine("<div class=\"summary\">");
            Summary(html, report.Packages.Count, "пакетов");
            Summary(html, report.FileCount, "файлов");
            Summary(html, report.ModelCount, "моделей (FBX/OBJ)");
            Summary(html, report.AnimationCount, "анимаций и контроллеров");
            Summary(html, report.IconCount, "с иконками");
            Summary(html, Math.Round(report.Megabytes).ToString("N0"), "МБ");
            html.AppendLine("</div>");

            html.AppendLine("<nav class=\"packages\"><h2>Пакеты подряд</h2><table>");
            html.AppendLine("<tr><th>Пакет</th><th>Категория</th><th class=\"num\">Моделей</th>"
                + "<th class=\"num\">Анимаций</th><th class=\"num\">Файлов</th><th class=\"num\">МБ</th><th></th></tr>");
            foreach (var package in report.Packages.OrderByDescending(p => p.ModelCount + p.AnimationCount))
            {
                html.Append("<tr><td><strong>").Append(Escape(package.Label)).Append("</strong><br><span class=\"dim\">")
                    .Append(Escape(package.Vendor)).Append("</span></td><td>").Append(Escape(package.Category))
                    .Append("</td><td class=\"num\">").Append(package.ModelCount)
                    .Append("</td><td class=\"num\">").Append(package.AnimationCount)
                    .Append("</td><td class=\"num\">").Append(package.Files.Count)
                    .Append("</td><td class=\"num\">").Append(package.Megabytes.ToString("N1"))
                    .Append("</td><td><a href=\"#").Append(Slug(package.Label)).Append("\">состав</a></td></tr>");
            }
            html.AppendLine("</table></nav>");

            foreach (var package in report.Packages.OrderByDescending(p => p.Files.Count))
            {
                RenderPackage(html, package);
            }

            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private static void RenderPackage(StringBuilder html, AssetPackage package)
        {
            html.Append("<section id=\"").Append(Slug(package.Label)).Append("\">");
            html.Append("<h2>").Append(Escape(package.Label)).Append("</h2>");
            html.Append("<p class=\"dim\">").Append(Escape(package.Vendor)).Append(" · ")
                .Append(Escape(package.Category)).Append(" · ")
                .Append(package.Megabytes.ToString("N1")).Append(" МБ · ")
                .Append(package.Files.Count).Append(" файлов</p>");

            var folders = package.Files
                .GroupBy(file => FolderOf(file.Path))
                .OrderByDescending(group => group.Count())
                .ToArray();

            html.Append("<table><tr><th>Папка в проекте</th><th class=\"num\">Файлов</th>"
                + "<th class=\"num\">Модели</th><th class=\"num\">Анимации</th><th class=\"num\">Иконки</th></tr>");
            foreach (var folder in folders.Take(40))
            {
                var files = folder.ToArray();
                var models = files.Count(file => IsOneOf(file.Extension, ".fbx", ".obj", ".blend", ".dae"));
                var anims = files.Count(file => IsOneOf(file.Extension, ".anim", ".controller", ".playable"));
                var icons = files.Count(file => file.HasIcon);                html.Append("<tr><td><code>").Append(Escape(folder.Key)).Append("</code></td>")
                    .Append("<td class=\"num\">").Append(files.Length).Append("</td>")
                    .Append("<td class=\"num\">").Append(models > 0 ? models.ToString() : "—").Append("</td>")
                    .Append("<td class=\"num\">").Append(anims > 0 ? anims.ToString() : "—").Append("</td>")
                    .Append("<td class=\"num\">").Append(icons > 0 ? icons.ToString() : "—").Append("</td></tr>");
            }
            html.Append("</table>");
            if (folders.Length > 40)
            {
                html.Append("<p class=\"dim\">…и ещё ").Append(folders.Length - 40).Append(" папок.</p>");
            }

            var thumbnails = package.Files.Where(file => file.IconBytes != null).ToArray();
            if (thumbnails.Length > 0)
            {
                html.Append("<h3>Иконки из пакета (превью Project window)</h3><div class=\"gallery\">");
                foreach (var icon in thumbnails)
                {
                    html.Append("<figure><img loading=\"lazy\" src=\"data:image/png;base64,")
                        .Append(Convert.ToBase64String(icon.IconBytes))
                        .Append("\" alt=\"\"><figcaption title=\"").Append(Escape(icon.Path)).Append("\">")
                        .Append(Escape(icon.Name)).Append("</figcaption></figure>");
                }
                html.Append("</div>");
            }

            html.AppendLine("</section>");
        }

        private static string FolderOf(string path)
        {
            var index = path.LastIndexOf('/');
            return index <= 0 ? path : path.Substring(0, index);
        }

        private static bool IsOneOf(string extension, params string[] candidates)
        {
            return candidates.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static void Summary(StringBuilder html, int value, string label)
        {
            Summary(html, value.ToString(), label);
        }

        private static void Summary(StringBuilder html, string value, string label)
        {
            html.Append("<div><strong>").Append(value).Append("</strong><span>")
                .Append(Escape(label)).Append("</span></div>");
        }

        private static string Slug(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
            }
            return builder.ToString().Trim('-');
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private const string Css = @"
:root { color-scheme: dark; }
* { box-sizing: border-box; }
body { margin: 0; padding: 32px; background: #14161a; color: #e6e8ec;
       font: 14px/1.5 -apple-system, 'Segoe UI', Roboto, sans-serif; }
h1 { margin: 0 0 4px; font-size: 26px; }
h2 { margin: 0 0 6px; font-size: 19px; }
h3 { margin: 22px 0 10px; font-size: 15px; font-weight: 600; color: #a8b0bd; }
a { color: #6cb6ff; text-decoration: none; }
a:hover { text-decoration: underline; }
code { background: #1e2229; padding: 2px 6px; border-radius: 4px; font-size: 12.5px; }
.lede { margin: 0 0 20px; color: #8b93a1; }
.dim { color: #8b93a1; font-size: 12.5px; }
.summary { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 28px; }
.summary > div { background: #1b1f26; border: 1px solid #262c36; border-radius: 10px;
                 padding: 12px 18px; min-width: 130px; }
.summary strong { display: block; font-size: 22px; }
.summary span { color: #8b93a1; font-size: 12.5px; }
section { background: #171a20; border: 1px solid #262c36; border-radius: 12px;
          padding: 20px; margin: 0 0 22px; }
table { width: 100%; border-collapse: collapse; margin: 12px 0 4px; }
th, td { text-align: left; padding: 7px 10px; border-bottom: 1px solid #23282f; vertical-align: top; }
th { color: #8b93a1; font-weight: 600; font-size: 12.5px; text-transform: uppercase; letter-spacing: .04em; }
.num { text-align: right; font-variant-numeric: tabular-nums; }
tbody tr:hover, table tr:hover { background: #1c2028; }
.gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(112px, 1fr)); gap: 12px; }
figure { margin: 0; background: #1b1f26; border: 1px solid #262c36; border-radius: 9px;
         padding: 8px; text-align: center; }
figure img { width: 100%; height: 84px; object-fit: contain; image-rendering: auto; }
figcaption { margin-top: 6px; font-size: 11px; color: #8b93a1; word-break: break-word;
             overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
nav.packages { margin-bottom: 30px; }
";
    }
}
#endif
