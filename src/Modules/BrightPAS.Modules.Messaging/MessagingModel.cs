using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BrightPAS.Core.Integration;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Messaging;

/// <summary>
/// TDS's own messaging engine (spec 6.2, ADR 0021): every interface, one encrypted message store, queues with retry and dead letters,
/// routes with versioned mappings and translation tables, monitoring and alerts. Built so it can later run on its own server (PAS-INT).
/// </summary>
public sealed class MessagingModule : IPasModule
{
    public const string Id = "messaging";
    public ModuleManifest Manifest { get; } = new(Id, "Messaging & workflow engine", typeof(MessagingModule).Assembly.GetName().Version ?? new Version(0, 0),
        MsgDbContext.Schema, ["patients"], "Interfaces, message store, queues, routing, mappings and translation tables.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<MsgDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, MsgDbContext.Schema));
        services.AddScoped<MessagingService>();
        services.AddScoped<MappingService>();
        services.AddScoped<FlowService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageTap, MessageStoreTap>());
    }
}

public enum InterfaceDirection { Inbound = 1, Outbound = 2 }

/// <summary>How messages arrive or leave. Monitored = traffic another BrightPAS module sends itself (HealthLink, SMS, email), recorded here for one view.</summary>
/// <summary>DicomListen = BrightPAS is a DICOM node (C-STORE and C-ECHO SCP) for devices.</summary>
/// <summary>HttpReceive = a REST endpoint on BrightPAS that other systems POST to (with a secret key).</summary>
public enum Transport { MllpListen = 1, MllpSend = 2, FolderWatch = 3, FolderWrite = 4, HttpPost = 5, Monitored = 6, DicomListen = 7, HttpReceive = 8 }

/// <summary>Dicom messages are stored as the DICOM JSON model (PS3.18 Annex F) without pixel data.</summary>
public enum MessageFormat { Hl7v2 = 1, Json = 2, Fhir = 3, Text = 4, Dicom = 5, Xml = 6, X12 = 7 }

/// <summary>What BrightPAS does with an inbound message before routing it on.</summary>
public enum InboundHandler { None = 0, LabResults = 1 }

/// <summary>A connection to or from another system.</summary>
public sealed class Interface
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Short unique code used by other modules and in logs (e.g. "labs-in").</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public InterfaceDirection Direction { get; set; }
    public Transport Transport { get; set; }
    public MessageFormat Format { get; set; } = MessageFormat.Hl7v2;
    public InboundHandler Handler { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? FolderPath { get; set; }
    public string? Url { get; set; }
    /// <summary>DICOM: BrightPAS's own AE title on this listener (called AE).</summary>
    public string? AeTitle { get; set; }
    /// <summary>HttpPost: Authorization header value sent. HttpReceive: the key callers must send (X-BrightPAS-Key). Encrypted.</summary>
    public byte[]? Secret { get; set; }
    /// <summary>For Monitored interfaces: which module records here ("healthlink", "sms", "email").</summary>
    public string? SystemKey { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Raise an alert after this many failures in a row.</summary>
    public int AlertAfterFailures { get; set; } = 3;
    /// <summary>Raise an alert when nothing has been received/sent for this many hours (blank = never).</summary>
    public int? SilentAlertHours { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>A system or device allowed to connect to a listener: IP address, and for DICOM its AE title (both must match).</summary>
public sealed class AllowedClient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterfaceId { get; set; }
    public string Address { get; set; } = "";
    public string? AeTitle { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// A message flow drawn on the designer canvas (like a Rhapsody route): an input communication point, then filters, decisions and mappers,
/// ending at one or more output communication points. Messages arriving on the input interface run through every active flow that starts there.
/// </summary>
public sealed class Flow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>Filter = stop unless the conditions match; Decision = Yes/No branches; Mapper = run a mapping; Output = send on an interface.</summary>
public enum NodeKind { Input = 1, Filter = 2, Decision = 3, Mapper = 4, Output = 5 }

public enum MatchMode { All = 1, Any = 2 }

public sealed class FlowNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public NodeKind Kind { get; set; }
    public string Label { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public Guid? InterfaceId { get; set; }
    public Guid? MappingId { get; set; }
    public MatchMode Match { get; set; } = MatchMode.All;
}

public sealed class FlowCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public int Sequence { get; set; }
    public string Source { get; set; } = "";
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;
    public string? Value { get; set; }
}

public enum EdgeBranch { Next = 1, Yes = 2, No = 3 }

public sealed class FlowEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public EdgeBranch Branch { get; set; } = EdgeBranch.Next;
}

public enum MessageStatus
{
    Received = 1, Processed = 2, Queued = 3, Sending = 4, Sent = 5, Failed = 6, DeadLetter = 7, Filtered = 8, Recorded = 9, Rejected = 10,
}

/// <summary>One message in or out. Content is encrypted; the SHA-256 is of the plain text.</summary>
public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterfaceId { get; set; }
    public InterfaceDirection Direction { get; set; }
    public MessageStatus Status { get; set; }
    public string? MessageType { get; set; }
    public string? ControlId { get; set; }
    /// <summary>Accession / order / letter number for searching.</summary>
    public string? Reference { get; set; }
    public Guid? PatientId { get; set; }
    public string? Nhi { get; set; }
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public int SizeBytes { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? AckCode { get; set; }
    public string? AckText { get; set; }
    /// <summary>The message this one was routed, replayed or resent from.</summary>
    public Guid? ParentId { get; set; }
    /// <summary>The flow that produced this outbound message.</summary>
    public Guid? FlowId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Each step a message went through (received, matched, routed, sent, retried…).</summary>
public sealed class MessageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Step { get; set; } = "";
    public string? Detail { get; set; }
}

public enum MissingCode { PassThrough = 1, Blank = 2, UseDefault = 3, Reject = 4 }

/// <summary>Translates codes between systems (sex codes, ethnicity, procedure names, funder codes…).</summary>
public sealed class TranslationTable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MissingCode WhenMissing { get; set; } = MissingCode.PassThrough;
    public string? DefaultValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class TranslationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    public string FromCode { get; set; } = "";
    public string ToCode { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>A named mapping (e.g. "Awanui ORU → PAS FHIR"). Its versions hold the rules; only the Live version is used.</summary>
public sealed class Mapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public MessageFormat SourceFormat { get; set; }
    public MessageFormat TargetFormat { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public enum VersionStatus { Draft = 1, AwaitingApproval = 2, Live = 3, Retired = 4 }

/// <summary>How the output starts: a copy of the input (change only what the rules say) or the template text.</summary>
public enum StartFrom { CopySource = 1, Template = 2 }

public sealed class MappingVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MappingId { get; set; }
    public int Number { get; set; }
    public VersionStatus Status { get; set; } = VersionStatus.Draft;
    public StartFrom StartFrom { get; set; } = StartFrom.CopySource;
    public string? Template { get; set; }
    /// <summary>Synthetic sample message used for the live preview (never real patient data).</summary>
    public string? SampleInput { get; set; }
    public string? ChangeNote { get; set; }
    public string EditedBy { get; set; } = "";
    public DateTimeOffset EditedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public enum RuleFunction { Copy = 1, Constant = 2, Translate = 3, Upper = 4, Lower = 5, Date = 6, Join = 7, Split = 8, Default = 9, Substring = 10, Clear = 11 }

public enum ConditionOperator { Equals = 1, NotEquals = 2, Contains = 3, StartsWith = 4, IsEmpty = 5, NotEmpty = 6, In = 7 }

/// <summary>Sets one target field. Source paths: HL7 "PID-5.1", "OBX[*]-5"; JSON "patient.name[0].family". Join takes several sources separated by " + ".</summary>
public sealed class MappingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public int Sequence { get; set; }
    public string Target { get; set; } = "";
    public string? Source { get; set; }
    public RuleFunction Function { get; set; } = RuleFunction.Copy;
    public string? Argument { get; set; }
    public Guid? TranslationTableId { get; set; }
    public string? ConditionSource { get; set; }
    public ConditionOperator? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
}

/// <summary>The message is only passed on when every filter matches (e.g. MSH-9.1 Equals ORU).</summary>
public sealed class FilterRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public int Sequence { get; set; }
    public string Source { get; set; } = "";
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;
    public string? Value { get; set; }
}

public enum AlertKind { Failures = 1, Silent = 2, Stopped = 3, DeadLetter = 4 }

public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterfaceId { get; set; }
    public AlertKind Kind { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
    public string? ClearedBy { get; set; }
}

public sealed class MsgSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Outbound attempts before a message goes to the dead-letter list.</summary>
    public int MaxAttempts { get; set; } = 8;
    /// <summary>Support contact emailed when an alert is raised (needs the Reminders email sender).</summary>
    public string? AlertEmail { get; set; }
    /// <summary>A second person must approve a mapping version before it goes live.</summary>
    public bool RequireSecondApprover { get; set; } = true;
}

public sealed class MsgDbContext(DbContextOptions<MsgDbContext> options) : DbContext(options)
{
    public const string Schema = "msg";
    public DbSet<Interface> Interfaces => Set<Interface>();
    public DbSet<AllowedClient> AllowedClients => Set<AllowedClient>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowNode> Nodes => Set<FlowNode>();
    public DbSet<FlowCondition> Conditions => Set<FlowCondition>();
    public DbSet<FlowEdge> Edges => Set<FlowEdge>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageEvent> Events => Set<MessageEvent>();
    public DbSet<TranslationTable> Tables => Set<TranslationTable>();
    public DbSet<TranslationEntry> Entries => Set<TranslationEntry>();
    public DbSet<Mapping> Mappings => Set<Mapping>();
    public DbSet<MappingVersion> Versions => Set<MappingVersion>();
    public DbSet<MappingRule> Rules => Set<MappingRule>();
    public DbSet<FilterRule> Filters => Set<FilterRule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<MsgSettings> Settings => Set<MsgSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Interface>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Transport).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Format).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Handler).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Host).HasMaxLength(255);
            e.Property(x => x.FolderPath).HasMaxLength(260);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.AeTitle).HasMaxLength(16);
            e.Property(x => x.SystemKey).HasMaxLength(20);
            e.HasIndex(x => x.SystemKey).IsUnique().HasFilter("[SystemKey] IS NOT NULL");
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<AllowedClient>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(45);
            e.Property(x => x.AeTitle).HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(100);
            e.HasIndex(x => new { x.InterfaceId, x.Address }).IsUnique();
            e.HasOne<Interface>().WithMany().HasForeignKey(x => x.InterfaceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Flow>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<FlowNode>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Label).HasMaxLength(60);
            e.Property(x => x.Match).HasConversion<string>().HasMaxLength(4);
            e.HasIndex(x => x.FlowId);
            e.HasIndex(x => new { x.InterfaceId, x.Kind });
            e.HasIndex(x => x.MappingId);
            e.HasOne<Flow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Interface>().WithMany().HasForeignKey(x => x.InterfaceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Mapping>().WithMany().HasForeignKey(x => x.MappingId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<FlowCondition>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(100);
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Value).HasMaxLength(200);
            e.HasIndex(x => new { x.NodeId, x.Sequence });
            e.HasOne<FlowNode>().WithMany().HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FlowEdge>(e =>
        {
            e.Property(x => x.Branch).HasConversion<string>().HasMaxLength(4);
            e.HasIndex(x => x.FlowId);
            e.HasIndex(x => new { x.FromNodeId, x.Branch });
            e.HasIndex(x => x.ToNodeId);
            e.HasOne<Flow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FlowNode>().WithMany().HasForeignKey(x => x.FromNodeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<FlowNode>().WithMany().HasForeignKey(x => x.ToNodeId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Message>(e =>
        {
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.MessageType).HasMaxLength(40);
            e.Property(x => x.ControlId).HasMaxLength(64);
            e.Property(x => x.Reference).HasMaxLength(64);
            e.Property(x => x.Nhi).HasMaxLength(10);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.AckCode).HasMaxLength(4);
            e.Property(x => x.AckText).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.InterfaceId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.Nhi);
            e.HasIndex(x => x.ControlId);
            e.HasIndex(x => x.Reference);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.FlowId);
            e.HasOne<Interface>().WithMany().HasForeignKey(x => x.InterfaceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Flow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<MessageEvent>(e =>
        {
            e.Property(x => x.Step).HasMaxLength(30);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.HasIndex(x => new { x.MessageId, x.At });
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TranslationTable>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.WhenMissing).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.DefaultValue).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<TranslationEntry>(e =>
        {
            e.Property(x => x.FromCode).HasMaxLength(100);
            e.Property(x => x.ToCode).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasIndex(x => new { x.TableId, x.FromCode }).IsUnique();
            e.HasOne<TranslationTable>().WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Mapping>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.SourceFormat).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TargetFormat).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
        });
        b.Entity<MappingVersion>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.StartFrom).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.ChangeNote).HasMaxLength(300);
            e.Property(x => x.EditedBy).HasMaxLength(100);
            e.Property(x => x.SubmittedBy).HasMaxLength(100);
            e.Property(x => x.ApprovedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.MappingId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.MappingId, x.Status });
            e.HasOne<Mapping>().WithMany().HasForeignKey(x => x.MappingId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MappingRule>(e =>
        {
            e.Property(x => x.Target).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(300);
            e.Property(x => x.Function).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Argument).HasMaxLength(300);
            e.Property(x => x.ConditionSource).HasMaxLength(100);
            e.Property(x => x.ConditionOperator).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.ConditionValue).HasMaxLength(200);
            e.HasIndex(x => new { x.VersionId, x.Sequence });
            e.HasIndex(x => x.TranslationTableId);
            e.HasOne<MappingVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TranslationTable>().WithMany().HasForeignKey(x => x.TranslationTableId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<FilterRule>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(100);
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Value).HasMaxLength(200);
            e.HasIndex(x => new { x.VersionId, x.Sequence });
            e.HasOne<MappingVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Alert>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.ClearedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.InterfaceId, x.ClearedAt });
            e.HasOne<Interface>().WithMany().HasForeignKey(x => x.InterfaceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MsgSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AlertEmail).HasMaxLength(254);
        });
    }
}

public sealed class MsgDbContextFactory : IDesignTimeDbContextFactory<MsgDbContext>
{
    public MsgDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MsgDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", MsgDbContext.Schema)).Options);
}
