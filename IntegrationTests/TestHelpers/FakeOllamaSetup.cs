using Application.Features.ChatTutor.AskTutor;
using Infrastructure.AI;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.TestHelpers;

public static class FakeOllamaSetup
{
    // A copy of the API where OllamaTutorModelClient sends its requests to the fake instead of the network.
    // It uses the test class's database, but has its own conversation store, so tests cannot see each other's conversations
    public static WebApplicationFactory<Program> WithFakeOllama(this MatMentorApiFactory factory, FakeOllamaHandler ollama)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<ITutorModelClient, OllamaTutorModelClient>()
                .ConfigurePrimaryHttpMessageHandler(() => ollama)));
}