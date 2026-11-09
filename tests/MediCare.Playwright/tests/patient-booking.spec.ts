import { test, expect, assertLocalPage, reserveLoginPost } from '../support/fixtures';
import { loginPatient, newSyntheticPatient, registerPatient } from '../support/patient';
import { randomUUID } from 'node:crypto';

test('patient books an available slot and sees the persisted appointment after sign-in again', async ({ page }, testInfo) => {
  const doctorSuffix = randomUUID().replaceAll('-', '').slice(0, 12).toLowerCase();
  const doctor = {
    fullName: 'Synthetic Test Doctor',
    email: `medicare-e2e-doctor-${doctorSuffix}@example.test`,
    password: `Doctor!E2E${doctorSuffix.slice(0, 8)}aA9`,
  };

  // The production-mode test database contains no demo accounts. Create all actors via
  // application screens so this test remains fully synthetic and exercises real flows.
  await page.goto('/Account/RegisterDoctor');
  await page.getByLabel('Full Legal Name').fill(doctor.fullName);
  await page.getByLabel('Clinic Email Address').fill(doctor.email);
  await page.getByLabel('Password', { exact: true }).fill(doctor.password);
  await page.getByLabel('Confirm Password', { exact: true }).fill(doctor.password);
  await page.getByLabel('Clinical Specialization').selectOption({ label: 'Cardiology' });
  await page.getByLabel('Medical Syndicate License #').fill(`E2E-${doctorSuffix}`);
  await page.getByLabel('Visit Fee (EGP)').fill('300');
  await page.getByLabel('Phone Number').fill('0000000001');
  await page.getByLabel('Clinic Location / Governorate').selectOption('Cairo');
  await page.getByRole('button', { name: /Submit Practice Application/i }).click();
  await expect(page).toHaveURL(/\/Account\/Login/i);
  await expect(page.getByText(/currently pending administrator verification/i)).toBeVisible();

  const adminPassword = process.env.MEDICARE_E2E_ADMIN_PASSWORD;
  expect(adminPassword, 'the synthetic Admin test password must be available to the test').toBeTruthy();
  expect(adminPassword).toBe('E2E_Admin_Test_2026!aA9');
  const adminPage = await page.context().newPage();
  await adminPage.goto('/Account/Login');
  await adminPage.getByLabel('Email Address').fill('admin@medicare.com');
  await adminPage.getByLabel('Password', { exact: true }).fill(adminPassword!);
  await expect(adminPage.getByLabel('Password', { exact: true })).toHaveValue(adminPassword!);
  await reserveLoginPost(testInfo);
  await adminPage.getByRole('button', { name: /Sign In/i }).click();
  await expect(adminPage).not.toHaveURL(/\/Account\/Login/i);
  await adminPage.goto('/Admin/Approvals');
  const pendingDoctor = adminPage.getByRole('row').filter({ hasText: doctor.email });
  await expect(pendingDoctor).toBeVisible();
  adminPage.once('dialog', (dialog) => dialog.accept());
  await pendingDoctor.getByRole('button', { name: /Approve/i }).click();
  await expect(adminPage.getByText(/Doctor account has been approved/i)).toBeVisible();
  await adminPage.getByRole('button', { name: /Log Out/i }).click();
  await adminPage.close();

  await page.goto('/Account/Login');
  await page.getByLabel('Email Address').fill(doctor.email);
  await page.getByLabel('Password', { exact: true }).fill(doctor.password);
  await reserveLoginPost(testInfo);
  await page.getByRole('button', { name: /Sign In/i }).click();
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
  await page.goto('/Doctor/Schedule');

  const schedule = await page.evaluate(() => {
    const date = new Date(new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit',
    }).format(new Date()) + 'T00:00:00Z');
    date.setUTCDate(date.getUTCDate() + 3);
    return { day: date.getUTCDay() };
  });
  await page.getByLabel('Day of the Week').selectOption(String(schedule.day));
  await page.getByLabel('Start Time').fill('09:00');
  await page.getByLabel('End Time').fill('12:00');
  await page.getByRole('button', { name: /Add Shift Interval/i }).click();
  await expect(page.getByText(/Working hours added successfully/i)).toBeVisible();
  await page.getByRole('button', { name: /Log Out/i }).click();

  const patient = newSyntheticPatient();
  await registerPatient(page, patient, false);

  await page.goto('/Doctors');
  const profileLink = page.locator('a[href^="/Doctors/Details/"]').first();
  await expect(profileLink).toBeVisible();
  await profileLink.click();
  await expect(page).toHaveURL(/\/Doctors\/Details\/\d+/);

  const doctorName = (await page.locator('.doc-name').first().innerText()).trim();
  const doctorId = Number(page.url().match(/\/Doctors\/Details\/(\d+)/)?.[1]);
  expect(Number.isInteger(doctorId) && doctorId > 0).toBeTruthy();

  const bookingLink = page.getByRole('link', { name: /Book/i }).first();
  await expect(bookingLink).toBeVisible();
  const bookingHref = await bookingLink.getAttribute('href');
  expect(bookingHref).toBeTruthy();
  await bookingLink.click();
  await expect(page).toHaveURL(/\/Appointments\/Book\//);

  // Probe only the public availability API from this browser page to find a seeded future slot;
  // the actual selection and booking remain UI interactions.
  const availableDate = await page.evaluate(async () => {
    const doctorIdInput = document.querySelector<HTMLInputElement>('input[name="doctorId"]');
    if (!doctorIdInput) throw new Error('Booking page did not render doctorId.');
    const doctorId = Number(doctorIdInput.value);
    const cairoDate = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit',
    }).format(new Date());
    const base = new Date(`${cairoDate}T00:00:00Z`);

    for (let offset = 1; offset <= 30; offset += 1) {
      const candidate = new Date(base);
      candidate.setUTCDate(candidate.getUTCDate() + offset);
      const date = new Intl.DateTimeFormat('en-CA', {
        timeZone: 'UTC', year: 'numeric', month: '2-digit', day: '2-digit',
      }).format(candidate);
      const response = await fetch(`/api/calendar/slots?doctorId=${doctorId}&date=${date}`);
      if (!response.ok) continue;
      const result = await response.json();
      if (result.success && result.data?.some((slot: { isAvailable: boolean }) => slot.isAvailable)) return date;
    }
    return null;
  });
  expect(availableDate, 'a seeded doctor should have at least one available future slot in the next 30 days').toBeTruthy();

  const url = new URL(page.url());
  url.searchParams.set('date', availableDate!);
  await page.goto(url.toString());
  const firstSlot = page.locator('#slotsContainer button.slot-btn').first();
  await expect(firstSlot).toBeVisible();
  await firstSlot.click();
  await expect(page.locator('#bookingModal')).toBeVisible();

  const testNote = `Synthetic booking ${patient.email}`;
  await page.locator('#patientNotesInput').fill(testNote);
  await page.getByRole('button', { name: /Confirm Reservation/i }).click();
  await expect(page).toHaveURL(/\/Appointments\/MyAppointments/i);
  await expect(page.getByText(/successfully scheduled/i)).toBeVisible();

  const bookedRow = page.getByRole('row').filter({ hasText: doctorName });
  await expect(bookedRow).toBeVisible();
  await expect(bookedRow).toContainText(/Pending Approval/i);

  // Reload and re-authenticate to prove the booking was persisted, not just rendered from client state.
  await page.getByRole('button', { name: /Log Out/i }).click();
  await loginPatient(page, patient, testInfo);
  await page.goto('/Appointments/MyAppointments');
  const persistedRow = page.getByRole('row').filter({ hasText: doctorName });
  await expect(persistedRow).toBeVisible();
  await expect(persistedRow).toContainText(/Pending Approval/i);
  await assertLocalPage(page);
});
