using System.Net;
using System.Text;

namespace IntegrationTests.TestHelpers;

// Replaces the network between OllamaTutorModelClient and Ollama, the last step before the request leaves the backend.
// Records the body of every request and answers like Ollama with a fixed reply
public sealed class FakeOllamaHandler : HttpMessageHandler
{
    public const string Reply = "Hvad har du prøvet indtil nu?";

    private const string OllamaResponse = """{"message":{"content":"Hvad har du prøvet indtil nu?"}}""";

    private readonly List<string> _requestBodies = [];

    // Every request sent to Ollama, in order, so a test can check how many there were and what they contained
    public IReadOnlyList<string> RequestBodies => _requestBodies;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(OllamaResponse, Encoding.UTF8, "application/json")
        };
    }
}