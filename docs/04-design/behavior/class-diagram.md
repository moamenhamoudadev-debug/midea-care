# UML Class Diagram: MediCare

This document details the object-oriented structure of MediCare across its three architectural layers (`MediCare.Data`, `MediCare.Services`, and `MediCare.Web`), illustrating entity hierarchies, repository abstractions, service contracts, design pattern factories, and real-time hub interfaces.

---

## 1. Domain Entities & Data Layer Classes (`MediCare.Data`)

```mermaid
classDiagram
    class BaseAuditableEntity {
        <<abstract>>
        +int Id
        +DateTime CreatedAt
        +DateTime UpdatedAt
    }

    class ApplicationUser {
        +string Id
        +string UserName
        +string Email
        +string PhoneNumber
        +string FullName
        +DateTime CreatedAt
    }

    class Specialization {
        +string Name
        +string Description
    }

    class Doctor {
        +string UserId
        +int SpecializationId
        +string LicenseNumber
        +decimal ConsultationFee
        +int SlotDurationMinutes
        +bool IsApproved
        +string ProfileImageUrl
        +string Bio
    }

    class Patient {
        +string UserId
        +DateTime DateOfBirth
        +string Gender
        +string BloodGroup
        +string EmergencyContact
    }

    class WorkingHours {
        +int DoctorId
        +DayOfWeek DayOfWeek
        +TimeSpan StartTime
        +TimeSpan EndTime
    }

    class DoctorLeave {
        +int DoctorId
        +DateTime StartDate
        +DateTime EndDate
        +string Reason
    }

    class Appointment {
        +int DoctorId
        +int PatientId
        +DateTime AppointmentDate
        +TimeSpan StartTime
        +TimeSpan EndTime
        +AppointmentStatus Status
        +decimal ConsultationFee
        +PaymentStatus PaymentStatus
        +AppointmentType Type
        +string Notes
    }

    class MedicalRecord {
        +int AppointmentId
        +int DoctorId
        +int PatientId
        +string Diagnosis
        +string Symptoms
        +string VisitNotes
        +string AttachmentPath
    }

    class Prescription {
        +int MedicalRecordId
        +int DoctorId
        +int PatientId
        +DateTime PrescriptionDate
        +string Notes
    }

    class PrescriptionItem {
        +int PrescriptionId
        +string MedicationName
        +string Dosage
        +string Frequency
        +int DurationDays
        +string Instructions
    }

    class Notification {
        +string UserId
        +string Title
        +string Message
        +bool IsRead
    }

    BaseAuditableEntity <|-- Specialization
    BaseAuditableEntity <|-- Doctor
    BaseAuditableEntity <|-- Patient
    BaseAuditableEntity <|-- WorkingHours
    BaseAuditableEntity <|-- DoctorLeave
    BaseAuditableEntity <|-- Appointment
    BaseAuditableEntity <|-- MedicalRecord
    BaseAuditableEntity <|-- Prescription
    BaseAuditableEntity <|-- PrescriptionItem
    BaseAuditableEntity <|-- Notification

    ApplicationUser "1" -- "0..1" Doctor : links
    ApplicationUser "1" -- "0..1" Patient : links
    Specialization "1" -- "0..*" Doctor : categorizes
    Doctor "1" -- "0..*" WorkingHours : defines
    Doctor "1" -- "0..*" DoctorLeave : registers
    Doctor "1" -- "0..*" Appointment : consults
    Patient "1" -- "0..*" Appointment : reserves
    Appointment "1" -- "0..1" MedicalRecord : produces
    class AppointmentStatus {
        <<enumeration>>
        Pending = 0
        Confirmed = 1
        Completed = 2
        Cancelled = 3
        Rejected = 4
        NoShow = 5
    }

    class PaymentStatus {
        <<enumeration>>
        Unpaid = 0
        Paid = 1
    }

    class AppointmentType {
        <<enumeration>>
        Consultation = 0
        FollowUp = 1
    }

    MedicalRecord "1" -- "0..1" Prescription : contains
    Prescription "1" -- "1..*" PrescriptionItem : items
    Appointment ..> AppointmentStatus : uses
    Appointment ..> PaymentStatus : uses
    Appointment ..> AppointmentType : uses
```

---

## 2. Repositories & Unit of Work Abstractions (`MediCare.Data`)

```mermaid
classDiagram
    class IRepository~T~ {
        <<interface>>
        +GetByIdAsync(int id) Task~T~
        +GetAllAsync() Task~IEnumerable~T~~
        +AddAsync(T entity) Task
        +Update(T entity) void
        +Delete(T entity) void
    }

    class IAppointmentRepository {
        <<interface>>
        +GetDoctorAppointmentsAsync(int doctorId, DateTime date) Task~List~Appointment~~
        +HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime) Task~bool~
        +GetAppointmentsByMonthAsync(int month, int year) Task~List~Appointment~~
    }

    class IDoctorRepository {
        <<interface>>
        +GetApprovedDoctorsAsync(int? specializationId) Task~List~Doctor~~
        +GetDoctorWithScheduleAsync(int doctorId) Task~Doctor~
    }

    class IUnitOfWork {
        <<interface>>
        +IAppointmentRepository Appointments
        +IDoctorRepository Doctors
        +IRepository~Patient~ Patients
        +IRepository~MedicalRecord~ MedicalRecords
        +IRepository~Prescription~ Prescriptions
        +IRepository~Notification~ Notifications
        +CommitAsync() Task~int~
    }

    class Repository~T~ {
        #ApplicationDbContext _context
        #DbSet~T~ _dbSet
    }

    class AppointmentRepository
    class DoctorRepository
    class UnitOfWork

    IRepository~T~ <|-- Repository~T~
    IRepository~Appointment~ <|-- IAppointmentRepository
    IRepository~Doctor~ <|-- IDoctorRepository
    Repository~Appointment~ <|-- AppointmentRepository
    Repository~Doctor~ <|-- DoctorRepository
    IAppointmentRepository <|.. AppointmentRepository
    IDoctorRepository <|.. DoctorRepository
    IUnitOfWork <|.. UnitOfWork
```

---

## 3. Business Services, Pattern Factories & Results (`MediCare.Services`)

```mermaid
classDiagram
    class Result {
        +bool IsSuccess
        +string Error
        +Success() Result
        +Failure(string error) Result
    }

    class Result~T~ {
        +T Value
        +Success(T value) Result~T~
        +Failure(string error) Result~T~
    }

    Result <|-- Result~T~

    class ISlotEngineService {
        <<interface>>
        +GetAvailableSlotsAsync(int doctorId, DateTime date) Task~List~SlotDto~~
    }

    class IAppointmentService {
        <<interface>>
        +BookAppointmentAsync(BookingRequestDto dto) Task~Result~int~~
        +ConfirmAppointmentAsync(int appointmentId, int doctorId) Task~Result~
        +RejectAppointmentAsync(int appointmentId, int doctorId) Task~Result~
        +CancelAppointmentAsync(int appointmentId, string userId) Task~Result~
    }

    class IMedicalRecordService {
        <<interface>>
        +SaveEncounterAsync(MedicalRecordCreateDto dto, IFormFile file) Task~Result~int~~
        +GetRecordByIdAsync(int recordId, string currentUserId) Task~Result~MedicalRecordDto~~
    }

    class IEmailService {
        <<interface>>
        +SendEmailAsync(string toEmail, string subject, string bodyHtml) Task~bool~
    }

    class ISmsService {
        <<interface>>
        +SendSmsAsync(string phoneNumber, string message) Task~bool~
    }

    class AppointmentFactory {
        +Create(BookingRequestDto dto, decimal fee, AppointmentType type) Appointment
    }

    class NotificationFactory {
        +CreateNotification(string userId, string title, string message) Notification
        +CreatePayload(Notification entity) NotificationDto
    }

    class SlotEngineService {
        -IUnitOfWork _uow
    }

    class AppointmentService {
        -IUnitOfWork _uow
        -ISlotEngineService _slotEngine
        -IEmailService _emailService
        -IAppointmentNotificationClient _hubClient
    }

    ISlotEngineService <|.. SlotEngineService
    IAppointmentService <|.. AppointmentService
    AppointmentService --> AppointmentFactory : uses
    AppointmentService --> NotificationFactory : uses
    AppointmentService --> IEmailService : uses
```

---

## 4. SignalR Real-Time Hub Architecture (`MediCare.Web`)

```mermaid
classDiagram
    class Hub {
        <<ASP.NET Core>>
    }

    class IAppointmentNotificationClient {
        <<interface>>
        +ReceiveNotification(NotificationDto notification) Task
        +AppointmentStatusChanged(int appointmentId, string status) Task
        +SlotAvailabilityChanged(int doctorId, DateTime date) Task
    }

    class AppointmentHub {
        +JoinUserGroup(string userId) Task
        +LeaveUserGroup(string userId) Task
    }

    Hub <|-- AppointmentHub
    AppointmentHub ..> IAppointmentNotificationClient : typed contract
```
