# Deployment & Component Diagrams: MediCare

This document specifies the structural composition of MediCare's software components and their physical distribution across runtime hardware and cloud environments.

---

## 1. High-Level Component Diagram

The Component Diagram details the internal modular structure of MediCare, the boundaries of its 3-tier projects, and their dependencies on external frameworks and third-party systems.

```mermaid
flowchart TD
    subgraph UI_Tier["Presentation Tier (MediCare.Web)"]
        MVC["MVC Controllers<br/>(Account, Doctors, Appointments,<br/>MedicalRecords, Admin)"]
        Razor["Razor View Engine<br/>(Bootstrap 5, CSS Print Views)"]
        FullCal["FullCalendar.js Client<br/>(Slot Picker Widget)"]
        Charts["Chart.js Analytics Client<br/>(Admin Visualizations)"]
        Hub["AppointmentHub<br/>(SignalR WebSocket Endpoint)"]
        Filters["Action Filters<br/>(IDOR Ownership, Anti-CSRF)"]
        Middleware["Global Exception<br/>Handling Middleware"]
    end

    subgraph Service_Tier["Business Logic Tier (MediCare.Services)"]
        SlotEng["SlotEngineService<br/>(Dynamic 30-min Slot Calculator)"]
        ApptSvc["AppointmentService<br/>(State Transitions & Concurrency Pre-check)"]
        RecSvc["MedicalRecordService<br/>(Consultation Notes & Attachments)"]
        PrescSvc["PrescriptionService<br/>(Medication Item Repeater)"]
        DocSvc["DoctorService<br/>(Hours & Leave Management)"]
        Validators["FluentValidation Rules<br/>(Booking, Leaves, Hours)"]
        Factories["Pattern Factories<br/>(AppointmentFactory, NotificationFactory)"]
        EmailGw["EmailService (MailKit)<br/>(SMTP Wrapper)"]
        MockSms["MockSmsService<br/>(SMS Gateway Stub)"]
    end

    subgraph Data_Tier["Data Access Tier (MediCare.Data)"]
        UoW["Unit of Work (IUnitOfWork)<br/>(Transaction Coordinator)"]
        Repos["Repositories (IRepository~T~,<br/>IAppointmentRepo, IDoctorRepo)"]
        DbContext["ApplicationDbContext<br/>(EF Core 8 Context)"]
        Entities["Domain Entity Models<br/>(Appointments, Records, Leaves)"]
        Configs["Fluent API Configurations<br/>(Filtered Unique Indexes)"]
        DbSeed["DbInitializer<br/>(Automated Data Seeder)"]
    end

    subgraph External_Systems["External Infrastructure & Cloud Gateways"]
        SQLDB[("Microsoft SQL Server / Azure SQL<br/>(Relational Tables & Indexes)")]
        DiskStorage[("File System (App_Data/uploads/records)<br/>(Diagnostic Scans & PDFs)")]
        SMTPRelay["External SMTP Relay Server<br/>(Mailtrap / SendGrid / Gmail)"]
        ClientBrowser["Client Web Browser<br/>(Desktop & Mobile Devices)"]
    end

    %% Client Interactions
    ClientBrowser <-->|"HTTPS (HTML, CSS, JS, JSON)"| MVC
    ClientBrowser <-->|"WSS (WebSocket Real-Time Push)"| Hub
    MVC --> Razor
    Razor --> FullCal
    Razor --> Charts
    MVC --> Filters
    MVC --> Middleware

    %% Web -> Services
    MVC -->|"Calls Service Interfaces"| SlotEng
    MVC -->|"Calls Service Interfaces"| ApptSvc
    MVC -->|"Calls Service Interfaces"| RecSvc
    MVC -->|"Calls Service Interfaces"| PrescSvc
    MVC -->|"Calls Service Interfaces"| DocSvc

    %% Services Internal
    ApptSvc --> SlotEng
    ApptSvc --> Validators
    ApptSvc --> Factories
    ApptSvc --> EmailGw
    ApptSvc --> MockSms
    RecSvc --> PrescSvc

    %% Services -> Data
    SlotEng --> UoW
    ApptSvc --> UoW
    RecSvc --> UoW
    DocSvc --> UoW
    UoW --> Repos
    Repos --> DbContext
    DbContext --> Entities
    DbContext --> Configs
    DbSeed --> DbContext

    %% Data & Services -> External
    DbContext <-->|"T-SQL / TCP Port 1433"| SQLDB
    RecSvc -->|"Saves File Stream"| DiskStorage
    EmailGw -->|"SMTP / TLS Port 587"| SMTPRelay
```

---

## 2. Cloud Deployment Diagram

The deployment diagram shows the proposed Azure topology. It is a target design, not evidence that the application is currently deployed or that each listed Azure service has been provisioned.

```mermaid
flowchart TD
    subgraph ClientTier["Client Tier (End-User Devices)"]
        nodeBrowser["Modern Web Browser<br/>(Chrome, Firefox, Safari, Edge)<br/>- HTML5 / CSS3 / JavaScript<br/>- FullCalendar.js, Chart.js<br/>- SignalR WebSocket Client"]
    end

    subgraph AzureCloud["Microsoft Azure Cloud (West Europe / East US)"]
        subgraph AppServicePlan["Azure App Service Plan (B1 Basic / F1 Free Tier)"]
            subgraph WebApp["MediCare Web Application Container"]
                AppRuntime[".NET 8.0 ASP.NET Core Runtime<br/>- Kestrel Web Server<br/>- In-Process SignalR Hub<br/>- MediCare.Web.dll<br/>- MediCare.Services.dll<br/>- MediCare.Data.dll"]
                LocalDisk[("App Service Virtual Storage<br/>App_Data/uploads/records/<br/>- Diagnostic Scans (.jpg, .png)<br/>- Medical Reports (.pdf)")]
            end
        end

        subgraph AzureDataTier["Azure Managed Data Services"]
            AzureSQL[("Azure SQL Database<br/>(Serverless / Basic 5 DTU)<br/>- Relational Tables<br/>- Filtered Unique Index<br/>- Automated Point-in-Time Backups")]
        end
    end

    subgraph ExternalServices["External Communication Relays"]
        SMTPServer["External SMTP Service<br/>(Mailtrap / Gmail SMTP / SendGrid)<br/>- Secure TLS Transmission<br/>- Port 587 / 465"]
    end

    %% Network Connections
    nodeBrowser <-->|"HTTPS (Port 443)<br/>TLS 1.3 Encrypted"| AppRuntime
    nodeBrowser <-->|"WSS / WebSockets (Port 443)<br/>Real-Time SignalR Stream"| AppRuntime
    AppRuntime <-->|"Local File I/O Stream"| LocalDisk
    AppRuntime <-->|"T-SQL over Encrypted TDS (Port 1433)<br/>Azure Connection String"| AzureSQL
    AppRuntime -->|"SMTP / TLS Encrypted (Port 587)<br/>MailKit Transmission"| SMTPServer
```

---

## 3. Network & Security Boundaries

1. **Client-to-Application Encryption:**  
   All HTTP traffic is automatically upgraded to **HTTPS (TLS 1.3)** via Azure App Service URL redirection. Cleartext HTTP requests on port 80 are permanently disabled.
2. **Database Isolation:**  
   Azure SQL Database enforces firewall rules restricting incoming connections exclusively to Azure App Service instances (`Allow Azure services and resources to access this server = YES`) and authorized administrator IP addresses. Port 1433 is shielded from the public internet.
3. **Application Secret Isolation:**  
   Database connection strings, SMTP passwords, and Identity security tokens are injected at runtime via Azure App Service **Application Settings (Environment Variables)** and are never written to physical disk or configuration files.

---

## 4. Deployment Status and Required Settings

The repository does **not** currently have a verified live MediCare web application. The latest public Azure workflow run on October 10, 2026 built and published the app, but the Azure deployment action reported that no Azure credentials were available. The expected fallback hostname `medicare-depi-app.azurewebsites.net` did not resolve when checked.

The Azure workflow is manual-only and now fails early with a clear message unless both GitHub settings exist:

- Repository variable `AZURE_WEBAPP_NAME`: the name of an existing Azure App Service.
- Repository secret `AZURE_WEBAPP_PUBLISH_PROFILE`: that App Service's publish profile.

The app also needs production App Service settings for `ConnectionStrings__DefaultConnection`, `Seed__AdminPassword`, and any required mail or AI settings. Configure the Azure SQL database, migrations, storage, and health endpoint before presenting a public URL. A GitHub Pages build serves static files only; it cannot host this ASP.NET Core MVC application.
