using FluentAssertions;
using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class DoctorServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly IValidator<DoctorFilterDto> _validator;
    private readonly DoctorService _sut;

    public DoctorServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _validator = new DoctorFilterValidator();
        _sut = new DoctorService(_mockUow.Object, _validator);
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithNoFilters_ReturnsApprovedDoctorsAndTotalCount()
    {
        // Arrange
        var testDoctors = new List<Doctor>
        {
            new()
            {
                Id = 1,
                SpecializationId = 1,
                ConsultationFee = 300,
                IsApproved = true,
                User = new ApplicationUser { FullName = "Dr. Ahmed Mahmoud" },
                Specialization = new Specialization { Id = 1, Name = "Cardiology" },
                WorkingHours = new List<WorkingHours>
                {
                    new() { DayOfWeek = DayOfWeek.Sunday }
                }
            },
            new()
            {
                Id = 2,
                SpecializationId = 2,
                ConsultationFee = 250,
                IsApproved = true,
                User = new ApplicationUser { FullName = "Dr. Sara Al-Sayed" },
                Specialization = new Specialization { Id = 2, Name = "Dermatology" },
                WorkingHours = new List<WorkingHours>
                {
                    new() { DayOfWeek = DayOfWeek.Monday }
                }
            }
        };

        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, null, 1, 6))
            .ReturnsAsync((testDoctors, 2));

        var filter = new DoctorFilterDto();

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.First().FullName.Should().Be("Dr. Ahmed Mahmoud");
        result.Items.First().SpecializationName.Should().Be("Cardiology");
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithSpecializationFilter_CallsRepositoryWithParameter()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                1, null, null, null, null, 1, 6))
            .ReturnsAsync((new List<Doctor>(), 0));

        var filter = new DoctorFilterDto { SpecializationId = 1 };

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        _mockUow.Verify(u => u.Doctors.SearchApprovedDoctorsAsync(
            1, null, null, null, null, 1, 6), Times.Once);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithGovernorateFilter_CallsRepositoryWithGovernorateParameter()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, "Alexandria", 1, 6))
            .ReturnsAsync((new List<Doctor>(), 0));

        var filter = new DoctorFilterDto { Governorate = "Alexandria" };

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        _mockUow.Verify(u => u.Doctors.SearchApprovedDoctorsAsync(
            null, null, null, null, "Alexandria", 1, 6), Times.Once);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDoctorDetailsAsync_WhenDoctorExistsAndApproved_ReturnsSuccessWithMappedDto()
    {
        // Arrange
        var doctor = new Doctor
        {
            Id = 5,
            SpecializationId = 3,
            LicenseNumber = "LIC-9988",
            ConsultationFee = 350,
            SlotDurationMinutes = 30,
            Bio = "Senior Consultant in Orthopedic Surgery.",
            IsApproved = true,
            User = new ApplicationUser
            {
                FullName = "Dr. Mona Mansour",
                Email = "mona@clinic.com",
                PhoneNumber = "+201012345678"
            },
            Specialization = new Specialization { Id = 3, Name = "Orthopedics" },
            WorkingHours = new List<WorkingHours>
            {
                new() { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(20, 0, 0) }
            }
        };

        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(5))
            .ReturnsAsync(doctor);

        // Act
        var result = await _sut.GetDoctorDetailsAsync(5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FullName.Should().Be("Dr. Mona Mansour");
        result.Value.Email.Should().Be("mona@clinic.com");
        result.Value.SpecializationName.Should().Be("Orthopedics");
        result.Value.LicenseNumber.Should().Be("LIC-9988");
        result.Value.WorkingHours.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDoctorDetailsAsync_WhenDoctorNotFound_ReturnsFailure()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(999))
            .ReturnsAsync((Doctor?)null);

        // Act
        var result = await _sut.GetDoctorDetailsAsync(999);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSpecializationsAsync_ReturnsMappedAlphabeticalList()
    {
        // Arrange
        var specs = new List<Specialization>
        {
            new() { Id = 1, Name = "Pediatrics" },
            new() { Id = 2, Name = "Cardiology" }
        };

        _mockUow.Setup(u => u.Specializations.GetAllAsync())
            .ReturnsAsync(specs);

        // Act
        var result = await _sut.GetSpecializationsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Cardiology"); // Sorted alphabetically
        result[1].Name.Should().Be("Pediatrics");
    }

    [Fact]
    public async Task SearchDoctorsAsync_DoesNotInventInsuranceBadges_WithoutInMemoryFiltering()
    {
        // Doctor 10 has seed 10 (10 % 10 == 0 -> accepts insurance)
        // Doctor 9 has seed 9 (9 % 10 == 9 -> does NOT accept insurance)
        var doctorInsurance = new Doctor
        {
            Id = 10,
            ConsultationFee = 400,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Tarek" },
            Specialization = new Specialization { Name = "Cardiology" },
            WorkingHours = new List<WorkingHours>()
        };

        var doctorNoInsurance = new Doctor
        {
            Id = 9,
            ConsultationFee = 400,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Samy" },
            Specialization = new Specialization { Name = "Cardiology" },
            WorkingHours = new List<WorkingHours>()
        };

        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, null, 1, 6))
            .ReturnsAsync((new List<Doctor> { doctorInsurance, doctorNoInsurance }, 2));

        var filter = new DoctorFilterDto();

        var result = await _sut.SearchDoctorsAsync(filter);

        // Verify SQL pagination integrity: both doctors are preserved in the paged result
        result.Items.Should().HaveCount(2);
        var doc1 = result.Items.First(d => d.Id == 10);
        var doc2 = result.Items.First(d => d.Id == 9);

        doc1.InsuranceBadge.Should().BeEmpty("the directory must not imply an insurance relationship without verified data");
        doc2.InsuranceBadge.Should().BeEmpty("the directory must not imply an insurance relationship without verified data");
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithInvalidPaging_NormalizesToPage1AndSize6()
    {
        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, null, 1, 6))
            .ReturnsAsync((new List<Doctor>(), 0));

        var filter = new DoctorFilterDto { Page = -5, PageSize = 0 };

        var result = await _sut.SearchDoctorsAsync(filter);

        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(6);
        _mockUow.Verify(u => u.Doctors.SearchApprovedDoctorsAsync(
            null, null, null, null, null, 1, 6), Times.Once);
    }

    [Fact]
    public async Task GetDoctorByUserIdAsync_WhenDoctorExists_ReturnsSuccess()
    {
        var doctor = new Doctor
        {
            Id = 42,
            UserId = "user-doctor-42",
            ConsultationFee = 500,
            User = new ApplicationUser { FullName = "Dr. Nader", Email = "nader@clinic.com" },
            Specialization = new Specialization { Name = "Neurology" },
            WorkingHours = new List<WorkingHours>()
        };

        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("user-doctor-42"))
            .ReturnsAsync(doctor);

        var result = await _sut.GetDoctorByUserIdAsync("user-doctor-42");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.FullName.Should().Be("Dr. Nader");
        result.Value.Email.Should().Be("nader@clinic.com");
    }

    [Fact]
    public async Task GetDoctorByUserIdAsync_WhenNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("non-existent"))
            .ReturnsAsync((Doctor?)null);

        var result = await _sut.GetDoctorByUserIdAsync("non-existent");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("profile not found");
    }

    [Fact]
    public async Task GetDoctorIdByUserIdAsync_WhenDoctorExists_ReturnsId()
    {
        var doctor = new Doctor { Id = 77, UserId = "user-77" };
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("user-77"))
            .ReturnsAsync(doctor);

        var result = await _sut.GetDoctorIdByUserIdAsync("user-77");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(77);
    }

    [Fact]
    public async Task GetDoctorIdByUserIdAsync_WhenNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("unknown"))
            .ReturnsAsync((Doctor?)null);

        var result = await _sut.GetDoctorIdByUserIdAsync("unknown");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("profile not found");
    }

    [Fact]
    public async Task GetDoctorDetailsAsync_DoesNotInferDirectoryClaimsFromBioOrId()
    {
        var doctor = new Doctor
        {
            Id = 99,
            Bio = "استشاري يعمل في الفيوم بحي المسلة",
            Governorate = "",
            Specialization = new Specialization { Name = "Cardiology" },
            User = new ApplicationUser { FullName = "Dr. Demo" },
            WorkingHours = new List<WorkingHours>()
        };

        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(99)).ReturnsAsync(doctor);
        var result = await _sut.GetDoctorDetailsAsync(99);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Governorate.Should().BeEmpty();
        result.Value.ClinicAddress.Should().BeEmpty();
        result.Value.Rating.Should().Be(0);
        result.Value.ReviewCount.Should().Be(0);
        result.Value.Reviews.Should().BeEmpty();
        result.Value.ExperienceYears.Should().Be(0);
        result.Value.AcceptsInsurance.Should().BeFalse();
    }

    [Theory]
    [InlineData("عيادة في سموحة بالإسكندرية")]
    [InlineData("استشاري بطنطا في الغربية")]
    [InlineData("مقر العيادة في المهندسين بالجيزة")]
    [InlineData("العيادة في المنصورة بالدقهلية")]
    [InlineData("عيادة بني سويف بميدان الزراعيين")]
    [InlineData("طبيب بالمنيا في شارع طه حسين")]
    [InlineData("عيادة قنا بميدان الساعة")]
    [InlineData("الأقصر منطقة العوامية")]
    [InlineData("أسوان شارع أبطال السيل")]
    [InlineData("الغردقة حي الكوثر بالبحر الأحمر")]
    [InlineData("شرم الشيخ جنوب سيناء")]
    [InlineData("العريش شمال سيناء")]
    [InlineData("مرسى مطروح والساحل الشمالي")]
    [InlineData("الخارجة بالوادي الجديد")]
    [InlineData("شبين الكوم بالمنوفية")]
    [InlineData("دمنهور بالبحيرة شارع عبد السلام الشاذلي")]
    [InlineData("كفر الشيخ حي الصوالحة")]
    [InlineData("رأس البر بدمياط")]
    [InlineData("أسيوط شارع يسري راغب")]
    [InlineData("الزقازيق بالشرقية")]
    [InlineData("بنها بالقليوبية شارع فريد ندا")]
    [InlineData("استشاري بمحافظة الإسماعيلية - عيادة نمرة 6")]
    [InlineData("بورسعيد حي الشرق")]
    [InlineData("السويس حي الأربعين")]
    [InlineData("سوهاج حي سيتي")]
    [InlineData("التجمع الخامس بالقاهرة الجديدة")]
    public async Task GetDoctorDetailsAsync_DoesNotInferGovernorateFromBio(string bio)
    {
        var doctor = new Doctor
        {
            Id = 120,
            Bio = bio,
            Governorate = "Stored governorate",
            Specialization = new Specialization { Name = "General Internal Medicine" },
            User = new ApplicationUser { FullName = "Dr. Test" },
            WorkingHours = new List<WorkingHours>()
        };

        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(120)).ReturnsAsync(doctor);
        var result = await _sut.GetDoctorDetailsAsync(120);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Governorate.Should().Be("Stored governorate");
        result.Value.ClinicAddress.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Dentistry")]
    [InlineData("Urology")]
    [InlineData("Chest")]
    [InlineData("Psychiatry")]
    [InlineData("Internal Medicine")]
    public async Task GetDoctorDetailsAsync_DoesNotInventQualificationsOrReviewsFromSpecialty(string specName)
    {
        var doctor = new Doctor
        {
            Id = 88,
            Specialization = new Specialization { Name = specName },
            User = new ApplicationUser { FullName = "Dr. Test" },
            WorkingHours = new List<WorkingHours>()
        };

        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(88)).ReturnsAsync(doctor);
        var result = await _sut.GetDoctorDetailsAsync(88);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubSpecialties.Should().BeEmpty();
        result.Value.AcademicDegree.Should().BeEmpty();
        result.Value.Reviews.Should().BeEmpty();
        result.Value.ReviewCount.Should().Be(0);
    }
    [Fact]
    public async Task GetDoctorDetailsAsync_WhenDoctorNotFound_ReturnsFailureResult()
    {
        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(999))
            .ReturnsAsync((Doctor?)null);

        var result = await _sut.GetDoctorDetailsAsync(999);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor not found");
    }

    [Fact]
    public async Task GetDoctorByUserIdAsync_WhenFound_ReturnsSuccessWithDetails()
    {
        var doctor = new Doctor
        {
            Id = 45,
            UserId = "user-doc-45",
            SpecializationId = 1,
            Specialization = new Specialization { Id = 1, Name = "Cardiology" },
            User = new ApplicationUser { Id = "user-doc-45", FullName = "Dr. Hazem", Email = "hazem@test.com" },
            WorkingHours = new List<WorkingHours>
            {
                new() { DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(13) }
            }
        };

        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("user-doc-45"))
            .ReturnsAsync(doctor);

        var result = await _sut.GetDoctorByUserIdAsync("user-doc-45");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FullName.Should().Be("Dr. Hazem");
        result.Value.Email.Should().Be("hazem@test.com");
        result.Value.WorkingHours.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDoctorByUserIdAsync_WhenNotFound_ReturnsFailureResult()
    {
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("non-existent-user"))
            .ReturnsAsync((Doctor?)null);

        var result = await _sut.GetDoctorByUserIdAsync("non-existent-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor profile not found");
    }

    [Fact]
    public async Task GetDoctorIdByUserIdAsync_WhenFound_ReturnsDoctorId()
    {
        var doctor = new Doctor { Id = 77, UserId = "doc-user-77" };
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("doc-user-77"))
            .ReturnsAsync(doctor);

        var result = await _sut.GetDoctorIdByUserIdAsync("doc-user-77");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(77);
    }

    [Fact]
    public async Task GetDoctorIdByUserIdAsync_WhenNotFound_ReturnsFailureResult()
    {
        _mockUow.Setup(u => u.Doctors.GetByUserIdAsync("unknown-user"))
            .ReturnsAsync((Doctor?)null);

        var result = await _sut.GetDoctorIdByUserIdAsync("unknown-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor profile not found");
    }

    [Fact]
    public async Task GetSpecializationsAsync_ReturnsAlphabeticallySortedList()
    {
        var specs = new List<Specialization>
        {
            new() { Id = 1, Name = "Pediatrics", Description = "Child care" },
            new() { Id = 2, Name = "Cardiology", Description = "Heart care" },
            new() { Id = 3, Name = "Dermatology", Description = "Skin care" }
        };

        _mockUow.Setup(u => u.Specializations.GetAllAsync())
            .ReturnsAsync(specs);

        var result = await _sut.GetSpecializationsAsync();

        result.Should().HaveCount(3);
        result.Select(s => s.Name).Should().ContainInOrder("Cardiology", "Dermatology", "Pediatrics");
    }
}

