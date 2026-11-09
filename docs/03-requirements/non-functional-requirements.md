# Non-Functional Requirements Specification: MediCare

## 1. Specification Overview
This document defines the Non-Functional Requirements (NFRs) governing the architectural quality, security posture, operational performance, and reliability of **MediCare**.

All requirements are defined with measurable quantitative criteria and correspond directly with the benchmarks established in `kpis.md`.

---

## 2. Requirements Matrix by Quality Attribute

### 2.1 Performance Requirements (NFR-PERF)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-PERF-01** | **Slot Generation Engine Response Time:** The backend algorithm that computes available 30-minute slots for a doctor must complete in sub-second time. | **< 300 ms** per doctor-month | Stopwatch profiling across 1,000 synthetic schedule calculations with leaves and active appointments. (Consistent with **KPI-02**). |
| **NFR-PERF-02** | **Server-Rendered Page Latency:** Primary public views (Doctor Directory, Profile, Booking View) must load rapidly over broadband connections. | **< 2.0 seconds** average page response | Chrome DevTools Network audit and Google Lighthouse running against Azure deployment. (Consistent with **KPI-03**). |
| **NFR-PERF-03** | **Real-Time Notification Delivery:** Latency between appointment status mutation on the server and message rendering in the browser client via SignalR. | **< 2.0 seconds** end-to-end | Client-side timestamp logging on receiving `AppointmentStatusChanged` SignalR event. (Consistent with **KPI-04**). |
| **NFR-PERF-04** | **Database Query Efficiency:** All queries searching appointments and doctor availability must utilize indexed lookups to prevent table scans. | Index seeks on all key relational filters | SQL Server Execution Plan inspection verifying index seek operations on `(DoctorId, AppointmentDate, StartTime)`. |

---

### 2.2 Security Requirements (NFR-SEC)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-SEC-01** | **Insecure Direct Object Reference (IDOR) Defense:** Patients must never be able to access, modify, or print clinical records or prescriptions belonging to other patients. | **100% rejection** of unauthorized resource access with `HTTP 403 Forbidden` | Automated security unit and integration tests attempting cross-patient URL access. (Addresses **RSK-02**). |
| **NFR-SEC-02** | **Cross-Site Request Forgery (CSRF) Protection:** All state-changing HTTP POST, PUT, and DELETE endpoints must validate anti-forgery tokens. | **100% enforcement** on all HTML forms | Global or controller-level `[AutoValidateAntiforgeryToken]` attribute verification. |
| **NFR-SEC-03** | **Credential & Secret Protection:** No database connection strings, SMTP passwords, or API keys shall be stored in plaintext within source control. | **Zero plaintext secrets** in Git repository history | Use of .NET `dotnet user-secrets` in local development and Azure App Service Environment Variables in production. (Addresses **RSK-07**). |
| **NFR-SEC-04** | **File Upload Restrictions:** Medical record attachments must enforce strict MIME type whitelisting (image/jpeg, image/png, application/pdf) and file size caps. | Max file size: **5 MB**; extension and signature validation | Server-side validation rejecting executable files (.exe, .dll, .js) and files exceeding the size limit. |
| **NFR-SEC-05** | **Password Security & Hashing:** User passwords managed by ASP.NET Core Identity must comply with industry complexity standards. | Min 8 chars, 1 uppercase, 1 lowercase, 1 number, 1 special character | Standard PBKDF2 with HMAC-SHA256 password hasher configured in Identity options. |

---

### 2.3 Reliability & Data Integrity Requirements (NFR-REL)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-REL-01** | **Zero Double-Booking Guarantee:** The database must physically prevent concurrent overlapping reservations for the same doctor and slot. | **0.0% double-booking rate** under parallel load | Automated xUnit test executing 10 simultaneous threads attempting to book the identical slot. (Consistent with **KPI-01**). |
| **NFR-REL-02** | **Notification Durability:** Real-time push failures (due to client tab closure or network drop) must not cause permanent alert loss. | **100% notification persistence** | All alerts committed to the `Notifications` table prior to SignalR broadcast. Client retrieves unread notifications upon reconnect. (Addresses **RSK-03**). |
| **NFR-REL-03** | **Transactional Consistency (ACID):** Operations modifying multiple entities (e.g., creating a Medical Record while updating Appointment to `Completed`) must execute atomically. | Zero partial data persistence on failure | Execution within Unit of Work `SaveChangesAsync()` transaction block; automated rollback test on failure. |
| **NFR-REL-04** | **Hosting Uptime on Evaluation Day:** The cloud-hosted instance must remain reachable and operational during the graduation evaluation defense. | **100.0% availability** during evaluation window | Azure App Service diagnostic logging and `/health` probe verification. (Consistent with **KPI-08**). |

---

### 2.4 Usability & Accessibility Requirements (NFR-USE)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-USE-01** | **Booking Funnel Efficiency:** The booking user journey must be intuitive and compact without unnecessary redirection. | **≤ 4 discrete interactions** from doctor selection to booking confirmation | Manual user experience audit. (Consistent with **KPI-05**). |
| **NFR-USE-02** | **Responsive Layout:** The application UI must render legibly across mobile devices, tablets, and desktop viewports. | Responsive rendering on viewports from **360px to 1920px** | Verification via Chrome Device Mode across standard mobile and desktop breakpoints using Bootstrap 5. |
| **NFR-USE-03** | **Prescription Print Optimization:** The prescription view must render cleanly for physical paper dispensing without web UI artifacts (navbars, buttons, footers). | Clean single-page output formatted via `@media print` | Browser print preview audit verifying inclusion of clinic header, physician signature, and suppression of navigation elements. |

---

### 2.5 Maintainability & Architecture Requirements (NFR-MNT)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-MNT-01** | **Strict 3-Tier Layer Decoupling:** The Presentation layer (`MediCare.Web`) must never reference database contexts or persistence repositories directly. | **Zero DbContext injections** in MVC controllers | Architecture dependency audit; controllers inject `MediCare.Services` interfaces exclusively. |
| **NFR-MNT-02** | **Validation Decoupling:** Complex domain business rules must be maintained outside presentation controllers. | **100% business rules** implemented via FluentValidation validators | Code review verifying FluentValidation classes in `MediCare.Services`. |
| **NFR-MNT-03** | **Extensible Notification Design:** Communication channels must be decoupled from the core appointment workflow using design patterns. | Use of `NotificationFactory` and `IEmailService` abstractions | Static code analysis verifying polymorphic creation of notifications. |

---

### 2.6 Testability Requirements (NFR-TST)
| Req ID | Attribute & Specification | Target Metric | Verification Method |
|---|---|---|---|
| **NFR-TST-01** | **Automated Test Coverage:** Core business logic, slot calculation, and state transition services must be covered by automated tests. | **≥ 60.0% code coverage** on `MediCare.Services` assembly | Coverlet test coverage report generated during CI execution. (Consistent with **KPI-06**). |
| **NFR-TST-02** | **Decoupled External Dependencies:** External dependencies (SMTP email server, SMS gateway) must be mockable during testing. | **100% of unit tests** run without live internet access | All communication interfaces mocked via `Moq` in xUnit test suites. |
