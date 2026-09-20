namespace PowerPete.Analyzer.Pipeline;

using System.Globalization;
using PowerPete.Analyzer.Analysis;
using PowerPete.Analyzer.Analysis.Handlers;
using PowerPete.Analyzer.DevOps;
using PowerPete.Analyzer.Domain;
using PowerPete.Analyzer.Extraction;

/// <summary>
/// The demonstration engagement everybody can open.
/// </summary>
/// <remarks>
/// A product whose every screen is empty until somebody has a client environment, a service
/// principal and a security review is a product nobody can evaluate, demonstrate or learn.
/// This is a synthetic Power Platform estate, shaped like a mid sized utility that has been
/// building on Dataverse for eight years, so the overview, the inventory, the findings, the
/// roadmap, the backlog and both documents have something real to render on a first sign in.
///
/// Nothing in it comes from a client. The tables, the flows and the people are invented, and
/// obviously so. The findings are not invented: they are produced by running the product's
/// own rule engine, scorer, complexity rater, roadmap builder and backlog builder over the
/// invented estate. What the demonstration shows is therefore what this product would say
/// about an estate that looked like this, which is the only kind of demonstration worth
/// giving. A hand written finding is a claim about the product that the product has not
/// made, and the first time a rule changes it becomes a lie on a screen in front of a client.
///
/// The estate is built deterministically, from a fixed sequence rather than a seeded
/// <see cref="Random"/>, because the backlog hash is computed from what comes out of here.
/// A hash that changes between two containers running the same version makes a backlog look
/// edited when nothing touched it.
/// </remarks>
public static class DemoEstate
{
    /// <summary>Stable, so the seeder can find what it wrote last time.</summary>
    /// <remarks>Held twice: the data layer grants everybody read on this identifier and does not reference this project. A test holds the two together.</remarks>
    public static readonly Guid EngagementId = Guid.Parse("de300000-0000-4000-8000-000000000001");

    /// <summary>The connection it pretends to have read through.</summary>
    public static readonly Guid ConnectionId = Guid.Parse("de300000-0000-4000-8000-000000000002");

    /// <summary>The run that produced everything below.</summary>
    public static readonly Guid RunId = Guid.Parse("de300000-0000-4000-8000-000000000003");

    /// <summary>
    /// Bumped whenever the shape of the demonstration changes.
    /// </summary>
    /// <remarks>
    /// The seeder rebuilds when the stamped version differs from this one, rather than
    /// skipping itself the moment the engagement exists. That is the difference between a
    /// demonstration that gains whatever a later release added and one frozen at whatever it
    /// looked like the first time the container started.
    /// </remarks>
    public const int SeedVersion = 4;

    /// <summary>What it is called.</summary>
    public const string Name = "Demonstration estate";

    /// <summary>Who it is for. Invented, and labelled as such on every screen that shows it.</summary>
    public const string ClientName = "Northwind Utilities (sample)";

    /// <summary>
    /// Production, because most of the catalogue is scoped to it.
    /// </summary>
    /// <remarks>
    /// Rules that only fire against production do not fire against an environment whose role
    /// nobody set. A demonstration on an unknown role would show half the catalogue as not
    /// assessed and would be showing a configuration mistake rather than the product.
    /// </remarks>
    public const string EnvironmentRole = "production";

    /// <summary>The name on the connection.</summary>
    public const string ConnectionName = "Northwind production (sample)";

    private const string MainSolution = "nwu_CustomerCore";
    private const string FieldSolution = "nwu_FieldService";
    private const string IsvSolution = "ISVSmartPortal";
    private const string DefaultSolution = "Default";

    /// <summary>
    /// Everything the demonstration engagement holds, computed rather than stored.
    /// </summary>
    /// <param name="Solutions">The solutions the components came out of.</param>
    /// <param name="Components">The inventory.</param>
    /// <param name="Links">Resolved references between components.</param>
    /// <param name="Unresolved">References pointing outside the inventory.</param>
    /// <param name="Reads">One record per component type read, as the extraction would have written them.</param>
    /// <param name="Findings">What the rules found, with their estimates.</param>
    /// <param name="NotAssessed">What could not be checked, and why.</param>
    /// <param name="Score">The numbers.</param>
    /// <param name="Customisation">The components by customisation chart.</param>
    /// <param name="Roadmap">The roadmap grid.</param>
    /// <param name="Backlog">The work items.</param>
    /// <param name="BacklogHash">What the backlog stage records, so a changed backlog is visible as a changed hash.</param>
    public sealed record Result(
        IReadOnlyList<SolutionZipReader.SolutionHeader> Solutions,
        IReadOnlyList<DiscoveredComponent> Components,
        IReadOnlyList<ComponentLink> Links,
        IReadOnlyList<UnresolvedLink> Unresolved,
        IReadOnlyList<(string ComponentTypeId, string EvidenceSource, bool Succeeded, int? Count, string? Reason)> Reads,
        IReadOnlyList<(Finding Finding, Estimate Estimate)> Findings,
        IReadOnlyList<NotAssessed> NotAssessed,
        RunScore Score,
        IReadOnlyList<CustomisationRow> Customisation,
        IReadOnlyList<RoadmapItem> Roadmap,
        IReadOnlyList<BacklogItem> Backlog,
        string BacklogHash);

    /// <summary>
    /// The scores a consultant would have brought out of a workshop.
    /// </summary>
    /// <remarks>
    /// The one part of this demonstration that is not produced by running the product, and
    /// it cannot be: the functional maturity section has no automated input by design,
    /// because a readiness score generated from metadata reads exactly like one produced
    /// from twenty interviews and is worth nothing. The contract says so, and the report
    /// prints the prompt wherever nobody has written an answer.
    ///
    /// Which left the demonstration showing an empty radar and five empty boxes, so the
    /// half of the report a client actually reads was the half nobody could see. These are
    /// therefore invented, like the rest of the estate, and shaped like what a real
    /// engagement produces: an organisation that has built a lot and governed little,
    /// strong where the business asked for something directly and weak underneath.
    ///
    /// Two axes are deliberately unscored. An axis nobody asked about has to draw as a
    /// spoke with no point on it, because nought means the capability is absent and not
    /// having looked is a different statement. A demonstration where every axis happened to
    /// be scored would never show that difference.
    /// </remarks>
    public static IReadOnlyList<(string Axis, decimal? Score, string? Evidence)> Maturity { get; } =
    [
        ("campaignManagement", 2.0m, "Campaigns run out of a marketing tool whose owner nobody in the workshop could name."),
        ("leadManagement", 2.5m, "Leads arrive by mail and are keyed in. The team described this as temporary in 2019."),
        ("customerFeedbackLoops", 1.5m, "Surveys are sent. Nobody could say what happens to the answers."),
        ("pipelineManagement", 3.0m, "Consistent and used, though the stages were last reviewed three years ago."),
        ("salesTeamManagement", 3.5m, "Clear ownership, and a manager who reviews it weekly."),
        ("onboarding", 2.0m, "Documented for the last intake and not since."),
        ("customerSegmentation", null, "Not covered. Nobody in the room owns it."),
        ("serviceRequestManagement", 4.0m, "The strongest thing in the estate. Well modelled, well used, and the team knows why."),
        ("omnichannel", 3.0m, "Voice and mail are joined. Chat is a separate queue with a separate rota."),
        ("contactCentre", 3.5m, "Mature, and the source of most of the customisation this report found."),
        ("customerSatisfaction", 2.5m, "Measured per channel, never aggregated."),
        ("selfService", 1.5m, "A portal exists. Deflection has not been measured since it launched."),
        ("customer360", 2.0m, "Four systems hold an address. Three of them disagree."),
        ("biAndAnalytics", 2.5m, "Good operational reporting, no single view above it."),
        ("businessAdoption", 3.0m, "High where the business asked for it, low where IT delivered it unasked."),
        ("dataQuality", null, "Not covered. The workshop ran out of time, which is itself worth reporting.")
    ];

    /// <summary>
    /// The paragraphs a consultant would have written over the generated numbers.
    /// </summary>
    /// <remarks>
    /// Same reasoning as the scores. A hybrid section prints generated numbers with a
    /// written paragraph over them, and with nothing written the demonstration showed the
    /// numbers and a prompt, which demonstrates the prompt rather than the product.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Narrative { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["managementSummary"] =
                "Northwind has built a great deal on Dataverse and governed almost none of it. That is not a "
                + "criticism of the people who built it: every one of the things this report calls debt was the "
                + "fastest correct answer at the time, and the estate works. What changed is the volume. Twelve "
                + "custom tables, four solutions and an unmanaged production environment were manageable when "
                + "two people knew all of it, and both of those people have moved on.\n\n"
                + "The one thing to fix first is the unmanaged solution in production. Everything else here can "
                + "be scheduled; that one removes the ability to schedule anything, because there is currently "
                + "no path from a change to production that anybody can describe or reverse.",

            ["delivery"] =
                "There is no deployment pipeline. Changes are made in the production environment and exported "
                + "afterwards if somebody remembers, which the team described without embarrassment because it "
                + "has always been that way and has not yet caused a visible failure.\n\n"
                + "The development environment is a copy taken in 2023 and has drifted far enough that the team "
                + "no longer trusts it for testing. In practice production is the test environment, and the "
                + "reason nothing has broken badly is that the two people who understood the dependencies were "
                + "careful. They have both left.",

            ["functionalMaturity"] =
                "Scored with the service lead, the CRM product owner and two agents, over a half day. The "
                + "pattern is consistent and unsurprising: the capabilities the business asked for directly are "
                + "the mature ones, and everything underneath them is weaker than the people using it realise.\n\n"
                + "Service request management at four is genuine and worth protecting. Customer 360 at two is "
                + "the constraint behind most of the frustration reported elsewhere in the workshop, and it will "
                + "not improve as a side effect of anything else on the roadmap.\n\n"
                + "Two axes are not scored. Customer segmentation had no owner in the room, and data quality was "
                + "not reached. Neither is nought, and neither should be read as one.",

            ["readiness"] =
                "The organisation is readier than it thinks for the technical work and less ready than it thinks "
                + "for the governance that has to come with it. Nobody objected to managed solutions or a "
                + "deployment pipeline; several people assumed somebody else was already doing it.\n\n"
                + "The real risk is capacity rather than willingness. The two people who could do this work are "
                + "the same two who keep the current estate running, and no plan that assumes otherwise will "
                + "survive its first month.",

            ["scenarios"] =
                "The client is choosing between two things, and described them in these words.\n\n"
                + "Stabilise first. Managed solutions, a pipeline, and a development environment that can be "
                + "trusted, before any new capability. It gives them a platform they can change safely. It needs "
                + "a quarter with no new features, which the business has not yet agreed to. The worry is that "
                + "the quarter gets cut in half and they are left with a pipeline nobody finished.\n\n"
                + "Build the self service portal. It gives them the deflection the service director has been "
                + "asked for. It needs the same two people. The worry, which the service lead raised himself, "
                + "is that it adds a third front end to a customer record that three systems already disagree "
                + "about."
        };

    /// <summary>
    /// Builds the estate and runs the product over it.
    /// </summary>
    /// <remarks>
    /// Pure. No database, no network, no clock beyond the dates written into the estate
    /// itself, which is what lets a test assert on the demonstration without a deployment.
    /// </remarks>
    public static Result Build()
    {
        var estate = new EstateBuilder();
        estate.Populate();

        var components = estate.Components;
        var links = estate.Links;

        var context = new AnalysisContext(
            components,
            links,
            estate.Unresolved,

            // Everything, including runtime. A demonstration run through an application user
            // with the trace privilege is the configuration this product recommends, and it
            // is the one worth showing. The catalogue still has rules it cannot answer here,
            // and the not assessed section below is theirs.
            new Reach([EvidenceSource.Metadata, EvidenceSource.SolutionZip, EvidenceSource.Checker, EvidenceSource.Runtime]),
            EnvironmentRole,
            estate.CheckerIssues,

            // One environment connected, which is the usual state of an engagement at the
            // point somebody first runs a discovery. The environment separation rule needs
            // two before it can say anything and therefore finds nothing here, which is the
            // correct answer rather than a clean bill: it reported on what it could see.
            environmentCount: 1);

        var engine = new RuleEngine([.. typeof(RuleEngine).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(IRuleHandler)))
            .Select(type => (IRuleHandler)Activator.CreateInstance(type)!)]);

        var outcome = engine.Run(context);

        // Band defaults rather than model estimates. The estimator calls Azure OpenAI, and a
        // demonstration that cannot be rebuilt without a model deployment, a quota and a bill
        // is one that breaks in the deployment where it matters most. These are the same
        // numbers a quick scan produces, from the same contract.
        var findings = outcome.Findings
            .Select(finding => (
                Finding: finding,
                Estimate: (EstimateCatalogue.Bands.TryGetValue(finding.Rule?.EstimateBand ?? "none", out var band)
                    ? band
                    : EstimateCatalogue.Bands["none"]).AsEstimate()))
            .ToList();

        var score = Scorer.Score(
            components,
            findings,
            outcome.NotAssessed,
            [.. EstimateCatalogue.FixedCosts.Select(cost => new FixedCost(cost.Id, cost.Name, cost.Low, cost.High))],
            solutionsAnalysed: estate.Solutions.Count,
            solutionsTotal: estate.Solutions.Count + 2);

        var customisation = new ComplexityRater(ComplexityRule.FromContract()).ByCustomisation(components);

        var roadmap = new RoadmapBuilder(RuleCatalogue.Roadmap.ToDictionary(
            entry => entry.Key,
            entry => new RoadmapPosition(entry.Value.Row, entry.Value.Column, entry.Value.Band),
            StringComparer.Ordinal)).Build(findings);

        var byKey = components.ToDictionary(component => component.StableKey, StringComparer.Ordinal);

        var backlog = new BacklogBuilder(EngagementId, Name, AcceptanceCriteria.Load()).Build(
            [.. findings.Select(entry => (
                entry.Finding,
                entry.Estimate,
                entry.Finding.ComponentKey is null ? null : byKey.GetValueOrDefault(entry.Finding.ComponentKey)))],
            $"Produced by the Power Platform Solution Analyzer from the demonstration estate, version {SeedVersion.ToString(CultureInfo.InvariantCulture)}.");

        var hash = BacklogHash.Of(backlog.Select(item =>
            (item.Key, item.Title, item.AcceptanceCriteria, item.LowHours ?? 0, item.HighHours ?? 0, item.StoryPoints)));

        return new Result(
            estate.Solutions,
            components,
            links,
            estate.Unresolved,
            estate.Reads,
            findings,
            outcome.NotAssessed,
            score,
            customisation,
            roadmap.Items,
            backlog,
            hash);
    }

    /// <summary>
    /// The invented estate.
    /// </summary>
    /// <remarks>
    /// Written as a sequence of deliberate decisions rather than generated from random
    /// numbers. Every component here is either a plain instance of its type, which is what
    /// makes the counts look like somewhere real, or it carries a specific defect, which is
    /// what gives the rules something to find. Both are commented where the reason is not
    /// obvious from the values.
    /// </remarks>
    private sealed class EstateBuilder
    {
        private readonly List<DiscoveredComponent> components = [];
        private readonly List<ComponentLink> links = [];
        private readonly List<UnresolvedLink> unresolved = [];
        private readonly List<SolutionZipReader.SolutionHeader> solutions = [];
        private readonly List<CheckerIssue> checkerIssues = [];
        private readonly List<(string, string, bool, int?, string?)> reads = [];

        public IReadOnlyList<DiscoveredComponent> Components => components;

        public IReadOnlyList<ComponentLink> Links => links;

        public IReadOnlyList<UnresolvedLink> Unresolved => unresolved;

        public List<SolutionZipReader.SolutionHeader> Solutions => solutions;

        public IReadOnlyList<CheckerIssue> CheckerIssues => checkerIssues;

        public IReadOnlyList<(string ComponentTypeId, string EvidenceSource, bool Succeeded, int? Count, string? Reason)> Reads =>
            [.. reads.Select(read => (read.Item1, read.Item2, read.Item3, read.Item4, read.Item5))];

        /// <summary>A stable identifier, so two builds produce the same estate.</summary>
        /// <remarks>
        /// Derived from the name rather than allocated, because a <see cref="Guid.NewGuid"/>
        /// here would change every stable key on every container start, and a stable key that
        /// is not stable takes every override and every published work item with it.
        /// </remarks>
        private static string PlatformId(string typeId, string name)
        {
            // A version 4 shaped identifier built out of the name's hash, so it reads like a
            // platform identifier in the interface rather than like a slug.
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{typeId}|{name}"))[..16];

            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes).ToString();
        }

        private DiscoveredComponent Add(
            string typeId,
            string displayName,
            string? schemaName,
            string solution,
            bool isManaged,
            params (string Key, object? Value)[] attributes)
        {
            var platformId = PlatformId(typeId, schemaName ?? displayName);

            var component = new DiscoveredComponent(
                Guid.Parse(platformId),
                StableKeys.ForComponent(typeId, platformId, schemaName),
                typeId,
                displayName,
                schemaName,
                platformId,
                solution,
                isManaged,
                null,
                attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

            components.Add(component);
            return component;
        }

        private void Link(DiscoveredComponent from, DiscoveredComponent to, string kind) =>
            links.Add(new ComponentLink(from.StableKey, to.StableKey, kind));

        private void Read(string typeId, string evidence, int count) =>
            reads.Add((typeId, evidence, true, count, null));

        public void Populate()
        {
            BuildSolutions();
            var tables = Tables();
            Columns(tables);
            Relationships(tables);
            Forms(tables);
            Views(tables);
            Flows();
            CanvasApps();
            ModelDrivenApps();
            ClassicLogic(tables);
            Plugins(tables);
            WebResources();
            Integration();
            Security();
            Analytics();
            Checker();
            Publishers();
            RecordReads();
        }

        private void BuildSolutions()
        {
            // Unmanaged, in production, which is the single most consequential thing this
            // product can tell a client on the first screen.
            solutions.Add(new SolutionZipReader.SolutionHeader(
                MainSolution, "Northwind Customer Core", "2.4.1.0", false, "nwu", "Northwind Utilities"));

            solutions.Add(new SolutionZipReader.SolutionHeader(
                FieldSolution, "Northwind Field Service", "1.9.0.3", false, "nwu", "Northwind Utilities"));

            // A managed third party solution, so the inventory has components that are not
            // the client's to fix and the rules that skip managed components have something
            // to skip.
            solutions.Add(new SolutionZipReader.SolutionHeader(
                IsvSolution, "SmartPortal for Utilities", "3.2.0.0", true, "isv", "SmartPortal BV"));

            solutions.Add(new SolutionZipReader.SolutionHeader(
                DefaultSolution, "Common Data Services Default Solution", "1.0.0.0", false, "new", "Northwind Utilities"));
        }

        private List<DiscoveredComponent> Tables()
        {
            var table = (string schema, string display, string solution, int rows, bool audit) =>
                Add("table", display, schema, solution, false,
                    ("isCustom", true),
                    ("isActivity", false),
                    ("isVirtual", false),
                    ("isElastic", false),
                    ("ownershipType", "UserOwned"),
                    ("auditEnabled", audit),
                    ("changeTrackingEnabled", rows > 100000),
                    ("rowCount", rows),

                    // Blank on most of them. Nobody writes a description for a table they are
                    // creating at four on a Friday, which is exactly what the rule reports.
                    ("description", (string?)null));

            List<DiscoveredComponent> tables =
            [
                // Deliberately three naming conventions across these, which is what the
                // naming rule reports: it is a proxy for how many people have built here
                // without anybody agreeing a standard.
                table("nwu_meter", "Meter", MainSolution, 486_000, true),
                table("nwu_meterreading", "Meter Reading", MainSolution, 14_200_000, false),
                table("nwu_outage", "Outage", MainSolution, 38_400, true),
                table("nwu_serviceorder", "ServiceOrder", FieldSolution, 221_000, true),
                table("nwu_tariff", "tariffPlan", MainSolution, 84, false),
                table("nwu_customerpremise", "Customer Premise", MainSolution, 612_000, true),
                table("nwu_inspection", "inspection_record", FieldSolution, 96_500, false),
                table("nwu_asset", "Asset", FieldSolution, 158_000, true),
                table("nwu_workcrew", "work-crew", FieldSolution, 240, false),
                table("nwu_complaint", "Complaint", MainSolution, 44_800, true),
                table("nwu_paymentplan", "PaymentPlan", MainSolution, 71_300, true),
                table("nwu_greenscheme", "green scheme enrolment", MainSolution, 9_400, false)
            ];

            // Standard tables, for the ones the estate obviously extends. Marked as not
            // custom so the rules that only look at customisation skip them, which is the
            // behaviour worth demonstrating: the product does not bill a client for Microsoft's
            // missing descriptions.
            foreach (var (schema, display) in new[]
            {
                ("account", "Account"), ("contact", "Contact"), ("incident", "Case"),
                ("task", "Task"), ("systemuser", "User"), ("team", "Team")
            })
            {
                Add("table", display, schema, DefaultSolution, true,
                    ("isCustom", false), ("ownershipType", "UserOwned"), ("rowCount", 250_000),
                    ("description", "Standard table."));
            }

            return tables;
        }

        /// <summary>
        /// The joins between the tables, and what each one does on delete.
        /// </summary>
        /// <remarks>
        /// A real estate of this size carries a couple of hundred of these, and until the
        /// offline reader learned to read them the demonstration did not have one. A data
        /// model section listing twelve tables and no relationships between them describes a
        /// spreadsheet rather than a system.
        ///
        /// The cascade behaviours are the point. Most are the platform's defaults, which is
        /// what a real export looks like, and three are not: a cascading delete from a
        /// low-volume parent onto a fourteen million row child is the finding worth being
        /// able to show somebody.
        /// </remarks>
        /// <param name="tables">The custom tables, which is what these join.</param>
        private void Relationships(List<DiscoveredComponent> tables)
        {
            // Referencing, referenced, what delete does. Named after the platform's own
            // convention so they read like something an export produced.
            var joins = new (string Child, string Parent, string OnDelete)[]
            {
                ("nwu_meterreading", "nwu_meter", "Cascade"),
                ("nwu_meter", "nwu_customerpremise", "Restrict"),
                ("nwu_outage", "nwu_customerpremise", "RemoveLink"),
                ("nwu_serviceorder", "nwu_asset", "Restrict"),
                ("nwu_serviceorder", "nwu_workcrew", "RemoveLink"),
                ("nwu_inspection", "nwu_asset", "Cascade"),
                ("nwu_asset", "nwu_customerpremise", "Restrict"),
                ("nwu_complaint", "nwu_customerpremise", "RemoveLink"),
                ("nwu_paymentplan", "nwu_tariff", "Restrict"),
                ("nwu_greenscheme", "nwu_customerpremise", "Cascade"),
                ("nwu_complaint", "nwu_outage", "RemoveLink"),
                ("nwu_serviceorder", "nwu_outage", "RemoveLink")
            };

            var byName = tables.ToDictionary(table => table.SchemaName ?? table.DisplayName, StringComparer.Ordinal);

            foreach (var (child, parent, onDelete) in joins)
            {
                var name = $"{parent}_{child}";

                var relationship = Add("relationship", name, name, MainSolution, false,
                    ("relationshipType", "OneToMany"),
                    ("cascadeDelete", onDelete),
                    ("cascadeConfiguration",
                        $"CascadeDelete={onDelete}, CascadeAssign=NoCascade, CascadeReparent=NoCascade, "
                        + "CascadeShare=NoCascade, CascadeUnshare=NoCascade"),
                    ("isCustom", true),
                    ("isHierarchical", false),
                    ("referencingTable", child),
                    ("referencedTable", parent),
                    ("description", (string?)null));

                foreach (var end in new[] { child, parent })
                {
                    if (byName.TryGetValue(end, out var table)) Link(relationship, table, "relatesTo");
                }
            }

            // The ownership relationships every custom table gets whether anybody wanted
            // them or not. Present because a real export carries them and an inventory that
            // quietly drops them understates the model, and marked as not custom so the
            // rules that only look at somebody's own work skip them.
            foreach (var table in tables)
            {
                var schema = table.SchemaName ?? table.DisplayName;
                var name = $"business_unit_{schema}";

                Add("relationship", name, name, MainSolution, false,
                    ("relationshipType", "OneToMany"),
                    ("cascadeDelete", "Restrict"),
                    ("cascadeConfiguration",
                        "CascadeDelete=Restrict, CascadeAssign=NoCascade, CascadeReparent=NoCascade, "
                        + "CascadeShare=NoCascade, CascadeUnshare=NoCascade"),
                    ("isCustom", false),
                    ("isHierarchical", false),
                    ("referencingTable", schema),
                    ("referencedTable", "BusinessUnit"),
                    ("description", "System relationship."));
            }
        }

        /// <summary>
        /// Who published each solution.
        /// </summary>
        /// <remarks>
        /// Two of them, which is the finding: the client's own prefix and an ISV's, in one
        /// estate. The prefix is what every other judgement in this product keys off, so
        /// showing it in the inventory rather than only using it silently is worth the two
        /// components it costs.
        /// </remarks>
        private void Publishers()
        {
            Add("publisher", "Northwind Utilities", "NorthwindUtilities", MainSolution, false,
                ("prefix", "nwu"), ("optionValuePrefix", "10000"), ("solutionCount", 2),
                ("description", (string?)null));

            Add("publisher", "SmartPortal BV", "SmartPortalBV", IsvSolution, true,
                ("prefix", "isv"), ("optionValuePrefix", "42000"), ("solutionCount", 1),
                ("description", "Third party publisher."));
        }

        private void Columns(List<DiscoveredComponent> tables)
        {
            var types = new[] { "String", "Lookup", "Picklist", "DateTime", "Decimal", "Boolean", "Money" };

            foreach (var (table, index) in tables.Select((table, index) => (table, index)))
            {
                // Between nine and twenty three columns per table, walked rather than drawn,
                // so the estate has a realistic spread and rebuilds identically.
                var count = 9 + ((index * 7) % 15);

                for (var position = 0; position < count; position++)
                {
                    var schema = $"{table.SchemaName}_field{position:D2}";
                    var attributeType = types[(index + position) % types.Length];

                    // Every fourth column keeps its description, which is roughly what an
                    // estate that once had a standard and lost it looks like.
                    var described = position % 4 == 0;

                    var column = Add("column", $"Field {position:D2}", schema, table.SolutionUniqueName!, false,
                        ("attributeType", attributeType),
                        ("isCustom", true),
                        ("requiredLevel", position == 0 ? "ApplicationRequired" : "None"),
                        ("maxLength", attributeType == "String" ? 100 : (object?)null),
                        ("isAuditEnabled", false),
                        ("isValidForAdvancedFind", position < 6),
                        ("table", table.SchemaName),
                        ("description", described ? $"Holds the {attributeType.ToLowerInvariant()} value for {table.DisplayName}." : null));

                    Link(column, table, "belongsTo");

                    // The last three columns of every other table are on no form and in no
                    // view, which is what the orphan rule looks for. Everything else gets an
                    // inbound reference below when the forms and views are built.
                    if (index % 2 == 0 && position >= count - 3) continue;

                    links.Add(new ComponentLink($"usage:{schema}", column.StableKey, "usedBy"));
                }
            }
        }

        private void Forms(List<DiscoveredComponent> tables)
        {
            foreach (var (table, index) in tables.Select((table, index) => (table, index)))
            {
                var main = Add("form", $"{table.DisplayName} – Main", $"{table.SchemaName}_main", table.SolutionUniqueName!, false,
                    ("formType", "Main"),
                    ("statecode", "Published"),
                    ("tabCount", 2 + (index % 4)),
                    ("fieldCount", 12 + (index * 3 % 20)),
                    ("hasOnLoadScript", index % 3 == 0),

                    // Three of these carry more than four libraries, which is the threshold
                    // the form weight rule uses. Six on one form is not invented for effect:
                    // it is what happens when four projects each add their own.
                    ("scriptLibraryCount", index switch { 0 => 6, 3 => 7, 7 => 5, _ => 1 + (index % 3) }),
                    ("isCustomisable", true),
                    ("table", table.SchemaName),
                    ("description", (string?)null));

                if (index % 5 == 0)
                {
                    Add("form", $"{table.DisplayName} – Quick create", $"{table.SchemaName}_quick", table.SolutionUniqueName!, false,
                        ("formType", "QuickCreate"), ("statecode", "Published"), ("tabCount", 1),
                        ("fieldCount", 5), ("hasOnLoadScript", false), ("scriptLibraryCount", 0),
                        ("isCustomisable", true), ("table", table.SchemaName), ("description", (string?)null));
                }

                formsByTable[table.SchemaName!] = main;
            }
        }

        private readonly Dictionary<string, DiscoveredComponent> formsByTable = new(StringComparer.Ordinal);

        private void Views(List<DiscoveredComponent> tables)
        {
            foreach (var (table, index) in tables.Select((table, index) => (table, index)))
            {
                // The default view is the one everybody lands on, which is why the complexity
                // rule only looks at those. Two of them are the view somebody kept adding a
                // column to for three years.
                var columns = index switch { 1 => 28, 5 => 24, _ => 7 + (index % 8) };
                var linked = index switch { 1 => 7, 5 => 6, _ => index % 3 };

                Add("view", $"Active {table.DisplayName}", $"{table.SchemaName}_active", table.SolutionUniqueName!, false,
                    ("queryType", "MainApplicationView"),
                    ("columnCount", columns),
                    ("linkedEntityCount", linked),
                    ("hasLinkedEntities", linked > 0),
                    ("filterComplexity", linked > 4 ? "high" : "low"),
                    ("isDefault", true),
                    ("table", table.SchemaName),
                    ("description", (string?)null));

                Add("view", $"My open {table.DisplayName}", $"{table.SchemaName}_mine", table.SolutionUniqueName!, false,
                    ("queryType", "MainApplicationView"), ("columnCount", 6), ("linkedEntityCount", 1),
                    ("hasLinkedEntities", true), ("filterComplexity", "low"), ("isDefault", false),
                    ("table", table.SchemaName), ("description", (string?)null));
            }
        }

        /// <summary>
        /// The table a flow is anchored to, where it has one.
        /// </summary>
        /// <remarks>
        /// Set by hand rather than guessed from the name. A flow that reads five tables has
        /// no single primary one, and the rule that counts how many mechanisms sit on a table
        /// is worth nothing if the answer is a keyword match on a title.
        /// </remarks>
        private static readonly Dictionary<string, string> FlowTables = new(StringComparer.Ordinal)
        {
            ["Create service order from outage"] = "nwu_outage",
            ["Outage bulk SMS"] = "nwu_outage",
            ["Archive closed outages"] = "nwu_outage",
            ["Notify crew of new assignment"] = "nwu_serviceorder",
            ["Crew timesheet rollup"] = "nwu_workcrew",
            ["Escalate complaint after 48 hours"] = "nwu_complaint",
            ["Approve payment plan"] = "nwu_paymentplan",
            ["Nightly meter reconciliation"] = "nwu_meterreading"
        };

        private void Flows()
        {
            // The flow estate, which is where most of a low code assessment's weight sits.
            // Mixed on purpose: some are fine, several are large, several have no failure
            // path, two fail often enough to matter and one carries a key in its definition.
            var specification = new (string Name, string Solution, int Actions, int Depth, bool ErrorHandling,
                int Runs, decimal FailureRate, int ConnectionReferences, string Connectors, string? Definition)[]
            {
                ("Create service order from outage", MainSolution, 34, 4, true, 4_200, 0.01m, 3, "shared_commondataserviceforapps,shared_office365", null),
                ("Notify crew of new assignment", FieldSolution, 12, 2, false, 9_800, 0.03m, 2, "shared_commondataserviceforapps,shared_teams", null),
                ("Nightly meter reconciliation", MainSolution, 61, 6, false, 30, 0.22m,
                    1, "shared_commondataserviceforapps,shared_sql,shared_azureblob",

                    // A hard coded host and a key in the same definition, which is what an
                    // integration written against a test endpoint and promoted looks like.
                    "{\"actions\":{\"HTTP\":{\"inputs\":{\"uri\":\"https://nwu-integration-test.azurewebsites.example/api/meters\"," +
                    "\"headers\":{\"x-api-key\":\"AbC123dEf456GhI789jKlMnO012pQrS345tUvW\"}}}}}"),
                ("Escalate complaint after 48 hours", MainSolution, 18, 3, false, 1_420, 0.14m, 2, "shared_commondataserviceforapps,shared_office365", null),
                ("Sync premise to billing", MainSolution, 47, 5, true, 12_600, 0.02m, 2, "shared_commondataserviceforapps,shared_sql", null),
                ("Close inspection and notify", FieldSolution, 9, 2, false, 2_100, 0.04m, 1, "shared_commondataserviceforapps", null),
                ("Approve payment plan", MainSolution, 22, 4, true, 860, 0.01m, 3, "shared_commondataserviceforapps,shared_approvals,shared_office365", null),
                ("post reading to SAP", MainSolution, 28, 3, false, 640, 0.31m,
                    1, "shared_commondataserviceforapps,shared_sap",
                    "{\"actions\":{\"HTTP\":{\"inputs\":{\"uri\":\"https://sap-gateway.northwind-utilities.example:8443/odata/readings\"}}}}"),
                ("Green scheme welcome pack", MainSolution, 7, 1, false, 310, 0.00m, 2, "shared_commondataserviceforapps,shared_office365", null),
                ("Outage bulk SMS", MainSolution, 15, 3, true, 180, 0.06m, 2, "shared_commondataserviceforapps,shared_twilio", null),
                ("asset_warranty_check", FieldSolution, 11, 2, false, 90, 0.02m, 1, "shared_commondataserviceforapps", null),
                ("Crew timesheet rollup", FieldSolution, 44, 5, true, 620, 0.01m, 2, "shared_commondataserviceforapps,shared_excelonline", null),
                ("Tariff change broadcast", MainSolution, 6, 1, false, 24, 0.00m, 1, "shared_commondataserviceforapps", null),
                ("Complaint satisfaction survey", MainSolution, 13, 2, false, 1_100, 0.05m, 2, "shared_commondataserviceforapps,shared_forms", null),
                ("Retry failed readings", MainSolution, 52, 7, false, 210, 0.18m, 1, "shared_commondataserviceforapps,shared_sql", null),
                ("Archive closed outages", MainSolution, 8, 2, true, 30, 0.00m, 1, "shared_commondataserviceforapps", null)
            };

            foreach (var (name, solution, actions, depth, handling, runs, rate, references, connectors, definition) in specification)
            {
                Add("cloudFlow", name, name.Replace(' ', '_').ToLowerInvariant(), solution, false,
                    ("statecode", "Activated"),
                    ("triggerType", "Automated"),
                    ("actionCount", actions),
                    ("maxDepth", depth),
                    ("connectionReferenceCount", references),
                    ("connectorIds", connectors),
                    ("usesPremiumConnector", connectors.Contains("sql", StringComparison.Ordinal) || connectors.Contains("sap", StringComparison.Ordinal)),
                    ("hasErrorHandling", handling),
                    ("runCount30d", runs),
                    ("failureRate30d", rate),
                    ("averageDurationMs", 400 + (actions * 90)),
                    ("isChildFlow", false),
                    ("definitionText", definition),
                    ("primaryEntity", FlowTables.GetValueOrDefault(name)),
                    ("ownerState", name.StartsWith("Green", StringComparison.Ordinal) ? "disabled" : "active"),
                    ("description", actions > 40 ? "Large flow, documented in the runbook." : null));
            }

            // Two flows whose owner has left. The rule reports these separately from the
            // technical findings, because the fix is a conversation rather than a change.
            Add("cloudFlow", "Legacy debt chase", "legacy_debt_chase", MainSolution, false,
                ("statecode", "Activated"), ("triggerType", "Scheduled"), ("actionCount", 19), ("maxDepth", 3),
                ("connectionReferenceCount", 1), ("connectorIds", "shared_commondataserviceforapps"),
                ("hasErrorHandling", false), ("runCount30d", 120), ("failureRate30d", 0.09m),
                ("ownerState", "absent"), ("description", (string?)null));
        }

        private void CanvasApps()
        {
            var specification = new (string Name, string Solution, int Screens, int Controls, int Formulas, bool Delegation, long Size, string? Content)[]
            {
                ("Field Inspection", FieldSolution, 14, 410, 1_280, true, 3_400_000, null),
                ("Meter Reading Capture", MainSolution, 8, 190, 640, true, 1_200_000, null),
                ("Outage Triage", MainSolution, 11, 260, 880, false, 2_100_000,

                    // A shared access signature pasted into a formula, which is the single
                    // most common way a secret ends up in a solution file.
                    "Set(varToken, \"AccountKey=Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6QUJDRA==\")"),
                ("Crew Dispatch", FieldSolution, 9, 220, 700, true, 1_800_000, null),
                ("Green Scheme Signup", MainSolution, 5, 96, 240, false, 700_000, null)
            };

            foreach (var (name, solution, screens, controls, formulas, delegation, size, content) in specification)
            {
                Add("canvasApp", name, name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), solution, false,
                    ("screenCount", screens), ("controlCount", controls), ("formulaCount", formulas),
                    ("dataSourceCount", 2 + (screens % 4)), ("usesDelegationWarnings", delegation),
                    ("connectionReferenceCount", 2), ("sizeBytes", size), ("content", content),
                    ("ownerState", "active"), ("description", (string?)null));
            }
        }

        private void ModelDrivenApps()
        {
            foreach (var (name, solution, components, modern) in new[]
            {
                ("Customer Operations", MainSolution, 84, true),
                ("Field Service Desk", FieldSolution, 61, false),
                ("Meter Administration", MainSolution, 34, false)
            })
            {
                Add("modelDrivenApp", name, name.Replace(' ', '_').ToLowerInvariant(), solution, false,
                    ("statecode", "Published"), ("componentCount", components),
                    ("usesModernCommanding", modern), ("lastPublishedUtc", "2026-04-18T09:12:00Z"),
                    ("description", (string?)null));
            }

            Add("siteMap", "Customer Operations sitemap", "nwu_customerops_sitemap", MainSolution, false,
                ("description", (string?)null));
        }

        private void ClassicLogic(List<DiscoveredComponent> tables)
        {
            // Two dialogs. Microsoft removed dialogs in 2020, so one still sitting here says
            // more about the estate than any other single component in it.
            foreach (var (name, table) in new[] { ("Log outage call", "nwu_outage"), ("Register complaint", "nwu_complaint") })
            {
                Add("dialog", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("statecode", "Activated"), ("stepCount", 14), ("primaryEntity", table),
                    ("description", (string?)null));
            }

            var activity = Add("customWorkflowActivity", "NorthwindRoundingActivity", "nwu_roundingactivity", MainSolution, false,
                ("description", (string?)null));

            var second = Add("customWorkflowActivity", "NorthwindGeocodeActivity", "nwu_geocodeactivity", FieldSolution, false,
                ("description", (string?)null));

            var backgroundSpecification = new (string Name, string Table, string Solution, int Executions30, int Executions90, int Steps)[]
            {
                ("Assign service order to crew", "nwu_serviceorder", FieldSolution, 3_100, 9_400, 11),
                ("Recalculate tariff on change", "nwu_tariff", MainSolution, 0, 0, 7),
                ("Set premise geocode", "nwu_customerpremise", MainSolution, 820, 2_600, 5),
                ("Escalate ageing inspection", "nwu_inspection", FieldSolution, 0, 0, 9),
                ("Round meter reading", "nwu_meterreading", MainSolution, 44_000, 131_000, 4)
            };

            foreach (var (name, table, solution, thirty, ninety, steps) in backgroundSpecification)
            {
                var workflow = Add("classicWorkflowBackground", name, name.Replace(' ', '_').ToLowerInvariant(), solution, false,
                    ("category", "Workflow"), ("mode", "Background"), ("scope", "Organization"),
                    ("statecode", "Activated"), ("triggerOnCreate", true), ("triggerOnUpdate", true),
                    ("triggerOnDelete", false), ("stepCount", steps),
                    ("usesCustomActivity", name.StartsWith("Round", StringComparison.Ordinal) || name.StartsWith("Set premise", StringComparison.Ordinal)),
                    ("lastExecutedUtc", ninety == 0 ? "2023-11-02T14:41:00Z" : "2026-09-18T22:05:00Z"),
                    ("executionCount30d", thirty), ("executionCount90d", ninety),
                    ("failureCount30d", thirty / 50), ("primaryEntity", table),
                    ("description", (string?)null));

                if (name.StartsWith("Round", StringComparison.Ordinal)) Link(workflow, activity, "callsActivity");
                if (name.StartsWith("Set premise", StringComparison.Ordinal)) Link(workflow, second, "callsActivity");
            }

            foreach (var (name, table, steps) in new[]
            {
                ("Validate meter serial", "nwu_meter", 6),
                ("Stamp complaint received", "nwu_complaint", 3),
                ("Set outage priority", "nwu_outage", 4)
            })
            {
                Add("classicWorkflowRealtime", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("mode", "Realtime"), ("stage", "PostOperation"), ("statecode", "Activated"),
                    ("stepCount", steps), ("rollsBackOnError", true), ("primaryEntity", table),
                    ("executionCount30d", 5_200), ("executionCount90d", 15_800),
                    ("description", (string?)null));
            }

            // Business rules and process flows, which are the healthy half of the low code
            // estate. A demonstration where everything is a finding teaches the wrong lesson.
            foreach (var (table, index) in tables.Take(8).Select((table, index) => (table, index)))
            {
                Add("businessRule", $"Require contact on {table.DisplayName}", $"{table.SchemaName}_br{index:D2}", table.SolutionUniqueName!, false,
                    ("scope", "Entity"), ("statecode", "Activated"), ("conditionCount", 2), ("actionCount", 2),
                    ("table", table.SchemaName),
                    ("description", "Keeps the contact mandatory once the record is active."));
            }

            foreach (var (name, table) in new[] { ("Outage resolution", "nwu_outage"), ("Service order lifecycle", "nwu_serviceorder") })
            {
                Add("businessProcessFlow", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("statecode", "Activated"), ("stageCount", 5), ("entityCount", 2),
                    ("hasWorkflowSteps", true), ("primaryEntity", table),
                    ("description", "The stages the service desk works to."));
            }

            Add("customProcessAction", "nwu_CalculateRebate", "nwu_calculaterebate", MainSolution, false,
                ("statecode", "Activated"), ("isApiCallable", true), ("argumentCount", 4),
                ("description", (string?)null));

            Add("sla", "Outage response SLA", "nwu_outage_sla", MainSolution, false,
                ("statecode", "Activated"), ("itemCount", 4), ("appliesToEntity", "nwu_outage"),
                ("description", "Four hour response on a high priority outage."));
        }

        private void Plugins(List<DiscoveredComponent> tables)
        {
            var specification = new (string Name, string Isolation, string Source, long Size, int Types)[]
            {
                ("Northwind.Crm.Plugins", "Sandbox", "Database", 412_000, 14),

                // Full trust, on disk. This is the critical finding the security section of
                // the report opens with, and it is not rare in an estate this age.
                ("Northwind.Integration.Plugins", "None", "Disk", 1_180_000, 9),
                ("Northwind.FieldService.Plugins", "Sandbox", "Database", 268_000, 7),
                ("SmartPortal.Core", "Sandbox", "Database", 890_000, 21),
                ("Northwind.Legacy.Plugins", "None", "Database", 96_000, 3)
            };

            foreach (var (name, isolation, source, size, types) in specification)
            {
                Add("pluginAssembly", name, name, name.StartsWith("SmartPortal", StringComparison.Ordinal) ? IsvSolution : MainSolution,
                    name.StartsWith("SmartPortal", StringComparison.Ordinal),
                    ("isolationMode", isolation), ("sourceType", source), ("version", "4.2.1.0"),
                    ("typeCount", types), ("sizeBytes", size), ("targetFramework", "net462"),
                    ("description", (string?)null));
            }

            // Steps, where the runtime numbers live. Three are synchronous and slow, which is
            // what a user means when they say the form takes a moment to save.
            var messages = new[] { "Create", "Update", "Delete", "Update", "Update", "Retrieve" };

            foreach (var (table, index) in tables.Select((table, index) => (table, index)))
            {
                var message = messages[index % messages.Length];
                var synchronous = index % 3 == 0;

                Add("pluginStep", $"{table.SchemaName} {message} step", $"{table.SchemaName}_{message.ToLowerInvariant()}_step",
                    table.SolutionUniqueName!, false,
                    ("message", message),
                    ("primaryEntity", table.SchemaName),
                    ("stage", "PostOperation"),
                    ("mode", synchronous ? "Synchronous" : "Asynchronous"),

                    // No filtering attributes on an Update step means it runs on every save of
                    // every column, which is the cheapest performance fix in the catalogue.
                    ("filteringAttributes", message == "Update" && index % 2 == 0 ? null : $"{table.SchemaName}_field00"),
                    ("statecode", "Activated"),
                    ("hasImages", true),
                    ("executionCount30d", 4_000 + (index * 900)),
                    ("failureCount30d", index * 3),
                    ("averageExecutionMs", synchronous && index % 2 == 0 ? 2_400 + (index * 400) : 120 + (index * 20)),
                    ("description", (string?)null));
            }
        }

        private void WebResources()
        {
            // A script that does nothing a business rule could not do, which is the finding
            // that starts the conversation about who is allowed to write code here.
            var simple = Add("jsWebResource", "nwu_meter_form.js", "nwu_meter_form.js", MainSolution, false,
                ("sizeBytes", 2_400L), ("isMinified", false), ("isLibrary", false),
                ("referencedByFormCount", 3), ("usesDeprecatedApi", false), ("usesUnsupportedSyntax", false),
                ("content", "function onLoad(context) { var form = context.getFormContext(); " +
                    "form.getControl('nwu_meter_field03').setVisible(false); " +
                    "form.getAttribute('nwu_meter_field04').setRequiredLevel('required'); }"),
                ("table", "nwu_meter"),
                ("description", (string?)null));

            var large = Add("jsWebResource", "nwu_common_library.js", "nwu_common_library.js", MainSolution, false,
                ("sizeBytes", 840_000L), ("isMinified", false), ("isLibrary", true),
                ("referencedByFormCount", 11), ("usesDeprecatedApi", true), ("usesUnsupportedSyntax", false),
                ("content", "Xrm.WebApi.retrieveMultipleRecords('nwu_meter', '?$select=nwu_meter_field00');"),
                ("description", "Shared helpers. Nobody is sure what still calls what."));

            var integration = Add("jsWebResource", "nwu_portal_bridge.js", "nwu_portal_bridge.js", FieldSolution, false,
                ("sizeBytes", 96_000L), ("isMinified", true), ("isLibrary", true),
                ("referencedByFormCount", 4), ("usesDeprecatedApi", false), ("usesUnsupportedSyntax", false),

                // A test host and a bearer token, in a file that ships to every environment.
                ("content", "const endpoint = 'https://portal-acc.northwind-utilities.example/bridge';" +
                    "const bearer = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9abcdefghijklmnop';"),
                ("description", (string?)null));

            foreach (var (name, size, forms) in new[]
            {
                ("nwu_outage_form.js", 12_000L, 2), ("nwu_crew_map.js", 220_000L, 1),
                ("nwu_validation.js", 34_000L, 6), ("nwu_ribbon_rules.js", 18_000L, 5)
            })
            {
                Add("jsWebResource", name, name, MainSolution, false,
                    ("sizeBytes", size), ("isMinified", false), ("isLibrary", true),
                    ("referencedByFormCount", forms), ("usesDeprecatedApi", false),
                    ("usesUnsupportedSyntax", false), ("content", "Xrm.Navigation.openForm({ entityName: 'nwu_outage' });"),
                    ("table", name == "nwu_outage_form.js" ? "nwu_outage" : null),
                    ("description", (string?)null));
            }

            // The forms that carry more than four libraries need the links, or the rule
            // reports a weight it cannot size.
            foreach (var form in formsByTable.Values.Where(form => (form.Attribute<int?>("scriptLibraryCount") ?? 0) > 4))
            {
                Link(form, large, "loadsScript");
                Link(form, integration, "loadsScript");
                Link(form, simple, "loadsScript");
            }

            Add("htmlWebResource", "nwu_help_panel.html", "nwu_help_panel.html", MainSolution, false,
                ("description", (string?)null));

            // Three code components, because one cannot show what the reader now sees.
            //
            // The bundle facts are the half of a control the manifest cannot answer, and a
            // demonstration estate that shows only manifests demonstrates the half that was
            // already working. These three are the three kinds that turn up in real estates:
            // one built properly, one carrying a library it did not need, and one shipped in
            // a hurry. Between them they trip every code component rule in the catalogue,
            // which is deliberate, and the first one trips none of them, which matters more:
            // a report where every control has a finding against it reads as a tool that
            // cannot tell good work from bad.
            Add("pcfControl", "NorthwindMeterGauge", "nwu_MeterGauge", MainSolution, false,
                ("isThirdParty", false), ("version", "1.3.0"), ("manifestVersion", "1.3.4"),
                ("controlType", "virtual"), ("builtBy", "pac 1.34.6"),
                ("usesReactPlatformLibrary", true), ("sizeBytes", 84_320L),
                ("isMinified", true), ("bundlesOwnReact", false), ("hasDebugCode", false),
                ("timerCount", 0), ("innerHtmlCount", 0),
                ("usesWebApi", true), ("callsWebApi", true),
                ("declaresExternalService", false), ("resourceCount", 2),
                ("description", (string?)null));

            Add("pcfControl", "Outage map viewer", "nwu_OutageMap", FieldSolution, false,
                ("isThirdParty", true), ("version", "4.2.1"), ("manifestVersion", "1.3.0"),
                ("controlType", "standard"), ("builtBy", "pac 1.21.4"),
                ("usesReactPlatformLibrary", false), ("sizeBytes", 1_482_640L),
                ("isMinified", true), ("bundlesOwnReact", true), ("hasDebugCode", false),
                ("timerCount", 0), ("innerHtmlCount", 6),
                ("usesWebApi", true), ("callsWebApi", true),
                ("declaresExternalService", true), ("resourceCount", 4),
                ("description", (string?)null));

            Add("pcfControl", "Crew presence tile", "nwu_CrewPresence", FieldSolution, false,
                ("isThirdParty", false), ("version", "0.9.2"), ("manifestVersion", "1.3.0"),
                ("controlType", "standard"), ("builtBy", "pac 1.28.2"),
                ("usesReactPlatformLibrary", false), ("sizeBytes", 246_180L),
                ("isMinified", false), ("bundlesOwnReact", false), ("hasDebugCode", true),
                ("timerCount", 3), ("innerHtmlCount", 14),

                // Declares nothing and calls it anyway, which is the one defect here rather
                // than a cost or a question. It works until somebody opens the tab that
                // loads the queue.
                ("usesWebApi", false), ("callsWebApi", true),
                ("declaresExternalService", false), ("resourceCount", 2),
                ("description", (string?)null));
        }

        private void Integration()
        {
            var connectors = new (string Id, bool Premium, int Used)[]
            {
                ("shared_commondataserviceforapps", false, 18),
                ("shared_office365", false, 6),
                ("shared_teams", false, 2),
                ("shared_sql", true, 4),
                ("shared_sap", true, 1),
                ("shared_azureblob", true, 2),
                ("shared_approvals", false, 1),
                ("shared_twilio", true, 1),
                ("shared_excelonline", false, 1),
                ("shared_forms", false, 1)
            };

            foreach (var (id, premium, used) in connectors)
            {
                Add("connectionReference", id.Replace("shared_", string.Empty, StringComparison.Ordinal), $"nwu_{id}", MainSolution, false,
                    ("connectorId", id), ("isPremium", premium), ("isCustom", false),
                    ("usedByCount", used), ("isShared", true), ("description", (string?)null));
            }

            Add("customConnector", "Northwind Billing API", "nwu_billingapi", MainSolution, false,
                ("authenticationType", "apiKey"), ("operationCount", 11),
                ("hasSwaggerDefinition", true), ("hasPolicyTemplates", false),
                ("host", "billing.northwind-utilities.example"), ("description", (string?)null));

            Add("serviceEndpoint", "Outage event grid", "nwu_outagegrid", MainSolution, false,
                ("contract", "Queue"), ("messageFormat", "Json"), ("authType", "SASKey"),
                ("url", "sb://northwind-outage.servicebus.example/outages"), ("description", (string?)null));

            var variables = new (string Name, bool Default, bool Secret, string? Value)[]
            {
                ("nwu_BillingApiBaseUrl", true, false, "https://billing-test.northwind-utilities.example"),
                ("nwu_BillingApiKey", true, true, null),
                ("nwu_CrewNotificationTeam", true, false, "Field crew – acceptance"),
                ("nwu_OutageThresholdMinutes", false, false, null),
                ("nwu_SapClientId", true, true, null),
                ("nwu_PortalUrl", false, false, null)
            };

            foreach (var (name, hasDefault, secret, value) in variables)
            {
                Add("environmentVariable", name, name, MainSolution, false,
                    ("type", secret ? "Secret" : "String"), ("hasDefaultValue", hasDefault),
                    ("hasCurrentValue", !hasDefault), ("isSecret", secret), ("defaultValue", value),
                    ("description", (string?)null));
            }

            foreach (var name in new[] { "Meter readings from SAP", "Asset register nightly" })
            {
                Add("dataflow", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("refreshSchedule", "Daily 02:00 UTC"), ("description", (string?)null));
            }

            // References the extraction saw and could not resolve. These are the honest half
            // of the inventory: a reference to something outside the solutions in scope.
            unresolved.Add(new UnresolvedLink("cloudFlow:name:sync_premise_to_billing", "connectsTo", "SQL server nwu-billing-prod, database NWUBilling"));
            unresolved.Add(new UnresolvedLink("cloudFlow:name:post_reading_to_sap", "connectsTo", "SAP gateway sap-gateway.northwind-utilities.example"));
            unresolved.Add(new UnresolvedLink("pluginAssembly:name:Northwind.Integration.Plugins", "callsOut", "An on premises endpoint named in configuration, not in the assembly"));
        }

        private void Security()
        {
            var roles = new (string Name, int Users, int Teams, bool OrgWrite, int Privileges)[]
            {
                ("Northwind Service Desk", 142, 3, false, 218),
                ("Northwind Field Crew", 310, 6, false, 96),
                ("nwu_integration_service", 1, 0, true, 512),
                ("Meter Administrator", 8, 1, true, 344),
                ("Outage Manager", 22, 2, false, 187),
                ("legacy-portal-role", 0, 0, false, 64),
                ("Temp migration role", 0, 0, true, 480),
                ("Green Scheme Reader", 14, 0, false, 41),
                ("SmartPortal Service", 1, 0, false, 122)
            };

            foreach (var (name, users, teams, orgWrite, privileges) in roles)
            {
                Add("securityRole", name, name.Replace(' ', '_').ToLowerInvariant(),
                    name.StartsWith("SmartPortal", StringComparison.Ordinal) ? IsvSolution : MainSolution,
                    name.StartsWith("SmartPortal", StringComparison.Ordinal),
                    ("isCustom", true), ("userCount", users), ("teamCount", teams),
                    ("hasOrganisationLevelWrite", orgWrite), ("hasPrivilegeToAllTables", privileges > 400),
                    ("privilegeCount", privileges), ("description", (string?)null));
            }

            Add("fieldSecurityProfile", "Meter serial numbers", "nwu_meterserial", MainSolution, false,
                ("description", "Restricts the serial to the meter administration team."));
        }

        private void Analytics()
        {
            // RDL reports, which are the Power BI conversation with a real hours cost attached.
            foreach (var (name, table, lastRun) in new[]
            {
                ("Monthly consumption", "nwu_meterreading", "2026-09-01T06:00:00Z"),
                ("Outage duration by region", "nwu_outage", "2026-08-12T06:00:00Z"),
                ("Crew utilisation", "nwu_workcrew", "2025-02-19T06:00:00Z"),
                ("Debt ageing", "nwu_paymentplan", "2026-09-15T06:00:00Z")
            })
            {
                Add("report", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("primaryEntity", table), ("lastRunUtc", lastRun), ("description", (string?)null));
            }

            foreach (var (name, interactive, powerBi) in new[]
            {
                ("Service desk overview", true, false),
                ("Outage command centre", true, true),
                ("Field performance", false, false)
            })
            {
                Add("dashboard", name, name.Replace(' ', '_').ToLowerInvariant(), MainSolution, false,
                    ("isInteractive", interactive), ("componentCount", 6),
                    ("hasPowerBiComponent", powerBi), ("description", (string?)null));
            }

            // A handful of components left in the default solution, which do not travel
            // between environments and are found the day a deployment is missing a column.
            foreach (var (typeId, name) in new[]
            {
                ("column", "new_tempflag"), ("column", "new_migrationnote"),
                ("view", "new_adhoc_outages"), ("chart", "new_quickchart")
            })
            {
                Add(typeId, name, name, DefaultSolution, false,
                    ("isCustom", true), ("table", "nwu_outage"), ("description", (string?)null));
            }
        }

        private void Checker()
        {
            // What the Power Apps checker returned. Three rules in this product's catalogue
            // are answered by Microsoft's rules rather than by a handler here, and a
            // demonstration with an empty checker result would show those three as not
            // assessed, which is a story about a missing connection rather than about an estate.
            var deprecated = components.First(component => component.SchemaName == "nwu_common_library.js");
            var canvas = components.First(component => component.TypeId == "canvasApp" && component.DisplayName == "Field Inspection");
            var plugin = components.First(component => component.DisplayName == "Northwind.Integration.Plugins");

            checkerIssues.Add(new CheckerIssue(
                "web-avoid-crm2011-service-odata", "lifecycle", "High",
                "Uses Xrm.Page, which was removed from the supported client API.",
                deprecated.StableKey, "nwu_common_library.js", 412));

            checkerIssues.Add(new CheckerIssue(
                "app-formula-issues-high", "quality", "High",
                "Delegation warning: this filter cannot be delegated, so only the first 500 rows are read.",
                canvas.StableKey, "Screen4.Gallery1.Items", null));

            checkerIssues.Add(new CheckerIssue(
                "il-use-tracingservice", "quality", "Medium",
                "The plug-in does not write to the tracing service, so a failure in production cannot be diagnosed.",
                plugin.StableKey, "Northwind.Integration.Plugins.OutageHandler", 88));
        }

        private void RecordReads()
        {
            // One row per component type the extraction attempted, which is what the
            // discovery screen renders. Counted from what was actually built, so the screen
            // and the inventory cannot disagree.
            foreach (var group in components.GroupBy(component => component.TypeId, StringComparer.Ordinal))
            {
                Read(group.Key, "metadata", group.Count());
            }

            // Two that were attempted and returned nothing, because an estate where every
            // read succeeded is not one anybody recognises.
            reads.Add(("desktopFlow", "metadata", true, 0, null));
            reads.Add(("copilotStudioAgent", "metadata", false, null,
                "The application user does not hold the privilege to read Copilot Studio agents. Nothing is reported about them, rather than reporting none."));
        }
    }
}
