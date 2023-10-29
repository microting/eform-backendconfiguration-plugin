FROM node:18-bookworm-slim as node-env
WORKDIR /app
ENV PATH /app/node_modules/.bin:$PATH
COPY eform-angular-frontend/eform-client ./
RUN yarn install
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:7.0-bookworm-slim AS build-env
WORKDIR /app
ARG GITVERSION
ARG PLUGINVERSION
ARG PLUGIN2VERSION
ARG PLUGIN3VERSION
ARG PLUGIN4VERSION
ARG PLUGIN5VERSION
ARG PLUGIN6VERSION

# Copy csproj and restore as distinct layers
COPY eform-angular-frontend/eFormAPI/eFormAPI.Web ./eFormAPI.Web
COPY eform-angular-items-planning-plugin/eFormAPI/Plugins/ItemsPlanning.Pn ./ItemsPlanning.Pn
COPY eform-angular-timeplanning-plugin/eFormAPI/Plugins/TimePlanning.Pn ./TimePlanning.Pn
COPY eform-backendconfiguration-plugin/eFormAPI/Plugins/BackendConfiguration.Pn ./BackendConfiguration.Pn
RUN dotnet publish eFormAPI.Web -o eFormAPI.Web/out /p:Version=$GITVERSION --runtime linux-x64 --configuration Release
RUN dotnet publish ItemsPlanning.Pn -o ItemsPlanning.Pn/out /p:Version=$PLUGINVERSION --runtime linux-x64 --configuration Release
RUN dotnet publish TimePlanning.Pn -o TimePlanning.Pn/out /p:Version=$PLUGIN5VERSION --runtime linux-x64 --configuration Release
RUN dotnet publish BackendConfiguration.Pn -o BackendConfiguration.Pn/out /p:Version=$PLUGIN4VERSION --runtime linux-x64 --configuration Release

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0-bookworm-slim
WORKDIR /app
COPY --from=build-env /app/eFormAPI.Web/out .
RUN mkdir -p ./Plugins/ItemsPlanning.Pn
RUN mkdir -p ./Plugins/TimePlanning.Pn
RUN mkdir -p ./Plugins/BackendConfiguration.Pn
COPY --from=build-env /app/ItemsPlanning.Pn/out ./Plugins/ItemsPlanning.Pn
COPY --from=build-env /app/BackendConfiguration.Pn/out ./Plugins/BackendConfiguration.Pn
COPY --from=build-env /app/TimePlanning.Pn/out ./Plugins/TimePlanning.Pn
COPY --from=node-env /app/dist wwwroot

ENV DEBIAN_FRONTEND noninteractive
ENV Logging__Console__FormatterName=

ENTRYPOINT ["dotnet", "eFormAPI.Web.dll"]
