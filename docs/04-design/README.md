# Phase 2: System Analysis & Design Documentation Index

This directory contains the complete system analysis, architectural modeling, database specifications, behavioral UML diagrams, user interface wireframes, and deployment design for **MediCare — Clinic Management & Appointment System**.

All deliverables comply with the **DEPI System Analysis & Design (6 Nov 2026)** requirements.

---

## Documentation Deliverables Catalog

| # | Topic / Deliverable | Document Link | Description |
|---|---|---|---|
| **01** | **Problem Statement & Objectives** | [`problem-statement-and-objectives.md`](./problem-statement-and-objectives.md) | Clinical operational challenges, 6 core design goals, scope boundaries, and architectural constraints. |
| **02** | **Use Cases & Narratives** | [`use-case-diagram-and-descriptions.md`](./use-case-diagram-and-descriptions.md) | Mermaid use case model across Patient, Doctor, Admin, and System Engine; detailed flows for UC-01 through UC-18. |
| **03** | **Software Architecture** | [`software-architecture.md`](./software-architecture.md) | 3-Project N-Tier layer decoupling (`Web` -> `Services` -> `Data`), solution folder layout, request flows, and design patterns. |
| **04** | **Database ERD** | [`database/er-diagram.md`](./database/er-diagram.md) | Mermaid entity-relationship diagram covering domain entities, Identity links, cardinalities, and relational keys. |
| **05** | **Database Schema & Constraints** | [`database/logical-and-physical-schema.md`](./database/logical-and-physical-schema.md) | Data dictionary, SQL Server data types, filtered unique index DDL, 3NF/BCNF normalization analysis, and seed data plan. |
| **06** | **Data Flow Diagrams (DFD)** | [`data-flow/dfd-context-and-level-1.md`](./data-flow/dfd-context-and-level-1.md) | Context-level DFD (Level 0), Level 1 subsystem decomposition, and Level 2 detailed booking engine data flow. |
| **07** | **Sequence Diagrams** | [`behavior/sequence-diagrams.md`](./behavior/sequence-diagrams.md) | Dynamic component interactions for Auth, Slot Search, Concurrency Booking, Confirm/Reject, Cancellation, Consultation, and Notifications. |
| **08** | **Activity Diagrams** | [`behavior/activity-diagrams.md`](./behavior/activity-diagrams.md) | Procedural workflows for appointment booking, clinical consultation, and doctor vacation/leave handling. |
| **09** | **State Machine Diagrams** | [`behavior/state-diagram.md`](./behavior/state-diagram.md) | Appointment lifecycle states (`Pending` -> `Confirmed` -> `Completed`/`Cancelled`/`NoShow`) and doctor account approval state machine. |
| **10** | **UML Class Diagram** | [`behavior/class-diagram.md`](./behavior/class-diagram.md) | Object-oriented class models for entities, repositories, Unit of Work, services, factories, and typed SignalR hub interfaces. |
| **11** | **Wireframe Specifications** | [`ui-ux/wireframes-spec.md`](./ui-ux/wireframes-spec.md) | Screen-by-screen functional layout specifications, page flow sitemap, and ASCII wireframes for Figma design. |
| **12** | **UI/UX Design Guidelines** | [`ui-ux/ui-ux-guidelines.md`](./ui-ux/ui-ux-guidelines.md) | Design principles, WCAG 2.1 AA accessible color palette, typography, Bootstrap 5 spacing, status badges, and print CSS styles. |
| **13** | **Technology Stack** | [`deployment/technology-stack.md`](./deployment/technology-stack.md) | Complete framework, library, and tool inventory (.NET 8, EF Core, SQL Server, SignalR, Bootstrap 5) with technical selection rationale. |
| **14** | **Deployment & Component Diagrams** | [`deployment/deployment-and-component-diagrams.md`](./deployment/deployment-and-component-diagrams.md) | High-level component diagram and cloud deployment topology mapping browser clients, Azure App Service, Azure SQL, and SMTP. |
| **15** | **Deployment & DevOps Strategy** | [`deployment/deployment-strategy.md`](./deployment/deployment-strategy.md) | Environment configuration, user-secrets, GitHub Actions CI pipeline, Azure hosting tiers, fallback hosting, and backup policies. |
| **16** | **Internal API Documentation** | [`api/api-documentation.md`](./api/api-documentation.md)<br>[`api/openapi.yaml`](./api/openapi.yaml) | JSON endpoint contracts for FullCalendar slot feeds, conflict pre-checks, and SignalR notification sync, with an OpenAPI 3.0 specification. |
| **17** | **Testing & Validation Plan** | [`testing/testing-and-validation-plan.md`](./testing/testing-and-validation-plan.md) | Testing pyramid, xUnit tooling, 10-thread concurrency test design, and traceability matrix mapping requirements to TC-01..TC-24. |
