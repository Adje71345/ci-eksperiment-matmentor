using Domain.Rules;

namespace UnitTests.Domain.Rules;

// Experiment only: documents the current behaviour for a comma without a digit before it
public class AnswerFormatExperimentTests
{
    [Fact]
    public void TryParse_CommaWithoutDigitBefore_ReturnsTrueAndNumber()
    {
        var success = AnswerFormat.TryParse(",5", out var result);

        Assert.True(success);
        Assert.Equal(0.5m, result);
    }
}