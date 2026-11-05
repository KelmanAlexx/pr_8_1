namespace AutoUpdater.Models;

/// Описание доступного обновления, которое загружается с сервера.
public class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";

    public void Validate()
    {
        if (!SemVer.TryParse(Version, out _))
        {
            throw new InvalidDataException("В манифесте указана некорректная версия SemVer.");
        }

        if (!Uri.TryCreate(DownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("DownloadUrl должен быть корректной HTTPS-ссылкой.");
        }

        if (string.IsNullOrWhiteSpace(Sha256) || Sha256.Length != 64)
        {
            throw new InvalidDataException("SHA-256 должен содержать ровно 64 шестнадцатеричных символа.");
        }

        foreach (char symbol in Sha256)
        {
            if (!Uri.IsHexDigit(symbol))
            {
                throw new InvalidDataException("SHA-256 содержит недопустимые символы.");
            }
        }
    }
}
