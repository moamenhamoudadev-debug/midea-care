using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Web.ViewModels;

public class DoctorFilterViewModel
{
    public int? SpecializationId { get; set; }
    public decimal? MaxFee { get; set; }
    public DayOfWeek? AvailableDay { get; set; }
    public string? SearchTerm { get; set; }
    public string? Governorate { get; set; }
    public int Page { get; set; } = 1;
}

public class DoctorListViewModel
{
    public DoctorFilterViewModel Filter { get; set; } = new();
    public PagedResult<DoctorSummaryDto> Doctors { get; set; } = new();
    public List<SpecializationDto> Specializations { get; set; } = new();
}
