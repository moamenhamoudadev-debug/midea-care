using MediCare.Data.Context;
using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class DoctorRepository : Repository<Doctor>, IDoctorRepository
{
    public DoctorRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Doctor>> GetApprovedDoctorsAsync(int? specializationId)
    {
        var query = _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Where(d => d.IsApproved);

        if (specializationId.HasValue)
        {
            query = query.Where(d => d.SpecializationId == specializationId.Value);
        }

        return await query.OrderBy(d => d.User.FullName).ToListAsync();
    }

    public async Task<Doctor?> GetDoctorWithScheduleAsync(int doctorId)
    {
        return await _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.Id == doctorId && d.IsApproved);
    }

    public Task<(List<Doctor> Doctors, int TotalCount)> SearchApprovedDoctorsAsync(
        int? specializationId,
        decimal? maxFee,
        DayOfWeek? availableDay,
        string? searchTerm,
        int page,
        int pageSize)
        => SearchApprovedDoctorsAsync(specializationId, maxFee, availableDay, searchTerm, null, page, pageSize);

    public async Task<(List<Doctor> Doctors, int TotalCount)> SearchApprovedDoctorsAsync(
        int? specializationId,
        decimal? maxFee,
        DayOfWeek? availableDay,
        string? searchTerm,
        string? governorate,
        int page,
        int pageSize)
    {
        var query = _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Where(d => d.IsApproved);

        if (specializationId.HasValue && specializationId.Value > 0)
        {
            query = query.Where(d => d.SpecializationId == specializationId.Value);
        }

        if (maxFee.HasValue && maxFee.Value > 0)
        {
            query = query.Where(d => d.ConsultationFee <= maxFee.Value);
        }

        if (availableDay.HasValue)
        {
            query = query.Where(d => d.WorkingHours.Any(w => w.DayOfWeek == availableDay.Value));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(d =>
                d.User.FullName.ToLower().Contains(term) ||
                d.Specialization.Name.ToLower().Contains(term) ||
                (d.Bio != null && d.Bio.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(governorate))
        {
            var gov = governorate.Trim().ToLower();
            var englishGov = gov switch
            {
                "القاهرة" => "cairo",
                "الجيزة" => "giza",
                "الإسكندرية" => "alexandria",
                "الدقهلية" or "المنصورة" => "dakahlia",
                "الغربية" or "طنطا" => "gharbia",
                "المنوفية" or "شبين الكوم" => "monufia",
                "البحيرة" or "دمنهور" => "beheira",
                "كفر الشيخ" => "kafr el sheikh",
                "دمياط" => "damietta",
                "الشرقية" or "الزقازيق" => "sharqia",
                "بورسعيد" => "port said",
                "الإسماعيلية" => "ismailia",
                "السويس" => "suez",
                "الفيوم" => "faiyum",
                "بني سويف" => "beni suef",
                "المنيا" => "minya",
                "أسيوط" => "assiut",
                "سوهاج" => "sohag",
                "قنا" => "qena",
                "الأقصر" => "luxor",
                "أسوان" => "aswan",
                "البحر الأحمر" or "الغردقة" => "red sea",
                "مطروح" => "matrouh",
                "جنوب سيناء" or "شرم الشيخ" => "south sinai",
                "شمال سيناء" or "العريش" => "north sinai",
                "الوادي الجديد" or "الخارجة" => "new valley",
                "القليوبية" or "بنها" => "qalyubia",
                _ => gov
            };

            query = query.Where(d => d.Governorate.ToLower() == gov ||
                                     d.Governorate.ToLower() == englishGov ||
                                     (d.Bio != null && (d.Bio.ToLower().Contains(gov) || d.Bio.ToLower().Contains(englishGov))));
        }

        var totalCount = await query.CountAsync();

        var doctors = await query
            .OrderBy(d => d.User.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (doctors, totalCount);
    }

    public async Task<Doctor?> GetDoctorWithDetailsAsync(int id)
    {
        return await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Doctor?> GetByUserIdAsync(string userId)
    {
        return await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.UserId == userId);
    }

    public async Task<Doctor?> GetDoctorWithScheduleAndLeavesAsync(int doctorId)
    {
        return await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.Id == doctorId);
    }

    public async Task<List<Doctor>> GetAllWithDetailsAsync()
    {
        return await _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .ToListAsync();
    }

    public async Task<List<Doctor>> GetPendingDoctorsWithDetailsAsync()
    {
        return await _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Where(d => !d.IsApproved)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }
}

