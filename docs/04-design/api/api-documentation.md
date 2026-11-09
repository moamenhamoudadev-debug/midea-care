# Internal API Specification: MediCare

While MediCare is built on server-rendered ASP.NET Core MVC (Razor Views), specific interactive UI components—notably `FullCalendar.js`, the dynamic slot picker, and real-time SignalR notifications—rely on lightweight internal JSON endpoints.

This document formalizes the internal REST/JSON API contract.

---

## 1. Global API Standards

* **Base URL:** `/api`
* **Content-Type:** `application/json; charset=utf-8`
* **Authentication:** ASP.NET Core Identity Cookie Authentication (`.AspNetCore.Identity.Application`).
* **Standard Success Response Format:**
  ```json
  {
    "success": true,
    "data": { ... }
  }
  ```
* **Standard Error Response Format:**
  ```json
  {
    "success": false,
    "error": "Human-readable primary error description.",
    "errors": [
      "Field-specific validation failure message."
    ]
  }
  ```

---

## 2. API Endpoints Catalog

---

### 2.1 Calculate Available Doctor Slots
Returns the dynamic unbooked 30-minute intervals for a specific doctor on a selected calendar date. Used by `FullCalendar.js` on the patient booking interface.

* **Method:** `GET`
* **Route:** `/api/calendar/slots`
* **Authorization:** Anonymous / Authenticated (`Patient`, `Doctor`, `Admin`).
* **Query Parameters:**
  * `doctorId` (`integer`, required): The primary key of the doctor.
  * `date` (`string`, ISO 8601 `YYYY-MM-DD`, required): Target appointment date.
* **Response `200 OK`:**
  ```json
  {
    "success": true,
    "data": [
      {
        "startTime": "09:00:00",
        "endTime": "09:30:00",
        "formattedTime": "09:00 AM - 09:30 AM",
        "isAvailable": true
      },
      {
        "startTime": "09:30:00",
        "endTime": "10:00:00",
        "formattedTime": "09:30 AM - 10:00 AM",
        "isAvailable": true
      },
      {
        "startTime": "10:00:00",
        "endTime": "10:30:00",
        "formattedTime": "10:00 AM - 10:30 AM",
        "isAvailable": false
      }
    ]
  }
  ```
* **Response `400 Bad Request`:**
  ```json
  {
    "success": false,
    "error": "Invalid query parameters.",
    "errors": ["The date parameter must be today or in the future."]
  }
  ```
* **Response `404 Not Found`:**
  ```json
  {
    "success": false,
    "error": "Doctor not found or doctor account is pending approval."
  }
  ```

---

### 2.2 Get Doctor Schedule Events
Returns scheduled appointments formatted as FullCalendar event objects for the authenticated doctor's private calendar view.

* **Method:** `GET`
* **Route:** `/api/calendar/doctor-events`
* **Authorization:** Authenticated (`Doctor` role required).
* **Query Parameters:**
  * `start` (`string`, ISO 8601, required): Range start date.
  * `end` (`string`, ISO 8601, required): Range end date.
* **Response `200 OK`:**
  ```json
  {
    "success": true,
    "data": [
      {
        "id": "101",
        "title": "Consultation: Omar Khaled",
        "start": "2026-11-15T09:00:00Z",
        "end": "2026-11-15T09:30:00Z",
        "status": "Confirmed",
        "color": "#198754",
        "url": "/Appointments/Details/101"
      },
      {
        "id": "102",
        "title": "Consultation: Sarah Adel",
        "start": "2026-11-15T10:00:00Z",
        "end": "2026-11-15T10:30:00Z",
        "status": "Pending",
        "color": "#FFC107",
        "url": "/Appointments/Details/102"
      }
    ]
  }
  ```
* **Response `401 Unauthorized` / `403 Forbidden`:** Returned if user is not logged in as an approved Doctor.

---

### 2.3 Pre-Check Slot Conflict
Validates whether a candidate slot is currently free immediately prior to presenting the booking confirmation modal, reducing race condition aborts.

* **Method:** `POST`
* **Route:** `/api/appointments/check-conflict`
* **Authorization:** Authenticated (`Patient` role required).
* **Request Body:**
  ```json
  {
    "doctorId": 5,
    "appointmentDate": "2026-11-15",
    "startTime": "09:30:00"
  }
  ```
* **Response `200 OK` (Slot is Free):**
  ```json
  {
    "success": true,
    "data": {
      "hasConflict": false,
      "message": "Slot is available."
    }
  }
  ```
* **Response `409 Conflict` (Slot is Already Booked):**
  ```json
  {
    "success": false,
    "error": "The selected time slot is already booked. Please choose an alternative time."
  }
  ```

---

### 2.4 Retrieve Unread Notifications
Pulls persisted unread notifications for the currently logged-in user. Called upon initial page load to synchronize any alerts missed while the browser tab was closed.

* **Method:** `GET`
* **Route:** `/api/notifications/unread`
* **Authorization:** Authenticated (`Patient`, `Doctor`, `Admin`).
* **Response `200 OK`:**
  ```json
  {
    "success": true,
    "data": [
      {
        "id": 42,
        "title": "Appointment Confirmed",
        "message": "Dr. Ahmed Mahmoud confirmed your appointment on Nov 15, 09:00 AM.",
        "createdAt": "2026-11-10T14:32:00Z",
        "isRead": false
      }
    ]
  }
  ```

---

### 2.5 Mark Notification as Read
Updates the `IsRead` boolean flag of a specific notification to `true`.

* **Method:** `POST`
* **Route:** `/api/notifications/mark-read/{id}`
* **Authorization:** Authenticated (Enforces ownership: `notification.UserId == currentUserId`).
* **Route Parameter:** `id` (`integer`, required).
* **Response `200 OK`:**
  ```json
  {
    "success": true,
    "data": {
      "id": 42,
      "isRead": true
    }
  }
  ```
* **Response `403 Forbidden`:** Returned if notification belongs to another user.
