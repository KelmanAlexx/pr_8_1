using System.Text.Json;

namespace AutoUpdater.Models;

/// Локальные настройки утилиты. Они хранятся в updater.json.
public class AppConfig
{
    public string ManifestUrl { get; set; } = "";
    public string CurrentVersion { get; set; } = "1.0.0";
    public string TargetDirectory { get; set; } = "./MyApplication";

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Файл настроек не найден. Создайте updater.json рядом с AutoUpdater.",
                path);
        }

        string json = File.ReadAllText(path);
        AppConfig? config = JsonSerializer.Deserialize<AppConfig>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (config == null)
        {
            throw new InvalidDataException("Файл updater.json пустой или содержит неправильный JSON.");
        }

        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ManifestUrl))
        {
            throw new InvalidDataException("В updater.json не указан ManifestUrl.");
        }

        if (!Uri.TryCreate(ManifestUrl, UriKind.Absolute, out Uri? manifestUri) ||
            manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("ManifestUrl должен быть корректной HTTPS-ссылкой.");
        }

        if (!SemVer.TryParse(CurrentVersion, out _))
        {
            throw new InvalidDataException(
                "CurrentVersion должен иметь формат SemVer, например 1.0.0 или 1.2.0-beta.1.");
        }

        if (string.IsNullOrWhiteSpace(TargetDirectory))
        {
            throw new InvalidDataException("В updater.json не указан TargetDirectory.");
        }
    }

    public string GetFullTargetDirectory(string updaterDirectory)
    {
        return Path.GetFullPath(Path.Combine(updaterDirectory, TargetDirectory));
    }

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        // Сначала записываем временный файл, затем заменяем основной.
        // Это снижает риск получить повреждённый updater.json при сбое записи.
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, true);
    }
}
