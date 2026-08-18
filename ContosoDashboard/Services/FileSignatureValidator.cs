namespace ContosoDashboard.Services;

// Validates actual file content (magic-byte signatures) rather than trusting extension/MIME type (FR-002)
public static class FileSignatureValidator
{
    private static readonly IReadOnlyDictionary<string, byte[][]> SignaturesByExtension = new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } }, // %PDF
        [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
        [".txt"] = Array.Empty<byte[]>(), // plain text has no reliable magic number
        // Office Open XML formats (docx/xlsx/pptx) are ZIP-based
        [".docx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".xlsx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".pptx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        // Legacy Office formats (doc/xls/ppt) share the OLE Compound File signature
        [".doc"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
        [".xls"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
        [".ppt"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
    };

    public static bool IsExtensionAllowed(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return SignaturesByExtension.ContainsKey(extension);
    }

    public static bool MatchesContentSignature(string fileName, byte[] contentHeader)
    {
        var extension = Path.GetExtension(fileName);
        if (!SignaturesByExtension.TryGetValue(extension, out var signatures))
        {
            return false;
        }

        // No known signature to validate (e.g., plain text) — extension allow-list already checked
        if (signatures.Length == 0)
        {
            return true;
        }

        return signatures.Any(signature =>
            contentHeader.Length >= signature.Length &&
            contentHeader.Take(signature.Length).SequenceEqual(signature));
    }
}
