using RibbonXmlEditor.Services;
using Xunit;

namespace RibbonXmlEditor.Tests;

public class ImageCatalogTests
{
    [Fact]
    public void SiblingFor_FollowsAccoNamingConvention()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var n in new[] { "Tool_16x16.png", "Tool_32x32.png", "Tool_192x192.png", "Short_16.png", "Short_32.png" })
                File.WriteAllBytes(Path.Combine(dir, n), Array.Empty<byte>());

            var picked = Path.Combine(dir, "Tool_16x16.png");
            Assert.Equal(Path.Combine(dir, "Tool_32x32.png"), ImageCatalog.SiblingFor(picked, "largeimage"));
            Assert.Equal(Path.Combine(dir, "Tool_192x192.png"), ImageCatalog.SiblingFor(picked, "tooltipimage"));
            Assert.Equal(picked, ImageCatalog.SiblingFor(Path.Combine(dir, "Tool_32x32.png"), "image"));

            // Short "_16"/"_32" variant used by a few production files.
            Assert.Equal(Path.Combine(dir, "Short_32.png"), ImageCatalog.SiblingFor(Path.Combine(dir, "Short_16.png"), "largeimage"));
            Assert.Null(ImageCatalog.SiblingFor(Path.Combine(dir, "Short_16.png"), "tooltipimage")); // no _192 file

            Assert.Null(ImageCatalog.SiblingFor(Path.Combine(dir, "NoSize.png"), "largeimage"));
            Assert.Null(ImageCatalog.SiblingFor(picked, "groupname"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadThumbnail_MissingFile_ReturnsNull()
    {
        Assert.Null(ImageCatalog.LoadThumbnail(@"C:\nope\missing.png", 32));
        Assert.Null(ImageCatalog.LoadThumbnail("", 32));
    }
}
