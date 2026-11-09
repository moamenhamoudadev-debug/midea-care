# Background Review and Design Rationale

## Purpose and scope

This review looks at outpatient appointment scheduling and online access to clinic services. It uses published reviews to identify design concerns, then compares MediCare with two public scheduling services at a feature level. It is not a clinical effectiveness study, a market survey, or a claim that MediCare is equivalent to a commercial healthcare platform.

## What the literature suggests

Outpatient scheduling is a service design problem: clinics need to match patient demand with provider capacity while accounting for uncertain arrival and consultation times, patient preferences, and available information. Reviews by Cayirli and Veral and by Gupta and Denton describe these factors and caution that scheduling policies depend on the clinic context; a single appointment rule does not fit every service [1, 2].

Online booking can give patients more control over when they request care, but it does not by itself solve every operational problem. A 2017 systematic review of web-based medical appointment systems found reported benefits such as lower waiting time and fewer missed visits in some settings, while also identifying implementation barriers [3]. A newer review of electronic patient-reported measures found that such measures may support scheduling decisions for some chronic-care populations; MediCare does not collect or use those measures [4].

These findings informed two bounded design choices in MediCare. Availability is calculated from a doctor's working hours, leave, appointment duration, and existing appointments. The database also enforces a filtered unique index on the doctor, date, and start time for rows that are not cancelled or rejected. The service-level availability check gives a useful response to the patient; the database index remains the final protection against two requests racing for the same slot. SQL Server documents unique indexes as a way to enforce uniqueness and filtered indexes as indexes over a selected subset of rows [5].

## Feature comparison

The comparison below describes public product features; it does not compare service quality, safety, pricing plans, or clinical outcomes.

| Capability | General scheduling service | Healthcare booking marketplace | MediCare project |
|---|---|---|---|
| Publicly visible scheduling model | Calendly describes availability using schedules, holidays, and connected calendars [6]. | Vezeeta exposes doctor search by specialty and area, with appointment booking [7]. | Doctor search uses specialty and Egyptian governorate filters; available slots are generated from clinic schedules. |
| Clinic workflow | General scheduling and calendar coordination. | Patient-facing provider discovery and booking. | Patient booking is connected to doctor approval, visit documentation, patient history, and prescriptions. |
| Booking consistency | Depends on the connected service configuration and calendar state. | Internal implementation details are not inferred from public pages. | A SQL Server filtered unique index prevents duplicate active bookings for the same doctor, date, and start time. |
| Scope in this project | Not applicable. | Not applicable. | Graduation-project prototype. Payments are simulated; external clinical, insurance, and payment integrations are outside the implemented scope. |

## Design implications and limits

The literature supports treating availability, booking, and follow-up as related workflow concerns. MediCare reflects that by keeping scheduling rules in the service layer and storing visit records and prescription items in related database entities. The database constraint protects one specific invariant: two non-cancelled, non-rejected appointments cannot share the same doctor's slot key. It does not prove that clinic schedules are clinically appropriate or that the application is ready for production use.

MediCare's clinic guide can use the OpenAI Responses API to suggest a broad specialty, with a local keyword-based fallback. It is not a diagnostic or triage service, and emergency-keyword matching is limited; it must not be relied on to identify or rule out emergencies. The interface discloses the external provider and warns against entering real health or identity information.

## Project review and next improvements

The strongest implementation choices are the explicit appointment state rules, ownership checks around patient records, and database-enforced slot uniqueness. The main work before a live clinic pilot would be a real payment provider, a formal privacy and retention review, and operational deployment with monitored backups and alerting. These are future requirements, not completed features.

### Supervisor feedback

No supervisor or lecturer feedback was included in the supplied project materials. Add the official comments here after the review; do not present this project team's assessment as lecturer feedback.

### Grading criteria

The supplied DEPI guideline lists the required project deliverables but does not provide a mark breakdown. Insert the official rubric if DEPI supplies one; no weighting is assumed in this document.

## References

1. Cayirli, T., & Veral, E. (2003). “Outpatient Scheduling in Health Care: A Review of Literature.” *Production and Operations Management*, 12(4), 519–549. https://doi.org/10.1111/j.1937-5956.2003.tb00218.x
2. Gupta, D., & Denton, B. (2008). “Appointment Scheduling in Health Care: Challenges and Opportunities.” *IIE Transactions*, 40(9), 800–819. https://doi.org/10.1080/07408170802165880
3. Zhao, P., et al. (2017). “Web-Based Medical Appointment Systems: A Systematic Review.” *Journal of Medical Internet Research*, 19(4), e134. https://doi.org/10.2196/jmir.6747
4. He, C., et al. (2025). “Electronic patient-reported outcome measures for triaging and scheduling outpatient appointments: a systematic review and meta-analysis.” *Journal of the American Medical Informatics Association*, 32(7), 1219–1226. https://doi.org/10.1093/jamia/ocaf078
5. Microsoft. “Create a unique index.” SQL Server documentation. https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-unique-indexes
6. Calendly Help Center. “Availability overview.” https://calendly.com/help/availability-overview (accessed 11 October 2026).
7. Vezeeta. Doctor search and booking pages. https://www.vezeeta.com/en (accessed 11 October 2026).
