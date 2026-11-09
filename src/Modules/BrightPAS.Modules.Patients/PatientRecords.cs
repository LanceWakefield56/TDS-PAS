using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Patients;

namespace BrightPAS.Modules.Patients;

// =====================================================================================================================
// Documents & scanning (0.10.0): attachments to the patient (optionally a booking). Contents are encrypted at rest.
// =====================================================================================================================

public enum DocumentCategory { Referral = 1, Consent = 2, Clinical = 3, Correspondence = 4, Identity = 5, Insurance = 6, Results = 7, Imaging = 8, Other = 9 }
public enum DocumentSource { Upload = 1, Scan = 2, Portal = 3 }

public sealed class PatientDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public DocumentCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 of the original (unencrypted) file, hex – proves the file has not changed.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Encrypted file contents (see <see cref="IDocumentCipher"/>).</summary>
    public byte[] Content { get; set; } = [];
    public DocumentSource Source { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedBy { get; set; } = "";
    public bool IsDeleted { get; set; }
    public string? DeletedReason { get; set; }
    public string? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Encrypts document contents at rest (the web host uses ASP.NET Data Protection; keys are DPAPI-protected on Windows).</summary>
public interface IDocumentCipher
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] cipher);
}

public enum TextStatus { Pending = 1, Done = 2, NoText = 3, Failed = 4 }

/// <summary>Searchable text of a document (text layer or OCR), encrypted like the document itself.</summary>
public sealed class DocumentText
{
    public Guid DocumentId { get; set; }
    public TextStatus Status { get; set; } = TextStatus.Pending;
    /// <summary>"text" (PDF text layer) or "ocr" (Tesseract).</summary>
    public string? Method { get; set; }
    public byte[]? Content { get; set; }
    public int CharCount { get; set; }
    public int Attempts { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Reads text from a document: PDF text layer first, OCR for scans and photos. Implemented in the web host.</summary>
public interface IDocumentTextExtractor
{
    Task<(string? Text, string Method, string? Problem)> ExtractAsync(string contentType, byte[] content, CancellationToken ct);
}

public sealed record DocumentHit(Guid Id, string Title, string Category, DateTimeOffset UploadedAt, string Snippet, int Matches);

public sealed record NewPatientDocument(Guid PatientId, Guid? BookingId, DocumentCategory Category, string? Title, string FileName, string ContentBase64, DocumentSource Source);
public sealed record DocumentRow(Guid Id, Guid PatientId, Guid? BookingId, string Category, string Title, string FileName, string ContentType, long SizeBytes, string Source,
    DateTimeOffset UploadedAt, string UploadedBy, string TextStatus = "Pending");

public sealed class DocumentService(PatientsDbContext db, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    public const int DefaultMaxMegabytes = 20;

    /// <summary>Detects the real file type from its first bytes (the file name and browser type are not trusted).</summary>
    public static string? DetectType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return "application/pdf";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        if (b.Length >= 4 && ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A && b[3] == 0x00) || (b[0] == 0x4D && b[1] == 0x4D && b[2] == 0x00 && b[3] == 0x2A))) return "image/tiff";
        return null;
    }

    public static string SafeFileName(string? name)
    {
        var n = Path.GetFileName(name ?? "").Trim();
        var sb = new StringBuilder();
        foreach (var c in n) sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ' ? c : '_');
        var s = sb.ToString().Trim('.', ' ');
        if (s.Length == 0) s = "document";
        return s.Length > 120 ? s[^120..] : s;
    }

    public async Task<(PatientDocument? Document, IReadOnlyList<string> Errors)> AddAsync(NewPatientDocument d, string user, int maxMegabytes = DefaultMaxMegabytes, CancellationToken ct = default)
    {
        var errors = new List<string>();
        byte[] bytes = [];
        try { bytes = Convert.FromBase64String(d.ContentBase64 ?? ""); } catch (FormatException) { errors.Add("The file could not be read."); }
        if (errors.Count == 0 && bytes.Length == 0) errors.Add("The file is empty.");
        if (bytes.Length > maxMegabytes * 1024L * 1024L) errors.Add($"Files must be {maxMegabytes} MB or smaller.");
        var type = DetectType(bytes);
        if (errors.Count == 0 && type is null) errors.Add("Only PDF, JPEG, PNG and TIFF files (scans and photos) can be attached.");
        if (!await db.Patients.AnyAsync(p => p.Id == d.PatientId && p.MergedIntoId == null, ct)) errors.Add("Patient not found.");
        if (!Enum.IsDefined(d.Category)) errors.Add("Choose a document type.");
        if (errors.Count > 0) return (null, errors);
        var name = SafeFileName(d.FileName);
        var row = new PatientDocument
        {
            PatientId = d.PatientId, BookingId = d.BookingId, Category = d.Category, Title = Trunc(string.IsNullOrWhiteSpace(d.Title) ? Path.GetFileNameWithoutExtension(name) : d.Title.Trim(), 150),
            FileName = name, ContentType = type!, SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Content = cipher.Protect(bytes),
            Source = d.Source, UploadedAt = clock.GetUtcNow(), UploadedBy = user,
        };
        db.Documents.Add(row);
        db.DocumentTexts.Add(new DocumentText { DocumentId = row.Id });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Upload", "PatientDocument", row.Id.ToString(), d.PatientId.ToString(), reason: $"{row.Category}: {row.Title} ({row.ContentType}, {row.SizeBytes / 1024} KB, {row.Source})", ct: ct);
        return (row, errors);
    }

    // ---------------------------------------------------------------- searchable text (0.11.0)

    public async Task<IReadOnlyList<Guid>> PendingTextAsync(int take, CancellationToken ct = default) =>
        await db.DocumentTexts.AsNoTracking().Where(t => t.Status == TextStatus.Pending && t.Attempts < 3).OrderBy(t => t.Attempts).Take(take).Select(t => t.DocumentId).ToListAsync(ct);

    /// <summary>Extracts and stores the text of one document (called by the background worker).</summary>
    public async Task<TextStatus> ProcessTextAsync(Guid documentId, IDocumentTextExtractor extractor, CancellationToken ct = default)
    {
        var t = await db.DocumentTexts.SingleOrDefaultAsync(x => x.DocumentId == documentId, ct);
        var d = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId, ct);
        if (t is null || d is null) return TextStatus.Failed;
        t.Attempts++;
        try
        {
            var bytes = cipher.Unprotect(d.Content);
            var (text, method, problem) = await extractor.ExtractAsync(d.ContentType, bytes, ct);
            text = text?.Trim();
            t.Method = method; t.ProcessedAt = clock.GetUtcNow(); t.Message = problem is null ? null : Trunc(problem, 300);
            if (!string.IsNullOrEmpty(text)) { t.Status = TextStatus.Done; t.Content = cipher.Protect(System.Text.Encoding.UTF8.GetBytes(text)); t.CharCount = text.Length; }
            else t.Status = problem is null ? TextStatus.NoText : (t.Attempts >= 3 ? TextStatus.Failed : TextStatus.Pending);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            t.Message = Trunc(ex.Message, 300); t.ProcessedAt = clock.GetUtcNow();
            t.Status = t.Attempts >= 3 ? TextStatus.Failed : TextStatus.Pending;
        }
        await db.SaveChangesAsync(ct);
        return t.Status;
    }

    public async Task<int> RetryFailedTextAsync(CancellationToken ct = default) =>
        await db.DocumentTexts.Where(t => t.Status == TextStatus.Failed).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TextStatus.Pending).SetProperty(x => x.Attempts, 0), ct);

    public async Task<object> TextSummaryAsync(CancellationToken ct = default)
    {
        var rows = await db.DocumentTexts.AsNoTracking().GroupBy(t => t.Status).Select(g => new { g.Key, n = g.Count() }).ToListAsync(ct);
        var lastProblem = await db.DocumentTexts.AsNoTracking().Where(t => t.Message != null).OrderByDescending(t => t.ProcessedAt).Select(t => t.Message).FirstOrDefaultAsync(ct);
        return new { counts = rows.ToDictionary(r => r.Key.ToString(), r => r.n), lastProblem };
    }

    /// <summary>Searches the text of one patient's documents (decrypted in memory only). Audited.</summary>
    public async Task<IReadOnlyList<DocumentHit>> SearchAsync(Guid patientId, string query, string user, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return [];
        var docs = await db.Documents.AsNoTracking().Where(d => d.PatientId == patientId && !d.IsDeleted)
            .Select(d => new { d.Id, d.Title, d.Category, d.UploadedAt }).ToListAsync(ct);
        var ids = docs.Select(d => d.Id).ToList();
        var texts = await db.DocumentTexts.AsNoTracking().Where(t => ids.Contains(t.DocumentId) && t.Status == TextStatus.Done && t.Content != null).ToListAsync(ct);
        var hits = new List<DocumentHit>();
        foreach (var t in texts)
        {
            string text;
            try { text = System.Text.Encoding.UTF8.GetString(cipher.Unprotect(t.Content!)); } catch (CryptographicException) { continue; }
            var titleHit = docs.First(d => d.Id == t.DocumentId).Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            var count = 0; var first = -1;
            for (var i = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase)) { if (first < 0) first = i; count++; }
            if (count == 0 && !titleHit) continue;
            var d = docs.First(x => x.Id == t.DocumentId);
            var snippet = first < 0 ? "" : System.Text.RegularExpressions.Regex.Replace(text.Substring(Math.Max(0, first - 60), Math.Min(text.Length - Math.Max(0, first - 60), query.Length + 120)), @"\s+", " ");
            hits.Add(new DocumentHit(d.Id, d.Title, d.Category.ToString(), d.UploadedAt, snippet, count));
        }
        foreach (var d in docs.Where(d => texts.All(t => t.DocumentId != d.Id) && d.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
            hits.Add(new DocumentHit(d.Id, d.Title, d.Category.ToString(), d.UploadedAt, "", 0));
        await audit.WriteAsync(user, "Search", "PatientDocument", patientId.ToString(), patientId.ToString(), reason: $"{hits.Count} document(s) matched", ct: ct);
        return hits.OrderByDescending(h => h.Matches).ThenByDescending(h => h.UploadedAt).ToList();
    }

    public async Task<IReadOnlyList<DocumentRow>> ListAsync(Guid patientId, Guid? bookingId = null, CancellationToken ct = default)
    {
        var rows = await db.Documents.AsNoTracking().Where(x => x.PatientId == patientId && !x.IsDeleted && (bookingId == null || x.BookingId == bookingId))
            .Select(x => new { x.Id, x.PatientId, x.BookingId, x.Category, x.Title, x.FileName, x.ContentType, x.SizeBytes, x.Source, x.UploadedAt, x.UploadedBy }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var status = await db.DocumentTexts.AsNoTracking().Where(t => ids.Contains(t.DocumentId)).ToDictionaryAsync(t => t.DocumentId, t => t.Status, ct);
        return rows.OrderByDescending(x => x.UploadedAt).Select(x => new DocumentRow(x.Id, x.PatientId, x.BookingId, x.Category.ToString(), x.Title, x.FileName, x.ContentType, x.SizeBytes,
            x.Source.ToString(), x.UploadedAt, x.UploadedBy, status.TryGetValue(x.Id, out var st) ? st.ToString() : "NoText")).ToList();
    }

    /// <summary>Decrypts a document for viewing or download and records who opened it. Returns null when not found or tampered with.</summary>
    public async Task<(PatientDocument Meta, byte[] Bytes)?> OpenAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var row = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (row is null) return null;
        byte[] bytes;
        try { bytes = cipher.Unprotect(row.Content); } catch (CryptographicException) { return null; }
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(row.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
        await audit.WriteAsync(user, "View", "PatientDocument", row.Id.ToString(), row.PatientId.ToString(), reason: row.Title, workstation: workstation, ct: ct);
        return (row, bytes);
    }

    public async Task<string?> DeleteAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var row = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (row is null) return "Document not found.";
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) return "Give a reason (e.g. 'scanned to the wrong patient').";
        row.IsDeleted = true; row.DeletedReason = Trunc(reason.Trim(), 300); row.DeletedBy = user; row.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "PatientDocument", row.Id.ToString(), row.PatientId.ToString(), reason: $"{row.Title}: {row.DeletedReason}", ct: ct);
        return null;
    }

    internal static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}

// =====================================================================================================================
// Patient identity correction (second-person check) and merge.
// =====================================================================================================================

public enum ChangeStatus { Pending = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

/// <summary>A requested change to a patient's identity (name, date of birth, sex, NHI). Applied only after a second person approves.</summary>
public sealed class IdentityCorrection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string? NewFamilyName { get; set; }
    public string? NewGivenNames { get; set; }
    public DateOnly? NewDateOfBirth { get; set; }
    public BirthSex? NewSex { get; set; }
    public string? NewNhi { get; set; }
    public bool ClearNhi { get; set; }
    /// <summary>Summary of the values before the change (for the audit trail and the approver).</summary>
    public string Before { get; set; } = "";
    public string Reason { get; set; } = "";
    /// <summary>Evidence seen, e.g. "Passport", "Driver licence", "NHI service".</summary>
    public string? Evidence { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>Merging a duplicate record into the surviving record. Everything recorded against the duplicate moves to the survivor.</summary>
public sealed class PatientMerge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurvivorId { get; set; }
    public Guid DuplicateId { get; set; }
    public string DuplicateSummary { get; set; } = "";
    public string Reason { get; set; } = "";
    public ChangeStatus Status { get; set; } = ChangeStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>Set when every module has moved its records (merges are re-runnable until then).</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Result { get; set; }
}

public sealed record CorrectionRequest(Guid PatientId, string? FamilyName, string? GivenNames, DateOnly? DateOfBirth, BirthSex? Sex, string? Nhi, bool ClearNhi, string Reason, string? Evidence);
public sealed record MergeRequest(Guid SurvivorId, Guid DuplicateId, string Reason);
public sealed record Decision(bool Approve, string? Note);

/// <summary>Moves a duplicate patient's records to the survivor in one module. Implemented per module in the web host.</summary>
public interface IPatientMergeParticipant
{
    string Name { get; }
    Task<int> MoveAsync(Guid fromPatientId, Guid toPatientId, CancellationToken ct);
}

public sealed class IdentityService(PatientsDbContext db, AuditWriter audit, TimeProvider clock, IEnumerable<IPatientMergeParticipant> participants)
{
    private static string Describe(Patient p) => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}; DOB {p.DateOfBirth:yyyy-MM-dd}; {p.Sex}; NHI {p.Nhi ?? "none"}; MRN {p.Mrn}";

    public async Task<(IdentityCorrection? Correction, IReadOnlyList<string> Errors)> RequestCorrectionAsync(CorrectionRequest r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.PatientId && x.MergedIntoId == null, ct);
        if (p is null) return (null, ["Patient not found."]);
        string? nhi = null;
        if (!string.IsNullOrWhiteSpace(r.Nhi))
        {
            nhi = r.Nhi.Trim().ToUpperInvariant();
            if (!NhiValidator.Validate(nhi).IsValid) errors.Add("The new NHI number is not valid (check digit).");
            else if (await db.Patients.AnyAsync(x => x.Nhi == nhi && x.Id != p.Id, ct)) errors.Add("Another patient already has this NHI – use 'Merge records' if they are the same person.");
        }
        var family = Blank(r.FamilyName); var given = Blank(r.GivenNames);
        if (family is { Length: > 100 } || given is { Length: > 150 }) errors.Add("Names are too long.");
        if (r.DateOfBirth is { } dob && (dob > DateOnly.FromDateTime(clock.GetLocalNow().DateTime) || dob.Year < 1900)) errors.Add("Check the date of birth.");
        var changes = (family is not null && family != p.FamilyName) || (given is not null && given != p.GivenNames) || (r.DateOfBirth is { } d2 && d2 != p.DateOfBirth)
            || (r.Sex is { } s && s != p.Sex) || (nhi is not null && nhi != p.Nhi) || (r.ClearNhi && p.Nhi is not null);
        if (!changes) errors.Add("Nothing to change – enter the corrected details.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 5) errors.Add("Give the reason for the correction.");
        if (await db.Corrections.AnyAsync(c => c.PatientId == p.Id && c.Status == ChangeStatus.Pending, ct)) errors.Add("A correction for this patient is already waiting for approval.");
        if (errors.Count > 0) return (null, errors);
        var row = new IdentityCorrection
        {
            PatientId = p.Id, NewFamilyName = family, NewGivenNames = given, NewDateOfBirth = r.DateOfBirth, NewSex = r.Sex, NewNhi = nhi, ClearNhi = r.ClearNhi && nhi is null,
            Before = Describe(p), Reason = DocumentService.Trunc(r.Reason.Trim(), 500), Evidence = Blank(r.Evidence) is { } ev ? DocumentService.Trunc(ev, 200) : null,
            RequestedAt = clock.GetUtcNow(), RequestedBy = user,
        };
        db.Corrections.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "IdentityCorrectionRequested", "Patient", p.Id.ToString(), p.Id.ToString(), reason: row.Reason, ct: ct);
        return (row, errors);
    }

    public async Task<IReadOnlyList<IdentityCorrection>> CorrectionsAsync(bool pendingOnly, Guid? patientId = null, CancellationToken ct = default) =>
        (await db.Corrections.AsNoTracking().Where(c => (!pendingOnly || c.Status == ChangeStatus.Pending) && (patientId == null || c.PatientId == patientId)).ToListAsync(ct))
            .OrderByDescending(c => c.RequestedAt).Take(200).ToList();

    public async Task<string?> DecideCorrectionAsync(Guid id, Decision d, string user, CancellationToken ct = default)
    {
        var c = await db.Corrections.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Correction not found.";
        if (c.Status != ChangeStatus.Pending) return "This correction has already been decided.";
        if (string.Equals(c.RequestedBy, user, StringComparison.OrdinalIgnoreCase)) return "A second person must approve identity corrections (you requested this one).";
        if (!d.Approve && string.IsNullOrWhiteSpace(d.Note)) return "Say why the correction is rejected.";
        var p = await db.Patients.SingleOrDefaultAsync(x => x.Id == c.PatientId, ct);
        if (p is null || p.MergedIntoId is not null) return "The patient record is no longer available (merged?).";
        if (d.Approve && c.NewNhi is not null && await db.Patients.AnyAsync(x => x.Nhi == c.NewNhi && x.Id != p.Id, ct)) return "Another patient now has this NHI – merge the records instead.";
        c.Status = d.Approve ? ChangeStatus.Approved : ChangeStatus.Rejected; c.DecidedAt = clock.GetUtcNow(); c.DecidedBy = user; c.DecisionNote = Blank(d.Note);
        if (d.Approve)
        {
            if (c.NewFamilyName is { } f) { p.FamilyName = f; p.FamilyNameSearch = PatientService.SearchKey(f); }
            if (c.NewGivenNames is { } g) { p.GivenNames = g; p.GivenNamesSearch = PatientService.SearchKey(g); }
            if (c.NewDateOfBirth is { } dob) p.DateOfBirth = dob;
            if (c.NewSex is { } s) p.Sex = s;
            if (c.NewNhi is { } n) p.Nhi = n;
            if (c.ClearNhi) p.Nhi = null;
            p.UpdatedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, d.Approve ? "IdentityCorrected" : "IdentityCorrectionRejected", "Patient", p.Id.ToString(), p.Id.ToString(),
            reason: d.Approve ? $"Before: {c.Before} | After: {Describe(p)} | Requested by {c.RequestedBy}: {c.Reason}" : $"{d.Note}", ct: ct);
        return null;
    }

    public async Task<(PatientMerge? Merge, IReadOnlyList<string> Errors)> RequestMergeAsync(MergeRequest r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (r.SurvivorId == r.DuplicateId) return (null, ["Choose two different records."]);
        var s = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.SurvivorId && x.MergedIntoId == null, ct);
        var dup = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.DuplicateId && x.MergedIntoId == null, ct);
        if (s is null || dup is null) return (null, ["Both records must exist and not already be merged."]);
        if (s.Nhi is not null && dup.Nhi is not null && s.Nhi != dup.Nhi) errors.Add("The two records have different NHI numbers – confirm with the NHI service; they may be different people.");
        if (s.IsTestPatient != dup.IsTestPatient) errors.Add("A test patient cannot be merged with a real patient.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 5) errors.Add("Give the reason (how you confirmed they are the same person).");
        if (await db.Merges.AnyAsync(m => m.Status == ChangeStatus.Pending && (m.DuplicateId == dup.Id || m.SurvivorId == dup.Id || m.DuplicateId == s.Id), ct))
            errors.Add("A merge involving one of these records is already waiting for approval.");
        if (errors.Count > 0) return (null, errors);
        var row = new PatientMerge { SurvivorId = s.Id, DuplicateId = dup.Id, DuplicateSummary = Describe(dup), Reason = DocumentService.Trunc(r.Reason.Trim(), 500), RequestedAt = clock.GetUtcNow(), RequestedBy = user };
        db.Merges.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "MergeRequested", "Patient", s.Id.ToString(), s.Id.ToString(), reason: $"Merge {dup.Mrn} into {s.Mrn}: {row.Reason}", ct: ct);
        return (row, errors);
    }

    public async Task<IReadOnlyList<PatientMerge>> MergesAsync(bool pendingOnly, CancellationToken ct = default) =>
        (await db.Merges.AsNoTracking().Where(m => !pendingOnly || m.Status == ChangeStatus.Pending || (m.Status == ChangeStatus.Approved && m.CompletedAt == null)).ToListAsync(ct))
            .OrderByDescending(m => m.RequestedAt).Take(200).ToList();

    /// <summary>Approves (and carries out) or rejects a merge. Carrying out is re-runnable: if a module fails part way, approve again to finish.</summary>
    public async Task<(string? Error, string? Result)> DecideMergeAsync(Guid id, Decision d, string user, CancellationToken ct = default)
    {
        var m = await db.Merges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return ("Merge not found.", null);
        if (m.CompletedAt is not null || m.Status is ChangeStatus.Rejected or ChangeStatus.Cancelled) return ("This merge has already been decided.", null);
        if (string.Equals(m.RequestedBy, user, StringComparison.OrdinalIgnoreCase)) return ("A second person must approve merges (you requested this one).", null);
        if (!d.Approve)
        {
            if (m.Status == ChangeStatus.Approved) return ("This merge is already approved; finish it instead.", null);
            if (string.IsNullOrWhiteSpace(d.Note)) return ("Say why the merge is rejected.", null);
            m.Status = ChangeStatus.Rejected; m.DecidedAt = clock.GetUtcNow(); m.DecidedBy = user; m.DecisionNote = d.Note!.Trim();
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "MergeRejected", "Patient", m.SurvivorId.ToString(), m.SurvivorId.ToString(), reason: m.DecisionNote, ct: ct);
            return (null, null);
        }
        if (m.Status == ChangeStatus.Pending) { m.Status = ChangeStatus.Approved; m.DecidedAt = clock.GetUtcNow(); m.DecidedBy = user; m.DecisionNote = Blank(d.Note); await db.SaveChangesAsync(ct); }

        var parts = new List<string>();
        foreach (var p in participants.OrderBy(p => p.Name))
        {
            var n = await p.MoveAsync(m.DuplicateId, m.SurvivorId, ct);
            if (n > 0) parts.Add($"{p.Name}: {n}");
        }
        // Patients module itself: alerts, ethnicities (if survivor has none), documents, then retire the duplicate.
        var dup = await db.Patients.Include(x => x.Alerts).Include(x => x.Ethnicities).SingleAsync(x => x.Id == m.DuplicateId, ct);
        var survivor = await db.Patients.Include(x => x.Alerts).Include(x => x.Ethnicities).SingleAsync(x => x.Id == m.SurvivorId, ct);
        var movedAlerts = 0;
        foreach (var a in dup.Alerts.Where(a => a.IsActive).ToList())
        {
            if (!survivor.Alerts.Any(x => x.IsActive && x.Type == a.Type && string.Equals(x.Description, a.Description, StringComparison.OrdinalIgnoreCase)))
            {
                db.Alerts.Add(new PatientAlert { PatientId = survivor.Id, Type = a.Type, Severity = a.Severity, Description = a.Description, RecordedBy = a.RecordedBy, RecordedAt = a.RecordedAt });
                movedAlerts++;
            }
        }
        if (survivor.Ethnicities.Count == 0) foreach (var e in dup.Ethnicities) db.Set<PatientEthnicity>().Add(new PatientEthnicity { PatientId = survivor.Id, Order = e.Order, Code = e.Code });
        var dupNhi = dup.Nhi;
        dup.Nhi = null; // the NHI belongs to the survivor now (unique index)
        dup.MergedIntoId = survivor.Id;
        await db.SaveChangesAsync(ct);
        if (survivor.Nhi is null && dupNhi is not null) { survivor.Nhi = dupNhi; await db.SaveChangesAsync(ct); }
        var docs = await db.Documents.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, survivor.Id), ct);
        var corr = await db.Corrections.Where(x => x.PatientId == dup.Id && x.Status == ChangeStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ChangeStatus.Cancelled), ct);
        if (movedAlerts > 0) parts.Add($"Alerts: {movedAlerts}");
        if (docs > 0) parts.Add($"Documents: {docs}");
        m.CompletedAt = clock.GetUtcNow();
        m.Result = parts.Count == 0 ? "No other records to move." : string.Join("; ", parts);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "PatientMerged", "Patient", survivor.Id.ToString(), survivor.Id.ToString(),
            reason: $"{dup.Mrn} merged into {survivor.Mrn} (requested by {m.RequestedBy}: {m.Reason}). Moved – {m.Result}", ct: ct);
        return (null, m.Result);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// =====================================================================================================================
// Wristbands and labels (Zebra ZPL).
// =====================================================================================================================

public enum PrinterKind { Wristband = 1, Label = 2 }
/// <summary>Zpl = Zebra network printer (BrightPAS sends to port 9100). Dymo = basic DYMO LabelWriter on the workstation (printed from the browser through the Windows driver).</summary>
public enum PrinterLanguage { Zpl = 1, Dymo = 2 }

public sealed class LabelPrinter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PrinterKind Kind { get; set; }
    public PrinterLanguage Language { get; set; } = PrinterLanguage.Zpl;
    /// <summary>DYMO label stock (e.g. 99012); null for Zebra.</summary>
    public string? MediaCode { get; set; }
    /// <summary>Printer IP address or host name on the TDS network (Zebra raw printing, port 9100). Empty for DYMO.</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; } = 9100;
    public int Dpi { get; set; } = 300;
    public int WidthMm { get; set; } = 25;
    public int LengthMm { get; set; } = 280;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record PrinterEdit(Guid? Id, string Name, PrinterKind Kind, string Host, int Port, int Dpi, int WidthMm, int LengthMm, bool IsDefault, bool IsActive,
    PrinterLanguage Language = PrinterLanguage.Zpl, string? MediaCode = null);

/// <summary>DYMO LabelWriter label stock BrightPAS can lay out (width × height in mm, as the label sits in the printer output).</summary>
public sealed record DymoMedia(string Code, string Name, int WidthMm, int HeightMm);

/// <summary>Code 128 (set B) barcode as bar positions in modules, for drawing as SVG in the browser.</summary>
public static class Code128
{
    private static readonly string[] Patterns =
    [
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213","221312","231212","112232","122132","122231","113222","123122","123221",
        "223211","221132","221231","213212","223112","312131","311222","321122","321221","312212","322112","322211","212123","212321","232121","111323","131123","131321",
        "112313","132113","132311","211313","231113","231311","112133","112331","132131","113123","113321","133121","313121","211331","231131","213113","213311","213131",
        "311123","311321","331121","312113","312311","332111","314111","221411","431111","111224","111422","121124","121421","141122","141221","112214","112412","122114",
        "122411","142112","142211","241211","221114","413111","241112","134111","111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141","114131","311141","411131","211412","211214","211232","2331112",
    ];

    /// <summary>Returns (x, width) of each black bar in modules, and the total width including 10-module quiet zones.</summary>
    public static (IReadOnlyList<int[]> Bars, int Width) Encode(string text)
    {
        var data = new string(text.Where(c => c is >= ' ' and <= '~').ToArray());
        var codes = new List<int> { 104 }; // Start B
        codes.AddRange(data.Select(c => c - 32));
        var sum = 104;
        for (var i = 0; i < data.Length; i++) sum += (data[i] - 32) * (i + 1);
        codes.Add(sum % 103);
        codes.Add(106); // Stop
        var bars = new List<int[]>();
        var x = 10;
        foreach (var code in codes)
        {
            var p = Patterns[code];
            for (var i = 0; i < p.Length; i++)
            {
                var w = p[i] - '0';
                if (i % 2 == 0) bars.Add([x, w]);
                x += w;
            }
        }
        return (bars, x + 10);
    }
}

/// <summary>Builds ZPL for Zebra wristband and label printers and sends it to the printer (raw TCP 9100).</summary>
public sealed class LabelService(PatientsDbContext db, AuditWriter audit)
{
    public static bool AllowedPort(int port) => port is >= 9100 and <= 9199 or 6101;

    public static readonly IReadOnlyList<DymoMedia> DymoMediaList =
    [
        new("11352", "11352 Return address 54 × 25 mm", 54, 25),
        new("11354", "11354 Multi-purpose 57 × 32 mm", 57, 32),
        new("99010", "99010 Standard address 89 × 28 mm", 89, 28),
        new("99012", "99012 Large address 89 × 36 mm", 89, 36),
        new("99014", "99014 Shipping 101 × 54 mm", 101, 54),
    ];

    /// <summary>Data for printing patient labels from the browser on a DYMO (or any Windows label printer). Audited as a print.</summary>
    public async Task<(string? Error, object? Sheet)> DymoSheetAsync(Guid patientId, int copies, Guid? printerId, string user, CancellationToken ct = default)
    {
        var p = await db.Patients.AsNoTracking().Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == patientId && x.MergedIntoId == null, ct);
        if (p is null) return ("Patient not found.", null);
        var printer = printerId is { } pid ? await db.Printers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid && x.IsActive, ct)
            : await db.Printers.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == PrinterKind.Label && x.IsDefault && x.IsActive, ct);
        if (printer is null || printer.Language != PrinterLanguage.Dymo) return ("No DYMO label printer is set up.", null);
        var (bars, width) = Code128.Encode(p.Nhi ?? p.Mrn);
        copies = Math.Clamp(copies, 1, 50);
        await audit.WriteAsync(user, "Print", "PatientLabel", patientId.ToString(), patientId.ToString(), reason: $"{copies} label(s) on {printer.Name} (DYMO {printer.MediaCode}, from the browser)", ct: ct);
        return (null, new
        {
            media = printer.MediaCode, printer = printer.Name, copies,
            name = $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", dateOfBirth = p.DateOfBirth, sex = p.Sex.ToString(), p.Nhi, p.Mrn,
            allergy = p.Alerts.Any(a => a.IsActive && a.Type == AlertType.Allergy), barcode = new { bars, width, text = p.Nhi ?? p.Mrn },
        });
    }

    public async Task<IReadOnlyList<LabelPrinter>> PrintersAsync(bool activeOnly, CancellationToken ct = default) =>
        (await db.Printers.AsNoTracking().Where(p => !activeOnly || p.IsActive).ToListAsync(ct)).OrderBy(p => p.Kind).ThenBy(p => p.Name).ToList();

    public async Task<IReadOnlyList<string>> SavePrinterAsync(PrinterEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = e.Name?.Trim() ?? ""; var host = e.Host?.Trim() ?? "";
        if (name.Length < 2) errors.Add("Name the printer (e.g. 'Admissions wristbands').");
        var media = e.Language == PrinterLanguage.Dymo ? DymoMediaList.FirstOrDefault(m => m.Code == e.MediaCode) : null;
        if (e.Language == PrinterLanguage.Dymo)
        {
            if (e.Kind != PrinterKind.Label) errors.Add("DYMO LabelWriters print labels, not wristbands – use a Zebra wristband printer for wristbands.");
            if (media is null) errors.Add("Choose the DYMO label stock loaded in the printer.");
        }
        else
        {
            if (host.Length is < 1 or > 100 || host.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-'))) errors.Add("Enter the printer's IP address or host name.");
            if (!AllowedPort(e.Port)) errors.Add("Zebra network printers use port 9100 (allowed: 9100–9199, 6101).");
            if (e.Dpi is not (203 or 300 or 600)) errors.Add("Resolution must be 203, 300 or 600 dpi.");
            if (e.WidthMm is < 10 or > 120 || e.LengthMm is < 10 or > 400) errors.Add("Check the media size (width 10–120 mm, length 10–400 mm).");
        }
        if (!Enum.IsDefined(e.Language)) errors.Add("Choose Zebra or DYMO.");
        if (await db.Printers.AnyAsync(p => p.Name == name && p.Id != e.Id, ct)) errors.Add("A printer with this name already exists.");
        if (errors.Count > 0) return errors;
        LabelPrinter row;
        if (e.Id is { } id) row = await db.Printers.SingleAsync(p => p.Id == id, ct);
        else { row = new LabelPrinter(); db.Printers.Add(row); }
        row.Name = name; row.Kind = e.Kind; row.Language = e.Language; row.IsActive = e.IsActive;
        if (media is not null) { row.MediaCode = media.Code; row.Host = ""; row.Port = 0; row.Dpi = 300; row.WidthMm = media.WidthMm; row.LengthMm = media.HeightMm; }
        else { row.MediaCode = null; row.Host = host; row.Port = e.Port; row.Dpi = e.Dpi; row.WidthMm = e.WidthMm; row.LengthMm = e.LengthMm; }
        row.IsDefault = e.IsDefault && e.IsActive;
        if (row.IsDefault) foreach (var other in await db.Printers.Where(p => p.Kind == e.Kind && p.Id != row.Id && p.IsDefault).ToListAsync(ct)) other.IsDefault = false;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "LabelPrinter", row.Id.ToString(), reason: $"{row.Name} ({row.Kind}) {row.Host}:{row.Port}", ct: ct);
        return errors;
    }

    /// <summary>ZPL field text: no control characters and no ZPL command characters (^ ~).</summary>
    public static string Z(string? s) => new((s ?? "").Where(c => !char.IsControl(c) && c is not '^' and not '~' and not '\\').ToArray());

    private static int Dots(int mm, int dpi) => (int)Math.Round(mm * dpi / 25.4);

    /// <summary>Patient wristband: name, DOB, NHI, MRN, sex, allergy warning and a Code 128 barcode (NHI, or MRN when there is no NHI). Printed along the band.</summary>
    public static string Wristband(Patient p, bool hasAllergy, LabelPrinter? printer = null)
    {
        var dpi = printer?.Dpi ?? 300;
        var w = Dots(printer?.WidthMm ?? 25, dpi); var len = Dots(printer?.LengthMm ?? 280, dpi);
        int f(int pt) => (int)Math.Round(pt * dpi / 72.0);
        var x0 = Dots(80, dpi); // leave the clasp end clear
        var sb = new StringBuilder();
        sb.Append("^XA^CI28").Append($"^PW{w}^LL{len}^FWR"); // rotated: text runs along the band
        sb.Append($"^FO{w - f(16)},{x0}^A0R,{f(14)},{f(14)}^FD{Z(p.FamilyName.ToUpperInvariant())}, {Z(p.GivenNames)}^FS");
        sb.Append($"^FO{w - f(32)},{x0}^A0R,{f(11)},{f(11)}^FDDOB {p.DateOfBirth:dd/MM/yyyy}  {Z(p.Sex.ToString())}^FS");
        sb.Append($"^FO{w - f(46)},{x0}^A0R,{f(11)},{f(11)}^FDNHI {Z(p.Nhi ?? "not recorded")}  MRN {Z(p.Mrn)}^FS");
        if (hasAllergy) sb.Append($"^FO{w - f(32)},{x0 + Dots(85, dpi)}^A0R,{f(14)},{f(14)}^FR^FD ALLERGY ^FS");
        sb.Append($"^FO{f(4)},{x0 + Dots(110, dpi)}^BY2^BCR,{Math.Max(f(20), w / 3)},N,N,N^FD{Z(p.Nhi ?? p.Mrn)}^FS");
        sb.Append("^PQ1^XZ");
        return sb.ToString();
    }

    /// <summary>Patient label (e.g. 50 × 25 mm) for forms, specimens and notes.</summary>
    public static string Label(Patient p, int copies, LabelPrinter? printer = null)
    {
        var dpi = printer?.Dpi ?? 300;
        var w = Dots(printer?.WidthMm ?? 50, dpi); var len = Dots(printer?.LengthMm ?? 25, dpi);
        int f(int pt) => (int)Math.Round(pt * dpi / 72.0);
        var m = Dots(2, dpi);
        var sb = new StringBuilder();
        sb.Append("^XA^CI28").Append($"^PW{w}^LL{len}");
        sb.Append($"^FO{m},{m}^A0N,{f(10)},{f(10)}^FB{w - 2 * m},1,0,L^FD{Z(p.FamilyName.ToUpperInvariant())}, {Z(p.GivenNames)}^FS");
        sb.Append($"^FO{m},{m + f(12)}^A0N,{f(8)},{f(8)}^FDDOB {p.DateOfBirth:dd/MM/yyyy}  {Z(p.Sex.ToString())}^FS");
        sb.Append($"^FO{m},{m + f(22)}^A0N,{f(8)},{f(8)}^FDNHI {Z(p.Nhi ?? "—")}  MRN {Z(p.Mrn)}^FS");
        sb.Append($"^FO{m},{m + f(32)}^BY2^BCN,{Math.Max(f(12), len - m - (m + f(32)))},N,N,N^FD{Z(p.Nhi ?? p.Mrn)}^FS");
        sb.Append($"^PQ{Math.Clamp(copies, 1, 50)}^XZ");
        return sb.ToString();
    }

    public async Task<(string? Error, string Zpl)> BuildAsync(Guid patientId, PrinterKind kind, int copies, Guid? printerId, CancellationToken ct = default)
    {
        var p = await db.Patients.AsNoTracking().Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == patientId && x.MergedIntoId == null, ct);
        if (p is null) return ("Patient not found.", "");
        var printer = printerId is { } pid ? await db.Printers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid && x.IsActive, ct)
            : await db.Printers.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind && x.IsDefault && x.IsActive, ct);
        var hasAllergy = p.Alerts.Any(a => a.IsActive && a.Type == AlertType.Allergy);
        var zpl = kind == PrinterKind.Wristband ? string.Concat(Enumerable.Repeat(Wristband(p, hasAllergy, printer), Math.Clamp(copies, 1, 3))) : Label(p, copies, printer);
        return (null, zpl);
    }

    /// <summary>Sends ZPL to a network printer. Returns an error message, or null when sent.</summary>
    public async Task<string?> PrintAsync(Guid patientId, PrinterKind kind, int copies, Guid? printerId, string user, CancellationToken ct = default)
    {
        var printer = printerId is { } pid ? await db.Printers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid && x.IsActive, ct)
            : await db.Printers.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind && x.IsDefault && x.IsActive, ct);
        if (printer is null) return $"No {kind.ToString().ToLowerInvariant()} printer is set up. An administrator can add one in Organisation set-up → Printers.";
        if (printer.Language == PrinterLanguage.Dymo) return "This is a DYMO printer – print from the browser (Print labels).";
        if (!AllowedPort(printer.Port)) return "The printer port is not allowed.";
        var (error, zpl) = await BuildAsync(patientId, kind, copies, printer.Id, ct);
        if (error is not null) return error;
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            await tcp.ConnectAsync(printer.Host, printer.Port, cts.Token);
            await using var s = tcp.GetStream();
            await s.WriteAsync(Encoding.UTF8.GetBytes(zpl), cts.Token);
            await s.FlushAsync(cts.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return $"Could not reach the printer '{printer.Name}' ({printer.Host}:{printer.Port}). Check it is switched on and connected to the network.";
        }
        await audit.WriteAsync(user, "Print", kind == PrinterKind.Wristband ? "Wristband" : "PatientLabel", patientId.ToString(), patientId.ToString(), reason: $"{copies} copy/copies on {printer.Name}", ct: ct);
        return null;
    }
}

// =====================================================================================================================
// Emergency transfer record: what was sent with the patient (the pack itself is assembled by the web host).
// =====================================================================================================================

public sealed class TransferRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public string ReceivingFacility { get; set; } = "";
    public string Reason { get; set; } = "";
    /// <summary>SBAR handover.</summary>
    public string? Situation { get; set; }
    public string? Background { get; set; }
    public string? Assessment { get; set; }
    public string? Recommendation { get; set; }
    public string? Escort { get; set; }
    public string? TransportMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed record TransferEdit(Guid PatientId, Guid? BookingId, string ReceivingFacility, string Reason, string? Situation, string? Background, string? Assessment,
    string? Recommendation, string? Escort, string? TransportMode);
