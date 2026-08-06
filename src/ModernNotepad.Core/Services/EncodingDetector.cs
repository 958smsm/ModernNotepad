using System.Text;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static (string Text, TextEncodingInfo Encoding) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            return DecodeWith(bytes, 4, new TextEncodingInfo(12001, "utf-32BE", true));
        }

        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            return DecodeWith(bytes, 4, new TextEncodingInfo(12000, "utf-32", true));
        }

        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return DecodeWith(bytes, 3, TextEncodingInfo.Utf8Bom);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return DecodeWith(bytes, 2, new TextEncodingInfo(1201, "utf-16BE", true));
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return DecodeWith(bytes, 2, new TextEncodingInfo(1200, "utf-16", true));
        }

        try
        {
            var utf8 = new UTF8Encoding(false, true);
            return (utf8.GetString(bytes), TextEncodingInfo.Utf8NoBom);
        }
        catch (DecoderFallbackException)
        {
            var fallback = Encoding.GetEncoding(
                1252,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ReplacementFallback);
            return (fallback.GetString(bytes), new TextEncodingInfo(1252, fallback.WebName, false));
        }
    }

    private static (string Text, TextEncodingInfo Encoding) DecodeWith(
        byte[] bytes,
        int preambleLength,
        TextEncodingInfo encodingInfo)
    {
        var encoding = encodingInfo.CreateEncoding(throwOnInvalidBytes: false);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return (text, encodingInfo);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }
}
