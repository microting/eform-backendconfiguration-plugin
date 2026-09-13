/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
namespace BackendConfiguration.Pn.Services.WordService;

using ImageMagick;

/// <summary>
/// The one rule both Word generators embed photographs by (#1219).
///
/// <para>
/// Shared rather than copied because <c>WordService.InsertImage</c> and
/// <c>ComplianceExportWordWriter.InsertImage</c> are already the same method
/// twice over — the compliance writer's own doc comment says so — and a
/// format/mime table that drifts between them is exactly the class of bug this
/// fixes.
/// </para>
/// </summary>
internal static class WordImageEmbedding
{
    /// <summary>
    /// The format an image is encoded in for its <c>data:</c> URI, together with
    /// the mime that must be declared for it. The two are one decision, and are
    /// returned together so a caller cannot take one without the other:
    /// HtmlToOpenXml derives the OOXML image part type from the DECLARED mime
    /// (<c>ImagePrefetcher.ReadDataUri</c> → <c>TryInspectMimeType</c>), never
    /// from the bytes, so a declaration that does not match its payload produces
    /// a malformed package — historically a <c>/word/media/imageN.png</c> part,
    /// content type <c>image/png</c>, holding JPEG.
    ///
    /// <para>
    /// <b>Why not re-encode everything to PNG.</b> That would make a hardcoded
    /// <c>image/png</c> declaration true, but PNG is lossless: a photograph is
    /// several times larger as PNG than as JPEG, and a compliance appendix is
    /// nothing but photographs across as many as 200 pages. The source format is
    /// therefore kept whenever it is one HtmlToOpenXml knows — a JPEG out of the
    /// picture store stays a JPEG, and the exported file does not grow at all.
    /// </para>
    ///
    /// <para>
    /// <b>Why not just declare <c>image.Format</c>'s mime.</b> Because an
    /// unrecognised one does not degrade gracefully. <c>ReadDataUri</c> calls
    /// <c>TryInspectMimeType</c> and IGNORES its <c>false</c> return, handing
    /// <c>default(PartTypeInfo)</c> to <c>AddImagePart</c>, and the data-URI arm
    /// has no byte-sniffing fallback (only the HTTP arm does). WebP and HEIC —
    /// ordinary phone-camera output — are both absent from that table. Anything
    /// outside the allow-list below is re-encoded to PNG, which every consumer
    /// understands; that costs size only for the formats that would otherwise be
    /// undefined.
    /// </para>
    /// </summary>
    /// <param name="sourceFormat">
    /// The decoded image's own <see cref="IMagickImage.Format"/>. Note that the
    /// returned format must be passed to <c>ToBase64(MagickFormat)</c>: the
    /// parameterless overload encodes in the image's CURRENT format
    /// (<c>ToByteArray()</c> writes through the image's settings), which is what
    /// let JPEG bytes ship under an <c>image/png</c> declaration.
    /// </param>
    public static (MagickFormat Format, string MimeType) ResolveEmbedFormat(MagickFormat sourceFormat)
        => sourceFormat switch
        {
            MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.Pjpeg => (MagickFormat.Jpeg, "image/jpeg"),
            MagickFormat.Png or MagickFormat.Png00 or MagickFormat.Png8 or MagickFormat.Png24
                or MagickFormat.Png32 or MagickFormat.Png48 or MagickFormat.Png64 => (MagickFormat.Png, "image/png"),
            MagickFormat.Gif or MagickFormat.Gif87 => (MagickFormat.Gif, "image/gif"),
            MagickFormat.Bmp or MagickFormat.Bmp2 or MagickFormat.Bmp3 => (MagickFormat.Bmp, "image/bmp"),
            MagickFormat.Tif or MagickFormat.Tiff or MagickFormat.Tiff64 => (MagickFormat.Tiff, "image/tiff"),
            _ => (MagickFormat.Png, "image/png")
        };
}
