using System.Text;
using System.Text.Json;

namespace CriScope.Core;

// A newline commits a JSONL record. A crash can leave bytes after the final LF;
// readers freeze the extent at the last complete line and never parse that tail.
internal static class RecordingLines
{
    internal static IEnumerable<WireEvent> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long completeLength = CompleteLength(file);
        file.Position = 0;
        using var reader = new StreamReader(new ReadWindowStream(file, completeLength), new UTF8Encoding(false, true));
        int lineNumber = 0;
        while (true)
        {
            string? line;
            try { line = reader.ReadLine(); }
            catch (DecoderFallbackException ex)
            { throw new InvalidDataException($"录制段 {Path.GetFileName(path)} 第 {lineNumber+1} 行 UTF-8 损坏", ex); }
            if (line == null) yield break;
            lineNumber++;
            if (line.Length > 64 * 1024 * 1024)
                throw new InvalidDataException($"录制段 {Path.GetFileName(path)} 第 {lineNumber} 行过长");
            WireEvent ev;
            try { ev = WireEvent.Parse(line); }
            catch (Exception ex) when (ex is JsonException or DecoderFallbackException or FormatException)
            { throw new InvalidDataException($"录制段 {Path.GetFileName(path)} 第 {lineNumber} 行损坏", ex); }
            yield return ev;
        }
    }

    private static long CompleteLength(FileStream file)
    {
        long remaining = file.Length;
        var buffer = new byte[8192];
        while (remaining > 0)
        {
            int count = (int)Math.Min(buffer.Length, remaining);
            long start = remaining - count;
            file.Position = start;
            file.ReadExactly(buffer.AsSpan(0, count));
            for (int i = count - 1; i >= 0; i--)
                if (buffer[i] == (byte)'\n') return start + i + 1;
            remaining = start;
        }
        return 0;
    }
}
