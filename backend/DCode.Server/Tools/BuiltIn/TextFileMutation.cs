namespace DCode.Server.Tools.BuiltIn;

internal static class TextFileMutation
{
    public static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    public static string ReplaceFirst(string content, string oldText, string newText)
    {
        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        return string.Concat(content.AsSpan(0, index), newText, content.AsSpan(index + oldText.Length));
    }

    public static async Task WriteAtomicallyAsync(string fullPath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.dcode-tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
