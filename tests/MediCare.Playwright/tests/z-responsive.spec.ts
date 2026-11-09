import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test, expect, reserveLoginPost } from '../support/fixtures';

const VIEWPORTS = [
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 },
  { name: 'small-mobile', width: 320, height: 720 },
] as const;

type ViewportDef = typeof VIEWPORTS[number];

const screenshotDir = resolve(__dirname, '../test-results/responsive-screenshots');
mkdirSync(screenshotDir, { recursive: true });

const sharedStatePath = resolve(__dirname, '../test-results/stage4-run-state.json');
const adminStoragePath = resolve(screenshotDir, 'admin-session-state.json');

function getPersistedState(): Record<string, any> {
  if (existsSync(sharedStatePath)) {
    try {
      return JSON.parse(readFileSync(sharedStatePath, 'utf8'));
    } catch {
      return {};
    }
  }
  return {};
}

async function restoreCookiesFromState(page: import('@playwright/test').Page, cookiePath?: string): Promise<boolean> {
  if (!cookiePath || !existsSync(cookiePath)) return false;
  try {
    const cookies = JSON.parse(readFileSync(cookiePath, 'utf8')).cookies;
    await page.context().clearCookies();
    await page.context().addCookies(cookies);
    return true;
  } catch {
    return false;
  }
}

interface OverflowCheckResult {
  winWidth: number;
  docScrollWidth: number;
  bodyScrollWidth: number;
  hasOverflow: boolean;
  overflowAmount: number;
  badElements: Array<{
    tag: string;
    id: string;
    className: string;
    right: number;
    left: number;
    width: number;
    selector: string;
  }>;
}

async function checkOverflow(page: import('@playwright/test').Page): Promise<OverflowCheckResult> {
  return await page.evaluate(() => {
    const doc = document.documentElement;
    const winWidth = window.innerWidth;
    const docScrollWidth = doc.scrollWidth;
    const bodyScrollWidth = document.body ? document.body.scrollWidth : 0;
    const maxScroll = Math.max(docScrollWidth, bodyScrollWidth);
    const hasOverflow = maxScroll > winWidth + 1; // 1px tolerance for subpixel rounding

    const badElements: Array<{
      tag: string;
      id: string;
      className: string;
      right: number;
      left: number;
      width: number;
      selector: string;
    }> = [];

    if (hasOverflow) {
      const all = document.querySelectorAll('*');
      for (const el of all) {
        if (el === document.documentElement || el === document.body) continue;
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) continue;
        const style = window.getComputedStyle(el);
        if (style.display === 'none' || style.visibility === 'hidden') continue;

        if (rect.right > winWidth + 1 || rect.left < -1) {
          let sel = el.tagName.toLowerCase();
          if (el.id) sel += '#' + el.id;
          else if (el.className && typeof el.className === 'string') {
            sel += '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.');
          }
          badElements.push({
            tag: el.tagName,
            id: el.id,
            className: typeof el.className === 'string' ? el.className.slice(0, 80) : '',
            right: Math.round(rect.right),
            left: Math.round(rect.left),
            width: Math.round(rect.width),
            selector: sel,
          });
          if (badElements.length >= 6) break;
        }
      }
    }

    return {
      winWidth,
      docScrollWidth,
      bodyScrollWidth,
      hasOverflow,
      overflowAmount: maxScroll - winWidth,
      badElements,
    };
  });
}

async function setLanguage(page: import('@playwright/test').Page, lang: 'en' | 'ar') {
  await page.evaluate((l) => {
    localStorage.setItem('medicare-lang', l);
    document.documentElement.setAttribute('lang', l);
    document.documentElement.setAttribute('dir', l === 'ar' ? 'rtl' : 'ltr');
  }, lang);
}

// --------------------------------------------------------------------------
// 1. PUBLIC PAGES AUDIT (Home, Doctors, Login, Register, Verify)
// --------------------------------------------------------------------------
test.describe('Responsive Public Pages Audit', () => {
  const publicPages = [
    { name: 'home', path: '/' },
    { name: 'doctors-catalog', path: '/Doctors' },
    { name: 'login', path: '/Account/Login' },
    { name: 'register-patient', path: '/Account/RegisterPatient' },
    { name: 'register-doctor', path: '/Account/RegisterDoctor' },
    { name: 'prescription-verify', path: '/Prescriptions/Verify' },
  ];

  for (const vp of VIEWPORTS) {
    test(`Public pages across viewport ${vp.name} (${vp.width}x${vp.height})`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });

      for (const p of publicPages) {
        // Test LTR
        await page.goto(p.path);
        await setLanguage(page, 'en');
        await page.reload();
        await page.waitForLoadState('domcontentloaded');

        const ltrResult = await checkOverflow(page);
        if (ltrResult.hasOverflow) {
          console.warn(`[OVERFLOW_WARNING] ${p.name} at ${vp.name} LTR has overflow of ${ltrResult.overflowAmount}px. Elements:`, ltrResult.badElements);
        }
        await page.screenshot({
          path: resolve(screenshotDir, `${p.name}-${vp.name}-ltr.png`),
          fullPage: false,
        });

        // Test RTL (Arabic)
        await setLanguage(page, 'ar');
        await page.reload();
        await page.waitForLoadState('domcontentloaded');

        const rtlResult = await checkOverflow(page);
        if (rtlResult.hasOverflow) {
          console.warn(`[OVERFLOW_WARNING] ${p.name} at ${vp.name} RTL has overflow of ${rtlResult.overflowAmount}px. Elements:`, rtlResult.badElements);
        }
        await page.screenshot({
          path: resolve(screenshotDir, `${p.name}-${vp.name}-rtl.png`),
          fullPage: false,
        });

        // Reset to English
        await setLanguage(page, 'en');

        // Verify no horizontal overflow (tolerance <= 2px for browser subpixel margins)
        expect(ltrResult.hasOverflow && ltrResult.overflowAmount > 2, `${p.name} LTR at ${vp.name} (${vp.width}px) has horizontal overflow of ${ltrResult.overflowAmount}px`).toBeFalsy();
        expect(rtlResult.hasOverflow && rtlResult.overflowAmount > 2, `${p.name} RTL at ${vp.name} (${vp.width}px) has horizontal overflow of ${rtlResult.overflowAmount}px`).toBeFalsy();
      }

      // Test mobile navigation toggler at mobile / small-mobile
      if (vp.width <= 768) {
        await page.goto('/');
        const navToggler = page.locator('.navbar-toggler');
        await expect(navToggler).toBeVisible();
        await navToggler.click();
        const navCollapse = page.locator('#mainNavbar');
        await expect(navCollapse).toHaveClass(/show/);
        const navOverflow = await checkOverflow(page);
        expect(navOverflow.hasOverflow && navOverflow.overflowAmount > 2, `Mobile navigation menu overflowed by ${navOverflow.overflowAmount}px at ${vp.name}`).toBeFalsy();
      }
    });
  }
});

// --------------------------------------------------------------------------
// 2. ADMIN PAGES AUDIT
// --------------------------------------------------------------------------
test.describe('Responsive Admin Pages Audit', () => {
  const adminPages = [
    { name: 'admin-dashboard', path: '/Admin' },
    { name: 'admin-approvals', path: '/Admin/Approvals' },
    { name: 'admin-patients', path: '/Admin/Patients' },
    { name: 'admin-pharmacists', path: '/Admin/Pharmacists' },
    { name: 'admin-reports', path: '/Admin/Reports' },
    { name: 'admin-specializations', path: '/Admin/Specializations' },
  ];

  for (const vp of VIEWPORTS) {
    test(`Admin dashboard and management tables at ${vp.name} (${vp.width}x${vp.height})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });

      let loggedIn = false;
      if (existsSync(adminStoragePath)) {
        loggedIn = await restoreCookiesFromState(page, adminStoragePath);
        if (loggedIn) {
          await page.goto('/Admin');
          if (page.url().includes('/Account/Login')) loggedIn = false;
        }
      }

      if (!loggedIn) {
        const adminPassword = process.env.MEDICARE_E2E_ADMIN_PASSWORD || 'E2E_Admin_Test_2026!aA9';
        await page.goto('/Account/Login');
        await page.getByLabel('Email Address').fill('admin@medicare.com');
        await page.getByLabel('Password', { exact: true }).fill(adminPassword);
        await reserveLoginPost(testInfo);
        await page.getByRole('button', { name: /Sign In/i }).click();
        await expect(page).not.toHaveURL(/\/Account\/Login/i);
        await page.context().storageState({ path: adminStoragePath });
      }

      for (const p of adminPages) {
        await page.goto(p.path);
        await page.waitForLoadState('domcontentloaded');

        // Check for table responsiveness: tables must be wrapped in .table-responsive
        const tables = page.locator('table');
        const count = await tables.count();
        for (let i = 0; i < count; i++) {
          const table = tables.nth(i);
          const hasResponsiveParent = await table.evaluate((t) => {
            return t.closest('.table-responsive') !== null || t.closest('.table-responsive-sm') !== null || t.closest('.table-responsive-md') !== null;
          });
          expect(hasResponsiveParent, `Table on ${p.name} must be wrapped in .table-responsive`).toBeTruthy();
        }

        const overflow = await checkOverflow(page);
        if (overflow.hasOverflow) {
          console.warn(`[OVERFLOW_WARNING] ${p.name} at ${vp.name} has overflow of ${overflow.overflowAmount}px:`, overflow.badElements);
        }
        await page.screenshot({
          path: resolve(screenshotDir, `${p.name}-${vp.name}.png`),
          fullPage: false,
        });

        expect(overflow.hasOverflow && overflow.overflowAmount > 2, `${p.name} at ${vp.name} (${vp.width}px) has horizontal overflow of ${overflow.overflowAmount}px`).toBeFalsy();
      }
    });
  }
});

// --------------------------------------------------------------------------
// 3. DOCTOR, PATIENT & PHARMACIST WORKFLOW PAGES AUDIT
// --------------------------------------------------------------------------
test.describe('Responsive Clinical & Patient Role Pages Audit', () => {
  for (const vp of VIEWPORTS) {
    test(`Role workflow pages at ${vp.name} (${vp.width}x${vp.height})`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });

      const persisted = getPersistedState();
      const state = persisted.state || {};

      // 1. Doctor pages
      if (state.doctorCookies && existsSync(state.doctorCookies)) {
        await restoreCookiesFromState(page, state.doctorCookies);

        const doctorPages = [
          { name: 'doctor-appointments', path: '/Doctor/Appointments' },
          { name: 'doctor-schedule', path: '/Doctor/Schedule' },
          { name: 'doctor-leaves', path: '/Doctor/Leaves' },
        ];

        if (state.appointmentId) {
          doctorPages.push({ name: 'doctor-encounter', path: `/MedicalRecords/Create/${state.appointmentId}` });
        }

        for (const dp of doctorPages) {
          await page.goto(dp.path);
          await page.waitForLoadState('domcontentloaded');
          const overflow = await checkOverflow(page);
          if (overflow.hasOverflow) {
            console.warn(`[OVERFLOW_WARNING] ${dp.name} at ${vp.name} has overflow of ${overflow.overflowAmount}px:`, overflow.badElements);
          }
          await page.screenshot({
            path: resolve(screenshotDir, `${dp.name}-${vp.name}.png`),
            fullPage: false,
          });
          expect(overflow.hasOverflow && overflow.overflowAmount > 2, `${dp.name} at ${vp.name} has horizontal overflow of ${overflow.overflowAmount}px`).toBeFalsy();
        }
      }

      // 2. Patient pages
      if (state.patientCookies && existsSync(state.patientCookies)) {
        await restoreCookiesFromState(page, state.patientCookies);

        const patientPages = [
          { name: 'patient-appointments', path: '/Appointments/MyAppointments' },
          { name: 'patient-profile', path: '/Account/Profile' },
          { name: 'patient-history', path: '/MedicalRecords/MyHistory' },
        ];

        if (state.doctorId) {
          patientPages.push({ name: 'patient-booking', path: `/Appointments/Book/${state.doctorId}` });
        }
        if (state.appointmentId) {
          patientPages.push({ name: 'appointment-details', path: `/Appointments/Details/${state.appointmentId}` });
        }

        for (const pp of patientPages) {
          await page.goto(pp.path);
          await page.waitForLoadState('domcontentloaded');
          const overflow = await checkOverflow(page);
          if (overflow.hasOverflow) {
            console.warn(`[OVERFLOW_WARNING] ${pp.name} at ${vp.name} has overflow of ${overflow.overflowAmount}px:`, overflow.badElements);
          }
          await page.screenshot({
            path: resolve(screenshotDir, `${pp.name}-${vp.name}.png`),
            fullPage: false,
          });
          expect(overflow.hasOverflow && overflow.overflowAmount > 2, `${pp.name} at ${vp.name} has horizontal overflow of ${overflow.overflowAmount}px`).toBeFalsy();
        }
      }

      // 3. Pharmacist pages
      if (state.verifyToken) {
        await page.goto(`/Prescriptions/Dispense?token=${state.verifyToken}`);
        await page.waitForLoadState('domcontentloaded');
        const overflow = await checkOverflow(page);
        if (overflow.hasOverflow) {
          console.warn(`[OVERFLOW_WARNING] pharmacist-dispense at ${vp.name} has overflow of ${overflow.overflowAmount}px:`, overflow.badElements);
        }
        await page.screenshot({
          path: resolve(screenshotDir, `pharmacist-dispense-${vp.name}.png`),
          fullPage: false,
        });
        expect(overflow.hasOverflow && overflow.overflowAmount > 2, `pharmacist-dispense at ${vp.name} has horizontal overflow of ${overflow.overflowAmount}px`).toBeFalsy();
      }
    });
  }
});
