#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Инвентаризация скачанных ассетов Asset Store, которые ещё НЕ импортированы в проект.
    ///
    /// <c>.unitypackage</c> — это обычный gzip-тарбол, где каждый ассет лежит в папке со своим
    /// GUID: <c>&lt;guid&gt;/asset</c> (сами данные), <c>&lt;guid&gt;/pathname</c> (путь внутри
    /// проекта), <c>&lt;guid&gt;/preview.png</c> (иконка для Project window) и
    /// <c>&lt;guid&gt;/asset.meta</c>. Читая только последние два, можно показать полный состав
    /// пакета за секунды, не распаковывая гигабайты мешей и текстур.
    ///
    /// Обход строго последовательный: <see cref="GZipStream"/> не поддерживает Seek, поэтому
    /// записи читаются один раз по ходу, а всё нужное копится в памяти по мере прохода.
    ///
    /// Отчёт — самодостаточный HTML с встроенными (base64) иконками: открывается из файловой
    /// системы, ничего не импортирует в проект и ничего в нём не меняет.
    /// </summary>
    public static class AssetInventory
    {
        /// <summary>Кэш Asset Store; переопределяется переменной окружения ALBION_ASSET_STORE.</summary>
        public static string AssetStoreRoot
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("ALBION_ASSET_STORE");
                if (!string.IsNullOrEmpty(overridden)) return overridden;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Unity", "Asset Store-5.x");
            }
        }

        /// <summary>
        /// Куда писать отчёт, относительно корня проекта. Не в Library: Unity пересоздаёт его и
        /// подчищает содержимое, отчёт там не переживёт реимпорт. artifacts/ игнорируется git.
        /// </summary>
        public const string OutputFolder = "artifacts/asset-inventory";

        /// <summary>
        /// Абсолютный путь вывода. Считается от <see cref="Application.dataPath"/>, а не от
        /// текущего каталога процесса: в batchmode CWD — каталог запуска, и относительный путь
        /// уводит отчёт за пределы проекта.
        /// </summary>
        public static string OutputPath
        {
            get
            {
                // dataPath заканчивается на /Assets — корень проекта на один уровень выше.
                var project = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
                return Path.GetFullPath(Path.Combine(project, OutputFolder));
            }
        }

        /// <summary>Сколько иконок максимум попадает в HTML на один пакет (base64 тяжелеет быстро).</summary>
        public const int MaxIconsPerPackage = 120;

        /// <summary>Отдельная иконка больше этого — почти наверняка не иконка; в HTML такое не тащим.</summary>
        private const int MaxIconBytes = 512 * 1024;

        [MenuItem("Albion/Art/Inventory Downloaded Assets", priority = 200)]
        public static void InventoryDownloadedAssets()
        {
            var report = Build();
            Save(report);
            WriteLog(report);
        }

        /// <summary>Точка входа для -executeMethod в batchmode.</summary>
        public static void InventoryBatch()
        {
            try
            {
                InventoryDownloadedAssets();
            }
            catch (Exception error)
            {
                Debug.LogError("[inventory] " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Собирает отчёт по всем пакетам, ничего не записывая на диск.</summary>
        public static InventoryReport Build()
        {
            var report = new InventoryReport { Root = AssetStoreRoot };
            if (!Directory.Exists(AssetStoreRoot))
            {
                throw new DirectoryNotFoundException("Asset Store cache not found: " + AssetStoreRoot);
            }

            foreach (var package in Directory.GetFiles(AssetStoreRoot, "*.unitypackage", SearchOption.AllDirectories))
            {
                report.Packages.Add(ScanPackage(package));
            }

            report.BuildCategories();
            return report;
        }

        private static void Save(InventoryReport report)
        {
            var folder = OutputPath;
            Directory.CreateDirectory(folder);
            var html = Path.Combine(folder, "index.html");
            File.WriteAllText(html, AssetInventoryHtml.Render(report), new UTF8Encoding(false));
            var csv = Path.Combine(folder, "files.csv");
            File.WriteAllText(csv, AssetInventoryHtml.RenderCsv(report), new UTF8Encoding(false));
            Debug.Log("[inventory] Отчёт: " + html);
        }

        private static void WriteLog(InventoryReport report)
        {
            var summary = new StringBuilder("[inventory] " + report.Packages.Count + " пакет(ов), "
                + report.FileCount + " файл(ов):");
            foreach (var package in report.Packages.OrderByDescending(p => p.Files.Count))
            {
                summary.Append("\n  ").Append(package.Label)
                    .Append(" — ").Append(package.Files.Count).Append(" файлов")
                    .Append(", моделей ").Append(package.ModelCount)
                    .Append(", анимаций ").Append(package.AnimationCount)
                    .Append(", иконок ").Append(package.IconCount);
            }
            Debug.Log(summary.ToString());
        }

        private static AssetPackage ScanPackage(string packagePath)
        {
            var package = new AssetPackage
            {
                Path = packagePath,
                Label = Path.GetFileNameWithoutExtension(packagePath),
                Vendor = Path.GetFileName(Path.GetDirectoryName(packagePath) ?? string.Empty),
                Bytes = new FileInfo(packagePath).Length,
            };

            // guid -> путь внутри проекта; в том же проходе собираем иконки по guid.
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            var icons = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var skipped = 0;

            using (var file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip, Encoding.ASCII))
            {
                var header = new byte[512];
                while (true)
                {
                    if (reader.Read(header, 0, 512) != 512) break;
                    if (header.All(value => value == 0)) break;

                    var name = ReadString(header, 0, 100);
                    if (string.IsNullOrEmpty(name)) break;

                    var size = ReadOctal(header, 124, 12);
                    var typeFlag = (char)header[156];
                    // '0' и NUL — обычный файл; '5' — каталог GUID. Всё остальное (PAX, GNU long
                    // name) не встречается в .unitypackage, но пропускается тем же путём.
                    var isFile = typeFlag == '0' || typeFlag == '\0';

                    // Читаем только нужные записи: asset'ы весят десятки мегабайт и нам не нужны.
                    var wanted = isFile && size > 0 && size <= MaxEntryBytes
                        && (name.EndsWith("/pathname", StringComparison.Ordinal)
                            || name.EndsWith("/preview.png", StringComparison.Ordinal));

                    if (wanted)
                    {
                        var payload = reader.ReadBytes((int)size);
                        var guid = name.Substring(0, name.LastIndexOf('/'));
                        if (name.EndsWith("/pathname", StringComparison.Ordinal))
                        {
                            paths[guid] = ParsePathname(payload);
                        }
                        else if (IsPng(payload) && payload.Length <= MaxIconBytes)
                        {
                            icons[guid] = payload;
                        }

                        // Хвост записи добираем чтением: Seek по GZipStream запрещён.
                        Skip(reader, size - payload.Length + Padding(size));
                    }
                    else
                    {
                        Skip(reader, size + Padding(size));
                    }
                }
            }

            foreach (var pair in paths)
            {
                var path = pair.Value;
                if (path.Length == 0 || path.IndexOfAny(InvalidPathChars) >= 0)
                {
                    if (path.Length > 0)
                    {
                        skipped++;
                        Debug.LogWarning("[inventory] " + package.Label + ": негодный путь <"
                            + Describe(path) + ">");
                    }
                    continue;
                }

                var name = Path.GetFileName(path);
                if (name.Length == 0) continue;
                icons.TryGetValue(pair.Key, out var icon);
                package.Files.Add(new AssetFile
                {
                    Path = path,
                    Name = name,
                    Extension = Path.GetExtension(path).ToLowerInvariant(),
                    Guid = pair.Key,
                    Bytes = icon == null ? 0 : icon.Length,
                    IconBytes = icon,
                });
            }

            package.Files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            package.DetectCategories();
            TrimIcons(package);
            if (skipped > 0) Debug.LogWarning("[inventory] " + package.Label + ": пропущено путей " + skipped);
            return package;
        }

        /// <summary>
        /// Оставляет в галерее не больше <see cref="MaxIconsPerPackage"/> иконок, отбирая их по
        /// осмысленности. Порядок приоритетов важен: в UMA все 341 иконки одежды лежат в
        /// <c>Wearables/Icons/</c>, а алфавитная сортировка путей поставила бы впереди них
        /// текстуры из <c>Core/</c> — и до одежды лимит бы не дошёл.
        /// </summary>
        private static void TrimIcons(AssetPackage package)
        {
            var ranked = package.Files
                .Where(file => file.IconBytes != null)
                .OrderByDescending(file => IconPriority(file.Path))
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = MaxIconsPerPackage; index < ranked.Count; index++)
            {
                ranked[index].IconBytes = null;
            }
        }

        /// <summary>Насколько иконка полезна в галерее: одежда и префабы важнее служебных текстур.</summary>
        private static int IconPriority(string path)
        {
            if (path.IndexOf("/Icons/", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return 2;
            if (path.IndexOf("/Races/", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>Символы, которые делают путь негодным для Path.* и для файловой системы.</summary>
        private static readonly char[] InvalidPathChars = { '\0', '\n', '\r', '\t' };

        /// <summary>Печатает строку с кодами управляющих символов — иначе в логе не видно, что не так.</summary>
        private static string Describe(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value.Length > 160 ? value.Substring(0, 160) : value)
            {
                if (char.IsControl(character)) builder.Append("\\u").Append(((int)character).ToString("x4"));
                else builder.Append(character);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Достаёт путь из содержимого <c>pathname</c>. Файл — не голая строка, а Unity-объект
        /// <c>Pathname</c> в YAML: после пути идёт перевод строки и маркер <c>00</c>, за которым
        /// начинаются двоичные нули. Поэтому берём первую строку и срезаем хвост "00".
        /// </summary>
        private static string ParsePathname(byte[] payload)
        {
            var text = Encoding.UTF8.GetString(payload);
            var newline = text.IndexOfAny(LineBreaks);
            if (newline >= 0) text = text.Substring(0, newline);
            if (text.EndsWith(Trailer, StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - Trailer.Length);
            }
            return text.Trim().Replace('\\', '/');
        }

        /// <summary>Защита от мусорного заголовка: pathname/preview больше этого не бывают.</summary>
        private const long MaxEntryBytes = 8L * 1024 * 1024;

        private const string Trailer = "00";

        private static readonly char[] LineBreaks = { '\r', '\n' };

        /// <summary>Дополнение tar-записи до границы 512 байт.</summary>
        private static long Padding(long size)
        {
            return (512 - (size % 512)) % 512;
        }

        /// <summary>Пропускает байты чтением: GZipStream не поддерживает Seek.</summary>
        private static void Skip(BinaryReader reader, long count)
        {
            var buffer = new byte[64 * 1024];
            while (count > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, count);
                var read = reader.Read(buffer, 0, chunk);
                if (read <= 0) return;
                count -= read;
            }
        }

        private static bool IsPng(byte[] bytes)
        {
            return bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        }

        private static string ReadString(byte[] buffer, int offset, int length)
        {
            var end = offset;
            var limit = offset + length;
            while (end < limit && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
        }

        /// <summary>
        /// Размер в tar-заголовке записан ВОСЬМЕРИЧНО в ASCII. <c>long.TryParse</c> по умолчанию
        /// читает десятично и превращает "00000000043" в 43 вместо 35 — после чего поток
        /// разъезжается и все следующие записи читаются как мусор. Отсюда база 8.
        /// </summary>
        private static long ReadOctal(byte[] buffer, int offset, int length)
        {
            var text = ReadString(buffer, offset, length).Trim();
            if (text.Length == 0) return 0;
            long value = 0;
            foreach (var character in text)
            {
                if (character < '0' || character > '7') return value;
                value = (value * 8) + (character - '0');
            }
            return value;
        }
    }
}
#endif
