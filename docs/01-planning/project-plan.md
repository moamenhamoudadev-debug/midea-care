# Project Plan: MediCare — Clinic Management & Appointment System

## 1. Timeline Overview (4 October – 4 December 2026)
The MediCare development lifecycle spans 9 weeks, divided into four sequential phases following DEPI academic deadlines:
1. **Phase 1: Planning, Literature Review & Requirements** (4 Oct – 16 Oct 2026)
2. **Phase 2: System Analysis & Design** (17 Oct – 6 Nov 2026)
3. **Phase 3: Implementation & Cloud Deployment** (7 Nov – 30 Nov 2026)
4. **Phase 4: Testing, User Manual & Project Defense** (1 Dec – 4 Dec 2026)

---

## 2. Project Gantt Chart

```mermaid
gantt
    title MediCare 9-Week Execution Timeline (4 Oct - 4 Dec 2026)
    dateFormat  YYYY-MM-DD
    axisFormat  %d %b

    section Phase 1: Planning & Requirements
    Project Proposal & Scope          :done, p1_1, 2026-10-04, 2026-10-08
    Stakeholders & User Stories       :done, p1_2, 2026-10-06, 2026-10-10
    Risk Assessment & KPIs            :done, p1_3, 2026-10-08, 2026-10-11
    FR & NFR Specifications           :active, p1_4, 2026-10-12, 2026-10-15
    Literature Review & Task RACI     :active, p1_5, 2026-10-13, 2026-10-16
    Milestone 1 Submission (16 Oct)   :milestone, m1, 2026-10-16, 0d

    section Phase 2: Analysis & Design
    Architecture, Use Cases & Specs   :p2_1, 2026-10-17, 2026-10-20
    ERD & Normalized Database Schema  :p2_2, 2026-10-20, 2026-10-23
    DFD (Context & Level 1)           :p2_3, 2026-10-24, 2026-10-27
    UML Diagrams (Seq, Act, State)    :p2_4, 2026-10-27, 2026-10-30
    UI Wireframes & Design Guidelines :p2_5, 2026-10-31, 2026-11-03
    Component & Deployment Diagrams   :p2_6, 2026-11-03, 2026-11-06
    Internal API Spec (Swagger)       :p2_7, 2026-11-04, 2026-11-06
    Milestone 2 Submission (6 Nov)    :milestone, m2, 2026-11-06, 0d

    section Phase 3: Implementation
    Sprint 1: Scaffolding, Identity, Directory :crit, p3_1, 2026-11-07, 2026-11-13
    Sprint 2: Schedule, Slots, FullCalendar, SignalR :crit, p3_2, 2026-11-14, 2026-11-20
    Circuit Breaker Checkpoint (20 Nov) :milestone, m_cb, 2026-11-20, 0d
    Sprint 3: Records, Prescriptions, Admin, Email :p3_3, 2026-11-21, 2026-11-27
    Sprint 4: Azure Deploy, Seed Data, CI/CD, README :p3_4, 2026-11-28, 2026-11-30
    Milestone 3 Submission (30 Nov)   :milestone, m3, 2026-11-30, 0d

    section Phase 4: Testing & Defense
    Test Cases & Automation Execution :p4_1, 2026-12-01, 2026-12-02
    User Manual & Presentation Slides :p4_2, 2026-12-02, 2026-12-03
    Demo Video & Final Defense Prep   :p4_3, 2026-12-03, 2026-12-04
    Milestone 4 Final Defense (4 Dec) :milestone, m4, 2026-12-04, 0d
```

---

## 3. Week-by-Week Breakdown

### Week 1 (4 Oct – 11 Oct): Planning Foundation & Requirements Intake
* Draft official Project Proposal, problem statement, and scope boundaries.
* Identify stakeholders and model core user stories with Gherkin acceptance criteria.
* Establish initial Risk Assessment matrix and quantitative engineering KPIs.
* Structure GitHub repository documentation tree and README files.

### Week 2 (12 Oct – 16 Oct): Specifications & Literature Review
* Author Functional Requirements (FR-01 to FR-28) and Non-Functional Requirements (NFR-PERF, NFR-SEC, etc.).
* Conduct Literature Review comparing MediCare with market aggregators and generic booking tools.
* Generate Gantt chart and complete RACI task assignment breakdown.
* **Deliverable:** Submit Phase 1 Documentation on GitHub by **16 Oct 2026**.

### Week 3 (17 Oct – 23 Oct): System Architecture, Conceptual & Logical Data Modeling
* Define 3-Tier Layered Architecture (`MediCare.Web`, `MediCare.Services`, `MediCare.Data`).
* Author Use Case Diagrams and narrative specifications for Patient, Doctor, and Admin.
* Design Entity-Relationship Diagram (ERD) with relational cardinalities.
* Formulate normalized physical database schema including data types, foreign keys, and indexes.

### Week 4 (24 Oct – 30 Oct): Behavioral Modeling & Object Design
* Create Context-Level (Level 0) and Detailed (Level 1) Data Flow Diagrams (DFD).
* Construct UML Sequence Diagrams for core workflows: Slot Generation, Concurrency-Safe Booking, Prescription Issuance.
* Develop UML Activity Diagrams for clinical consultations and cancellation rules.
* Model Appointment State Machine diagram covering all valid and terminal states.
* Build detailed Class Diagram showcasing domain entities, repository interfaces, and services.

### Week 5 (31 Oct – 6 Nov): UI/UX Prototyping & Deployment Modeling
* Create screen wireframes and layout prototypes (Navbar, Calendar View, Doctor Dashboard, Medical Record Form).
* Define UI/UX design guidelines (Bootstrap 5 typography, responsive color palette, accessible alert states).
* Design Component Diagram illustrating service abstractions, repositories, and external gateways.
* Design Deployment Diagram mapping Azure App Service, Azure SQL, and browser clients.
* Document internal AJAX API endpoints using OpenAPI / Swagger specifications.
* **Deliverable:** Submit Phase 2 Design Documentation on GitHub by **6 Nov 2026**.

### Week 6 (7 Nov – 13 Nov) [Sprint 1]: Solution Foundation, Identity & Directory
* Scaffold Visual Studio solution with strict 3-tier dependencies (`Web` -> `Services` -> `Data`).
* Configure Entity Framework Core with SQL Server connection strings and DbContext.
* Implement ASP.NET Core Identity with roles (`Admin`, `Doctor`, `Patient`) and custom registration.
* Link `Doctor` and `Patient` entities to `AspNetUsers` using `UserId` foreign keys.
* Implement public Doctor Directory with search and filtering (by Specialization, Fee, Day).
* Build `DbInitializer` seeding baseline Specializations and Administrator credentials.

### Week 7 (14 Nov – 20 Nov) [Sprint 2]: Scheduling Engine, FullCalendar & Real-Time Sync
* Implement Doctor Working Hours and Vacation Leave management (`DoctorLeaves`).
* Develop deterministic 30-minute Slot Generation Engine in `MediCare.Services`.
* Apply SQL Server **Filtered Unique Index** on `(DoctorId, AppointmentDate, StartTime)` where `Status NOT IN (Cancelled, Rejected)`.
* Integrate `FullCalendar.js` to render available slots and manage booking submissions.
* Implement transactional booking workflow with service-level pre-check and `DbUpdateException` handler.
* Configure strongly-typed SignalR Hub (`AppointmentHub`) for real-time notification push.
* **Circuit-Breaker Milestone (20 Nov):** Review booking and concurrency results before taking on optional work.

### Week 8 (21 Nov – 27 Nov) [Sprint 3]: Clinical Records, Prescriptions, Admin & Email
* Build Doctor Consultation view for recording clinical notes, diagnosis, and symptoms.
* Implement single-file upload handler for diagnostic images/PDFs under `App_Data/uploads/records`.
* Build Prescription and `PrescriptionItems` entry form with dynamic client-side item addition.
* Create CSS `@media print` layout for professional prescription printing.
* Build Admin Dashboard with Chart.js analytics (Monthly Appointments, Specializations, Fees).
* Integrate MailKit `IEmailService` for appointment confirmation emails.
* Implement mock `ISmsService` for architecture completeness.

### Week 9 (28 Nov – 4 Dec): Deployment, Quality Assurance & Final Defense
* **28 Nov – 30 Nov [Release Sprint]:**
  * Provision Azure App Service and Azure SQL Database.
  * Configure environment variables for production connection strings and mail settings.
  * Setup GitHub Actions automated build and test workflow.
  * Execute `DbInitializer` on production database (seed doctors, schedules, patients, history).
  * Complete repository `README.md` with live deployment URL and local setup instructions.
  * **Deliverable:** Submit Phase 3 Implementation Source Code & Deployment by **30 Nov 2026**.
* **1 Dec – 4 Dec [Final Defense Sprint]:**
  * Execute automated xUnit tests (slot calculation, conflict test, state transitions) and log bug reports.
  * Author comprehensive End-User Manual with system screenshots.
  * Prepare PowerPoint presentation slides highlighting architecture, conflict design, and security.
  * Record short system demonstration video.
  * **Deliverable:** Final Presentation & Project Defense by **4 Dec 2026**.

---

## 4. Milestones and Deliverables Summary

| Milestone | Target Date | Deliverables Required | Success Criteria |
|---|---|---|---|
| **M1: Planning & Requirements** | **16 Oct 2026** | • Proposal, Plan, RACI, Risks, KPIs<br>• Literature Review<br>• Stakeholders, User Stories, FR/NFR | All documents committed to `/docs/01-planning`, `/docs/02-literature-review`, `/docs/03-requirements` on GitHub. |
| **M2: Analysis & System Design** | **6 Nov 2026** | • Architecture, Use Cases, ERD, Schema<br>• DFD, Sequence, Activity, State, Class<br>• Wireframes, Component, Deployment, API | Complete architectural and UML diagrams approved; ready for code scaffolding. |
| **M3: Implementation & Deployment** | **30 Nov 2026** | • Complete NTier C# Solution<br>• Live Azure Deployment & Seed Data<br>• CI/CD GitHub Actions Pipeline<br>• Comprehensive README.md | Application running live on Azure; core booking and clinical workflows functional. |
| **M4: Final Defense & Evaluation** | **4 Dec 2026** | • Automated Tests & Bug Reports<br>• User Manual & Final Tech Report<br>• Presentation Slides & Demo Video | Live system defense presented to DEPI evaluators; zero double-booking verified. |

---

## 5. Resource Allocation

### 5.1 Time Capacity (Solo Full-Stack Engineer)
* **Weekly Commitment:** 28 – 35 hours per week.
* **Allocation by Phase:**
  * Weeks 1–2 (Planning & Requirements): ~30 hours total.
  * Weeks 3–5 (Analysis & System Design): ~45 hours total.
  * Weeks 6–8 (Core Implementation Sprints): ~90 hours total (~30 hrs/week).
  * Week 9 (Deployment, QA & Presentation): ~35 hours total.

### 5.2 Engineering Tools & Infrastructure
* **IDE & Editors:** Microsoft Visual Studio 2022 Community / Visual Studio Code.
* **Database Management:** SQL Server 2022 LocalDB / SQL Server Management Studio (SSMS).
* **Version Control:** Git, Git Bash, GitHub repository with branch protection rules.
* **Diagramming:** Mermaid.js (embedded Markdown) and draw.io / Figma (wireframes).
* **API Testing & Debugging:** Swagger UI, Postman, Browser Developer Tools.
* **Cloud Hosting:**
  * Primary: Microsoft Azure App Service (Linux or Windows B1 / Free tier F1) + Azure SQL Database (Free / Serverless).
  * Fallback Hosting: SmarterASP.NET or MonsterASP.NET free student hosting.

---

## 6. MVP Scope Hierarchy & The Circuit-Breaker Rule

### 6.1 Priority Hierarchy If Time Becomes Constrained
If unexpected technical challenges or time constraints occur during development, tasks must be executed strictly according to this priority order:
1. **Priority 1 (Mandatory Core):** Appointment Booking with Slot Engine and Filtered Unique Index Conflict Detection.
2. **Priority 2 (Clinical Workflow):** Doctor Consultation Notes, Medical Records, Prescriptions, and Print Formatting.
3. **Priority 3 (Communications):** SignalR Real-Time In-App Alerts, followed by MailKit Email Confirmations.
4. **Priority 4 (Administration):** Doctor Approval Workflow, Specialization CRUD, and Chart.js Analytics.
5. **Priority 5 (Secondary Enhancements):** CSV Data Export, Theme/Dark-Mode toggle, Mock SMS integration.

### 6.2 The 20 November Circuit-Breaker Rule
> [!IMPORTANT]
> **Hard Engineering Rule:**  
> If the core appointment booking engine and double-booking conflict prevention are **not fully functional, concurrency-tested, and verified by 20 November 2026 (end of Sprint 2)**, all development of secondary features (analytics charts, CSV exports, theme polish) is **immediately frozen**.  
> The remaining development time will be dedicated solely to stabilizing the booking workflow, writing automated unit tests, executing database seeding, and deploying the stable core to Azure.
