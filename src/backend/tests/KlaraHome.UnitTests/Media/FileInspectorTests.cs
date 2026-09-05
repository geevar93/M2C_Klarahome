using KlaraHome.Modules.Media.Infrastructure.Validation;

namespace KlaraHome.UnitTests.Media;

/// <summary>
/// The inspector decides what an upload actually is, from its bytes rather than from what the
/// caller called it.
/// </summary>
/// <remarks>
/// These are the tests that stop a marketplace serving a script from its own media domain: the
/// declared content type and the filename both come from the uploader, so neither is evidence.
/// </remarks>
public sealed class FileInspectorTests
{
    [Fact]
    public void A_png_is_identified_and_its_dimensions_read_from_the_header()
    {
        var inspected = FileInspector.Inspect(TestImages.Png(width: 640, height: 480), MediaIntent.Image);

        Assert.NotNull(inspected);
        Assert.Equal("image/png", inspected.ContentType);
        Assert.Equal(640, inspected.Width);
        Assert.Equal(480, inspected.Height);
        Assert.Equal(".png", inspected.Extension);
    }

    [Fact]
    public void A_gif_is_identified_and_its_dimensions_are_little_endian()
    {
        var inspected = FileInspector.Inspect(TestImages.Gif(width: 300, height: 200), MediaIntent.Image);

        Assert.NotNull(inspected);
        Assert.Equal("image/gif", inspected.ContentType);
        Assert.Equal(300, inspected.Width);
        Assert.Equal(200, inspected.Height);
    }

    [Fact]
    public void A_lossy_webp_is_identified_and_its_dimensions_read()
    {
        var inspected = FileInspector.Inspect(TestImages.WebpLossy(width: 1024, height: 768), MediaIntent.Image);

        Assert.NotNull(inspected);
        Assert.Equal("image/webp", inspected.ContentType);
        Assert.Equal(1024, inspected.Width);
        Assert.Equal(768, inspected.Height);
    }

    [Fact]
    public void A_jpeg_frame_header_is_found_by_walking_the_marker_segments()
    {
        var inspected = FileInspector.Inspect(TestImages.Jpeg(width: 1200, height: 900), MediaIntent.Image);

        Assert.NotNull(inspected);
        Assert.Equal("image/jpeg", inspected.ContentType);
        Assert.Equal(1200, inspected.Width);
        Assert.Equal(900, inspected.Height);
    }

    [Fact]
    public void A_script_named_as_an_image_is_refused()
    {
        // The whole point of inspecting content: this arrives as "logo.png", image/png.
        var payload = "<?php system($_GET['c']); ?>"u8.ToArray();

        Assert.Null(FileInspector.Inspect(payload, MediaIntent.Image));
    }

    [Fact]
    public void An_image_is_refused_where_a_document_was_expected()
    {
        Assert.Null(FileInspector.Inspect(TestImages.Png(10, 10), MediaIntent.Document));
    }

    [Fact]
    public void A_pdf_is_the_only_document_type_accepted()
    {
        var inspected = FileInspector.Inspect("%PDF-1.7\nnot really a pdf"u8.ToArray(), MediaIntent.Document);

        Assert.NotNull(inspected);
        Assert.Equal("application/pdf", inspected.ContentType);
        Assert.Equal(".pdf", inspected.Extension);
    }

    [Fact]
    public void A_pdf_is_refused_where_an_image_was_expected()
    {
        Assert.Null(FileInspector.Inspect("%PDF-1.7"u8.ToArray(), MediaIntent.Image));
    }

    [Fact]
    public void An_empty_upload_is_refused_rather_than_read_past_its_end()
    {
        Assert.Null(FileInspector.Inspect([], MediaIntent.Image));
        Assert.Null(FileInspector.Inspect([], MediaIntent.Document));
    }

    [Fact]
    public void A_truncated_png_header_is_refused_rather_than_read_past_its_end()
    {
        var truncated = TestImages.Png(100, 100)[..12];

        Assert.Null(FileInspector.Inspect(truncated, MediaIntent.Image));
    }
}
