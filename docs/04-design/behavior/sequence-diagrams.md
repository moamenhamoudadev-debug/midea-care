# UML Sequence Diagrams: MediCare

This document details the dynamic component interactions across key system workflows using UML Sequence Diagrams.

---

## 1. User Registration & Authentication Flow

```mermaid
sequenceDiagram
    autonumber
    actor User as Patient / Doctor
    participant Web as AccountController (Web)
    participant BLL as IdentityService (Services)
    participant ID as ASP.NET Core Identity (UserManager / SignInManager)
    participant UoW as UnitOfWork & Repositories (Data)
    participant DB as SQL Server Database

    User->>Web: POST /Account/Register (Credentials, Role, Profile Fields)
    Web->>BLL: RegisterUserAsync(RegisterDto)
    BLL->>ID: CreateAsync(ApplicationUser, Password)
    alt Password or Identity Validation Fails
        ID-->>BLL: IdentityResult.Failed(errors)
        BLL-->>Web: Result.Failure(errors)
        Web-->>User: Display Registration Form with Errors
    else Identity User Created
        ID-->>BLL: IdentityResult.Success
        BLL->>ID: AddToRoleAsync(user, Role)
        alt Role is Doctor
            BLL->>UoW: Doctors.AddAsync(new Doctor { UserId = user.Id, IsApproved = false, ... })
        else Role is Patient
            BLL->>UoW: Patients.AddAsync(new Patient { UserId = user.Id, ... })
        end
        BLL->>UoW: CommitAsync()
        UoW->>DB: INSERT INTO Doctors/Patients ...
        DB-->>UoW: Commit OK
        BLL-->>Web: Result.Success(user.Id)
        Web->>ID: PasswordSignInAsync(user, isPersistent: false)
        Web-->>User: Redirect to Role Dashboard
    end
```

---

## 2. Search Doctors & Book Appointment (with Conflict Handling)

```mermaid
sequenceDiagram
    autonumber
    actor Patient as Patient Browser
    participant Web as AppointmentsController (Web)
    participant Service as AppointmentService (Services)
    participant SlotEng as SlotEngineService (Services)
    participant Factory as AppointmentFactory (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    participant Hub as AppointmentHub (SignalR)
    actor Doctor as Doctor Browser

    Patient->>Web: GET /Doctors/Book/5?date=2026-11-15
    Web->>SlotEng: GetAvailableSlotsAsync(doctorId: 5, date: 2026-11-15)
    SlotEng->>UoW: WorkingHours.GetByDoctorAsync(5)
    SlotEng->>UoW: DoctorLeaves.GetActiveLeavesAsync(5, date)
    SlotEng->>UoW: Appointments.GetDoctorBookingsAsync(5, date)
    SlotEng-->>Web: List<SlotDto> (Unreserved 30-min slots)
    Web-->>Patient: Render FullCalendar with Available Slots

    Patient->>Web: POST /Appointments/Book (DoctorId: 5, Date, StartTime: "10:00")
    Web->>Service: BookAppointmentAsync(BookingDto)
    Service->>UoW: Appointments.HasConflictAsync(5, date, "10:00")
    alt Pre-Check Detects Slot Already Taken
        UoW-->>Service: true (Slot Taken)
        Service-->>Web: Result.Failure("Selected slot is no longer available.")
        Web-->>Patient: Display Error Toast & Refresh Slots
    else Pre-Check Passes
        UoW-->>Service: false (Slot Free)
        Service->>Factory: CreateAppointment(BookingDto, fee, type)
        Factory-->>Service: Appointment Entity (Status = Pending)
        Service->>UoW: Appointments.Add(appointment)
        
        alt Concurrent Race Condition at DB Level
            Service->>UoW: CommitAsync()
            UoW->>DB: INSERT INTO Appointments ...
            DB-->>UoW: Violation of Filtered Unique Index [IX_Appointments_Doctor_NoOverlap]
            UoW-->>Service: Throws DbUpdateException
            Service-->>Web: Result.Failure("This slot was just reserved by another patient.")
            Web-->>Patient: Friendly Conflict Error (HTTP 409)
        else Normal Success Execution
            UoW->>DB: INSERT Success (Commit)
            DB-->>UoW: 1 Row Affected
            Service->>UoW: Notifications.Add(new Notification { UserId = doctorUserId, ... })
            Service->>UoW: CommitAsync()
            Service->>Hub: PushToUser(doctorUserId, "New Pending Appointment #101")
            Hub-->>Doctor: Instant Real-Time Alert Received
            Service-->>Web: Result.Success(appointmentId)
            Web-->>Patient: RedirectToAction("Confirmation", id)
        end
    end
```

---

## 3. Doctor Confirms or Rejects Appointment

```mermaid
sequenceDiagram
    autonumber
    actor Doctor as Doctor Browser
    participant Web as AppointmentsController (Web)
    participant Service as AppointmentService (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    participant Hub as AppointmentHub (SignalR)
    participant Mail as EmailService (Services)
    actor Patient as Patient Browser

    Doctor->>Web: POST /Appointments/UpdateStatus (Id: 101, NewStatus: Confirmed)
    Web->>Service: ChangeStatusAsync(id: 101, doctorId, Confirmed)
    Service->>UoW: Appointments.GetByIdAsync(101)
    Service->>Service: Verify Transition: Pending -> Confirmed (Allowed)
    Service->>UoW: Appointments.UpdateStatus(101, Confirmed)
    Service->>UoW: Notifications.Add(new Notification { UserId = patientUserId, Message = "Appointment Confirmed" })
    Service->>UoW: CommitAsync()
    UoW->>DB: UPDATE Appointments SET Status = 1, UpdatedAt = GETUTCDATE()
    DB-->>UoW: Success

    Service->>Hub: PushToUser(patientUserId, "Appointment Confirmed")
    Hub-->>Patient: Real-Time Toast Displayed
    Service->>Mail: SendEmailAsync(patientEmail, "Appointment Confirmed", details)
    Service-->>Web: Result.Success()
    Web-->>Doctor: Redirect to Dashboard with Updated Status
```

---

## 4. Patient Advance Cancellation Workflow

```mermaid
sequenceDiagram
    autonumber
    actor Patient as Patient Browser
    participant Web as AppointmentsController (Web)
    participant Service as AppointmentService (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    participant Hub as AppointmentHub (SignalR)
    actor Doctor as Doctor Browser

    Patient->>Web: POST /Appointments/Cancel (Id: 101)
    Web->>Service: CancelAppointmentAsync(id: 101, patientId)
    Service->>UoW: Appointments.GetByIdAsync(101)
    Service->>Service: Verify Ownership (appointment.Patient.UserId == currentUserId)
    
    alt Time to Appointment <= 2 Hours
        Service->>Service: Calculate (AppointmentTime - UtcNow) <= 2 Hours
        Service-->>Web: Result.Failure("Cannot cancel less than 2 hours before the start time.")
        Web-->>Patient: 400 Bad Request / Display Constraint Alert
    else Time to Appointment > 2 Hours (Valid)
        Service->>UoW: Update Status to Cancelled (3)
        Service->>UoW: Notifications.Add(new Notification { UserId = doctorUserId, Message = "Appointment #101 Cancelled" })
        Service->>UoW: CommitAsync()
        UoW->>DB: UPDATE Appointments SET Status = 3 (Filtered Index frees slot)
        DB-->>UoW: Success
        Service->>Hub: PushToUser(doctorUserId, "Appointment Cancelled")
        Hub-->>Doctor: Calendar Slot Re-Opened Alert
        Service-->>Web: Result.Success()
        Web-->>Patient: Display Cancellation Confirmation
    end
```

---

## 5. Clinical Encounter, Diagnostic Upload & Prescription Issuance

```mermaid
sequenceDiagram
    autonumber
    actor Doctor as Doctor Browser
    participant Web as MedicalRecordsController (Web)
    participant RecService as MedicalRecordService (Services)
    participant FileStorage as FileStorageService (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    actor Patient as Patient Browser

    Doctor->>Web: POST /MedicalRecords/Create (AppointmentId: 101, Symptoms, Diagnosis, PrescriptionItems, IFormFile)
    Web->>RecService: SaveEncounterAsync(dto, file)
    RecService->>UoW: Appointments.GetByIdAsync(101)
    RecService->>RecService: Verify Appointment Time Has Elapsed (StartTime <= UtcNow)
    
    opt Diagnostic File Attached
        RecService->>FileStorage: SaveFileAsync(file, "uploads/records")
        FileStorage-->>RecService: "uploads/records/guid_xray.pdf"
    end
    
    RecService->>UoW: Appointments.UpdateStatus(101, Completed)
    RecService->>UoW: MedicalRecords.Add(new MedicalRecord { Diagnosis, Symptoms, AttachmentPath })
    RecService->>UoW: Prescriptions.Add(new Prescription { PrescriptionItems })
    RecService->>UoW: CommitAsync()
    UoW->>DB: Multi-Table Transaction (Appointment, Record, Prescription, Items)
    DB-->>UoW: Commit Success (Atomic)
    
    RecService-->>Web: Result.Success(recordId)
    Web-->>Doctor: Render Formatted Print Prescription View (@media print)
    Note over Patient: Patient now sees Record in "My History"<br/>Protected by IDOR Guard
```

---

## 6. Notification Save-Then-Push Pattern

```mermaid
sequenceDiagram
    autonumber
    participant EventSource as Service Method (e.g. AppointmentService)
    participant NotifFactory as NotificationFactory (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    participant Hub as AppointmentHub (SignalR)
    actor Client as Target User Browser

    EventSource->>NotifFactory: Create(userId, title, message)
    NotifFactory-->>EventSource: Notification Entity & NotificationDto
    EventSource->>UoW: Notifications.Add(notification)
    EventSource->>UoW: CommitAsync()
    UoW->>DB: INSERT INTO Notifications (UserId, Title, Message, IsRead=0)
    DB-->>UoW: Committed (Guaranteed Persistence)
    
    EventSource->>Hub: Clients.User(userId).ReceiveNotification(notificationDto)
    alt Client Browser Tab Connected
        Hub-->>Client: WebSocket Push Received -> Toast Pop-Up Triggered
    else Client Disconnected / Network Offline
        Note over Hub,Client: Push fails silently without throwing error.<br/>Record remains saved in DB.
    end
    Note over Client: Upon next login/reload,<br/>Client calls GET /api/notifications/unread<br/>to retrieve unread alerts.
```

---

## 7. Administrator Approves Doctor Account

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Admin Browser
    participant Web as AdminController (Web)
    participant DocService as DoctorService (Services)
    participant UoW as UnitOfWork (Data)
    participant DB as SQL Server Database
    participant Mail as EmailService (Services)
    actor Doctor as Doctor Browser

    Admin->>Web: POST /Admin/ApproveDoctor/5
    Web->>DocService: ApproveDoctorAsync(doctorId: 5)
    DocService->>UoW: Doctors.GetByIdAsync(5)
    DocService->>UoW: Doctors.SetApproval(5, isApproved: true)
    DocService->>UoW: CommitAsync()
    UoW->>DB: UPDATE Doctors SET IsApproved = 1, UpdatedAt = GETUTCDATE()
    DB-->>UoW: Success
    DocService->>Mail: SendEmailAsync(doctorEmail, "Account Approved", "You can now publish your schedule.")
    DocService-->>Web: Result.Success()
    Web-->>Admin: Redirect to Approval Queue (Doctor Removed from Queue)
    Note over Doctor: Doctor profile and slots now appear in Public Directory
```
