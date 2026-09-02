using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VSModifier.Memory.Trainer;

public sealed record TreasureLogObservation(int PrizeCount, IReadOnlyList<string> PrizeTypes);

internal readonly record struct TreasureLogCheckpoint(long Offset);

internal static class TreasureLogMonitor
{
    private const string PrizeCountMarker = "Treasure PrizeCount =";
    private const string PrizeTypeMarker = "PrizeType =";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static string GetDefaultPlayerLogPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? appData = Path.GetDirectoryName(localAppData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new DirectoryNotFoundException("無法定位目前使用者的 AppData 目錄。");
        }

        return Path.Combine(appData, "LocalLow", "poncle", "Vampire Survivors", "Player.log");
    }

    public static TreasureLogCheckpoint CaptureCheckpoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new TreasureLogCheckpoint(0);
        }

        using FileStream stream = OpenSharedRead(path);
        return new TreasureLogCheckpoint(stream.Length);
    }

    public static async Task<TreasureLogObservation> WaitForObservationAsync(
        string path,
        TreasureLogCheckpoint checkpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                string appended = await ReadAppendedTextAsync(path, checkpoint, cancellationToken).ConfigureAwait(false);
                if (TryParseObservation(appended, out TreasureLogObservation observation))
                {
                    return observation;
                }
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("驗證期間 Player.log 沒有新增寶箱獎勵事件；patch 已進入還原流程。");
    }

    internal static bool TryParseObservation(string text, out TreasureLogObservation observation)
    {
        observation = null!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int markerIndex = line.IndexOf(PrizeCountMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            ReadOnlySpan<char> remainder = line.AsSpan(markerIndex + PrizeCountMarker.Length).TrimStart();
            int digitCount = 0;
            while (digitCount < remainder.Length && char.IsAsciiDigit(remainder[digitCount]))
            {
                digitCount++;
            }

            if (digitCount == 0
                || !int.TryParse(
                    remainder[..digitCount],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int prizeCount))
            {
                continue;
            }

            List<string> prizeTypes = [];
            int searchIndex = markerIndex + PrizeCountMarker.Length + digitCount;
            while (searchIndex < line.Length)
            {
                int typeIndex = line.IndexOf(PrizeTypeMarker, searchIndex, StringComparison.Ordinal);
                if (typeIndex < 0)
                {
                    break;
                }

                int valueStart = typeIndex + PrizeTypeMarker.Length;
                int nextType = line.IndexOf(PrizeTypeMarker, valueStart, StringComparison.Ordinal);
                string value = line[valueStart..(nextType < 0 ? line.Length : nextType)].Trim(' ', ',', ';');
                if (value.Length > 0)
                {
                    prizeTypes.Add(value);
                }

                searchIndex = nextType < 0 ? line.Length : nextType;
            }

            observation = new TreasureLogObservation(prizeCount, prizeTypes);
            return true;
        }

        return false;
    }

    private static async Task<string> ReadAppendedTextAsync(
        string path,
        TreasureLogCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        using FileStream stream = OpenSharedRead(path);
        long offset = stream.Length < checkpoint.Offset ? 0 : checkpoint.Offset;
        stream.Seek(offset, SeekOrigin.Begin);
        // A checkpoint can be captured while Unity is midway through a multi-byte log character.
        // Replacement fallback keeps the later ASCII evidence searchable without accepting stale bytes.
        using StreamReader reader = new(stream, new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileStream OpenSharedRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
    }
}
