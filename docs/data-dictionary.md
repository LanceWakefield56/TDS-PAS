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


## core.Sites (0.8.0 additions)
AddressLine1/2, Suburb, City, Postcode, Phone, Email – the clinic letterhead and {Clinic…} placeholders.

## core.Clinicians
Name (unique, as printed), Type (Surgeon/Anaesthetist/Endoscopist/Physician/Other), Specialty, HpiCpn (2 digits + 4 letters), Phone, Email, PracticeName, UserName (optional link to a BrightPAS account), IsActive.

## core.Procedures
Code (unique), Name, Service (Surgical/Endoscopy), DefaultMinutes, DefaultAnaesthetic, SideRequired, FeeCode, IsActive.

## core.ModuleSetup
One row per module that has completed its guided set-up (ADR 0014). ModuleId (PK, e.g. `referrals`), SetUpAt, SetUpBy, SkippedChecks (optional steps not done at the time). No row = "Needs set-up".

## core.LicenceState (single row, Id = 1)
| Column | Type | Notes |
|---|---|---|
| InstallationId | uniqueidentifier | Random; with the server's machine id forms the installation code (ADR 0009) |
| LicenceText | nvarchar(max) | Signed licence file as installed |
| LicenceInstalledAt / LicenceInstalledBy | datetimeoffset / nvarchar(100) | |
| ActivationText | nvarchar(max) | Latest signed activation token from the licence server |
| ActivationCheckedAt / ActivationError | datetimeoffset / nvarchar(500) | |
| LastKnownModules | nvarchar(500) | Modules of the last valid licence – kept viewable if the licence later fails |
| LastSeenAt | datetimeoffset | Updated every 15 min; clock-rollback detection |

## labs.Messages
Every inbound HL7 message as received. Id, ReceivedAt, Source (MLLP/Upload), ControlId (MSH-10), SendingFacility (MSH-4), MessageType (MSH-9), Raw (nvarchar(max)), Outcome (Accepted/Duplicate/Rejected), Error, ReceivedBy.

## labs.Reports
One OBR. PatientId (null = unmatched), MatchStatus (Matched/Unmatched/ManuallyMatched), MatchNote, MatchedBy; patient as sent (PatientNhi, PatientMrn, PatientFamilyName, PatientGivenNames, PatientDateOfBirth); Laboratory, PlacerOrderNumber (OBR-2), AccessionNumber (OBR-3), TestCode/TestName (OBR-4), RequestedBy/RequestedByCode (OBR-16), CollectedAt (OBR-7), ReportedAt (OBR-22), ResultStatus (OBR-25: Preliminary/Final/Corrected/Cancelled/Partial), IsAbnormal, Comment (NTE), ReceivedAt, SupersededById, AcknowledgedAt/By/Comment. Code systems: NZPOCS/LOINC for tests (spec 7.15).

## labs.Observations
One OBX. Sequence, Code/Name (OBX-3), ValueType (OBX-2), Value (OBX-5, repetitions joined by new lines), Units (OBX-6), ReferenceRange (OBX-7), Flag (OBX-8: H, L, HH, LL, A, AA, N), Comment (NTE after the OBX).

## theatre.Sessions
Theatre list: LocationId (core.Locations – active Theatre/ProcedureRoom only), LocationName, Date, StartTime, EndTime, Type (Surgical/Endoscopy/Mixed), Surgeon, Anaesthetist, Notes, IsCancelled, CreatedAt/By. No overlapping lists in one room.

## theatre.Bookings
SessionId, PatientId, Order, Procedure, Laterality (NotApplicable/Left/Right/Bilateral), DurationMinutes (5–720), Anaesthetic (General/Sedation/Regional/Local/None), Status (Booked → Confirmed → Arrived → InTheatre → Recovery → Discharged; Cancelled/DidNotAttend), StatusAt, CancelReason (required to cancel), Notes, CreatedAt/By. Planned times are calculated (list start + durations + 10-minute turnover).

## forms.Requests
Pre-admission questionnaire link: PatientId, BookingId, DefinitionVersion (PAQ-1), TokenHash (SHA-256 hex, unique), CreatedAt/By, ExpiresAt (14 days), Status (Sent/Opened/Submitted/Triaged/Cancelled/Expired), FailedAttempts, LockedUntil, OpenedAt, SubmittedAt, AnswersJson (question id → answer; "_details" suffix for details), FlagsJson ([{level, text}]), SuggestedCategory, TriageCategory (Green/Amber/Red), TriageOutcome, TriageNotes, TriagedBy/At.

## billing.Settings (single row)
SurgeonPercent (default 40), AnaesthetistPercent (20), GstPercent (15), EstimateValidDays (30), PaymentTermsDays (20), GstNumber, BankAccount, NextEstimateNumber, NextInvoiceNumber (concurrency tokens).

## billing.Fees
Code (unique, upper case), Description, Price (excl GST, decimal(12,2)), GstApplicable, Split (procedure fee shared with clinicians), SurgeonPercent/AnaesthetistPercent (optional per-item override), IsActive.

## billing.Documents
Estimate or invoice: Kind, Number (EST-000001 at creation; INV-000001 when issued), PatientId, BookingId, SourceEstimateId, Payer (SelfFunded/Insurer/Acc/HealthNz/Other), PayerName, PayerReference, Surgeon, Anaesthetist, Status (Draft/Issued/Accepted/Declined/Expired/Invoiced/PartPaid/Paid/Void), GstPercent (snapshot), CreatedAt/By, IssuedOn, DueOn (estimate valid-until / invoice due), Notes, VoidReason.

## billing.Lines
DocumentId, Sequence, Code, Description, Quantity, UnitPrice, GstApplicable, SurgeonPercent, AnaesthetistPercent (snapshots). Shares: surgeon = net × surgeon %, anaesthetist = net × anaesthetist %, facility = remainder; GST per line, rounded to cents (away from zero).

## billing.Payments
DocumentId, PaidOn, Amount, Method (Eftpos/CreditCard/BankTransfer/Insurer/Acc/Cash/Other), Reference, RecordedAt/By. Payments cannot exceed the balance; invoices with payments cannot be voided.

## reminders.Settings (single row, Id = 1)
SMS vendor (`Modica` default, `GenericHttp`, `TestOutbox`, `Off`), email sender (`Smtp`, `TestOutbox`, `Off`), what to send, next-day send hour, arrival lead minutes, quiet hours, clinic name/phone, `ConfirmationsFrom`, Modica URL/application/mask, generic vendor URL/header/body template, SMTP host/port/STARTTLS/user/from. **Restricted:** `*Protected` columns hold Data Protection–encrypted secrets (Modica password, vendor auth value, SMTP password, callback key).

## reminders.Templates
Kind (BookingConfirmation, DayBeforeReminder, FormLink, ReplyConfirmed, ReplyCancelRequested, OptOutConfirmed, Test) × Channel (Sms, Email) × Language (en, mi) – unique. Subject (email), Body (≤612 chars SMS, ≤4000 email), IsActive, UpdatedAt/By. Placeholders only from the allowed list (no clinical detail).

## reminders.Preferences
PatientId (PK), AllowSms, AllowEmail, Language (en/mi), UpdatedAt/By. No row = SMS and email allowed, English.

## reminders.OptOuts
Channel + Address (E.164 mobile or lower-case email; unique), CreatedAt, Source (e.g. "SMS reply STOP" or staff user). UEMA 2007 unsubscribe register.

## reminders.Messages
Outbound message: PatientId, BookingId, AppointmentDate, Kind, Channel, Address, Subject, Body (questionnaire link replaced by "[questionnaire link]"), SecretLink (**Restricted**, cleared when sent/closed), Status (Queued, Sending, Sent, Delivered, Failed, Cancelled, Suppressed), IgnoreQuietHours, Provider, ProviderMessageId, Attempts, NextAttemptAt, LastError, DedupeKey (unique when set), CreatedAt/By, SentAt, DeliveredAt. **Clinical-adjacent:** contact details and appointment dates.

## reminders.Replies
Inbound SMS: From (E.164), Body, ReceivedAt, Provider, ProviderMessageId, ReplyToProviderId, MatchedMessageId, PatientId, BookingId, Meaning (Confirm, Cancel, OptOut, OptIn, Unknown), ActionTaken, Status (New, Handled), HandledBy/At, Note.


## referrals.Referrals
A referral letter. Number (R000001…, unique), PatientId, ReceivedOn, Source (GeneralPractice/Specialist/SelfReferral/HealthNz/Acc/Insurer/Other), ReferrerName, ReferrerOrganisation, ReferrerHpi (HPI CPN or facility id), Specialist, Service (Surgical/Endoscopy), Funder (SelfPay/Insurer/Acc/HealthNz/Other), FunderReference (insurer approval, ACC claim, Health NZ referral id), Reason, Status (Received/Accepted/Declined/Withdrawn), TriagedAt/By, TriageNotes, DeclineReason, CreatedAt/By.

## referrals.Waitlist
A patient waiting for a procedure. ReferralId (null when added directly), PatientId, Procedure, Laterality, Anaesthetic, DurationMinutes, Service, Specialist, Funder (HealthNz = publicly funded, NBRS-reportable), Priority (Urgent/SemiUrgent/Routine), AddedOn (decision to treat – start of waiting time), TargetDate (AddedOn + target days for the priority), Status (Waiting/OnHold/Booked/Completed/Removed), HoldReason, HoldUntil (hold ends automatically), BookingId (theatre.Bookings), BookedFor, RemovedReason, Notes, StatusAt, CreatedAt/By. A cancelled or did-not-attend theatre booking returns the entry to Waiting; a discharged booking completes it.

## referrals.Settings (single row, Id = 1)
Target days per priority: UrgentDays (30), SemiUrgentDays (90), RoutineDays (120 – Health NZ's 4-month planned-care target); NextReferralNumber; UpdatedAt/By (set when an administrator confirms the targets in the set-up).

## theatre.Templates
Recurring list: LocationId, DayOfWeek, StartTime, EndTime, Type, Surgeon, Anaesthetist, EveryWeeks (1–8, counted from StartsOn), StartsOn, EndsOn, IsActive, CreatedAt/By.

## letters.Practices
GP practice: Name, HpiFacilityId (unique when set), HealthLinkEdi, address, Phone, Email, PreferredChannel (HealthLink/Post/Email), IsActive.

## letters.Templates
Code (unique), Name, Kind (Appointment/Admission/GpNotification/ReferralAcknowledgement/ReferralDeclined/Custom), Recipient (Patient/Gp/Referrer/Other), Subject, Body (placeholders in {Braces}, whitelisted), IsActive, Version, UpdatedAt/By.

## letters.Letters
Number (L000001…), PatientId, TemplateCode/Version, Kind, Recipient, RecipientName/Address, GpPracticeId, Subject, Body (locked when Final), BookingId, ReferralId, Status (Draft/Final/Sent/Acknowledged/Failed/Void), Channel (Print/HealthLink), PrintCount, MessageControlId (HL7 MSH-10), AckCode/AckText/AckAt, VoidReason, CreatedAt/By, Author, FinalisedAt/By, SentAt/By.

## letters.Settings (single row)
HealthLink Mode (Off/TestOutbox/FolderDrop), SenderEdi, OutboxFolder, InboxFolder, NextControlNumber, NextLetterNumber, UpdatedAt/By.

## theatre.Bookings (0.9.0 additions)
IsTentative, Payment (NotSet/SelfFunded/Insurer/Acc/HealthNz/NoCharge), DepositNote, Checklist (comma list: consent, form, estimate, approval, tests, anaesthetic, adult), BilledExternally, TheatreIn, TheatreOut, TraysNeeded, TraysUsed, LoanItems, ScopeId.

## theatre.Appointments
Clinic appointment: PatientId, Date, StartTime, DurationMinutes, Kind (Consultation/FollowUp/PhoneCall/PreAdmission/Other), Clinician, LocationId, NoCharge, IsTentative, Status (Booked/Confirmed/Arrived/Completed/Cancelled/DidNotAttend), CancelReason, Notes, CreatedAt/By.

## core.Clinicians (0.9.0)
Colour – calendar colour index 0–11.

## roster.Staff
Name (unique), Role, UserName (link to BrightPAS account), Colour (0–11), ContractedHours (per week), Phone, IsActive.

## roster.Shifts
StaffId, Date, AllDay, Start, End, BreakMinutes, Area, IsTentative, Note, CreatedAt/By. No overlapping timed shifts per person.

## roster.Templates
Regular shift: StaffId, DayOfWeek, AllDay, Start, End, BreakMinutes, Area, EveryWeeks, StartsOn, EndsOn, IsActive.

## roster.Leave
StaffId, From, To, Type (Annual/Sick/Training/Parental/Other), Status (Requested/Approved/Declined/Cancelled), Note, RequestedAt/By, DecidedAt/By.

## 0.10.0 additions

### core.Settings
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Key | nvarchar(60) | No | PK, e.g. export.protection, export.minPasswordLength, documents.maxUploadMegabytes, portal.linkDays | P |
| Value | nvarchar(500) | No | Setting value | P |
| UpdatedAt / UpdatedBy | datetimeoffset / nvarchar(100) | No | Last change | I |

### patients.Patients (new column)
| MergedIntoId | uniqueidentifier | Yes | Survivor record when this record was merged; hidden from search | R |

### patients.Documents
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| Id, PatientId, BookingId | uniqueidentifier | No/No/Yes | Keys | R |
| Category / Source | nvarchar(20)/(10) | No | Referral…Other / Upload, Scan, Portal | I |
| Title, FileName, ContentType, SizeBytes | | No | Description of the file | R |
| Sha256 | nvarchar(64) | No | Hash of the original file (tamper check) | I |
| Content | varbinary(max) | No | Encrypted contents (Data Protection) | R |
| UploadedAt/By, IsDeleted, DeletedReason/By/At | | | Audit fields; removal is a soft delete | I |

### patients.DocumentTexts (0.11.0)
| Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|
| DocumentId | uniqueidentifier | No | PK and FK → patients.Documents (cascade) | R |
| Status | nvarchar(10) | No | Pending, Done, NoText, Failed (indexed) | I |
| Method | nvarchar(10) | Yes | text (PDF text layer) or ocr | I |
| Content | varbinary(max) | Yes | Extracted text, **encrypted** (Data Protection) | R |
| CharCount, Attempts | int | No | Size of text; tries so far (max 3) | I |
| Message | nvarchar(300) | Yes | Last problem | I |
| ProcessedAt | datetimeoffset | Yes | When last read | I |

### patients.Corrections
Requested identity change: PatientId, New* fields, ClearNhi, Before (summary), Reason, Evidence, Status (Pending/Approved/Rejected/Cancelled), RequestedAt/By, DecidedAt/By, DecisionNote. **R**

### patients.Merges
SurvivorId, DuplicateId, DuplicateSummary, Reason, Status, RequestedAt/By, DecidedAt/By, DecisionNote, CompletedAt, Result (what moved). **R**

### patients.Printers
Name, Kind (Wristband/Label), Host, Port, Dpi, WidthMm, LengthMm, IsDefault, IsActive. **P**

### patients.Transfers
PatientId, BookingId, ReceivingFacility, Reason, Situation, Background, Assessment, Recommendation, Escort, TransportMode, CreatedAt/By. **R**

### roster.Staff (new columns) / roster.Swaps
PayrollCode, EarningsRate (I). Swaps: ShiftId, FromStaffId, ToStaffId, OfferedShiftId, Status (Requested/Accepted/Approved/Declined/Cancelled), Note, RequestedAt/By, AcceptedAt/By, DecidedAt/By, DecisionNote (I).

### referrals.Settings (new columns)
NbrsAgencyCode, NbrsFacilityCode, NbrsSpecialtySurgical, NbrsSpecialtyEndoscopy (P).

### cred.Credentials / cred.Privileges / cred.Settings
Credentials: ClinicianId, Type (Apc, VocationalScope, Indemnity, CredentialingApproval, Resuscitation, Other), Reference, Issuer, IssuedOn, ExpiresOn, Notes, VerifiedBy/At, IsActive (I). Privileges: ClinicianId, Service, ProcedureCode (null = whole service), ApprovedOn, ReviewDue, Conditions, ApprovedBy, IsActive (I). Settings: RequiredTypes, WarningDays, CheckPrivileges (P).

### portal.Access / portal.Requests / portal.Delegates
Access: PatientId, TokenHash (SHA-256; token never stored), CreatedAt/By, ExpiresAt, RevokedAt/By, FailedAttempts, LockedUntil, LastUsedAt, UploadsToday, UploadDay (R). Requests: PatientId, AccessId, Kind, BookingId, Message, Status, CreatedAt, HandledAt/By, Response (R). Delegates: UserName (PA), ClinicianId, CreatedAt/By (I).


## 0.10.1 – normalisation and reporting
- **theatre.BookingChecks** (BookingId, ItemKey) PK; CheckedAt, CheckedBy – replaces Bookings.Checklist (comma-separated). I
- **forms.Answers** (RequestId, QuestionKey) PK; Value – replaces Requests.AnswersJson. R
- **forms.Flags** (RequestId, Sequence) PK; Level, Text – replaces Requests.FlagsJson (flags as shown to the triage nurse). R
- **cred.RequiredTypes** (Type) PK – replaces Settings.RequiredTypes (comma-separated). P
- **patients.Printers**: + Language (Zpl/Dymo), MediaCode (DYMO stock). P
- **reporting.*** views/procedures and **maintenance.*** procedures: see 14-reporting.sql; role brightpas_reporting.

### Normal-form review
All tables have a single-purpose primary key; repeating groups removed (1NF); no partial dependencies on composite keys (2NF). Intentional point-in-time copies (not 3NF violations in meaning, as they record what was true then): billing.Documents.Surgeon/Anaesthetist and Lines.Description, patients.Corrections.Before, patients.Merges.DuplicateSummary, letters.Letters.Author/RecipientName/RecipientAddress. Closed in 0.10.2: clinicians and rooms are referenced by id only – see below.


## 0.10.2 – clinician and room references
| Table | Column | Type | Null | Description | Sensitivity |
|---|---|---|---|---|---|
| theatre.Sessions | SurgeonId | uniqueidentifier | No | FK → core.Clinicians (replaces Surgeon text) | I |
| theatre.Sessions | AnaesthetistId | uniqueidentifier | Yes | FK → core.Clinicians (replaces Anaesthetist text) | I |
| theatre.Sessions | (LocationName removed) | | | Room name now read from core.Locations via LocationId | |
| theatre.Templates | SurgeonId / AnaesthetistId | uniqueidentifier | No / Yes | FK → core.Clinicians | I |
| theatre.Appointments | ClinicianId | uniqueidentifier | No | FK → core.Clinicians (replaces Clinician text) | I |
| referrals.Referrals | SpecialistId | uniqueidentifier | No | FK → core.Clinicians (replaces Specialist text) | I |
| referrals.Waitlist | SpecialistId | uniqueidentifier | No | FK → core.Clinicians (replaces Specialist text) | I |
