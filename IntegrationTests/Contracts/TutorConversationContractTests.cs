using System.Net;
using System.Net.Http.Json;
using Application.Features.ChatTutor.Conversations;
using IntegrationTests.TestHelpers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Contracts;

// Requests are written as plain JSON from the contract, not with the backend's own request types.
// Ollama is replaced, so the tests check the backend's answers, not the model's
public class TutorConversationContractTests(MatMentorApiFactory factory) : IClassFixture<MatMentorApiFactory>
{
    private const string ConversationsPath = "/api/tutor-conversations";
    private const string MessagesPath = "/api/tutor-conversations/{conversationId}/messages";

    [Fact]
    [Trait("TestCase", "TC-035")]
    public async Task CreateConversation_ValidRequest_MatchesApprovedContract()
    {
        await using WebApplicationFactory<Program> api = factory.WithFakeOllama(new FakeOllamaHandler());
        LoggedInStudent student = await api.CreateLoggedInStudentAsync(grade: 8);

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(ConversationsPath,
            new { exerciseText = "Hvad er 3/4 af 20?", attemptText = "Jeg har ganget 20 med 3.", requestId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", ConversationsPath);
    }

    [Fact]
    [Trait("TestCase", "TC-047")]
    public async Task SendMessage_ValidRequest_MatchesApprovedContract()
    {
        await using WebApplicationFactory<Program> api = factory.WithFakeOllama(new FakeOllamaHandler());
        LoggedInStudent student = await api.CreateLoggedInStudentAsync(grade: 8);
        HttpResponseMessage created = await student.Client.PostAsJsonAsync(ConversationsPath,
            new CreateTutorConversationRequest("Hvad er 3/4 af 20?", "Jeg har ganget 20 med 3.", Guid.NewGuid()),
            TestContext.Current.CancellationToken);
        TutorConversationResponse conversation = (await created.Content.ReadFromJsonAsync<TutorConversationResponse>(
            TestContext.Current.CancellationToken))!;

        HttpResponseMessage response = await student.Client.PostAsJsonAsync(
            $"{ConversationsPath}/{conversation.ConversationId}/messages",
            new { content = "Jeg ved ikke, hvad jeg skal gøre nu.", expectedVersion = conversation.Version, messageId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await OpenApiContract.AssertMatchesAsync(response, "POST", MessagesPath);
    }
}
