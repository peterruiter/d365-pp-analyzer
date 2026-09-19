namespace PowerPete.Analyzer.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PowerPete.Analyzer.DevOps;
using Xunit;

/// <summary>
/// What the Jira publisher actually sends.
/// </summary>
/// <remarks>
/// There is no Jira site to point this at, so the round trip is not what is being tested.
/// What is testable without one, and is the part most likely to be wrong, is the request it
/// builds: the issue type it picks for a project that does not have the type we asked for,
/// the document format the description has to be converted into, and whether the
/// deterministic key that makes a re-publish idempotent actually goes on the issue.
///
/// Every one of those is a decision made before anything is sent, and every one of them
/// fails in a way a smoke test would not catch: a missing label does not error, it creates
/// a second copy of the whole backlog the next time somebody publishes.
/// </remarks>
public class JiraPublisherTests
{
    /// <summary>Records what was sent and answers as Jira would.</summary>
    private sealed class Recorder : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Sent.Add((request.Method.Method, request.RequestUri!.ToString(), body));

            // A search that finds nothing, so everything is a create. The update path is
            // the same field building with two keys removed.
            var json = request.RequestUri.AbsolutePath.Contains("/search/", StringComparison.Ordinal)
                ? """{"issues":[]}"""
                : """{"id":"10001","key":"ABC-1"}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static BacklogItem Item(
        string key, string type, string? parent = null, string? html = null) =>
        new(key, type, $"Title for {key}", html ?? "<h3>Why it matters</h3><p>Because &amp; so on.</p>",
            "<p>Given a thing</p><p>Then another</p>", "Prove it by testing", 2, 3, 4m, 16m,
            ["analyzer"], parent, []);

    private static (JiraPublisher Publisher, Recorder Recorder) Build(
        Dictionary<string, string>? types = null)
    {
        var recorder = new Recorder();

        return (new JiraPublisher(
            new HttpClient(recorder),
            "https://contoso.atlassian.net/",
            "ABC",
            types ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["epic"] = "1", ["story"] = "2", ["bug"] = "3", ["task"] = "4"
            }), recorder);
    }

    private static JsonElement Fields(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("fields").Clone();

    [Fact]
    public async Task Puts_the_deterministic_key_on_the_issue_as_a_label()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("pp-alm-001", "task")], "hash", "hash", false, 1, CancellationToken.None);

        var create = recorder.Sent.Single(sent => sent.Method == "POST");
        var labels = Fields(create.Body).GetProperty("labels").EnumerateArray()
            .Select(label => label.GetString()).ToList();

        // Without this the next publish finds nothing and creates the whole backlog again.
        labels.Should().Contain("pp-alm-001");
        labels.Should().Contain("analyzer", "the builder's own tags travel with it");
    }

    [Fact]
    public async Task Looks_for_an_existing_issue_before_creating_one()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("pp-alm-001", "task")], "hash", "hash", false, 1, CancellationToken.None);

        var search = recorder.Sent.First();

        search.Method.Should().Be("GET");
        search.Url.Should().Contain("labels", "the key is how a re-publish finds what it made");

        // Scoped to the project. The same backlog in two projects is two sets of issues,
        // so a key found elsewhere is not this one.
        Uri.UnescapeDataString(search.Url).Should().Contain("project = \"ABC\"");
    }

    [Theory]
    [InlineData("epic", "1")]
    [InlineData("story", "2")]
    [InlineData("bug", "3")]
    [InlineData("task", "4")]
    [InlineData("feature", "2")]
    public async Task Maps_our_types_onto_the_ones_the_project_has(string ours, string expected)
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("k", ours)], "hash", "hash", false, 1, CancellationToken.None);

        var create = recorder.Sent.Single(sent => sent.Method == "POST");

        // Jira has no Feature by default, so ours becomes a story rather than failing.
        Fields(create.Body).GetProperty("issuetype").GetProperty("id").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task Falls_back_rather_than_refusing_a_project_without_the_type()
    {
        // A team managed project with nothing but Task, which is a real shape and the one
        // that would otherwise fail a publish outright.
        var (publisher, recorder) = Build(new Dictionary<string, string>(StringComparer.Ordinal) { ["task"] = "9" });

        await publisher.PublishAsync([Item("k", "epic")], "hash", "hash", false, 1, CancellationToken.None);

        var create = recorder.Sent.Single(sent => sent.Method == "POST");

        Fields(create.Body).GetProperty("issuetype").GetProperty("id").GetString()
            .Should().Be("9", "a board somebody can work with beats a publish that refused");
    }

    [Fact]
    public async Task Turns_the_description_into_a_document_rather_than_sending_markup()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync(
            [Item("k", "task", html: "<h3>Why it matters</h3><p>A &amp; B<br/>C</p><ul><li>One</li></ul>")],
            "hash", "hash", false, 1, CancellationToken.None);

        var description = Fields(recorder.Sent.Single(sent => sent.Method == "POST").Body)
            .GetProperty("description");

        description.GetProperty("type").GetString().Should().Be("doc");
        description.GetProperty("version").GetInt32().Should().Be(1);

        var blocks = description.GetProperty("content").EnumerateArray().ToList();

        blocks[0].GetProperty("type").GetString().Should().Be("heading");

        var all = description.GetRawText();

        // Entities decoded and the tags gone. Sending "&amp;" to Jira prints it literally,
        // and sending a tag prints the tag.
        all.Should().Contain("A \\u0026 B C", "entities are decoded and a break is a space");
        all.Should().NotContain("<p>");
        all.Should().NotContain("&amp;");

        // The two fields that have nowhere of their own on a default Jira issue, and are
        // the half of a work item that says when it is finished.
        all.Should().Contain("Acceptance criteria");
        all.Should().Contain("Prove it by testing");
    }

    [Fact]
    public async Task Names_the_parent_it_just_created()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync(
            [Item("parent", "epic"), Item("child", "story", parent: "parent")],
            "hash", "hash", false, 2, CancellationToken.None);

        var creates = recorder.Sent.Where(sent => sent.Method == "POST").ToList();

        creates.Should().HaveCount(2);

        // The parent is created first and the child names the key it came back with.
        Fields(creates[0].Body).TryGetProperty("parent", out _).Should().BeFalse();
        Fields(creates[1].Body).GetProperty("parent").GetProperty("key").GetString().Should().Be("ABC-1");
    }

    [Fact]
    public async Task Refuses_a_backlog_that_changed_since_it_was_approved()
    {
        var (publisher, recorder) = Build();

        var attempt = async () => await publisher.PublishAsync(
            [Item("k", "task")], "approved-hash", "a-different-hash", false, 1, CancellationToken.None);

        await attempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed since it was approved*");

        recorder.Sent.Should().BeEmpty("nothing is sent when the approval does not match");
    }

    [Fact]
    public async Task Calls_nothing_on_a_dry_run()
    {
        var (publisher, recorder) = Build();

        var result = await publisher.PublishAsync(
            [Item("k", "task")], "hash", "hash", dryRun: true, confirmedCount: 1, CancellationToken.None);

        recorder.Sent.Should().BeEmpty();
        result.Should().ContainSingle().Which.Action.Should().Be("skipped");
    }

    [Fact]
    public async Task Takes_the_spaces_out_of_a_label()
    {
        var (publisher, recorder) = Build();

        var spaced = Item("k", "task") with { Tags = ["two words"] };

        await publisher.PublishAsync([spaced], "hash", "hash", false, 1, CancellationToken.None);

        var labels = Fields(recorder.Sent.Single(sent => sent.Method == "POST").Body)
            .GetProperty("labels").EnumerateArray().Select(label => label.GetString()).ToList();

        // Jira refuses a label containing whitespace, and it refuses the whole issue with
        // it rather than dropping the label.
        labels.Should().Contain("two-words");
        labels.Should().NotContain(label => label!.Contains(' ', StringComparison.Ordinal));
    }
}
