# MediCare directory data and asset audit

**Checked:** 2026-10-16
**Branch / baseline:** `feature/free-demo-readiness` / `d1c45998f0cc100a6b9b548dc3a49f6e97430948`
**Scope:** source-code and repository asset audit. No application database was queried or changed.

## Findings

- The development seeder contains **81 hardcoded doctor cohort entries**. The code gates demo doctors, patients, and appointments to Development. The list is synthetic demo material, not a verified public directory. No count of records in a running database was taken; some entries overlap earlier seed entries.
- The cohort references 14 specialty lookup variables, but those are demo taxonomy assignments and are not evidence of real practitioners. No source metadata or provider-verification state is stored on the `Doctor` record. The current governorate column can be a default or user-entered value and is not proof of a clinic location.
- **Verified public doctor records imported: 0. Verified facilities: 0. Verified governorates: 0.** No existing seed identity was matched to an authoritative source and imported. The official [Cleopatra Hospitals Group booking directory](https://www.cleopatrahospitals.com/en/booking/?doctors=0&location=1&specialty=0) was checked on 2026-10-16. It is a Cairo Specialized Hospital listing; this project has no source link/provenance field or approved photo reuse terms, so its profiles and images were not copied.
- Current code had generated ratings/review counts, fabricated patient names/comments, qualifications, years of experience, insurance acceptance/discounts, and clinic addresses. Reviews were also added only to browser `localStorage`, then labelled verified. Those claims and the local-only submission UI have been removed from doctor-facing pages/service output. There is no server-side `Review` entity/workflow in the inspected schema. **Authentic reviews: 0; synthetic reviews shown: 0.** A legitimate appointment-linked review/moderation workflow remains unimplemented. Existing browser `localStorage` keys were not inspected or cleared; the UI no longer reads/displays them.
- Current price values and bios may be entered or seeded values. Views now label them as unverified account data and do not invent missing bios/prices. Clinic address, qualifications, license number, and contact email are not inferred from bio or exposed as verified directory facts. Existing appointments and database records were not deleted or edited. **Unverified legacy database records removed: 0; hidden in the database: 0; classified in place: 0**, because no database was inspected.
- Found **93 image files** under `wwwroot/images`. The 81 `doctors/doc-*.jpg` assets referenced by development seed profiles have no source/license manifest. All 81 expected filenames exist and are non-zero length; image decoding, correct person mapping, and reuse rights were not verified. They are no longer used for public doctor profiles; those pages use the neutral `doctor-default.svg`. No verified individual photo has been established. Other UI images also lack provenance records.

## Data classification for this snapshot

| Classification | Current treatment |
|---|---|
| `VerifiedPublicDirectoryData` | Zero records. The schema currently has no provenance fields. |
| `ApplicationUserGeneratedData` | Account/profile submissions are shown as unverified. |
| `SyntheticDemoData` | Seed doctor cohorts, schedules, prices, accounts, and mock appointment data in Development. |
| `UnverifiedLegacyData` | Existing records without source/verification evidence remain untouched and are not promoted as verified directory data. |

The classification is documented, not persisted as a new database enum; adding durable per-record provenance requires a reviewed migration/backfill policy. No real patient data was added, queried, or modified.

## Images and import process

`medical-image-provenance.csv` inventories the repository image files, their known seed references, attribution status, and current display handling. Unknown attribution is explicitly unresolved. The public doctor directory uses a neutral placeholder until a specific image-to-person mapping and reuse permission can be established.

No real-directory import was performed. Before import, collect official source URLs, verification dates, field-level evidence, and photo permissions; validate records in a staging file; reject incomplete or duplicate entries; then obtain human review before applying an idempotent importer. This repository does not yet implement that importer or source-metadata schema.

## Coverage and remaining work

- Verified doctors/facilities/governorates: **0 / 0 / 0**.
- Distinct demo specialty assignments in the extended seed cohort: **14**; no verified directory count is claimed.
- Seed geography, current prices, hours, biographies, and license strings need record-level review. The code-based audit does not assert every seed detail is false; it asserts that its public evidence/provenance is absent.
- Browser visual verification and live source-to-record matching were not performed. The test and build results are recorded in `docs/05-testing/test-execution-report.md`.
- No clinical or regulatory certification is claimed.

### Verification on this audit's final working tree — October 16, 2026

- `git diff --check` passed (line-ending notices only).
- Release solution build passed with zero warnings and errors after the current application/test-source edits.
- The full-solution `dotnet test` command exited 1 without output. A targeted doctor service run aborted before test discovery because VSTest could not connect to testhost after 90 seconds; its TRX records 0 total / 0 executed. SQL Server integration and browser flows did not run. Earlier test evidence in the historical sections does not cover this directory remediation.
