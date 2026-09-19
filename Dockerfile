# ===========================================================================
# One image: the API, the React workspace it serves, and the worker.
#
# One image rather than two, because this product is deployed per engagement
# and per client. One thing to build, one thing to tag, one thing to delete.
# The Container App runs the API entry point and the Container Apps Job runs
# the worker entry point, from the same layers.
#
# It also removes a whole class of failure. Two images can disagree about which
# build they are: the API offers a screen the worker's pipeline does not
# implement, and nothing in either version number says so.
# ===========================================================================

FROM node:22-alpine AS web-build
WORKDIR /src

# The build log has to stay ASCII.
#
# az acr build streams the registry's log back and prints it through colorama, which writes
# to the Windows console in cp1252 whatever PYTHONIOENCODING says. One tick character from
# Vite's "modules transformed" line crashes the client mid-build, and the error it prints is
# a Python traceback about a codec rather than anything to do with the image.
#
# So the noisy tools are told to be quiet. --logLevel warn is what removes the tick.
ENV NPM_CONFIG_FUND=false \
    NPM_CONFIG_AUDIT=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false

# Dependencies before sources, so editing a page does not reinstall node_modules.
COPY src/web/package*.json ./src/web/
RUN npm ci --prefix src/web

COPY src/web ./src/web

# The web build reaches outside its own project for two things, and both are compiled into
# the bundle rather than fetched: the sign-in screen has to render in the reader's language
# before any request has returned.
#
# The translations, which check-vocabulary.mjs also reads to refuse a key with no English
# string, and the locale contract, which is the list of languages on offer.
COPY src/PowerPete.Analyzer.Domain/Localization/Resources ./src/PowerPete.Analyzer.Domain/Localization/Resources
COPY build/contracts ./build/contracts

# Created rather than copied from a placeholder. Vite empties this directory on every build,
# so a file kept here to hold the folder open would be deleted by the step that needs it.
RUN mkdir -p ./src/PowerPete.Analyzer.Api/wwwroot/app

RUN npm run build --prefix src/web -- --logLevel warn

# The written documentation is served as files rather than paraphrased into a panel, so a
# consultant can read it offline and it cannot go stale against the product.
COPY docs ./docs
RUN mkdir -p ./src/PowerPete.Analyzer.Api/wwwroot/documentation \
 && cp -R docs/. ./src/PowerPete.Analyzer.Api/wwwroot/documentation/

# The public site, built here into the web root. It reads the contracts so the rule count and
# the reach matrix on the page are the product's own numbers rather than a second copy that
# drifts. English at the root, every other language in a folder of its own.
COPY src/microsite ./src/microsite
COPY src/web/public ./src/web/public
RUN node src/microsite/build.mjs src/microsite src/PowerPete.Analyzer.Api/wwwroot

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS publish
WORKDIR /src

COPY . .
COPY --from=web-build /src/src/PowerPete.Analyzer.Api/wwwroot ./src/PowerPete.Analyzer.Api/wwwroot

# The generated code is gitignored, so whether it reaches the image depends on the machine
# that packed the build context having run the generator. That is a real way to ship an image
# built from a stale contract, so it is checked rather than assumed. The publish script runs
# the generator first; this is what catches a hand rolled docker build that did not.
#
# Checked rather than generated here because the generator is PowerShell and the SDK image
# has none. Installing it would add a hundred megabytes to every build to run one script that
# the publish script already ran.
RUN test -f src/PowerPete.Analyzer.Domain/Generated/ComponentCatalogue.g.cs && test -f src/PowerPete.Analyzer.Domain/Generated/RuleCatalogue.g.cs || (echo 'The generated code is missing from the build context. Run ./build/Invoke-CodeGen.ps1 and build again.' && exit 1)

RUN dotnet restore Analyzer.slnx

RUN dotnet publish src/PowerPete.Analyzer.Api/PowerPete.Analyzer.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

RUN dotnet publish src/PowerPete.Analyzer.Jobs/PowerPete.Analyzer.Jobs.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# A shell entry point rather than the assembly directly, so the Container Apps Job can pass
# arguments through to the command dispatcher without the platform's own argument handling
# getting in the way.
#
# analyzer.dll, not PowerPete.Analyzer.Jobs.dll. The Jobs project sets AssemblyName to
# "analyzer" so its command line reads like a tool, and this line named the project instead.
# Nothing caught it. The image built, the API half of it ran perfectly, and the worker crash
# looped every five minutes from the first deployment onwards saying the application did not
# exist. Every run queued through the product sat in the queue, and nobody read the worker's
# log because the product it feeds looked healthy.
#
# So the name is asserted rather than trusted. If the assembly name changes again the image
# fails to build, which is somewhere somebody is looking.
RUN test -f /app/publish/analyzer.dll \
 || (echo 'The worker assembly is not analyzer.dll. Check AssemblyName in PowerPete.Analyzer.Jobs.csproj and fix the entry point below to match.'; ls /app/publish/*.dll; exit 1)

# "work" when nothing is passed, because the only thing that runs this image without
# arguments is the worker container app, and the dispatcher's answer to no arguments is to
# print its help and exit 0. That is what the container did once it could find the assembly:
# started, printed the help, exited successfully, restarted, for ever, with nothing in the log
# that reads like a failure. The bicep passes "work" explicitly as well; this is so an image
# deployed without it still polls rather than looping quietly.
RUN printf '#!/bin/sh\nif [ $# -eq 0 ]; then set -- work; fi\nexec dotnet /app/analyzer.dll "$@"\n' > /app/publish/jobs-entrypoint.sh \
 && chmod +x /app/publish/jobs-entrypoint.sh

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=publish /app/publish .

# The translation bundles, which /api/locales/{code}/{ns} reads at run time. They are data
# rather than code, so nothing in the publish output carries them and the language picker
# would offer six languages and serve one.
COPY src/PowerPete.Analyzer.Domain/Localization/Resources ./src/PowerPete.Analyzer.Domain/Localization/Resources

# The contracts, which /api/extraction-modes serves. The connection wizard is generated from
# extraction-sources.json rather than describing the modes a second time in the API.
COPY build/contracts ./build/contracts

ENTRYPOINT ["dotnet", "PowerPete.Analyzer.Api.dll"]
