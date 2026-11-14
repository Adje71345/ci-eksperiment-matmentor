using Domain;
using Infrastructure;
using IntegrationTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Infrastructure.Persistence.Configurations;

public class AnswerSubmissionConfigurationTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    // Two requests can both pass the handler's check before either has saved.
    // Then only the unique index stops the second answer
    [Fact]
    [Trait("TestCase", "TC-028")]
    public async Task SaveChangesAsync_SameAttemptAndExerciseTwice_ThrowsDbUpdateException()
    {
        (int attemptId, int exerciseId) = await CreateAttemptAsync();
        await SaveSubmissionAsync(new AnswerSubmission(attemptId, exerciseId, "3,5", true));

        Exception? exception = await Record.ExceptionAsync(
            () => SaveSubmissionAsync(new AnswerSubmission(attemptId, exerciseId, "4", false)));

        Assert.IsType<DbUpdateException>(exception);
    }

    // Each call gets its own DbContext, like two separate requests
    private async Task SaveSubmissionAsync(AnswerSubmission submission)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AnswerSubmissions.Add(submission);
        await db.SaveChangesAsync();
    }

    private async Task<(int AttemptId, int ExerciseId)> CreateAttemptAsync()
    {
        LoggedInStudent student = await factory.CreateLoggedInStudentAsync(grade: 8);
        int topicId = await factory.AddTopicAsync();
        SeededExerciseSet set = await factory.AddExerciseSetAsync(topicId, grade: 8, interestId: null, "3,5");

        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = new ExerciseSetAttempt(student.StudentId, set.Id);
        db.ExerciseSetAttempts.Add(attempt);
        await db.SaveChangesAsync();

        return (attempt.Id, set.ExerciseIds[0]);
    }
}