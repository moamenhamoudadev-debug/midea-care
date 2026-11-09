import { test, expect } from '../support/fixtures';
import { loginPatient, newSyntheticPatient, registerPatient } from '../support/patient';

test('registration labels, password toggles, and shared controls expose accessible names and state', async ({ page }, testInfo) => {
  await page.goto('/Account/RegisterPatient');
  await page.evaluate(() => {
    localStorage.setItem('medicare-theme', 'light');
    localStorage.setItem('medicare-lang', 'en');
  });
  await page.reload();

  const password = page.getByLabel('Password', { exact: true });
  const confirmPassword = page.getByLabel('Confirm Password', { exact: true });
  await expect(password).toHaveAttribute('id', 'regPassword');
  await expect(confirmPassword).toHaveAttribute('id', 'regConfirmPassword');

  const passwordToggles = page.getByRole('button', { name: 'Show password' });
  await expect(passwordToggles).toHaveCount(2);
  await expect(passwordToggles.nth(0)).toHaveAttribute('aria-controls', 'regPassword');
  await passwordToggles.nth(0).click();
  const hidePassword = page.getByRole('button', { name: 'Hide password' }).first();
  await expect(hidePassword).toHaveAttribute('aria-pressed', 'true');
  await expect(password).toHaveAttribute('type', 'text');
  await hidePassword.click();
  await expect(page.getByRole('button', { name: 'Show password' }).first()).toHaveAttribute('aria-pressed', 'false');
  await expect(password).toHaveAttribute('type', 'password');

  await password.focus();
  await page.keyboard.press('Tab');
  await expect(page.locator('.btn-toggle-pwd').first()).toBeFocused();
  await expect(page.locator('.btn-toggle-pwd').first()).toHaveCSS('outline-style', 'solid');
  await expect(page.locator('.btn-toggle-pwd').first()).toHaveCSS('outline-width', '3px');

  const themeToggle = page.getByRole('button', { name: 'Toggle color theme' });
  await expect(themeToggle).toHaveAttribute('aria-pressed', 'false');
  await themeToggle.click();
  await expect(themeToggle).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('html')).toHaveAttribute('data-bs-theme', 'dark');
  await themeToggle.click();
  await expect(themeToggle).toHaveAttribute('aria-pressed', 'false');

  const languageToggle = page.getByRole('button', { name: 'Toggle language' });
  await expect(languageToggle).toHaveAttribute('aria-pressed', 'false');
  await languageToggle.click();
  await expect(languageToggle).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  await languageToggle.click();
  await expect(languageToggle).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');

  const patient = newSyntheticPatient();
  await registerPatient(page, patient);
  await loginPatient(page, patient, testInfo);
  const notifications = page.getByRole('button', { name: 'Notifications' });
  await expect(notifications).toHaveAttribute('aria-expanded', 'false');
  await notifications.click();
  await expect(notifications).toHaveAttribute('aria-expanded', 'true');
});

test('doctor registration password labels point to their inputs', async ({ page }) => {
  await page.goto('/Account/RegisterDoctor');
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('id', 'regPassword');
  await expect(page.getByLabel('Confirm Password', { exact: true })).toHaveAttribute('id', 'regConfirmPassword');
});
