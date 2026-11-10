using System;
using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class SubmitDoctorReviewValidator : AbstractValidator<SubmitDoctorReviewDto>
{
    public SubmitDoctorReviewValidator()
    {
        RuleFor(x => x.DoctorId)
            .GreaterThan(0).WithMessage("Valid Doctor ID is required.");

        RuleFor(x => x.AppointmentId)
            .GreaterThan(0).WithMessage("A completed appointment ID is required to submit a verified review.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5 stars.");

        RuleFor(x => x.Comment)
            .NotEmpty().WithMessage("Review comment is required.")
            .MinimumLength(5).WithMessage("Review comment must be at least 5 characters long.")
            .MaximumLength(2000).WithMessage("Review comment cannot exceed 2000 characters.")
            .Must(NotContainScriptTags).WithMessage("Review comment contains invalid characters or markup.");

        RuleFor(x => x.Title)
            .MaximumLength(100).WithMessage("Title cannot exceed 100 characters.")
            .Must(NotContainScriptTags).When(x => !string.IsNullOrEmpty(x.Title))
            .WithMessage("Review title contains invalid characters or markup.");

        RuleFor(x => x.PatientDisplayName)
            .MaximumLength(150).WithMessage("Display name cannot exceed 150 characters.")
            .Must(NotContainScriptTags).When(x => !string.IsNullOrEmpty(x.PatientDisplayName))
            .WithMessage("Display name contains invalid characters or markup.");
    }

    private static bool NotContainScriptTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var lower = text.ToLowerInvariant();
        return !lower.Contains("<script") &&
               !lower.Contains("javascript:") &&
               !lower.Contains("onload=") &&
               !lower.Contains("onerror=");
    }
}
