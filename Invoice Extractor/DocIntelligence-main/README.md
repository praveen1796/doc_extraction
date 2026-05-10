# Generic Document Extraction Service

A production-grade **zero-code-extensible** document extraction web service built on ASP.NET Core 8 and Azure OpenAI GPT-4o. Extract structured data from any document type — invoices, purchase orders, timesheets, tour sheets, and more — by simply dropping a configuration folder. No code changes, no redeployment.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                     DocumentExtractionService                       │
│                                                                     │
│  ┌──────────────┐   ┌───────────────────────────────────────────┐  │
│  │  REST API    │   │          Plugin System                    │  │
│  │  (ASP.NET 8) │   │  DocumentTypes/                           │  │
│  │              │   │    invoice/          ← drop folder here   │  │
│  │  POST /extract│   │      config.json                         │  │
│  │  POST /batch  │   │      system_prompt.txt                   │  │
│  │  GET /types   │   │      extraction_prompt.txt               │  │
│  └──────┬───────┘   │      schema.json                         │  │
│         │           │    purchase_order/                        │  │
│  ┌──────▼───────┐   │    timesheet/                             │  │
│  │   Services   │   │    toursheet/                             │  │
│  │              │   │    your_new_type/  ← add anytime          │  │
│  │ ExtractionSvc│◄──┤  DocumentTypeRegistry (hot-reload)        │  │
│  │ OpenAI Svc   │   └───────────────────────────────────────────┘  │
│  │ PDF Processor│                                                   │
│  │ Validation   │   ┌───────────────────────────────────────────┐  │
│  └──────┬───────┘   │         Security Layer                    │  │
│         │           │  OAuth 2.0 JWT (Azure AD) + API Keys      │  │
│         ▼           │  Rate Limiting (sliding window)           │  │
│  Azure OpenAI GPT-4o│  Correlation IDs + Audit Logging          │  │
└─────────────────────┴───────────────────────────────────────────────┘
```

---

## Quick Start

### 1. Clone & Configure

```bash
git clone <repo-url>
cd DocumentExtractionService

# Copy sample config and fill in your Azure OpenAI credentials
cp src/DocumentExtractionService.Api/appsettings.json appsettings.Production.json
```

Edit `appsettings.Production.json` (or use environment variables):
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-key-here",
    "DeploymentName": "gpt-4o"
  }
}
```

### 2. Run with Docker (Recommended)

```bash
# Set your Azure OpenAI key
export AZURE_OPENAI_KEY=your-key-here

# Start the service
docker compose up -d

# Check health
curl http://localhost:8080/health
```

### 3. Run Locally

```bash
cd src/DocumentExtractionService.Api
dotnet run

# Service starts at http://localhost:5000
```

---

## API Reference

### Extract a Document

```http
POST /api/v1/extraction/extract
Authorization: Bearer <jwt-token>   (or X-Api-Key: <key>)
Content-Type: multipart/form-data

file=@invoice.pdf
documentType=invoice
```

**Response:**
```json
{
  "requestId": "req_abc123",
  "documentType": "invoice",
  "fileName": "invoice.pdf",
  "status": "Success",
  "processingTimeMs": 3241,
  "data": {
    "vendor_name": "Amazon Web Services, Inc.",
    "invoice_number": "2445304273",
    "invoice_date": "2026-01-01",
    "total_amount": 3539.19,
    "currency": "USD",
    ...
  },
  "validation": {
    "isValid": true,
    "overallConfidence": 0.97,
    "errors": [],
    "warnings": []
  }
}
```

### Batch Extract

```http
POST /api/v1/extraction/batch
Content-Type: multipart/form-data

files=@invoice1.pdf
files=@invoice2.pdf
documentType=invoice
async=true
```

**Async job polling:**
```http
GET /api/v1/extraction/jobs/{jobId}
```

### List Document Types

```http
GET /api/v1/document-types
```

```json
[
  {
    "id": "invoice",
    "displayName": "AP Invoice",
    "description": "Accounts Payable invoice extraction...",
    "version": "6.0",
    "enabled": true,
    "acceptedFileTypes": ["pdf", "png", "jpg"]
  },
  {
    "id": "purchase_order",
    "displayName": "Purchase Order",
    ...
  }
]
```

### Reload Document Types (Admin)

```http
POST /api/v1/document-types/reload
Authorization: Bearer <admin-jwt>
```

### Health Endpoints

```http
GET /health          # Full diagnostic report
GET /health/live     # Liveness probe (Kubernetes)
GET /health/ready    # Readiness probe (Kubernetes)
```

---

## Adding a New Document Type (Zero-Code)

This is the core feature. To support a new document type tomorrow:

### Step 1: Create the folder

```
DocumentTypes/
└── expense_report/          ← new document type ID
    ├── config.json           ← metadata + settings
    ├── system_prompt.txt     ← GPT role + extraction rules
    ├── extraction_prompt.txt ← step-by-step field extraction
    └── schema.json           ← JSON Schema for output validation
```

### Step 2: Write `config.json`

```json
{
  "id": "expense_report",
  "displayName": "Expense Report",
  "description": "Employee expense reports with line-item receipts",
  "version": "1.0",
  "enabled": true,
  "acceptedFileTypes": ["pdf", "png", "jpg"],
  "maxFileSizeMb": 20,
  "maxPages": 15,

  "extraction": {
    "model": "gpt-4o",
    "reasoningEffort": "medium",
    "temperature": 0,
    "maxTokens": 4000,
    "dualPassEnabled": true,
    "dualPassStrategy": "text_search",
    "imageResolutionDpi": 150,
    "imagesBeforeText": true
  },

  "validation": {
    "minConfidenceScore": 0.5,
    "rules": [
      {
        "field": "employee_name",
        "type": "required",
        "severity": "Error",
        "message": "Employee name is required"
      },
      {
        "field": "report_date",
        "type": "date",
        "severity": "Error",
        "message": "Report date must be YYYY-MM-DD"
      }
    ]
  }
}
```

### Step 3: Write the prompts

**`system_prompt.txt`** — GPT's role and extraction rules:
```
You are an expert expense report extraction specialist.

Your task: Extract structured data from employee expense reports.
Return ONLY valid JSON.

RULE 1 — REQUIRED FIELDS:
- employee_name: The employee who submitted the report
- report_date: Date submitted → YYYY-MM-DD
- total_amount: Grand total requested for reimbursement

RULE 2 — LINE ITEMS: Each expense entry with date, category,
description, amount, and receipt reference.
```

**`extraction_prompt.txt`** — Step-by-step instructions with output template:
```
Extract all expense report data from the attached document.

FILE: {{FILE_NAME}} | PAGES: {{PAGE_COUNT}}

Return JSON:
{
  "document_type": "Expense Report",
  "employee_name": "",
  "report_date": "",
  "department": "",
  "total_amount": null,
  "expenses": [...]
}
```

### Step 4: Hot-reload (no restart needed!)

```bash
# The FileSystemWatcher picks this up automatically within 5 seconds.
# Or force reload:
curl -X POST http://localhost:8080/api/v1/document-types/reload \
     -H "Authorization: Bearer <admin-token>"
```

That's it. Your new document type is live.

---

## Configuration Reference

### `appsettings.json` — Top-level settings

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-key",
    "DeploymentName": "gpt-4o"
  },

  "Auth": {
    "Enabled": true,
    "AzureAd": {
      "Enabled": true,
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "Audience": "api://your-client-id"
    },
    "ApiKey": {
      "Enabled": true,
      "HeaderName": "X-Api-Key",
      "Keys": ["key1-replace-me", "key2-replace-me"]
    }
  },

  "RateLimiting": {
    "DefaultWindowSeconds": 60,
    "DefaultRequestsPerWindow": 60,
    "DefaultDailyLimit": 1000,
    "BurstSize": 10
  },

  "DocumentTypesPath": "DocumentTypes",

  "Processing": {
    "MaxFileSizeMb": 50,
    "TempDirectory": "/tmp/docextract",
    "MaxConcurrentJobs": 4,
    "BatchMaxFiles": 20
  }
}
```

### Environment Variables

All settings can be overridden via environment variables using the `DOCEXTRACT_` prefix with `__` for nested keys:

```bash
DOCEXTRACT_AzureOpenAI__Endpoint=https://...
DOCEXTRACT_AzureOpenAI__ApiKey=your-key
DOCEXTRACT_Auth__Enabled=false        # Disable auth for local dev
DOCEXTRACT_DocumentTypesPath=/app/DocumentTypes
```

---

## Document Type Plugin Reference

### `config.json` Full Schema

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | ✅ | Unique identifier (lowercase, underscores). Used in API calls. |
| `displayName` | string | ✅ | Human-readable name shown in API responses |
| `description` | string | | Description shown in `/document-types` endpoint |
| `version` | string | | Your version of this document type config |
| `enabled` | bool | | Set `false` to disable without deleting |
| `acceptedFileTypes` | array | | `["pdf", "png", "jpg", "jpeg", "tiff"]` |
| `maxFileSizeMb` | int | | Max upload size for this type |
| `maxPages` | int | | Max pages to process |
| `extraction.model` | string | | OpenAI model. Always use `"gpt-4o"` |
| `extraction.reasoningEffort` | string | | `"low"`, `"medium"`, `"high"` |
| `extraction.temperature` | float | | Always `0` for deterministic extraction |
| `extraction.dualPassEnabled` | bool | | Enable dual-pass verification |
| `extraction.dualPassStrategy` | string | | `"text_search"` (recommended) |
| `extraction.imageResolutionDpi` | int | | DPI for PDF→image conversion (150–300) |
| `extraction.imagesBeforeText` | bool | | Send images before extracted text in prompt |
| `validation.minConfidenceScore` | float | | Minimum confidence to pass (0.0–1.0) |
| `validation.rules` | array | | Field validation rules (see below) |

### Validation Rule Types

| Rule Type | Description | Extra Fields |
|-----------|-------------|--------------|
| `required` | Field must not be null or empty | — |
| `date` | Field must be YYYY-MM-DD format | — |
| `format` | Field must match regex | `pattern` |
| `regex` | Explicit regex match | `pattern` |
| `range` | Numeric range check | `min`, `max` |
| `not_value` | Field must NOT equal value | `value` |
| `cross_field` | Compare two fields | `compareField`, `operator` |

### Prompt Template Variables

Use these placeholders in `extraction_prompt.txt`:

| Variable | Description |
|----------|-------------|
| `{{FILE_NAME}}` | Original uploaded filename |
| `{{PAGE_COUNT}}` | Total number of pages in document |
| `{{IS_SCANNED}}` | `true` if PDF appears to be a scan |
| `{{EXTRACTION_METHOD}}` | `"image_primary"` or `"text_primary"` |
| `{{DOCUMENT_TYPE}}` | Document type ID |

---

## Security

### Authentication

The service supports two authentication methods, both active simultaneously:

**1. Azure AD JWT (Primary)**
```http
Authorization: Bearer eyJ0eXAiOiJKV1Qi...
```
Configure in `appsettings.json`:
```json
"AzureAd": {
  "Enabled": true,
  "TenantId": "your-tenant-id",
  "ClientId": "your-api-client-id"
}
```

**2. API Key (Service-to-Service / Fallback)**
```http
X-Api-Key: your-api-key-here
```
Configure keys in `appsettings.json`:
```json
"ApiKey": {
  "Enabled": true,
  "Keys": ["key-1-here", "key-2-here"]
}
```

### Authorization Policies

| Policy | Who | Used On |
|--------|-----|---------|
| `Authenticated` | Any authenticated user | `/api/v1/extraction/*` |
| `AdminOnly` | Azure AD users with `Admin` role | `/api/v1/document-types/reload` |

### Rate Limiting

Sliding window rate limiting is applied per client (API key or JWT subject):

- Default: 60 requests/minute, 1000/day
- Per document type overrides via `rateLimiting` in `config.json`
- Headers returned: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `Retry-After`

---

## Included Document Types

| Type ID | Display Name | Description |
|---------|-------------|-------------|
| `invoice` | AP Invoice | Full Nabors AP invoice extraction (v6.0 — all vendor patterns) |
| `purchase_order` | Purchase Order | Nabors iProcurement POs and vendor acknowledgements |
| `timesheet` | Field Service Timesheet | Oilfield service timesheets with employee hours |
| `toursheet` | Drilling Tour Sheet | Rig shift reports with depth, mud, BHA data |

---

## Deployment

### Azure Container Apps (Recommended)

```bash
# Build and push image
az acr build --registry yourregistry --image doc-extractor:latest .

# Deploy
az containerapp create \
  --name doc-extraction-service \
  --resource-group your-rg \
  --image yourregistry.azurecr.io/doc-extractor:latest \
  --target-port 8080 \
  --ingress external \
  --env-vars \
    DOCEXTRACT_AzureOpenAI__Endpoint=https://... \
    DOCEXTRACT_AzureOpenAI__ApiKey=secretref:openai-key \
    DOCEXTRACT_Auth__AzureAd__TenantId=your-tenant
```

### Docker Swarm (Nabors On-Prem)

```yaml
# docker-stack.yml (add to existing swarm)
version: '3.9'
services:
  doc-extractor:
    image: yourregistry/doc-extraction-service:latest
    ports:
      - "8080:8080"
    deploy:
      replicas: 2
      restart_policy:
        condition: on-failure
    environment:
      - DOCEXTRACT_AzureOpenAI__Endpoint=https://...
    volumes:
      - doc-types:/app/DocumentTypes
volumes:
  doc-types:
    driver: local
    driver_opts:
      type: nfs
      o: addr=fileserver,rw
      device: ":/docextract/DocumentTypes"
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: doc-extraction-service
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: doc-extractor
        image: yourregistry/doc-extraction-service:latest
        ports:
        - containerPort: 8080
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        env:
        - name: AZURE_OPENAI_KEY
          valueFrom:
            secretKeyRef:
              name: openai-secret
              key: api-key
        volumeMounts:
        - name: document-types
          mountPath: /app/DocumentTypes
      volumes:
      - name: document-types
        configMap:
          name: document-types-config
```

---

## Project Structure

```
DocumentExtractionService/
├── DocumentExtractionService.sln
├── Dockerfile
├── docker-compose.yml
│
└── src/
    ├── DocumentExtractionService.Core/        ← Business logic (no HTTP)
    │   ├── Configuration/
    │   │   └── AppSettings.cs
    │   ├── Models/
    │   │   ├── ExtractionModels.cs            ← Request/Response DTOs
    │   │   └── DocumentTypeConfig.cs          ← Plugin config model
    │   └── Services/
    │       ├── DocumentTypeRegistry.cs        ← Plugin loader + hot-reload
    │       ├── GenericOpenAIService.cs        ← Azure OpenAI client
    │       ├── GenericExtractionService.cs    ← Orchestration + dual-pass
    │       ├── PdfProcessorService.cs         ← PDF → images + text
    │       └── ConfigurableValidationService.cs ← Rule-based validation
    │
    └── DocumentExtractionService.Api/         ← HTTP layer
        ├── Auth/
        │   └── ApiKeyAuthHandler.cs           ← API Key auth scheme
        ├── Controllers/
        │   ├── ExtractionController.cs        ← POST /extract, /batch, /jobs
        │   ├── DocumentTypesController.cs     ← GET /document-types, POST /reload
        │   └── HealthController.cs            ← GET /health, /live, /ready
        ├── Middleware/
        │   ├── ExceptionHandlingMiddleware.cs ← ProblemDetails error responses
        │   └── RequestLoggingMiddleware.cs    ← Correlation IDs + audit
        ├── DocumentTypes/                     ← Plugin folder (hot-reload)
        │   ├── invoice/
        │   │   ├── config.json
        │   │   ├── system_prompt.txt
        │   │   ├── extraction_prompt.txt
        │   │   └── schema.json
        │   ├── purchase_order/
        │   ├── timesheet/
        │   └── toursheet/
        ├── Program.cs                         ← DI + middleware setup
        └── appsettings.json
```

---

## Extending the Service

### Adding a new vendor pattern (invoice type)

Edit `DocumentTypes/invoice/system_prompt.txt` and add the vendor to the known vendor list. No restart needed — the prompt is read from disk on every request.

### Adding a new language

Add Spanish/French/Arabic extraction rules to the existing system prompt, or create a new document type (e.g., `invoice_es`) with locale-specific rules.

### Enabling Excel export for a new type

Set `output.excelExportEnabled: true` in `config.json` and the service will automatically generate an Excel file alongside the JSON response.

### Adding authentication for a specific document type

Set a per-type policy in `config.json`:
```json
"auth": {
  "requiredRole": "FinanceTeam",
  "additionalPolicies": ["AuditLog"]
}
```

---

## FAQ

**Q: How do I add a new document type without touching code?**
A: Drop a folder under `DocumentTypes/` with `config.json`, `system_prompt.txt`, `extraction_prompt.txt`, and `schema.json`. It's available within 5 seconds via hot-reload.

**Q: How is the dual-pass verification different from just calling GPT twice?**
A: The first pass uses images + text. The second pass uses `text_search` strategy — if any critical field (vendor_name, invoice_number, invoice_date, total_amount) has low confidence, the service runs a targeted text search on the extracted document text to verify it before returning. This avoids "louder instructions" (the hallucination trap) and instead provides evidence.

**Q: Can I use a different GPT model per document type?**
A: Yes — set `extraction.model` in each `config.json`. You can use `gpt-4o` for complex invoices and `gpt-4o-mini` for simple structured forms to save cost.

**Q: How do I disable auth for local development?**
A: Set `DOCEXTRACT_Auth__Enabled=false` or `"Auth": { "Enabled": false }` in `appsettings.Development.json`.

**Q: What happens if a document type folder is malformed?**
A: The registry logs a warning and skips that type. All other document types continue to work. Check `/health` for the list of loaded types.
