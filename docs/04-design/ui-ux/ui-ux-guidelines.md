# UI/UX Style & Design Guidelines: MediCare

## 1. Design Principles
1. **Clinical Clarity & Minimal Cognitive Load:** Medical applications require rapid, error-free comprehension. Data layouts prioritize high legibility, clean visual hierarchies, and clear status indicators.
2. **Deterministic Feedback:** Every asynchronous operation (slot booking, status updates, file uploads) provides immediate, unambiguous user feedback via badges, disabled state spinners, or real-time toast alerts.
3. **Responsive Mobile-First Architecture:** All interfaces are designed to operate seamlessly across mobile phone viewports (360px), tablets (768px), and clinical desktop monitors (1920px).

---

## 2. Color Palette & Contrast Compliance

The color scheme utilizes a modern medical healthcare palette designed to meet **WCAG 2.1 AA standards** (minimum contrast ratio of 4.5:1 against light backgrounds for body text).

| Role | Color Name | Hex Code | Contrast vs White (`#FFFFFF`) | Usage Context |
|---|---|:---:|:---:|---|
| **Primary Brand** | Medical Teal | `#0D6EFD` | 4.6 : 1 (Pass AA) | Primary navigation, active calendar highlights, primary CTAs |
| **Secondary Accent**| Slate Blue | `#4A607A` | 5.8 : 1 (Pass AA) | Subheadings, secondary badges, table column headers |
| **Success State** | Clinical Emerald | `#198754` | 4.5 : 1 (Pass AA) | `Confirmed` & `Completed` badges, successful booking toasts |
| **Warning State** | Warm Amber | `#FFC107` | 1.2 : 1 (Requires dark text) | `Pending` status badges (used with `#212529` text) |
| **Danger State** | Crimson Alert | `#DC3545` | 4.8 : 1 (Pass AA) | `Cancelled` & `Rejected` badges, delete/abort confirmations |
| **Neutral Dark** | Charcoal Body | `#212529` | 15.3 : 1 (Pass AAA) | Primary headings, table text, body typography |
| **Neutral Muted** | Slate Gray | `#6C757D` | 4.5 : 1 (Pass AA) | Captions, timestamps, disabled placeholder inputs |
| **Surface Background**| Off-White | `#F8F9FA` | 1.1 : 1 | Main page body background |
| **Card Surface** | Pure White | `#FFFFFF` | N/A | Cards, modals, data tables, printable prescriptions |

---

## 3. Typography & Hierarchy

MediCare utilizes standard system font stacks optimized for performance without external web-font download overhead:

```css
font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
```

| Element | Font Size | Weight | Line Height | Usage |
|---|:---:|:---:|:---:|---|
| **Display H1** | 2.25 rem (36px) | 700 (Bold) | 1.2 | Landing page hero, primary dashboard titles |
| **Section H2** | 1.75 rem (28px) | 600 (Semi-bold) | 1.3 | Portal section headings, calendar headers |
| **Card H3** | 1.25 rem (20px) | 600 (Semi-bold) | 1.4 | Doctor card titles, modal headings, report panels |
| **Body Text** | 1.00 rem (16px) | 400 (Regular) | 1.5 | General form labels, clinical notes, table text |
| **Small / Meta**| 0.875 rem (14px) | 400 (Regular) | 1.4 | Timestamps, specialty tags, helper instructions |
| **Badge / Label**| 0.75 rem (12px) | 700 (Bold) | 1.0 | Status pills (`Confirmed`, `Pending`, `NoShow`) |

---

## 4. Spacing System & Grid Layout

* **Grid System:** Standard **Bootstrap 5 12-column flexbox grid** with responsive breakpoints:
  * `sm` (≥ 576px), `md` (≥ 768px), `lg` (≥ 992px), `xl` (≥ 1200px).
* **Spacing Scale:** Standard 8px increment scale:
  * `$spacer-1` = 0.25rem (4px)
  * `$spacer-2` = 0.50rem (8px)
  * `$spacer-3` = 1.00rem (16px) — Default component padding
  * `$spacer-4` = 1.50rem (24px) — Card and column margins
  * `$spacer-5` = 3.00rem (48px) — Section vertical spacing

---

## 5. Core UI Component Specifications

### 5.1 Status Badges
Status indicators use rounded pills (`rounded-pill`) with explicit text labels:
* `Pending`: `<span class="badge bg-warning text-dark">Pending</span>`
* `Confirmed`: `<span class="badge bg-success">Confirmed</span>`
* `Completed`: `<span class="badge bg-primary">Completed</span>`
* `Cancelled`: `<span class="badge bg-danger">Cancelled</span>`
* `Rejected`: `<span class="badge bg-secondary">Rejected</span>`
* `NoShow`: `<span class="badge bg-dark">No-Show</span>`

### 5.2 Interactive Time Slot Buttons
* **Available Slot:** Outline primary button (`btn btn-outline-primary btn-sm`), transitions to solid fill upon click.
* **Selected Slot:** Solid primary with checkmark icon (`btn btn-primary btn-sm`).
* **Reserved Slot:** Disabled muted button with line-through (`btn btn-light disabled`).

### 5.3 Modals & Dialogues
* Modals (e.g. Booking Confirmation, Cancellation Warning) utilize semi-transparent backdrops with focus locked inside the dialogue.
* Destructive actions (e.g. Cancellation) require explicit two-step confirmation.

### 5.4 Real-Time Toast Notifications (SignalR)
* Positioned fixed in the bottom-right viewport (`position-fixed bottom-0 end-0 p-3`).
* Features an auto-dismiss timeout of 6 seconds, a close button, and an alert sound hook.

---

## 6. Internationalization & Text Direction (RTL / LTR)

* **Primary Implementation:** English-first interface structured in standard **Left-to-Right (LTR)** layout (`dir="ltr"`).
* **Localization Readiness:** To support future Arabic localization, markup adheres to Bootstrap 5 logical properties (`ms-*` / `me-*` instead of `ml-*` / `mr-*`, `text-start` instead of `text-left`).
* No bidirectional overrides or mixed-direction text inputs are used in the primary release.

---

## 7. Accessibility Considerations (WCAG 2.1 AA)

1. **Keyboard Navigability:** All interactive components (slot buttons, calendar date cells, modals, table actions) are focusable via `Tab` key and activatable via `Enter` or `Space`.
2. **Form Accessibility:** Every form input has an explicitly associated `<label for="...">`. Error states display red outline borders and accessible validation spans (`<span class="text-danger">`).
3. **ARIA Semantic Attributes:**
   * Dynamic SignalR toast alerts include `role="alert"` and `aria-live="polite"`.
   * Modals include `aria-labelledby` and `aria-hidden`.
   * FullCalendar grid elements maintain semantic `aria-label` tags describing slot date and time.
4. **Print Optimization (`@media print`):**
   * Prescriptions hide navigation bars, search inputs, buttons, and footers (`d-print-none`).
   * Body margins set to 0.5 inches with high-contrast monochrome rendering for standard clinical paper printers.
