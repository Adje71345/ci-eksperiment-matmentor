using System.Net;
using Domain;
using IntegrationTests.TestHelpers;

namespace IntegrationTests.Application.Features.ExerciseSets.SubmitAnswer;

public class SubmitAnswerEndpointTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    // The answer, the assessment and the attempt are all in the database afterwards
    [Fact]
    [Trait("TestCase", "TC-026")]
    public async Task SubmitAnswer_ValidAnswer_IsStoredInDatabase()
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("3,5");
        int exerciseId = attempt.Exercises[0].Id;

        HttpResponseMessage response = await student.SubmitAnswerAsync(attempt.AttemptId, exerciseId, "3,5");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AnswerSubmission submission = Assert.Single(await factory.GetSubmissionsAsync(attempt.AttemptId));
        Assert.Equal(exerciseId, submission.ExerciseId);
        Assert.Equal("3,5", submission.SubmittedAnswer);
        Assert.True(submission.IsCorrect);
    }
}