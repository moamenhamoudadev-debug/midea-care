# Entity-Relationship Diagram (ERD): MediCare

## 1. Relational Data Architecture

The MediCare database is modeled as a normalized relational schema hosted on Microsoft SQL Server. It decouples identity concerns (`AspNetUsers`) from domain profile entities (`Doctors`, `Patients`), ensures referential integrity across the clinical lifecycle, and provides explicit tracking of physician working hours and leaves.

---

## 2. Mermaid Entity-Relationship Diagram

```mermaid
erDiagram
    APPLICATION_USER ||--o| DOCTOR : "extends as doctor (1:0..1)"
    APPLICATION_USER ||--o| PATIENT : "extends as patient (1:0..1)"
    APPLICATION_USER ||--o{ NOTIFICATION : "receives (1:N)"

    SPECIALIZATION ||--o{ DOCTOR : "categorizes (1:N)"
    DOCTOR ||--o{ WORKING_HOURS : "configures (1:N)"
    DOCTOR ||--o{ DOCTOR_LEAVE : "registers (1:N)"
    DOCTOR ||--o{ APPOINTMENT : "consults for (1:N)"
    PATIENT ||--o{ APPOINTMENT : "books (1:N)"

    APPOINTMENT ||--o| MEDICAL_RECORD : "generates (1:0..1)"
    MEDICAL_RECORD ||--o| PRESCRIPTION : "includes (1:0..1)"
    PRESCRIPTION ||--|{ PRESCRIPTION_ITEM : "contains items (1:N)"

    DOCTOR ||--o{ MEDICAL_RECORD : "authors (1:N)"
    PATIENT ||--o{ MEDICAL_RECORD : "pertains to (1:N)"

    APPLICATION_USER {
        string Id PK "Identity GUID"
        string UserName "Unique username"
        string Email "User email"
        string PhoneNumber "Contact number"
        string FullName "Display name"
        datetime CreatedAt "Audit creation"
    }

    SPECIALIZATION {
        int Id PK "Auto-increment ID"
        string Name "Unique specialty name"
        string Description "Specialty details"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    DOCTOR {
        int Id PK "Auto-increment ID"
        string UserId FK "AspNetUsers ID (1:1)"
        int SpecializationId FK "Specialization ID"
        string LicenseNumber "Medical license"
        decimal ConsultationFee "Fee per 30-min visit"
        int SlotDurationMinutes "Default slot length (30 min)"
        boolean IsApproved "Admin verification status"
        string ProfileImageUrl "Relative photo path"
        string Bio "Doctor summary"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    PATIENT {
        int Id PK "Auto-increment ID"
        string UserId FK "AspNetUsers ID (1:1)"
        date DateOfBirth "Birth date"
        string Gender "Gender (Male/Female)"
        string BloodGroup "e.g. A+, O-, B+"
        string EmergencyContact "Phone number"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    WORKING_HOURS {
        int Id PK "Auto-increment ID"
        int DoctorId FK "Doctor ID"
        int DayOfWeek "0=Sun, 1=Mon ... 6=Sat"
        time StartTime "Shift start"
        time EndTime "Shift end"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    DOCTOR_LEAVE {
        int Id PK "Auto-increment ID"
        int DoctorId FK "Doctor ID"
        date StartDate "Leave start"
        date EndDate "Leave end"
        string Reason "Absence explanation"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    APPOINTMENT {
        int Id PK "Auto-increment ID"
        int DoctorId FK "Doctor ID"
        int PatientId FK "Patient ID"
        date AppointmentDate "Scheduled date"
        time StartTime "30-min interval start"
        time EndTime "30-min interval end"
        int Status "0=Pending, 1=Confirmed, 2=Completed, 3=Cancelled, 4=Rejected, 5=NoShow"
        decimal ConsultationFee "Fee snapshot"
        int PaymentStatus "0=Unpaid, 1=Paid"
        int Type "0=Consultation, 1=FollowUp"
        string Notes "Patient complaint"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    MEDICAL_RECORD {
        int Id PK "Auto-increment ID"
        int AppointmentId FK "Unique Appointment ID"
        int DoctorId FK "Doctor ID"
        int PatientId FK "Patient ID"
        string Diagnosis "Clinical diagnosis"
        string Symptoms "Patient symptoms"
        string VisitNotes "Doctor clinical notes"
        string AttachmentPath "Optional PDF/JPG path under App_Data/uploads/records"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    PRESCRIPTION {
        int Id PK "Auto-increment ID"
        int MedicalRecordId FK "Unique MedicalRecord ID"
        int DoctorId FK "Doctor ID"
        int PatientId FK "Patient ID"
        datetime PrescriptionDate "Date issued"
        string Notes "General instructions"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    PRESCRIPTION_ITEM {
        int Id PK "Auto-increment ID"
        int PrescriptionId FK "Prescription ID"
        string MedicationName "Drug commercial name"
        string Dosage "e.g. 500mg"
        string Frequency "e.g. Twice daily"
        int DurationDays "Duration in days"
        string Instructions "e.g. After meals"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }

    NOTIFICATION {
        int Id PK "Auto-increment ID"
        string UserId FK "Recipient AspNetUsers ID"
        string Title "Alert headline"
        string Message "Notification body"
        boolean IsRead "Read receipt indicator"
        datetime CreatedAt "Audit timestamp"
        datetime UpdatedAt "Audit timestamp"
    }
```

---

## 3. Structural Entity Cardinalities

1. **Identity to Profiles (1:1 optional):**
   * An `ApplicationUser` may be linked to at most one `Doctor` profile or one `Patient` profile via a unique foreign key (`UserId`).
2. **Specialization to Doctors (1:N):**
   * A `Specialization` categorizes multiple `Doctor` entities. A `Doctor` references exactly one primary `Specialization`.
3. **Doctor Schedules & Leaves (1:N):**
   * A `Doctor` maintains multiple weekly recurring `WorkingHours` entries and multiple date-bound `DoctorLeaves`.
4. **Appointments (M:N via Appointment Entity):**
   * An `Appointment` establishes a junction between `Doctor` and `Patient`, bound to a specific date and time slot.
5. **Clinical Encounter Chain (1:1 cascade):**
   * An `Appointment` can produce at most one `MedicalRecord` (created when moving to `Completed`).
   * A `MedicalRecord` can be linked to at most one `Prescription`.
   * A `Prescription` contains one or more `PrescriptionItems` (1:N mandatory).
6. **User Notifications (1:N):**
   * An `ApplicationUser` has zero or more persisted `Notification` records.
