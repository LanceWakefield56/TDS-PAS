using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Setup;
using BrightPAS.Modules.Endoscopy;

namespace BrightPAS.Modules.Tests;

/// <summary>0.15.0 endoscopy: structured record, sign hard stops, polyps/histology, quality indicators, surveillance suggestion, media store.</summary>
public class EndoscopyTests
{
    private static DetailsEdit Details(ProcedureKind k, params string[] ind) =>
        new(k, null, "Dr Lindqvist", "RN Kim", null, null, null, false, null, ind, null, true, SedationKind.Conscious, null, true, false, false, false, false);

    private static async Task<(ModulesHost H, AsyncServiceScope S, EndoService Svc, Procedure P)> NewAsync(ProcedureKind kind, string text)
    {
        var h = new ModulesHost();
        var pt = await h.PatientAsync();
        var s = h.Scope();
        var dir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        await s.ServiceProvider.GetRequiredService<SettingsService>().SetAsync(MediaStore.PathKey, dir, "admin");
        var svc = s.ServiceProvider.GetRequiredService<EndoService>();
        var p = await svc.EnsureAsync(Guid.NewGuid(), pt.Id, text, null, "Dr Lindqvist", new DateOnly(2026, 11, 9), "rn");
        Assert.Equal(kind, p.Kind);
        return (h, s, svc, p);
    }

    [Fact]
    public void Procedure_is_guessed_from_the_booking_text()
    {
        Assert.Equal(ProcedureKind.Bidirectional, Terminology.Guess("Gastroscopy and colonoscopy"));
        Assert.Equal(ProcedureKind.Colonoscopy, Terminology.Guess("Colonoscopy with polypectomy"));
        Assert.Equal(ProcedureKind.FlexibleSigmoidoscopy, Terminology.Guess("Flexible sigmoidoscopy"));
        Assert.Equal(ProcedureKind.Gastroscopy, Terminology.Guess("OGD"));
        Assert.Null(Terminology.Guess("Carpal tunnel release"));
    }

    [Fact]
    public async Task Colonoscopy_record_validation_sign_amend_and_histology()
    {
        var (h, s, svc, p) = await NewAsync(ProcedureKind.Colonoscopy, "Colonoscopy");
        await using var _ = h; await using var __ = s;
        Assert.Matches("^E261109\\d{4}$", p.AccessionNumber);
        Assert.StartsWith("2.25.", p.StudyInstanceUid);
        Assert.Contains(await svc.UpdateDetailsAsync(p.Id, Details(ProcedureKind.Colonoscopy, "Dyspepsia"), "rn"), e => e.Contains("not an indication"));
        Assert.Empty(await svc.UpdateDetailsAsync(p.Id, Details(ProcedureKind.Colonoscopy, "PositiveFit"), "rn"));

        var t0 = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13));
        var bad = new ProcedureEdit(null, null, null, null, null, t0, t0.AddMinutes(10), null, t0.AddMinutes(5), LowerExtent.Caecum, true, true, false, true, 4, 2, 2, true, null, 2);
        var (errors, _) = await svc.UpdateProcedureAsync(p.Id, bad, "rn");
        Assert.Contains(errors, e => e.Contains("0–3"));
        Assert.Contains(errors, e => e.Contains("before the caecum reached"));
        var good = bad with { BbpsRight = 2, BbpsLeft = 1, EndedAt = t0.AddMinutes(14) };
        var (e2, warnings) = await svc.UpdateProcedureAsync(p.Id, good, "rn");
        Assert.Empty(e2);
        Assert.Contains(warnings, w => w.Contains("Withdrawal time 4.0"));
        Assert.Contains(warnings, w => w.Contains("inadequate"));
        Assert.Equal(4.0m, EndoService.WithdrawalMinutes((await svc.FindAsync(p.Id))!));

        Assert.NotEmpty((await svc.SavePolypAsync(p.Id, null, new PolypEdit("D2", null, 6, "Is", null, null, "ColdSnare", null, "EnBloc", "Yes", "P1", "Yes", 0, false, null), "rn")).Errors); // upper site on a colonoscopy
        Assert.NotEmpty((await svc.SavePolypAsync(p.Id, null, new PolypEdit("Sigmoid", 25, 6, "Is", null, null, "NotRemoved", null, "EnBloc", "Yes", null, "Yes", 0, false, null), "rn")).Errors);
        var (polyp, pe) = await svc.SavePolypAsync(p.Id, null, new PolypEdit("Sigmoid", 25, 6, "Is", "2", null, "ColdSnare", null, "EnBloc", "Yes", null, "Yes", 0, false, null), "rn");
        Assert.Empty(pe);
        Assert.Equal(1, polyp!.Number);

        var signErr = await svc.SignAsync(p.Id, "dr");
        Assert.Contains(signErr, e => e.Contains("Polyp 1: specimen pot") && e.Contains("Diagnosis"));
        await svc.SavePolypAsync(p.Id, polyp.Id, new PolypEdit("Sigmoid", 25, 6, "Is", "2", null, "ColdSnare", null, "EnBloc", "Yes", "P1", "Yes", 0, false, null), "rn");
        Assert.Empty(await svc.UpdateReportAsync(p.Id, new ReportEdit("None", null, "Single sigmoid polyp removed.", "Await histology.", null, null, null), "rn"));
        Assert.Contains(await svc.UpdateReportAsync(p.Id, new ReportEdit("Bleeding", null, "x", null, null, null, null), "rn"), e => e.Contains("Describe"));
        Assert.Empty(await svc.SignAsync(p.Id, "dr"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateReportAsync(p.Id, new ReportEdit("None", null, "changed", null, null, null, null), "rn"));
        Assert.NotNull(await svc.AmendAsync(p.Id, "", "dr"));
        Assert.Null(await svc.HistologyAsync(polyp.Id, new HistologyEdit("TubularAdenoma", "Low", null), "path")); // allowed after signing
        Assert.Null(await svc.AmendAsync(p.Id, "Typo in conclusion", "dr"));
        Assert.Equal(ProcedureStatus.Completed, (await svc.FindAsync(p.Id))!.Status);
    }

    [Fact]
    public async Task Quality_indicators_and_surveillance_suggestion()
    {
        var (h, s, svc, p) = await NewAsync(ProcedureKind.Colonoscopy, "Colonoscopy +/- polypectomy");
        await using var _ = h; await using var __ = s;
        var t0 = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13));
        await svc.UpdateDetailsAsync(p.Id, Details(ProcedureKind.Colonoscopy, "PositiveFit"), "rn");
        await svc.UpdateProcedureAsync(p.Id, new ProcedureEdit(null, null, null, null, null, t0, t0.AddMinutes(8), null, t0.AddMinutes(17), LowerExtent.Caecum, true, true, false, true, 3, 3, 2, true, null, 4), "rn");
        var (a, _) = await svc.SavePolypAsync(p.Id, null, new PolypEdit("Ascending", null, 12, "Is", "2", null, "HotSnare", null, "EnBloc", "Yes", "P1", "Yes", 1, false, null), "rn");
        await svc.SavePolypAsync(p.Id, null, new PolypEdit("Rectum", 5, 3, "IIa", "1", null, "ColdForceps", null, "EnBloc", "No", null, "Uncertain", 0, false, null), "rn");
        var pr = (await svc.FindAsync(p.Id))!;
        var polyps = new[] { a! };
        Assert.Null(EndoService.SuggestSurveillance(pr, polyps)); // histology awaited
        await svc.HistologyAsync(a!.Id, new HistologyEdit("TubularAdenoma", "Low", null), "path");
        a.Histology = "TubularAdenoma";
        Assert.Contains("3 years", EndoService.SuggestSurveillance(pr, polyps)); // adenoma >= 10 mm
        var rows = await svc.KpisAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        var r = Assert.Single(rows);
        Assert.Equal(1, r.Colonoscopies);
        Assert.Equal(100m, r.CaecalIntubation);
        Assert.Equal(100m, r.Adr);
        Assert.Equal(50m, r.Retrieval);
        Assert.Equal(100m, r.AdequatePrep);
        Assert.Equal(100m, r.ComfortPoor); // Gloucester 4
        Assert.Null(r.MedianWithdrawal); // only polypectomy procedures
    }

    [Fact]
    public async Task Pictures_videos_and_unmatched_queue()
    {
        var (h, s, svc, p) = await NewAsync(ProcedureKind.Gastroscopy, "Gastroscopy");
        await using var _ = h; await using var __ = s;
        var png = Imaging.DemoPicture(1, polyp: true, 64, 48);
        Assert.Equal("image/png", Imaging.Sniff(png));
        Assert.Equal("Only JPEG, PNG or TIFF pictures can be stored.", (await svc.AddImageAsync(p.Id, "hello"u8.ToArray(), MediaSource.Upload, "rn")).Error);
        var (img, err) = await svc.AddImageAsync(p.Id, png, MediaSource.CaptureStation, "rn");
        Assert.Null(err);
        Assert.Equal(64, img!.Sha256!.Length);
        Assert.Contains("site", await svc.UpdateMediaAsync(img.Id, new MediaEdit("Sigmoid-x", null, true, true, null, null, null), "rn") ?? "");
        Assert.Null(await svc.UpdateMediaAsync(img.Id, new MediaEdit("Antrum", "Antrum", true, true, null, null, null), "rn"));

        var (vid, verr) = await svc.StartVideoAsync(p.Id, "video/webm;codecs=vp9", "rn");
        Assert.Null(verr);
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4 };
        Assert.Null(await svc.AppendVideoAsync(vid!.Id, 0, new MemoryStream(webm)));
        Assert.Contains("out of order", await svc.AppendVideoAsync(vid.Id, 5, new MemoryStream(webm)));
        Assert.Null(await svc.AppendVideoAsync(vid.Id, 1, new MemoryStream([9, 9])));
        Assert.Null(await svc.AppendVideoAsync(vid.Id, 1, new MemoryStream([9, 9]))); // a retried chunk is ignored
        Assert.Null(await svc.FinishVideoAsync(vid.Id, 12.34m, "rn"));
        var media = await svc.MediaAsync(p.Id);
        Assert.Equal(2, media.Count);
        var v = media.Single(m => m.Kind == MediaKind.Video);
        Assert.Equal(10, v.Bytes);
        Assert.Equal(12.3m, v.DurationSeconds);
        Assert.Contains("Videos cannot", await svc.UpdateMediaAsync(v.Id, new MediaEdit(null, null, true, false, null, null, null), "rn"));

        var (orphan, oerr) = await svc.AddImageAsync(null, png, MediaSource.Dicom, "tower", new DicomImageInfo("1.2.3.4", "ZZZ0001", "TEST PATIENT", null, null));
        Assert.Null(oerr);
        Assert.Single(await svc.UnmatchedAsync());
        Assert.Equal(orphan!.Id, (await svc.AddImageAsync(null, png, MediaSource.Dicom, "tower", new DicomImageInfo("1.2.3.4", null, null, null, null))).Row!.Id); // same SOP UID stored once
        Assert.Null(await svc.AssignAsync(orphan.Id, p.Id, "rn"));
        Assert.Empty(await svc.UnmatchedAsync());
        Assert.Equal(3, (await svc.MediaAsync(p.Id)).Count);

        Assert.NotNull(await svc.DeleteMediaAsync(img.Id, "", "rn"));
        Assert.Null(await svc.DeleteMediaAsync(img.Id, "Blurred", "rn"));
        Assert.Equal(2, (await svc.MediaAsync(p.Id)).Count);
        Assert.Null(await s.ServiceProvider.GetRequiredService<MediaStore>().PathIfExistsAsync(img));
    }

    [Fact]
    public async Task Mpps_sets_times_and_gastroscopy_needs_its_dataset()
    {
        var (h, s, svc, p) = await NewAsync(ProcedureKind.Gastroscopy, "Gastroscopy");
        await using var _ = h; await using var __ = s;
        var t0 = h.Clock.GetUtcNow();
        await svc.MppsAsync(p.AccessionNumber, "1.2.840.99", false, t0);
        await svc.MppsAsync(p.AccessionNumber, "1.2.840.99", true, t0.AddMinutes(8));
        var x = (await svc.FindAsync(p.Id))!;
        Assert.Equal(8.0m, EndoService.UpperMinutes(x));
        Assert.Equal(ProcedureStatus.InProgress, x.Status);
        var missing = await svc.MissingAsync(x);
        Assert.Contains("Gastroscopy: extent reached", missing);
        Assert.Contains("Gastroscopy: retroflexion (J-manoeuvre)", missing);
        Assert.DoesNotContain(missing, m => m.StartsWith("Colonoscopy", StringComparison.Ordinal));
        Assert.NotEmpty((await svc.SaveFindingAsync(p.Id, null, new FindingEdit("LowerOesophagus", "Oesophagitis", "LA Z", null, null), "rn")).Errors);
        Assert.Empty((await svc.SaveFindingAsync(p.Id, null, new FindingEdit("LowerOesophagus", "Oesophagitis", "LA B", null, null), "rn")).Errors);
    }

    [Theory]
    [InlineData("t-lzw.tif", 3)]
    [InlineData("t-pb.tif", 3)]
    [InlineData("t-grey.tif", 1)]
    public void Tiff_pictures_decode_exactly(string file, int channels)
    {
        var (w, h, ch, px) = Tiff.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        Assert.Equal((37, 23, channels), (w, h, ch));
        int x = 30, y = 17;
        if (ch == 3) Assert.Equal(new byte[] { (byte)(x * 7 % 256), (byte)(y * 11 % 256), (byte)((x + y) * 5 % 256) }, px.AsSpan((y * w + x) * 3, 3).ToArray());
        else Assert.InRange(px[y * w + x], 1, 255);
        var rgb = Enumerable.Range(0, 4 * 3 * 3).Select(i => (byte)(i * 9)).ToArray();
        var round = Tiff.Decode(Tiff.Write(4, 3, rgb));
        Assert.Equal(rgb, round.Pixels);
    }

    [Fact]
    public async Task Tiff_upload_annotation_and_nbsp_dataset()
    {
        var (h, s, svc, p) = await NewAsync(ProcedureKind.Colonoscopy, "Colonoscopy");
        await using var _ = h; await using var __ = s;
        var tif = Tiff.Write(8, 6, Enumerable.Repeat((byte)200, 8 * 6 * 3).ToArray());
        var (m, err) = await svc.AddImageAsync(p.Id, tif, MediaSource.Upload, "rn");
        Assert.Null(err);
        Assert.Equal("image/png", m!.ContentType);
        Assert.Equal("image/tiff", m.OriginalContentType);
        var store = s.ServiceProvider.GetRequiredService<MediaStore>();
        Assert.Equal(tif, await File.ReadAllBytesAsync((await store.OriginalPathAsync(m))!));
        Assert.NotNull(Tiff.ToPng([.. tif[..8], .. new byte[200]]).Error); // damaged/unsupported → plain error, no crash

        await svc.UpdateMediaAsync(m.Id, new MediaEdit("Caecum", "Caecum", true, true, null, null, null), "rn");
        Assert.NotNull((await svc.AnnotateAsync(m.Id, "nope"u8.ToArray(), "rn")).Error);
        var (copy, aerr) = await svc.AnnotateAsync(m.Id, Imaging.DemoPicture(3, false, 16, 12), "dr");
        Assert.Null(aerr);
        var media = await svc.MediaAsync(p.Id);
        var orig = media.Single(x => x.Id == m.Id);
        var ann = media.Single(x => x.Id == copy!.Id);
        Assert.Equal(m.Id, ann.AnnotatedFromId);
        Assert.True(ann.InReport && ann.InPatientReport);
        Assert.False(orig.InReport || orig.InPatientReport);
        Assert.Equal("Caecum (annotated)", ann.Caption);

        await svc.UpdateDetailsAsync(p.Id, Details(ProcedureKind.Colonoscopy, "PositiveFit") with { NbspScreening = true }, "rn");
        var (polyp, _) = await svc.SavePolypAsync(p.Id, null, new PolypEdit("Sigmoid", 30, 12, "Is", "2", null, "HotSnare", null, "EnBloc", "Yes", "P1", "Yes", 0, false, null), "rn");
        var row = Assert.Single(await svc.NbspAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
        Assert.Contains("NBSP participant number missing", row.Problems);
        Assert.Contains(row.Problems, x => x.Contains("histology awaited for 1"));
        await svc.HistologyAsync(polyp!.Id, new HistologyEdit("TubularAdenoma", "Low", null), "path");
        row = Assert.Single(await svc.NbspAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
        Assert.Equal((1, 1, 12), (row.Adenomas, row.AdvancedAdenomas, row.LargestPolypMm!.Value)); // >= 10 mm = advanced
        await svc.MarkNbspExportedAsync([p.Id], "dr");
        Assert.NotNull((await svc.NbspAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)))[0].ExportedAt);
    }
}
