using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class DoctorFilterValidator : AbstractValidator<DoctorFilterDto>
{
    public DoctorFilterValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100.");

        RuleFor(x => x.MaxFee)
            .GreaterThanOrEqualTo(0).When(x => x.MaxFee.HasValue)
            .WithMessage("Maximum fee must be greater than or equal to 0.");
    }
}
