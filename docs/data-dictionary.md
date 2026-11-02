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
