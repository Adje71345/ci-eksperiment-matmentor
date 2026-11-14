using Application.Features.ExerciseSets.StartExerciseSet;
using Domain;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.TestHelpers;

public sealed record SeededExerciseSet(int Id, IReadOnlyList<int> ExerciseIds);

// Data the tests add directly to the database. Each test gets its own topic,
// so tests in the same database cannot see each other's exercise sets
public static class TestData
{
    public static async Task<int> AddTopicAsync(this MatMentorApiFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var topic = new Topic($"Emne {Guid.NewGuid():N}");
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.Id;
    }

    // Adds a set with one exercise per correct answer, at positions 1, 2, 3 ...
    public static async Task<SeededExerciseSet> AddExerciseSetAsync(
        this MatMentorApiFactory factory, int topicId, int grade, int? interestId, params string[] correctAnswers)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var set = new ExerciseSet($"Sæt {Guid.NewGuid():N}", grade, topicId, interestId);
        db.ExerciseSets.Add(set);
        await db.SaveChangesAsync();

        List<Exercise> exercises = correctAnswers
            .Select((correctAnswer, index) => new Exercise(index + 1, $"Opgave {index + 1}", correctAnswer, null, set.Id, topicId))
            .ToList();
        db.Exercises.AddRange(exercises);
        await db.SaveChangesAsync();

        return new SeededExerciseSet(set.Id, exercises.Select(e => e.Id).ToList());
    }

    // A logged-in 8th grade student without interests, who has started a standard set in a topic of its own
    public static async Task<(LoggedInStudent Student, StartExerciseSetResponse Attempt)> StartNewStandardSetAsync(
        this MatMentorApiFactory factory, params string[] correctAnswers)
    {
        int topicId = await factory.AddTopicAsync();
        await factory.AddExerciseSetAsync(topicId, grade: 8, interestId: null, correctAnswers);
        LoggedInStudent student = await factory.CreateLoggedInStudentAsync(grade: 8);
        StartExerciseSetResponse attempt = await student.StartExerciseSetAsync(topicId);
        return (student, attempt);
    }

    public static async Task<List<AnswerSubmission>> GetSubmissionsAsync(this MatMentorApiFactory factory, int attemptId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AnswerSubmissions.Where(s => s.ExerciseSetAttemptId == attemptId).ToListAsync();
    }

    public static async Task<ExerciseSetAttempt> GetAttemptAsync(this MatMentorApiFactory factory, int attemptId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ExerciseSetAttempts.SingleAsync(a => a.Id == attemptId);
    }
}
