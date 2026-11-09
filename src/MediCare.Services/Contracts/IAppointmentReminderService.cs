namespace MediCare.Services.Contracts;

public interface IAppointmentReminderService
{
    Task<int> ProcessPendingRemindersAsync(CancellationToken cancellationToken = default);
}
