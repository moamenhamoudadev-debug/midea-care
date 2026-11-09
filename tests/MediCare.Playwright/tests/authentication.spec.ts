import { test, expect, assertLocalPage, reserveLoginPost } from '../support/fixtures';
import { loginPatient, newSyntheticPatient, registerPatient } from '../support/patient';

test('synthetic patient can register, sign in, and sign out', async ({ page }, testInfo) => {
  const patient = newSyntheticPatient();

  await registerPatient(page, patient);
  await assertLocalPage(page);

  // Logout is POST-only; use the visible navigation form to exercise the real flow.
  await loginPatient(page, patient, testInfo);
  await expect(page.getByText(patient.email, { exact: true })).toBeVisible();

  await page.getByRole('button', { name: /Log Out/i }).click();
  await expect(page).toHaveURL(/\/$|\/Home$/i);
  await page.goto('/Appointments/MyAppointments');
  await expect(page).toHaveURL(/\/Account\/Login/i);
  await assertLocalPage(page);
});

test('invalid credentials do not create an authenticated session', async ({ page }, testInfo) => {
  await page.goto('/Account/Login');
  await page.getByLabel('Email Address').fill('medicare-e2e-invalid@example.test');
  await page.getByLabel('Password', { exact: true }).fill('Wrong!Password123');
  await reserveLoginPost(testInfo);
  await page.getByRole('button', { name: /Sign In/i }).click();

  await expect(page).toHaveURL(/\/Account\/Login/i);
  await expect(page.getByRole('alert')).toBeVisible();
  await assertLocalPage(page);
});
