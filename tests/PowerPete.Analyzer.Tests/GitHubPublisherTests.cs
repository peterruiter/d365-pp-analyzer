namespace PowerPete.Analyzer.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PowerPete.Analyzer.DevOps;
using Xunit;

/// <summary>
/// What the GitHub publisher actually sends.
/// </summary>
/// <remarks>
/// There is no repository to point this at, so the round trip is not what is being tested.
/// What is testable without one, and is the part most likely to be wrong, is the request it
/// builds: whether the deterministic key that makes a re-publish idempotent goes on the
/// issue, whether it is looked up somewhere that can actually find it, and whether the
/// parent link carries the identifier GitHub wants rather than the one that reads correctly
/// to a person.
///
/// Every one of those fails silently. A missing label does not error, it creates a second
/// copy of the whole backlog the next time somebody publishes. A lookup through the search
/// index does not error either: it works in a test, works by hand, and duplicates the
/// backlog for anybody who publishes twice inside the minute the index takes to catch up.
/// And a sub-issue call given an issue number where a global id belongs does not fail, it
/// adopts an unrelated issue in somebody else's repository.
/// </remarks>
public class GitHubPublisherTests
{
    /// <summary>Records what was sent and answers as GitHub would.</summary>
    private sealed class Recorder(bool alreadyThere = false) : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body)> Sent { get; } = [];

        /// <summary>Numbers and ids are different values here, so swapping them is visible.</summary>
        private int next = 100;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var url = request.RequestUri!.ToString();
            Sent.Add((request.Method.Method, url, body));

            string json;

            if (url.Contains("/labels", StringComparison.Ordinal))
            {
                json = """[{"name":"needs-triage"},{"name":"team-alpha"}]""";
            }
            else if (request.Method == HttpMethod.Get && url.Contains("labels=", StringComparison.Ordinal))
            {
                json = alreadyThere ? """[{"number":42,"id":9042,"html_url":"https://github.com/c/r/issues/42"}]""" : "[]";
            }
            else if (url.EndsWith("/sub_issues", StringComparison.Ordinal))
            {
                json = "{}";
            }
            else
            {
                var number = next++;
                json = $$"""{"number":{{number}},"id":{{number * 10}},"html_url":"https://github.com/c/r/issues/{{number}}"}""";
            }

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

    private static (GitHubPublisher Publisher, Recorder Recorder) Build(bool alreadyThere = false)
    {
        var recorder = new Recorder(alreadyThere);

        return (new GitHubPublisher(new HttpClient(recorder), "contoso", "platform"), recorder);
    }

    private static JsonElement Sent(string body) => JsonDocument.Parse(body).RootElement.Clone();

    [Fact]
    public async Task Puts_the_deterministic_key_on_the_issue_as_a_label()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None);

        var create = recorder.Sent.Single(sent => sent.Method == "POST");
        var labels = Sent(create.Body).GetProperty("labels").EnumerateArray()
            .Select(label => label.GetString()).ToList();

        // Without this the next publish finds nothing and creates the whole backlog again.
        labels.Should().Contain("ppa-0011223344556677");
        labels.Should().Contain("analyzer", "the builder's own tags travel with it");

        // GitHub has no work item types, so this is the only thing separating an epic from
        // a task once the backlog has landed.
        labels.Should().Contain("task");
    }

    [Fact]
    public async Task Looks_for_an_existing_issue_where_it_can_actually_be_found()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None);

        var lookup = recorder.Sent.First(sent => sent.Method == "GET");

        // Not /search/issues. GitHub's search index lags a write by seconds to minutes, so
        // a lookup through it finds nothing for anybody who publishes twice in a row and
        // duplicates every item. The issues endpoint reads the repository itself.
        lookup.Url.Should().NotContain("/search/");
        lookup.Url.Should().Contain("/repos/contoso/platform/issues");
        lookup.Url.Should().Contain("labels=ppa-0011223344556677");

        // A closed issue is still an issue. Publishing a second copy beside one somebody
        // closed is the failure the whole key mechanism exists to prevent.
        lookup.Url.Should().Contain("state=all");
    }

    [Fact]
    public async Task Links_a_child_to_its_parent_by_the_identifier_github_wants()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync(
            [Item("ppa-parent", "epic"), Item("ppa-child", "task", parent: "ppa-parent")],
            false, 2, CancellationToken.None);

        var adopt = recorder.Sent.Single(sent => sent.Url.EndsWith("/sub_issues", StringComparison.Ordinal));

        // The parent is addressed by its number, which is what the path takes, and the
        // child by its global id, which is what the body takes. The recorder deliberately
        // gives an issue a number and an id that differ, because these two being the same
        // value in real life for small repositories is how a swap survives a test.
        adopt.Url.Should().Contain("/issues/100/sub_issues");
        Sent(adopt.Body).GetProperty("sub_issue_id").GetInt64().Should().Be(1010);
    }

    [Fact]
    public async Task Keeps_labels_somebody_else_put_on_the_issue()
    {
        var (publisher, recorder) = Build(alreadyThere: true);

        await publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None);

        var update = recorder.Sent.Single(sent => sent.Method == "PATCH");
        var labels = Sent(update.Body).GetProperty("labels").EnumerateArray()
            .Select(label => label.GetString()).ToList();

        // GitHub replaces the label set rather than merging it, so sending only ours would
        // silently strip whatever a team had added. A publish is not a reason to undo
        // somebody's triage.
        labels.Should().Contain("needs-triage");
        labels.Should().Contain("team-alpha");
        labels.Should().Contain("ppa-0011223344556677", "and ours still has to be there to find it next time");
    }

    [Fact]
    public async Task Never_touches_the_state_of_an_issue_that_already_exists()
    {
        var (publisher, recorder) = Build(alreadyThere: true);

        await publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None);

        var update = recorder.Sent.Single(sent => sent.Method == "PATCH");

        // Somebody closed it for a reason and this product does not know what it was. The
        // same rule the other two publishers keep.
        Sent(update.Body).TryGetProperty("state", out _).Should().BeFalse();
        Sent(update.Body).TryGetProperty("assignees", out _).Should().BeFalse();
        Sent(update.Body).TryGetProperty("milestone", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Carries_the_estimate_into_the_body_because_an_issue_has_nowhere_for_it()
    {
        var (publisher, recorder) = Build();

        await publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None);

        var body = Sent(recorder.Sent.Single(sent => sent.Method == "POST").Body)
            .GetProperty("body").GetString()!;

        // Hours are what this product is for. Azure DevOps has a field, Jira has a custom
        // field somebody has to create, and GitHub has neither, so the body is the only
        // honest place left.
        body.Should().Contain("4–16 hours");
        body.Should().Contain("Acceptance criteria");
        body.Should().Contain("How to prove it");

        // Markdown, not the builder's HTML. GitHub renders a subset of HTML and the day the
        // builder emits a tag outside it, the body renders as source in a client's repo.
        body.Should().NotContain("<h3>");
        body.Should().Contain("### Why it matters");
        body.Should().Contain("Because & so on.", "entities are decoded rather than passed through");
    }

    [Fact]
    public async Task Writes_nothing_at_all_on_a_dry_run()
    {
        var (publisher, recorder) = Build();

        var result = await publisher.PublishAsync(
            [Item("ppa-one", "task"), Item("ppa-two", "epic")], true, 2, CancellationToken.None);

        recorder.Sent.Should().BeEmpty("a dry run is the thing somebody presses to find out what would happen");
        result.Should().OnlyContain(entry => entry.Action == "skipped");
    }

    [Fact]
    public async Task Refuses_a_large_backlog_until_the_count_is_confirmed()
    {
        var (publisher, recorder) = Build();

        var many = Enumerable.Range(0, 201)
            .Select(index => Item($"ppa-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}", "task"))
            .ToList();

        // Nobody means to put two hundred issues in somebody else's repository, and the one
        // time it happens is the time the tool never gets used at that client again.
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(many, false, null, CancellationToken.None));

        refusal.Message.Should().Contain("201");
        refusal.Message.Should().Contain("contoso/platform");
        recorder.Sent.Should().BeEmpty();
    }
}
