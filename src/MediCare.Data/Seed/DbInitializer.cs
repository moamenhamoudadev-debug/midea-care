using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediCare.Data.Seed;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetService<ILogger<ApplicationDbContext>>();

        var hostEnvironment = scope.ServiceProvider.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        var isDevelopment = hostEnvironment != null
            ? string.Equals(hostEnvironment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
            : string.Equals(configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);

        var migrationSetting = configuration["Database:ApplyMigrationsOnStartup"]
            ?? configuration["APPLY_MIGRATIONS_ON_STARTUP"];
        var applyMigrationsOnStartup = isDevelopment;
        if (!string.IsNullOrWhiteSpace(migrationSetting))
        {
            if (!bool.TryParse(migrationSetting, out var configuredApplyMigrations))
            {
                throw new InvalidOperationException(
                    "Database:ApplyMigrationsOnStartup must be set to 'true' or 'false' when configured.");
            }

            applyMigrationsOnStartup |= configuredApplyMigrations;
        }

        // Auto-migrate database if pending (relational databases only)
        // In Production, automatic schema migration on startup is disabled by default to avoid concurrent schema modifications across instances.
        if (context.Database.IsRelational() && (await context.Database.GetPendingMigrationsAsync()).Any())
        {
            if (applyMigrationsOnStartup)
            {
                logger?.LogInformation("Applying pending EF Core migrations automatically (Development / Opt-in mode)...");
                await context.Database.MigrateAsync();
            }
            else
            {
                logger?.LogWarning("Pending EF Core migrations detected, but automatic startup migration is disabled in non-Development environments. Apply migrations using deployment pipeline scripts or 'dotnet ef database update'.");
            }
        }

        // Ensure all application roles exist including Pharmacist
        string[] appRoles = { "Admin", "Doctor", "Patient", "Pharmacist" };
        foreach (var role in appRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Demo seed password is configured locally and is never embedded in source code.
        string defaultPassword = isDevelopment
            ? (configuration["Seed:DefaultPassword"] ?? configuration["SEED_DEFAULT_PASSWORD"] ?? string.Empty)
            : string.Empty;

        // Ensure Pharmacist test user is seeded ONLY in Development mode with password read from configuration/user-secrets
        if (isDevelopment)
        {
            var pharmacistPassword = configuration["Seed:PharmacistPassword"] ?? configuration["SEED_PHARMACIST_PASSWORD"];
            if (string.IsNullOrWhiteSpace(pharmacistPassword))
            {
                logger?.LogWarning("Security: Pharmacist seed account skipped because 'Seed:PharmacistPassword' secret is not configured.");
            }
            else
            {
                var pharmacistUser = await userManager.FindByEmailAsync("pharmacist@medicare.com");
                if (pharmacistUser == null)
                {
                    pharmacistUser = new ApplicationUser
                    {
                        UserName = "pharmacist@medicare.com",
                        Email = "pharmacist@medicare.com",
                        FullName = "Licensed Pharmacist",
                        PhoneNumber = "+201000000099",
                        EmailConfirmed = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    var createRes = await userManager.CreateAsync(pharmacistUser, pharmacistPassword);
                    if (createRes.Succeeded)
                    {
                        await userManager.AddToRoleAsync(pharmacistUser, "Pharmacist");
                        logger?.LogInformation("Development mode: Pharmacist test account seeded successfully.");
                    }
                    else
                    {
                        logger?.LogWarning("Failed to seed Pharmacist user: {Errors}", string.Join(", ", createRes.Errors.Select(e => e.Description)));
                    }
                }
            }
        }

        // Ensure administrator account has lockout enabled for defense-in-depth brute-force protection.
        // Failed-attempt counters and lockout state must survive application restarts in every environment.
        const string adminEmail = "admin@medicare.com";
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin != null)
        {
            if (!existingAdmin.LockoutEnabled)
            {
                await userManager.SetLockoutEnabledAsync(existingAdmin, true);
            }
        }

        // If database already initialized, ensure doctor profile photos and pending admin approvals exist
        if (await context.Specializations.AnyAsync() && await context.Users.AnyAsync())
        {
            await EnsureDoctorPhotosAndPendingDoctorAsync(context, userManager, configuration, isDevelopment);
            return;
        }

        if (isDevelopment && string.IsNullOrWhiteSpace(defaultPassword))
        {
            throw new InvalidOperationException("Set Seed:DefaultPassword in user-secrets before seeding a fresh Development database.");
        }

        string? adminPassword = null;
        if (isDevelopment)
        {
            adminPassword = configuration["Seed:AdminPassword"] ?? defaultPassword;
        }
        else
        {
            adminPassword = configuration["Seed:AdminPassword"] ?? configuration["SEED_ADMIN_PASSWORD"];
            if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 16)
            {
                logger?.LogCritical("Critical Security Failure: Missing required 'Seed:AdminPassword' configuration in Production, or password is less than 16 characters.");
                throw new InvalidOperationException("Critical Security Failure: Cannot seed administrator account in Production without a strong configured password of at least 16 characters. Please set 'Seed:AdminPassword' via environment variable or secret manager.");
            }
        }

        // 1. Seed Roles
        string[] roles = { "Admin", "Doctor", "Patient" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Seed Administrator
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                PhoneNumber = "+201000000001",
                EmailConfirmed = true,
                LockoutEnabled = true,
                CreatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(adminUser, adminPassword!);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                await userManager.SetLockoutEnabledAsync(adminUser, true);
            }
        }
        else
        {
            if (!adminUser.LockoutEnabled)
            {
                await userManager.SetLockoutEnabledAsync(adminUser, true);
            }
        }

        // 3. Seed 5 Specializations
        var specs = new List<Specialization>
        {
            new() { Name = "Cardiology", Description = "Cardiovascular health, coronary diseases, heart failure, and hypertension management." },
            new() { Name = "Dermatology", Description = "Skin disorders, pediatric dermatology, cosmetic treatments, and rash care." },
            new() { Name = "Pediatrics", Description = "Comprehensive medical care and developmental tracking for infants, children, and adolescents." },
            new() { Name = "Orthopedics", Description = "Musculoskeletal injuries, joints, sports medicine, spine care, and fracture treatment." },
            new() { Name = "General Internal Medicine", Description = "Adult primary care, wellness examinations, chronic disease prevention, and diagnostics." }
        };

        if (!await context.Specializations.AnyAsync())
        {
            await context.Specializations.AddRangeAsync(specs);
            await context.SaveChangesAsync();
        }

        // In Production, only reference lookup data (Roles, Admin, and Specializations) are initialized.
        // Demo accounts (Pharmacist, Doctors, Patients, mock Appointments) are NEVER seeded in Production.
        if (!isDevelopment)
        {
            logger?.LogInformation("Production environment detected: Demo accounts and mock appointments seeding skipped.");
            return;
        }

        var cardiology = await context.Specializations.FirstAsync(s => s.Name == "Cardiology");
        var dermatology = await context.Specializations.FirstAsync(s => s.Name == "Dermatology");
        var pediatrics = await context.Specializations.FirstAsync(s => s.Name == "Pediatrics");
        var orthopedics = await context.Specializations.FirstAsync(s => s.Name == "Orthopedics");
        var internalMed = await context.Specializations.FirstAsync(s => s.Name == "General Internal Medicine");

        // 4. Seed 5 Approved Doctors
        var doctorsData = new[]
        {
            new {
                Email = "ahmed.mahmoud@medicare.com",
                FullName = "Dr. Ahmed Mahmoud",
                Phone = "+201011112222",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-4421",
                Fee = 300.00m,
                Bio = "Senior Consultant in Interventional Cardiology with over 12 years of specialized clinical experience in heart failure and catheterization.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "sara.alsayed@medicare.com",
                FullName = "Dr. Sara Al-Sayed",
                Phone = "+201022223333",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-8892",
                Fee = 250.00m,
                Bio = "Specialist in Clinical & Aesthetic Dermatology with international European fellowship in laser and autoimmune skin therapies.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "youssef.nabil@medicare.com",
                FullName = "Dr. Youssef Nabil",
                Phone = "+201033334444",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2014-1109",
                Fee = 200.00m,
                Bio = "Consultant Pediatrician dedicated to neonatal development, immunizations, and pediatric pulmonary diseases.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }
                }
            },
            new {
                Email = "mona.mansour@medicare.com",
                FullName = "Dr. Mona Mansour",
                Phone = "+201044445555",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2016-5531",
                Fee = 350.00m,
                Bio = "Orthopedic Surgeon specializing in knee arthroscopy, sports injury rehabilitation, and joint reconstructive procedures.",
                Hours = new[] {
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }
                }
            },
            new {
                Email = "tarek.ezzat@medicare.com",
                FullName = "Dr. Tarek Ezzat",
                Phone = "+201055556666",
                SpecId = internalMed.Id,
                License = "EGY-MED-2012-7744",
                Fee = 180.00m,
                Bio = "Internal Medicine Consultant focusing on adult hypertension, diabetes mellitus type 2, and preventive health screenings.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            }
        };

        var doctorsList = new List<Doctor>();
        foreach (var docData in doctorsData)
        {
            var user = await userManager.FindByEmailAsync(docData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = docData.Email,
                    Email = docData.Email,
                    FullName = docData.FullName,
                    PhoneNumber = docData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(user, defaultPassword);
                await userManager.AddToRoleAsync(user, "Doctor");
            }

            var doctor = await context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
            if (doctor == null)
            {
                doctor = new Doctor
                {
                    UserId = user.Id,
                    SpecializationId = docData.SpecId,
                    LicenseNumber = docData.License,
                    ConsultationFee = docData.Fee,
                    SlotDurationMinutes = 30,
                    IsApproved = true,
                    Governorate = "Cairo",
                    Bio = docData.Bio,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Doctors.AddAsync(doctor);
                await context.SaveChangesAsync();

                foreach (var h in docData.Hours)
                {
                    await context.WorkingHours.AddAsync(new WorkingHours
                    {
                        DoctorId = doctor.Id,
                        DayOfWeek = h.Day,
                        StartTime = h.Start,
                        EndTime = h.End,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await context.SaveChangesAsync();
            }
            doctorsList.Add(doctor);
        }

        // Add 1 sample doctor leave
        var firstDoc = doctorsList.First();
        if (!await context.DoctorLeaves.AnyAsync(l => l.DoctorId == firstDoc.Id))
        {
            await context.DoctorLeaves.AddAsync(new DoctorLeave
            {
                DoctorId = firstDoc.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(20),
                EndDate = DateTime.UtcNow.Date.AddDays(24),
                Reason = "Annual Medical Cardiology Conference Attendance",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // 5. Seed 5 Patients
        var patientsData = new[]
        {
            new { Email = "khaled.omar@medicare.com", Name = "Khaled Omar", Phone = "+201099112233", DoB = new DateTime(1988, 4, 12), Gender = "Male", Blood = "A+", Contact = "+201099112234" },
            new { Email = "nourhan.ali@medicare.com", Name = "Nourhan Ali", Phone = "+201088223344", DoB = new DateTime(1995, 9, 25), Gender = "Female", Blood = "O+", Contact = "+201088223345" },
            new { Email = "mostafa.hassan@medicare.com", Name = "Mostafa Hassan", Phone = "+201077334455", DoB = new DateTime(1978, 11, 3), Gender = "Male", Blood = "B+", Contact = "+201077334456" },
            new { Email = "dina.fathy@medicare.com", Name = "Dina Fathy", Phone = "+201066445566", DoB = new DateTime(2001, 2, 18), Gender = "Female", Blood = "AB+", Contact = "+201066445567" },
            new { Email = "mohamed.selim@medicare.com", Name = "Mohamed Selim", Phone = "+201055556677", DoB = new DateTime(1992, 7, 30), Gender = "Male", Blood = "O-", Contact = "+201055556678" }
        };

        var patientsList = new List<Patient>();
        foreach (var pData in patientsData)
        {
            var user = await userManager.FindByEmailAsync(pData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = pData.Email,
                    Email = pData.Email,
                    FullName = pData.Name,
                    PhoneNumber = pData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(user, defaultPassword);
                await userManager.AddToRoleAsync(user, "Patient");
            }

            var patient = await context.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (patient == null)
            {
                patient = new Patient
                {
                    UserId = user.Id,
                    DateOfBirth = pData.DoB,
                    Gender = pData.Gender,
                    BloodGroup = pData.Blood,
                    EmergencyContact = pData.Contact,
                    Allergies = pData.Gender == "Male" ? "Penicillin allergy" : "None known",
                    MedicalHistory = pData.Gender == "Male" ? "Seasonal asthma, mild hypertension" : "No chronic illnesses",
                    CreatedAt = DateTime.UtcNow
                };
                await context.Patients.AddAsync(patient);
                await context.SaveChangesAsync();
            }
            patientsList.Add(patient);
        }

        // 6. Seed 20+ Past Appointments with Medical Records and Prescriptions
        if (!await context.Appointments.AnyAsync())
        {
            var basePastDate = DateTime.UtcNow.Date.AddDays(-14);
            var apptCount = 0;

            for (int i = 0; i < 22; i++)
            {
                var doc = doctorsList[i % doctorsList.Count];
                var pat = patientsList[i % patientsList.Count];
                var date = basePastDate.AddDays(i);
                var startHour = 9 + (i % 6);
                var startTime = new TimeSpan(startHour, 0, 0);
                var endTime = startTime.Add(TimeSpan.FromMinutes(30));

                var appt = new Appointment
                {
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    AppointmentDate = date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = AppointmentStatus.Completed,
                    ConsultationFee = doc.ConsultationFee,
                    PaymentStatus = PaymentStatus.Paid,
                    Type = AppointmentType.Consultation,
                    Notes = $"Routine medical clinical follow-up for visit {i + 1}.",
                    CreatedAt = date.AddHours(-12)
                };
                await context.Appointments.AddAsync(appt);
                await context.SaveChangesAsync();

                var medRecord = new MedicalRecord
                {
                    AppointmentId = appt.Id,
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    Diagnosis = $"Diagnosis summary: Stage {(i % 2) + 1} clinical assessment for {doc.Specialization.Name}.",
                    Symptoms = "Mild recurrent fatigue, elevated blood pressure, localized tension.",
                    VisitNotes = "Patient advised regular hydration, lifestyle modifications, and prescribed medical treatment regimen.",
                    CreatedAt = date.AddMinutes(35)
                };
                await context.MedicalRecords.AddAsync(medRecord);
                await context.SaveChangesAsync();

                var prescription = new Prescription
                {
                    MedicalRecordId = medRecord.Id,
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    PrescriptionDate = date.AddMinutes(35),
                    Notes = "Take all medications strictly as directed. Contact clinic if adverse reactions appear.",
                    CreatedAt = date.AddMinutes(35)
                };
                await context.Prescriptions.AddAsync(prescription);
                await context.SaveChangesAsync();

                var items = new List<PrescriptionItem>
                {
                    new()
                    {
                        PrescriptionId = prescription.Id,
                        MedicationName = "Amoxicillin 500mg",
                        Dosage = "500 mg",
                        Frequency = "Every 8 hours",
                        DurationDays = 7,
                        Instructions = "Take orally after meals with a full glass of water.",
                        CreatedAt = date.AddMinutes(35)
                    },
                    new()
                    {
                        PrescriptionId = prescription.Id,
                        MedicationName = "Panadol Extra",
                        Dosage = "500 mg / 65 mg",
                        Frequency = "Twice daily as needed",
                        DurationDays = 5,
                        Instructions = "For symptomatic pain relief; do not exceed 4000mg paracetamol daily.",
                        CreatedAt = date.AddMinutes(35)
                    }
                };
                await context.PrescriptionItems.AddRangeAsync(items);
                await context.SaveChangesAsync();
                apptCount++;
            }

            // 7. Seed 5 Upcoming Appointments
            var futureDate = DateTime.UtcNow.Date.AddDays(2);
            for (int j = 0; j < 5; j++)
            {
                var doc = doctorsList[j % doctorsList.Count];
                var pat = patientsList[(j + 2) % patientsList.Count];
                var apptDate = futureDate.AddDays(j);
                var startTime = new TimeSpan(10 + j, 0, 0);

                var futureAppt = new Appointment
                {
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    AppointmentDate = apptDate,
                    StartTime = startTime,
                    EndTime = startTime.Add(TimeSpan.FromMinutes(30)),
                    Status = j % 2 == 0 ? AppointmentStatus.Confirmed : AppointmentStatus.Pending,
                    ConsultationFee = doc.ConsultationFee,
                    PaymentStatus = PaymentStatus.Unpaid,
                    Type = AppointmentType.Consultation,
                    Notes = "Pre-booked upcoming consultation.",
                    CreatedAt = DateTime.UtcNow
                };
                await context.Appointments.AddAsync(futureAppt);
            }
            await context.SaveChangesAsync();

            logger?.LogInformation("DbInitializer: Successfully seeded database with Admin, Doctors, Patients, and Appointments.");
        }
    }

    private static async Task EnsureDoctorPhotosAndPendingDoctorAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // Ensure all 14 clinical specialties exist in database
        var allSpecs = new (string Name, string Description)[]
        {
            ("Cardiology", "Cardiovascular health, coronary diseases, heart failure, and hypertension management."),
            ("Dermatology", "Skin disorders, pediatric dermatology, aesthetic laser treatments, and rash care."),
            ("Pediatrics", "Comprehensive medical care, developmental tracking, and neonatology for infants and children."),
            ("Orthopedics", "Musculoskeletal injuries, joints, sports medicine, spine surgery, and fracture treatment."),
            ("General Internal Medicine", "Adult primary care, wellness examinations, chronic disease prevention, and diagnostics."),
            ("Ophthalmology", "Comprehensive eye care, cataract surgery, refractive LASIK, and vitreoretinal treatments."),
            ("Obstetrics & Gynecology", "Women's healthcare, prenatal and high-risk pregnancy monitoring, and fertility treatments."),
            ("Neurology", "Disorders of the central and peripheral nervous system, epilepsy, migraines, and stroke care."),
            ("ENT / Ear, Nose & Throat", "Ear infections, sinus endoscopy, throat disorders, and audiology."),
            ("General Surgery", "Laparoscopic procedures, tumor surgery, digestive care, and hernia repairs."),
            ("Dentistry", "Oral and maxillofacial surgery, aesthetic dental implants, orthodontics, and cosmetic veneers."),
            ("Urology", "Kidney stone laser lithotripsy, prostate treatment, and male reproductive health."),
            ("Pulmonology", "Respiratory tract diseases, asthma, chronic obstructive pulmonary disease (COPD), and allergy."),
            ("Psychiatry", "Behavioral health, anxiety, mood disorders, depression therapy, and clinical psychotherapy.")
        };

        foreach (var s in allSpecs)
        {
            if (!await context.Specializations.AnyAsync(e => e.Name == s.Name))
            {
                await context.Specializations.AddAsync(new Specialization { Name = s.Name, Description = s.Description });
            }
        }
        await context.SaveChangesAsync();

        var cardiology = await context.Specializations.FirstAsync(s => s.Name == "Cardiology");
        var dermatology = await context.Specializations.FirstAsync(s => s.Name == "Dermatology");
        var pediatrics = await context.Specializations.FirstAsync(s => s.Name == "Pediatrics");
        var orthopedics = await context.Specializations.FirstAsync(s => s.Name == "Orthopedics");
        var internalMed = await context.Specializations.FirstAsync(s => s.Name == "General Internal Medicine");
        var ophthalmology = await context.Specializations.FirstAsync(s => s.Name == "Ophthalmology");
        var obGyn = await context.Specializations.FirstAsync(s => s.Name == "Obstetrics & Gynecology");
        var neurology = await context.Specializations.FirstAsync(s => s.Name == "Neurology");
        var ent = await context.Specializations.FirstAsync(s => s.Name == "ENT / Ear, Nose & Throat");
        var surgery = await context.Specializations.FirstAsync(s => s.Name == "General Surgery");
        var dentistry = await context.Specializations.FirstAsync(s => s.Name == "Dentistry");
        var urology = await context.Specializations.FirstAsync(s => s.Name == "Urology");
        var pulmonology = await context.Specializations.FirstAsync(s => s.Name == "Pulmonology");
        var psychiatry = await context.Specializations.FirstAsync(s => s.Name == "Psychiatry");

        // In Production, do not update demo doctors or seed pending demo doctor accounts
        if (!isDevelopment)
        {
            return;
        }

        var defaultPassword = configuration["Seed:DefaultPassword"] ?? configuration["SEED_DEFAULT_PASSWORD"];
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            return;
        }

        var fullDoctorsCohort = new[]
        {
            new {
                Email = "ahmed.mahmoud@medicare.com",
                FullName = "Dr. Ahmed Mahmoud",
                Phone = "+201011112222",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-4421",
                Fee = 350.00m,
                Photo = "/images/doctors/doc-1.jpg",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة التداخلية بكلية الطب جامعة عين شمس، زميل جمعية القلب الأمريكية. عيادة المعادي، شارع النصر، القاهرة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "mona.mansour@medicare.com",
                FullName = "Dr. Mona Mansour",
                Phone = "+201044445555",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2016-5531",
                Fee = 400.00m,
                Photo = "/images/doctors/doc-53.jpg",
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل وإصابات الملاعب، كلية الطب القصر العيني. التجمع الخامس، شارع التسعين، القاهرة الجديدة.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "reham.abdelaziz@medicare.com",
                FullName = "Dr. Reham Abdel-Aziz",
                Phone = "+201088889999",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1002",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-54.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة والتغذية السريرية، البورد العربي في طب الأطفال. ميدان روكسي، مصر الجديدة، القاهرة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "hossam.elmaghraby@medicare.com",
                FullName = "Dr. Hossam El-Maghraby",
                Phone = "+201022114455",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1003",
                Fee = 380.00m,
                Photo = "/images/doctors/doc-2.jpg",
                Bio = "استشاري جراحة العظام والعمود الفقري وإصابات الملاعب، زميل الجمعية السويسرية لجراحة العظام (AO). ميدان مصطفى محمود، المهندسين، الجيزة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) } }
            },
            new {
                Email = "dalia.farouk@medicare.com",
                FullName = "Dr. Dalia Farouk",
                Phone = "+201033221144",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1004",
                Fee = 300.00m,
                Photo = "/images/doctors/doc-55.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، زمالة الأكاديمية الأمريكية للأمراض الجلدية (AAD). مجمع زايد الطبي، الشيخ زايد، الجيزة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "amr.elshennawy@medicare.com",
                FullName = "Dr. Amr El-Shennawy",
                Phone = "+201055443322",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1005",
                Fee = 340.00m,
                Photo = "/images/doctors/doc-3.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة الشرايين، كلية الطب جامعة القاهرة. شارع مصدق، الدقي، الجيزة.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "sara.alsayed@medicare.com",
                FullName = "Dr. Sara Al-Sayed",
                Phone = "+201022223333",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-8892",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-56.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، زمالة الأكاديمية الأوروبية للأمراض الجلدية (EADV). سموحة، ميدان فيكتور عمانويل، الإسكندرية.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "youssef.nabil@medicare.com",
                FullName = "Dr. Youssef Nabil",
                Phone = "+201033334444",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2014-1109",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-4.jpg",
                Bio = "استشاري أول طب الأطفال وحديثي الولادة وأمراض الجهاز التنفسي والحساسية، جامعة الإسكندرية. طريق الحرية، لوران، الإسكندرية.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) } }
            },
            new {
                Email = "sherif.ghoneim@medicare.com",
                FullName = "Dr. Sherif Ghoneim",
                Phone = "+201088776655",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1008",
                Fee = 320.00m,
                Photo = "/images/doctors/doc-5.jpg",
                Bio = "استشاري أمراض القلب وقصور الشرايين التاجية والقسطرة، كلية الطب جامعة الإسكندرية. شارع المشير أحمد إسماعيل، سيدي جابر، الإسكندرية.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "nourhan.elshazly@medicare.com",
                FullName = "Dr. Nourhan El-Shazly",
                Phone = "+201066667777",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1009",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-57.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقصور الشرايين التاجية، جامعة المنصورة. المشاية السفلية، أمام نادي جزيرة الورد، المنصورة.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "mohamed.eldesouky@medicare.com",
                FullName = "Dr. Mohamed El-Desouky",
                Phone = "+201077778888",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1010",
                Fee = 320.00m,
                Photo = "/images/doctors/doc-6.jpg",
                Bio = "أستاذ أمراض الباطنة والكلى وارتفاع ضغط الدم، مركز الكلى والمسالك البولية بالمنصورة. شارع الجمهورية، المنصورة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "rania.eldeeb@medicare.com",
                FullName = "Dr. Rania El-Deeb",
                Phone = "+201099887766",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1011",
                Fee = 220.00m,
                Photo = "/images/doctors/doc-58.jpg",
                Bio = "استشاري طب الأطفال والسكر والغدد الصماء لدى الأطفال، مستشفى الأطفال الجامعي بالمنصورة. حي توريل، شارع سعد زغلول، المنصورة.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "tarek.ezzat@medicare.com",
                FullName = "Dr. Tarek Ezzat",
                Phone = "+201055556666",
                SpecId = internalMed.Id,
                License = "EGY-MED-2012-7744",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-7.jpg",
                Bio = "استشاري أول أمراض الباطنة العامة والسكر والجهاز الهضمي، كلية الطب جامعة طنطا. شارع النحاس مع المحطة، طنطا.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "marwa.elganzoury@medicare.com",
                FullName = "Dr. Marwa El-Ganzoury",
                Phone = "+201044332211",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1013",
                Fee = 240.00m,
                Photo = "/images/doctors/doc-59.jpg",
                Bio = "استشاري الأمراض الجلدية وتجميل الجلد والليزر، ماجستير الأمراض الجلدية جامعة طنطا. شارع الجيش، أمام مستشفى الجامعة، طنطا.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "ahmed.elbanna@medicare.com",
                FullName = "Dr. Ahmed El-Banna",
                Phone = "+201011224466",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1014",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-8.jpg",
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل، جامعة طنطا. ميدان الساعة، برج الأطباء، طنطا.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "essam.badawy@medicare.com",
                FullName = "Dr. Essam Badawy",
                Phone = "+201099881122",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1015",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-9.jpg",
                Bio = "أستاذ واستشاري أمراض الباطنة العامة وأمراض الكبد والجهاز الهضمي، كلية الطب جامعة أسيوط. شارع يسري راغب، أسيوط.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "menna.elsayed@medicare.com",
                FullName = "Dr. Menna El-Sayed",
                Phone = "+201088771133",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1016",
                Fee = 210.00m,
                Photo = "/images/doctors/doc-60.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة ورعاية المبتسرين، مستشفى الأطفال الجامعي بأسيوط. شارع النميس، برج الأطباء، أسيوط.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "hazem.mostafa@medicare.com",
                FullName = "Dr. Hazem Mostafa",
                Phone = "+201077661144",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1017",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-10.jpg",
                Bio = "استشاري أمراض القلب والقسطرة التداخلية، معهد أورام وأمراض القلب جامعة أسيوط. شارع الجمهورية، أمام المحافظة، أسيوط.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) } }
            },
            new {
                Email = "wael.fouad@medicare.com",
                FullName = "Dr. Wael Fouad",
                Phone = "+201066551155",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1018",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-11.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة الشرايين، كلية الطب جامعة الزقازيق. شارع القومية، برج الأطباء، الزقازيق.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "salma.elnaggar@medicare.com",
                FullName = "Dr. Salma El-Naggar",
                Phone = "+201055441166",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1019",
                Fee = 230.00m,
                Photo = "/images/doctors/doc-61.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر والعلاج الضوئي، جامعة الزقازيق. شارع المحافظة، الزقازيق.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "karim.abdelhameed@medicare.com",
                FullName = "Dr. Karim Abdel-Hameed",
                Phone = "+201044331177",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1020",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-12.jpg",
                Bio = "استشاري الأمراض الباطنية وتنظيم السكري ومقاومة الإنسولين، جامعة الزقازيق. شارع سعد زغلول، الزقازيق.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "hany.mansour@medicare.com",
                FullName = "Dr. Hany Mansour",
                Phone = "+201033221188",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1021",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-13.jpg",
                Bio = "استشاري أول أمراض الباطنة العامة والغدد الصماء، كلية الطب جامعة بنها. شارع فريد ندا، برج الأطباء، بنها.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "heba.soliman@medicare.com",
                FullName = "Dr. Heba Soliman",
                Phone = "+201022111199",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1022",
                Fee = 220.00m,
                Photo = "/images/doctors/doc-62.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة وحساسية الصدر، مستشفى بنها الجامعي. شارع الأهرام، بنها.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "tamer.elwakil@medicare.com",
                FullName = "Dr. Tamer El-Wakil",
                Phone = "+201011001100",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1023",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-14.jpg",
                Bio = "استشاري جراحة العظام ومناظير المفاصل والكسور، جامعة بنها. شارع سعد زغلول، ميدان المحطة، بنها.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) } }
            },
            new {
                Email = "yasser.elgammal@medicare.com",
                FullName = "Dr. Yasser El-Gammal",
                Phone = "+201099112233",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1024",
                Fee = 300.00m,
                Photo = "/images/doctors/doc-15.jpg",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة، كلية الطب جامعة قناة السويس. حي الشيخ زايد، الشارع التجاري، الإسماعيلية.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "inas.elkady@medicare.com",
                FullName = "Dr. Inas El-Kady",
                Phone = "+201088223344",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1025",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-63.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، جامعة قناة السويس. شارع شبين الكوم، الإسماعيلية.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "mahmoud.saber@medicare.com",
                FullName = "Dr. Mahmoud Saber",
                Phone = "+201077334455",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1026",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-16.jpg",
                Bio = "استشاري أمراض الباطنة العامة وأمراض الجهاز الهضمي والكبد، الإسماعيلية. نمرة 6 أمام هيئة قناة السويس، الإسماعيلية.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "khaled.elgendy@medicare.com",
                FullName = "Dr. Khaled El-Gendy",
                Phone = "+201066445566",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1027",
                Fee = 310.00m,
                Photo = "/images/doctors/doc-17.jpg",
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل، بورسعيد. حي الشرق، شارع الجمهورية، بورسعيد.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "mariam.elnahas@medicare.com",
                FullName = "Dr. Mariam El-Nahas",
                Phone = "+201055556677",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1028",
                Fee = 240.00m,
                Photo = "/images/doctors/doc-64.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة والتغذية العلاجية، بورسعيد. شارع الثلاثيني، برج السلام، بورسعيد.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "mohamed.elattar@medicare.com",
                FullName = "Dr. Mohamed El-Attar",
                Phone = "+201044667788",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1029",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-18.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة الشرايين، بورسعيد. شارع محمد علي، بورسعيد.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(21, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(21, 0, 0) } }
            },
            new {
                Email = "mostafa.radwan@medicare.com",
                FullName = "Dr. Mostafa Radwan",
                Phone = "+201033778899",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1030",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-19.jpg",
                Bio = "استشاري أمراض الباطنة العامة والسكر والأمراض الصدرية، السويس. حي الأربعين، ميدان الإسعاف، السويس.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "rasha.elbatal@medicare.com",
                FullName = "Dr. Rasha El-Batal",
                Phone = "+201022889900",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1031",
                Fee = 230.00m,
                Photo = "/images/doctors/doc-65.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، السويس. شارع الجيش، مجمع السويس الطبي، السويس.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "ahmed.elsawy@medicare.com",
                FullName = "Dr. Ahmed El-Sawy",
                Phone = "+201011990011",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1032",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-20.jpg",
                Bio = "استشاري أمراض القلب وقصور الشرايين التاجية، السويس. بورتوفيق، شارع النمساوي، السويس.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) } }
            },
            new {
                Email = "omar.elfarouq@medicare.com",
                FullName = "Dr. Omar El-Farouq",
                Phone = "+201000112244",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1033",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-21.jpg",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة، كلية الطب جامعة سوهاج. حي سيتي، برج النخبة، سوهاج.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "fatma.elzahraa@medicare.com",
                FullName = "Dr. Fatma El-Zahraa",
                Phone = "+201099223355",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1034",
                Fee = 210.00m,
                Photo = "/images/doctors/doc-66.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة وأمراض الدم لدى الأطفال، جامعة سوهاج. شارع 15 مايو، أمام مستشفى الهلال، سوهاج.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "ahmed.abdelmawgood@medicare.com",
                FullName = "Dr. Ahmed Abdel-Mawgoud",
                Phone = "+201088334466",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1035",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-22.jpg",
                Bio = "استشاري جراحة العظام وإصابات المفاصل والكسور، جامعة سوهاج. شارع الجمهورية، أمام مجمع المحاكم، سوهاج.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "walid.kassem@medicare.com",
                FullName = "Dr. Walid Kassem",
                Phone = "+201077445577",
                SpecId = surgery.Id,
                License = "EGY-MED-2025-1036",
                Fee = 300.00m,
                Photo = "/images/doctors/doc-23.jpg",
                Bio = "أستاذ واستشاري الجراحة العامة وجراحة المناظير والأورام، كلية الطب جامعة الفيوم. ميدان السواقي، برج الأطباء، الفيوم.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "shaimaa.elrify@medicare.com",
                FullName = "Dr. Shaimaa El-Rify",
                Phone = "+201066556688",
                SpecId = ophthalmology.Id,
                License = "EGY-MED-2025-1037",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-67.jpg",
                Bio = "استشاري طب وجراحة العيون وتصحيح الإبصار بالليزك والمياه البيضاء، جامعة الفيوم. حي دلة، شارع أحمد شوقي، الفيوم.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "medhat.elkashef@medicare.com",
                FullName = "Dr. Medhat El-Kashef",
                Phone = "+201055667799",
                SpecId = pulmonology.Id,
                License = "EGY-MED-2025-1038",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-24.jpg",
                Bio = "استشاري أمراض الصدر والجهاز التنفسي والحساسية واضطرابات النوم، جامعة الفيوم. حي المسلة، شارع بطل السلام، الفيوم.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "ehab.mowafi@medicare.com",
                FullName = "Dr. Ehab Mowafi",
                Phone = "+201044778800",
                SpecId = neurology.Id,
                License = "EGY-MED-2025-1039",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-25.jpg",
                Bio = "أستاذ واستشاري أمراض المخ والأعصاب والسكتة الدماغية، كلية الطب جامعة بني سويف. شارع عبد السلام عارف، برج النيل، بني سويف.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "noha.elsayed@medicare.com",
                FullName = "Dr. Noha El-Sayed",
                Phone = "+201033889911",
                SpecId = obGyn.Id,
                License = "EGY-MED-2025-1040",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-68.jpg",
                Bio = "استشاري أمراض النساء والتوليد والحقن المجهري وجراحات المناظير، جامعة بني سويف. ميدان الزراعيين، برج الصفا، بني سويف.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "tarek.kamel@medicare.com",
                FullName = "Dr. Tarek Kamel",
                Phone = "+201022990022",
                SpecId = dentistry.Id,
                License = "EGY-MED-2025-1041",
                Fee = 230.00m,
                Photo = "/images/doctors/doc-26.jpg",
                Bio = "استشاري جراحة الفم والأسنان وزراعة وتجميل الأسنان، بني سويف. حي الناصرية، شارع بورسعيد، بني سويف.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "samer.malak@medicare.com",
                FullName = "Dr. Samer Malak",
                Phone = "+201011001133",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1042",
                Fee = 310.00m,
                Photo = "/images/doctors/doc-27.jpg",
                Bio = "أستاذ واستشاري أمراض القلب وقسطرة الشرايين، كلية الطب جامعة المنيا. كورنيش النيل، مجمع حورس الطبي، المنيا.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "basma.abdelhamid@medicare.com",
                FullName = "Dr. Basma Abdel-Hamid",
                Phone = "+201000112244",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1043",
                Fee = 240.00m,
                Photo = "/images/doctors/doc-69.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل البشرة، جامعة المنيا. ميدان بالاس، شارع التجارة، المنيا.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "ashraf.elminiawy@medicare.com",
                FullName = "Dr. Ashraf El-Miniawy",
                Phone = "+201099223355",
                SpecId = urology.Id,
                License = "EGY-MED-2025-1044",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-28.jpg",
                Bio = "استشاري جراحة المسالك البولية وتفتيت الحصوات بالليزر وأمراض الذكورة، جامعة المنيا. شارع طه حسين، برج الأطباء، المنيا.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "gamal.elqinawy@medicare.com",
                FullName = "Dr. Gamal El-Qinawy",
                Phone = "+201088334466",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1045",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-29.jpg",
                Bio = "استشاري جراحة العظام والعمود الفقري والكسور المعقدة، كلية الطب جامعة جنوب الوادي. شارع مصطفى كامل، ميدان المحطة، قنا.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "asmaa.elhawary@medicare.com",
                FullName = "Dr. Asmaa El-Hawary",
                Phone = "+201077445577",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1046",
                Fee = 210.00m,
                Photo = "/images/doctors/doc-70.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة وأمراض الجهاز الهضمي للأطفال، مستشفى قنا الجامعي. ميدان الساعة، برج قنا الطبي، قنا.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "mahmoud.aboulmagd@medicare.com",
                FullName = "Dr. Mahmoud Aboul-Magd",
                Phone = "+201066556688",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1047",
                Fee = 240.00m,
                Photo = "/images/doctors/doc-30.jpg",
                Bio = "استشاري أمراض الباطنة العامة والسكري وارتفاع ضغط الدم، قنا. شارع 23 يوليو، أمام نادي المعلمين، قنا.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "alaa.youssef@medicare.com",
                FullName = "Dr. Alaa El-Din Youssef",
                Phone = "+201055667799",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1048",
                Fee = 320.00m,
                Photo = "/images/doctors/doc-31.jpg",
                Bio = "أستاذ واستشاري أمراض القلب وقسطرة الشرايين، كلية الطب جامعة الأقصر. شارع التلفزيون، برج حتحور، الأقصر.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "shereen.elshazly@medicare.com",
                FullName = "Dr. Shereen El-Shazly",
                Phone = "+201044778800",
                SpecId = ophthalmology.Id,
                License = "EGY-MED-2025-1049",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-71.jpg",
                Bio = "استشاري طب وجراحة العيون والفيمتو ليزك وزرع العدسات، الأقصر. منطقة العوامية، طريق الكورنيش، الأقصر.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "remon.nashed@medicare.com",
                FullName = "Dr. Remon Nashed",
                Phone = "+201033889911",
                SpecId = dentistry.Id,
                License = "EGY-MED-2025-1050",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-32.jpg",
                Bio = "استشاري جراحة الفم وزراعة وتجميل الأسنان، الأقصر. شارع المنشية، برج الأقصر الدولي، الأقصر.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "hesham.nouh@medicare.com",
                FullName = "Dr. Hesham Nouh",
                Phone = "+201022990022",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1051",
                Fee = 340.00m,
                Photo = "/images/doctors/doc-33.jpg",
                Bio = "استشاري أمراض القلب والقسطرة، كلية الطب جامعة أسوان ومركز مجدي يعقوب للقلب. كورنيش النيل، مجمع أسوان للقلب، أسوان.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "rehab.elkhatib@medicare.com",
                FullName = "Dr. Rehab El-Khatib",
                Phone = "+201011001133",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1052",
                Fee = 230.00m,
                Photo = "/images/doctors/doc-72.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، جامعة أسوان. شارع أبطال السيل، برج النخيل، أسوان.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "mina.safwat@medicare.com",
                FullName = "Dr. Mina Safwat",
                Phone = "+201000112244",
                SpecId = surgery.Id,
                License = "EGY-MED-2025-1053",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-34.jpg",
                Bio = "استشاري الجراحة العامة ومناظير الجهاز الهضمي والفتق الجراحي، مستشفى أسوان الجامعي. شارع كسر الحجر، أمام المستشفى الجامعي، أسوان.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "ayman.elgammal@medicare.com",
                FullName = "Dr. Ayman El-Gammal",
                Phone = "+201099223355",
                SpecId = ent.Id,
                License = "EGY-MED-2025-1054",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-35.jpg",
                Bio = "أستاذ واستشاري جراحة الأنف والأذن والحنجرة ومناظير الجيوب الأنفية، كلية الطب جامعة المنوفية. شارع صبري أبو علم، برج الأطباء، شبين الكوم، المنوفية.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "dina.abdelsalam@medicare.com",
                FullName = "Dr. Dina Abdel-Salam",
                Phone = "+201088334466",
                SpecId = obGyn.Id,
                License = "EGY-MED-2025-1055",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-73.jpg",
                Bio = "استشاري أمراض النساء والتوليد وجراحات المناظير والحقن المجهري، جامعة المنوفية. شارع الجلاء البحري، شبين الكوم، المنوفية.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "bahaa.hegazy@medicare.com",
                FullName = "Dr. Bahaa El-Din Hegazy",
                Phone = "+201077445577",
                SpecId = neurology.Id,
                License = "EGY-MED-2025-1056",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-36.jpg",
                Bio = "استشاري أمراض المخ والأعصاب والطب النفسي ورسم المخ، جامعة المنوفية. شارع جمال عبد الناصر، شبين الكوم، المنوفية.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "emad.elwakil@medicare.com",
                FullName = "Dr. Emad El-Wakil",
                Phone = "+201066556688",
                SpecId = urology.Id,
                License = "EGY-MED-2025-1057",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-37.jpg",
                Bio = "استشاري أول جراحة المسالك البولية ومناظير الكلى وتفتيت الحصوات، دمنهور. ميدان الساعة، برج الفيروز، دمنهور، البحيرة.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "abeer.abdelbary@medicare.com",
                FullName = "Dr. Abeer Abdel-Bary",
                Phone = "+201055667799",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1058",
                Fee = 230.00m,
                Photo = "/images/doctors/doc-74.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة وأمراض حساسية الصدر، دمنهور. شارع عبد السلام الشاذلي، أمام المحافظة، دمنهور، البحيرة.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "adel.hamed@medicare.com",
                FullName = "Dr. Adel Hamed",
                Phone = "+201044778800",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1059",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-38.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة القلب، المعهد الطبي بدمنهور. شارع الروضة، برج دمنهور الطبي، دمنهور، البحيرة.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "magdy.elsherbiny@medicare.com",
                FullName = "Dr. Magdy El-Sherbiny",
                Phone = "+201033889911",
                SpecId = surgery.Id,
                License = "EGY-MED-2025-1060",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-39.jpg",
                Bio = "أستاذ واستشاري الجراحة العامة ومناظير البطن وجراحات الغدة الدرقية، كلية الطب جامعة كفر الشيخ. شارع الخليفة المأمون، برج المحاربين، كفر الشيخ.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "ghada.mansour@medicare.com",
                FullName = "Dr. Ghada Mansour",
                Phone = "+201022990022",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1061",
                Fee = 220.00m,
                Photo = "/images/doctors/doc-75.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، جامعة كفر الشيخ. حي الصوالحة، شارع النبوي المهندس، كفر الشيخ.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "raafat.elmaghraby@medicare.com",
                FullName = "Dr. Raafat El-Maghraby",
                Phone = "+201011001133",
                SpecId = pulmonology.Id,
                License = "EGY-MED-2025-1062",
                Fee = 250.00m,
                Photo = "/images/doctors/doc-40.jpg",
                Bio = "استشاري أمراض الصدر والحساسية والسدة الرئوية، كفر الشيخ. مجمع مواقف كفر الشيخ، برج الجامعة، كفر الشيخ.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "saeed.elattar@medicare.com",
                FullName = "Dr. Saeed El-Attar",
                Phone = "+201000112244",
                SpecId = dentistry.Id,
                License = "EGY-MED-2025-1063",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-41.jpg",
                Bio = "استشاري جراحة الفم والأسنان وزراعة وتجميل الأسنان، كلية طب الأسنان جامعة الأزهر بدمياط. ميدان سرور، برج الأطباء، دمياط.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "lamia.elkafrawy@medicare.com",
                FullName = "Dr. Lamia El-Kafrawy",
                Phone = "+201099223355",
                SpecId = obGyn.Id,
                License = "EGY-MED-2025-1064",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-76.jpg",
                Bio = "استشاري أمراض النساء والتوليد والحقن المجهري ومتابعة الحمل الحرج، دمياط. شارع صلاح سالم، رأس البر، دمياط.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "hamdy.elgammal@medicare.com",
                FullName = "Dr. Hamdy El-Gammal",
                Phone = "+201088334466",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1065",
                Fee = 300.00m,
                Photo = "/images/doctors/doc-42.jpg",
                Bio = "استشاري جراحة العظام والمفاصل الصناعية ومناظير الركبة، دمياط. شارع كورنيش النيل الأعظم، دمياط.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "michael.samy@medicare.com",
                FullName = "Dr. Michael Samy",
                Phone = "+201077445577",
                SpecId = psychiatry.Id,
                License = "EGY-MED-2025-1066",
                Fee = 350.00m,
                Photo = "/images/doctors/doc-43.jpg",
                Bio = "استشاري الطب النفسي والعلاج السلوكي المعرفي والاستشارات الأسرية، الغردقة. حي الكوثر، طريق القرى السياحية، الغردقة، البحر الأحمر.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "dalia.nassar@medicare.com",
                FullName = "Dr. Dalia Nassar",
                Phone = "+201066556688",
                SpecId = dermatology.Id,
                License = "EGY-MED-2025-1067",
                Fee = 310.00m,
                Photo = "/images/doctors/doc-77.jpg",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، مستشفى الغردقة العام. ميدان السقالة، مجمع النخيل الطبي، الغردقة، البحر الأحمر.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "peter.shenouda@medicare.com",
                FullName = "Dr. Peter Shenouda",
                Phone = "+201055667799",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1068",
                Fee = 360.00m,
                Photo = "/images/doctors/doc-44.jpg",
                Bio = "استشاري جراحة العظام وإصابات الملاعب والمفاصل، منتجع الجونة. منتجع الجونة، المارينا، الغردقة، البحر الأحمر.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "karim.abdelmoneim@medicare.com",
                FullName = "Dr. Karim Abdel-Moneim",
                Phone = "+201044778800",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1069",
                Fee = 350.00m,
                Photo = "/images/doctors/doc-45.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة الشرايين، مستشفى شرم الشيخ الدولي. هضبة أم السيد، مجمع السلام الطبي، شرم الشيخ، جنوب سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "nada.elshawarby@medicare.com",
                FullName = "Dr. Nada El-Shawarby",
                Phone = "+201033889911",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1070",
                Fee = 290.00m,
                Photo = "/images/doctors/doc-78.jpg",
                Bio = "استشاري أمراض الباطنة العامة وأمراض الجهاز الهضمي والسكري، شرم الشيخ. خليج نعمة، طريق السلام، شرم الشيخ، جنوب سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "george.wadie@medicare.com",
                FullName = "Dr. George Wadie",
                Phone = "+201022990022",
                SpecId = ent.Id,
                License = "EGY-MED-2025-1071",
                Fee = 320.00m,
                Photo = "/images/doctors/doc-46.jpg",
                Bio = "استشاري جراحة الأنف والأذن والحنجرة ومناظير الجيوب الأنفية، شرم الشيخ. حي النور، المجمع الطبي الدولي، شرم الشيخ، جنوب سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "moamen.elarishy@medicare.com",
                FullName = "Dr. Moamen El-Arishy",
                Phone = "+201011001133",
                SpecId = surgery.Id,
                License = "EGY-MED-2025-1072",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-47.jpg",
                Bio = "أستاذ واستشاري الجراحة العامة وجراحة المناظير، كلية الطب جامعة العريش. شارع 23 يوليو، ميدان الرفاعي، العريش، شمال سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "heba.elshennawy@medicare.com",
                FullName = "Dr. Heba El-Shennawy",
                Phone = "+201000112244",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1073",
                Fee = 220.00m,
                Photo = "/images/doctors/doc-79.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة وأمراض النمو، جامعة العريش. حي المساعيد، شارع البحر، العريش، شمال سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "ziad.elboushi@medicare.com",
                FullName = "Dr. Ziad El-Boushi",
                Phone = "+201099223355",
                SpecId = ophthalmology.Id,
                License = "EGY-MED-2025-1074",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-48.jpg",
                Bio = "استشاري طب وجراحة العيون والفيمتو ليزك وعلاج أمراض الشبكية، العريش. شارع الفاتح، أمام مجمع المصالح، العريش، شمال سيناء.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
            new {
                Email = "farag.elmatrouhy@medicare.com",
                FullName = "Dr. Farag El-Matrouhy",
                Phone = "+201088334466",
                SpecId = cardiology.Id,
                License = "EGY-MED-2025-1075",
                Fee = 300.00m,
                Photo = "/images/doctors/doc-49.jpg",
                Bio = "استشاري أمراض القلب وقصور الشرايين التاجية، مستشفى مطروح العام. شارع الإسكندرية، برج اللؤلؤة، مرسى مطروح.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "nesreen.elhawary@medicare.com",
                FullName = "Dr. Nesreen El-Hawary",
                Phone = "+201077445577",
                SpecId = obGyn.Id,
                License = "EGY-MED-2025-1076",
                Fee = 280.00m,
                Photo = "/images/doctors/doc-80.jpg",
                Bio = "استشاري أمراض النساء والتوليد وجراحات المناظير ومتابعة الحمل الحرج، مرسى مطروح. شارع الجلاء، كورنيش مطروح، مرسى مطروح.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "saber.badran@medicare.com",
                FullName = "Dr. Saber Badran",
                Phone = "+201066556688",
                SpecId = internalMed.Id,
                License = "EGY-MED-2025-1077",
                Fee = 320.00m,
                Photo = "/images/doctors/doc-50.jpg",
                Bio = "استشاري أمراض الباطنة العامة والسكري والرعاية الحرجة، مارينا الساحل الشمالي. مارينا، بوابة 2، الساحل الشمالي، مطروح.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) } }
            },
            new {
                Email = "mamdouh.elwadawy@medicare.com",
                FullName = "Dr. Mamdouh El-Wadawy",
                Phone = "+201055667799",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2025-1078",
                Fee = 270.00m,
                Photo = "/images/doctors/doc-51.jpg",
                Bio = "أستاذ واستشاري جراحة العظام والعمود الفقري ومناظير المفاصل، الوادي الجديد. شارع جمال عبد الناصر، ميدان الشعلة، الخارجة، الوادي الجديد.",
                Hours = new[] { new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }, new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) } }
            },
            new {
                Email = "naglaa.saad@medicare.com",
                FullName = "Dr. Naglaa Saad",
                Phone = "+201044778800",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2025-1079",
                Fee = 210.00m,
                Photo = "/images/doctors/doc-81.jpg",
                Bio = "استشاري طب الأطفال وحديثي الولادة ورعاية المبتسرين، مستشفى الخارجة التخصصي. شارع النبوي المهندس، مجمع الأمل الطبي، الخارجة، الوادي الجديد.",
                Hours = new[] { new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }, new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) } }
            },
            new {
                Email = "mohsen.abdelhady@medicare.com",
                FullName = "Dr. Mohsen Abdel-Hady",
                Phone = "+201033889911",
                SpecId = urology.Id,
                License = "EGY-MED-2025-1080",
                Fee = 260.00m,
                Photo = "/images/doctors/doc-52.jpg",
                Bio = "استشاري جراحة المسالك البولية والتناسلية وتفتيت الحصوات بالليزر، الوادي الجديد. حي المروة، أمام مستشفى الخارجة العام، الوادي الجديد.",
                Hours = new[] { new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }, new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) } }
            },
        };

        foreach (var dData in fullDoctorsCohort)
        {
            var user = await userManager.FindByEmailAsync(dData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = dData.Email,
                    Email = dData.Email,
                    FullName = dData.FullName,
                    PhoneNumber = dData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                var userCreateRes = await userManager.CreateAsync(user, defaultPassword);
                if (userCreateRes.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Doctor");
                }
            }

            var doctor = await context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id)
                         ?? await context.Doctors.FirstOrDefaultAsync(d => d.LicenseNumber == dData.License);
            if (doctor == null)
            {
                doctor = new Doctor
                {
                    UserId = user.Id,
                    SpecializationId = dData.SpecId,
                    LicenseNumber = dData.License,
                    ConsultationFee = dData.Fee,
                    SlotDurationMinutes = 30,
                    IsApproved = true,
                    Governorate = ExtractGovernorateFromBio(dData.Bio),
                    ProfileImageUrl = dData.Photo,
                    Bio = dData.Bio,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Doctors.AddAsync(doctor);
                await context.SaveChangesAsync();

                foreach (var h in dData.Hours)
                {
                    await context.WorkingHours.AddAsync(new WorkingHours
                    {
                        DoctorId = doctor.Id,
                        DayOfWeek = h.Day,
                        StartTime = h.Start,
                        EndTime = h.End,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await context.SaveChangesAsync();
            }
            else
            {
                // Update photo, bio, spec & consultation fee if updated
                doctor.UserId = user.Id;
                doctor.ProfileImageUrl = dData.Photo;
                doctor.Bio = dData.Bio;
                doctor.ConsultationFee = dData.Fee;
                doctor.SpecializationId = dData.SpecId;
                doctor.Governorate = ExtractGovernorateFromBio(dData.Bio);
                doctor.IsApproved = true;
                context.Doctors.Update(doctor);
            }
        }

        // Backfill patient Allergies and MedicalHistory if null
        var existingPatients = await context.Patients.Where(p => p.Allergies == null || p.MedicalHistory == null).ToListAsync();
        foreach (var p in existingPatients)
        {
            if (p.Allergies == null) p.Allergies = p.Gender == "Male" ? "Penicillin allergy" : "None known";
            if (p.MedicalHistory == null) p.MedicalHistory = p.Gender == "Male" ? "Seasonal asthma, mild hypertension" : "No chronic illnesses";
        }

        // Ensure at least 1 pending doctor exists for Admin Approval demo
        if (!await context.Doctors.AnyAsync(d => !d.IsApproved))
        {
            const string pendingEmail = "kareem.zaki@medicare.com";
            var pendingUser = await userManager.FindByEmailAsync(pendingEmail);
            if (pendingUser == null)
            {
                pendingUser = new ApplicationUser
                {
                    UserName = pendingEmail,
                    Email = pendingEmail,
                    FullName = "Dr. Kareem Zaki",
                    PhoneNumber = "+201066778899",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(pendingUser, defaultPassword);
                await userManager.AddToRoleAsync(pendingUser, "Doctor");
            }

            var cardSpec = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Cardiology");
            var pendingDoc = new Doctor
            {
                UserId = pendingUser.Id,
                SpecializationId = cardSpec?.Id ?? cardiology.Id,
                LicenseNumber = "EGY-MED-2024-9988",
                ConsultationFee = 250.00m,
                SlotDurationMinutes = 30,
                IsApproved = false,
                Governorate = "Port Said",
                ProfileImageUrl = "/images/doctors/doc-1.jpg",
                Bio = "استشاري أمراض القلب والأوعية الدموية، متقدم بطلب الاعتماد السريري بشبكة عيادات ميدي كير. بورسعيد.",
                CreatedAt = DateTime.UtcNow
            };
            await context.Doctors.AddAsync(pendingDoc);
        }

        await context.SaveChangesAsync();
    }

    private static string ExtractGovernorateFromBio(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio)) return "Cairo";
        if (bio.Contains("الجيزة") || bio.Contains("الشيخ زايد") || bio.Contains("الدقي") || bio.Contains("المهندسين") || bio.Contains("أكتوبر")) return "Giza";
        if (bio.Contains("الإسكندرية") || bio.Contains("سموحة") || bio.Contains("محرم بك")) return "Alexandria";
        if (bio.Contains("المنصورة") || bio.Contains("الدقهلية")) return "Dakahlia";
        if (bio.Contains("طنطا") || bio.Contains("الغربية") || bio.Contains("المحلة")) return "Gharbia";
        if (bio.Contains("المنوفية") || bio.Contains("شبين الكوم")) return "Monufia";
        if (bio.Contains("البحيرة") || bio.Contains("دمنهور")) return "Beheira";
        if (bio.Contains("كفر الشيخ")) return "Kafr El Sheikh";
        if (bio.Contains("دمياط")) return "Damietta";
        if (bio.Contains("الشرقية") || bio.Contains("الزقازيق")) return "Sharqia";
        if (bio.Contains("بورسعيد")) return "Port Said";
        if (bio.Contains("الإسماعيلية")) return "Ismailia";
        if (bio.Contains("السويس")) return "Suez";
        if (bio.Contains("الفيوم")) return "Faiyum";
        if (bio.Contains("بني سويف")) return "Beni Suef";
        if (bio.Contains("المنيا")) return "Minya";
        if (bio.Contains("أسيوط")) return "Assiut";
        if (bio.Contains("سوهاج")) return "Sohag";
        if (bio.Contains("قنا")) return "Qena";
        if (bio.Contains("الأقصر")) return "Luxor";
        if (bio.Contains("أسوان")) return "Aswan";
        if (bio.Contains("البحر الأحمر") || bio.Contains("الغردقة")) return "Red Sea";
        if (bio.Contains("مطروح")) return "Matrouh";
        if (bio.Contains("جنوب سيناء") || bio.Contains("شرم الشيخ")) return "South Sinai";
        if (bio.Contains("شمال سيناء") || bio.Contains("العريش")) return "North Sinai";
        if (bio.Contains("الوادي الجديد")) return "New Valley";
        if (bio.Contains("القليوبية") || bio.Contains("بنها")) return "Qalyubia";
        return "Cairo";
    }
}
