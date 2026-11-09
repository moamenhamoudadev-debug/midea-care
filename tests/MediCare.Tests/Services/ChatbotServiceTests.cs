using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class ChatbotServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly ChatbotService _sut;

    public ChatbotServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _mockUow.Setup(u => u.Doctors.GetApprovedDoctorsAsync(null))
            .ReturnsAsync(new List<Doctor>());
        _sut = new ChatbotService(_mockUow.Object);
    }

    [Theory]
    [InlineData("I feel severe chest pain and cannot breathe")]
    [InlineData("Sudden numbness in arm and stroke symptoms")]
    [InlineData("Heavy bleeding from a wound")]
    public async Task ProcessMessageAsync_EmergencyKeywords_FlagsEmergencyNoticeImmediately(string message)
    {
        // Act
        var result = await _sut.ProcessMessageAsync(new ChatbotRequestDto { Message = message });

        // Assert
        result.Should().NotBeNull();
        result.IsEmergency.Should().BeTrue();
        result.EmergencyNotice.Should().Contain("EMERGENCY NOTICE");
        result.EmergencyNotice.Should().Contain("123");
    }

    [Fact]
    public async Task ProcessMessageAsync_CardiologySymptoms_RecommendsCardiologists()
    {
        // Arrange
        var cardiologists = new List<Doctor>
        {
            new()
            {
                Id = 10,
                IsApproved = true,
                ConsultationFee = 450,
                SpecializationId = 1,
                User = new ApplicationUser { FullName = "Dr. Hassan Ali", Email = "hassan@clinic.com" },
                Specialization = new Specialization { Id = 1, Name = "Cardiology" }
            }
        };

        _mockUow.Setup(u => u.Doctors.GetApprovedDoctorsAsync(null))
            .ReturnsAsync(cardiologists);

        // Act
        var result = await _sut.ProcessMessageAsync(new ChatbotRequestDto { Message = "I have palpitations and heart rhythm problems" });

        // Assert
        result.RecommendedSpecialization.Should().Be("Cardiology");
        result.Doctors.Should().NotBeEmpty();
        result.Doctors.First().Id.Should().Be(10);
        result.Doctors.First().Specialization.Should().Be("Cardiology");
    }

    [Fact]
    public async Task ProcessMessageAsync_BookingPolicyQuery_ReturnsNavigationGuidance()
    {
        // Act
        var result = await _sut.ProcessMessageAsync(new ChatbotRequestDto { Message = "How can I book an appointment?" });

        // Assert
        result.ReplyText.Should().Contain("Book");
        result.ReplyText.Should().Contain("Find Doctors");
    }
}
