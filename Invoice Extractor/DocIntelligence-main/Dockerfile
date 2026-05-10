# ═══════════════════════════════════════════════════════════════════════════════
#  Generic Document Extraction Service — Dockerfile
#  Multi-stage build for a lean production image
# ═══════════════════════════════════════════════════════════════════════════════

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy solution + project files first (enables Docker layer caching for NuGet restore)
COPY DocumentExtractionService.sln .
COPY src/DocumentExtractionService.Core/DocumentExtractionService.Core.csproj \
     src/DocumentExtractionService.Core/
COPY src/DocumentExtractionService.Api/DocumentExtractionService.Api.csproj \
     src/DocumentExtractionService.Api/

# Restore NuGet packages
RUN dotnet restore DocumentExtractionService.sln

# Copy remaining source
COPY src/ src/

# Build and publish (Release, trimmed)
RUN dotnet publish src/DocumentExtractionService.Api/DocumentExtractionService.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install libgdiplus for System.Drawing (PDF image processing)
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Copy published output
COPY --from=build /app/publish .

# Create required directories with proper permissions
RUN mkdir -p /app/DocumentTypes /app/logs /tmp/docextract \
    && chown -R appuser:appgroup /app /tmp/docextract

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Environment variables (override in docker-compose or Kubernetes)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOCEXTRACT_DocumentTypesPath=/app/DocumentTypes

# Health check for Docker
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "DocumentExtractionService.Api.dll"]
