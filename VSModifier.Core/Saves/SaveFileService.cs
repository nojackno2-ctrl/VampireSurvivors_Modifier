using System.Text;

namespace VSModifier.Core.Saves;

public sealed record SaveWriteResult(string SavePath, string BackupPath, string Checksum);

public sealed class SaveFileService(IGameProcessDetector? processDetector = null)
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly IGameProcessDetector _processDetector = processDetector ?? new GameProcessDetector();

    public async Task<SaveDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        string json = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken);
        return SaveDocument.Parse(json);
    }

    public async Task<SaveWriteResult> SaveAsync(
        string path,
        SaveDocument document,
        string? backupDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(document);

        if (_processDetector.IsGameRunning())
        {
            throw new InvalidOperationException("Vampire Survivors 正在執行。請先關閉遊戲，避免離開遊戲時覆蓋存檔。");
        }

        string fullPath = Path.GetFullPath(path);
        string targetDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("無法判斷存檔所在資料夾。");
        backupDirectory ??= Path.Combine(AppContext.BaseDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = Path.Combine(backupDirectory, $"SaveData_{stamp}.json");
        File.Copy(fullPath, backupPath, overwrite: false);

        string output = document.SerializeWithChecksum();
        string tempPath = Path.Combine(targetDirectory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                byte[] bytes = Utf8WithoutBom.GetBytes(output);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
            document.AcceptSavedText(output);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        SaveChecksum.TryRead(output, out string checksum);
        return new SaveWriteResult(fullPath, backupPath, checksum);
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 SaveData。", path);
        }
    }
}
