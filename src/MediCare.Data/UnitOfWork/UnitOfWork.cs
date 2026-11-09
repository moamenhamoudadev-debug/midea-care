using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IAppointmentRepository? _appointments;
    private IDoctorRepository? _doctors;
    private IRepository<Patient>? _patients;
    private IRepository<Specialization>? _specializations;
    private IRepository<WorkingHours>? _workingHours;
    private IRepository<DoctorLeave>? _doctorLeaves;
    private IMedicalRecordRepository? _medicalRecords;
    private IPrescriptionRepository? _prescriptions;
    private IRepository<PrescriptionItem>? _prescriptionItems;
    private IRepository<Notification>? _notifications;
    private IRepository<NotificationOutbox>? _notificationOutbox;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IAppointmentRepository Appointments =>
        _appointments ??= new AppointmentRepository(_context);

    public IDoctorRepository Doctors =>
        _doctors ??= new DoctorRepository(_context);

    public IRepository<Patient> Patients =>
        _patients ??= new Repository<Patient>(_context);

    public IRepository<Specialization> Specializations =>
        _specializations ??= new Repository<Specialization>(_context);

    public IRepository<WorkingHours> WorkingHours =>
        _workingHours ??= new Repository<WorkingHours>(_context);

    public IRepository<DoctorLeave> DoctorLeaves =>
        _doctorLeaves ??= new Repository<DoctorLeave>(_context);

    public IMedicalRecordRepository MedicalRecords =>
        _medicalRecords ??= new MedicalRecordRepository(_context);

    public IPrescriptionRepository Prescriptions =>
        _prescriptions ??= new PrescriptionRepository(_context);

    public IRepository<PrescriptionItem> PrescriptionItems =>
        _prescriptionItems ??= new Repository<PrescriptionItem>(_context);

    public IRepository<Notification> Notifications =>
        _notifications ??= new Repository<Notification>(_context);

    public IRepository<NotificationOutbox> NotificationOutbox =>
        _notificationOutbox ??= new Repository<NotificationOutbox>(_context);

    public async Task<int> CommitAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task<IDatabaseTransaction?> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            return new InMemoryDatabaseTransaction();
        }

        var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new EfDatabaseTransaction(tx);
    }

    public async Task<IAsyncDisposable?> AcquireApplicationLockAsync(string lockKey, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return await ApplicationLockManager.AcquireLockAsync(_context, lockKey, timeout, cancellationToken);
    }

    public async Task<IAsyncDisposable?> AcquireApplicationLocksAsync(IEnumerable<string> lockKeys, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return await ApplicationLockManager.AcquireLocksAsync(_context, lockKeys, timeout, cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
