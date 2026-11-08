namespace BrightPAS.Modules.Patients;

/// <summary>A registered patient. Identity fields follow HISO 10046; ethnicity follows HISO 10001.</summary>
public sealed class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>TDS medical record number, e.g. TDS0000123.</summary>
    public required string Mrn { get; set; }
    /// <summary>National Health Index number (validated, upper case). Optional until confirmed with the NHI service.</summary>
    public string? Nhi { get; set; }
    public required string FamilyName { get; set; }
    public required string GivenNames { get; set; }
    public string? PreferredName { get; set; }
    /// <summary>Upper-case, macron-free search keys so "Māori" and "Maori" match.</summary>
    public string FamilyNameSearch { get; set; } = string.Empty;
    public string GivenNamesSearch { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public BirthSex Sex { get; set; }
    /// <summary>Gender as stated by the patient (free text/code until the HISO gender code set is configured).</summary>
    public string? Gender { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Suburb { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    /// <summary>HPI facility id of the enrolled GP practice.</summary>
    public string? GpPracticeHpiFacilityId { get; set; }
    public bool IsTestPatient { get; set; }
    /// <summary>Set when this record was merged into another (the survivor); merged records are hidden from search.</summary>
    public Guid? MergedIntoId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<PatientEthnicity> Ethnicities { get; set; } = [];
    public List<PatientAlert> Alerts { get; set; } = [];
}

public enum BirthSex { Unknown = 0, Female = 1, Male = 2, Indeterminate = 3 }

/// <summary>Up to six ethnicities per patient, HISO 10001 level-4 (5-digit) codes, in the order given.</summary>
public sealed class PatientEthnicity
{
    public Guid PatientId { get; set; }
    public int Order { get; set; }
    public required string Code { get; set; }
}

public sealed class PatientAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public required string Description { get; set; }
    public bool IsActive { get; set; } = true;
    public required string RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public enum AlertType { Allergy = 1, Infection = 2, Clinical = 3, Safety = 4, Administrative = 5 }

public enum AlertSeverity { Information = 1, Warning = 2, Critical = 3 }

/// <summary>Single-row counter for MRN allocation.</summary>
public sealed class MrnCounter
{
    public int Id { get; set; } = 1;
    public long NextValue { get; set; } = 1;
}
