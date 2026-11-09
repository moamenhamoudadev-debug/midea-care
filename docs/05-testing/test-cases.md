# MediCare — Comprehensive Test Cases Specification

## 1. Authentication & Identity Management

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-AUTH-01** | Patient Registration with Valid Credentials | User not logged in | 1. Navigate to `/Account/RegisterPatient`<br/>2. Fill valid name, email, Egyptian phone, DOB, compliant password MeetingRequirements!<br/>3. Submit form | Account created in Identity, Patient record created in DB, assigned to `Patient` role, redirected to login. | **PASS** |
| **TC-AUTH-02** | Registration Password Complexity Rejection | User on registration form | 1. Enter password `simple` (lacks digit, uppercase, symbol, min length)<br/>2. Submit form | FluentValidation rejects submission with specific complexity errors. | **PASS** |
| **TC-AUTH-03** | Duplicate Email Registration Prevention | User `test@medicare.com` already registered | 1. Attempt registering new patient with `test@medicare.com`<br/>2. Submit form | Registration rejected; friendly error message displayed; no duplicate user created. | **PASS** |
| **TC-AUTH-04** | Doctor Registration Syndicate License Format | Doctor applicant on `/Account/RegisterDoctor` | 1. Enter license number with invalid syntax<br/>2. Submit form | FluentValidation prevents submission; requires valid Egyptian Syndicate ID format. | **PASS** |
| **TC-AUTH-05** | Doctor Registration Pending Approval Flag | Valid doctor registration submitted | 1. Submit valid doctor registration<br/>2. Inspect database `Doctors` table | Doctor record created with `IsApproved = false`; cannot accept bookings until approved. | **PASS** |
| **TC-AUTH-06** | Secure Cookie Attributes Verification | User logged into system | 1. Authenticate as any user<br/>2. Inspect `.AspNetCore.Identity.Application` cookie headers | Cookie has `HttpOnly = true`, `SameSite = Lax`, `Secure` in production, 24h sliding expiration. | **PASS** |

---

## 2. Doctor Discovery & Scheduling Engine

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-SCHED-01** | Unapproved Doctor Excluded from Public Directory | Doctor `IsApproved = false` exists | 1. Navigate to `/Home/Doctors` (Public directory)<br/>2. Search for unapproved doctor | Unapproved doctor is excluded from search results and listing pages. | **PASS** |
| **TC-SCHED-02** | Doctor Directory Filter by Governorate and Specialty | Seeded doctors across 27 governorates | 1. Select Specialty `Cardiology` and Governorate `Alexandria`<br/>2. Apply filter | Directory renders only approved cardiologists located in Alexandria. | **PASS** |
| **TC-SCHED-03** | SlotEngine Generates 30-Min Intervals | Doctor has schedule 09:00 - 12:00 on Monday | 1. Request slots for target Monday<br/>2. Inspect generated intervals | Generates exactly 6 slots: 09:00, 09:30, 10:00, 10:30, 11:00, 11:30. | **PASS** |
| **TC-SCHED-04** | SlotEngine Suppresses Leave Intervals | Doctor approved leave on 2026-11-20 | 1. Request slots for 2026-11-20 | Zero available slots returned for the leave date. | **PASS** |
| **TC-SCHED-05** | SlotEngine Excludes Past Time Slots | Current time is 11:15 AM on date of consultation | 1. Request slots for today | Slots at 09:00, 09:30, 10:00, 10:30, 11:00 marked unavailable; only future slots shown. | **PASS** |

---

## 3. Booking, Concurrency & State Machine

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-BOOK-01** | Single-Threaded Booking Lifecycle | Free slot available | 1. Patient selects slot and books<br/>2. Doctor confirms appointment | Appointment created as `Pending (0)`, transitions to `Confirmed (1)` upon acceptance. | **PASS** |
| **TC-BOOK-02** | Multi-Threaded 10-Patient Concurrency Test | 1 slot open for Doctor #1 on 2026-11-17 at 10:00 | 1. Trigger 10 parallel asynchronous booking requests simultaneously | Exactly ONE request succeeds; 9 requests fail with HTTP 409 Conflict. Filtered index holds. | **PASS** |
| **TC-BOOK-03** | Rebooking Allowed After Cancellation | Existing appointment is `Cancelled (3)` | 1. Another patient books the same slot on the same doctor | Booking succeeds because `[Status] <> 3 AND [Status] <> 4` filters out cancelled rows from uniqueness constraint. | **PASS** |
| **TC-BOOK-04** | Patient Timely Cancellation (> 2 Hours) | Appointment scheduled 24 hours in future | 1. Patient requests cancellation | Status transitions to `Cancelled (3)` and slot is freed for other patients. | **PASS** |
| **TC-BOOK-05** | Patient Late Cancellation Blocked (≤ 2 Hours) | Appointment starts in 45 minutes | 1. Patient requests cancellation | Request rejected with policy error: "Cancellation must be at least 2 hours prior to appointment." | **PASS** |
| **TC-BOOK-06** | Appointment Direct Completion Blocked | Appointment in `Confirmed` state | 1. Attempt calling any status update endpoint to set `Completed` directly | Action rejected; status `Completed (2)` can only be set via clinical encounter save. | **PASS** |

---

## 4. Clinical Records, Encounters & Prescriptions

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-CLIN-01** | Atomic Encounter Save | Doctor viewing confirmed appointment | 1. Fill diagnosis, vital signs, physical exam, and 2 medications<br/>2. Submit encounter | Transaction saves `MedicalRecord`, `Prescription`, and items atomically; appointment marked `Completed (2)`. | **PASS** |
| **TC-CLIN-02** | Diagnostic File Attachment Upload | Doctor uploading lab result | 1. Select 2 MB PDF file<br/>2. Submit with encounter | File saved to `App_Data/uploads/records` with GUID filename; relative path persisted. | **PASS** |
| **TC-CLIN-03** | Invalid File Extension Upload Rejection | Doctor uploading file | 1. Select `.exe` or `.bat` file<br/>2. Submit encounter | Validation failure: only `.jpg`, `.jpeg`, `.png`, `.pdf` allowed. | **PASS** |
| **TC-CLIN-04** | Oversized Attachment Rejection | Doctor uploading file | 1. Select 8 MB PDF (exceeds 5 MB limit)<br/>2. Submit encounter | Validation failure: attachment exceeds maximum 5 MB limit. | **PASS** |
| **TC-CLIN-05** | Prescription Print View Formatting | Valid prescription exists | 1. Navigate to `/Prescriptions/Print/42` | Clean layout renders with doctor header, syndicate ID, patient age, items, and clean print styles. | **PASS** |

---

## 5. Security & Penetration Testing

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-SEC-01** | Medical Record IDOR Protection | Patient A (ID 1) and Patient B (ID 2) | 1. Authenticate as Patient A<br/>2. Request `/MedicalRecords/Details/99` (belongs to Patient B) | Server detects ownership mismatch, logs `Security IDOR`, and returns HTTP 403 Forbidden. | **PASS** |
| **TC-SEC-02** | Prescription Cross-Doctor IDOR Protection | Doctor A and Doctor B | 1. Authenticate as Doctor A<br/>2. Request `/Prescriptions/Print/50` (issued by Doctor B) | Server detects doctor mismatch, logs `Security IDOR`, and returns HTTP 403 Forbidden. | **PASS** |
| **TC-SEC-03** | State-Changing POST Anti-CSRF Protection | Authenticated session | 1. Submit POST to `/Appointments/Cancel/10` without `__RequestVerificationToken` | Request rejected with HTTP 400 Bad Request (Anti-Forgery token validation failed). | **PASS** |
| **TC-SEC-04** | Admin Endpoint Role Guard | User logged in as Patient | 1. Directly browse `/Admin/Index` or `/Admin/ApproveDoctor/5` | Redirected to `/Account/AccessDenied` or returned HTTP 403 Forbidden. | **PASS** |
| **TC-SEC-05** | CSV Formula Injection (CWE-1236) Neutralization | Patient name starts with `=cmd\|' /C calc'!A0` | 1. Export CSV via `/Admin/ExportAppointmentsCsv` | Leading formula characters (`=`, `+`, `-`, `@`, `\t`) prepended with `'` preventing spreadsheet execution. | **PASS** |
| **TC-SEC-06** | Attachment Path Traversal Defense | Attacker crafts request with `../../etc/passwd` | 1. Request `/MedicalRecords/DownloadAttachment/10` | Sanitized filename combined strictly with `WebRootPath`; directory traversal blocked. | **PASS** |

---

## 6. Admin Analytics & Reporting

| Test Case ID | Test Title | Pre-Conditions | Test Steps | Expected Result | Status |
|---|---|---|---|---|---|
| **TC-ADM-01** | Doctor Syndicate Approval Workflow | Unapproved doctor in queue | 1. Admin clicks "Approve Doctor"<br/>2. Inspect doctor status | `IsApproved` flipped to true; confirmation email dispatched; doctor visible in directory. | **PASS** |
| **TC-ADM-02** | Doctor Rejection with Reason | Unapproved doctor in queue | 1. Admin clicks "Reject" with reason "Invalid ID"<br/>2. Submit rejection | Doctor record deleted or deactivated; notification email sent with reason. | **PASS** |
| **TC-ADM-03** | Monthly Analytics Aggregation | Appointments exist across past 12 months | 1. View `/Admin/Reports` | Chart.js displays accurate volume, completion rate, no-show rate, and revenue totals. | **PASS** |
| **TC-ADM-04** | Appointments RFC 4180 CSV Export | Appointments exist | 1. Click "Export CSV"<br/>2. Inspect downloaded binary stream | Byte stream contains UTF-8 BOM preamble (`0xEF, 0xBB, 0xBF`), quoted comma fields, and sanitized strings. | **PASS** |
