# Data Dictionary

Schema `core` (platform). Sensitivity: **P** = public/configuration, **I** = internal, **R** = restricted (patient-identifiable). An Excel version will be generated from the model as the system grows.

## core.Organisations
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | P |
| Name | nvarchar(200) | No | Legal organisation name | P |
| HpiOrganisationId | nvarchar(20) | Yes | HPI organisation identifier (maps to FHIR Organization.identifier) | P |

## core.Sites
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | P |
| OrganisationId | uniqueidentifier | No | FK → Organisations | P |
| Name | nvarchar(200) | No | Site name | P |
| HpiFacilityId | nvarchar(20) | Yes | HPI facility identifier (FHIR Location.identifier; NMDS facility code) | P |
| IsActive | bit | No | Inactive sites are hidden from booking | P |

## core.Facilities
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | P |
| SiteId | uniqueidentifier | No | FK → Sites | P |
| Name | nvarchar(200) | No | Functional area name | P |
| Type | nvarchar(30) | No | SurgicalSuite, Endoscopy, Recovery, Cssd, Clinic, Administration | P |
| IsActive | bit | No | Active flag | P |

## core.Locations
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | P |
| FacilityId | uniqueidentifier | No | FK → Facilities | P |
| Code | nvarchar(20) | No | Short code, unique within the facility (e.g. TH1); used as DICOM scheduled station / room | P |
| Name | nvarchar(200) | No | Display name | P |
| Type | nvarchar(30) | No | Theatre, ProcedureRoom, PacuBay, Stage2Recovery, ConsultRoom, CssdZone, Office | P |
| IsActive | bit | No | Inactive locations (e.g. future Theatre 2) cannot be booked | P |
| SortOrder | int | No | Display order | P |

## core.AuditEvents (SQL Server append-only LEDGER table)
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Sequence | bigint | No | Primary key; gap-free, assigned by the application | I |
| OccurredAt | datetimeoffset | No | When the action happened (UTC offset stored) | I |
| UserId | nvarchar(100) | No | User who acted | I |
| Action | nvarchar(50) | No | View, Create, Update, Print, Export, BreakGlass, Login, … | I |
| EntityType | nvarchar(100) | No | Type of record acted on | I |
| EntityId | nvarchar(100) | No | Id of record acted on | I |
| PatientId | nvarchar(100) | Yes | Patient concerned (for patient access reports) | R |
| Reason | nvarchar(500) | Yes | Stated reason (mandatory for break-glass) | R |
| Workstation | nvarchar(100) | Yes | Device/workstation name | I |
| PreviousHash | nvarchar(64) | No | Hash of previous record (chain) | I |
| Hash | nvarchar(64) | No | SHA-256 of this record + previous hash | I |

Retention: at least 10 years. Records are never updated or deleted (enforced by the ledger table and application).

## core.SetupState (single row, Id = 1)
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | int | No | Always 1 (check constraint) | I |
| BootstrapCodeHash | nvarchar(200) | Yes | PBKDF2-SHA256 hash of the one-time first-administrator code (code itself never stored) | I |
| BootstrapCodeCreatedAt | datetimeoffset | Yes | Code expires 7 days after this | I |
| BootstrapCodeUsedAt | datetimeoffset | Yes | Set when the first administrator completed MFA enrolment | I |
| InstalledVersion | nvarchar(50) | Yes | Platform version last installed by Setup | P |
| InstalledAt | datetimeoffset | Yes | When Setup last ran | P |

# Schema `identity`

Standard ASP.NET Core Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`). Passwords are stored only as salted PBKDF2 hashes; authenticator keys and recovery codes are held in `AspNetUserTokens`. Permissions are role claims of type `pas:perm`.

## identity.AspNetUsers – PAS additions
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| DisplayName | nvarchar(200) | No | Name shown in the application | I |
| IsActive | bit | No | Inactive accounts cannot sign in (leavers) | I |
| CreatedAt | datetimeoffset | No | Account creation time | I |
| LastSignInAt | datetimeoffset | Yes | Last successful sign-in (password + MFA) | I |
| HpiCpn | nvarchar(20) | Yes | HPI Common Person Number for registered practitioners (FHIR Practitioner.identifier) | I |
| TwoFactorEnabled | bit | No | Must be true before the account can be used | I |

## identity.BreakGlassGrants
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | I |
| UserId | uniqueidentifier | No | User granted emergency access | I |
| PatientId | nvarchar(100) | No | Patient record accessed | R |
| Reason | nvarchar(500) | No | Mandatory reason (≥ 10 characters) | R |
| GrantedAt / ExpiresAt | datetimeoffset | No | Access lasts 30 minutes | I |
| ReviewStatus | nvarchar(20) | No | Pending, Appropriate, Inappropriate | I |
| ReviewedBy / ReviewedAt / ReviewNotes | various | Yes | Privacy Officer review | R |

# Schema `patients`

## patients.Patients
| Column | Type | Null | Description | Sensitivity | Mapping |
|---|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | I | FHIR Patient.id |
| Mrn | nvarchar(20) | No | TDS record number, unique (TDS0000001…) | R | Patient.identifier (local) |
| Nhi | nchar(7) | Yes | NHI, validated (HISO 10046), unique when present | R | Patient.identifier (NHI); HL7 PID-3 |
| FamilyName / GivenNames / PreferredName | nvarchar | No/No/Yes | Names as given (macrons kept) | R | Patient.name; PID-5 |
| FamilyNameSearch / GivenNamesSearch | nvarchar | No | Upper-case, macron-free search keys | R | – |
| DateOfBirth | date | No | Date of birth | R | Patient.birthDate; PID-7; NMDS |
| Sex | nvarchar(15) | No | Unknown, Female, Male, Indeterminate | R | Patient.extension (sex at birth); PID-8 |
| Gender | nvarchar(50) | Yes | Gender as stated (HISO gender code set to be configured) | R | Patient.gender |
| MobilePhone / HomePhone / Email | nvarchar | Yes | Contact details (reminders) | R | Patient.telecom; PID-13 |
| AddressLine1/2, Suburb, City, Postcode | nvarchar | Yes | Residential address (NZDep lookup later) | R | Patient.address; PID-11 |
| GpPracticeHpiFacilityId | nvarchar(20) | Yes | Enrolled GP practice (HPI) | R | Patient.generalPractitioner |
| IsTestPatient | bit | No | True for test NHIs (Z…) – excluded from reporting | I | – |
| CreatedAt / CreatedBy / UpdatedAt | various | – | Record history | I | – |

Planned: NHI, contact and address columns encrypted with SQL Server Always Encrypted (spec 5).

## patients.PatientEthnicities
| Column | Type | Null | Description | Mapping |
|---|---|---|---|---|
| PatientId | uniqueidentifier | No | FK → Patients (part of key) | – |
| Order | int | No | 1–6, order given by the patient (part of key) | – |
| Code | nvarchar(5) | No | HISO 10001:2017 level-4 ethnicity code | Patient.extension (ethnicity); NMDS |

## patients.Alerts
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id | uniqueidentifier | No | Primary key | I |
| PatientId | uniqueidentifier | No | FK → Patients | R |
| Type | nvarchar(20) | No | Allergy, Infection, Clinical, Safety, Administrative | R |
| Severity | nvarchar(20) | No | Information, Warning, Critical | R |
| Description | nvarchar(500) | No | Alert text | R |
| IsActive | bit | No | Current alert | R |
| RecordedBy / RecordedAt | various | No | Who recorded it and when | I |

## patients.MrnCounter (single row)
| Column | Type | Description |
|---|---|---|
| Id | int | Always 1 |
| NextValue | bigint | Next MRN number (concurrency-checked) |
