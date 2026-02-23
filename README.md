# AIDE Lite for Mendix Studio Pro

**AIDE: AI + IDE, right where you build**

An AI-powered IDE extension for Mendix Studio Pro — read your model, build your logic.

Built for **Mendix Studio Pro 10.24 LTS**. Uses the Claude API by Anthropic.

## Features

- **Chat with your app model** — Ask questions about your domain model, microflows, pages, and associations. Claude reads the actual Mendix model, not just file names.
- **Create microflows from natural language** — Describe what you need and Claude builds it with 21 activity types including CreateObject, ChangeObject, Retrieve, Commit, Delete, AggregateList, MicroflowCall, and more.
- **Edit existing microflows** — Rename microflows, add activities, edit activity properties in-place, or replace microflows entirely.
- **Deep folder support** — Finds microflows, pages, and enumerations regardless of folder nesting depth within modules.
- **Annotation reading** — Claude can read and understand annotations on your microflows.
- **Search across modules** — Find any entity, microflow, page, or enumeration by name across your entire project (Marketplace modules excluded).
- **Best practices built in** — Claude applies Mendix development guidelines covering XPath optimization, performance, naming conventions, and anti-patterns.
- **Streaming responses** — Tokens stream into the chat as Claude generates, with a progress bar showing processing status.
- **Context-aware** — Load full app context or scope to a single module for focused conversations.
- **Grounding rules** — Claude only references entities, attributes, and associations that actually exist in your loaded model. It will not hallucinate names.
- **Custom project rules** — Place a `.aide-lite-rules.md` file in your Mendix project root to provide project-specific instructions to Claude.

## Prerequisites

- **Mendix Studio Pro 10.24.x** (LTS)
- **.NET 8.0 SDK** (for building from source)
- **Claude API key** from [Anthropic](https://console.anthropic.com/)

## Quick Start

### 1. Clone & Build

```powershell
git clone https://github.com/Golden-Earth-Inc/AIDE-Lite.git
cd AIDE-Lite
dotnet build src -c Release
```

### 2. Deploy to Your Mendix Project

```powershell
.\deploy.ps1 -TargetProject "C:\Path\To\Your\MendixProject"
```

The deploy script builds in Release mode and copies the extension (DLL, manifest, web assets) to your project's `extensions/AideLite/` folder.

### 3. Add to .gitignore

In your **Mendix project** (not this repo), add to `.gitignore`:

```
/extensions/
```

### 4. Launch Studio Pro

Studio Pro must be started with the extension development flag:

```
"C:\Program Files\Mendix\10.24.0\modeler\studiopro.exe" --enable-extension-development
```

### 5. Open AIDE Lite

1. **Extensions** menu > **AIDE Lite Chat** (opens a dockable pane)
2. Click the **gear icon** to open Settings
3. Enter your **Claude API key** (encrypted locally with DPAPI — never sent anywhere except Anthropic's API)
4. Click the **refresh button** to load your app context
5. Start chatting

## What It Can Do

### Read your app model
> "What entities are in the Administration module?"
> "Show me the details of the Customer entity"
> "What microflows exist in the OrderProcessing module?"

### Create microflows
> "Create a microflow that retrieves all active customers and commits a log entry"
> "Build a SUB_Order_CalculateTotal that retrieves order lines and aggregates the amounts"

### Edit microflows
> "Rename ACT_ProcessOrder to ACT_Order_Process"
> "Add a Commit activity to the end of SUB_SaveCustomer"
> "Change the XPath constraint on the Retrieve in ACT_GetOrders"

### Advise on best practices
> "Is there a performance concern with how I'm retrieving orders?"
> "What's the best way to implement a batch cleanup?"

### Search across the project
> "Find anything named 'Customer' in the model"

## 14 Claude Tools

### Read Tools (9)
| Tool | Purpose |
|------|---------|
| `get_modules` | List all modules with document counts |
| `get_entities` | List entities in a module |
| `get_entity_details` | Full entity details (attributes, types, associations, generalization) |
| `get_associations` | All associations in a module |
| `get_enumerations` | Enumerations and their values |
| `get_pages` | List pages in a module |
| `get_microflows` | List microflows with parameter/return info |
| `get_microflow_details` | Full microflow (parameters, return type, activities, flow control, annotations) |
| `search_model` | Search any element by name across all modules |

### Write Tools (5)
| Tool | Purpose |
|------|---------|
| `create_microflow` | Create a microflow with sequential activities (21 types) |
| `rename_microflow` | Rename a microflow (all references auto-update) |
| `add_activities_to_microflow` | Add activities to an existing microflow |
| `replace_microflow` | Replace a microflow entirely (original backed up) |
| `edit_microflow_activity` | Edit activity properties in-place (preserves decisions/loops) |

## 21 Supported Activity Types

| Activity | Description |
|----------|-------------|
| CreateObject | Create a new entity instance |
| ChangeObject | Change attributes on an existing object |
| Retrieve | Database retrieve with XPath constraints |
| Commit | Commit objects to the database |
| DeleteObject | Delete an object |
| Rollback | Roll back uncommitted changes |
| CreateList | Create an empty typed list |
| AggregateList | COUNT, SUM, AVG, MIN, MAX on a list |
| MicroflowCall | Call another microflow |
| AssociationRetrieve | Retrieve objects over an association |
| Sort | Sort a list by an attribute |
| ChangeList | Set, add to, remove from, or clear a list |
| ListOperation | Union, intersect, subtract, contains, head, tail |
| FindByExpression | Find first item matching an expression |
| FindByAttribute | Find first item matching an attribute condition |
| FindByAssociation | Find first item matching an association condition |
| FilterByAttribute | Filter a list by an attribute condition |
| FilterByAssociation | Filter a list by an association condition |
| ChangeAssociation | Change an association on an object |
| AggregateByExpression | Aggregate with a custom expression |
| AggregateByAttribute | Aggregate by a specific attribute |

> **Note:** Only sequential activities are supported. Decisions, exclusive splits, loops, and merges cannot be created via the Extensions API. Claude uses a sub-microflow decomposition pattern for branching logic and guides you on where to add the 1-2 decisions manually.

## Configuration

Settings are stored in `%APPDATA%\AideLite\config.json`:

| Setting | Options | Default |
|---------|---------|---------|
| Model | `claude-sonnet-4-5-20250929`, `claude-opus-4-6`, `claude-haiku-4-5-20251001` | Sonnet 4.5 |
| Context Depth | `full` (all modules), `summary` (lighter), `none` (tools only) | summary |
| Max Tokens | 256 – 128,000 | 8,192 |

Your API key is encrypted with DPAPI (Windows Data Protection API) using app-specific entropy and stored locally. It never appears in logs, project files, or anywhere in this repository.

## Naming Conventions

Claude follows these prefixes when creating microflows:

| Prefix | Purpose | Example |
|--------|---------|---------|
| `ACT_` | User action (button click) | `ACT_Customer_Save` |
| `SUB_` | Sub-microflow (reusable logic) | `SUB_Order_CalculateTotal` |
| `DS_`  | Data source (page/widget) | `DS_Customer_GetActive` |
| `VAL_` | Validation | `VAL_Order_BeforeCommit` |

## Architecture

```
src/
├── Extensions/        # MEF entry points (pane, menu, context menu, web server)
├── ViewModels/        # WebView view models (chat pane, settings dialog)
├── Services/          # Claude API, config, conversation manager, prompt builder
├── ModelReaders/      # Read domain models, microflows, pages from the Mendix app
├── ModelWriters/      # Create and modify microflows via Mendix Extensions API
├── Tools/             # 14 Claude tool-use implementations + registry + executor
├── Models/            # DTOs, messages, instructions, config
├── Resources/         # Mendix best practices guidelines (embedded resource)
└── WebAssets/         # Chat UI (HTML/CSS/JS served via WebView)
```

### How It Works

1. The extension registers a dockable pane in Studio Pro via MEF
2. A WebView renders the chat UI (HTML/JS/CSS)
3. User messages are sent to the Claude API with tool definitions and the full app model as context
4. Claude can call tools to read model details or create/edit microflows
5. Tool results are sent back to Claude for continued reasoning (up to 5 tool rounds per message)
6. Microflow creation/editing happens through the Mendix Extensions API within transactions

## Custom Project Rules

Create a `.aide-lite-rules.md` file in the root of your Mendix project to provide project-specific instructions. These rules are injected into Claude's system prompt and can include:

- Domain-specific terminology
- Project naming conventions
- Business logic constraints
- Preferred patterns

## Known Limitations

| Limitation | Workaround |
|------------|------------|
| No decision/loop/merge creation | Claude creates sub-microflows and guides you to add 1-2 decisions manually |
| No page creation/modification | Claude provides guidance only |
| No nanoflow creation | Microflows only |
| No entity/enumeration creation | Read-only access to domain model |
| No security rule APIs | Claude provides guidance only |
| SSE streaming is buffered | Tokens arrive in small batches due to Mendix IHttpClient limitation |
| Requires `--enable-extension-development` | Standard Mendix requirement for custom extensions |

## Building from Source

```powershell
dotnet build src -c Release
```

Output goes to `src/bin/Release/net8.0-windows/`.

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Mendix.StudioPro.ExtensionsAPI | 10.23.0 | Mendix Studio Pro extension SDK |
| System.Security.Cryptography.ProtectedData | 8.0.0 | DPAPI encryption for API key storage |

## Tech Stack

- **.NET 8.0** (`net8.0-windows`)
- **Mendix.StudioPro.ExtensionsAPI** 10.23.0
- **MEF** (Managed Extensibility Framework) for extension discovery
- **WebView** for the chat UI
- **DPAPI** for API key encryption
- **Claude API** (Anthropic) via raw HTTP — no SDK dependency

## Security

- API keys are encrypted with DPAPI using app-specific entropy before storage
- Keys are never logged, committed, or transmitted outside of Anthropic API calls
- All model data stays local — context is built in-memory and sent only to the Claude API
- Conversation history is DPAPI-encrypted at rest in `%APPDATA%\AideLite\history\`
- WebView content is served from local files with no-cache headers and strict Content Security Policy
- Input sanitization prevents XSS in the chat UI
- Debug logs contain no user message content (only message lengths)
- Log files rotate at 5 MB with a single backup

## Data Privacy & Compliance

> **Important:** If your organization is subject to GDPR, SOC 2, or other data-protection regulations, review this section and Anthropic's terms before deploying AIDE Lite in a production environment.

### What Data Is Sent

| Data Category | Examples | When Sent |
|---------------|----------|-----------|
| App model context | Entity names, attribute names/types, association mappings, microflow names/parameters/activities, page names, enumeration values | Every API call (scope controlled by Context Depth setting) |
| Chat messages | Text you type in the chat input | Every API call |
| Tool results | Query results from model reads, microflow creation confirmations | During tool-use rounds |

### Where Data Is Processed

All data is sent via **HTTPS** to Anthropic's Claude API (`api.anthropic.com`). Anthropic is headquartered in **San Francisco, CA, USA** and processes data on US-based infrastructure.

- Review [Anthropic's Privacy Policy](https://www.anthropic.com/privacy) for details on how they handle data.
- Review [Anthropic's Terms of Service](https://www.anthropic.com/terms) for data processing terms.
- For enterprise use, contact Anthropic about their Data Processing Addendum (DPA).

### Data Stored Locally

| Item | Location | Protection |
|------|----------|------------|
| API key | `%APPDATA%\AideLite\config.json` | DPAPI-encrypted with app-specific entropy |
| Conversation history | `%APPDATA%\AideLite\history\` | DPAPI-encrypted (max 50 conversations) |
| Debug log | `%APPDATA%\AideLite\debug.log` | Plaintext, no message content, rotated at 5 MB |
| User settings | `%APPDATA%\AideLite\config.json` | Plaintext (model, context depth, token limits) |

### GDPR Considerations

- **Lawful basis (Art. 6):** A first-use consent dialog is presented before any data is sent to Anthropic. No API calls are made until the user accepts.
- **Data minimization (Art. 5(1)(c)):** The default context depth is `summary` to limit the amount of model data transmitted. Users can further reduce this to `none`.
- **Right to erasure (Art. 17):** Delete individual conversations from the History panel. Remove your API key from Settings to revoke API access.
- **Data export (Art. 20):** Export conversations as Markdown via the Export button.
- **Cross-border transfer (Art. 44-49):** Data is transferred to the US. Review Anthropic's transfer mechanisms (Standard Contractual Clauses, etc.) for your compliance requirements.
- **Data processor (Art. 28):** Anthropic acts as the data processor. Organizations should review Anthropic's DPA for contractual safeguards.

### SOC 2 Considerations

- **CC6.1 (Logical Access):** API keys are DPAPI-encrypted. Conversation history is encrypted at rest. Debug logs contain no sensitive content.
- **CC6.6 (System Boundaries):** WebView content is served with strict Content Security Policy headers. No inline scripts. All input is sanitized.
- **C1.1 (Confidentiality):** First-use consent dialog informs users about data processing before any data leaves the machine.
- **C1.2 (Disposal):** Conversation history is capped at 50 entries. Log files rotate at 5 MB.

### Current Compliance Gaps

| Gap | Risk | Mitigation |
|-----|------|------------|
| No formal Anthropic DPA in place | Data processor contract missing | Contact Anthropic for enterprise DPA |
| .NET 8.0 runtime may have unpatched CVEs | Runtime vulnerabilities | Keep .NET runtime updated to latest patch |
| No per-conversation data retention policy | Data kept until manually deleted | Users can delete via History panel |
| No admin audit trail | No central log of who used the extension | Debug log tracks activity locally |

### Recommendations for Enterprise Deployment

1. Review and sign Anthropic's Data Processing Addendum (DPA) before deployment.
2. Set Context Depth to `summary` or `none` to minimize data exposure.
3. Keep the .NET 8.0 runtime updated to the latest security patch.
4. Establish a data retention policy for conversation history.
5. Ensure users understand and accept the data consent dialog before use.
6. Consider network-level controls (firewall rules) to restrict outbound traffic to `api.anthropic.com` only.

## AIDE Pro & Partnerships

For access to the pro version or partnership inquiries, reach out to **Neel Desai**:

- **LinkedIn:** [Neel Desai](https://www.linkedin.com/in/neeldesai/)
- **Email:** hello@goldenearth.io

## Disclaimer

This extension was built as a fun weekend project by Neel with assistance from Claude Co-Pilot. Please use at your own discretion. Asking the app owner for permission before using an AI agent on their app is recommended.

## License

MIT
