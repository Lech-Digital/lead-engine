# syntax=docker/dockerfile:1

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless a .csproj or NuGet.Config changes).
# NuGet.Config pins nuget.org so the build never touches any machine-global feed.
COPY NuGet.Config Coldstart.sln ./
COPY src/Coldstart.Domain/Coldstart.Domain.csproj             src/Coldstart.Domain/
COPY src/Coldstart.Application/Coldstart.Application.csproj    src/Coldstart.Application/
COPY src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj src/Coldstart.Infrastructure/
COPY src/Coldstart.Web/Coldstart.Web.csproj                    src/Coldstart.Web/
RUN dotnet restore src/Coldstart.Web/Coldstart.Web.csproj

# Copy the sources and publish the web app.
COPY src/ src/
RUN dotnet publish src/Coldstart.Web/Coldstart.Web.csproj -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Data Protection keys live on a mounted volume so invite/reset tokens and
# auth cookies survive redeploys. Make the dir owned by the non-root app user
# so the first-time named-volume mount inherits that ownership.
RUN mkdir -p /app/keys && chown -R $APP_UID /app/keys

COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Coldstart.Web.dll"]
