using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class WorkingHoursValidator : AbstractValidator<WorkingHoursDto>
{
    public WorkingHoursValidator()
    {
        RuleFor(x => x.DayOfWeek)
            .IsInEnum().WithMessage("A valid day of the week is required.");

        RuleFor(x => x.StartTime)
            .GreaterThanOrEqualTo(TimeSpan.Zero).WithMessage("Start time cannot be negative.")
            .LessThan(TimeSpan.FromHours(24)).WithMessage("Start time must be within a 24-hour day.");

        RuleFor(x => x.EndTime)
            .GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.")
            .LessThanOrEqualTo(TimeSpan.FromHours(24)).WithMessage("End time cannot exceed 24:00.");
    }
}
