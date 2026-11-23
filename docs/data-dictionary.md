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
Outbound message: PatientId (null for test messages sent from the settings page – 0.12.3; otherwise FK to patients.Patients), BookingId, AppointmentDate, Kind, Channel, Address, Subject, Body (questionnaire link replaced by "[questionnaire link]"), SecretLink (**Restricted**, cleared when sent/closed), Status (Queued, Sending, Sent, Delivered, Failed, Cancelled, Suppressed), IgnoreQuietHours, Provider, ProviderMessageId, Attempts, NextAttemptAt, LastError, DedupeKey (unique when set), CreatedAt/By, SentAt, DeliveredAt. **Clinical-adjacent:** contact details and appointment dates.

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
| Key | nvarchar(60) | No | PK, e.g. export.protection, export.minPasswordLength, documents.maxUploadMegabytes, portal.linkDays; demonstration systems only (0.12.3): demo.state (requested/running/done/failed, set by Setup --demo-data), demo.detail (summary or error), demo.banner (true = DEMONSTRATION SYSTEM bar) | P |
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
- **reporting.*** views/procedures and **maintenance.*** procedures: see 90-reporting.sql (was 14-reporting.sql); role brightpas_reporting.

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

## 0.12.0 – messaging engine (schema msg)
Sensitivity: **R** restricted (patient data), **I** internal, **S** secret (encrypted).

| Table | Key columns and meaning |
|---|---|
| msg.Interfaces | Id, Code (unique), Name, Direction (Inbound/Outbound), Transport (MllpListen, MllpSend, FolderWatch, FolderWrite, HttpPost, HttpReceive, DicomListen, SqlQuery, SqlWrite, Monitored), Format (Hl7v2, Fhir, Json, Dicom, Xml, X12, Text), Handler (None, LabResults), Host, Port, FolderPath, Url, AeTitle, SqlText, PollSeconds, WatermarkColumn, **Secret** (S – auth header / REST key / SQL connection string, encrypted), SystemKey (monitored modules), IsActive, AlertAfterFailures, SilentAlertHours, Notes, Created/Updated At/By. **I** |
| msg.AllowedClients | InterfaceId → Interfaces; Address (IP), AeTitle (DICOM devices), Description. **I** |
| msg.States | InterfaceId (PK/FK); Watermark, LastPolledAt, LastRows, LastError (SQL polling). **I** |
| msg.Messages | Id, InterfaceId, Direction, Status (Received, Processed, Queued, Sending, Sent, Failed, DeadLetter, Filtered, Recorded, Rejected), MessageType, ControlId, Reference (accession/order/letter), PatientId → patients.Patients, Nhi, **Content** (R, encrypted), Sha256, SizeBytes, Attempts, NextAttemptAt, LastError, AckCode, AckText, ParentId → Messages (routed/replayed from), FlowId → Flows, CreatedAt/By, CompletedAt. **R** |
| msg.Events | MessageId → Messages (cascade); At, Step, Detail – each step a message went through. **R** (details may name codes) |
| msg.Flows | Id, Name (unique), Description, IsActive, UpdatedAt/By. **I** |
| msg.Nodes | FlowId → Flows; Kind (Input, Filter, Decision, Mapper, Output), Label, X, Y (canvas), InterfaceId, MappingId, Match (All/Any). **I** |
| msg.Conditions | NodeId → Nodes; Sequence, Source (path), Operator, Value. **I** |
| msg.Edges | FlowId; FromNodeId, ToNodeId → Nodes; Branch (Next, Yes, No). **I** |
| msg.Mappings | Id, Name (unique), SourceFormat, TargetFormat, Description, CreatedAt/By. **I** |
| msg.Versions | MappingId; Number (unique per mapping), Status (Draft, AwaitingApproval, Live, Retired), StartFrom, Template, SampleInput (synthetic only), ChangeNote, Edited/Submitted/Approved By/At. **I** |
| msg.Rules | VersionId; Sequence, Target, Source, Function, Argument, TranslationTableId → Tables, ConditionSource/Operator/Value. **I** |
| msg.Filters | VersionId; Sequence, Source, Operator, Value. **I** |
| msg.Tables / msg.Entries | Translation tables: Name (unique), Description, WhenMissing (PassThrough/Blank/UseDefault/Reject), DefaultValue; entries FromCode (unique per table) → ToCode, Description. **I** |
| msg.Alerts | InterfaceId; Kind (Failures, DeadLetter, Silent, Stopped), Detail, RaisedAt, NotifiedAt, ClearedAt/By. **I** |
| msg.McpClients | Name, **TokenHash** (SHA-256 only), IsActive, ExpiresAt, LastUsedAt, CreatedAt/By. **S** |
| msg.Settings (single row) | MaxAttempts, AlertEmail, RequireSecondApprover, AiEnabled, AiEndpoint, AiModel, **AiKey** (S, encrypted), AiPrivacyReference, AiApprovedBy/At. **I** |

Cross-schema FK (90-reporting.sql): msg.Messages.PatientId → patients.Patients.

## 0.13.0 – Assets & CSSD (schema assets)
Classification letters as above (P = public/configuration, I = internal, R = restricted patient-linked). Cross-schema keys (90-reporting.sql): Equipment.LocationId → core.Locations; TrayIssues, ScopeEvents, StockMovements, Implants PatientId → patients.Patients and BookingId → theatre.Bookings; LoanSets.BookingId → theatre.Bookings.
- **assets.Suppliers**: Name (unique), ContactName, Email, Phone, XeroContactName (name used on Xero bills), IsActive. I
- **assets.Equipment**: AssetNumber (unique, upper case), Name, Category (Electromedical, Monitor, Washer, Steriliser, Aer, DryingCabinet, Endoscope, EndoscopyTower, Instrument, Furniture, It, Other), Make, Model, SerialNumber, LocationId, Owner, SupplierId, PurchasedOn, Value, WarrantyUntil, Risk (Low/Medium/High), Status (InService/OutOfService/Retired), OutOfServiceReason, LockedOutBy/At, NetworkAddress (devices that connect to BrightPAS), Notes, CreatedAt. I
- **assets.MaintenancePlans** (EquipmentId, Kind) unique: Kind (Ppm, Calibration, TestAndTag, BiomedicalSafety, Validation), IntervalMonths, NextDue, DoneBy. I
- **assets.MaintenanceRecords**: EquipmentId, Kind (+ Repair), DoneOn, DoneBy, Passed, TagNumber, Notes, RecordedBy/At. A failed test-and-tag, biomedical check or calibration locks the equipment out. I
- **assets.TraySets** (Code unique, Name, Specialty, ShelfLifeDays, IsActive) / **assets.TrayItems** (TraySetId, Sort, Description, Quantity): tray catalogue and count sheet. P
- **assets.Trays**: Barcode (unique, CODE-nn), TraySetId, Status (Dirty, Washed, Packed, Sterile, Issued, Quarantine, Retired), LastCycleId (steriliser load), SterileUntil, PackedBy/At, Notes, UpdatedAt. I
- **assets.Cycles**: LoadNumber (unique, W/S/L/A + yyMMdd-nnn), MachineId → Equipment, Kind (Wash, Steam, LowTemperature, Aer), MachineCycleNumber, Program, StartedAt, EndedAt, MaxTemperatureC, HoldSeconds, PressureKpa, A0, MachinePassed, ChemicalIndicator / BiologicalIndicator (NotUsed, Pending, Pass, Fail), BowieDickTest, Release (Pending, Released, Failed), ReleasedBy/At, ReleaseNote, Operator, Source ("Manual" or log file name), RawLog (machine log as received). I
- **assets.CycleItems**: CycleId, TrayInstanceId (or Description + Quantity for packs/devices). I
- **assets.TrayIssues**: TrayInstanceId, CycleId, BookingId, PatientId, IssuedAt/By, ReturnedAt – set-to-patient traceability and recall by load. R
- **assets.ScopeEvents**: ScopeId → Equipment, Kind (Use, LeakTest, ManualClean, AerCycle, DryingIn, DryingOut, MicroSample, Repair, Quarantine), At, By, Passed, CycleId, BookingId, PatientId, Notes. R
- **assets.LoanSets**: Number (unique), SupplierId, Description, ItemCount, BookingId, Status (Expected, Received, Checked, Reprocessed, Used, Returned), ReceivedAt, CheckedBy, CheckNotes, CycleId, ReturnedAt, UpdatedAt. I
- **assets.Nonconformances**: Number (unique), RaisedOn/By, Category, Description, CycleId, TrayInstanceId, EquipmentId, ImmediateAction, RootCause, CorrectiveAction, PreventiveAction, Status (Open, ActionsAgreed, Closed), ClosedBy/At. I
- **assets.StockItems**: Code (unique), Name, Unit, IsImplant, SupplierId, SupplierCode, ReorderLevel, ReorderQuantity, UnitCost, AccountCode (Xero), IsActive. P
- **assets.StockLots** (StockItemId, Lot) unique: Expiry, OnHand, ReceivedOn. I
- **assets.StockMovements**: StockItemId, LotId, Quantity (+/-), Kind (Receipt, Use, Adjust, Expired, Return), BookingId, PatientId, PurchaseOrderId, Reason, At, By. R
- **assets.Implants**: PatientId, BookingId, StockItemId, LotId, Lot, SerialNumber, Manufacturer, Site, ImplantedOn, RecordedBy/At – implant registry for recalls. R
- **assets.PurchaseOrders** (Number unique, SupplierId, Status Draft/Approved/Sent/PartReceived/Received/Invoiced/Cancelled, CreatedBy/At, ApprovedBy/At, Notes) / **assets.PurchaseOrderLines** (StockItemId, Quantity, UnitCost, QuantityReceived). I
- **assets.SupplierInvoices**: PurchaseOrderId, InvoiceNumber, InvoiceDate, DueDate, AmountExGst, Matched (within $1 or 1 % of goods received, or difference accepted), MatchNote, ExportedToXeroAt, RecordedBy/At. I
- **assets.Settings** (Id = 1): ScopeStorageHours (72), MaintenanceWarningDays (30), ReleaseNeedsSecondPerson, NextLoad/NextNc/NextPo/NextLoan counters. P
- Reporting views: **reporting.vTrayTrace**, **reporting.vImplant**, **reporting.vMaintenanceDue**.

## 0.14.0 – Clinical record (schema emr)
Cross-schema keys (90-reporting.sql): Episodes.BookingId → theatre.Bookings, Episodes.PatientId → patients.Patients. All tables R unless marked.
- **emr.Episodes**: BookingId (unique), PatientId, Stage, SedationOrAnaesthesia, AdmittedAt/By, EscortName, EscortPhone, OvernightCarer, EscortConfirmed, EscortOverrideReason, DischargedAt/By, DischargeDestination, FollowUpCallOn.
- **emr.Assessments** / **emr.AssessmentAnswers**: Kind (NursingAdmission, Vte, Falls, PressureInjury, Frailty, Delirium), Score, Risk (Low/Moderate/High), Actions, RecordedBy/At; answers as Key/Value.
- **emr.SafetyChecks** / **SafetyCheckItems** / **SafetyCheckTeam**: Phase (SignIn, TimeOut, SignOut), CompletedAt/By, Notes; confirmed item keys; Role + Name of each team member.
- **emr.IntraopRecords**: one per episode – theatre times, position, positioning aids, skin prep, diathermy unit/pad site/skin after, tourniquet site/pressure/on/off, smoke evacuation, wound class, drains, dressings, notes, UpdatedBy/At.
- **emr.Counts**: Stage (Initial, Additional, Closing, Final), Item, Expected, Counted, CountedBy, CheckedBy, At, Resolved, ResolutionNote.
- **emr.Specimens**: Number (per episode), Site, Test, Container, Status (Collected, Sent, ReceivedByLab), Courier, CollectedBy/At.
- **emr.Observations**: At, RespiratoryRate, SpO2, OnOxygen, OxygenLitres, SystolicBp, DiastolicBp, HeartRate, TemperatureC, Consciousness (ACVPU), Pain, Sedation, Ews, Escalation, Source (Manual/Device), DeviceId → assets.Equipment, RecordedBy.
- **emr.MedicationsGiven**: Medicine, Dose, Route, GivenAt, GivenBy, Barcode, ControlledDrugId, WitnessedBy, Notes.
- **emr.ControlledDrugs** (Name, Strength, Form, Balance, IsActive) / **emr.ControlledDrugRegister** (Movement: Received, Administered, Wasted, Returned, Correction; Quantity, BalanceAfter, By, WitnessedBy, Reference, MedicationId/PatientId when administered). I/R
- **emr.FluidBalance**: At, IsInput, Kind, Millilitres, RecordedBy.
- **emr.BloodUnits**: UnitNumber, Product, ReceivedAt, ColdChainOk, CheckedBy1/2, StartedAt, StoppedAt, Reaction, ReactionDetails, Returned.
- **emr.Notes**: Kind (Nursing, Medical, OperationNote, Anaesthetic, Discharge, PatientInstructions), Text, Author, At, Signed, AmendsNoteId.
- **emr.Templates**: Name, Kind, Body (placeholders {procedure}, {surgeon}, {anaesthetist}, {date}, {clinic_phone}). P
- Views: reporting.vEpisode, reporting.vControlledDrugRegister.

## 0.15.0 – Endoscopy (schema endo)
Cross-schema keys (90-reporting.sql): Procedures.PatientId → patients.Patients, BookingId → theatre.Bookings, EndoscopistId → core.Clinicians, UpperScopeId/LowerScopeId/TowerId → assets.Equipment, Media.PatientId → patients.Patients. All tables R unless marked.
- **endo.Procedures**: BookingId (unique), PatientId, Kind (Gastroscopy, Colonoscopy, FlexibleSigmoidoscopy, Bidirectional), Status (Planned, InProgress, Completed, Signed), EndoscopistId/Name, Assistants, UpperScopeId, LowerScopeId, TowerId, AccessionNumber (unique, E+yyMMdd+nnnn), StudyInstanceUid, NbspScreening, NbspNumber, OtherIndication, ConsentConfirmed, Sedation, SedationNotes, Co2, WaterAssisted, ScopeGuide, PositionChange, AbdominalPressure; UpperStartAt, UpperEndAt, UpperExtent (Oesophagus, Stomach, D1, D2, Failed), Retroflexion, UpperPhotoDocumentation; InsertedAt, CaecumAt, WithdrawalStartAt, EndedAt, LowerExtent (Rectum … Caecum, TerminalIleum, Anastomosis), AppendicealOrifice, IleocaecalValve, TerminalIleum, CaecumPhotographed, BbpsRight/Transverse/Left (0–3), RectalRetroflexion, IncompleteReason, Comfort (Gloucester 1–5), Complication, ComplicationDetails, Diagnosis, Recommendations, FollowUp, SurveillanceMonths, PatientAdvice, MppsUid, SignedBy/At, Created/Updated By/At.
- **endo.ProcedureIndications** (ProcedureId, Code) PK both.
- **endo.Findings**: ProcedureId, Number, Site, Kind, Grade, SizeMm, Description.
- **endo.Polyps**: ProcedureId, Number, Site, DistanceCm, SizeMm, Paris, Nice, Jnet, Technique, Lifted, Resection (EnBloc, Piecemeal, NotRemoved), Retrieved (Yes, No, Partial, NotApplicable), Pot, Complete (Yes, No, Uncertain), Clips, Tattooed, Notes, Histology, Dysplasia (None, Low, High), HistologyNotes, HistologyBy/At.
- **endo.Tattoos**: ProcedureId, Site, DistanceCm, Position (Distal, Proximal, Both, Opposite wall), Ink, VolumeMl, Injections, PolypId, Reason.
- **endo.Interventions**: ProcedureId, Kind (Biopsy, Clo, Clip, Banding, Dilatation, Apc, Injection, Haemostasis, ForeignBody, Other), Site, Count, Pot, Purpose, Details, Result.
- **endo.Media**: ProcedureId (null = unmatched), PatientId, Kind (Image, Video), Source (CaptureStation, Dicom, Upload), Status (Recording, Ready, Deleted), FileName (relative to the media folder), ContentType, Bytes, Sha256, Chunks, CapturedAt/By, DurationSeconds, Site, Caption, InReport, InPatientReport, Sort, FindingId, PolypId, SopInstanceUid (unique when set), DicomPatientId, DicomPatientName, DicomAccession, DeletedBy/At, DeleteReason.
- **endo.AdviceTexts**: Key (unique), Title, Body, UpdatedBy/At – patient report wording. P
- Settings (core.Settings): endo.mediaPath, endo.dicom.enabled/port/aeTitle/callingAes/anyTower, endo.videoRetentionDays, endo.target.* (cir, adr, withdrawal, retrieval, prep, comfort, upper). P
- Views: reporting.vEndoscopyProcedure, reporting.vEndoscopyPolyp.

### 0.15.1 additions
- endo.Procedures: GpLetterId → letters.Letters (report sent to GP), NbspExportedAt.
- endo.Media: AnnotatedFromId → endo.Media (annotated copy of), OriginalFileName, OriginalContentType (e.g. the TIFF a PNG was made from).
- **letters.LetterAttachments**: LetterId (PK, FK letters.Letters), FileName, ContentType, Content (varbinary) – PDF sent with a letter (OBX ED in HealthLink). R

## 0.16.0 – BrightPAS Scribe (schema scribe)
Cross-schema keys (90-reporting.sql): Sessions.PatientId → patients.Patients, BookingId → theatre.Bookings, NoteId → emr.Notes, DocumentId → patients.Documents; Consents.PatientId → patients.Patients. All tables R unless marked.
- **scribe.Settings** (single row): Provider (Off, BrightPas, Medow), Speech (Off, OnSite, Cloud), SpeechUrl, SpeechModel, SpeechKey (encrypted), Draft (TemplateOnly, OnSite, CloudClaude), DraftUrl, DraftModel, DraftKey (encrypted), CloudPrivacyReference, CloudApprovedBy/At, MedowPiaReference, MedowSupplierAssessment, MedowAgreementReference, MedowLaunchUrl, InboundTokenHash (SHA-256), MedowApprovedBy/At, AudioRetentionDays, UpdatedBy/At. P
- **scribe.Templates**: Code (unique), Name, Context, Sections ("Heading: instruction" per line), Version, Status (Draft, Approved, Retired), ApprovedBy/At, UpdatedBy/At. P
- **scribe.DictionaryTerms**: Term (unique), SoundsLike (comma-separated), Category, UpdatedBy/At. P
- **scribe.Consents**: PatientId, Given, Standing, Product, RecordedBy/At, WithdrawnBy/At.
- **scribe.Sessions**: PatientId, BookingId, Context, TemplateId, TemplateVersion, Provider, Status (Recording, Recorded, Transcribed, Drafted, Signed, Discarded), Clinician, ConsentId, AudioFolder, AudioType, AudioChunks, AudioBytes, DurationSeconds, AudioDeletedAt, SpeechEngine, DraftEngine, DeIdentified, Draft, SignedText, SignedBy/At, EditPercent, NoteId, DocumentId, DiscardReason, CreatedAt, UpdatedAt.
- **scribe.TranscriptSegments** (SessionId, Seq): Speaker, StartSeconds, EndSeconds, Text, Confidence.
- **scribe.DraftSentences** (SessionId, Seq): Section, Text, Sources (segment numbers).
- View: reporting.vScribeSession. Audio files: <data>\scribe\<session>\nnnnn.part (encrypted).

## 0.17.0 – Devices & SaferSleep (schema devices)
Cross-schema keys (90-reporting.sql): Monitors.EquipmentId → assets.Equipment; Associations.EpisodeId → emr.Episodes, PatientId → patients.Patients; Captures.EpisodeId → emr.Episodes, ObservationId → emr.Observations; AnaestheticRecords.EpisodeId → emr.Episodes, PatientId → patients.Patients, DocumentId → patients.Documents, NoteId → emr.Notes; AdtSends.EpisodeId → emr.Episodes. All tables R unless marked.
- **devices.Settings** (single row): TheatreChartMinutes, RecoveryChartMinutes, WardChartMinutes, StoreEverySeconds, OfflineAfterSeconds, UnassociatedKeepHours, KeepMonitorMessagesHours, AutoConnectByNhi, SaferSleepEnabled, SaferSleepOutCode (messaging interface), SaferSleepApplication, SaferSleepFacility, SendOn (Admission, InTheatre), FileDrugsToEmr, UpdatedAt/By. P
- **devices.Monitors**: EquipmentId (unique), Code (unique – the gateway's bed label or equipment id, upper case, "/" separated), IsActive, LastSeenAt, LastNhi (the NHI the monitor last sent), CreatedAt/By.
- **devices.Associations** (a patient connected to a monitor): MonitorId, EpisodeId, PatientId, StartedAt/By, Method (Manual, Monitor NHI), EndedAt/By. Unique filtered indexes UX_Associations_OpenMonitor and UX_Associations_OpenEpisode (WHERE EndedAt IS NULL): one open connection per monitor and per episode.
- **devices.Readings** (bigint identity; downsampled monitor values): MonitorId, AssociationId (null = no patient connected; deleted after UnassociatedKeepHours), At, Code (HR, PR, SpO2, NIBP-S/D/M, ART-S/D/M, RR, Temp, EtCO2), Value, Unit.
- **devices.Captures** (a reading offered to the chart): AssociationId, EpisodeId, MonitorId, At, HeartRate, SpO2, SystolicBp, DiastolicBp, MeanBp, RespiratoryRate, TemperatureC, EtCo2 (mmHg), Status (Pending, Validated, Rejected), ObservationId (the eMR observation created on validation), DecidedBy/At, RejectReason.
- **devices.AnaestheticRecords**: EpisodeId and PatientId (null = unmatched), CaseNumber (SaferSleep's, same across corrections), ControlId (unique – duplicate detection), IsCorrection, SupersededById (self), Nhi (as sent), Anaesthetist, StartAt, EndAt, Asa, Technique, Airway, Comments, DocumentId, NoteId, MessageId (messaging store), ReceivedAt.
- **devices.AnaestheticDrugs** (RecordId): At, Name, Dose, Unit, Route, Rate (infusions), Code. **devices.AnaestheticEvents** (RecordId): At, Text. **devices.AnaestheticVitals** (RecordId): At, Code, Value, Unit.
- **devices.AdtSends**: EpisodeId, Event (A01, A08, A03), At, By, MessageId, Error.
- **emr.MedicationsGiven** (new columns): Source (Manual, SaferSleep), ExternalRef (the external record's case number – replaced as a set on correction).
- **msg.Interfaces.Handler**: new values DeviceObservations and AnaesthesiaRecord.
- Views: reporting.vDeviceCapture (what was offered and what nurses did with it), reporting.vAnaestheticDrug (drugs from current records).

## 0.19.0 – Quality (schema quality)
Cross-schema keys (90-reporting.sql): Incidents.PatientId, Complaints.PatientId → patients.Patients; Incidents.BookingId → scheduling.Bookings; Incidents.EquipmentId, Risks.EquipmentId → assets.Equipment.
- **quality.Incidents**: Number (unique, INC-yyyy-nnnn), ReportedAt/By, OccurredAt, Location, Category (Fall, Medication, ClinicalCare, SurgicalProcedural, Anaesthetic, Infection, EquipmentDevice, InformationPrivacy, Staff…), Description, ImmediateAction, PatientId, BookingId, EquipmentId, StaffInvolved, Harm (None–Death), Sac (1–4, null until triaged), AlwaysReport, Status (Reported, Triaged, Investigating, Actions, Closed), Investigator, TriagedBy/At, EscalatedAt, Findings, ContributingFactors, LearningSummary, ClosedBy/At.
- **quality.Disclosures** (IncidentId): At, By, PersonTold, Method, Notes. **quality.Notifications** (IncidentId, Agency – unique together): NotifiedAt/By, Reference. Agency: Hqsc, AccTreatmentInjury, Medsafe, Hdc, HealthNz, WorkSafe, PrivacyCommissioner, Coroner.
- **quality.Complaints**: Number (unique), ReceivedOn, ReceivedVia, Complainant, Contact, PatientId, Summary, Category, Status (Received, Acknowledged, Investigating, Responded, Closed), AcknowledgeBy, AcknowledgedOn, DecideBy, ExtensionNotifiedOn, ExtensionReason, RespondedOn, Outcome (Upheld, PartlyUpheld, NotUpheld, Withdrawn), Response, HdcReferral, AdvocacyInvolved, IncidentId, Owner, CreatedAt/By.
- **quality.Compliments**: ReceivedOn, From, StaffNamed, Text, SharedAt, CreatedBy.
- **quality.Risks**: Number (unique), IsHazard, Title, Category, Description, Owner, Likelihood, Consequence (1–5), Controls, ResidualLikelihood, ResidualConsequence, Location, EquipmentId, ReviewBy, Status (Open, Monitoring, Closed), UpdatedAt/By.
- **quality.CorrectiveActions**: Number (unique), Source (Incident, Complaint, InternalAudit, DaaAudit, Risk, Feedback, Other), SourceId, SourceRef, Title, Detail, Owner, DueOn, Status (Open, Done, Verified), Evidence, DoneAt/By, VerifiedAt/By (must differ from DoneBy), CreatedAt/By.
- **quality.Policies**: Code (unique), Title, Version, Owner, Link, ReviewBy, RequiresAcknowledgement, IsActive, UpdatedAt/By. **quality.PolicyAcknowledgements**: DocumentId, Version, UserName (unique together), At.
- **quality.Indicators**: Code (unique), Name, Definition, Numerator, Denominator, Calculator (built-in code or null = manual), Scale (100 = %, 1000 = per 1,000), Direction, Target, AlertAt, Owner, Source, IsActive, Sort. **quality.IndicatorValues**: IndicatorId, Month (unique together), NumeratorValue, DenominatorValue, EnteredBy/At.
- **quality.Settings** (single row): EscalateTo, ExtraNonWorkingDays, UpdatedAt/By. **quality.Sequences**: Key, Next.
- Views: reporting.vIncident, reporting.vComplaint.


## 0.20.0 – Coding & statutory reporting (schema coding)
Cross-schema references (not enforced as keys): Episodes.EpisodeId → emr.Episodes; Episodes.PatientId, NbrsEntries.PatientId → patients.Patients; Episodes.BookingId → scheduling.Bookings; NbrsEntries.WaitlistEntryId → referrals.Waitlist; Registrations.ClinicianId → core.Clinicians.
- **coding.Codes**: System (2, e.g. 16), Code (8, no punctuation), Type (A diagnosis, B injury, E external cause, M morphology, O procedure, V supplementary) – unique together; Description, Sex (M/F), AgeFromYears, AgeToYears, NeverAssign (ACS 0049), NotPrincipal, IsActive.
- **coding.ProcedureMaps**: Term (unique – theatre procedure code or name), Label, ProcedureCodes (formatted, space-separated), DiagnosisCode, UpdatedAt/By.
- **coding.Advice**: Reference, Title, Source (IhacpaRule, IhacpaFaq, NzConvention, NmdsRequirement, Local), Edition, AppliesToNz, ReviewedOn, CodePrefixes, Summary, Link, IsActive, UpdatedAt/By.
- **coding.Registrations** (ClinicianId key): ProfessionalGroup (MC…), RegistrationNumber.
- **coding.Episodes**: EpisodeId (unique), PatientId, BookingId, AdmissionDate, DischargeDate, PurchaserCode (2), AdmissionSource (R/T), EventEndType (2), Status (ToCode, InProgress, Coded, Submitted, Rejected, NotReportable), NotReportableReason, CodedBy/At, CoderNote, SubmissionId, HealthNzError, Version (concurrency), CreatedAt. **coding.Items**: CodedEpisodeId + Sequence (unique), DiagnosisType (A/B/O/E/M), Code, Description (≤ 50, free text), Date.
- **coding.Submissions** (NMDS files): FileName, PeriodTo, Records, Content (the CSV), Status (Created, Sent, Accepted, Errors), IsResubmission, CreatedBy/At, SentBy/At, Notes. **coding.SubmissionEvents**: SubmissionId + CodedEpisodeId, RecordId.
- **coding.NbrsEntries**: FacilityCode + LocalId (unique), PatientId, WaitlistEntryId, BookingSource, ReferralDate, FirstAssessmentDate, CpacDate, CpacScore, CpacSystem, PurchaserCode, HealthSpecialty, ClinicalCode, ClinicalCodeType, DomicileCode, ProfessionalGroup, ResponsibilityCode, AssessorCode, AssessorGroup, StagedFlag, CreatedAt/By. **coding.NbrsEvents**: EntryId, Action (A/C/D/E), StatusCode, StatusDate, CertaintyDate, BookingMadeDate, BookedForDate, DeferredBy, ExitDate, ExitCategory, ContractAgency, TreatmentFacility, ClientSystemId, EventLocalId, BatchId, LineNumber, Result (Pending, Sent, Accepted, Rejected), Messages, CreatedAt/By. **coding.NbrsBatches**: Environment + Number (unique), FileName, Records, Content, Status, Accepted, Rejected, Warnings, CreatedAt/By, AcknowledgedAt.
- **coding.Settings** (single row): CodingSystem, EditionName, HealthAgencyCode, FacilityCode, FileAcronym, purchaser code per payment type, TdsReportsContractedEvents, ReportWithinDays, NbrsAgencyCode, NbrsEnvironment, NextNbrsBatchCmpl/Prod, UpdatedAt/By.
- **endo.Media** additions: ArchivedAt, ArchivedTo (encrypted archive copy). Settings keys endo.videoRetentionDays (blank = 30, "never"), endo.videoArchiveFolder, endo.videoArchiveFirst, endo.videoArchiveSecret (Data Protection encrypted).

## 0.21.0 – Procedure estimates (schema est)
Cross-schema references (not enforced as keys): Estimates.PatientId → patients.Patients; Surgeons.ClinicianId → core.Clinicians. Money is decimal(12,2) GST inclusive unless stated.
- **est.Specialties**: Name (unique), DefaultNotes (printed on new estimates), IsActive.
- **est.Procedures**: Code (unique, permanent once used), SpecialtyId, Group, Name, PrintTitle, Minutes, DefaultAnaesthetic (General, SedationAnaesthetist, SedationConsultant, Local, None), Facility, FacilitySpecial, Anaesthetist, AnaesthetistSpecial, ConsumablesLine, IncludedAddOnCodes (comma-separated add-on codes already in the fees), EffectiveFrom, IsActive, Source.
- **est.Surgeons**: Name (unique), ClinicianId, IsActive. **est.SurgeonSpecialties**: SurgeonId + SpecialtyId.
- **est.SurgeonFees**: ProcedureId, SurgeonId (null = All surgeons; one row per procedure and surgeon, one "All surgeons" row), Fee, SpecialFee, Notes.
- **est.AddOns**: Code (unique), SpecialtyId, Group (blank = whole specialty), Name, Fee, Type (Surgeon – adds to the surgeon fee; AllIn – own line), SurgeonId (only for this surgeon), Source, IsActive.
- **est.StockItems** (supplier prices, ex GST, internal only): Supplier, Reference, Name, PackPrice, UnitsPerPack, UnitPrice (12,4), InStockQty, NeededInHouse, WhereUsed, Issues, UpdatedAt. **est.ProcedureConsumables**: ProcedureId, StockItemId (or UnitCost), Description, Quantity.
- **est.PendingPrices**: ProcedureId, Field (Facility, FacilitySpecial, Anaesthetist, AnaesthetistSpecial, ConsumablesLine), NewValue, EffectiveFrom, CreatedBy/At, AppliedAt.
- **est.Estimates**: Ref (unique, EST-yymmdd-NHI or LASTNAME, -2…), EstimateDate, Status (Draft, Issued, Cancelled), PatientId, patient snapshot (FirstName, LastName, DateOfBirth, Nhi, Phone, Email lower-case, Address, Town, Postcode), Funding (SelfFunding, Insurance, NotYetKnown), Insurer, PolicyNumber, PriorApproval, ProcedureId + ProcedureCode/SpecialtyName/ProcedureName/Title snapshot, SurgeonId/SurgeonName, ProcedureDate, Anaesthetic, PlannedMinutes, UseSpecial, Multiplier (1 or 2), Facility/Surgeon/AnaesthetistOverride, InputsJson (add-ons, other charges, adjustment, "if required" lines), Notes, PreparedBy, Subtotal, Deposit, Balance, ValidUntil, CreatedBy/At, UpdatedBy/At, IssuedAt, EmailedAt, SentToIncisiveAt, CancelReason.
- **est.EstimateLines** (printed snapshot): EstimateId + Sequence (unique), Kind (Fee, IfRequired), Label, Amount, DisplayText ("To be advised"), Note.
- **est.ArchivedPdfs**: EstimateId, CreatedAt/By, Reason (Issued, Changed after issue), FileName, PdfCipher (encrypted PDF).
- **est.Outbox**: EstimateId, EstimateRef, PatientName, Recipient (Patient, Insurer, Other), ToAddress, Subject, Body, AttachmentName, AttachmentCipher (encrypted; emptied when sent), PasswordProtected, Status (Held, Sent, Stopped, Failed), CreatedAt/By, SendAfter, SentAt, StoppedAt/By, Error, Attempts.
- **est.Templates**: Name, Description, ProcedureId, SurgeonId, Anaesthetic, PlannedMinutes, UseSpecial, overrides, InputsJson, Notes, Visible, Favourite, SortOrder, UsedCount, LastUsedAt, CreatedBy/At, UpdatedBy/At. **est.TemplateSurgeons**: TemplateId + SurgeonId (none = everyone).
- **est.Settings** (single row): GstRate, Loading, Markup, ExtendedTimePer15, ValidityMonths, DepositPercent, DepositDaysBefore, BalanceDaysAfter, SurgeonFeeLabel, BankAccount, Phone, Email, Address, Terms, SelfFundingText, InsuranceText, AcceptanceText, SendMode (OutlookDraft, SendFromApp), HoldMinutes, RequireIssued, PatientPassword (DobShort, DobLong, Nhi, Typed, None), InsurerSameAsPatient, email subjects/bodies and password hint lines, FromName, BccAddress, InsurerAddresses, IncisiveFolder, IncisiveFileName, ArchiveIssued, StaffCostPerMinute, OverheadPerCase, TemplatesSeeded.

## 0.22.0 – Triage & suitability (schema triage)
Cross-schema references (not enforced as keys): PatientId → patients.Patients; BookingId → theatre.Bookings; FormRequestId → forms.FormRequests.
- **triage.RuleSets**: Version (unique), Name, Status (Draft, Approved, Retired; one Approved – filtered unique index), IsExample, Notes, CreatedBy/At, ApprovedBy/Role/At, ApprovalNote, RetiredAt.
- **triage.SuitabilityRules**: RuleSetId, Code (unique in the set), Name, Fact, Operator (AtLeast, AtMost, Above, Below, IsYes, Equals, Missing), Threshold, TextValue, ProcedureContains, Anaesthetic, Outcome, Precaution, Explanation, SortOrder, IsActive.
- **triage.MedicineRules**: RuleSetId, Name, Keywords (comma separated), Instruction, Outcome, IsActive.
- **triage.Assessments**: PatientId, BookingId, FormRequestId, PreviousId, Trigger (Initial, NewAnswers, NewResult, BookingChanged, Stale, Manual), TriggerDetail, Status (Open, SignedOff, Superseded), Procedure, Anaesthetic, ProcedureDate, RuleSetId/Version, RulesApproved, Bmi, StopBang, StopBangKnown, Frailty, Vte, Rcri, SuggestedAsa, ConfirmedAsa, FactsJson, StaffInputsJson, SuggestedOutcome, FinalOutcome, Precautions, Missing, ChangeSummary, Worse, Notes, OverrideReason, SignedOffBy/Role/At, CreatedBy/At.
- **triage.Findings**: AssessmentId + Sequence (key), Source (rule code, MED:, SCORE:, INFO:), Outcome, Text, Precaution.
- **triage.Settings** (single row): HoldBookingUntilSignedOff, HoldProcedures, RetriageAfterDays, SignOffDaysBefore, LastScanAt.

## 0.24.0 – Post-op follow-up & SSI (schema followup)
Cross-schema references (not enforced as keys): PatientId → patients.Patients; BookingId → theatre.Bookings; IncidentId → quality.Incidents.
- **followup.Plans**: Name (unique), ProcedureContains (comma-separated words), IsDefault (one – filtered unique index), IsActive, MaxAttempts, SortOrder, Notes, UpdatedBy/At.
- **followup.PlanSteps**: PlanId + Sequence (unique), Kind (Call, WoundCheck, Review, SsiCheck), Method (Phone, Clinic, Surgeon, PatientForm), Days, DaysWithImplant, WorkingDays (NZ clinic days), OnlyIfHighRisk, AssignTo, Label.
- **followup.Contacts**: PatientId, BookingId, PlanId, Procedure, OperationDate, Kind, Method, Label, DueOn, Status (Due, Done, NoAnswer, Unreachable, Cancelled), Attempts, MaxAttempts, AssignedTo, Concern, ConcernText, SuspectedSsi, Escalation (None, Advice, GpReview, ReturnToTds, SurgeonContacted, EmergencyDepartment), EscalationNote, EscalationClosedAt/By, EscalationOutcome, IncidentId, DoneBy/At/How, Notes, TokenHash (SHA-256 of the check-in link; unique), LinkSentAt, LinkExpiresAt, FailedAttempts, LockedUntil, AnsweredAt, CreatedAt/By.
- **followup.ContactAnswers**: ContactId + QuestionKey (key), Value.
- **followup.SsiCases**: Number (unique, SSI-yyyy-nnn), PatientId, BookingId, ContactId, Procedure, Surgeon, OperationDate, OnsetOn, Signs, Source (Patient, Gp, Surgeon, Readmission, Microbiology, Staff), Antibiotics, ReturnToTheatre, Readmitted, Microbiology, Class (Suspected, Superficial, Deep, OrganSpace, NotSsi), ConfirmedBy/At, ConfirmNote, SurveillanceDays (30/90), Implant, WoundClass, Asa, IncidentId, CreatedAt/By.
- **followup.SsiRiskFactors**: SsiCaseId + Name (key), Value (operation facts at the time: skin_prep, duration_minutes, antibiotic_prophylaxis, antibiotic_minutes_before_incision, first_recovery_temp_c, bmi, diabetes, precautions, implant, planned_minutes).
- **followup.Settings** (single row): DutyMobile, PublicUrl (https only), SendCheckInLinks, LinkDays, GpLetter, StartFrom, LastScanAt, NextSsiNumber.

## 0.25.0 – Send safety, de-identified reporting, form designer
- **reminders.Messages** (new columns): RequiresRelease, ReleasedBy/At, RecalledBy/At, RecallReason, WrongRecipientAt, CorrectionForId (→ Messages). **reminders.Settings**: HoldSeconds (120), BulkHoldSeconds (600), ReviewKinds.
- **letters.Letters** (new columns): WrongRecipientAt, WrongRecipientReason, CorrectionForId (→ Letters). New default template RETRACT.
- **research.Extracts**: Number (unique DX-yyyy-nnn), Title, Dataset, Level (Pseudonymised, DeIdentified, Aggregate), FromDate, ToDate, Purpose, Recipient, EthicsReference, MaoriDataGovernance, SmallCell, RemoveSmallGroups, Status (Draft, Submitted, Approved, Declined, Released), Rows, MinGroupSize, AtRiskRows, SuppressedCells, RequestedBy/At, DecidedBy/At, DecisionNote, ReleasedBy/At, Downloads. **research.ExtractColumns** (ExtractId + Column, Position). **research.StudyCodes** (ExtractId + PatientId, Code unique per extract) – the pseudonym linking key. **research.Settings** (NextNumber).
- **forms.Designs**: Code (unique), Name, Category (Clinical, Consent, Audit, Cssd, Admin, PatientQuestionnaire). **forms.DesignVersions**: DesignId + Version (unique), Status (Draft, Testing, Published – one per design, Retired), Source (Designer, Csv, Fhir, Pdf, Copy), Header, Footer, ThirdParty, LicenceNote, ApprovedBy/At, ApprovalNote, PublishedAt, RetiredAt. **forms.Fields**: VersionId + Key, Sequence, Section, Label, Type, Required, Min, Max, ShowIfKey, ShowIfValue, Calculation, MapsTo, Help. **forms.FieldOptions**: VersionId + FieldKey + Sequence, Value, Score. **forms.Entries**: VersionId, PatientId, BookingId, Status (InProgress, Completed, Voided), StartedBy/At, CompletedBy/At, VoidReason. **forms.EntryValues**: EntryId + FieldKey, Value.
