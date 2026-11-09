# Project Proposal: MediCare — Clinic Management & Appointment System

## 1. Project Overview
**MediCare** is a clinic management and appointment system developed with **ASP.NET Core MVC** and **Entity Framework Core** as a graduation project for the **Digital Egypt Pioneers Initiative (DEPI)**. The project models the workflow of a single outpatient clinic.

The platform streamlines healthcare administrative workflows by automating patient booking, eliminating schedule conflicts through deterministic concurrency controls, facilitating digital medical consultations and prescription printing, and providing real-time visibility into clinic operations for administrators.

---

## 2. Problem Statement
Outpatient clinics and medium-sized healthcare centers frequently suffer from operational inefficiencies caused by manual or disconnected scheduling workflows:
1. **Double-Booking & Schedule Clashes:** Reliance on manual appointment logs or naive web forms frequently causes overlapping reservations when multiple patients or receptionists schedule appointments simultaneously.
2. **Lack of Real-Time Coordination:** Schedule modifications, doctor emergency leaves, and appointment confirmations are communicated asynchronously, causing long clinic waiting times or unattended appointments (No-Shows).
3. **Fragmented Clinical Records:** Consultations, written prescriptions, and laboratory/imaging attachments remain separated across paper files, preventing historical tracking during follow-up visits.
4. **Administrative Opacity:** Clinic management lacks centralized visibility into appointment volumes, physician schedules, cancellation ratios, and consultation fee metrics.

MediCare addresses these workflows through a layered application, database-enforced booking constraints, and real-time in-app notifications.

---

## 3. Project Objectives
The primary architectural and functional objectives of MediCare are:
* **Conflict-aware Scheduling:** Calculate bookable slots from clinic hours and existing appointments, then enforce slot uniqueness in SQL Server so simultaneous requests cannot create duplicate eligible bookings.
* **Role-Differentiated Access:** Provide dedicated operational portals for **Patients**, **Doctors**, and **Clinic Administrators** using secure ASP.NET Core Identity authentication.
* **Real-Time Notification Pipeline:** Integrate ASP.NET Core SignalR with database persistence to deliver instant appointment alerts to active browsers, backed by transactional email notifications via MailKit.
* **Comprehensive Clinical Records:** Enable physicians to record patient visit notes, upload diagnostic documents (images/PDFs), and generate standardized, print-friendly digital prescriptions.
* **Operational Reporting:** Deliver administrative visual analytics tracking monthly appointments, top specializations, and fee collection summaries using Chart.js.

---

## 4. System Scope

### 4.1 In-Scope Capabilities
* **Authentication & Identity:**
  * ASP.NET Core Identity with `Admin`, `Doctor`, `Patient`, and `Pharmacist` roles.
  * Specialized profile entities (`Doctor`, `Patient`) linked to `AspNetUsers` via `UserId` foreign key without column duplication.
* **Doctor Directory & Schedules:**
  * Public doctor directory searchable and filterable by Medical Specialization, Consultation Fee, and Available Day.
  * Doctor working hour configuration per day of the week.
  * Doctor leave/vacation management (`DoctorLeaves`) to prevent slot generation during physician absences.
* **Appointment Scheduling:**
  * Dynamic calculation of available 30-minute slots based on working hours, leaves, and booked appointments.
  * Interactive calendar view via `FullCalendar.js` with client-side slot selection.
  * Appointment lifecycle management with strict state machine validation: `Pending`, `Confirmed`, `Completed`, `Cancelled`, `Rejected`, `NoShow`.
  * Patient cancellation restricted to at least 2 hours prior to the scheduled start time.
  * Transition to `Completed` allowed only after the scheduled appointment time has elapsed.
  * Triple-layer double-booking prevention: service-level pre-check, database filtered unique index, and transactional `DbUpdateException` catch.
* **Clinical Records & Prescriptions:**
  * Medical visit record entry (Symptoms, Diagnosis, Visit Notes).
  * Secure single-file attachment per record (PDF or JPG/PNG image under `App_Data/uploads/records`).
  * Digital prescription creation with dynamic medication items (Medication Name, Dosage, Frequency, Duration, Instructions).
  * CSS print-friendly prescription layout for physical dispensing.
* **Notifications & Messaging:**
  * Dual-layer notifications: persisted in the database, then pushed via a strongly-typed SignalR Hub (`IAppointmentNotificationClient`).
  * Asynchronous email dispatch using MailKit via `IEmailService`.
  * Mock SMS interface via `ISmsService` for architecture completeness.
* **Administration & Analytics:**
  * Doctor registration approval and profile activation workflow.
  * Specialization management (CRUD).
  * Dashboard analytics: monthly appointment counts, specialization distribution, and revenue/fee collection summaries.
* **Audit & Entity Integrity:**
  * All domain entities inherit standard audit columns (`CreatedAt` and `UpdatedAt`) only (no soft delete is implemented).

### 4.2 Out-of-Scope Capabilities
To maintain project focus and ensure the highest engineering quality within the graduation timeframe, the following features are explicitly excluded:
* **Online Payment Gateways:** Real-time credit card processing (e.g., Stripe, Paymob) is excluded. Consultation fees and payment status (`Unpaid`, `Paid`) are tracked operationally within the system.
* **Direct Telemedicine / Doctor-Patient Chat:** Real-time two-way messaging or video conferencing is excluded.
* **AI clinic guide:** An optional OpenAI Responses API integration can suggest a broad specialty from a symptom description. A local keyword-based fallback remains available when the API is not configured or unavailable. Neither mode diagnoses, triages, or recommends treatment; the UI warns users not to submit real health or identity information.

---

## 5. Technology Stack

| Layer / Responsibility | Technology Selected | Rationale |
|---|---|---|
| **Web Framework & UI** | ASP.NET Core MVC (.NET 8) | Robust, server-rendered architecture with native dependency injection and security features. |
| **Data Access & ORM** | Entity Framework Core 8 | Modern code-first ORM supporting migrations, LINQ queries, and transaction management. |
| **Relational Database** | Microsoft SQL Server | Enterprise RDBMS providing ACID compliance, filtered unique indexes, and referential integrity. |
| **Authentication & RBAC** | ASP.NET Core Identity | Proven framework for secure password hashing, cookie authentication, and role authorization. |
| **Real-Time WebSockets** | ASP.NET Core SignalR | High-performance, bi-directional real-time communication for instant browser alerts. |
| **Calendar UI** | FullCalendar.js 6 | Industry-standard JavaScript calendar component for schedule visualization. |
| **Data Visualization** | Chart.js | Lightweight HTML5 canvas charting library for responsive administrative analytics. |
| **Styling & Components** | Bootstrap 5 & jQuery | Responsive UI layout, modal dialogues, and AJAX-driven schedule interactions. |
| **Email Communication** | MailKit & MimeKit | Robust, RFC-compliant cross-platform SMTP client library. |
| **Business Validation** | FluentValidation | Fluent API for decoupling complex domain validation rules from presentation models. |
| **Testing Frameworks** | xUnit, Moq, FluentAssertions | De facto testing stack in modern .NET for isolated unit and integration testing. |
| **CI/CD Pipeline** | GitHub Actions | Automated build, unit test execution, and deployment verification on every push. |
| **Deployment Target** | Microsoft Azure App Service & Azure SQL | A GitHub Actions workflow is included; an active deployment must be verified separately. |

---

## 6. High-Level Software Architecture

MediCare implements a strict **3-Project N-Tier Architecture** enforcing clear separation of concerns. The Web presentation layer references the Services business logic layer, which in turn references the Data access layer. Controllers interact solely with service abstractions.

```mermaid
flowchart TD
    ClientBrowser["Client Browser (Patient / Doctor / Admin)"]
    
    subgraph PresentationLayer["MediCare.Web (Presentation Layer)"]
        Controllers["MVC Controllers<br/>(Inject Services Only)"]
        RazorViews["Razor Views & ViewModels<br/>(Bootstrap 5, FullCalendar.js, Chart.js)"]
        SignalRHub["Strongly-Typed SignalR Hub<br/>(AppointmentHub)"]
    end

    subgraph BusinessLogicLayer["MediCare.Services (Business Logic Layer)"]
        ServiceInterfaces["Service Interfaces<br/>(IAppointmentService, IDoctorService, etc.)"]
        ServiceImplementations["Service Implementations<br/>(Slot Calculation, Conflict Detection, State Transitions)"]
        ValidationRules["FluentValidation Validators<br/>(Booking Rules, Hours Verification)"]
        Factories["Pattern Factories<br/>(AppointmentFactory, NotificationFactory)"]
        ExternalGateways["Communication Gateways<br/>(MailKit EmailService, Mock SmsService)"]
    end

    subgraph DataAccessLayer["MediCare.Data (Data Access Layer)"]
        UnitOfWork["Unit of Work & DbContext<br/>(Commit Transactions)"]
        Repositories["Generic & Specific Repositories<br/>(IAppointmentRepository, IDoctorRepository, etc.)"]
        EntityModels["Domain Entity Models<br/>(Appointments, Doctors, Patients, Records, Leaves)"]
        EFConfigurations["EF Core Fluent API Configurations<br/>(Filtered Unique Indexes, Relationships)"]
    end

    Database[("Microsoft SQL Server Database<br/>(Tables, Constraints, Indexes)")]

    ClientBrowser <-->|"HTTP(S) Requests / HTML / AJAX"| Controllers
    ClientBrowser <-->|"WebSocket / SignalR Transport"| SignalRHub
    Controllers --> RazorViews
    Controllers -->|"Invokes Business Operations"| ServiceInterfaces
    ServiceInterfaces -.-> ServiceImplementations
    ServiceImplementations --> ValidationRules
    ServiceImplementations --> Factories
    ServiceImplementations --> ExternalGateways
    ServiceImplementations -->|"Requests Aggregates / Commits"| UnitOfWork
    UnitOfWork --> Repositories
    Repositories --> EntityModels
    EntityModels --> EFConfigurations
    EFConfigurations -->|"SQL Queries & Schema Migrations"| Database
```

### Architectural Principles Enforced:
1. **Controller Encapsulation:** Controllers never inject `ApplicationDbContext` or repository interfaces directly; all database interaction is mediated through business services.
2. **Persistence Abstraction:** Data persistence and query execution are encapsulated within the Repository and Unit of Work patterns.
3. **Domain Integrity:** Scheduling invariants (such as lead-time cancellation windows and slot boundary checks) are enforced in `MediCare.Services` using FluentValidation rules.

---

## 7. Deliverables & Milestone Schedule

The project adheres to the official DEPI 2026 graduation timeline across four major milestone phases:

| Phase # | Phase Title | Deliverables Required | Official Deadline |
|---|---|---|---|
| **Phase 1** | **Project Planning & Requirements** | • Project Proposal Document<br>• Project Plan (Gantt Chart & Resource Allocation)<br>• Task Assignment & Roles (RACI Matrix)<br>• Risk Assessment & Mitigation Plan<br>• Engineering KPIs & Metrics<br>• Literature Review & Competitive Analysis<br>• Stakeholder Analysis & User Stories<br>• Functional & Non-Functional Specifications | **16 Oct 2026** |
| **Phase 2** | **System Analysis & Design** | • System Architecture Specification<br>• Use Case Diagrams & Narratives<br>• Entity-Relationship Diagram (ERD)<br>• Logical & Physical Database Schema<br>• Data Flow Diagrams (Context & Level 1 DFD)<br>• UML Sequence, Activity, State & Class Diagrams<br>• UI Wireframes & Design Guidelines<br>• Component & Deployment Diagrams<br>• Internal API Specifications (Swagger/OpenAPI) | **6 Nov 2026** |
| **Phase 3** | **Implementation & Execution** | • Complete NTier C# Solution Source Code<br>• EF Core Database Migrations & Initializer Seed Data<br>• Unit & Integration Test Suites<br>• Automated CI/CD GitHub Actions Pipeline<br>• Live Cloud Deployment on Microsoft Azure<br>• Project README & Configuration Guide | **30 Nov 2026** |
| **Phase 4** | **Testing, Reports & Final Presentation** | • Quality Assurance Test Plan & Test Cases<br>• Defect Tracking & Bug Reports<br>• Comprehensive End-User Manual<br>• Final Technical Documentation Report<br>• Graduation Project Presentation Slides (PPT/PDF)<br>• Demonstration Video & Live System Defense | **4 Dec 2026** |
