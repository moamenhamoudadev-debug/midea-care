import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test, expect, reserveLoginPost } from '../support/fixtures';
import { newSyntheticPatient, registerPatient, type SyntheticPatient } from '../support/patient';

type Actor = { fullName: string; email: string; password: string };
const sharedStatePath = resolve(__dirname, '../test-results/stage4-run-state.json');
const runId = process.env.MEDICARE_E2E_RUN_ID ?? '';
const databaseName = process.env.MEDICARE_E2E_DATABASE ?? '';
const persistedCandidate: Record<string, any> = existsSync(sharedStatePath) ? JSON.parse(readFileSync(sharedStatePath, 'utf8')) : {};
const persisted: Record<string, any> = persistedCandidate.runId === runId && persistedCandidate.databaseName === databaseName
  ? persistedCandidate
  : {};
const suffix = persisted.suffix ?? randomUUID().replaceAll('-', '').slice(0, 12).toLowerCase();
const doctor: Actor = persisted.doctor ?? { fullName: 'Synthetic Test Doctor', email: `medicare-doctor-${suffix}@example.test`, password: `Doctor!E2E${suffix}aA9` };
const patientA: SyntheticPatient = persisted.patientA ?? { fullName: 'Synthetic Patient Alpha', email: `medicare-patient-a-${suffix}@example.test`, password: `Patient!E2E${suffix}aA9` };
const pharmacist: Actor = persisted.pharmacist ?? { fullName: 'Synthetic Test Pharmacist', email: `medicare-pharmacist-${suffix}@example.test`, password: `Pharm!Temp${suffix}aA9` };
const pharmacistNewPassword: string = persisted.pharmacistNewPassword ?? `Pharm!New${suffix}bB8`;
const medication = 'Synthetic Test Medication 10mg';
const diagnosis = 'Synthetic E2E diagnosis';
const state: { doctorCookies?: string; patientCookies?: string; pharmacistCookies?: string; doctorId?: number; appointmentId?: number; recordId?: number; prescriptionId?: number; verifyToken?: string; patientB?: SyntheticPatient; patientBUserId?: string; adminWorkflowComplete?: boolean; doctorWorkflowComplete?: boolean; } = persisted.state ?? {};

function persistState(): void {
  Object.assign(persisted, { runId, databaseName, suffix, doctor, patientA, pharmacist, pharmacistNewPassword, state });
  writeFileSync(sharedStatePath, JSON.stringify({ runId, databaseName, suffix, doctor, patientA, pharmacist, pharmacistNewPassword, state }, null, 2));
}

function requireCurrentRunState(workflow: string, requiredState: string[]): void {
  if (!/^[a-f0-9]{32}$/.test(runId) || databaseName !== `MediCare_PlaywrightE2E_${runId}`) {
    throw new Error(`Prerequisite failure: ${workflow} has no valid current Playwright run and disposable database identity.`);
  }
  if (persisted.runId !== runId || persisted.databaseName !== databaseName) {
    throw new Error(`Prerequisite failure: ${workflow} state was not produced by this Playwright run and disposable database.`);
  }
  const missing = requiredState.filter((key) => {
    const value = state[key as keyof typeof state];
    return value === undefined || value === null || value === '' || value === false || (typeof value === 'number' && (!Number.isInteger(value) || value <= 0));
  });
  if (missing.length) {
    throw new Error(`Prerequisite failure: ${workflow} requires completed upstream workflow state; missing or invalid: ${missing.join(', ')}.`);
  }
}

async function saveCookies(page: import('@playwright/test').Page, testInfo: import('@playwright/test').TestInfo): Promise<string> {
  const path = testInfo.outputPath('actor-storage-state.json');
  await page.context().storageState({ path });
  return path;
}

async function restoreCookies(page: import('@playwright/test').Page, path: string): Promise<void> {
  const cookies = JSON.parse(readFileSync(path, 'utf8')).cookies;
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
  await page.goto('/');
}

async function login(page: import('@playwright/test').Page, email: string, password: string, testInfo: import('@playwright/test').TestInfo): Promise<void> {
  await page.goto('/Account/Login');
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await reserveLoginPost(testInfo);
  await page.getByRole('button', { name: /Sign In/i }).click();
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
}

async function expectAccessDenied(page: import('@playwright/test').Page, route: string): Promise<void> {
  const response = await page.goto(route);
  const deniedStatus = [401, 403].includes(response?.status() ?? 0);
  const safeDenyRedirect = /\/Account\/(AccessDenied|Login)/i.test(page.url());
  expect(deniedStatus || safeDenyRedirect, `Expected a deny response or safe redirect for ${route}; received ${response?.status()} at ${page.url()}`).toBeTruthy();
}

async function registerDoctor(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/Account/RegisterDoctor');
  await page.getByLabel('Full Legal Name').fill(doctor.fullName);
  await page.getByLabel('Clinic Email Address').fill(doctor.email);
  await page.getByLabel('Password', { exact: true }).fill(doctor.password);
  await page.getByLabel('Confirm Password', { exact: true }).fill(doctor.password);
  await page.getByLabel('Clinical Specialization').selectOption({ label: 'Cardiology' });
  await page.getByLabel('Medical Syndicate License #').fill(`E2E-${suffix}`);
  await page.getByLabel('Visit Fee (EGP)').fill('300');
  await page.getByLabel('Phone Number').fill('0000000001');
  await page.getByLabel('Clinic Location / Governorate').selectOption('Cairo');
  await page.getByRole('button', { name: /Submit Practice Application/i }).click();
  await expect(page).toHaveURL(/\/Account\/Login/i);
  await expect(page.getByText(/currently pending administrator verification/i)).toBeVisible();
}

async function backdateOnlyE2EAppointment(appointmentId: number, runId: string, databaseName: string): Promise<void> {
  const connectionString = `Server=(localdb)\\MSSQLLocalDB;Database=${databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=15;Application Name=MediCare.Playwright.E2E`;
  expect(runId).toMatch(/^[a-f0-9]{32}$/);
  expect(databaseName).toBe(`MediCare_PlaywrightE2E_${runId}`);
  expect(connectionString).toContain(`Database=${databaseName};`);
  expect(connectionString).toContain('Server=(localdb)\\MSSQLLocalDB;');
  expect(Number.isInteger(appointmentId) && appointmentId > 0).toBeTruthy();
  const query = `SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF; SET NOCOUNT ON; IF DB_NAME() <> N'master' THROW 51000, 'Safety check must start in master', 1; IF DB_ID(N'${databaseName}') IS NULL THROW 51001, 'Run database does not exist', 1; USE [${databaseName}]; IF DB_NAME() <> N'${databaseName}' THROW 51002, 'Wrong test database', 1; UPDATE dbo.Appointments SET AppointmentDate = DATEADD(day, -7, AppointmentDate) WHERE Id = ${appointmentId}; IF @@ROWCOUNT <> 1 THROW 51003, 'Expected exactly one synthetic appointment', 1; SELECT DB_NAME() AS DatabaseName, Id, AppointmentDate FROM dbo.Appointments WHERE Id = ${appointmentId};`;
  let output: string;
  try {
    output = execFileSync('sqlcmd', ['-S', '(localdb)\\MSSQLLocalDB', '-E', '-d', 'master', '-b', '-Q', query], { encoding: 'utf8' });
  } catch (error) {
    const detail = error as NodeJS.ErrnoException & { stderr?: Buffer; stdout?: Buffer };
    throw new Error(`The guarded disposable appointment fixture failed: ${detail.stderr?.toString() || detail.stdout?.toString() || detail.message}`);
  }
  expect(output).toContain(databaseName);
  console.log(`[E2E_FIXTURE] Backdated only synthetic appointment ${appointmentId} in ${databaseName} to reach the application encounter flow.`);
}

test('Admin approves doctor, manages synthetic patient, and provisions pharmacist', async ({ page }, testInfo) => {
  await registerDoctor(page);
  await registerPatient(page, patientA, false);
  state.patientCookies = await saveCookies(page, testInfo);
  persistState();
  await page.getByRole('button', { name: /Log Out/i }).click();

  const adminPassword = process.env.MEDICARE_E2E_ADMIN_PASSWORD;
  expect(adminPassword).toBe('E2E_Admin_Test_2026!aA9');
  await login(page, 'admin@medicare.com', adminPassword!, testInfo);
  await page.goto('/Admin/Approvals');
  const pendingDoctor = page.getByRole('row').filter({ hasText: doctor.email });
  await expect(pendingDoctor).toBeVisible();
  const approvalAction = await pendingDoctor.locator('form[action^="/Admin/ApproveDoctor/"]').getAttribute('action');
  state.doctorId = Number(approvalAction?.match(/\/Admin\/ApproveDoctor\/(\d+)$/)?.[1]);
  expect(Number.isInteger(state.doctorId) && state.doctorId! > 0, 'approved synthetic Doctor must have a valid profile ID').toBeTruthy();
  page.once('dialog', (dialog) => dialog.accept());
  await pendingDoctor.getByRole('button', { name: /Approve/i }).click();
  await expect(page.getByText(/Doctor account has been approved/i)).toBeVisible();

  await page.goto(`/Admin/Patients?search=${encodeURIComponent(patientA.email)}`);
  await expect(page.locator('table caption')).toHaveText('Patient records');
  await expect(page.locator('table thead th')).not.toHaveCount(0);
  await expect(page.locator('table thead th').first()).toHaveAttribute('scope', 'col');
  const patientRow = page.getByRole('row').filter({ hasText: patientA.email });
  await expect(patientRow).toBeVisible();
  page.once('dialog', (dialog) => dialog.accept());
  await patientRow.getByRole('button').click();
  await expect(patientRow.getByText('Locked', { exact: true })).toBeVisible();
  page.once('dialog', (dialog) => dialog.accept());
  await patientRow.getByRole('button').click();
  await expect(patientRow.getByText('Active', { exact: true })).toBeVisible();

  await page.goto('/Admin/CreatePharmacist');
  await page.getByLabel('Full Name').fill(pharmacist.fullName);
  await page.getByLabel('Email Address').fill(pharmacist.email);
  await page.getByLabel('Temporary Password').fill(pharmacist.password);
  await page.getByLabel('Confirm Password').fill(pharmacist.password);
  await page.getByRole('button', { name: /Create Pharmacist/i }).click();
  await expect(page.getByText(new RegExp(pharmacist.email))).toBeVisible();
  await page.goto('/Admin/Pharmacists');
  await expect(page.getByRole('row').filter({ hasText: pharmacist.email })).toBeVisible();
  state.adminWorkflowComplete = true;
  persistState();
});

test('Doctor manages schedule, confirms appointment, creates encounter and prescription', async ({ page }, testInfo) => {
  requireCurrentRunState('Doctor workflow', ['patientCookies', 'adminWorkflowComplete', 'doctorId']);
  await login(page, doctor.email, doctor.password, testInfo);
  await page.goto('/Doctor/Schedule');
  const day = await page.evaluate(() => {
    const cairoToday = new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
    const date = new Date(`${cairoToday}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + 3); return date.getUTCDay();
  });
  await page.getByLabel('Day of the Week').selectOption(String(day));
  await page.getByLabel('Start Time').fill('09:00');
  await page.getByLabel('End Time').fill('12:00');
  await page.getByRole('button', { name: /Add Shift Interval/i }).click();
  await expect(page.getByText(/Working hours added successfully/i)).toBeVisible();
  await page.goto('/Doctor/Schedule');
  await expect(page.locator('table caption')).toHaveText('Doctor working schedule');
  await expect(page.locator('table thead th').first()).toHaveAttribute('scope', 'col');
  state.doctorCookies = await saveCookies(page, testInfo);
  persistState();

  await restoreCookies(page, state.patientCookies!);
  const doctorId = state.doctorId!;
  await page.goto(`/Doctors/Details/${doctorId}`);
  await expect(page).toHaveURL(new RegExp(`/Doctors/Details/${doctorId}$`));
  await expect(page.locator('.doc-name').first()).toContainText(doctor.fullName);
  const doctorName = (await page.locator('.doc-name').first().innerText()).trim();
  await page.getByRole('link', { name: /Book/i }).first().click();
  await expect(page.locator('input[name="doctorId"]')).toHaveValue(String(doctorId));
  const availableDate = await page.evaluate(async () => {
    const input = document.querySelector<HTMLInputElement>('input[name="doctorId"]'); if (!input) throw new Error('doctorId missing');
    const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
    const base = new Date(`${today}T00:00:00Z`);
    for (let offset = 1; offset <= 30; offset++) {
      const candidate = new Date(base); candidate.setUTCDate(candidate.getUTCDate() + offset);
      const date = new Intl.DateTimeFormat('en-CA', { timeZone: 'UTC', year: 'numeric', month: '2-digit', day: '2-digit' }).format(candidate);
      const response = await fetch(`/api/calendar/slots?doctorId=${Number(input.value)}&date=${date}`);
      if (response.ok) { const result = await response.json(); if (result.success && result.data?.some((slot: { isAvailable: boolean }) => slot.isAvailable)) return date; }
    }
    return null;
  });
  expect(availableDate).toBeTruthy();
  const bookingUrl = new URL(page.url()); bookingUrl.searchParams.set('date', availableDate!); await page.goto(bookingUrl.toString());
  await page.locator('#slotsContainer button.slot-btn').first().click();
  await expect(page.locator('#bookingModal')).toBeVisible();
  await page.locator('#patientNotesInput').fill(`Synthetic booking ${patientA.email}`);
  await page.getByRole('button', { name: /Confirm Reservation/i }).click();
  await expect(page).toHaveURL(/\/Appointments\/MyAppointments/i);
  const appointmentLink = page.locator('a[href^="/Appointments/Details/"]').first();
  const href = await appointmentLink.getAttribute('href');
  state.appointmentId = Number(href?.match(/Details\/(\d+)/)?.[1]);
  expect(state.appointmentId).toBeGreaterThan(0);
  persistState();
  const runId = String(testInfo.config.metadata.medicareE2ERunId ?? '');
  const databaseName = String(testInfo.config.metadata.medicareE2EDatabase ?? '');
  await backdateOnlyE2EAppointment(state.appointmentId!, runId, databaseName);
  persistState();

  await restoreCookies(page, state.doctorCookies!);
  await page.goto('/Doctor/Appointments');
  const appointmentRow = page.getByRole('row').filter({ hasText: patientA.email });
  await expect(appointmentRow).toBeVisible();
  await appointmentRow.getByRole('button', { name: /Confirm/i }).click();
  await expect(page.getByText(/Appointment confirmed successfully/i)).toBeVisible();
  await page.goto('/Doctor/Appointments');
  const confirmedRow = page.getByRole('row').filter({ hasText: patientA.email });
  await confirmedRow.getByRole('link', { name: /Consult/i }).click();
  await expect(page).toHaveURL(new RegExp(`/MedicalRecords/Create/${state.appointmentId}`));
  await page.locator('[name="Encounter.Diagnosis"]').fill(diagnosis);
  await page.locator('[name="Encounter.Symptoms"]').fill('Synthetic test observation');
  await page.locator('[name="Encounter.VisitNotes"]').fill('Synthetic follow-up note');
  await page.locator('#btnAddMedicationEmpty').click();
  await page.locator('[name="Encounter.PrescriptionItems[0].MedicationName"]').fill(medication);
  await page.locator('[name="Encounter.PrescriptionItems[0].Dosage"]').fill('1 tablet');
  await page.locator('[name="Encounter.PrescriptionItems[0].Frequency"]').fill('Once daily');
  await page.locator('[name="Encounter.PrescriptionItems[0].DurationDays"]').fill('5');
  await page.locator('[name="Encounter.PrescriptionItems[0].Instructions"]').fill('Synthetic test only');
  await page.locator('[name="Encounter.Notes"]').fill('Synthetic pharmacy note');
  await expect(page.locator('#encounterForm')).toHaveAttribute('action', '/MedicalRecords/Create');
  await expect(page.locator('[name="Encounter.AppointmentId"]')).toHaveValue(String(state.appointmentId));
  const encounterResponse = page.waitForResponse((response) => new URL(response.url()).pathname === '/MedicalRecords/Create' && response.request().method() === 'POST');
  await page.getByRole('button', { name: /Complete Consultation & Finalize Visit/i }).click();
  const postResponse = await encounterResponse;
  console.log(`[E2E_RESULT] Encounter form POST returned HTTP ${postResponse.status()} at ${postResponse.url()}.`);
  expect([302, 303], 'successful encounter submission should redirect after POST').toContain(postResponse.status());
  await expect(page).toHaveURL(/\/MedicalRecords\/Details\/\d+/);
  state.recordId = Number(page.url().match(/Details\/(\d+)/)?.[1]);
  persistState();
  await expect(page.getByText(diagnosis)).toBeVisible();
  await expect(page.getByText(medication)).toBeVisible();
  await page.goto(`/Prescriptions/ByAppointment/${state.appointmentId}`);
  await expect(page).toHaveURL(/\/Prescriptions\/Print\/\d+/);
  state.prescriptionId = Number(page.url().match(/Print\/(\d+)/)?.[1]);
  const verifyHref = await page.locator('a[href*="/Prescriptions/Verify?token="]').first().getAttribute('href');
  state.verifyToken = new URL(verifyHref!, page.url()).searchParams.get('token') ?? undefined;
  persistState();
  expect(state.verifyToken).toMatch(/^[a-f0-9]{32}$/i);
  await expect(page.getByText(medication)).toBeVisible();
  await expect(page.locator('table caption')).toHaveText('Prescribed medications');
  await expect(page.locator('table thead th').first()).toHaveAttribute('scope', 'col');
  state.doctorWorkflowComplete = true;
  persistState();
});

test('Pharmacist verifies and dispenses the doctor-issued prescription exactly once', async ({ page }, testInfo) => {
  requireCurrentRunState('Pharmacist workflow', ['adminWorkflowComplete', 'doctorWorkflowComplete', 'appointmentId', 'recordId', 'prescriptionId', 'verifyToken', 'doctorCookies']);
  await login(page, pharmacist.email, pharmacist.password, testInfo);
  await expect(page).toHaveURL(/\/Account\/ChangePassword/i);
  await page.getByLabel('Current Temporary Password').fill(pharmacist.password);
  await page.getByLabel('New Secure Password').fill(pharmacistNewPassword);
  await page.getByLabel('Confirm New Password').fill(pharmacistNewPassword);
  await page.getByRole('button', { name: /Update Password & Continue/i }).click();
  await expect(page).not.toHaveURL(/\/Account\/ChangePassword/i);
  state.pharmacistCookies = await saveCookies(page, testInfo);
  persistState();
  expect(state.verifyToken).toMatch(/^[a-f0-9]{32}$/i);
  await page.goto(`/Prescriptions/Verify?token=${state.verifyToken}`);
  await expect(page.getByText(/تم العثور على سجل الوصفة في النظام/)).toBeVisible();
  await expect(page.getByText(medication)).toHaveCount(0);
  await expect(page.getByText(/already dispensed/i)).toHaveCount(0);
  await page.goto(`/Prescriptions/Dispense?token=${state.verifyToken}`);
  await expect(page.getByText(medication)).toBeVisible();
  await expect(page.locator('form[action="/Prescriptions/Dispense"]')).toBeVisible();
  await page.locator('[name="pharmacyNotes"]').fill('Synthetic E2E dispensing record only');
  await page.getByRole('button', { name: /تأكيد صرف الروشتة رسمياً/ }).click();
  await expect(page.getByText(/already dispensed/i)).toBeVisible();
  await expect(page.getByText(/Synthetic E2E dispensing record only/i)).toBeVisible();
  await expect(page.locator('form[action="/Prescriptions/Dispense"]')).toHaveCount(0);
  await page.goto(`/Prescriptions/Verify?token=${state.verifyToken}`);
  await expect(page.getByText(/already dispensed/i)).toBeVisible();
});

test('Security blocks role violations and cross-account clinical data access', async ({ page }, testInfo) => {
  requireCurrentRunState('Security workflow', ['adminWorkflowComplete', 'doctorWorkflowComplete', 'appointmentId', 'recordId', 'prescriptionId', 'verifyToken', 'doctorCookies']);
  const appointmentId = state.appointmentId!;

  state.patientB = newSyntheticPatient();
  state.patientB.fullName = 'Synthetic Patient Beta';
  await registerPatient(page, state.patientB, false);
  const patientBUserId = await page.locator('body').getAttribute('data-user-id');
  // The identity is not exposed as a DOM attribute; obtain a safe, synthetic identifier from the profile route.
  await page.goto('/Account/Profile');
  const betaId = patientBUserId ?? await page.locator('input[name="UserId"]').getAttribute('value').catch(() => null);
  expect(betaId, 'test account user id is needed only to probe doctor patient-history authorization').toBeTruthy();
  state.patientBUserId = betaId!;
  persistState();
  const ownHistory = await page.goto('/MedicalRecords/MyHistory');
  expect(ownHistory?.status()).toBe(200);
  const deniedRoutes = [
    `/Appointments/Details/${appointmentId}`,
    `/MedicalRecords/Details/${state.recordId ?? 1}`,
    `/Prescriptions/Print/${state.prescriptionId ?? 1}`,
    '/Admin/Patients', '/Doctor/Schedule', `/Prescriptions/Dispense?token=${state.verifyToken}`,
  ];
  for (const route of deniedRoutes) {
    const response = await page.goto(route);
    console.log(`[E2E_SECURITY] patient-B GET ${route} -> HTTP ${response?.status()} ${page.url()}`);
    if (route.startsWith('/Appointments/Details/')) {
      expect([200, 403]).toContain(response?.status());
      expect(page.url()).not.toContain('/Appointments/Details/');
      await expect(page.getByText(patientA.email, { exact: true })).toHaveCount(0);
    } else {
      const deniedByResponse = [403, 404].includes(response?.status() ?? 0);
      const redirectedAway = response?.status() === 200 && !page.url().includes(route.split('?')[0]);
      expect(deniedByResponse || redirectedAway, `Patient must not receive protected content at ${route}`).toBeTruthy();
    }
  }

  await page.context().clearCookies();
  await restoreCookies(page, state.doctorCookies!);
  if (state.recordId) {
    const doctorOwnRecord = await page.goto(`/MedicalRecords/Details/${state.recordId}`);
    expect(doctorOwnRecord?.status()).toBe(200);
  }
  await expectAccessDenied(page, `/MedicalRecords/PatientHistory/${betaId}`);
  await expectAccessDenied(page, `/Prescriptions/Dispense?token=${state.verifyToken ?? '00000000000000000000000000000000'}`);

  await page.context().clearCookies();
  await login(page, 'admin@medicare.com', process.env.MEDICARE_E2E_ADMIN_PASSWORD!, testInfo);
  const adminRegistry = await page.goto('/Admin/Patients');
  expect(adminRegistry?.status()).toBe(200);
  await expectAccessDenied(page, `/MedicalRecords/Details/${state.recordId ?? 1}`);
  await expectAccessDenied(page, `/Prescriptions/Print/${state.prescriptionId ?? 1}`);

  await page.context().clearCookies();
  await restoreCookies(page, state.pharmacistCookies!);
  await expectAccessDenied(page, '/Admin/Patients');
  await expectAccessDenied(page, '/Doctor/Schedule');
});
