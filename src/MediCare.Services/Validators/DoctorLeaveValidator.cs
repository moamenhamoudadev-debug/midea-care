using FluentValidation;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class DoctorLeaveValidator : AbstractValidator<DoctorLeaveDto>
{
    public DoctorLeaveValidator(IClinicClock? clinicClock = null)
    {
        var clock = clinicClock ?? new ClinicClock();

        RuleFor(x => x.StartDate.Date)
            .GreaterThanOrEqualTo(_ => clock.Today)
            .WithMessage("Leave start date cannot be in the past.");

        RuleFor(x => x.EndDate.Date)
            .GreaterThanOrEqualTo(x => x.StartDate.Date)
            .WithMessage("Leave end date must be on or after the start date.");

        RuleFor(x => x.Reason)
            .MaximumLength(250).WithMessage("Reason cannot exceed 250 characters.");
    }
}
