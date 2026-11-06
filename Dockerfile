# One Dockerfile for every .NET service: docker build --build-arg PROJECT=GridPulse.Api .
ARG PROJECT

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT
ENV APP_DLL="${PROJECT}.dll" \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
