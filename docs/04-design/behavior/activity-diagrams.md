# UML Activity Diagrams: MediCare

This document models the procedural control flows of MediCare's primary business operations using UML Activity Diagrams.

---

## 1. Appointment Booking Workflow (Patient & Engine)

```mermaid
flowchart TD
    Start([Start: Patient searches for doctor]) --> Search[Search directory by specialty, fee, or day]
    Search --> SelectDoc[Select approved doctor profile]
    SelectDoc --> LoadCal[Open FullCalendar view]
    
    LoadCal --> CallEngine[SlotEngine computes unbooked 30-min slots]
    CallEngine --> AnySlots{"Are slots available?"}
    
    AnySlots -- No --> NoSlotsMsg[Display: No available slots for this date]
    NoSlotsMsg --> SelectDate[Select alternative date]
    SelectDate --> CallEngine
    
    AnySlots -- Yes --> PickSlot[Patient clicks desired 30-min time slot]
    PickSlot --> SubmitBook[Click Confirm Booking]
    
    SubmitBook --> ValidateReq{"Slot valid & >= 30m in future?"}
    ValidateReq -- Invalid --> ShowValErr[Display validation error]
    ShowValErr --> PickSlot
    
    ValidateReq -- Valid --> PreCheck{"Service Pre-Check: HasConflictAsync?"}
    PreCheck -- Conflict Found --> SlotTaken[Display: Slot is no longer available]
    SlotTaken --> RefreshSlots[Reload updated calendar slots]
    RefreshSlots --> PickSlot
    
    PreCheck -- Clear --> DBInsert[Unit of Work: INSERT into Appointments]
    DBInsert --> CatchDbEx{"Database Filtered Unique Index check"}
    
    CatchDbEx -- Violation (DbUpdateException) --> HandleEx[Catch exception & Rollback]
    HandleEx --> ConflictMsg[Display: Slot just taken by another patient]
    ConflictMsg --> RefreshSlots
    
    CatchDbEx -- Success --> SaveNotif[Save Notification to Database]
    SaveNotif --> SignalRPush[Push alert to Doctor via SignalR]
    SignalRPush --> SendEmail[Queue confirmation email via MailKit]
    SendEmail --> ConfirmView[Display booking confirmation with Pending status]
    ConfirmView --> End([End: Booking completed])
```

---

## 2. Clinical Encounter & Prescription Workflow (Doctor)

```mermaid
flowchart TD
    StartEncounter([Start: Doctor opens scheduled appointment]) --> CheckStatus{"Status == Confirmed?"}
    
    CheckStatus -- No --> Abort[Cannot conduct consultation on unconfirmed visit]
    Abort --> EndEncounter([End])
    
    CheckStatus -- Yes --> CheckTime{"Current Time >= Start Time?"}
    CheckTime -- No --> TooEarly[Display: Consultation can only occur at scheduled time]
    TooEarly --> EndEncounter
    
    CheckTime -- Yes --> EnterNotes[Doctor inputs Symptoms, Diagnosis & Visit Notes]
    EnterNotes --> AttachReport{"Attach diagnostic file?"}
    
    AttachReport -- Yes --> SelectFile[Select file: JPG, PNG, or PDF]
    SelectFile --> ValFile{"File <= 5 MB and valid MIME?"}
    ValFile -- Invalid --> FileErr[Display: Invalid file format or size exceeds 5 MB]
    FileErr --> SelectFile
    ValFile -- Valid --> UploadDisk[Store file on disk under App_Data/uploads/records]
    UploadDisk --> PrescribeMed
    
    AttachReport -- No --> PrescribeMed[Add Medication items: Name, Dosage, Frequency, Duration]
    
    PrescribeMed --> SaveEncounter[Click Save Consultation & Prescription]
    SaveEncounter --> AtomicTx[Open Database Transaction via Unit of Work]
    
    AtomicTx --> Step1[Update Appointment Status -> Completed]
    Step1 --> Step2[INSERT MedicalRecord with Attachment Path]
    Step2 --> Step3[INSERT Prescription & PrescriptionItems]
    Step3 --> CommitTx{"Commit Transaction"}
    
    CommitTx -- Failure --> Rollback[Rollback all updates & show error]
    Rollback --> EndEncounter
    
    CommitTx -- Success --> PrintPreview[Render formatted print view @media print]
    PrintPreview --> PrintDoc[Doctor prints physical prescription for patient]
    PrintDoc --> EndEncounter
```

---

## 3. Doctor Vacation / Leave Handling Workflow

```mermaid
flowchart TD
    StartLeave([Start: Doctor accesses schedule settings]) --> Form[Open Add Absence / Vacation Leave form]
    Form --> InputDates[Input StartDate, EndDate and Reason]
    InputDates --> SubmitLeave[Click Submit Leave]
    
    SubmitLeave --> ValDates{"EndDate >= StartDate and StartDate >= Today?"}
    ValDates -- Invalid --> DateErr[Display: End date cannot precede start date]
    DateErr --> InputDates
    
    ValDates -- Valid --> SaveLeave[Save record in DoctorLeaves table]
    SaveLeave --> QueryAffected[Query Appointments overlapping leave dates]
    
    QueryAffected --> HasOverlaps{"Any Pending or Confirmed bookings exist?"}
    HasOverlaps -- Yes --> WarnDoc[Display Warning: Doctor must manually reschedule affected appointments]
    WarnDoc --> SlotSuppression
    
    HasOverlaps -- No --> SlotSuppression[SlotEngine updates: 0 slots generated for leave duration]
    SlotSuppression --> CalBlocked[FullCalendar displays leave period as blocked]
    CalBlocked --> EndLeave([End: Leave active])
```
