# eForm Backend Configuration Plugin

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

This is a plugin component for the Microting eForm system that provides backend configuration management. It consists of a .NET 9.0 backend API plugin and Angular frontend components designed to integrate into the larger eform-angular-frontend ecosystem.

## Working Effectively

### Prerequisites - Install .NET 9.0 SDK
- Download and install .NET 9.0 SDK: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 9.0.102`
- Add to PATH: `export PATH="$HOME/.dotnet:$PATH"`
- Verify installation: `dotnet --version` (should show 9.0.102 or later)

### Build the .NET Plugin Only (Standalone Development)
- Navigate to plugin directory: `cd eFormAPI/Plugins/BackendConfiguration.Pn/`
- Build solution: `dotnet build BackendConfiguration.Pn.sln` -- takes ~47 seconds on first build, ~3 seconds on subsequent builds. NEVER CANCEL. Set timeout to 120+ seconds.
- Run unit tests: `dotnet test --no-restore -c Release -v n BackendConfiguration.Pn.Test/BackendConfiguration.Pn.Test.csproj` -- takes ~2 seconds. Set timeout to 30+ seconds.
- Run integration tests: `dotnet test --no-restore -c Release -v n BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj` -- takes 10+ minutes and requires database. NEVER CANCEL. Set timeout to 30+ minutes.

### Full System Integration (Multi-Repository Development)
**WARNING**: This plugin cannot run standalone. It requires the full eform-angular-frontend ecosystem with multiple plugin dependencies.

The build process requires these repositories checked out as siblings:
- `eform-angular-frontend` (main application)
- `eform-angular-items-planning-plugin` 
- `eform-angular-timeplanning-plugin`
- `eform-backendconfiguration-plugin` (this repo)

Integration workflow from GitHub Actions:
1. Check out all required repositories as sibling directories
2. Copy plugin files using `devinstall.sh` or `devinstall.py`
3. Build Docker container with all components: `docker build . -t container:latest --build-arg GITVERSION=1.0.0 --build-arg PLUGINVERSION=1.0.0 --build-arg PLUGIN2VERSION=1.0.0 --build-arg PLUGIN3VERSION=1.0.0 --build-arg PLUGIN4VERSION=1.0.0 --build-arg PLUGIN5VERSION=1.0.0 --build-arg PLUGIN6VERSION=1.0.0` -- takes 15+ minutes. NEVER CANCEL. Set timeout to 60+ minutes.
4. Run with MariaDB and RabbitMQ for testing

### Testing Infrastructure 
- Database: MariaDB 10.8 (`docker run --name mariadbtest -e MYSQL_ROOT_PASSWORD=secretpassword -p 3306:3306 -d mariadb:10.8`)
- Message Queue: RabbitMQ (`docker run --name some-rabbit -e RABBITMQ_DEFAULT_USER=admin -e RABBITMQ_DEFAULT_PASS=password rabbitmq:latest`)
- Frontend testing: Multiple Cypress and WebDriver IO configurations (a,b,c,d,e,f,g,h,i,j test suites)
- E2E tests: Each test suite takes 5-15 minutes. NEVER CANCEL. Set timeout to 30+ minutes.

## Validation
- Always test compilation after making C# code changes: `dotnet build BackendConfiguration.Pn.sln`
- Run unit tests for basic validation: `dotnet test BackendConfiguration.Pn.Test/BackendConfiguration.Pn.Test.csproj`
- ONLY run integration tests if you have database infrastructure set up (takes 10+ minutes)
- For frontend changes, you need the full multi-repository setup for proper testing

## Common Tasks

### Codebase Structure
```
eFormAPI/Plugins/BackendConfiguration.Pn/
├── BackendConfiguration.Pn/                 # Main plugin code
├── BackendConfiguration.Pn.Test/           # Unit tests (fast, ~7 seconds)
├── BackendConfiguration.Pn.Integration.Test/ # Integration tests (slow, 10+ minutes)
└── BackendConfiguration.Pn.sln             # Solution file

eform-client/                                # Angular frontend components
├── src/app/plugins/modules/backend-configuration-pn/  # Angular module
├── e2e/                                     # E2E test files
├── cypress/                                 # Cypress test configurations  
└── wdio-*.conf.ts                          # WebDriver IO configurations
```

### Key Files
- **Backend Plugin**: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/BackendConfiguration.Pn.csproj` (.NET 9.0)
- **Frontend Module**: `eform-client/src/app/plugins/modules/backend-configuration-pn/`
- **Installation Scripts**: `devinstall.sh`, `devinstall.py`, `testinginstallpn.sh`
- **Docker**: `Dockerfile` (requires multi-repo setup)
- **CI/CD**: `.github/workflows/dotnet-core-master.yml` and `dotnet-core-pr.yml`

### Development Recommendations
- Work on .NET backend changes in isolation using the BackendConfiguration.Pn solution
- For frontend changes or full integration testing, use the multi-repository Docker approach
- Integration tests require extensive infrastructure (MariaDB, RabbitMQ) and are very time-consuming
- Use unit tests for rapid feedback during development
- The CI system runs extensive browser automation tests that take 5-15 minutes per test suite

### Timing Expectations
- .NET Build: ~47 seconds first build, ~3 seconds subsequent builds
- Unit Tests: ~2 seconds  
- Integration Tests: 10+ minutes (requires database)
- Docker Build: 15+ minutes
- E2E Test Suites: 5-15 minutes each (10 suites total)
- **CRITICAL**: NEVER CANCEL long-running builds or tests. Always use appropriate timeouts.

### Cannot Do Standalone
- Cannot run the application standalone - requires eform-angular-frontend main app
- Cannot run meaningful frontend tests without full ecosystem
- Cannot use Docker build without all dependent repositories
- Integration tests require complex database schema and multi-plugin setup

### Plugin Integration
This plugin integrates into the main eForm application by:
- Backend: Copying plugin DLL to `eFormAPI.Web/Plugins/BackendConfiguration.Pn/`
- Frontend: Copying Angular module from `eform-client/src/app/plugins/modules/backend-configuration-pn/` to main application
- Routes: `testinginstallpn.sh` injects routing configuration using perl commands to modify `plugins.routing.ts`
- Tests: E2E tests and page objects copied to main application test structure for CI execution

### Angular Frontend (Component Files Only)
- This repository contains Angular TypeScript components, services, and models
- No package.json - frontend components are source files only
- Structure: modules, components, services, state management, i18n, models
- Integration: Copied to main eform-angular-frontend application for building