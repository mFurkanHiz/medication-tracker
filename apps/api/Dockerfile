FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY apps/api/ apps/api/
COPY .config/ .config/
RUN dotnet restore apps/api
RUN dotnet publish apps/api -c Release -o /app --no-restore
RUN dotnet tool restore
ENV ConnectionStrings__Database="Host=localhost;Database=design_only;Username=design_only"
RUN dotnet ef migrations script --idempotent --project apps/api --output /app/migrations.sql

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MedicationTracker.Api.dll"]
