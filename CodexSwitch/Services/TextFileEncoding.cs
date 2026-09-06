using System.Text;

namespace CodexSwitch.Services;

internal static class TextFileEncoding
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool HasUtf8Bom(string path)
    {
        if (!File.Exists(path))
            return false;

        var bom = Encoding.UTF8.GetPreamble();
        if (bom.Length == 0)
            return false;

        var buffer = new byte[bom.Length];
        using var stream = File.OpenRead(path);
        if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
            return false;

        return buffer.AsSpan().SequenceEqual(bom);
    }
}
