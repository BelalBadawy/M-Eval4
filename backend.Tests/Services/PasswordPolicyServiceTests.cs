using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Services;

public class PasswordPolicyServiceTests
{
    private readonly PasswordPolicyService _service;

    public PasswordPolicyServiceTests()
    {
        var settings = Options.Create(new SecuritySettings
        {
            DefaultUserPassword = "Mina@123",
            BcryptWorkFactor = 4 // Fast for tests
        });
        _service = new PasswordPolicyService(settings);
    }

    [Theory]
    [InlineData("", "Password cannot be empty.")]
    [InlineData("Short1!", "Password must be at least 8 characters long.")]
    [InlineData("nouppercase123!", "Password must contain at least one uppercase letter.")]
    [InlineData("NOLOWERCASE123!", "Password must contain at least one lowercase letter.")]
    [InlineData("NoDigitsHere!", "Password must contain at least one digit.")]
    [InlineData("NoSpecialChars123", "Password must contain at least one special character.")]
    [InlineData("Mina@123", "New password cannot match the default temporary password.")]
    public void ValidatePassword_ShouldRejectInvalidPasswords(string password, string expectedError)
    {
        var (isValid, errorMessage) = _service.ValidatePassword(password);

        isValid.Should().BeFalse();
        errorMessage.Should().Be(expectedError);
    }

    [Fact]
    public void ValidatePassword_ShouldAcceptValidStrongPassword()
    {
        var (isValid, errorMessage) = _service.ValidatePassword("Str0ng#P@ssword2026");

        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void HashAndVerifyPassword_ShouldVerifyCorrectly()
    {
        var rawPassword = "ValidP@ssword123";
        var hash = _service.HashPassword(rawPassword);

        hash.Should().NotBeNullOrWhiteSpace();
        _service.VerifyPassword(rawPassword, hash).Should().BeTrue();
        _service.VerifyPassword("WrongPassword123!", hash).Should().BeFalse();
    }
}
