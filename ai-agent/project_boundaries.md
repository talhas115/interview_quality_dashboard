# IN SCOPE

Version 1 includes:

- .NET 10 Worker Service
- Microsoft Graph API Integration
- Teams Group Monitoring
- Meeting Retrieval
- Transcript Retrieval
- Meeting Description Retrieval
- JD Extraction
- Prompt Injection
- OpenAI Integration
- Claude Integration
- Markdown Report Generation
- Processed Meeting Tracking
- MSI Setup Package
- Configuration Driven Design
- Structured Logging

---

# OUT OF SCOPE

Do NOT implement:

- CQRS
- MediatR
- Event Sourcing
- Service Bus
- RabbitMQ
- Kafka
- Azure Functions
- Microservices
- Dashboard
- Admin Portal
- Authentication UI
- Database
- SQL Server
- PostgreSQL
- Redis
- SignalR
- Queue Processing
- Blob Storage
- SharePoint Storage
- Email Notifications
- Teams Notifications
- Historical Question Analysis
- Multi Tenant Support
- AI Agent Frameworks
- LangChain
- Semantic Kernel
- Auto Prompt Generation
- Auto Prompt Optimization
- RAG Architecture
- Vector Database

---

# ARCHITECTURE RESTRICTIONS

Must Use:

- Clean Architecture
- Dependency Injection
- Interface Driven Design

Must Not Use:

- Static Helper Classes
- Global State
- Hardcoded Values
- Tight Coupling

---

# CONFIGURATION RESTRICTIONS

Everything must be configurable:

- Scheduler Interval
- Prompt Location
- Group IDs
- Graph API Settings
- LLM Settings
- Folder Locations
- Parsing Rules
- Logging Settings

No business value may be hardcoded.

---

# DEPLOYMENT RESTRICTIONS

Deployment target:

Windows Server

Installer:

MSI Only

No Docker.

No Kubernetes.

No Azure Deployment requirement in V1.