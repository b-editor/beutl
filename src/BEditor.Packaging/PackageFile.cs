using System;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    public static class PackageFile
    {
        internal static readonly JsonSerializerOptions _serializerOptions = new()
        {
            // すべての言語セットをエスケープせずにシリアル化させる
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };

        public static void CreatePackage(string mainfile, string packagefile, Package info, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile)) throw new FileNotFoundException(null, mainfile);

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            Compress(dir, packagefile, info, progress);
        }

        public static async Task CreatePackageAsync(string mainfile, string packagefile, Package info, IProgress<int>? progress = null)
        {
            if (!File.Exists(mainfile)) throw new FileNotFoundException(null, mainfile);

            var dirinfo = Directory.GetParent(mainfile)!;
            var dir = dirinfo.FullName;

            await CompressAsync(dir, packagefile, info, progress);
        }

        public static Package? OpenPackage(string packagefile, string destDirectory, IProgress<int>? progress = null)
        {
            using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Package? info = null;
            Directory.CreateDirectory(destDirectory);
            var entries = zip.Entries;

            progress?.Report(0);
            for (var i = 0; i < entries.Count; i++)
            {
                var item = entries[i];
                if (item.FullName is "PACKAGEINFO")
                {
                    using var itemStream = item.Open();
                    info = ReadInfo(itemStream);
                }
                else
                {
                    var dstPath = Path.Combine(destDirectory, item.FullName);
                    var dirInfo = Directory.GetParent(dstPath)!;
                    if (!dirInfo.Exists) dirInfo.Create();
                    using var dstStream = new FileStream(dstPath, FileMode.Create);
                    using var srcStream = item.Open();

                    srcStream.CopyTo(dstStream);
                }
                progress?.Report(i / entries.Count);
            }

            return info;
        }

        public static async Task<Package?> OpenPackageAsync(string packagefile, string destDirectory, IProgress<int>? progress = null)
        {
            await using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Package? info = null;
            Directory.CreateDirectory(destDirectory);
            var entries = zip.Entries;

            progress?.Report(0);
            for (var i = 0; i < entries.Count; i++)
            {
                var item = entries[i];
                if (item.FullName is "PACKAGEINFO")
                {
                    await using var itemStream = item.Open();
                    info = await ReadInfoAsync(itemStream);
                }
                else
                {
                    var dstPath = Path.Combine(destDirectory, item.FullName);
                    var dirInfo = Directory.GetParent(dstPath)!;
                    if (!dirInfo.Exists) dirInfo.Create();
                    await using var dstStream = new FileStream(dstPath, FileMode.Create);
                    await using var srcStream = item.Open();

                    await srcStream.CopyToAsync(dstStream);
                }
                progress?.Report(i / entries.Count);
            }

            return info;
        }

        public static Package GetPackageInfo(string packagefile)
        {
            if (!File.Exists(packagefile)) throw new FileNotFoundException(null, packagefile);

            using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = zip.GetEntry("PACKAGEINFO") ?? throw new InvalidOperationException("このファイルは BEditor Package ではありません。");

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return JsonSerializer.Deserialize<Package>(reader.ReadToEnd(), _serializerOptions) ?? throw new NotSupportedException("サポートしていないパッケージ情報です。");
        }

        public static async Task<Package> GetPackageInfoAsync(string packagefile)
        {
            if (!File.Exists(packagefile)) throw new FileNotFoundException(null, packagefile);

            await using var stream = new FileStream(packagefile, FileMode.Open);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = zip.GetEntry("PACKAGEINFO") ?? throw new InvalidOperationException("このファイルは BEditor Package ではありません。");

            await using var entryStream = entry.Open();
            return await JsonSerializer.DeserializeAsync<Package>(entryStream, _serializerOptions) ?? throw new NotSupportedException("サポートしていないパッケージ情報です。");
        }

        private static void Compress(string directory, string packagefile, Package info, IProgress<int>? progress = null)
        {
            using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                var entryName = Path.GetRelativePath(directory, item);
                var entry = zip.CreateEntry(entryName);

                using var entryStream = entry.Open();
                using var itemStream = new FileStream(item, FileMode.Open);

                itemStream.CopyTo(entryStream);
                progress?.Report(i / array.Length);
            }

            using var infoStream = zip.CreateEntry("PACKAGEINFO").Open();
            WriteInfo(infoStream, info);
        }

        private static async Task CompressAsync(string directory, string packagefile, Package info, IProgress<int>? progress = null)
        {
            await using var stream = new FileStream(packagefile, FileMode.Create);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var array = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            progress?.Report(0);
            for (var i = 0; i < array.Length; i++)
            {
                var item = array[i];
                var entryName = Path.GetRelativePath(directory, item);
                var entry = zip.CreateEntry(entryName);

                await using var entryStream = entry.Open();
                await using var itemStream = new FileStream(item, FileMode.Open);

                await itemStream.CopyToAsync(entryStream);
                progress?.Report(i / array.Length);
            }

            await using var infoStream = zip.CreateEntry("PACKAGEINFO").Open();
            await WriteInfoAsync(infoStream, info);
        }

        private static void WriteInfo(Stream stream, Package package)
        {
            var json = JsonSerializer.Serialize(package, _serializerOptions);
            using var writer = new StreamWriter(stream);
            writer.NewLine = "\n";

            writer.Write(json);
        }

        private static async Task WriteInfoAsync(Stream stream, Package package)
        {
            await JsonSerializer.SerializeAsync(stream, package, _serializerOptions);
        }

        private static Package ReadInfo(Stream stream)
        {
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Package>(reader.ReadToEnd(), _serializerOptions)!;
        }

        private static ValueTask<Package> ReadInfoAsync(Stream stream)
        {
            return JsonSerializer.DeserializeAsync<Package>(stream, _serializerOptions)!;
        }
    }
}
