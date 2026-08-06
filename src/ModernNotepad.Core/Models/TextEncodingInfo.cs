using System.Text;

namespace ModernNotepad.Core.Models;

public sealed record TextEncodingInfo(int CodePage, string WebName, bool EmitBom)
{
    static TextEncodingInfo()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TextEncodingInfo Utf8NoBom { get; } = new(65001, "utf-8", false);
    public static TextEncodingInfo Utf8Bom { get; } = new(65001, "utf-8", true);

    public Encoding CreateEncoding(bool throwOnInvalidBytes = true)
    {
        var encoderFallback = throwOnInvalidBytes
            ? EncoderFallback.ExceptionFallback
            : EncoderFallback.ReplacementFallback;
        var decoderFallback = throwOnInvalidBytes
            ? DecoderFallback.ExceptionFallback
            : DecoderFallback.ReplacementFallback;

        return CodePage switch
        {
            65001 => new UTF8Encoding(EmitBom, throwOnInvalidBytes),
            1200 => new UnicodeEncoding(false, EmitBom, throwOnInvalidBytes),
            1201 => new UnicodeEncoding(true, EmitBom, throwOnInvalidBytes),
            12000 => new UTF32Encoding(false, EmitBom, throwOnInvalidBytes),
            12001 => new UTF32Encoding(true, EmitBom, throwOnInvalidBytes),
            _ => Encoding.GetEncoding(CodePage, encoderFallback, decoderFallback)
        };
    }

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var encoding = CreateEncoding();
        var body = encoding.GetBytes(text);
        var preamble = EmitBom ? encoding.GetPreamble() : Array.Empty<byte>();

        if (preamble.Length == 0)
        {
            return body;
        }

        var output = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
        return output;
    }

    public override string ToString() => EmitBom ? $"{WebName} (BOM)" : WebName;
}
