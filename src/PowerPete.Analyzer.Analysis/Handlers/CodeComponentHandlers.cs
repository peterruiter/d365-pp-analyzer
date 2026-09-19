namespace PowerPete.Analyzer.Analysis.Handlers;

using PowerPete.Analyzer.Domain;

/// <summary>
/// What a code component's bundle says about it.
/// </summary>
/// <remarks>
/// A PCF control is the one place in a Power Platform estate where somebody ships arbitrary
/// JavaScript that runs on every form load, and until this existed the product read the
/// manifest and said nothing about any of it.
///
/// Everything here is measured off the file. None of it is an opinion about whether the code
/// is any good, because a minified bundle cannot support one: a complexity metric over
/// webpack output measures the bundler. What a bundle can honestly answer is what it costs
/// to load and whether the build that produced it was a production build, and those are the
/// two questions a client actually asks about a control somebody wrote for them.
/// </remarks>
public sealed class PcfBundleSizeHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "performance.pcfBundleSize";

    /// <summary>
    /// Half a megabyte.
    /// </summary>
    /// <remarks>
    /// Well above anything the tooling produces on its own and well below the point where a
    /// control is obviously wrong, so it catches the ones carrying a library they did not
    /// need rather than every control built with the standard template.
    /// </remarks>
    private const long Threshold = 512 * 1024;

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            var size = control.Attribute<long?>("sizeBytes");

            // Null rather than zero. A control whose bundle was not in the file is not a
            // control with a small bundle, and reporting it as one would be inventing a
            // measurement.
            if (size is null or <= Threshold) continue;

            yield return Fire.At(RuleId, control,
                ("sizeBytes", size),
                ("kilobytes", size / 1024),
                ("bundlesOwnReact", control.Attribute<bool?>("bundlesOwnReact")),
                ("controlType", control.Attribute<string>("controlType")),
                ("paidWhen", "On every load of every form the control is on, by every user."),
                ("beforeEstimating", "Look at what is in the bundle. A charting library included whole for one "
                    + "function is an afternoon; a control that genuinely needs what it carries is not worth touching."));
        }
    }
}

/// <summary>A standard control shipping a second copy of React onto the form.</summary>
public sealed class PcfOwnReactHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "modernisation.pcfBundlesOwnReact";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            if (control.Attribute<bool?>("bundlesOwnReact") is not true) continue;

            // Already declared. A virtual control references React without shipping it, and
            // the marker in its bundle is the reference rather than the copy.
            if (control.Attribute<bool?>("usesReactPlatformLibrary") is true) continue;

            yield return Fire.At(RuleId, control,
                ("controlType", control.Attribute<string>("controlType") ?? "standard"),
                ("sizeBytes", control.Attribute<long?>("sizeBytes")),
                ("whatChanges", "Declaring the React platform library and returning an element instead of "
                    + "rendering into the container."),
                ("leaveItAlone", "A small control that works is not worth converting for its own sake. This is "
                    + "worth doing where the bundle is large or where several controls sit on one form, each "
                    + "bringing their own copy."));
        }
    }
}

/// <summary>A bundle that was never built for production.</summary>
public sealed class PcfNotMinifiedHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.pcfNotMinified";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            if (control.Attribute<bool?>("isMinified") is not false) continue;

            yield return Fire.At(RuleId, control,
                ("sizeBytes", control.Attribute<long?>("sizeBytes")),
                ("meaning", "The deployed bundle came out of a development build."),
                ("theRealFinding", "Usually not the size. It is that nobody can say which build produced what "
                    + "is running in production."));
        }
    }
}

/// <summary>Debug code that reached production.</summary>
public sealed class PcfDebugCodeHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.pcfDebugCodeShipped";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            if (control.Attribute<bool?>("hasDebugCode") is not true) continue;

            yield return Fire.At(RuleId, control,
                ("consequence", "A debugger statement halts the browser of anybody who has developer tools open, "
                    + "which is every consultant looking at why something else is broken."),
                ("note", "Low severity on its own. It is worth reading as a statement about the build pipeline "
                    + "rather than about this control."));
        }
    }
}

/// <summary>A control that keeps working after the page has finished loading.</summary>
/// <remarks>
/// The only cost in a code component that is not paid once. Everything else the bundle can
/// be judged on is a download; a timer is a commitment for as long as the form stays open.
/// </remarks>
public sealed class PcfPollingTimerHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "performance.pcfPollingTimer";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            var timers = control.Attribute<int?>("timerCount");

            // Null is a bundle that was not read, which is not a control without timers.
            if (timers is null or 0) continue;

            yield return Fire.At(RuleId, control,
                ("timerCount", timers),
                ("callsWebApi", control.Attribute<bool?>("callsWebApi")),
                ("whatToCheck", "Whether each timer is cleared in destroy. One that is not keeps running "
                    + "after the form closes, and the user does not get it back until they reload."),
                ("notAlwaysWrong", "A control that displays elapsed time needs a timer and should keep it. "
                    + "This is worth reading as a question rather than as a defect."));
        }
    }
}

/// <summary>A control building its own markup.</summary>
public sealed class PcfInnerHtmlHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "security.pcfInnerHtml";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            var writes = control.Attribute<int?>("innerHtmlCount");

            if (writes is null or 0) continue;

            yield return Fire.At(RuleId, control,
                ("innerHtmlCount", writes),
                ("callsWebApi", control.Attribute<bool?>("callsWebApi")),
                ("theQuestion", "Where the strings come from. Constant markup is fine; anything built from "
                    + "record data is a user deciding what the form renders."),
                ("whyLow", "This is a surface rather than a finding about the code. It takes reading the "
                    + "source to tell which of the two it is, and that is not something a bundle can answer."));
        }
    }
}

/// <summary>A control reaching for something its manifest never asked for.</summary>
/// <remarks>
/// The one rule here that is a defect rather than a question. The others describe a cost or
/// a surface; this one describes a control that throws.
/// </remarks>
public sealed class PcfUndeclaredWebApiHandler : IRuleHandler
{
    /// <inheritdoc />
    public string RuleId => "quality.pcfUndeclaredWebApi";

    /// <inheritdoc />
    public IEnumerable<Finding> Run(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var control in context.OfType("pcfControl"))
        {
            if (control.Attribute<bool?>("callsWebApi") is not true) continue;

            // Declared, so the platform hands it over and there is nothing to report.
            if (control.Attribute<bool?>("usesWebApi") is true) continue;

            yield return Fire.At(RuleId, control,
                ("whenItFails", "At the first call, not at form load. The control renders correctly and "
                    + "breaks when somebody uses the part of it that reads data."),
                ("theFix", "One uses-feature element in the manifest, then rebuild and redeploy."));
        }
    }
}
