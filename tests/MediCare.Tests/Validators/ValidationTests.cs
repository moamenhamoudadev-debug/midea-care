using FluentAssertions;
using MediCare.Services.DTOs;
using MediCare.Services.Validators;
using Xunit;

namespace MediCare.Tests.Validators;

public class ValidationTests
{
    private readonly PatientRegisterValidator _patientValidator = new();
    private readonly DoctorRegisterValidator _doctorValidator = new();

    [Fact]
    public void PatientRegisterValidator_ShouldFail_WhenPasswordDoesNotMeetComplexity()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Ahmed",
            Email = "ahmed@clinic.com",
            Password = "simple", // Missing upper, digit, special, and min length
            ConfirmPassword = "simple",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void PatientRegisterValidator_ShouldFail_WhenBirthDateIsInFuture()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Ahmed",
            Email = "ahmed@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            DateOfBirth = DateTime.Today.AddDays(5),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DateOfBirth");
    }

    [Fact]
    public void DoctorRegisterValidator_ShouldFail_WhenSpecializationIdIsZero()
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = "Dr. Tarek",
            Email = "tarek@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 0,
            LicenseNumber = "LIC-1234",
            ConsultationFee = 200
        };

        // Act
        var result = _doctorValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SpecializationId");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("Ahmed123")]
    [InlineData("محمد123")]
    [InlineData("أحمد١٢٣")]
    [InlineData("١٢٣٤٥")]
    [InlineData("Al")] // Less than 3 chars
    [InlineData(".....")] // No letters
    public void PatientRegisterValidator_ShouldFail_WhenFullNameContainsDigitsOrNoLetters(string invalidName)
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = invalidName,
            Email = "patient@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Theory]
    [InlineData("أحمد محمود")]
    [InlineData("د. محمد علي")]
    [InlineData("John Doe")]
    [InlineData("Mary-Jane O'Connor")]
    public void PatientRegisterValidator_ShouldSucceed_WhenFullNameIsValid(string validName)
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = validName,
            Email = "patient@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "01012345678",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.Errors.Should().NotContain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void PatientRegisterValidator_ShouldFail_WhenPhoneNumberContainsLetters()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Ahmed Tarek",
            Email = "ahmed@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "0101234abcd",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PhoneNumber");
    }

    [Theory]
    [InlineData("Dr. 1234")]
    [InlineData("دكتور١٢٣")]
    public void DoctorRegisterValidator_ShouldFail_WhenFullNameContainsDigits(string invalidName)
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = invalidName,
            Email = "doctor@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 1,
            LicenseNumber = "LIC-9999",
            ConsultationFee = 250,
            Governorate = "Cairo"
        };

        // Act
        var result = _doctorValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void PatientUpdateProfileValidator_ShouldFail_WhenFullNameContainsDigits()
    {
        // Arrange
        var validator = new PatientUpdateProfileValidator();
        var dto = new PatientUpdateProfileDto
        {
            FullName = "Ahmed123",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void DoctorRegisterValidator_ShouldFail_WhenConsultationFeeExceedsTenThousand()
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = "Dr. Mohamed Ali",
            Email = "doctor@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 1,
            LicenseNumber = "DOC-9999",
            ConsultationFee = 15000m, // Exceeds 10,000 EGP ceiling
            Governorate = "Cairo"
        };

        // Act
        var result = _doctorValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConsultationFee" && e.ErrorMessage.Contains("10,000"));
    }

    [Fact]
    public void CreateEncounterValidator_ShouldFail_WhenDiagnosisExceeds500Characters()
    {
        // Arrange
        var validator = new CreateEncounterValidator();
        var dto = new CreateEncounterDto
        {
            AppointmentId = 10,
            Diagnosis = new string('A', 501)
        };

        // Act
        var result = validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Diagnosis" && e.ErrorMessage.Contains("500"));
    }

    [Fact]
    public void CreateEncounterValidator_ShouldFail_WhenPrescriptionItemExceedsLimits()
    {
        // Arrange
        var validator = new CreateEncounterValidator();
        var dto = new CreateEncounterDto
        {
            AppointmentId = 10,
            Diagnosis = "Acute Bronchitis",
            PrescriptionItems = new List<PrescriptionItemDto>
            {
                new()
                {
                    MedicationName = new string('M', 151), // Exceeds 150
                    Dosage = "500mg",
                    Frequency = "Once daily",
                    DurationDays = 400 // Exceeds 365
                }
            }
        };

        // Act
        var result = validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("150 characters"));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("365 days"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void SubmitDoctorReviewValidator_ShouldFail_WhenRatingOutOfRange(int rating)
    {
        var validator = new SubmitDoctorReviewValidator();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 1,
            Rating = rating,
            Comment = "Valid comment text."
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Rating");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void SubmitDoctorReviewValidator_ShouldFail_WhenCommentTooShort(string comment)
    {
        var validator = new SubmitDoctorReviewValidator();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 1,
            Rating = 5,
            Comment = comment
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Comment");
    }

    [Fact]
    public void SubmitDoctorReviewValidator_ShouldFail_WhenScriptTagsInjected()
    {
        var validator = new SubmitDoctorReviewValidator();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 1,
            Rating = 5,
            Comment = "<script>alert(1)</script> Hello doctor"
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Comment" && e.ErrorMessage.Contains("invalid characters"));
    }

    [Fact]
    public void SubmitDoctorReviewValidator_ShouldPass_WhenInputIsValid()
    {
        var validator = new SubmitDoctorReviewValidator();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 10,
            Rating = 5,
            Title = "ممتاز جداً",
            Comment = "طبيب متفهم واستمع لكل شكوتاي بعناية."
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeTrue();
    }
}