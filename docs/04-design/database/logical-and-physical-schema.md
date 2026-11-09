# Logical & Physical Database Schema Specification: MediCare

## 1. Physical Database Tables & Data Dictionary

The physical schema is implemented on Microsoft SQL Server 2022 via Entity Framework Core 8 migrations. All primary keys are integer identity columns (`int IDENTITY(1,1)`), except for ASP.NET Core Identity tables which use `nvarchar(450)` GUID strings. All entities inherit audit columns `CreatedAt` and `UpdatedAt`.

---

### 1.1 `AspNetUsers` (Identity Storage)
*Managed by ASP.NET Core Identity. Custom profile data is stored in specialized child tables.*
* **`Id`** (`nvarchar(450)`, PK, NOT NULL)
* **`UserName`** (`nvarchar(256)`, NOT NULL)
* **`NormalizedUserName`** (`nvarchar(256)`, INDEX)
* **`Email`** (`nvarchar(256)`, NOT NULL)
* **`NormalizedEmail`** (`nvarchar(256)`, INDEX)
* **`PasswordHash`** (`nvarchar(max)`, NOT NULL)
* **`SecurityStamp`** (`nvarchar(max)`)
* **`ConcurrencyStamp`** (`nvarchar(max)`)
* **`PhoneNumber`** (`nvarchar(50)`)
* **`FullName`** (`nvarchar(150)`, NOT NULL)
* **`CreatedAt`** (`datetime2`, NOT NULL, DEFAULT `GETUTCDATE()`)

---

### 1.2 `Specializations`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Specialization unique ID |
| `Name` | `nvarchar(100)` | NO | UNIQUE INDEX | e.g., Cardiology, Pediatrics |
| `Description` | `nvarchar(500)` | YES | NULL | Overview of the specialty |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.3 `Doctors`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Doctor unique ID |
| `UserId` | `nvarchar(450)` | NO | FK -> `AspNetUsers(Id)`, UNIQUE | One-to-one link to Identity user |
| `SpecializationId` | `int` | NO | FK -> `Specializations(Id)` | Primary medical specialty |
| `LicenseNumber` | `nvarchar(50)` | NO | UNIQUE INDEX | Medical practice license |
| `ConsultationFee` | `decimal(18,2)` | NO | CHECK (`ConsultationFee >= 0`) | Fee charged per standard 30-min visit |
| `SlotDurationMinutes`| `int` | NO | DEFAULT 30, CHECK (`> 0`) | Slot length (reserved for future flexibility)|
| `IsApproved` | `bit` | NO | DEFAULT 0 | Admin account approval flag |
| `ProfileImageUrl` | `nvarchar(500)` | YES | NULL | Relative path to profile picture |
| `Bio` | `nvarchar(1000)` | YES | NULL | Professional biography and qualifications |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.4 `Patients`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Patient unique ID |
| `UserId` | `nvarchar(450)` | NO | FK -> `AspNetUsers(Id)`, UNIQUE | One-to-one link to Identity user |
| `DateOfBirth` | `date` | NO | CHECK (`DateOfBirth < GETDATE()`) | Date of birth |
| `Gender` | `nvarchar(10)` | NO | CHECK (`Gender IN ('Male','Female')`)| Biological gender |
| `BloodGroup` | `nvarchar(5)` | YES | CHECK (`BloodGroup IN ('A+','A-','B+','B-','AB+','AB-','O+','O-')`) | Blood group type |
| `EmergencyContact`| `nvarchar(50)` | YES | NULL | Contact phone for emergencies |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.5 `WorkingHours`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Working hours unique ID |
| `DoctorId` | `int` | NO | FK -> `Doctors(Id)` ON DELETE CASCADE | Doctor ID |
| `DayOfWeek` | `int` | NO | CHECK (`DayOfWeek BETWEEN 0 AND 6`) | 0 = Sunday, 1 = Monday ... 6 = Saturday |
| `StartTime` | `time(0)` | NO | - | Shift start time |
| `EndTime` | `time(0)` | NO | CHECK (`EndTime > StartTime`) | Shift end time |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

*Index:* `IX_WorkingHours_Doctor_Day` on `(DoctorId, DayOfWeek)`

---

### 1.6 `DoctorLeaves`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Leave unique ID |
| `DoctorId` | `int` | NO | FK -> `Doctors(Id)` ON DELETE CASCADE | Doctor ID |
| `StartDate` | `date` | NO | - | Absence start date |
| `EndDate` | `date` | NO | CHECK (`EndDate >= StartDate`) | Absence end date |
| `Reason` | `nvarchar(250)` | YES | NULL | Purpose of vacation or leave |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

*Index:* `IX_DoctorLeaves_Doctor_Dates` on `(DoctorId, StartDate, EndDate)`

---

### 1.7 `Appointments`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Appointment unique ID |
| `DoctorId` | `int` | NO | FK -> `Doctors(Id)` | Assigned physician |
| `PatientId` | `int` | NO | FK -> `Patients(Id)` | Booking patient |
| `AppointmentDate` | `date` | NO | - | Scheduled appointment calendar date |
| `StartTime` | `time(0)` | NO | - | Interval start (e.g. 09:00:00) |
| `EndTime` | `time(0)` | NO | CHECK (`EndTime > StartTime`) | Interval end (e.g. 09:30:00) |
| `Status` | `int` | NO | CHECK (`Status BETWEEN 0 AND 5`) | 0=Pending, 1=Confirmed, 2=Completed, 3=Cancelled, 4=Rejected, 5=NoShow |
| `ConsultationFee` | `decimal(18,2)` | NO | CHECK (`ConsultationFee >= 0`) | Historical snapshot of fee charged |
| `PaymentStatus` | `int` | NO | CHECK (`PaymentStatus IN (0, 1)`)| 0 = Unpaid, 1 = Paid |
| `Type` | `int` | NO | CHECK (`Type IN (0, 1)`) | 0 = Consultation, 1 = FollowUp |
| `Notes` | `nvarchar(500)` | YES | NULL | Patient symptoms / booking notes |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

#### C# Domain Enums for Appointments:
```csharp
public enum AppointmentStatus
{
    Pending = 0,    // Initial reservation; slot held
    Confirmed = 1,  // Approved by doctor/admin
    Completed = 2,  // Consultation finished; terminal state
    Cancelled = 3,  // Patient cancelled >2h before; slot freed; terminal state
    Rejected = 4,   // Doctor declined; slot freed; terminal state
    NoShow = 5      // Patient absent; terminal state
}

public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1
}

public enum AppointmentType
{
    Consultation = 0,
    FollowUp = 1
}
```

---

### 1.8 `MedicalRecords`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Medical record unique ID |
| `AppointmentId` | `int` | NO | FK -> `Appointments(Id)`, UNIQUE | 1:1 link to triggering appointment |
| `DoctorId` | `int` | NO | FK -> `Doctors(Id)` | Authoring physician |
| `PatientId` | `int` | NO | FK -> `Patients(Id)` | Patient subject |
| `Diagnosis` | `nvarchar(500)` | NO | - | Clinical diagnosis text |
| `Symptoms` | `nvarchar(1000)`| YES | NULL | Clinical symptoms observed |
| `VisitNotes` | `nvarchar(max)` | YES | NULL | Detailed doctor examination notes |
| `AttachmentPath` | `nvarchar(500)` | YES | NULL | Relative file path in `App_Data/uploads/records` |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.9 `Prescriptions`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Prescription unique ID |
| `MedicalRecordId`| `int` | NO | FK -> `MedicalRecords(Id)`, UNIQUE | 1:1 link to medical visit record |
| `DoctorId` | `int` | NO | FK -> `Doctors(Id)` | Issuing physician |
| `PatientId` | `int` | NO | FK -> `Patients(Id)` | Receiving patient |
| `PrescriptionDate`| `datetime2`| NO | DEFAULT `GETUTCDATE()` | Issue date and time |
| `Notes` | `nvarchar(500)` | YES | NULL | General pharmacy dispensing notes |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.10 `PrescriptionItems`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Line item unique ID |
| `PrescriptionId` | `int` | NO | FK -> `Prescriptions(Id)` ON DELETE CASCADE | Parent prescription |
| `MedicationName` | `nvarchar(150)` | NO | - | Name of drug / medication |
| `Dosage` | `nvarchar(100)` | NO | - | e.g. "500 mg", "10 ml" |
| `Frequency` | `nvarchar(100)` | NO | - | e.g. "Twice daily", "Every 8 hours" |
| `DurationDays` | `int` | NO | CHECK (`DurationDays > 0`) | Treatment course in days |
| `Instructions` | `nvarchar(250)` | YES | NULL | e.g. "Take after food with water" |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

---

### 1.11 `Notifications`
| Column Name | Data Type | Nullable | Constraints & Defaults | Description |
|---|---|:---:|---|---|
| `Id` | `int` | NO | PRIMARY KEY, IDENTITY(1,1) | Notification unique ID |
| `UserId` | `nvarchar(450)` | NO | FK -> `AspNetUsers(Id)` ON DELETE CASCADE | Alert recipient |
| `Title` | `nvarchar(150)` | NO | - | Notification title |
| `Message` | `nvarchar(500)` | NO | - | Body text |
| `IsRead` | `bit` | NO | DEFAULT 0 | Read flag |
| `CreatedAt` | `datetime2` | NO | DEFAULT `GETUTCDATE()` | Creation audit timestamp |
| `UpdatedAt` | `datetime2` | YES | NULL | Modification audit timestamp |

*Index:* `IX_Notifications_User_Unread` on `(UserId, IsRead)` INCLUDE `(CreatedAt)`

---

## 2. Concurrency-Safe Filtered Unique Index

To fulfill **FR-11** and **NFR-REL-01** (Zero Double-Booking), a SQL Server **Filtered Unique Index** is applied to the `Appointments` table. This allows multiple historical cancelled or rejected appointments for a given slot while strictly prohibiting more than one active appointment.

### SQL DDL Statement:
```sql
CREATE UNIQUE NONCLUSTERED INDEX [IX_Appointments_Doctor_NoOverlap]
ON [dbo].[Appointments] ([DoctorId], [AppointmentDate], [StartTime])
WHERE [Status] <> 3 AND [Status] <> 4; -- 3 = Cancelled, 4 = Rejected
```

### Entity Framework Core Fluent API Configuration:
```csharp
public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasIndex(a => new { a.DoctorId, a.AppointmentDate, a.StartTime })
               .IsUnique()
               .HasFilter("[Status] <> 3 AND [Status] <> 4")
               .HasDatabaseName("IX_Appointments_Doctor_NoOverlap");
    }
}
```

---

## 3. Database Normalization Analysis

The schema satisfies **Third Normal Form (3NF)** and **Boyce-Codd Normal Form (BCNF)**:

1. **First Normal Form (1NF):**
   * All attributes are atomic (no multi-valued columns or repeating groups).
   * Composite fields (e.g., prescriptions) are decoupled into distinct rows via `PrescriptionItems`.
2. **Second Normal Form (2NF):**
   * Every relation uses a single-column surrogate primary key (`Id`).
   * No non-key attributes exhibit partial functional dependency on a composite key.
3. **Third Normal Form (3NF):**
   * All non-key attributes depend directly and exclusively on the primary key (no transitive dependencies).
   * For example, doctor specialization details (`Name`, `Description`) reside in `Specializations`, with only `SpecializationId` referenced in `Doctors`. Patient Identity data (`Email`, `PhoneNumber`, `FullName`) is referenced via `UserId`, eliminating redundant storage.
4. **BCNF Compliance:**
   * Every determinant in every relation is a candidate key. The filtered index enforces determinant uniqueness on `(DoctorId, AppointmentDate, StartTime)` for active appointment tuples.

---

## 4. Database Seeding Specification (`DbInitializer`)

The automated seeder executes on application startup if the database is empty:

1. **Identity Roles & Admin Account:**
   * Roles: `Admin`, `Doctor`, `Patient`.
   * Administrator: `admin@medicare.com` (Development password configured via user-secrets/Seed:AdminPassword; Production strictly requires configured secret >= 16 characters).
2. **5 Medical Specializations:**
   * Cardiology, Dermatology, Pediatrics, Orthopedics, General Internal Medicine.
3. **5 Approved Doctors:**
   * 1 per specialization, complete with license numbers, bios, consultation fees (ranging from 150 to 350 EGP), and profile photos.
   * Recurring working hours configured across standard weekdays (Sunday – Thursday, 09:00 to 17:00).
   * Sample `DoctorLeaves` configured for 1 doctor to verify slot suppression.
4. **5 Patients:**
   * Configured with varied birth dates, blood types (A+, O+, B+), and emergency contact phone numbers.
5. **20+ Historical Appointments (Completed Visits):**
   * Pre-populated with historical dates across the previous 30 days.
   * Every completed appointment has an associated `MedicalRecord` (diagnosis, symptoms) and an itemized `Prescription` (with 2–3 `PrescriptionItems`).
   * `PaymentStatus = Paid` to verify Chart.js revenue reporting.
6. **5 Upcoming Appointments:**
   * Spread across tomorrow and next week with status `Confirmed` and `Pending` to allow immediate live demonstration of the calendar view, doctor confirmation, and SignalR alerts.
