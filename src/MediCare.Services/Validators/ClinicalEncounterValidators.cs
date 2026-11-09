using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class PrescriptionItemValidator : AbstractValidator<PrescriptionItemDto>
{
    public PrescriptionItemValidator()
    {
        RuleFor(x => x.MedicationName)
            .NotEmpty().WithMessage("Medication name is required.")
            .MaximumLength(150).WithMessage("Medication name cannot exceed 150 characters.");

        RuleFor(x => x.Dosage)
            .NotEmpty().WithMessage("Dosage is required.")
            .MaximumLength(100).WithMessage("Dosage cannot exceed 100 characters.");

        RuleFor(x => x.Frequency)
            .NotEmpty().WithMessage("Frequency is required.")
            .MaximumLength(100).WithMessage("Frequency cannot exceed 100 characters.");

        RuleFor(x => x.DurationDays)
            .GreaterThan(0).WithMessage("Duration must be at least 1 day.")
            .LessThanOrEqualTo(365).WithMessage("Duration cannot exceed 365 days.");

        RuleFor(x => x.Instructions)
            .MaximumLength(250).WithMessage("Instructions cannot exceed 250 characters.");
    }
}

public class CreateEncounterValidator : AbstractValidator<CreateEncounterDto>
{
    public CreateEncounterValidator()
    {
        RuleFor(x => x.AppointmentId)
            .GreaterThan(0).WithMessage("Valid appointment ID is required.");

        RuleFor(x => x.Diagnosis)
            .NotEmpty().WithMessage("Clinical diagnosis is required.")
            .MaximumLength(500).WithMessage("Diagnosis cannot exceed 500 characters.");

        RuleFor(x => x.Symptoms)
            .MaximumLength(1000).WithMessage("Symptoms cannot exceed 1000 characters.");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Prescription notes cannot exceed 500 characters.");

        RuleForEach(x => x.PrescriptionItems)
            .SetValidator(new PrescriptionItemValidator());
    }
}
