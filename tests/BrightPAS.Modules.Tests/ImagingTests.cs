using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Pacs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>0.37.0 Imaging (PACS): reading and drawing DICOM (16-bit, window, MONOCHROME1, signed CT values), storing once, matching by request
/// accession number and by NHI/MRN + date of birth, filing by a person (date of birth check), episodes, requests and worklist, delete and merge.</summary>
public sealed class ImagingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "brightpas-imaging-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static readonly DateOnly Dob = new(1980, 5, 17);
    private static DemoDicom.Patient Who(string id, DateOnly? dob = null) => new(id, "NGATAI^Aroha Mere", dob ?? Dob, "F");
    private static byte[] Xray(DemoDicom.Patient p, string? acc = null, string? study = null, string? series = null, int instance = 1) =>
        DemoDicom.Xray(p, study ?? ImagingDicom.NewUid(), series ?? ImagingDicom.NewUid(), acc, new DateOnly(2026, 11, 17), "DX", "KNEE", "Left knee", instance, instance, 64, 48);

    private async Task<ImagingService> SvcAsync(IServiceProvider sp)
    {
        var svc = sp.GetRequiredService<ImagingService>();
        Assert.Empty(await svc.SaveSettingsAsync(new(_dir, null, true), "admin"));
        return svc;
    }

    private static async Task<Guid> BookingAsync(IServiceProvider sp, Guid patientId, DateOnly date)
    {
        var th = sp.GetRequiredService<TheatreService>();
        var loc = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var (session, err) = await th.CreateSessionAsync(new(loc.Id, date, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        Assert.Null(err);
        return (await th.BookAsync(new(session!.Id, patientId, "Arthroscopy left knee", Laterality.Left, 60, AnaestheticType.General, null, false, PaymentType.SelfFunded), "rec")).Booking!.Id;
    }

    [Fact]
    public void Renders_16_bit_greyscale_with_window_and_scales_down()
    {
        var bytes = DemoDicom.Xray(Who("ZSC21TN"), ImagingDicom.NewUid(), ImagingDicom.NewUid(), null, new DateOnly(2026, 2, 28), "CR", "CHEST", "Chest", 1, 1, 400, 300);
        var file = ImagingDicom.Open(bytes);
        var f = ImagingDicom.Facts(file);
        Assert.Equal((300, 400, 1, "MONOCHROME2"), (f.Rows, f.Columns, f.Frames, f.Photometric));
        Assert.Equal(new DateOnly(1980, 5, 17), f.BirthDate);
        Assert.Equal("NGATAI Aroha Mere", f.PatientName);
        Assert.Equal(1100, f.WindowCentre);
        Assert.True(ImagingDicom.CanRender(file));

        var r = ImagingDicom.Render(file.Dataset, 0, null, null, false, 200)!;
        Assert.Equal(("image/png", 200, 150, true, 1100.0, 2000.0), (r.ContentType, r.Width, r.Height, r.Adjustable, r.Centre, r.Width2));
        Assert.Equal(0x89, r.Bytes[0]);
        // A narrow window around a low value makes the picture almost all white.
        var narrow = ImagingDicom.Render(file.Dataset, 0, 10, 2, false, 200)!;
        Assert.Equal((10.0, 2.0), (narrow.Centre, narrow.Width2));
        Assert.Null(ImagingDicom.Render(file.Dataset, 3, null, null, false, 200)); // no such frame
    }

    [Fact]
    public void Window_maths_signed_values_rescale_and_monochrome1()
    {
        // 2 x 1 picture, signed 16-bit CT values -1000 (air) and +1000 (bone) with rescale intercept 0.
        static DicomDataset Ct(string photometric)
        {
            var ds = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
            {
                { DicomTag.SOPClassUID, DicomUID.CTImageStorage }, { DicomTag.SOPInstanceUID, ImagingDicom.NewUid() },
                { DicomTag.SamplesPerPixel, (ushort)1 }, { DicomTag.PhotometricInterpretation, photometric }, { DicomTag.Rows, (ushort)1 }, { DicomTag.Columns, (ushort)2 },
                { DicomTag.BitsAllocated, (ushort)16 }, { DicomTag.BitsStored, (ushort)16 }, { DicomTag.HighBit, (ushort)15 }, { DicomTag.PixelRepresentation, (ushort)1 },
                { DicomTag.RescaleSlope, "1" }, { DicomTag.RescaleIntercept, "0" },
            };
            var px = new byte[4];
            BitConverter.TryWriteBytes(px.AsSpan(0, 2), (short)-1000);
            BitConverter.TryWriteBytes(px.AsSpan(2, 2), (short)1000);
            DicomPixelData.Create(ds, true).AddFrame(new FellowOakDicom.IO.Buffer.MemoryByteBuffer(px));
            return ds;
        }
        static byte[] Pixels(RenderedImage r)
        {
            // Decode our own uncompressed-filter PNG: IDAT is zlib; rows start with filter byte 0.
            var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(r.Bytes.AsSpan(33));
            using var z = new System.IO.Compression.ZLibStream(new MemoryStream(r.Bytes, 33 + 8, len), System.IO.Compression.CompressionMode.Decompress);
            using var ms = new MemoryStream(); z.CopyTo(ms);
            return ms.ToArray()[1..];
        }
        var m2 = ImagingDicom.Render(Ct("MONOCHROME2"), 0, 0, 2000, false, 64)!;
        Assert.Equal((0.0, 2000.0), (m2.Centre, m2.Width2));
        var p = Pixels(m2);
        Assert.True(p[0] < 5 && p[1] > 250, $"air dark, bone bright: {p[0]} {p[1]}");
        var inv = Pixels(ImagingDicom.Render(Ct("MONOCHROME2"), 0, 0, 2000, true, 64)!);
        Assert.True(inv[0] > 250 && inv[1] < 5);
        var m1 = Pixels(ImagingDicom.Render(Ct("MONOCHROME1"), 0, 0, 2000, false, 64)!);
        Assert.True(m1[0] > 250 && m1[1] < 5, "MONOCHROME1 is drawn the other way round");
        // Bone window (500/2000): air is black.
        Assert.True(Pixels(ImagingDicom.Render(Ct("MONOCHROME2"), 0, 500, 2000, false, 64)!)[0] < 5);
    }

    [Fact]
    public async Task Stores_once_files_by_accession_or_nhi_and_birth_date_and_queues_the_rest()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = await SvcAsync(sp);
        var p = await h.PatientAsync();
        var booking = await BookingAsync(sp, p.Id, new DateOnly(2026, 11, 17));

        // Request → accession number; pictures with it are filed to the patient and episode, the request is done.
        Assert.Contains("Choose the type of imaging (e.g. DX X-ray, CT, US).", (await svc.CreateRequestAsync(new(p.Id, null, "ZZ", "Knee", null, null, null), "nurse")).Errors);
        var (req, errors) = await svc.CreateRequestAsync(new(p.Id, booking, "dx", "Left knee AP and lateral", "knee", "Pain after fall", new DateOnly(2026, 11, 16)), "nurse");
        Assert.Empty(errors);
        Assert.Equal("X2611090001", req!.AccessionNumber);
        Assert.Equal(("DX", "KNEE"), (req.Modality, req.BodyPart));
        var wl = Assert.Single(await svc.WorklistAsync(new DateOnly(2026, 11, 17), "DX"));
        Assert.Equal(("ZSC21TN", "NGĀTAI^Aroha Mere", "X2611090001"), (wl.PatientId, wl.PatientName, wl.AccessionNumber));

        var study = ImagingDicom.NewUid(); var series = ImagingDicom.NewUid();
        var first = Xray(Who("SOMEONE-ELSE", new DateOnly(1999, 1, 1)), req.AccessionNumber, study, series, 1);
        var r1 = await svc.StoreAsync(first, ImageSource.Upload, "upload by nurse");
        Assert.True(r1.Stored && r1.Filed && r1.Error is null);
        var r2 = await svc.StoreAsync(Xray(Who("SOMEONE-ELSE"), req.AccessionNumber, study, series, 2), ImageSource.Upload, "upload by nurse");
        Assert.Equal(r1.StudyId, r2.StudyId);
        var dup = await svc.StoreAsync(first, ImageSource.Folder, "watched folder");
        Assert.True(dup.Duplicate && !dup.Stored);
        var db = sp.GetRequiredService<ImagingDbContext>();
        var s1 = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == r1.StudyId);
        Assert.Equal((StudyStatus.Filed, p.Id, booking, MatchMethod.Accession, 2), (s1.Status, s1.PatientId, s1.BookingId, s1.MatchedBy, s1.ImageCount));
        Assert.Equal((RequestStatus.Done, s1.Id), await db.Requests.Where(r => r.Id == req.Id).Select(r => ValueTuple.Create(r.Status, r.StudyId!.Value)).SingleAsync());
        Assert.Empty(await svc.WorklistAsync(null, null));
        var img = await db.Images.AsNoTracking().FirstAsync(i => i.StudyId == s1.Id);
        Assert.True(img.Viewable);
        Assert.Equal(64, img.Sha256.Length);
        Assert.True(File.Exists(Path.Combine(_dir, img.FileName)));
        var rendered = await svc.RenderAsync(img.Id, 0, null, null, false, 32);
        Assert.Equal(32, rendered!.Width);

        // NHI + date of birth: filed automatically, and to the only booking that day.
        var r3 = await svc.StoreAsync(Xray(Who("zsc21tn")), ImageSource.Folder, "watched folder");
        var s3 = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == r3.StudyId);
        Assert.Equal((StudyStatus.Filed, p.Id, booking, MatchMethod.PatientIdAndBirthDate), (s3.Status, s3.PatientId, s3.BookingId, s3.MatchedBy));
        // MRN works too.
        var r3b = await svc.StoreAsync(Xray(Who(p.Mrn)), ImageSource.Folder, "watched folder");
        Assert.True(r3b.Filed);

        // Right NHI, wrong date of birth: not filed – a person must check.
        var r4 = await svc.StoreAsync(Xray(Who("ZSC21TN", new DateOnly(1980, 5, 18))), ImageSource.Folder, "watched folder");
        Assert.False(r4.Filed);
        // Unknown ID: waits.
        var r5 = await svc.StoreAsync(Xray(Who("EXT-1")), ImageSource.Upload, "upload");
        Assert.False(r5.Filed);
        Assert.Equal(2, await svc.UnmatchedCountAsync());
        Assert.Equal(2, (await svc.StudiesAsync("unmatched", null, null)).Count);
        Assert.Equal(3, (await svc.StudiesAsync(null, p.Id, null)).Count);

        // Automatic filing can be switched off (only BrightPAS requests then file by themselves).
        Assert.Empty(await svc.SaveSettingsAsync(new(_dir, null, false), "admin"));
        Assert.False((await svc.StoreAsync(Xray(Who("ZSC21TN")), ImageSource.Folder, "watched folder")).Filed);

        // Not DICOM, or a DICOMDIR.
        Assert.True((await svc.StoreAsync([1, 2, 3], ImageSource.Upload, "u")).NotUnderstood);
    }

    [Fact]
    public async Task Person_files_with_birth_date_check_links_episode_reports_deletes_and_merge_moves()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = await SvcAsync(sp);
        var p = await h.PatientAsync();
        var other = await h.PatientAsync("Smith", "John", "ZAA0024", new DateOnly(1950, 1, 1));
        var booking = await BookingAsync(sp, p.Id, new DateOnly(2026, 11, 20));
        var r = await svc.StoreAsync(Xray(Who("EXT-9", new DateOnly(1980, 5, 18))), ImageSource.Upload, "upload");
        var id = r.StudyId!.Value;

        var errs = await svc.FileAsync(id, p.Id, null, false, "nurse");
        Assert.Contains("is not this patient's", Assert.Single(errs));
        Assert.Contains("not this patient's", Assert.Single(await svc.FileAsync(id, p.Id, Guid.NewGuid(), true, "nurse")));
        Assert.Empty(await svc.FileAsync(id, p.Id, null, true, "nurse"));
        var db = sp.GetRequiredService<ImagingDbContext>();
        Assert.Equal((StudyStatus.Filed, MatchMethod.Person, "nurse"), await db.Studies.Where(s => s.Id == id).Select(s => ValueTuple.Create(s.Status, s.MatchedBy!.Value, s.FiledBy!)).SingleAsync());

        Assert.Equal("That episode is not this patient's.", await svc.LinkEpisodeAsync(id, await BookingAsync(sp, other.Id, new DateOnly(2026, 11, 21)), "nurse"));
        Assert.Null(await svc.LinkEpisodeAsync(id, booking, "nurse"));
        Assert.Single(await svc.StudiesAsync(null, null, booking));
        Assert.Contains(await svc.PatientEpisodesAsync(p.Id), e => e.BookingId == booking);
        Assert.Null(await svc.SaveReportAsync(id, "No fracture.", "hreid"));
        Assert.NotNull(await svc.StudyAsync(id, "hreid"));
        var export = await svc.ExportFilesAsync(id);
        Assert.Single(export!.Value.Files);

        // Merge moves studies and requests.
        Assert.Empty((await svc.CreateRequestAsync(new(p.Id, null, "US", "Ultrasound abdomen", null, null, null), "nurse")).Errors);
        Assert.Equal(2, await svc.MergeAsync(other.Id, p.Id));
        Assert.Equal(other.Id, await db.Studies.Where(s => s.Id == id).Select(s => s.PatientId).SingleAsync());

        // Delete removes the files, keeps who/why, and the same study is not taken in again.
        Assert.Equal("Say why the study is being deleted.", await svc.DeleteAsync(id, " ", "admin"));
        var file = Path.Combine(_dir, (await db.Images.AsNoTracking().FirstAsync(i => i.StudyId == id)).FileName);
        Assert.True(File.Exists(file));
        Assert.Null(await svc.DeleteAsync(id, "Wrong patient's CD", "admin"));
        Assert.False(File.Exists(file));
        Assert.Null(await svc.StudyAsync(id, "hreid"));
        Assert.Empty(await svc.StudiesAsync(null, other.Id, null));
    }

    [Fact]
    public async Task Settings_need_full_paths()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<ImagingService>();
        Assert.Equal(2, (await svc.SaveSettingsAsync(new("relative\\path", "also-relative", true), "admin")).Count);
        var watched = Path.Combine(_dir, "in");
        Assert.Empty(await svc.SaveSettingsAsync(new(_dir, watched, true), "admin"));
        var s = await svc.SettingsAsync();
        Assert.Equal((_dir, watched, true), (s.StoragePath, s.ImportFolder, s.AutoFile));
        Assert.Contains("cannot be the imaging folder", Assert.Single(await svc.SaveSettingsAsync(new(_dir, _dir, true), "admin")));
    }
}
