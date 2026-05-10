# ═══════════════════════════════════════════════════════════════════════════════
#  Generic Document Extraction Service — Dockerfile
#  Multi-stage build for a lean production image
#
#  FIX v1.3-hotfix:
#  - Added fontconfig + fonts-dejavu for international PDF text extraction
#  - Added curl for healthcheck endpoint
#  - Added libgdiplus verification step
#  - Added web.config for IIS request timeout override
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

# Build and publish (Release)
RUN dotnet publish src/DocumentExtractionService.Api/DocumentExtractionService.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# ══════════════════════════════════════════════════════════════════════════════
#  FIX PDF-2: Install libgdiplus + font packages.
#
#  libgdiplus: Required by System.Drawing.Bitmap for PDF page → PNG conversion.
#              WITHOUT this, ConvertBgraToPng() silently returns empty bytes
#              and ALL vision-based extraction fails (catastrophic for well plans).
#
#  libc6-dev:  Required dependency for libgdiplus.
#
#  fontconfig + fonts-dejavu-core: Required for proper text rendering in PDFs
#              with international characters (Spanish: ñ, á, é; Arabic: ع, ش).
#              Without fonts, text extraction may return boxes or garbage.
#
#  curl:       Required for Docker HEALTHCHECK endpoint.
# ══════════════════════════════════════════════════════════════════════════════
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgdiplus \
    libc6-dev \
    fontconfig \
    fonts-dejavu-core \
    curl \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -fv

# Verify libgdiplus is installed and loadable
RUN ldconfig && ldconfig -p | grep libgdiplus && echo "✅ libgdiplus verified" || \
    (echo "❌ libgdiplus NOT FOUND — PDF image rendering will fail" && exit 1)

# Create non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Copy published output
COPY --from=build /app/publish .

# Copy web.config for IIS timeout override (needed if deployed to Azure App Service)
COPY web.config ./web.config 2>/dev/null || true

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
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DocumentExtractionService.Api.dll"]
