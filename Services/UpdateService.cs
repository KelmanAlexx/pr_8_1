using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoUpdater.Models;

namespace AutoUpdater.Services;

/// <summary>
/// Проверяет обновления, загружает архив, проверяет его хеш и устанавливает файлы.
/// </summary>
public class UpdateService
{
    private const long MaximumPackageSize = 500L * 1024L * 1024L; // 500 МБ
    private const long MaximumExtractedSize = 1024L * 1024L * 1024L; // 1 ГБ

    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly string _configPath;
    private readonly string _updaterDirectory;

    public UpdateService(
        HttpClient httpClient,
        AppConfig config,
        string configPath,
        string updaterDirectory)
    {
        _httpClient = httpClient;
        _config = config;
        _configPath = configPath;
        _updaterDirectory = updaterDirectory;
    }

    public async Task CheckForUpdatesAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Проверка обновлений...");

        UpdateManifest manifest = await DownloadManifestAsync();
        SemVer currentVersion = SemVer.Parse(_config.CurrentVersion);
        SemVer availableVersion = SemVer.Parse(manifest.Version);

        Console.WriteLine("Установленная версия: " + currentVersion);
        Console.WriteLine("Версия на сервере:    " + availableVersion);

        int comparison = availableVersion.CompareTo(currentVersion);

        if (comparison > 0)
        {
            Console.WriteLine("Доступно новое обновление.");
            if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
            {
                Console.WriteLine("Описание: " + manifest.ReleaseNotes);
            }
        }
        else if (comparison == 0)
        {
            Console.WriteLine("Установлена последняя версия.");
        }
        else
        {
            Console.WriteLine("На сервере находится более старая версия. Обновление не требуется.");
        }
    }

    public async Task UpdateAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Проверка обновлений...");

        UpdateManifest manifest = await DownloadManifestAsync();
        SemVer currentVersion = SemVer.Parse(_config.CurrentVersion);
        SemVer availableVersion = SemVer.Parse(manifest.Version);

        Console.WriteLine("Установленная версия: " + currentVersion);
        Console.WriteLine("Версия на сервере:    " + availableVersion);

        if (availableVersion.CompareTo(currentVersion) <= 0)
        {
            Console.WriteLine("Новая версия не найдена. Установка не требуется.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            Console.WriteLine("Изменения: " + manifest.ReleaseNotes);
        }

        Console.WriteLine("Начинается установка версии " + manifest.Version + ".");
        Console.WriteLine("Перед запуском убедитесь, что обновляемая программа закрыта.");

        await InstallUpdateAsync(manifest);
    }

    private async Task<UpdateManifest> DownloadManifestAsync()
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(_config.ManifestUrl);
        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content.ReadAsStreamAsync();
        UpdateManifest? manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
            responseStream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (manifest == null)
        {
            throw new InvalidDataException("Сервер вернул пустой манифест обновления.");
        }

        manifest.Validate();
        return manifest;
    }

    private async Task InstallUpdateAsync(UpdateManifest manifest)
    {
        string targetDirectory = _config.GetFullTargetDirectory(_updaterDirectory);
        string updaterFullPath = Path.GetFullPath(_updaterDirectory);

        // Нельзя заменять папку, в которой находится сама утилита.
        // Иначе Windows может заблокировать её файлы во время установки.
        if (AreSamePath(targetDirectory, updaterFullPath) || IsInside(targetDirectory, updaterFullPath))
        {
            throw new InvalidOperationException(
                "TargetDirectory не должен совпадать с папкой Auto-Updater или содержать её.");
        }

        string? parentDirectory = Directory.GetParent(targetDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("Нельзя обновлять корневую папку диска.");
        }

        if (File.Exists(targetDirectory))
        {
            throw new IOException("По пути TargetDirectory находится файл, а не папка.");
        }

        Directory.CreateDirectory(parentDirectory);

        string targetName = Path.GetFileName(targetDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        string workspace = Path.Combine(parentDirectory, "." + targetName + ".updater-temp");
        string archivePath = Path.Combine(workspace, "update.zip");
        string stagingDirectory = Path.Combine(workspace, "staging");
        string backupDirectory = Path.Combine(
            parentDirectory,
            targetName + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);

        bool oldDirectoryMoved = false;
        bool newDirectoryMoved = false;

        try
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, true);
            }

            Directory.CreateDirectory(workspace);

            Console.WriteLine("Загрузка архива обновления...");
            await DownloadPackageAsync(manifest.DownloadUrl, archivePath);

            Console.WriteLine("Проверка SHA-256...");
            await VerifySha256Async(archivePath, manifest.Sha256);
            Console.WriteLine("Контрольная сумма совпадает.");

            Console.WriteLine("Распаковка файлов...");
            await ExtractArchiveSafelyAsync(archivePath, stagingDirectory);

            if (!Directory.EnumerateFileSystemEntries(stagingDirectory).Any())
            {
                throw new InvalidDataException("Архив обновления не содержит файлов.");
            }

            // Сначала переименовываем старую папку в резервную копию.
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, backupDirectory);
                oldDirectoryMoved = true;
            }

            // Затем перемещаем подготовленные файлы на место программы.
            Directory.Move(stagingDirectory, targetDirectory);
            newDirectoryMoved = true;

            // Версию меняем только после успешной замены файлов.
            string oldVersion = _config.CurrentVersion;
            _config.CurrentVersion = manifest.Version;

            try
            {
                _config.Save(_configPath);
            }
            catch
            {
                _config.CurrentVersion = oldVersion;
                throw;
            }

            Console.WriteLine("Обновление успешно установлено.");
            Console.WriteLine("Текущая версия: " + _config.CurrentVersion);

            if (oldDirectoryMoved)
            {
                Console.WriteLine("Резервная копия: " + backupDirectory);
            }
        }
        catch
        {
            Console.WriteLine("Установка не завершилась. Выполняется попытка отката...");

            try
            {
                if (newDirectoryMoved && Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, true);
                }

                if (oldDirectoryMoved && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, targetDirectory);
                    Console.WriteLine("Предыдущая версия восстановлена.");
                }
            }
            catch (Exception rollbackException)
            {
                Console.WriteLine("Не удалось автоматически выполнить полный откат: " + rollbackException.Message);
                Console.WriteLine("Проверьте резервную папку: " + backupDirectory);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                try
                {
                    Directory.Delete(workspace, true);
                }
                catch (IOException)
                {
                    Console.WriteLine("Не удалось удалить временную папку: " + workspace);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("Нет прав на удаление временной папки: " + workspace);
                }
            }
        }
    }

    private async Task DownloadPackageAsync(string url, string destinationPath)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength.HasValue &&
            response.Content.Headers.ContentLength.Value > MaximumPackageSize)
        {
            throw new InvalidDataException("Размер архива превышает допустимые 500 МБ.");
        }

        await using Stream inputStream = await response.Content.ReadAsStreamAsync();
        await using FileStream outputStream = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        byte[] buffer = new byte[81920];
        long downloadedBytes = 0;

        while (true)
        {
            int bytesRead = await inputStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (bytesRead == 0)
            {
                break;
            }

            downloadedBytes += bytesRead;
            if (downloadedBytes > MaximumPackageSize)
            {
                throw new InvalidDataException("Размер архива превышает допустимые 500 МБ.");
            }

            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }
    }

    private static async Task VerifySha256Async(string filePath, string expectedHash)
    {
        await using FileStream fileStream = File.OpenRead(filePath);
        byte[] actualHash = await SHA256.HashDataAsync(fileStream);
        byte[] expectedHashBytes = Convert.FromHexString(expectedHash);

        // FixedTimeEquals сравнивает массивы без раннего выхода при первом несовпадении.
        bool hashesAreEqual = CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHashBytes);

        if (!hashesAreEqual)
        {
            throw new InvalidDataException(
                "SHA-256 архива не совпадает с хешем из манифеста. Установка отменена.");
        }
    }

    private static async Task ExtractArchiveSafelyAsync(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        int extractedFiles = 0;
        long totalUncompressedSize = 0;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        if (archive.Entries.Count > 10000)
        {
            throw new InvalidDataException("В архиве слишком много элементов.");
        }

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length > MaximumExtractedSize - totalUncompressedSize)
            {
                throw new InvalidDataException("Распакованный размер архива превышает допустимый 1 ГБ.");
            }

            totalUncompressedSize += entry.Length;

            string entryName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entryName));

            // Проверяем, что путь из ZIP не пытается выйти за пределы папки распаковки.
            string relativePath = Path.GetRelativePath(destinationRoot, fullPath);
            if (Path.IsPathRooted(relativePath) ||
                relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Архив содержит недопустимый путь: " + entry.FullName);
            }

            // Не разрешаем создавать символические ссылки из содержимого ZIP.
            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
            {
                throw new InvalidDataException(
                    "Архив содержит символическую ссылку: " + entry.FullName);
            }

            bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

            if (isDirectory)
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            string? parentDirectory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            await using Stream inputStream = entry.Open();
            await using FileStream outputStream = new(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await inputStream.CopyToAsync(outputStream);
            extractedFiles++;
        }

        if (extractedFiles == 0)
        {
            throw new InvalidDataException("В архиве не найдено ни одного файла.");
        }
    }

    private static bool AreSamePath(string firstPath, string secondPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            comparison);
    }

    private static bool IsInside(string parentPath, string childPath)
    {
        string fullParentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        string fullChildPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(childPath));

        string relativePath = Path.GetRelativePath(fullParentPath, fullChildPath);
        return relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }
}
