using System.Net;
using System.Net.Http.Json;
using IntegrationTests.TestHelpers;

namespace IntegrationTests.Contracts;

// Requests are written as plain JSON from the contract, not with the backend's own request types,
// so a renamed property in a request type is not hidden from the tests
public class ExerciseSetAttemptContractTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    private const string StartPath = "/api/exercise-set-attempts";
    private const string AnswersPath = "/api/exercise-set-attempts/{attemptId}/answers";
    private const string CompletionPath = "/api/exercise-set-attempts/{attemptId}/completion";

    [Fact]
    [Trait("TestCase", "TC-031")]
    public async Task StartExerciseSet_ValidRequest_MatchesApprovedContract()
    {
        int topicId = await factory.AddTopicAsync();
        await factory.AddExerciseSetAsync(topicId, grade: 8, interestId: null, "15", "320");
        LoggedInStudent student = await factory.CreateLoggedInStudentAsync(grade: 8);

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(
            StartPath, new { topicId }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", StartPath);
    }

    [Fact]
    [Trait("TestCase", "TC-032")]
    public async Task SubmitAnswer_ValidRequest_MatchesApprovedContract()
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("15");

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(
            $"/api/exercise-set-attempts/{attempt.AttemptId}/answers",
            new { exerciseId = attempt.Exercises[0].Id, answer = "15" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", AnswersPath);
    }

    [Fact]
    [Trait("TestCase", "TC-033")]
    public async Task CompleteAttempt_NoBody_MatchesApprovedContract()
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("15");

        HttpResponseMessage response = await student.Client.PostAsync(
            $"/api/exercise-set-attempts/{attempt.AttemptId}/completion", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", CompletionPath);
    }

    // All three requests go to a completed attempt. The completion endpoint ignores the body,
    // so every row can send the same body and the act stays the same
    [Theory]
    [Trait("TestCase", "TC-034")]
    [InlineData(AnswersPath, AnswersPath, HttpStatusCode.BadRequest)]
    [InlineData("/api/exercise-set-attempts/999999/completion", CompletionPath, HttpStatusCode.NotFound)]
    [InlineData(CompletionPath, CompletionPath, HttpStatusCode.Conflict)]
    public async Task ErrorResponse_InvalidUnknownOrConflict_MatchesApprovedContract(
        string requestPath, string contractPath, HttpStatusCode expectedStatus)
    {
        var (student, attempt) = await factory.StartNewStandardSetAsync("15");
        (await student.CompleteAsync(attempt.AttemptId)).EnsureSuccessStatusCode();
        string url = requestPath.Replace("{attemptId}", attempt.AttemptId.ToString());

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(
            url, new { exerciseId = attempt.Exercises[0].Id, answer = "3.5" }, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", contractPath);
    }
}