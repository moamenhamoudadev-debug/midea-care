using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class DoctorReviewsControllerTests
{
    private static ControllerContext CreateControllerContext(string userId, string role = "Patient", bool isAjax = true)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        if (isAjax)
        {
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        }

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task Submit_WhenServiceSucceeds_ReturnsJsonSuccess()
    {
        // Arrange
        var mockService = new Mock<IDoctorReviewService>();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 101,
            Rating = 5,
            Comment = "Excellent service and care."
        };

        var createdReview = new DoctorReviewDto
        {
            Id = 42,
            DoctorId = 1,
            Rating = 5,
            Comment = dto.Comment,
            IsVerifiedAppointment = true
        };

        mockService.Setup(s => s.SubmitReviewAsync(dto, "patient-1"))
            .ReturnsAsync(Result<DoctorReviewDto>.Success(createdReview));

        var controller = new DoctorReviewsController(mockService.Object, NullLogger<DoctorReviewsController>.Instance)
        {
            ControllerContext = CreateControllerContext("patient-1", "Patient", isAjax: true)
        };

        // Act
        var result = await controller.Submit(dto);

        // Assert
        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        jsonResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Submit_WhenServiceFails_ReturnsJsonFailure()
    {
        // Arrange
        var mockService = new Mock<IDoctorReviewService>();
        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = 1,
            AppointmentId = 101,
            Rating = 5,
            Comment = "Duplicate review test."
        };

        mockService.Setup(s => s.SubmitReviewAsync(dto, "patient-1"))
            .ReturnsAsync(Result<DoctorReviewDto>.Failure("A review has already been submitted for this appointment."));

        var controller = new DoctorReviewsController(mockService.Object, NullLogger<DoctorReviewsController>.Instance)
        {
            ControllerContext = CreateControllerContext("patient-1", "Patient", isAjax: true)
        };

        // Act
        var result = await controller.Submit(dto);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Helpful_WhenAuthenticated_CallsServiceAndReturnsJson()
    {
        // Arrange
        var mockService = new Mock<IDoctorReviewService>();
        mockService.Setup(s => s.ToggleHelpfulVoteAsync(10, "user-1", It.IsAny<string?>()))
            .ReturnsAsync(Result<ReviewHelpfulVoteResultDto>.Success(new ReviewHelpfulVoteResultDto
            {
                Success = true,
                ReviewId = 10,
                HelpfulCount = 3,
                HasVoted = true,
                Message = "Marked review as helpful."
            }));

        var controller = new DoctorReviewsController(mockService.Object, NullLogger<DoctorReviewsController>.Instance)
        {
            ControllerContext = CreateControllerContext("user-1", "Patient", isAjax: true)
        };

        // Act
        var result = await controller.Helpful(10);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task DoctorReply_WhenDoctorCalls_ReturnsSuccess()
    {
        // Arrange
        var mockService = new Mock<IDoctorReviewService>();
        var dto = new DoctorReplyReviewDto
        {
            ReviewId = 10,
            Response = "Thank you for visiting our clinic!"
        };

        mockService.Setup(s => s.DoctorReplyAsync(dto, "doc-user-1"))
            .ReturnsAsync(Result<DoctorReviewDto>.Success(new DoctorReviewDto
            {
                Id = 10,
                DoctorResponse = dto.Response
            }));

        var controller = new DoctorReviewsController(mockService.Object, NullLogger<DoctorReviewsController>.Instance)
        {
            ControllerContext = CreateControllerContext("doc-user-1", "Doctor", isAjax: true)
        };

        // Act
        var result = await controller.DoctorReply(dto);

        // Assert
        result.Should().BeOfType<JsonResult>();
    }
}
