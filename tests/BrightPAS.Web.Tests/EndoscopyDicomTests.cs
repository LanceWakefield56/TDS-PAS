using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Web.Integration;

namespace BrightPAS.Web.Tests;

/// <summary>0.15.0: pictures from an Olympus tower arrive as DICOM – JPEG baseline frames are kept, uncompressed RGB becomes PNG, MPEG-4 becomes MP4.</summary>
[Collection("fo-dicom")]
public class EndoscopyDicomTests
{
    public EndoscopyDicomTests()
    {
        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        FellowOakDicom.IServiceCollectionExtension.AddFellowOakDicom(sc);
        DicomSetupBuilder.UseServiceProvider(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(sc));
    }

    private static DicomDataset Base(DicomTransferSyntax ts) => new(ts)
    {
        { DicomTag.SOPClassUID, DicomUID.VLEndoscopicImageStorage }, { DicomTag.SOPInstanceUID, EndoService.Uid() },
        { DicomTag.PatientID, "ZZZ0016" }, { DicomTag.AccessionNumber, "E2611090001" },
    };

    [Fact]
    public void Rgb_becomes_png()
    {
        var ds = Base(DicomTransferSyntax.ExplicitVRLittleEndian);
        ds.Add(DicomTag.PhotometricInterpretation, PhotometricInterpretation.Rgb.Value);
        ds.Add(DicomTag.Rows, (ushort)2); ds.Add(DicomTag.Columns, (ushort)2);
        ds.Add(DicomTag.BitsAllocated, (ushort)8); ds.Add(DicomTag.BitsStored, (ushort)8); ds.Add(DicomTag.HighBit, (ushort)7);
        ds.Add(DicomTag.SamplesPerPixel, (ushort)3); ds.Add(DicomTag.PlanarConfiguration, (ushort)0); ds.Add(DicomTag.PixelRepresentation, (ushort)0);
        var px = DicomPixelData.Create(ds, true);
        px.AddFrame(new MemoryByteBuffer([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]));
        var (bytes, video) = EndoDicomScp.Extract(ds);
        Assert.False(video);
        Assert.Equal("image/png", Imaging.Sniff(bytes));
    }

    [Fact]
    public void Jpeg_frame_is_kept_and_mp4_is_video()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 0xFF, 0xD9 };
        var ds = Base(DicomTransferSyntax.JPEGProcess1);
        foreach (var (t, v) in new[] { (DicomTag.Rows, (ushort)1), (DicomTag.Columns, (ushort)1), (DicomTag.BitsAllocated, (ushort)8), (DicomTag.BitsStored, (ushort)8), (DicomTag.HighBit, (ushort)7), (DicomTag.SamplesPerPixel, (ushort)3), (DicomTag.PixelRepresentation, (ushort)0) })
            ds.Add(t, v);
        ds.Add(DicomTag.PhotometricInterpretation, "YBR_FULL_422");
        DicomPixelData.Create(ds, true).AddFrame(new MemoryByteBuffer(jpeg.Length % 2 == 0 ? jpeg : [.. jpeg, 0]));
        var (bytes, video) = EndoDicomScp.Extract(ds);
        Assert.False(video);
        Assert.Equal("image/jpeg", Imaging.Sniff(bytes));

        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0 };
        var vds = Base(DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41);
        foreach (var (t, v) in new[] { (DicomTag.Rows, (ushort)1080), (DicomTag.Columns, (ushort)1920), (DicomTag.BitsAllocated, (ushort)8), (DicomTag.BitsStored, (ushort)8), (DicomTag.HighBit, (ushort)7), (DicomTag.SamplesPerPixel, (ushort)3), (DicomTag.PixelRepresentation, (ushort)0) })
            vds.Add(t, v);
        vds.Add(DicomTag.PhotometricInterpretation, "YBR_PARTIAL_420");
        DicomPixelData.Create(vds, true).AddFrame(new MemoryByteBuffer(mp4));
        var (vb, isVideo) = EndoDicomScp.Extract(vds);
        Assert.True(isVideo);
        Assert.Equal("video/mp4", Imaging.Sniff(vb));
    }

    [Fact]
    public void No_pixels_is_refused() => Assert.Null(EndoDicomScp.Extract(Base(DicomTransferSyntax.ExplicitVRLittleEndian)).Bytes);
}
