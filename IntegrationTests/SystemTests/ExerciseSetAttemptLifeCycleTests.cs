using System.Net;
using System.Net.Http.Json;
using Application.Common.Responses;
using Application.Features.ExerciseSets.StartExerciseSet;
using Domain;
using IntegrationTests.TestHelpers;

namespace IntegrationTests.SystemTests;

// The attempt's lifecycle through HTTP with the real API, login and database
public class ExerciseSetAttemptLifecycleTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    // The longest happy path: start, answer every exercise and complete
    [Fact]
    [Trait("TestCase", "TC-022")]
    public async Task CompleteAttempt_AllExercisesAnswered_CompletesAttemptAndKeepsAnswers()
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("15", "320");
        foreach (ExerciseItem exercise in attempt.Exercises)
            (await student.SubmitAnswerAsync(attempt.AttemptId, exercise.Id, "15")).EnsureSuccessStatusCode();

        HttpResponseMessage response = await student.CompleteAsync(attempt.AttemptId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ExerciseSetAttempt saved = await factory.GetAttemptAsync(attempt.AttemptId);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal(2, (await factory.GetSubmissionsAsync(attempt.AttemptId)).Count);
    }

    // Without the handler's check the answer would be saved, because the domain has no rule against it
    [Fact]
    [Trait("TestCase", "TC-024")]
    public async Task SubmitAnswer_AttemptCompleted_Returns409WithErrorResponse()
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("15");
        (await student.CompleteAsync(attempt.AttemptId)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await student.SubmitAnswerAsync(attempt.AttemptId, attempt.Exercises[0].Id, "15");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(error?.Error));
        Assert.Empty(await factory.GetSubmissionsAsync(attempt.AttemptId));
    }
}