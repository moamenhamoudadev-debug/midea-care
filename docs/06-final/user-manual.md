# MediCare — Comprehensive User Manual & Operations Guide

## 1. System Overview & Persona Hierarchy

MediCare is a clinic management and appointment booking application built on ASP.NET Core 8 MVC, Entity Framework Core, SQL Server, and SignalR. It supports four operational roles:

1. **System Administrator (`Admin`)**: Oversees clinic operations, verifies medical licenses, manages clinical departments, audits patient accounts, and exports analytics reports.
2. **Medical Clinician (`Doctor`)**: Manages clinical practice schedules, defines working hours, declares leaves, conducts consultations, and creates medical records and digital prescriptions.
3. **Patient (`Patient`)**: Discovers approved clinicians across Egyptian governorates, schedules consultations, reschedules visits, manages medical history and allergies, and receives real-time alerts.

---

## 2. Administrator Operations Guide

### 2.1 Dashboard Overview (`/Admin`)
Upon authenticating with Administrator credentials, the system presents the operations dashboard:
- **Top Metrics Ribbon**: Total visits logged, active certified doctors, pending doctor licensing applications, completed visits, total paid revenue (EGP), and pending accounts receivable.
- **Monthly Clinical Trends**: Interactive 12-month visual charts tracking completed appointments, patient cancellations, and no-show occurrences.
- **Specialization Distribution**: Doughnut visualization illustrating appointment volume across clinical departments.
- **Financial Revenue Chart**: Stacked bar comparison of paid consultation fees versus unpaid/pending clinic fees.
- **Top Performing Clinicians**: Ranked league table displaying the top 5 doctors by consultation volume and revenue generation.
- **Patient Demographics & Cohort Analysis**: Aggregate distributions of patient gender and age brackets (`<18`, `18-35`, `36-50`, `50+`).

### 2.2 Doctor Licensing & Application Approvals (`/Admin/Approvals`)
To ensure medical safety, newly registered doctors cannot be booked until verified by an Administrator:
1. Navigate to **Doctor Approvals** via the top navigation bar or dashboard button.
2. Review pending applicants with their medical license number, graduation credentials, and consultation fees.
3. Click **Approve Doctor** to grant clinical booking privileges. The system updates the status and automatically dispatches a welcoming email to the doctor.
4. Click **Reject Application** to deny credentials, with an optional formal reason communicated via email.

### 2.3 Patient Registry & Account Access Management (`/Admin/Patients`)
Administrators can inspect patient profiles and control system access:
1. Navigate to **Patients** in the admin header.
2. Search patients by **Name**, **Email**, or **Phone Number** using the search toolbar.
3. Inspect patient clinical flags: recorded **Allergies**, **Medical History**, and **Blood Group**.
4. To mitigate fraudulent activity or enforce security policies, click **Lockout** to disable login access. Click **Unlock** to restore active access immediately.

### 2.4 Clinical Specializations Management (`/Admin/Specializations`)
Manage the medical specialties offered by the clinic:
1. Click **Add Specialization** to define a new medical department (e.g., Cardiology, Dermatology, Neurology).
2. Click **Edit** to update an existing department's title or clinical scope.
3. Click **Delete** to retire a specialization. **Safety Guard**: The system prevents deletion if any doctors are currently assigned to that specialty.

### 2.5 Multi-Format Reports & Data Export (`/Admin/Reports`)
Export clinic appointment records in three formats:
- **CSV Export** (`/Admin/ExportAppointmentsCsv`): UTF-8 encoded with BOM, protected against CSV Formula Injection (CWE-1236).
- **Excel Spreadsheet** (`/Admin/ExportAppointmentsExcel`): Native Microsoft Office OpenXML `.xlsx` workbook formatted with styled column headers and typed data cells.
- **PDF Report** (`/Admin/ExportAppointmentsPdf`): Standard PDF document featuring clinic branding, metadata summaries, and formatted tabular appointment data.

---

## 3. Doctor (Clinician) Operations Guide

### 3.1 Weekly Working Hours & Consultation Setup (`/Doctor/Schedule`)
Clinicians configure their availability to enable online booking:
1. Navigate to **My Schedule**.
2. Select working days (e.g., Sunday through Thursday).
3. For each active day, specify **Start Time** and **End Time** (anchored to Local Egypt Clinic Time).
4. Save schedule. The appointment slot engine immediately generates valid booking slots for patients.

### 3.2 Leave Management (`/Doctor/Leaves`)
To block consultations during conferences or personal time off:
1. Navigate to **Manage Leaves**.
2. Click **Request Leave** and specify the start and end dates along with a reason.
3. Once declared, the system automatically blocks the leave window from the booking calendar and notifies affected patients.

### 3.3 Patient Consultations & Appointment Workflow (`/Doctor/Appointments`)
Clinicians track patient visits in real time:
1. **Confirmed Visits**: Review scheduled patients for the day.
2. **Status Progression**:
   - Mark as **Completed** once the consultation concludes.
   - Mark as **No-Show** if the patient fails to arrive.
   - Mark as **Cancelled** if emergency rescheduling is required.
3. Real-time updates push directly to the patient's screen via SignalR.

### 3.4 Authoring Medical Records & Prescriptions
Following a completed consultation:
1. Open the appointment details view and click **Create Medical Record**.
2. Enter **Diagnosis**, **Symptoms**, **Physical Examination Notes**, and **Treatment Plan**.
3. Click **Add Digital Prescription** to prescribe medications:
   - Medication Name (e.g., Amoxicillin 500mg)
   - Dosage (e.g., 1 capsule every 8 hours)
   - Duration (e.g., 7 days)
   - Special Instructions (e.g., Take after meals)
4. The patient can immediately view and download their prescription from their portal.

---

## 4. Patient Operations Guide

### 4.1 Clinician Discovery & Governorate Filtering (`/Doctors`)
Patients search for specialized care across Egypt:
1. Navigate to **Find a Doctor**.
2. Filter clinicians by:
   - **Governorate**: Filter by all Egyptian governorates (Cairo, Giza, Alexandria, Dakahlia, Red Sea, etc.) in English or Arabic.
   - **Medical Specialization**: Cardiology, Pediatrics, Ophthalmology, etc.
   - **Doctor Name**: Search by doctor name keyword.
3. View clinician profiles displaying biography, consultation fee (EGP), and clinic address.

### 4.2 Online Appointment Booking
1. Click **Book Appointment** on the selected doctor's profile.
2. Pick an available date from the calendar.
3. Select an unoccupied time slot generated dynamically by the slot engine.
4. Confirm booking. The slot is atomically locked with SQL Server concurrency protection.
5. Receive an in-app booking alert. An email is sent when an email provider is configured.

### 4.3 Atomic Appointment Rescheduling (`/Appointments/Reschedule/{id}`)
If your schedule changes, appointments can be rescheduled online:
1. Navigate to **My Appointments**.
2. Locate the confirmed visit and click the **Reschedule** button.
3. **Business Rules**:
   - Rescheduling is permitted up to **2 hours** prior to the original appointment time.
   - Choose a new date within the next **30 days**.
   - Select an available time slot matching the doctor's active working hours and non-leave dates.
4. Submit request. The system atomically moves your reservation, unlocks the previous slot, and updates the doctor's calendar.

### 4.4 Managing Patient Profile & Allergies (`/Account/Profile`)
Keep medical history up to date for attending physicians:
1. Click on your user avatar and select **My Profile**.
2. Review personal contact information and demographics.
3. Update clinical profile fields:
   - **Known Allergies**: Enter drug or food allergies (e.g., Penicillin, NSAIDs, Sulfa drugs, Latex).
   - **Medical History & Chronic Conditions**: Record preexisting conditions (e.g., Type 2 Diabetes, Hypertension, Asthma).
   - **Emergency Contact**: Provide phone number of next of kin.
4. Click **Update Profile**. Attending doctors will see these clinical alerts when reviewing your records.

### 4.5 Notifications & 24-Hour Automated Reminders
- **Real-Time Alerts**: Receive instant SignalR toasts when your appointment status changes or a doctor updates your consultation notes.
- **24-Hour Automated Reminders**: The background worker checks for confirmed appointments about 24 hours ahead and sends in-app reminders. Email and SMS delivery depend on provider configuration; local development may use mock SMS.
