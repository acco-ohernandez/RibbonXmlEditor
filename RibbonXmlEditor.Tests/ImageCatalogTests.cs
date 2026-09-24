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
    public void LoadThumbnail_RealPng_LoadsAndReleasesTheFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "Tool_16x16.png");
            ViewModelTests.Sta(() =>
            {
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawRectangle(System.Windows.Media.Brushes.SeaGreen, null, new System.Windows.Rect(0, 0, 16, 16));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = File.Create(png))
                    encoder.Save(fs);

                var bmp = ImageCatalog.LoadThumbnail(png, 16);
                Assert.NotNull(bmp);
                Assert.Equal(16, bmp!.PixelWidth);
                Assert.True(bmp.IsFrozen);
            });

            // The file must not stay locked after loading (users replace PNGs while the editor is open).
            File.Delete(png);
            Assert.False(File.Exists(png));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadThumbnail_MissingFile_ReturnsNull()
    {
        Assert.Null(ImageCatalog.LoadThumbnail(@"C:\nope\missing.png", 32));
        Assert.Null(ImageCatalog.LoadThumbnail("", 32));
    }
}
