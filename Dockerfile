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

# The public site, built here, and its assets. The connector picker in the product reads
# its vendor logos from the same folder, so the two cannot show different marks.
COPY src/microsite ./src/microsite
COPY build/contracts ./build/contracts
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
RUN printf '#!/bin/sh\nexec dotnet PowerPete.Analyzer.Jobs.dll "$@"\n' > /app/publish/jobs-entrypoint.sh \
 && chmod +x /app/publish/jobs-entrypoint.sh

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PowerPete.Analyzer.Api.dll"]
