using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.Services;

public class FileSignatureValidatorTests
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("photo.jpg")]
    [InlineData("photo.png")]
    [InlineData("notes.txt")]
    [InlineData("doc.docx")]
    public void IsExtensionAllowed_ReturnsTrue_ForSupportedExtensions(string fileName)
    {
        Assert.True(FileSignatureValidator.IsExtensionAllowed(fileName));
    }

    [Fact]
    public void IsExtensionAllowed_ReturnsFalse_ForUnsupportedExtension()
    {
        Assert.False(FileSignatureValidator.IsExtensionAllowed("malware.exe"));
    }

    [Fact]
    public void MatchesContentSignature_ReturnsTrue_ForValidPdfHeader()
    {
        var pdfHeader = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };

        Assert.True(FileSignatureValidator.MatchesContentSignature("report.pdf", pdfHeader));
    }

    [Fact]
    public void MatchesContentSignature_ReturnsFalse_WhenRenamedExecutableClaimsToBePdf()
    {
        // MZ header — a Windows executable disguised with a .pdf extension
        var exeHeader = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

        Assert.False(FileSignatureValidator.MatchesContentSignature("report.pdf", exeHeader));
    }

    [Fact]
    public void MatchesContentSignature_ReturnsTrue_ForPlainTextWithNoKnownSignature()
    {
        var textHeader = System.Text.Encoding.UTF8.GetBytes("Hello World");

        Assert.True(FileSignatureValidator.MatchesContentSignature("notes.txt", textHeader));
    }
}
