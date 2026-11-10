using FluentValidation;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Infrastructure & Clock
        services.AddSingleton<IClinicClock, ClinicClock>();

        // Domain Factories
        services.AddSingleton<AppointmentFactory>();
        services.AddSingleton<NotificationFactory>();

        // Application Business Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IDoctorReviewService, DoctorReviewService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<ISlotEngineService, SlotEngineService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAppointmentService, AppointmentService>();

        // Sprint 3 Services
        services.AddScoped<IFileStorageService, FileStorageService>();

        // Communication & Notifications (Pluggable Cloud Providers):
        // Dynamically selects SendGridEmailService when SendGrid is enabled with an API key,
        // or falls back seamlessly to MailKitEmailService (SMTP).
        services.AddHttpClient<SendGridEmailService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<MailKitEmailService>();
        services.AddScoped<SendGridEmailService>();
        services.AddScoped<IEmailService>(sp =>
        {
            var sendGridOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediCare.Services.Common.SendGridSettings>>().Value;
            if (sendGridOptions.Enabled && !string.IsNullOrWhiteSpace(sendGridOptions.ApiKey))
            {
                return sp.GetRequiredService<SendGridEmailService>();
            }
            return sp.GetRequiredService<MailKitEmailService>();
        });
        services.AddScoped<ISmsService, MockSmsService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<AttachmentStorageMigrationHelper>();

        // Enterprise AI & Payment Services:
        // - IAiAssistantService (OpenAiAssistantService): Primary enterprise assistant engine with patient consent
        //   verification, PII redaction guardrails, prompt injection defenses, and Azure OpenAI failover.
        // - IOpenAiChatbotClient (OpenAiResponsesChatbotClient): Lightweight direct client for structured responses API.
        services.AddScoped<ChatbotService>();
        services.AddHttpClient<IAiAssistantService, OpenAiAssistantService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(25);
        });
        services.AddHttpClient<IOpenAiChatbotClient, OpenAiResponsesChatbotClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddScoped<IChatbotService, AiChatbotService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddHttpClient<IPaymobCheckoutService, PaymobCheckoutService>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<IPaymobRefundService, PaymobRefundService>(client => client.Timeout = TimeSpan.FromSeconds(20));

        // Automated 24-Hour Reminder Services
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();
        services.AddHostedService<MediCare.Services.BackgroundServices.AppointmentReminderBackgroundService>();

        // Enterprise Real-Time Notification Outbox Background Worker
        services.AddHostedService<MediCare.Services.BackgroundServices.NotificationOutboxBackgroundService>();

        // FluentValidation Validators
        services.AddValidatorsFromAssemblyContaining<PatientRegisterValidator>();

        return services;
    }
}
