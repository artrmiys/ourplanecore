using System.Buffers;
using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal static class BoundedJsonStream
{
    private const int InitialBufferBytes = 256 * 1024;
    private const int MaxTokenBytes = 16 * 1024 * 1024;
    private const int MaxJsonLineBytes = 16 * 1024 * 1024;

    public static void ValidateDocument(string path) =>
        InspectDocument(path, null);

    public static void InspectDocument(
        string path,
        Action<JsonTokenType, string?, int>? inspectToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            InitialBufferBytes,
            FileOptions.SequentialScan);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
        try
        {
            var state = new JsonReaderState(JsonOptions());
            int buffered = 0;
            bool sawToken = false;
            while (true)
            {
                int read = stream.Read(buffer, buffered, buffer.Length - buffered);
                int available = buffered + read;
                bool final = read == 0;
                var reader = new Utf8JsonReader(
                    buffer.AsSpan(0, available),
                    final,
                    state);
                while (reader.Read())
                {
                    sawToken = true;
                    inspectToken?.Invoke(
                        reader.TokenType,
                        reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String
                            ? reader.GetString()
                            : null,
                        reader.CurrentDepth);
                }

                int consumed = checked((int)reader.BytesConsumed);
                state = reader.CurrentState;
                int remaining = available - consumed;
                if (final)
                {
                    if (!sawToken)
                        throw new JsonException("The JSON document is empty.");
                    if (remaining != 0)
                        throw new JsonException("The JSON document ended with an incomplete token.");
                    return;
                }

                if (remaining > 0)
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, remaining);
                buffered = remaining;
                if (buffered != buffer.Length)
                    continue;
                if (buffer.Length >= MaxTokenBytes)
                    throw new JsonException($"A JSON token exceeds {MaxTokenBytes / (1024 * 1024)} MB.");

                int nextLength = Math.Min(buffer.Length * 2, MaxTokenBytes);
                byte[] larger = ArrayPool<byte>.Shared.Rent(nextLength);
                Buffer.BlockCopy(buffer, 0, larger, 0, buffered);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = larger;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void ValidateJsonLines(string path) =>
        InspectJsonLines(path, null);

    public static void InspectJsonLines(
        string path,
        Action<int, JsonTokenType, string?, int>? inspectToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            InitialBufferBytes,
            FileOptions.SequentialScan);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
        using var line = new MemoryStream();
        int lineNumber = 0;
        try
        {
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                int offset = 0;
                while (offset < read)
                {
                    int newline = buffer.AsSpan(offset, read - offset).IndexOf((byte)'\n');
                    int count = newline < 0 ? read - offset : newline;
                    AppendBoundedLine(line, buffer, offset, count);
                    offset += count;
                    if (newline < 0)
                        continue;
                    lineNumber++;
                    ValidateLine(line, lineNumber, inspectToken);
                    line.SetLength(0);
                    offset++;
                }
            }

            if (line.Length > 0)
            {
                lineNumber++;
                ValidateLine(line, lineNumber, inspectToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendBoundedLine(
        MemoryStream line,
        byte[] buffer,
        int offset,
        int count)
    {
        if (line.Length + count > MaxJsonLineBytes)
        {
            throw new JsonException(
                $"A JSONL record exceeds {MaxJsonLineBytes / (1024 * 1024)} MB.");
        }
        line.Write(buffer, offset, count);
    }

    private static void ValidateLine(
        MemoryStream line,
        int lineNumber,
        Action<int, JsonTokenType, string?, int>? inspectToken)
    {
        ReadOnlySpan<byte> bytes = line.GetBuffer().AsSpan(0, checked((int)line.Length));
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];
        if (bytes.IsEmpty || bytes.IndexOfAnyExcept((byte)' ', (byte)'\t', (byte)'\r') < 0)
            return;
        try
        {
            var reader = new Utf8JsonReader(bytes, JsonOptions());
            bool sawToken = false;
            while (reader.Read())
            {
                sawToken = true;
                inspectToken?.Invoke(
                    lineNumber,
                    reader.TokenType,
                    reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String
                        ? reader.GetString()
                        : null,
                    reader.CurrentDepth);
            }
            if (!sawToken)
                throw new JsonException("The JSON record is empty.");
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Invalid JSON on line {lineNumber}: {ex.Message}", ex);
        }
    }

    private static JsonReaderOptions JsonOptions() =>
        new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 256,
        };
}
