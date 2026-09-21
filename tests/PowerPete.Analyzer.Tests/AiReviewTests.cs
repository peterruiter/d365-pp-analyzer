namespace PowerPete.Analyzer.Tests;

using FluentAssertions;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Domain;
using Xunit;

/// <summary>
/// The one rule a model decides.
/// </summary>
/// <remarks>
/// Every other rule in the catalogue is a measurement and its tests are about arithmetic.
/// This one is a judgement, so its tests are about what happens when the judgement is
/// unusable: a model that says nothing, answers with prose, or condemns a description
/// without saying why. All three have to produce no finding, because a low severity finding
/// nobody can explain is worse than a missing one.
/// </remarks>
public sealed class AiReviewTests
{
    /// <summary>A model that says whatever the test needs it to.</summary>
    /// <param name="answer">Its reply, or null for no reply at all.</param>
    private sealed class Says(string? answer) : IReviewModel
    {
        public string Name => "test-model";

        public int Calls { get; private set; }

        public Task<string?> AskAsync(string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private static DiscoveredComponent Component(string name, string? description, bool managed = false) =>
        new(Guid.NewGuid(), $"table:{name}", "table", name, name, null, "nwu_core", managed, null,
            new Dictionary<string, object?> { ["description"] = description });

    [Fact]
    public async Task Reports_a_description_the_model_says_is_empty_of_meaning()
    {
        var model = new Says("""{"informative": false, "reason": "Repeats the component name."}""");

        var findings = await new DescriptionReview(model)
            .RunAsync([Component("Account", "Account")], CancellationToken.None);

        findings.Should().ContainSingle();
        findings[0].RuleId.Should().Be("quality.descriptionUninformative");

        // The model's own sentence, and the fact that a model produced it. A consultant
        // reading this to a client has to know which kind of claim it is.
        findings[0].Evidence["whyItSaysNothing"].Should().Be("Repeats the component name.");
        findings[0].Evidence["judgedBy"].Should().Be("test-model");
    }

    [Fact]
    public async Task Says_nothing_about_a_description_the_model_accepts()
    {
        var model = new Says("""{"informative": true}""");

        var findings = await new DescriptionReview(model)
            .RunAsync([Component("Account", "Customers we bill, as opposed to sites we serve.")], CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("It looks like a placeholder to me.")]
    [InlineData("""{"informative": false}""")]
    [InlineData("""{"informative": "no", "reason": "hm"}""")]
    public async Task Produces_nothing_from_an_answer_it_cannot_use(string? answer)
    {
        // No answer, prose instead of JSON, a verdict with no reason, a verdict that is not
        // a boolean. Each one is a finding this product would not be able to defend, so
        // each one produces silence and the rule reports as not assessed on the evidence
        // source instead.
        var model = new Says(answer);

        var findings = await new DescriptionReview(model)
            .RunAsync([Component("Account", "asdf")], CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Asks_nothing_about_managed_components_or_empty_descriptions()
    {
        // A managed component's description belongs to whoever shipped it. An empty one is
        // already a different rule, and asking a model about it would report the same
        // component twice for the same reason.
        var model = new Says("""{"informative": false, "reason": "Nothing here."}""");

        var findings = await new DescriptionReview(model).RunAsync(
            [
                Component("Managed thing", "x", managed: true),
                Component("Empty", null),
                Component("Blank", "   "),
                Component("Short", "ok"),
            ],
            CancellationToken.None);

        findings.Should().BeEmpty();
        model.Calls.Should().Be(0, "none of these is worth a model call");
    }

    [Fact]
    public async Task Runs_at_all_only_when_there_is_a_model()
    {
        var review = new DescriptionReview(null);

        review.CanRun.Should().BeFalse();
        (await review.RunAsync([Component("Account", "asdf")], CancellationToken.None)).Should().BeEmpty();
    }
}
