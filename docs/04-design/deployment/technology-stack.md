# Technology Stack Specification: MediCare

This document provides a comprehensive technical inventory, version baseline, and engineering justification for all frameworks, libraries, protocols, and hosting platforms comprising **MediCare**.

---

## 1. Complete Technology Inventory

| Category | Component / Library | Version | Engineering Justification & Selection Rationale |
|---|---|:---:|---|
| **Runtime & Framework** | Microsoft .NET | **8.0 LTS** | Enterprise Long Term Support release offering performance optimizations, cross-platform capabilities, and unified runtime support. |
| **Language** | C# | **12.0** | Modern language features: primary constructors, collection expressions, pattern matching, and nullable reference types. |
| **Web Presentation** | ASP.NET Core MVC | 8.0 | Robust server-rendered architecture with built-in dependency injection, filter pipelines, and native security features. |
| **Object-Relational Mapping** | Entity Framework Core | 8.0 | Code-First database migrations, LINQ to Entities query optimization, and support for SQL Server filtered unique indexes. |
| **Relational Database** | Microsoft SQL Server | 2022 / Azure SQL | Enterprise RDBMS providing ACID compliance, filtered unique indexes, transactional locking, and temporal auditing capabilities. |
| **Identity & Authentication** | ASP.NET Core Identity | 8.0 | Battle-tested security framework for password hashing (PBKDF2-HMAC-SHA256), cookie authentication, and role authorization. |
| **Real-Time Communication** | ASP.NET Core SignalR | 8.0 | High-performance WebSocket abstraction with automatic fallback to Server-Sent Events and Long Polling. |
| **Scheduling Calendar** | FullCalendar.js | 6.1 | Leading open-source JavaScript calendar library providing interactive month/week views and seamless AJAX slot binding. |
| **Data Visualization** | Chart.js | 4.4 | Lightweight HTML5 canvas charting library for rendering responsive bar, line, and doughnut analytics without third-party plugins. |
| **CSS Framework** | Bootstrap | 5.3 | Responsive mobile-first grid, accessible form components, utility classes, and built-in modal and toast dialogue support. |
| **Client Scripting** | jQuery | 3.7 | Streamlines AJAX requests, dynamic DOM manipulation for prescription item repeaters, and calendar event listeners. |
| **Business Validation** | FluentValidation.AspNetCore | 11.3 | Decouples complex domain invariants (cancellation windows, slot overlaps) from presentation models and controller actions. |
| **Email Protocol & Client** | MailKit & MimeKit | 4.7 | Industrial-standard, RFC-compliant cross-platform SMTP client for asynchronous transactional email delivery. |
| **Testing Framework** | xUnit | 2.9 | Modern test framework for isolated unit testing, data-driven tests (`[Theory]`), and multi-threaded concurrency simulations. |
| **Mocking & Assertions** | Moq & FluentAssertions | Latest | Fluent assertions for readable unit tests; interface mocking for `IEmailService`, `ISmsService`, and repository abstractions. |
| **Code Coverage** | Coverlet | 6.0 | Cross-platform code coverage library integrated into `dotnet test` to audit test coverage against the ≥60% KPI target. |
| **Continuous Integration** | GitHub Actions | Standard | Cloud-based CI pipeline automating build validation, linter checks, and unit test execution on every pull request. |
| **Primary Cloud Host** | Microsoft Azure App Service | Linux/Windows B1/F1 | Fully managed platform-as-a-service (PaaS) providing zero-configuration deployment, HTTPS, and environment configuration. |
| **Database Cloud Host** | Azure SQL Database | Serverless / Basic | Managed relational cloud database supporting dynamic auto-pause, automated backups, and filtered index execution. |
| **Fallback Cloud Host** | MonsterASP.NET / SmarterASP | Free Student Tier | Cost-effective shared Windows/.NET hosting fallback if cloud credits expire during evaluation. |

---

## 2. Selection Rationale by System Tier

### 2.1 Backend Architecture (.NET 8 + ASP.NET Core MVC)
ASP.NET Core MVC was chosen over a decoupled SPA (e.g. React/Angular) to minimize framework bloat and eliminate client-side state synchronization overhead. Server-side rendering guarantees that HTML views, Identity cookie authentication, and Anti-CSRF tokens remain unified under a single, cohesive request pipeline.

### 2.2 Relational Data Tier (SQL Server & EF Core 8)
Healthcare scheduling demands absolute transactional consistency. Non-relational (NoSQL) stores rely on eventual consistency, which introduces unacceptable race conditions when booking shared doctor slots. SQL Server’s support for **Filtered Unique Indexes** (`WHERE [Status] <> 3 AND [Status] <> 4`) provides the exact mechanism required to prevent double-booking without blocking re-booking of cancelled slots.

### 2.3 Real-Time Communications (ASP.NET Core SignalR)
SignalR eliminates polling overhead. By utilizing strongly-typed hubs (`IAppointmentNotificationClient`), the backend triggers instant client-side UI refreshes and toast notifications whenever appointments are scheduled, confirmed, or cancelled.

### 2.4 Business Validation (FluentValidation)
Standard MVC `[DataAnnotation]` attributes quickly clutter view models when business rules involve multi-property logic (e.g., "Doctor leave `EndDate` must be greater than or equal to `StartDate`"). FluentValidation centralizes these business invariants within `MediCare.Services` where they can be thoroughly unit-tested in isolation.
