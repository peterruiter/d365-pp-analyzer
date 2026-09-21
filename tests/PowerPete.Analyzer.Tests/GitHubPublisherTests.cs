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

    /// <summary>Refuses the write exactly as GitHub refuses a token without Issues: write.</summary>
    private sealed class Refuses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            var refusal = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"message":"Resource not accessible by personal access token"}""",
                    Encoding.UTF8,
                    "application/json")
            };

            // The header GitHub actually sends on this refusal, naming what it wanted.
            refusal.Headers.Add("X-Accepted-GitHub-Permissions", "issues=write");

            return Task.FromResult(refusal);
        }
    }

    /// <summary>Takes the issues and refuses the nesting, as a repository without sub-issues does.</summary>
    private sealed class NoSubIssues : HttpMessageHandler
    {
        private int next = 100;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith("/sub_issues", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"message":"Not Found"}""", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            var number = next++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"number":{{number}},"id":{{number * 10}},"html_url":"https://github.com/c/r/issues/{{number}}"}""",
                    Encoding.UTF8,
                    "application/json")
            });
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
        result.Items.Should().OnlyContain(entry => entry.Action == "skipped");
        result.Warnings.Should().BeEmpty("nothing was attempted, so nothing half worked");
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

    [Fact]
    public async Task Publishes_everything_and_says_so_when_the_shape_cannot_be_kept()
    {
        var publisher = new GitHubPublisher(new HttpClient(new NoSubIssues()), "contoso", "platform");

        var result = await publisher.PublishAsync(
            [Item("ppa-parent", "epic"), Item("ppa-child", "task", parent: "ppa-parent")],
            false, 2, CancellationToken.None);

        // Never fatal. A publish that stopped half way through is considerably worse than a
        // flat backlog, and the item is already in the repository by the time this is known.
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(entry => entry.Action == "created");

        // And never silent, which is what it used to be. An epic with nothing under it looks
        // exactly like an epic that was never meant to have children, so a backlog that
        // arrived flat has to say it arrived flat. This is the product's own rule about a
        // partial read, applied to a partial write.
        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Should().Contain("1 of 1");
        result.Warnings[0].Should().Contain("contoso/platform");
        result.Warnings[0].Should().Contain("Sub-issues are not available");
    }

    [Fact]
    public async Task Does_not_complain_when_a_child_is_already_where_it_belongs()
    {
        // 422 is what a re-publish gets for every child that was nested the first time, so
        // treating it as a problem would put a warning on every successful second publish.
        var handler = new Refuses422();
        var publisher = new GitHubPublisher(new HttpClient(handler), "contoso", "platform");

        var result = await publisher.PublishAsync(
            [Item("ppa-parent", "epic"), Item("ppa-child", "task", parent: "ppa-parent")],
            false, 2, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>Answers the nesting with 422, which is what "already a sub-issue" looks like.</summary>
    private sealed class Refuses422 : HttpMessageHandler
    {
        private int next = 100;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith("/sub_issues", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            var number = next++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"number":{{number}},"id":{{number * 10}},"html_url":"https://github.com/c/r/issues/{{number}}"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    [Fact]
    public async Task Says_what_github_asked_for_when_it_refuses_the_write()
    {
        var publisher = new GitHubPublisher(new HttpClient(new Refuses()), "contoso", "platform");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync([Item("ppa-0011223344556677", "task")], false, 1, CancellationToken.None));

        // GitHub names the permission it wanted in a header on this exact refusal. The first
        // version of this message guessed instead, and offered an archived repository as
        // equally likely: the picker excludes archived repositories, so that half of the
        // sentence sent the first person to hit it looking at something impossible.
        refusal.Message.Should().Contain("issues=write");

        // And the reason it is confusing: the connection test passes with a token that
        // cannot do this, because listing repositories needs only Metadata: Read.
        refusal.Message.Should().Contain("Issues: Read and write");
        refusal.Message.Should().Contain("connection test");

        refusal.Message.Should().NotContain("archived",
            "the repository picker already excludes archived repositories, so naming one here "
            + "sends somebody to check a thing the product has guaranteed");

        // GitHub's own words survive too. They are what somebody will paste into a search.
        refusal.Message.Should().Contain("Resource not accessible by personal access token");
    }
}
