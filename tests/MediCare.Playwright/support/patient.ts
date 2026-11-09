import { randomUUID } from 'node:crypto';
import type { Page, TestInfo } from '@playwright/test';
import { expect, reserveLoginPost } from './fixtures';

export type SyntheticPatient = {
  fullName: string;
  email: string;
  password: string;
};

export function newSyntheticPatient(): SyntheticPatient {
  const suffix = randomUUID().replaceAll('-', '').slice(0, 12).toLowerCase();
  return {
    fullName: 'Synthetic Test Patient',
    email: `medicare-e2e-${suffix}@example.test`,
    password: `Patient!E2E${suffix.slice(0, 8)}aA9`,
  };
}

export async function registerPatient(
  page: Page,
  patient: SyntheticPatient,
  logoutAfterRegistration = true,
): Promise<void> {
  await page.goto('/Account/RegisterPatient');
  await page.getByLabel('Full Legal Name').fill(patient.fullName);
  await page.getByLabel('Email Address').fill(patient.email);
  await page.getByLabel('Password', { exact: true }).fill(patient.password);
  await page.getByLabel('Confirm Password', { exact: true }).fill(patient.password);
  await page.getByLabel('Phone Number').fill('0000000000');
  await page.getByLabel('Date of Birth').fill('1990-01-01');
  await page.getByLabel('Gender').selectOption('Male');
  await page.getByRole('button', { name: /Complete Registration/i }).click();
  await expect(page.getByText(/patient account has been created successfully/i)).toBeVisible();
  const logoutButton = page.getByRole('button', { name: /Log Out/i });
  if (logoutAfterRegistration && await logoutButton.isVisible()) await logoutButton.click();
}

export async function loginPatient(page: Page, patient: SyntheticPatient, testInfo: TestInfo): Promise<void> {
  await page.goto('/Account/Login');
  await page.getByLabel('Email Address').fill(patient.email);
  await page.getByLabel('Password', { exact: true }).fill(patient.password);
  await reserveLoginPost(testInfo);
  await page.getByRole('button', { name: /Sign In/i }).click();
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
}
