using FluentAssertions;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class ChatbotApiControllerTests
{
    private readonly Mock<IChatbotService> _chatbotServiceMock;
    private readonly ChatbotApiController _controller;

    public ChatbotApiControllerTests()
    {
        _chatbotServiceMock = new Mock<IChatbotService>();
        _controller = new ChatbotApiController(_chatbotServiceMock.Object);
    }

    [Fact]
    public async Task SendMessage_ShouldReturnBadRequest_WhenPayloadIsNull()
    {
        // Act
        var result = await _controller.SendMessage(null!);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task SendMessage_ShouldReturnBadRequest_WhenMessageIsBlank(string message)
    {
        // Arrange
        var request = new ChatbotRequestDto { Message = message };

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendMessage_ShouldReturnOk_WithEmergencyTriage_WhenSevereSymptomsReported()
    {
        // Arrange
        var request = new ChatbotRequestDto { Message = "I feel severe crushing chest pain and numbness in my left arm" };
        var botResponse = new ChatbotResponseDto
        {
            ReplyText = "Immediate medical evaluation required.",
            IsEmergency = true,
            EmergencyNotice = "Call 123 emergency services immediately.",
            RecommendedSpecialization = "Cardiology"
        };

        _chatbotServiceMock.Setup(s => s.ProcessMessageAsync(request))
            .ReturnsAsync(botResponse);

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var responseObj = okResult.Value;
        responseObj.Should().NotBeNull();
    }

    [Fact]
    public async Task SendMessage_ShouldReturnOk_WithMatchedSpecialists_WhenRoutineSymptomProvided()
    {
        // Arrange
        var request = new ChatbotRequestDto { Message = "Severe rash and itchy hives on my arms" };
        var botResponse = new ChatbotResponseDto
        {
            ReplyText = "Based on your symptoms, we recommend consulting Dermatology.",
            IsEmergency = false,
            RecommendedSpecialization = "Dermatology",
            Doctors = new List<ChatbotDoctorCardDto>
            {
                new()
                {
                    Id = 2,
                    FullName = "Dr. Mona Dermatology",
                    Specialization = "Dermatology",
                    Fee = 350
                }
            }
        };

        _chatbotServiceMock.Setup(s => s.ProcessMessageAsync(request))
            .ReturnsAsync(botResponse);

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
