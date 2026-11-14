using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Features.ChatTutor.Conversations;
using IntegrationTests.TestHelpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Application.Features.ChatTutor.AskTutor;

namespace IntegrationTests.Application.Features.ChatTutor.Conversations;

// The whole chain from the endpoint to the request that leaves the backend. Only the network to Ollama is replaced
public class TutorConversationEndpointTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    private const string ConversationsPath = "/api/tutor-conversations";
    private const string ExerciseText = "Hvad er 3/4 af 20?";
    private const string AttemptText = "Jeg har ganget 20 med 3.";

    // The id is set to 4711, so it can be searched for. A small id like 3 could appear in the prompt for other reasons
    [Fact]
    [Trait("TestCase", "TC-038")]
    public async Task CreateConversation_StudentWithKnownId_SendsNoPersonalDataToModel()
    {
        await factory.SetNextStudentIdAsync(4711);
        var ollama = new FakeOllamaHandler();
        await using WebApplicationFactory<Program> api = factory.WithFakeOllama(ollama);
        LoggedInStudent student = await api.CreateLoggedInStudentAsync(grade: 8);

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(ConversationsPath,
            new CreateTutorConversationRequest("Hvad er 3/4 af 20?", "Jeg har ganget 20 med 3.", Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string sent = AllNamesAndValues(Assert.Single(ollama.RequestBodies));
        Assert.DoesNotContain(student.StudentId.ToString(), sent);
        Assert.DoesNotContain(student.Username, sent);
        Assert.DoesNotContain(StudentRequests.ParentEmail, sent);
    }

    // Every field name and value in the JSON as plain text. Reading the JSON decodes escaped characters,
    // and numbers are included, so an id sent as a number is found too
    private static string AllNamesAndValues(string json)
    {
        var parts = new List<string>();
        Collect(JsonDocument.Parse(json).RootElement, parts);
        return string.Join("\n", parts);
    }

    private static void Collect(JsonElement element, List<string> parts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    parts.Add(property.Name);
                    Collect(property.Value, parts);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    Collect(item, parts);
                break;
            case JsonValueKind.String:
                parts.Add(element.GetString()!);
                break;
            default:
                parts.Add(element.GetRawText());
                break;
        }
    }

    // The request has exactly the fields Ollama needs, and every message has exactly the expected content
    [Fact]
    [Trait("TestCase", "TC-039")]
    public async Task SendMessage_ConversationWithHistory_SendsOnlyPromptExerciseAttemptAndHistory()
    {
        var ollama = new FakeOllamaHandler();
        await using WebApplicationFactory<Program> api = factory.WithFakeOllama(ollama);
        LoggedInStudent student = await api.CreateLoggedInStudentAsync(grade: 8);
        HttpResponseMessage created = await student.Client.PostAsJsonAsync(ConversationsPath,
            new CreateTutorConversationRequest(ExerciseText, AttemptText, Guid.NewGuid()),
            TestContext.Current.CancellationToken);
        TutorConversationResponse conversation = (await created.Content.ReadFromJsonAsync<TutorConversationResponse>(
            TestContext.Current.CancellationToken))!;

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(
            $"{ConversationsPath}/{conversation.ConversationId}/messages",
            new SendTutorMessageRequest("Jeg ved ikke, hvad jeg skal gøre nu.", conversation.Version, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, ollama.RequestBodies.Count);
        JsonElement sent = JsonDocument.Parse(ollama.RequestBodies[1]).RootElement;
        Assert.Equal(["model", "stream", "messages"], sent.EnumerateObject().Select(p => p.Name));
        Assert.False(string.IsNullOrWhiteSpace(sent.GetProperty("model").GetString()));
        Assert.False(sent.GetProperty("stream").GetBoolean());
        JsonElement[] messages = sent.GetProperty("messages").EnumerateArray().ToArray();
        Assert.All(messages, m => Assert.Equal(["role", "content"], m.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(["system", "user", "assistant", "user"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(TutorInstructions.SystemPrompt, messages[0].GetProperty("content").GetString());
        Assert.Equal($"Opgavetekst:\n{ExerciseText}\n\nDet har eleven prøvet:\n{AttemptText}",
            messages[1].GetProperty("content").GetString());
        Assert.Equal(FakeOllamaHandler.Reply, messages[2].GetProperty("content").GetString());
        Assert.Equal("Jeg ved ikke, hvad jeg skal gøre nu.", messages[3].GetProperty("content").GetString());
    }

    // There is no filtering, so a name the student writes in the exercise text reaches the model unchanged.
    // The test documents known behaviour
    [Fact]
    [Trait("TestCase", "TC-040")]
    public async Task CreateConversation_StudentWritesOwnName_SendsTextUnchanged()
    {
        const string exerciseWithName = "Jeg hedder Sofie. Hvad er 3/4 af 20?";
        var ollama = new FakeOllamaHandler();
        await using WebApplicationFactory<Program> api = factory.WithFakeOllama(ollama);
        LoggedInStudent student = await api.CreateLoggedInStudentAsync(grade: 8);

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(ConversationsPath,
            new CreateTutorConversationRequest(exerciseWithName, AttemptText, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement sent = JsonDocument.Parse(Assert.Single(ollama.RequestBodies)).RootElement;
        Assert.Equal($"Opgavetekst:\n{exerciseWithName}\n\nDet har eleven prøvet:\n{AttemptText}",
            sent.GetProperty("messages")[1].GetProperty("content").GetString());
    }
}