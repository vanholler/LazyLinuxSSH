using System.Text;

namespace LazyLinuxSSH.Infrastructure.Files;

internal static class TextFileReader
{
    public const int MaxTextFileBytes = 16 * 1024 * 1024;

    public static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek && stream.Length > MaxTextFileBytes)
            throw new InvalidDataException("Файл больше 16 МБ. Для безопасной работы редактор открывает только текстовые файлы до 16 МБ.");

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        if (memory.Length > MaxTextFileBytes)
            throw new InvalidDataException("Файл больше 16 МБ. Для безопасной работы редактор открывает только текстовые файлы до 16 МБ.");

        memory.Position = 0;
        using var reader = new StreamReader(
            memory,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (text.Contains('\0'))
            throw new InvalidDataException("Это бинарный файл. Встроенный редактор предназначен для текстовых файлов.");
        return text;
    }

    public static MemoryStream CreateUtf8Stream(string content) =>
        new(new UTF8Encoding(false).GetBytes(content), writable: false);
}
