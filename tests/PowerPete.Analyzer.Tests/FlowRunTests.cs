namespace PowerPete.Analyzer.Tests;

using System.Net;
using System.Text;
using FluentAssertions;
using PowerPete.Analyzer.Dataverse;
using Xunit;

/// <summary>
/// Cloud flow run history, read from Dataverse's own flowrun table.
/// </summary>
/// <remarks>
/// This reader has no environment to be checked against, which is why it was left throwing
/// for so long. What can be checked is how it behaves when the environment answers in a
/// shape it did not expect, and that is the half that matters: quietly treating an
/// unrecognised status as "not a failure" would report a flow that fails every night as
/// running perfectly, and silence a critical severity rule on the one estate that needed it.
/// </remarks>
public class FlowRunTests
{
    private const string FlowId = "11111111-1111-1111-1111-111111111111";

    /// <summary>Answers whatever the reader asks for, from a table of canned bodies.</summary>
    private sealed class Canned(Func<string, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    respond(request.RequestUri!.ToString()), Encoding.UTF8, "application/json")
            });
    }

    /// <summary>One cloud flow and whatever runs the test wants against it.</summary>
    private static DataverseReader Reader(string runs)
    {
        var client = new HttpClient(new Canned(url =>
            url.Contains("WhoAmI", StringComparison.Ordinal)
                ? """{"UserId":"00000000-0000-0000-0000-000000000009"}"""
                : url.Contains("workflows", StringComparison.Ordinal)
                    ? $$"""
                        {"value":[{"workflowid":"{{FlowId}}","name":"Nightly reconciliation",
                        "category":5,"mode":0,"type":1,"statecode":1}]}
                        """
                    : url.Contains("flowruns", StringComparison.Ordinal)
                        ? runs
                        : """{"value":[]}"""))
        {
            BaseAddress = new Uri("https://contoso.crm4.dynamics.example")
        };

        return new DataverseReader(client);
    }

    private static string Run(string status, string? error = null, string start = "2026-09-01T10:00:00Z", string end = "2026-09-01T10:00:20Z") =>
        $$"""
          {"_workflow_value":"{{FlowId}}","starttime":"{{start}}","endtime":"{{end}}",
           "status@OData.Community.Display.V1.FormattedValue":"{{status}}",
           "errormessage":{{(error is null ? "null" : $"\"{error}\"")}}}
          """;

    [Fact]
    public async Task Counts_the_failures_against_the_runs_that_finished()
    {
        var reader = Reader($$"""{"value":[{{Run("Succeeded")}},{{Run("Succeeded")}},{{Run("Failed", "Timed out.")}},{{Run("Failed")}}]}""");

        var result = await reader.ReadAsync([], includeRuntime: true, progress: null, CancellationToken.None);

        var flow = result.Components.Single(component => component.TypeId == "cloudFlow");

        flow.Attribute<int?>("runCount30d").Should().Be(4);
        flow.Attribute<int?>("failureCount30d").Should().Be(2);
        flow.Attribute<decimal?>("failureRate30d").Should().Be(0.5m);
        flow.Attribute<string>("lastFailureMessage").Should().Be("Timed out.");

        result.Reads.Should().ContainSingle(read => read.ComponentTypeId == "flowRun" && read.Succeeded);
    }

    [Fact]
    public async Task Leaves_a_run_still_going_out_of_the_rate()
    {
        // A run in flight is not evidence that the flow works and not evidence that it does
        // not. Counting it either way moves a number somebody quotes.
        var reader = Reader($$"""{"value":[{{Run("Succeeded")}},{{Run("Failed")}},{{Run("Running", end: "")}},{{Run("Cancelled")}}]}""");

        var result = await reader.ReadAsync([], includeRuntime: true, progress: null, CancellationToken.None);
        var flow = result.Components.Single(component => component.TypeId == "cloudFlow");

        flow.Attribute<int?>("runCount30d").Should().Be(2);
        flow.Attribute<decimal?>("failureRate30d").Should().Be(0.5m);
    }

    [Fact]
    public async Task Refuses_the_whole_read_on_a_status_it_does_not_know()
    {
        // The dangerous case, and the reason this test file exists. An unrecognised status
        // counted as a success turns a failing flow into a healthy one.
        var reader = Reader($$"""{"value":[{{Run("Succeeded")}},{{Run("Bananas")}}]}""");

        var result = await reader.ReadAsync([], includeRuntime: true, progress: null, CancellationToken.None);

        var read = result.Reads.Single(entry => entry.ComponentTypeId == "flowRun");

        read.Succeeded.Should().BeFalse();
        read.FailureReason.Should().Contain("Bananas");

        // And nothing was written onto the flow, so the rules see no run history at all and
        // report as not assessed rather than working from half a tally.
        var flow = result.Components.Single(component => component.TypeId == "cloudFlow");
        flow.Has("failureRate30d").Should().BeFalse();
    }

    [Fact]
    public async Task Leaves_a_flow_with_no_runs_alone()
    {
        // Not stamped with a zero. The table does not retain runs forever, and "did not run"
        // and "no rows kept" would both read as a dormant flow.
        var reader = Reader("""{"value":[]}""");

        var result = await reader.ReadAsync([], includeRuntime: true, progress: null, CancellationToken.None);
        var flow = result.Components.Single(component => component.TypeId == "cloudFlow");

        flow.Has("runCount30d").Should().BeFalse();
    }

    [Fact]
    public async Task Says_nothing_was_read_when_runtime_was_not_asked_for()
    {
        var reader = Reader("""{"value":[]}""");

        var result = await reader.ReadAsync([], includeRuntime: false, progress: null, CancellationToken.None);
        var read = result.Reads.Single(entry => entry.ComponentTypeId == "flowRun");

        read.Succeeded.Should().BeFalse();
        read.FailureReason.Should().Contain("not assessed");
    }

    [Fact]
    public async Task Averages_only_the_runs_that_recorded_both_ends()
    {
        var reader = Reader($$"""
            {"value":[
              {{Run("Succeeded", start: "2026-09-01T10:00:00Z", end: "2026-09-01T10:00:10Z")}},
              {{Run("Succeeded", start: "2026-09-01T11:00:00Z", end: "2026-09-01T11:00:30Z")}}
            ]}
            """);

        var result = await reader.ReadAsync([], includeRuntime: true, progress: null, CancellationToken.None);
        var flow = result.Components.Single(component => component.TypeId == "cloudFlow");

        flow.Attribute<int?>("averageDurationMs").Should().Be(20_000);
    }
}
