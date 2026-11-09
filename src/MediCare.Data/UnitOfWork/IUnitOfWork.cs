using MediCare.Data.Entities;
using MediCare.Data.Repositories;

namespace MediCare.Data.UnitOfWork;

public interface IUnitOfWork : IDisposable
{
    IAppointmentRepository Appointments { get; }
    IDoctorRepository Doctors { get; }
    IRepository<Patient> Patients { get; }
    IRepository<Specialization> Specializations { get; }
    IRepository<WorkingHours> WorkingHours { get; }
    IRepository<DoctorLeave> DoctorLeaves { get; }
    IMedicalRecordRepository MedicalRecords { get; }
    IPrescriptionRepository Prescriptions { get; }
    IRepository<PrescriptionItem> PrescriptionItems { get; }
    IRepository<Notification> Notifications { get; }
    IRepository<NotificationOutbox> NotificationOutbox { get; }

    Task<int> CommitAsync();
    Task<IDatabaseTransaction?> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<IAsyncDisposable?> AcquireApplicationLockAsync(string lockKey, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable?> AcquireApplicationLocksAsync(IEnumerable<string> lockKeys, TimeSpan timeout, CancellationToken cancellationToken = default);
}
