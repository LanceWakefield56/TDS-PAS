using System.Net;
using System.Net.Sockets;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Network;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Setup;
using BrightPAS.Licensing;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Api;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

public sealed record EndoDicomStatus(bool Enabled, bool Listening, int Port, string AeTitle, string? Error, DateTimeOffset? LastActivity, int Images, int Videos, int Worklist, int Unmatched);

/// <summary>
/// The endoscopy DICOM node for the Olympus towers (and any video processor or capture box that speaks DICOM):
/// Verification (C-ECHO), Modality Worklist (C-FIND – the tower shows today's patients, so pictures carry the right accession number),
/// Storage (C-STORE – JPEG, uncompressed RGB/greyscale and MPEG-4 video become pictures/videos on the procedure) and
/// Modality Performed Procedure Step (N-CREATE/N-SET – start and end times). Devices are accepted by tower IP address
/// (Assets › endoscopy tower › network address) or by AE title; "accept any device" is for testing only.
/// It also deletes videos older than the retention setting, when one is set.
/// </summary>
public sealed class EndoscopyDicomHost(IServiceScopeFactory scopes, ILogger<EndoscopyDicomHost> log, TimeProvider clock) : BackgroundService
{
    private readonly object _gate = new();
    private IDicomServer? _server;
    private EndoDicomStatus _status = new(false, false, 0, "", null, null, 0, 0, 0, 0);
    private readonly SemaphoreSlim _reload = new(0);
    public EndoDicomStatus Status { get { lock (_gate) return _status; } }
    internal void Update(Func<EndoDicomStatus, EndoDicomStatus> f) { lock (_gate) _status = f(_status); }
    public void Reload() => _reload.Release();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ContinueWith(_ => { }, CancellationToken.None);
        var nextClean = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try { await ConfigureAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Endoscopy DICOM node could not be configured."); }
            if (clock.GetUtcNow() > nextClean)
            {
                try { await RetentionAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Endoscopy video retention clean-up failed."); }
                nextClean = clock.GetUtcNow().AddHours(6);
            }
            await _reload.WaitAsync(TimeSpan.FromMinutes(5), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
        Stop();
    }

    private string _signature = "";

    private async Task ConfigureAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var licensed = sp.GetRequiredService<LicenceCache>().Status.AccessTo(EndoscopyModule.Id) == ModuleAccess.Full;
        var s = await EndoscopyEndpoints.SettingsAsync(sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<MediaStore>());
        var ips = new List<IPAddress>();
        if (sp.GetRequiredService<LicenceCache>().Status.AccessTo("assets") != ModuleAccess.None)
            foreach (var a in await sp.GetRequiredService<BrightPAS.Modules.Assets.AssetsDbContext>().Equipment.AsNoTracking()
                         .Where(e => e.Category == BrightPAS.Modules.Assets.EquipmentCategory.EndoscopyTower && e.NetworkAddress != null).Select(e => e.NetworkAddress!).ToListAsync(ct))
                if (IPAddress.TryParse(a.Split(':')[0].Trim(), out var ip)) ips.Add(ip);
        var aes = (s.DicomCallingAes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).ToHashSet();
        var on = licensed && s.DicomEnabled;
        var sig = $"{on}|{s.DicomPort}|{s.DicomAeTitle}|{s.DicomAnyTower}|{string.Join(',', aes.Order())}|{string.Join(',', ips.Select(i => i.ToString()).Order())}";
        if (sig == _signature && (_server is not null || !on)) return;
        Stop();
        _signature = sig;
        Update(x => x with { Enabled = on, Listening = false, Port = s.DicomPort, AeTitle = s.DicomAeTitle, Error = null });
        if (!on) return;
        try
        {
            var ctx = new EndoScpContext(s.DicomAeTitle, aes, ips, s.DicomAnyTower, scopes, this, log);
            var server = DicomServerFactory.Create<EndoDicomScp>(s.DicomPort, userState: ctx);
            if (server.Exception is { } ex) throw ex;
            lock (_gate) _server = server;
            Update(x => x with { Listening = true });
            log.LogInformation("Endoscopy DICOM node {Ae} listening on port {Port} ({Towers} tower address(es), {Aes} AE title(s){Any}).", s.DicomAeTitle, s.DicomPort, ips.Count, aes.Count, s.DicomAnyTower ? ", ANY device" : "");
        }
        catch (Exception ex) when (ex is SocketException or DicomNetworkException or InvalidOperationException)
        {
            Update(x => x with { Error = $"Could not listen on port {s.DicomPort}: {ex.Message} (a Messaging DICOM interface or another program may be using this port – choose another)." });
            log.LogError(ex, "Endoscopy DICOM node could not start.");
        }
    }

    private void Stop()
    {
        lock (_gate) { _server?.Dispose(); _server = null; }
        Update(x => x with { Listening = false });
    }

    private async Task RetentionAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (!int.TryParse(await sp.GetRequiredService<SettingsService>().GetAsync(EndoscopyEndpoints.RetentionKey, ct), out var days) || days < 30) return;
        var cutoff = clock.GetUtcNow().AddDays(-days);
        var db = sp.GetRequiredService<EndoDbContext>();
        var old = await db.Media.Where(m => m.Kind == MediaKind.Video && m.Status == MediaStatus.Ready && m.CapturedAt < cutoff).Select(m => m.Id).Take(200).ToListAsync(ct);
        var svc = sp.GetRequiredService<EndoService>();
        foreach (var id in old) await svc.DeleteMediaAsync(id, $"Video retention ({days} days)", "system", ct);
        if (old.Count > 0) log.LogInformation("Endoscopy: deleted {Count} video(s) older than {Days} days.", old.Count, days);
    }

    public override void Dispose() { Stop(); base.Dispose(); }
}

public sealed record EndoScpContext(string AeTitle, HashSet<string> CallingAes, List<IPAddress> TowerIps, bool AnyDevice, IServiceScopeFactory Scopes, EndoscopyDicomHost Host, ILogger Log);

public sealed class EndoDicomScp(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
    : DicomService(stream, fallbackEncoding, log, dependencies), IDicomServiceProvider, IDicomCEchoProvider, IDicomCFindProvider, IDicomCStoreProvider, IDicomNServiceProvider
{
    private EndoScpContext Ctx => (EndoScpContext)UserState;
    private static readonly DicomTransferSyntax[] StoreSyntaxes =
    [
        DicomTransferSyntax.JPEGProcess1, DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41, DicomTransferSyntax.MPEG4AVCH264BDCompatibleHighProfileLevel41,
        DicomTransferSyntax.ExplicitVRLittleEndian, DicomTransferSyntax.ImplicitVRLittleEndian,
    ];

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        var ctx = Ctx;
        var ip = IPAddress.TryParse(association.RemoteHost, out var a) ? a.MapToIPv4() : null;
        if (!string.Equals(association.CalledAE?.Trim(), ctx.AeTitle, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Log.LogWarning("Endoscopy DICOM: refused association called {Called} (expected {Ae}).", association.CalledAE, ctx.AeTitle);
            return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CalledAENotRecognized);
        }
        var known = ctx.AnyDevice || (ip is not null && ctx.TowerIps.Any(t => t.MapToIPv4().Equals(ip))) || ctx.CallingAes.Contains((association.CallingAE ?? "").Trim().ToUpperInvariant());
        if (!known)
        {
            ctx.Log.LogWarning("Endoscopy DICOM: refused unknown device {Calling} at {Ip}.", association.CallingAE, ip);
            return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CallingAENotRecognized);
        }
        foreach (var pc in association.PresentationContexts)
        {
            if (pc.AbstractSyntax == DicomUID.Verification || pc.AbstractSyntax == DicomUID.ModalityWorklistInformationModelFind || pc.AbstractSyntax == DicomUID.ModalityPerformedProcedureStep)
                pc.AcceptTransferSyntaxes(DicomTransferSyntax.ExplicitVRLittleEndian, DicomTransferSyntax.ImplicitVRLittleEndian);
            else if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None) pc.AcceptTransferSyntaxes(StoreSyntaxes);
        }
        ctx.Host.Update(x => x with { LastActivity = DateTimeOffset.UtcNow });
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }
    public void OnConnectionClosed(Exception exception) { }
    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

    // ---------------------------------------------------------------- Modality Worklist
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        if (request.SOPClassUID != DicomUID.ModalityWorklistInformationModelFind)
        {
            yield return new DicomCFindResponse(request, DicomStatus.QueryRetrieveIdentifierDoesNotMatchSOPClass);
            yield break;
        }
        List<DicomDataset> items;
        try { items = await WorklistAsync(request.Dataset); }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException or DicomDataException)
        {
            Ctx.Log.LogError(ex, "Endoscopy worklist query failed.");
            items = [];
        }
        Ctx.Host.Update(x => x with { Worklist = x.Worklist + 1, LastActivity = DateTimeOffset.UtcNow });
        foreach (var ds in items) yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = ds };
        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    private static string Q(DicomDataset? ds, DicomTag tag) => ds?.GetSingleValueOrDefault(tag, "")?.Trim() ?? "";
    private static bool Match(string query, string value) =>
        query.Length == 0 || query == "*" || (query.EndsWith('*') ? value.StartsWith(query.TrimEnd('*'), StringComparison.OrdinalIgnoreCase) : string.Equals(query, value, StringComparison.OrdinalIgnoreCase));

    private async Task<List<DicomDataset>> WorklistAsync(DicomDataset q)
    {
        var sps = q.TryGetSequence(DicomTag.ScheduledProcedureStepSequence, out var seq) && seq.Items.Count > 0 ? seq.Items[0] : null;
        var modality = Q(sps, DicomTag.Modality);
        if (modality.Length > 0 && modality != "*" && !string.Equals(modality, "ES", StringComparison.OrdinalIgnoreCase)) return [];
        var dateQ = Q(sps, DicomTag.ScheduledProcedureStepStartDate);
        await using var scope = Ctx.Scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<TimeProvider>();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        DateOnly from = today, to = today;
        if (dateQ.Length >= 8)
        {
            var parts = dateQ.Split('-');
            if (DateOnly.TryParseExact(parts[0], "yyyyMMdd", out var f)) from = f;
            to = parts.Length > 1 && DateOnly.TryParseExact(parts[1], "yyyyMMdd", out var t) ? t : parts.Length > 1 ? from.AddDays(7) : from;
            if (to.DayNumber - from.DayNumber > 14) to = from.AddDays(14);
        }
        var sessions = (await sp.GetRequiredService<TheatreService>().SessionsAsync(from, to)).Where(s => !s.IsCancelled).ToList();
        var bookings = sessions.SelectMany(s => s.Bookings.Where(b => b.Status is not ("Cancelled" or "DidNotAttend" or "Discharged") && (s.Type != "Surgical" || Terminology.Guess(b.Procedure) is not null)).Select(b => (s, b))).ToList();
        var pids = bookings.Select(x => x.b.PatientId).Distinct().ToList();
        var patients = await sp.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var svc = sp.GetRequiredService<EndoService>();
        var station = Q(sps, DicomTag.ScheduledStationAETitle);
        var result = new List<DicomDataset>();
        foreach (var (s, b) in bookings)
        {
            var pt = patients[b.PatientId];
            var pid = pt.Nhi ?? pt.Mrn;
            if (!Match(Q(q, DicomTag.PatientID), pid)) continue;
            if (!Match(Q(q, DicomTag.PatientName).Replace('^', ' ').Trim(), $"{pt.FamilyName} {pt.GivenNames}")) continue;
            var proc = await svc.EnsureAsync(b.Id, b.PatientId, b.Procedure, s.SurgeonId, s.Surgeon, s.Date, "worklist");
            if (!Match(Q(q, DicomTag.AccessionNumber), proc.AccessionNumber)) continue;
            var item = new DicomDataset
            {
                { DicomTag.Modality, "ES" }, { DicomTag.ScheduledStationAETitle, station.Length > 0 && station != "*" ? station : Association.CallingAE },
                { DicomTag.ScheduledProcedureStepStartDate, s.Date.ToString("yyyyMMdd") }, { DicomTag.ScheduledProcedureStepStartTime, b.PlannedStart.ToString("HHmmss") },
                { DicomTag.ScheduledPerformingPhysicianName, (s.Surgeon ?? "").Replace(' ', '^') }, { DicomTag.ScheduledProcedureStepDescription, Terminology.KindLabel(proc.Kind) },
                { DicomTag.ScheduledProcedureStepID, proc.AccessionNumber }, { DicomTag.ScheduledStationName, s.LocationName.Length > 16 ? s.LocationName[..16] : s.LocationName },
            };
            var ds = new DicomDataset
            {
                { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
                { DicomTag.PatientName, $"{pt.FamilyName.ToUpperInvariant()}^{pt.GivenNames}" }, { DicomTag.PatientID, pid }, { DicomTag.IssuerOfPatientID, pt.Nhi is null ? "TDS" : "NHI" },
                { DicomTag.PatientBirthDate, pt.DateOfBirth.ToString("yyyyMMdd") }, { DicomTag.PatientSex, pt.Sex.ToString() switch { "Male" => "M", "Female" => "F", _ => "O" } },
                { DicomTag.AccessionNumber, proc.AccessionNumber }, { DicomTag.StudyInstanceUID, proc.StudyInstanceUid }, { DicomTag.RequestedProcedureID, proc.AccessionNumber },
                { DicomTag.RequestedProcedureDescription, b.Procedure.Length > 64 ? b.Procedure[..64] : b.Procedure }, { DicomTag.ReferringPhysicianName, "" },
                new DicomSequence(DicomTag.ScheduledProcedureStepSequence, item),
            };
            result.Add(ds);
        }
        return result;
    }

    // ---------------------------------------------------------------- Storage (pictures and video)
    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e) { Ctx.Log.LogWarning(e, "Endoscopy DICOM store failed."); return Task.CompletedTask; }

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        var ctx = Ctx;
        try
        {
            var ds = request.Dataset;
            var (bytes, isVideo) = Extract(ds);
            if (bytes is null) return new DicomCStoreResponse(request, DicomStatus.StorageCannotUnderstand);
            var info = new DicomImageInfo(request.SOPInstanceUID.UID, Q(ds, DicomTag.PatientID), Q(ds, DicomTag.PatientName).Replace('^', ' '), Q(ds, DicomTag.AccessionNumber), Q(ds, DicomTag.StudyInstanceUID));
            await using var scope = ctx.Scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<EndoDbContext>();
            var svc = sp.GetRequiredService<EndoService>();
            var proc = info.Accession is { Length: > 0 } acc ? await db.Procedures.AsNoTracking().SingleOrDefaultAsync(p => p.AccessionNumber == acc) : null;
            proc ??= info.StudyInstanceUid is { Length: > 0 } st ? await db.Procedures.AsNoTracking().SingleOrDefaultAsync(p => p.StudyInstanceUid == st) : null;
            var who = $"tower {Association.CallingAE}";
            if (isVideo)
            {
                if (proc is null) { ctx.Log.LogWarning("Endoscopy DICOM: video {Uid} has no matching procedure – not stored.", info.SopInstanceUid); return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure); }
                var (_, verr) = await svc.AddVideoFileAsync(proc.Id, bytes, MediaSource.Dicom, who, info);
                if (verr is not null) return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
                ctx.Host.Update(x => x with { Videos = x.Videos + 1, LastActivity = DateTimeOffset.UtcNow });
            }
            else
            {
                var (_, err) = await svc.AddImageAsync(proc?.Id, bytes, MediaSource.Dicom, who, info);
                if (err is not null) { ctx.Log.LogWarning("Endoscopy DICOM: {Uid} not stored: {Error}", info.SopInstanceUid, err); return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure); }
                ctx.Host.Update(x => x with { Images = x.Images + 1, Unmatched = x.Unmatched + (proc is null ? 1 : 0), LastActivity = DateTimeOffset.UtcNow });
            }
            return new DicomCStoreResponse(request, DicomStatus.Success);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or DicomDataException or DbUpdateException or ArgumentException)
        {
            ctx.Log.LogError(ex, "Endoscopy DICOM: could not store {Uid}.", request.SOPInstanceUID.UID);
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
        }
    }

    /// <summary>JPEG baseline frames are kept as they are; MPEG-4 video fragments become an MP4 file; 8-bit uncompressed RGB/greyscale becomes PNG.</summary>
    public static (byte[]? Bytes, bool Video) Extract(DicomDataset ds)
    {
        if (!ds.Contains(DicomTag.PixelData)) return (null, false);
        var ts = ds.InternalTransferSyntax;
        var px = DicomPixelData.Create(ds);
        if (ts == DicomTransferSyntax.JPEGProcess1) return (px.GetFrame(0).Data, false);
        if (ts == DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41 || ts == DicomTransferSyntax.MPEG4AVCH264BDCompatibleHighProfileLevel41)
        {
            using var ms = new MemoryStream();
            if (ds.GetDicomItem<DicomItem>(DicomTag.PixelData) is DicomOtherByteFragment frags)
                foreach (var f in frags.Fragments) ms.Write(f.Data);
            else ms.Write(px.GetFrame(0).Data);
            return (ms.ToArray(), true);
        }
        if (ts.IsEncapsulated || px.BitsAllocated != 8) return (null, false);
        var w = px.Width; var h = px.Height;
        var frame = px.GetFrame(0).Data;
        var pi = px.PhotometricInterpretation;
        if (pi == PhotometricInterpretation.Monochrome2 || pi == PhotometricInterpretation.Monochrome1)
        {
            var g = frame[..(w * h)];
            if (pi == PhotometricInterpretation.Monochrome1) for (var i = 0; i < g.Length; i++) g[i] = (byte)(255 - g[i]);
            return (Imaging.Png(w, h, g, 1), false);
        }
        if (pi == PhotometricInterpretation.Rgb || pi == PhotometricInterpretation.YbrFull)
        {
            var rgb = new byte[w * h * 3];
            var planar = px.PlanarConfiguration == PlanarConfiguration.Planar;
            for (var i = 0; i < w * h; i++)
            {
                byte a = planar ? frame[i] : frame[i * 3], b = planar ? frame[w * h + i] : frame[i * 3 + 1], c = planar ? frame[2 * w * h + i] : frame[i * 3 + 2];
                if (pi == PhotometricInterpretation.YbrFull)
                {
                    double y = a, cb = b - 128, cr = c - 128;
                    a = (byte)Math.Clamp(y + 1.402 * cr, 0, 255); b = (byte)Math.Clamp(y - 0.344136 * cb - 0.714136 * cr, 0, 255); c = (byte)Math.Clamp(y + 1.772 * cb, 0, 255);
                }
                rgb[i * 3] = a; rgb[i * 3 + 1] = b; rgb[i * 3 + 2] = c;
            }
            return (Imaging.Png(w, h, rgb), false);
        }
        return (null, false);
    }

    // ---------------------------------------------------------------- MPPS
    public async Task<DicomNCreateResponse> OnNCreateRequestAsync(DicomNCreateRequest request)
    {
        if (request.SOPClassUID != DicomUID.ModalityPerformedProcedureStep) return new DicomNCreateResponse(request, DicomStatus.SOPClassNotSupported);
        var ds = request.Dataset;
        var acc = ds.TryGetSequence(DicomTag.ScheduledStepAttributesSequence, out var seq) && seq.Items.Count > 0 ? Q(seq.Items[0], DicomTag.AccessionNumber) : "";
        if (acc.Length > 0) await MppsAsync(acc, request.SOPInstanceUID?.UID ?? "", false);
        return new DicomNCreateResponse(request, DicomStatus.Success);
    }

    public async Task<DicomNSetResponse> OnNSetRequestAsync(DicomNSetRequest request)
    {
        if (request.SOPClassUID != DicomUID.ModalityPerformedProcedureStep) return new DicomNSetResponse(request, DicomStatus.SOPClassNotSupported);
        var status = Q(request.Dataset, DicomTag.PerformedProcedureStepStatus);
        if (status is "COMPLETED" or "DISCONTINUED")
        {
            await using var scope = Ctx.Scopes.CreateAsyncScope();
            var acc = await scope.ServiceProvider.GetRequiredService<EndoDbContext>().Procedures.AsNoTracking().Where(p => p.MppsUid == request.SOPInstanceUID.UID).Select(p => p.AccessionNumber).SingleOrDefaultAsync();
            if (acc is not null) await MppsAsync(acc, request.SOPInstanceUID.UID, true);
        }
        return new DicomNSetResponse(request, DicomStatus.Success);
    }

    private async Task MppsAsync(string accession, string uid, bool completed)
    {
        await using var scope = Ctx.Scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<EndoService>().MppsAsync(accession, uid, completed, sp.GetRequiredService<TimeProvider>().GetUtcNow());
        Ctx.Host.Update(x => x with { LastActivity = DateTimeOffset.UtcNow });
    }

    public Task<DicomNActionResponse> OnNActionRequestAsync(DicomNActionRequest request) => Task.FromResult(new DicomNActionResponse(request, DicomStatus.SOPClassNotSupported));
    public Task<DicomNDeleteResponse> OnNDeleteRequestAsync(DicomNDeleteRequest request) => Task.FromResult(new DicomNDeleteResponse(request, DicomStatus.SOPClassNotSupported));
    public Task<DicomNEventReportResponse> OnNEventReportRequestAsync(DicomNEventReportRequest request) => Task.FromResult(new DicomNEventReportResponse(request, DicomStatus.SOPClassNotSupported));
    public Task<DicomNGetResponse> OnNGetRequestAsync(DicomNGetRequest request) => Task.FromResult(new DicomNGetResponse(request, DicomStatus.SOPClassNotSupported));
}
