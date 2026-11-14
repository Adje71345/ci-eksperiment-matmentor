using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Features.Auth.Login;
using Application.Features.ExerciseSets.StartExerciseSet;
using Application.Features.ExerciseSets.SubmitAnswer;
using Application.Features.Students.CreateStudentProfile;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.TestHelpers;

// A student who is logged in. The client sends the student's token with every request
public sealed record LoggedInStudent(HttpClient Client, int StudentId);

public static class StudentRequests
{
    private const string Password = "hemmelig1";

    // Creates a student through the API and logs in, the same way the app does
    public static async Task<LoggedInStudent> CreateLoggedInStudentAsync(this MatMentorApiFactory factory, int grade, params int[] interestIds)
    {
        HttpClient client = factory.CreateClient();
        // Unique, so tests that share a database do not collide
        string username = $"elev{Guid.NewGuid():N}";

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/students",
            new CreateStudentProfileRequest(username, Password, "parent@example.com", grade, [.. interestIds]));
        created.EnsureSuccessStatusCode();

        HttpResponseMessage loggedIn = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, Password));
        loggedIn.EnsureSuccessStatusCode();
        LoginResponse login = (await loggedIn.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        // The API does not return the student's id, so it is read from the database
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int? studentId = await db.Users.Where(u => u.UserName == username).Select(u => u.StudentId).SingleAsync();

        return new LoggedInStudent(client, studentId!.Value);
    }

    public static Task<HttpResponseMessage> PostStartAsync(this LoggedInStudent student, int topicId)
        => student.Client.PostAsJsonAsync("/api/exercise-set-attempts", new StartExerciseSetRequest(topicId));

    // Starts an exercise set and fails the test if the API does not answer 201
    public static async Task<StartExerciseSetResponse> StartExerciseSetAsync(this LoggedInStudent student, int topicId)
    {
        HttpResponseMessage response = await student.PostStartAsync(topicId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StartExerciseSetResponse>())!;
    }

    public static Task<HttpResponseMessage> SubmitAnswerAsync(this LoggedInStudent student, int attemptId, int exerciseId, string answer)
        => student.Client.PostAsJsonAsync($"/api/exercise-set-attempts/{attemptId}/answers", new SubmitAnswerRequest(exerciseId, answer));

    public static Task<HttpResponseMessage> CompleteAsync(this LoggedInStudent student, int attemptId)
        => student.Client.PostAsync($"/api/exercise-set-attempts/{attemptId}/completion", null);
}