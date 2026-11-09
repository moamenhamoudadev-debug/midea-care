# MediCare — Comprehensive Technical Architecture & Engineering Documentation

## 1. Architectural Architecture & Design Principles

MediCare is engineered following a clean **Three-Tier Layered Architecture** with strict directional dependency enforcement and separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                      MediCare.Web                           │
│   (ASP.NET Core 8 MVC, Razor Views, REST APIs, SignalR Hub) │
└──────────────────────────────┬──────────────────────────────┘
                               │ references
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                    MediCare.Services                        │
│   (Business Logic, Domain Services, DTOs, FluentValidation, │
│    Background Workers, IClinicClock, Notification Adapters) │
└──────────────────────────────┬──────────────────────────────┘
                               │ references
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                      MediCare.Data                          │
│   (EF Core 8, DbContext, Relational Entities, Migrations,   │
│    Generic & Specialized Repositories, Unit of Work, Seed)  │
└─────────────────────────────────────────────────────────────┘
```

### Architectural Principles:
1. **Separation of Concerns**: Controllers handle HTTP requests and return ViewModels or DTOs. Business logic resides strictly within domain services. Database operations execute via Repository and Unit of Work abstractions.
2. **Result Pattern**: Services return strongly typed `Result` and `Result<T>` envelopes containing success state, error messages, and payload, avoiding uncontrolled exception throwing for domain logic.
3. **Wall-Clock Local Time Anchoring**: MediCare implements `IClinicClock`, resolving `Africa/Cairo` (Egypt Standard Time) with automatic Daylight Saving Time handling to guarantee consistent appointment slot calculations.
4. **Defense in Depth**: Security controls operate across presentation (anti-forgery tokens, role attributes), service (ownership verification, DTO validation), and data layers (filtered unique constraints, foreign keys).

---

## 2. Relational Database Schema & Entity Relationships

The data model is managed via EF Core Code-First migrations with SQL Server.

```mermaid
erDiagram
    ApplicationUser ||--o| Doctor : "has profile"
    ApplicationUser ||--o| Patient : "has profile"
    ApplicationUser ||--o{ Notification : "receives"
    Specialization ||--o{ Doctor : "classifies"
    Doctor ||--o{ WorkingHours : "defines"
    Doctor ||--o{ DoctorLeave : "declares"
    Doctor ||--o{ Appointment : "conducts"
    Patient ||--o{ Appointment : "schedules"
    Appointment ||--o| MedicalRecord : "documents"
    Appointment ||--o| Prescription : "issues"
    Appointment ||--o{ PaymentAttempt : "has hosted checkout attempts"
    Prescription ||--o{ PrescriptionItem : "contains"

    ApplicationUser {
        string Id PK
        string FullName
        string Email
        string PhoneNumber
        DateTimeOffset LockoutEnd
    }

    Doctor {
        int Id PK
        string UserId FK
        int SpecializationId FK
        string LicenseNumber
        string ClinicAddress
        string Governorate
        decimal ConsultationFee
        bool IsApproved
    }

    Patient {
        int Id PK
        string UserId FK
        DateTime DateOfBirth
        string Gender
        string BloodGroup
        string EmergencyContact
        string Allergies
        string MedicalHistory
    }

    Appointment {
        int Id PK
        int DoctorId FK
        int PatientId FK
        DateTime AppointmentDate
        TimeSpan StartTime
        TimeSpan EndTime
        int Status
        int PaymentStatus
        decimal ConsultationFee
        bool ReminderSent
    }

    PaymentAttempt {
        int Id PK
        int AppointmentId FK
        int PatientId FK
        string MerchantReference UK
        string ProviderOrderId
        string ProviderTransactionId UK
        long AmountCents
        string Currency
        string Status
        DateTime ExpiresAt
    }

    MedicalRecord {
        int Id PK
        int AppointmentId FK
        int PatientId FK
        int DoctorId FK
        string Diagnosis
        string Symptoms
        string PhysicalExamination
        string TreatmentPlan
    }

    Prescription {
        int Id PK
        int AppointmentId FK
        int PatientId FK
        int DoctorId FK
        string Notes
    }
```

### 2.1 Filtered Unique Index for Active Slot Protection
To prevent double bookings at the database engine level, EF Core applies a filtered unique index in `ApplicationDbContext`:

```csharp
modelBuilder.Entity<Appointment>()
    .HasIndex(a => new { a.DoctorId, a.AppointmentDate, a.StartTime })
    .IsUnique()
    .HasFilter("[Status] IN (1, 2)"); // Status: 1 = Pending, 2 = Confirmed
```
- **Guaranteed Isolation**: Even if two concurrent booking requests pass memory validation simultaneously, SQL Server strictly rejects the second transaction with a unique constraint violation (`DbUpdateException`).
- **Reuse of Cancelled Slots**: Because the index only filters on active statuses (`Pending`, `Confirmed`), rejected (`3`), cancelled (`4`), or no-show (`5`) slots automatically free the time slot for subsequent bookings.

---

### Directory data and verification boundary

`Doctor` currently stores account/profile fields but no source URL, source-check date, photo rights, or directory-verification state. The Development seed cohort is synthetic; account approval alone does not verify identity, license, facility, price, schedule, or biography. The application currently imports no verified public-directory doctors/facilities and has no server-side review entity. Doctor-facing pages therefore show no fabricated ratings/reviews, use a neutral photo placeholder, and label profile details as unverified. See `docs/06-final/directory-data-audit.md` and `docs/06-final/medical-image-provenance.csv`.

## 3. Hosted Payment Flow (Paymob Sandbox)

The checkout page sends only the appointment ID and optional promo code to MediCare. The server checks ownership, calculates the amount in EGP, persists a pending `PaymentAttempt`, then creates a Paymob intention and redirects the patient to Paymob Unified Checkout. Card number and CVV are entered at the provider-hosted page.

The browser return URL is informational and cannot mark an appointment paid. The anonymous callback endpoint validates the Paymob HMAC, configured integration ID, EGP currency, amount in cents, the HMAC-signed Paymob order ID, and matching merchant reference before atomically updating the attempt and appointment in SQL Server. Unique filtered indexes limit each appointment to one pending attempt and reserve a promo for one patient while that payment is pending or succeeded; repeated checkout requests reuse the unexpired hosted URL.

This implementation is constrained to Paymob test key prefixes. Merchant credentials, public HTTPS callback delivery, and an end-to-end sandbox transaction remain unverified. Live payments and refunds are not implemented. An ambiguous gateway/network failure leaves an attempt pending for reconciliation; the system does not claim exactly-once payment processing.

## 4. Concurrency Control & Atomic Operations

### 3.1 Slot Booking Concurrency Flow
1. **Memory Pre-Flight**: Service checks `IAppointmentRepository` for existing active bookings.
2. **Transaction Insertion**: `_uow.Appointments.AddAsync(appointment)`.
3. **Database Commit**: `_uow.CommitAsync()`.
4. **Conflict Handling**: If two requests hit the same slot concurrently, the filtered index throws `DbUpdateException`. The service intercepts this, rolls back cleanly, and returns `Result.Failure("This appointment slot was just reserved by another patient.")`.

### 3.2 Atomic Appointment Rescheduling
Rescheduling is executed atomically in `AppointmentService.RescheduleAppointmentAsync`:
- Enforces a minimum **2-hour lead time** prior to the original appointment.
- Verifies target date is within a **30-day forward window**.
- Validates the doctor's weekly **Working Hours** and checks against declared **Doctor Leaves**.
- Validates that the patient has no overlapping appointments at that target time.
- Verifies doctor slot availability and updates `AppointmentDate`, `StartTime`, `EndTime`, resetting `ReminderSent = false`.
- Dispatches notifications to both patient and doctor.

---

## 5. Security Architecture & Threat Mitigations

| Security Domain | Mitigation Strategy | Technical Implementation |
|:---|:---|:---|
| **Authentication** | ASP.NET Core Identity with PBKDF2 hashing | Password complexity rules, lockout management via `UserManager`. |
| **Authorization & RBAC** | Declarative Role & Claim Attributes | `[Authorize(Roles = "Admin")]`, `[Authorize(Roles = "Doctor")]`, `[Authorize(Roles = "Patient")]`. |
| **CSRF / XSRF** | Anti-forgery tokens on MVC forms | State-changing MVC form actions use `[ValidateAntiForgeryToken]`; JSON API routes use their own authentication and authorization checks and should be reviewed individually. |
| **Broken Object Level Auth (IDOR)** | Ownership Verification | Services extract `currentUserId` from trusted ClaimsPrincipal and enforce `patient.UserId == currentUserId` before any data mutation or viewing. |
| **CSV Formula Injection (CWE-1236)** | Preamble Quoting | Sanitization function intercepts cells starting with `=`, `+`, `-`, `@`, `\t` and prepends a single quote (`'`). |
| **Input Validation** | Strict DTO Validation | FluentValidation rules applied before business processing. |

---

## 6. Background Services & Notification Infrastructure

### 5.1 Automated 24-Hour Reminder Background Worker
- **Component**: `AppointmentReminderBackgroundService : BackgroundService`.
- **Hosting**: Registered as a hosted background service via `services.AddHostedService<AppointmentReminderBackgroundService>()`.
- **Lifecycle**: Executes periodically every 15 minutes, creating a discrete `IServiceScope` to safely consume scoped dependencies (`IUnitOfWork`, `IClinicClock`, `IEmailService`, `ISmsService`, `INotificationService`).
- **Target Selection**: Evaluates appointments where `!ReminderSent` and `Status IN (Pending, Confirmed)` occurring between `[ClinicClock.Now + 23h, ClinicClock.Now + 25h]`.
- **Multi-Channel Dispatch**:
  1. **Email Notification** (HTML formatted appointment details, clinic location, doctor name).
  2. **SMS Dispatch** (Concise appointment reminder text).
  3. **In-App Notification** (SignalR push alert).
- **Reminder Completion Flag**: After the available channels return success, marks `appointment.ReminderSent = true` and commits to the database. On SQL Server, a transaction-owned `sp_getapplock` coordinates workers sharing the same database, followed by a pending-state recheck.
- **Delivery Limit**: This is not exactly-once delivery. Provider acceptance followed by database commit failure, or partial email/SMS success, can result in duplicate delivery on retry. There is no per-channel outbox or provider idempotency key.

### 5.2 External Provider Adapters
- **Twilio SMS**: `TwilioSmsService` dispatches SMS via Twilio REST API with Basic Auth credentials; includes automatic simulation mode when credentials are not configured.
- **SendGrid Email**: `SendGridEmailService` dispatches emails via SendGrid v3 JSON API with Bearer token authentication.
- **MailKit SMTP**: `MailKitEmailService` delivers transactional emails via TLS/STARTTLS SMTP servers.

These are implementation adapters, not evidence of live provider configuration or successful external delivery. The October 16, 2026 Release test report did not verify live SMTP, SendGrid, or Twilio delivery. SMS uses the mock adapter unless a live provider is configured.

---

## 7. Multi-Format Reporting Subsystem

MediCare generates reports in three formats without heavy external third-party dependencies:

1. **CSV Export (`/Admin/ExportAppointmentsCsv`)**: Generates UTF-8 encoded text with Byte Order Mark (BOM) for seamless opening in Microsoft Excel and LibreOffice, sanitized against CSV formula injection.
2. **Native Excel Workbook (`/Admin/ExportAppointmentsExcel`)**: Generates true OpenXML `.xlsx` zip packages containing styled headers, workbook relations, and typed numeric/date cell values using `System.IO.Compression.ZipArchive`.
3. **PDF Document (`/Admin/ExportAppointmentsPdf`)**: Generates standard Adobe PDF-1.4 documents containing clinic branding headers, executive summary metrics, and formatted appointment data tables with alternating row shading.

---

## 8. Testing Strategy & Verification Evidence

The repository contains unit, controller, security, and SQL Server integration tests. An earlier report states 131 passed on 7 October 2026, but its detailed suite counts do not reconcile with that total. The current code has also changed since that run. Rerun the suite and save its output before presenting a current result.

- **Unit Tests**: Moq-driven unit tests verifying business validation, slot generation, appointment rescheduling, patient profile management, CSV formula injection mitigation, and reminder processing.
- **Integration Tests**: Executed against Microsoft SQL Server LocalDB with real EF Core migrations, validating schema constraints, filtered unique indexes, and concurrent booking race conditions.

---

## 9. Deployment & Environment Configuration

### Key Settings (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "SmtpSettings": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "SenderEmail": "noreply@medicare.local",
    "SenderName": "MediCare Clinic",
    "EnableSsl": true
  },
  "TwilioSettings": {
    "Enabled": false,
    "AccountSid": "",
    "AuthToken": "",
    "FromPhoneNumber": "+10000000000"
  },
  "SendGridSettings": {
    "Enabled": false,
    "ApiKey": "",
    "SenderEmail": "noreply@medicare.local",
    "SenderName": "MediCare Clinic"
  }
}
```
For non-Development environments, configure `ConnectionStrings__DefaultConnection` before startup. SMTP and provider settings can also be supplied through environment variables. This repository includes a deployment workflow, but a live deployment must be confirmed separately.
