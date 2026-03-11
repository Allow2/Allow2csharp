namespace Allow2;

using System.Text.Json;

/// <summary>
/// Stored device pairing credentials.
/// </summary>
public record Allow2StoredCredentials(ulong UserId, ulong PairId, string PairToken);

/// <summary>
/// Interface for persisting and loading device pairing credentials.
/// Implement this for platform-specific secure storage (e.g., Windows DPAPI,
/// macOS Keychain, Linux Secret Service).
/// </summary>
public interface IAllow2CredentialStore
{
    Task<bool> SaveAsync(Allow2StoredCredentials credentials);
    Task<Allow2StoredCredentials?> LoadAsync();
    Task<bool> ClearAsync();
}

/// <summary>
/// Default file-based credential store for development and simple deployments.
/// Stores credentials as JSON in the user's application data folder.
/// Production integrations should use a platform-specific secure store.
/// </summary>
public class FileCredentialStore : IAllow2CredentialStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FileCredentialStore(string? path = null)
    {
        _filePath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Allow2", "credentials.json");
    }

    public async Task<bool> SaveAsync(Allow2StoredCredentials credentials)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(credentials, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
        return true;
    }

    public async Task<Allow2StoredCredentials?> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Allow2StoredCredentials>(json, JsonOptions);
    }

    public Task<bool> ClearAsync()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        return Task.FromResult(true);
    }
}
