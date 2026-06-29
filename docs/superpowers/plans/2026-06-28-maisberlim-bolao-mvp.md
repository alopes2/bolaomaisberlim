# MaisBerlim Bolão da Copa Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar um bolão público mobile-first para os jogos do Brasil, com autenticação por e-mail, um palpite por pessoa, apuração assistida pela API-Football e ranking confirmado pela administração.

**Architecture:** Uma SPA React/Vite é distribuída por S3/CloudFront e chama uma HTTP API protegida por Cognito. Lambdas C#/.NET 10 persistem dados no DynamoDB, executam a pontuação e consultam a API-Football sob controle de quota; EventBridge Scheduler dispara a sincronização. Terraform gerencia a infraestrutura, enquanto GitHub Actions publica separadamente o pacote das Lambdas e o build da UI. Resultados importados permanecem provisórios até confirmação administrativa.

**Tech Stack:** React, TypeScript, Vite, Tailwind CSS, shadcn/ui (`radix-nova`), React Router, TanStack Query, React Hook Form, Zod, Vitest, Testing Library, Playwright, C#/.NET 10, ASP.NET Core Minimal API on Lambda, xUnit, FluentAssertions, AWS SDK for .NET, Terraform, GitHub Actions com AWS OIDC, API Gateway, Lambda, DynamoDB, Cognito Essentials, EventBridge Scheduler, S3 e CloudFront.

**Source spec:** `docs/superpowers/specs/2026-06-28-maisberlim-bolao-mvp-design.md`

**Git policy:** Não executar `git commit`; o proprietário do repositório fará os commits. Ao final de cada tarefa, relatar os arquivos alterados e aguardar o checkpoint solicitado.

---

## File map

### Root

- `assets/teams.json`: fonte canônica dos elencos.
- `docs/domain-and-flow.md`: glossário curto dos termos e fluxos principais, mantido junto com a implementação.
- `README.md`: pré-requisitos, comandos locais, implantação e operação.
- `.gitignore`: saídas de .NET, Node, Terraform e arquivos locais de ambiente.

### Backend

- `backend/Bolao.slnx`: solução .NET 10.
- `backend/src/Bolao.Functions/Bolao.Functions.csproj`: assembly único das Lambdas HTTP e agendadas.
- `backend/src/Bolao.Functions/Program.cs`: composição da Minimal API e dependências.
- `backend/src/Bolao.Functions/Domain/*.cs`: modelos, prazo, pontuação e ranking sem dependências AWS.
- `backend/src/Bolao.Functions/Rosters/*.cs`: leitura e normalização de `teams.json`.
- `backend/src/Bolao.Functions/Persistence/*.cs`: contratos e implementações DynamoDB.
- `backend/src/Bolao.Functions/Auth/*.cs`: leitura de claims e autorização administrativa.
- `backend/src/Bolao.Functions/Api/*.cs`: contratos HTTP e mapeamento de rotas.
- `backend/src/Bolao.Functions/FootballApi/*.cs`: cliente externo, quota e mapeamento.
- `backend/src/Bolao.Functions/Jobs/*.cs`: sincronização diária e polling de partida.
- `backend/src/Bolao.Functions/Admin/*.cs`: correção, confirmação e publicação.
- `backend/src/Bolao.Functions/Notifications/*.cs`: notificação idempotente do vencedor.
- `backend/tests/Bolao.Functions.Tests/*`: testes unitários e de aplicação.

### Infrastructure

- `infra/backend.tf`: backend S3 remoto fornecido pelo proprietário.
- `infra/providers.tf`: versões do Terraform e providers AWS/archive.
- `infra/variables.tf`: entradas do ambiente; valores sensíveis são fornecidos externamente e não versionados.
- `infra/outputs.tf`: nomes e IDs consumidos na configuração dos workflows.
- `infra/dynamodb.tf`, `infra/cognito.tf`, `infra/lambda.tf`, `infra/api-gateway.tf`, `infra/scheduler.tf`, `infra/frontend.tf`, `infra/github-oidc.tf`: recursos AWS agrupados por responsabilidade no root module.
- `infra/lambda-bootstrap/placeholder.txt`: pacote inicial mínimo, substituído pelo primeiro deploy do backend.
- `infra/terraform.tfvars.example`: valores não secretos de referência; `terraform.tfvars` local não é versionado.

### Continuous delivery

- `.github/workflows/infra.yml`: formatação, validação, plano e aplicação de Terraform.
- `.github/workflows/backend.yml`: testes, pacote .NET e publicação do mesmo artefato em todas as Lambdas.
- `.github/workflows/frontend.yml`: testes, build, sincronização do S3 e invalidação do CloudFront.

### Frontend

- `frontend/src/app/*`: providers, router e shell.
- `frontend/src/auth/*`: Cognito OTP, sessão e perfil inicial.
- `frontend/src/api/*`: cliente HTTP tipado e contratos.
- `frontend/src/features/match/*`: jogo atual, prazo e formulário.
- `frontend/src/features/players/*`: combobox pesquisável.
- `frontend/src/features/leaderboard/*`: ranking, vencedor e histórico.
- `frontend/src/features/admin/*`: resultado provisório, correção e confirmação.
- `frontend/src/features/legal/*`: regulamento e privacidade.
- `frontend/src/test/*`: utilitários de teste.
- `frontend/e2e/*`: fluxo crítico no navegador.

## Task 1: Scaffold .NET, React and verification commands

**Files:**

- Create: `.gitignore`
- Create: `README.md`
- Create: `backend/Bolao.slnx`
- Create: `backend/src/Bolao.Functions/Bolao.Functions.csproj`
- Create: `backend/src/Bolao.Functions/Program.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj`
- Create: `frontend/package.json`
- Create: `frontend/vite.config.ts`
- Create: `frontend/src/main.tsx`

- [ ] **Step 1: Create the .NET 10 solution and test project**

Run:

```bash
mkdir -p backend/src backend/tests
dotnet new sln -n Bolao -o backend --format slnx
dotnet new web -n Bolao.Functions -o backend/src/Bolao.Functions -f net10.0
dotnet new xunit -n Bolao.Functions.Tests -o backend/tests/Bolao.Functions.Tests -f net10.0
dotnet sln backend/Bolao.slnx add backend/src/Bolao.Functions/Bolao.Functions.csproj backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj
dotnet add backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj reference backend/src/Bolao.Functions/Bolao.Functions.csproj
```

Expected: solution lists both projects and targets `net10.0`.

- [ ] **Step 2: Add backend dependencies**

Run:

```bash
dotnet add backend/src/Bolao.Functions package Amazon.Lambda.AspNetCoreServer
dotnet add backend/src/Bolao.Functions package AWSSDK.DynamoDBv2
dotnet add backend/src/Bolao.Functions package AWSSDK.Scheduler
dotnet add backend/tests/Bolao.Functions.Tests package FluentAssertions
dotnet add backend/tests/Bolao.Functions.Tests package NSubstitute
```

Expected: restore succeeds without warnings about incompatible target frameworks.

- [ ] **Step 3: Scaffold the React application and dependencies**

Run:

```bash
npm create vite@latest frontend -- --template react-ts
npm --prefix frontend install
npm --prefix frontend install react-router-dom @tanstack/react-query react-hook-form zod @hookform/resolvers aws-amplify
npm --prefix frontend install --save-dev vitest jsdom @testing-library/react @testing-library/user-event @testing-library/jest-dom playwright
```

Expected: `npm --prefix frontend run build` succeeds.

- [ ] **Step 4: Initialize shadcn/ui**

Run from `frontend/`:

```bash
npx shadcn@latest init --preset nova --base radix
npx shadcn@latest info --json
```

Expected: `components.json` reports Vite, Tailwind and the `radix-nova` preset. Do not add the full registry; each later task adds only its required components after checking `info` and reading `npx shadcn@latest docs <component>`.

- [ ] **Step 5: Configure baseline verification scripts**

Set `frontend/package.json` scripts to:

```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "test": "vitest",
    "test:run": "vitest run --passWithNoTests",
    "test:e2e": "playwright test",
    "lint": "eslint ."
  }
}
```

Add `.gitignore` entries for `bin/`, `obj/`, `node_modules/`, `dist/`, `.aws-sam/`, `.env*`, `test-results/` and `playwright-report/`.

- [ ] **Step 6: Verify the empty foundations**

Run:

```bash
dotnet test backend/Bolao.slnx
npm --prefix frontend run test:run
npm --prefix frontend run build
```

Expected: both test commands pass and the frontend build completes.

## Task 2: Load teams and expose stable player identities

**Files:**

- Create: `backend/src/Bolao.Functions/Rosters/RosterModels.cs`
- Create: `backend/src/Bolao.Functions/Rosters/IRosterCatalog.cs`
- Create: `backend/src/Bolao.Functions/Rosters/JsonRosterCatalog.cs`
- Modify: `backend/src/Bolao.Functions/Bolao.Functions.csproj`
- Test: `backend/tests/Bolao.Functions.Tests/Rosters/JsonRosterCatalogTests.cs`

- [ ] **Step 1: Write the failing roster test**

```csharp
[Fact]
public async Task LoadsBrazilAndBuildsStablePlayerKeys()
{
    var catalog = new JsonRosterCatalog("assets/teams.json");
    var brazil = await catalog.GetTeamAsync("BRA", CancellationToken.None);

    brazil.FifaCode.Should().Be("BRA");
    brazil.Players.Should().OnlyContain(p => p.Key.StartsWith("BRA:"));
    brazil.Players.Select(p => p.Key).Should().OnlyHaveUniqueItems();
}
```

- [ ] **Step 2: Run the test and confirm failure**

Run: `dotnet test backend/Bolao.slnx --filter LoadsBrazilAndBuildsStablePlayerKeys`

Expected: FAIL because `JsonRosterCatalog` does not exist.

- [ ] **Step 3: Implement the minimal catalog**

Define:

```csharp
public record Player(string Key, int Number, string Position, string Name);
public record TeamRoster(string FifaCode, string Name, string FlagIcon, IReadOnlyList<Player> Players);

public interface IRosterCatalog
{
    Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken);
}
```

Generate the player key as `$"{fifaCode}:{number}"`. Configure the project to copy the root JSON:

```xml
<ItemGroup>
  <Content Include="../../../assets/teams.json" Link="assets/teams.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 4: Test valid and unknown teams**

Add a test asserting an unknown FIFA code throws `KeyNotFoundException`; run `dotnet test backend/Bolao.slnx` and expect PASS.

## Task 3: Implement scoring and ranking as pure domain code

**Files:**

- Create: `backend/src/Bolao.Functions/Domain/Prediction.cs`
- Create: `backend/src/Bolao.Functions/Domain/MatchResult.cs`
- Create: `backend/src/Bolao.Functions/Domain/ScoreCalculator.cs`
- Create: `backend/src/Bolao.Functions/Domain/RankingComparer.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Domain/ScoreCalculatorTests.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Domain/RankingComparerTests.cs`

- [ ] **Step 1: Write table-driven failing scoring tests**

```csharp
[Theory]
[InlineData(2, 1, 2, 1, 5)]
[InlineData(1, 0, 2, 0, 2)]
[InlineData(1, 1, 2, 0, 0)]
public void ScoresExactOrOutcomeButNeverBoth(
    int predictedHome, int predictedAway, int actualHome, int actualAway, int expected)
{
    ScoreCalculator.ScoreResult(predictedHome, predictedAway, actualHome, actualAway)
        .Should().Be(expected);
}
```

Add tests for first scorer (3), unique top scorer (3), tied top scorer (2), zero-goal match (0), and exact yellow/red counts (1 per team).

- [ ] **Step 2: Confirm the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter FullyQualifiedName~ScoreCalculatorTests`

Expected: FAIL because domain types do not exist.

- [ ] **Step 3: Implement the scoring contract**

Use immutable records:

```csharp
public record PredictionAnswers(
    int HomeGoals, int AwayGoals, string FirstScorerKey,
    string HomeTopScorerKey, string AwayTopScorerKey,
    int HomeYellowCards, int AwayYellowCards,
    int HomeRedCards, int AwayRedCards);

public record ConfirmedResult(
    int HomeGoals, int AwayGoals, string? FirstScorerKey,
    IReadOnlySet<string> HomeTopScorerKeys, IReadOnlySet<string> AwayTopScorerKeys,
    int HomeYellowCards, int AwayYellowCards,
    int HomeRedCards, int AwayRedCards);

public record ScoreBreakdown(
    int Result, int FirstScorer, int HomeTopScorer, int AwayTopScorer,
    int HomeYellowCards, int AwayYellowCards, int HomeRedCards, int AwayRedCards)
{
    public int Total => Result + FirstScorer + HomeTopScorer + AwayTopScorer
        + HomeYellowCards + AwayYellowCards + HomeRedCards + AwayRedCards;
}
```

Return 3 for a selected scorer when the relevant scorer set contains one player, 2 when it contains multiple players, and 0 when empty or unmatched.

- [ ] **Step 4: Implement ranking comparison**

Sort by total points descending, exact-score count descending, first-scorer count descending, then final submission timestamp ascending. Add tests proving all four keys.

- [ ] **Step 5: Run domain tests**

Run: `dotnet test backend/Bolao.slnx`

Expected: PASS and maximum-score test equals 18.

## Task 4: Enforce cutoff, one prediction and edit timestamp

**Files:**

- Create: `backend/src/Bolao.Functions/Domain/Match.cs`
- Create: `backend/src/Bolao.Functions/Domain/PredictionService.cs`
- Create: `backend/src/Bolao.Functions/Persistence/IPredictionRepository.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Domain/PredictionServiceTests.cs`

- [ ] **Step 1: Write failing cutoff tests**

```csharp
[Fact]
public async Task RejectsSubmissionAtCutoff()
{
    var kickoff = new DateTimeOffset(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);
    var service = CreateService(now: kickoff.AddMinutes(-10));

    var act = () => service.SaveAsync("match-1", "user-1", ValidAnswers(), default);

    await act.Should().ThrowAsync<PredictionClosedException>();
}
```

Add tests for one millisecond before cutoff, replacement of the existing prediction, and replacement of `SubmittedAt` after edit.

- [ ] **Step 2: Run and observe failure**

Run: `dotnet test backend/Bolao.slnx --filter FullyQualifiedName~PredictionServiceTests`

Expected: FAIL because the service does not exist.

- [ ] **Step 3: Implement server-authoritative saving**

Inject `TimeProvider`, load the match, compute `kickoff - 10 minutes`, validate nonnegative card/goal counts and valid roster keys, then call:

```csharp
Task UpsertAsync(
    string matchId,
    string participantId,
    PredictionAnswers answers,
    DateTimeOffset submittedAt,
    CancellationToken cancellationToken);
```

The DynamoDB implementation in Task 5 must use `(MatchId, ParticipantId)` as the key so upsert cannot create a second prediction.

- [ ] **Step 4: Run the focused and full tests**

Run: `dotnet test backend/Bolao.slnx`

Expected: PASS.

## Task 5: Add DynamoDB persistence and idempotent result publication

**Files:**

- Create: `backend/src/Bolao.Functions/Persistence/DynamoDbOptions.cs`
- Create: `backend/src/Bolao.Functions/Persistence/DynamoMatchRepository.cs`
- Create: `backend/src/Bolao.Functions/Persistence/DynamoPredictionRepository.cs`
- Create: `backend/src/Bolao.Functions/Persistence/DynamoStandingRepository.cs`
- Create: `backend/src/Bolao.Functions/Persistence/DynamoResultRepository.cs`
- Create: `backend/src/Bolao.Functions/Persistence/InMemoryRepositories.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Persistence/PublicationTests.cs`

- [ ] **Step 1: Write a failing idempotency test**

```csharp
[Fact]
public async Task PublishingSameResultTwiceDoesNotDuplicatePoints()
{
    var app = TestApplication.Create();
    await app.SeedPredictionAsync(pointsExpected: 18);

    await app.PublishConfirmedResultAsync("match-1", "result-v1");
    await app.PublishConfirmedResultAsync("match-1", "result-v1");

    (await app.GetStandingAsync()).TotalPoints.Should().Be(18);
}
```

- [ ] **Step 2: Confirm failure**

Run: `dotnet test backend/Bolao.slnx --filter PublishingSameResultTwiceDoesNotDuplicatePoints`

Expected: FAIL because publication persistence is absent.

- [ ] **Step 3: Implement repository boundaries**

Use separate tables and these keys:

```text
Participants: ParticipantId (PK)
Matches:      MatchId (PK)
Predictions:  MatchId (PK), ParticipantId (SK)
Standings:    ParticipantId (PK)
ApiUsage:     Provider (PK)
```

Store provisional and confirmed result snapshots on the match item. Use a DynamoDB transaction whose condition requires `PublishedResultVersion <> :version`; update all affected standings and then mark the version published. For community-scale batches above DynamoDB's transaction limit, chunk scoring but keep a per-participant `AppliedMatches` set so retries remain idempotent.

- [ ] **Step 4: Verify persistence behavior with in-memory fakes**

Run: `dotnet test backend/Bolao.slnx`

Expected: PASS for upsert uniqueness and repeated publication.

## Task 6: Build the public and participant HTTP API

**Files:**

- Modify: `backend/src/Bolao.Functions/Program.cs`
- Create: `backend/src/Bolao.Functions/Api/Contracts.cs`
- Create: `backend/src/Bolao.Functions/Api/PublicEndpoints.cs`
- Create: `backend/src/Bolao.Functions/Api/ParticipantEndpoints.cs`
- Create: `backend/src/Bolao.Functions/Auth/CurrentUser.cs`
- Create: `docs/domain-and-flow.md`
- Test: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`

- [ ] **Step 1: Write failing endpoint tests**

Test these routes with `WebApplicationFactory`:

```text
GET  /matches/current                 public
GET  /matches/history                 public
GET  /matches/{matchId}/predictions   public only after cutoff
GET  /leaderboard                     public confirmed data only
PUT  /me/profile                      authenticated
GET  /matches/{matchId}/prediction    authenticated owner only
PUT  /matches/{matchId}/prediction    authenticated owner only
```

The key test sends `PUT /matches/match-1/prediction` at cutoff and expects `409` with `{ "code": "prediction_closed" }`.

- [ ] **Step 2: Confirm the route tests fail**

Run: `dotnet test backend/Bolao.slnx --filter FullyQualifiedName~ParticipantEndpointTests`

Expected: FAIL with 404 responses.

- [ ] **Step 3: Map minimal API routes and stable errors**

Return RFC 7807 problem details with stable codes: `unauthenticated`, `profile_required`, `prediction_closed`, `invalid_player`, `match_not_found`. Read `sub` and `cognito:groups` only from validated claims; never accept participant ID from request bodies.

- [ ] **Step 4: Document terms and main flows**

Create `docs/domain-and-flow.md` with a concise glossary for `Participant`, `Match`, `Prediction`, `MatchResult`, `ScoreBreakdown`, `Standing` and `ResultVersion`. Document the participant submission flow and provisional-to-confirmed publication flow without duplicating implementation details.

- [ ] **Step 5: Verify API behavior**

Run: `dotnet test backend/Bolao.slnx`

Expected: PASS; logs captured by tests contain no email, name, token or request body.

## Task 7: Provision AWS foundations with Terraform

**Files:**

- Create: `infra/backend.tf`
- Create: `infra/providers.tf`
- Create: `infra/variables.tf`
- Create: `infra/outputs.tf`
- Create: `infra/dynamodb.tf`
- Create: `infra/cognito.tf`
- Create: `infra/lambda.tf`
- Create: `infra/api-gateway.tf`
- Create: `infra/scheduler.tf`
- Create: `infra/frontend.tf`
- Create: `infra/github-oidc.tf`
- Create: `infra/lambda-bootstrap/placeholder.txt`
- Create: `infra/terraform.tfvars.example`
- Test: Terraform formatting, initialization and validation

- [ ] **Step 1: Configure the supplied S3 backend**

Create `infra/backend.tf` exactly as supplied:

```hcl
terraform {
  backend "s3" {
    bucket       = "andre-lopes-iac"
    key          = "bolaomaisberlim.tfstate"
    encrypt      = true
    use_lockfile = true
    region       = "eu-central-1"
  }
}
```

- [ ] **Step 2: Define parameters, Lambdas and encrypted DynamoDB tables**

Use `aws_lambda_function` with runtime `dotnet10`, API Gateway payload v2, DynamoDB `PAY_PER_REQUEST`, point-in-time recovery and server-side encryption. Accept the API-Football key through the sensitive Terraform variable `api_football_key` and expose it as `FOOTBALL_API_KEY` only to the API, daily-sync and match-polling Lambdas. The GitHub Actions workflow supplies it through the `TF_VAR_api_football_key` environment variable backed by a GitHub secret. Create functions initially from an `archive_file` containing `infra/lambda-bootstrap/placeholder.txt`; the first backend workflow replaces it. Terraform owns Lambda configuration and uses `lifecycle.ignore_changes` only for `filename` and `source_code_hash`, which are owned by that workflow.

- [ ] **Step 3: Configure Cognito passwordless email OTP**

Configure `aws_cognito_user_pool` with the Terraform equivalents of:

```hcl
user_pool_tier            = "ESSENTIALS"
username_attributes       = ["email"]
auto_verified_attributes  = ["email"]

sign_in_policy {
  allowed_first_auth_factors = ["EMAIL_OTP"]
}
```

The public app client uses no secret:

```hcl
generate_secret = false
explicit_auth_flows = [
  "ALLOW_USER_AUTH",
  "ALLOW_REFRESH_TOKEN_AUTH"
]
```

Create the `admins` group. Keep Cognito's default email sender in dev; parameterize SES identity settings for production.

- [ ] **Step 4: Configure API authorization, GitHub OIDC and least privilege**

Public routes require no authorizer; participant and admin routes use the Cognito JWT authorizer. Lambda IAM policies name exact tables, scheduler group and identidade SES autorizada para a notificação do vencedor. `infra/github-oidc.tf` references the existing GitHub OIDC provider ARN and creates separate infrastructure, backend and frontend roles. Restrict trust policies to this repository and the protected branch/environment. Do not grant `dynamodb:*`, `lambda:*`, `ses:*` or `AdministratorAccess`. The first apply uses local AWS credentials because the CI roles do not exist yet.

- [ ] **Step 5: Add S3 and CloudFront resources**

Create a private bucket, Origin Access Control, default root object `index.html`, SPA 403/404 fallback to `/index.html`, HTTPS-only viewer policy and no public S3 access. Output the bucket name, distribution ID, API URL, Cognito IDs and Lambda names for deployment workflows.

- [ ] **Step 6: Validate infrastructure**

Run:

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```

Expected: formatting is clean and the development root module validates without AWS credentials.

## Task 8: Implement Cognito OTP and private profile frontend

**Files:**

- Create: `frontend/src/auth/cognito.ts`
- Create: `frontend/src/auth/AuthProvider.tsx`
- Create: `frontend/src/auth/SignInPage.tsx`
- Create: `frontend/src/auth/ProfilePage.tsx`
- Create: `frontend/src/api/client.ts`
- Test: `frontend/src/auth/SignInPage.test.tsx`

- [ ] **Step 1: Write a failing OTP flow test**

```tsx
it('requests an email code and confirms it', async () => {
  const user = userEvent.setup();
  render(<SignInPage auth={fakeAuth} />);
  await user.type(screen.getByLabelText(/e-mail/i), 'ana@example.com');
  await user.click(screen.getByRole('button', { name: /enviar código/i }));
  expect(fakeAuth.start).toHaveBeenCalledWith('ana@example.com');
  expect(await screen.findByLabelText(/código/i)).toBeVisible();
});
```

- [ ] **Step 2: Confirm failure**

Run: `npm --prefix frontend run test:run -- SignInPage`

Expected: FAIL because the page is absent.

- [ ] **Step 3: Implement Cognito USER_AUTH with EMAIL_OTP**

Before editing UI, run from `frontend/`:

```bash
npx shadcn@latest info --json
npx shadcn@latest docs input-otp field input button card
npx shadcn@latest add input-otp field input button card
```

Wrap Amplify Auth behind:

```ts
export interface AuthClient {
  start(email: string): Promise<void>;
  confirm(code: string): Promise<void>;
  signOut(): Promise<void>;
  accessToken(): Promise<string | null>;
}
```

Configure values through `VITE_COGNITO_USER_POOL_ID`, `VITE_COGNITO_CLIENT_ID` and `VITE_API_BASE_URL`. Never place secrets in Vite variables.

- [ ] **Step 4: Add first-login profile collection**

Require `givenName` and `familyName`; send them to `PUT /me/profile`. Render the public label from first name plus surname initial and display the disambiguation suffix returned by the API.

- [ ] **Step 5: Run frontend tests and build**

Run:

```bash
npm --prefix frontend run test:run
npm --prefix frontend run build
```

Expected: PASS.

## Task 9: Implement current match, cutoff and searchable player fields

**Files:**

- Create: `frontend/src/features/match/CurrentMatchPage.tsx`
- Create: `frontend/src/features/match/PredictionForm.tsx`
- Create: `frontend/src/features/match/useCutoff.ts`
- Create: `frontend/src/features/players/PlayerCombobox.tsx`
- Test: `frontend/src/features/players/PlayerCombobox.test.tsx`
- Test: `frontend/src/features/match/PredictionForm.test.tsx`

- [ ] **Step 1: Write failing combobox accessibility tests**

```tsx
it('filters without accents and only returns roster options', async () => {
  const user = userEvent.setup();
  render(<PlayerCombobox label="Primeiro gol" players={players} value={null} onChange={vi.fn()} />);
  await user.type(screen.getByRole('combobox', { name: /primeiro gol/i }), 'vinicius');
  expect(screen.getByRole('option', { name: /vinícius/i })).toBeVisible();
  expect(screen.queryByText('Cadastrar vinicius')).not.toBeInTheDocument();
});
```

Also test keyboard selection and that top-scorer fields receive only one team's roster.

- [ ] **Step 2: Confirm failure**

Run: `npm --prefix frontend run test:run -- PlayerCombobox PredictionForm`

Expected: FAIL because components are absent.

- [ ] **Step 3: Implement the shadcn/ui player combobox**

Before editing UI, run from `frontend/`:

```bash
npx shadcn@latest info --json
npx shadcn@latest docs combobox
npx shadcn@latest add combobox
```

Normalize search with:

```ts
const normalize = (value: string) =>
  value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase('pt-BR');
```

Compose one reusable `PlayerCombobox` from the installed shadcn component. It emits only a selected `player.key`; typed text is never submitted. Group both teams in the first-scorer field and restrict each top-scorer field to its team.

- [ ] **Step 4: Implement the prediction form**

Use React Hook Form + Zod. Fields are score, first scorer, two top scorers, and yellow/red counts per team. Disable editing when server time is at or past `cutoffAt`, but still rely on backend rejection. Show `submittedAt` returned by the API.

- [ ] **Step 5: Verify tests and mobile build**

Run: `npm --prefix frontend run test:run && npm --prefix frontend run build`

Expected: PASS.

## Task 10: Add leaderboard, winner, visibility and history

**Files:**

- Create: `frontend/src/features/leaderboard/Leaderboard.tsx`
- Create: `frontend/src/features/leaderboard/RoundWinner.tsx`
- Create: `frontend/src/features/leaderboard/MatchHistory.tsx`
- Test: `frontend/src/features/leaderboard/Leaderboard.test.tsx`
- Test: `backend/tests/Bolao.Functions.Tests/Api/PublicVisibilityTests.cs`

- [ ] **Step 1: Write failing visibility and rendering tests**

Backend: before cutoff, `GET /matches/{id}/predictions` returns `403`; at cutoff it returns sanitized public names and answers. Public leaderboard returns confirmed standings even when a provisional result exists.

Frontend:

```tsx
it('highlights first place with an accessible crown label', () => {
  render(<Leaderboard entries={entries} />);
  expect(screen.getByText('Ana S.')).toHaveAttribute('data-rank', '1');
  expect(screen.getByLabelText(/primeiro lugar/i)).toBeVisible();
});
```

- [ ] **Step 2: Run tests and confirm failure**

Run:

```bash
dotnet test backend/Bolao.slnx --filter FullyQualifiedName~PublicVisibilityTests
npm --prefix frontend run test:run -- Leaderboard
```

Expected: FAIL.

- [ ] **Step 3: Implement confirmed-only public queries**

Return position, public name, total points, exact-score count and first-scorer count. Return round winner only when `ResultState == Confirmed`.

- [ ] **Step 4: Implement responsive ranking UI**

Check `npx shadcn@latest info`, read docs, and add only missing `card`, `badge`, `table` and `skeleton` components. Use semantic ordered-list markup. Highlight rank one with a crown icon plus text/ARIA label so color is not the only indicator. Render round winner after confirmation and prior matches in descending kickoff order.

- [ ] **Step 5: Run full tests**

Run: `dotnet test backend/Bolao.slnx && npm --prefix frontend run test:run`

Expected: PASS.

## Task 11: Implement API-Football client and daily quota guard

**Files:**

- Create: `backend/src/Bolao.Functions/FootballApi/IFootballApiClient.cs`
- Create: `backend/src/Bolao.Functions/FootballApi/FootballApiClient.cs`
- Create: `backend/src/Bolao.Functions/FootballApi/FootballApiModels.cs`
- Create: `backend/src/Bolao.Functions/FootballApi/ApiQuotaGuard.cs`
- Test: `backend/tests/Bolao.Functions.Tests/FootballApi/FootballApiClientTests.cs`
- Test: `backend/tests/Bolao.Functions.Tests/FootballApi/ApiQuotaGuardTests.cs`

- [x] **Step 1: Write failing HTTP mapping tests**

Use a fake `HttpMessageHandler` with recorded fixture, events and statistics JSON. Assert mapping of `FT`, `AET`, `PEN`, `PST`, `SUSP`, first scorer, scorers per team, yellow totals and red totals.

- [x] **Step 2: Write the failing quota test**

```csharp
[Fact]
public async Task RejectsAutomaticCallAfterEightyReservations()
{
    var guard = new ApiQuotaGuard(repository, TimeProvider.System, limit: 80);
    await repository.SeedAsync(DateOnly.FromDateTime(DateTime.UtcNow), 80);

    var act = () => guard.ReserveAsync(CancellationToken.None);

    await act.Should().ThrowAsync<ApiQuotaExceededException>();
}
```

- [x] **Step 3: Implement atomic quota reservation**

Use DynamoDB `ADD RequestCount :one` with condition `attribute_not_exists(RequestCount) OR RequestCount < :limit`. After every provider response, persist its request-limit and request-remaining headers. Refuse another automatic or admin call when the provider reports 20 or fewer remaining requests. When the reported remaining value increases, treat that as the provider's quota reset and atomically reset the internal `RequestCount`; this avoids guessing the provider's reset timezone.

- [x] **Step 4: Implement the HTTP client**

Read the key from the `FOOTBALL_API_KEY` environment variable, send it only in the required API-Football header, use a 10-second timeout and reserve quota before each request. Never log the key, headers or response bodies containing unexpected personal data.

- [x] **Step 5: Run backend tests**

Run: `dotnet test backend/Bolao.slnx`

Expected: PASS.

## Task 12: Schedule polling and create provisional results

**Files:**

- Create: `backend/src/Bolao.Functions/Jobs/DailyMatchSyncHandler.cs`
- Create: `backend/src/Bolao.Functions/Jobs/MatchPollingHandler.cs`
- Create: `backend/src/Bolao.Functions/Jobs/MatchScheduleService.cs`
- Modify: `infra/scheduler.tf`
- Test: `backend/tests/Bolao.Functions.Tests/Jobs/MatchPollingHandlerTests.cs`

- [x] **Step 1: Write failing state-machine tests**

Cover:

```text
NS/1H/HT/2H -> persist status and continue
FT/AET/PEN  -> fetch events/statistics, compute provisional result, delete schedule
PST/SUSP    -> persist state, delete schedule, require admin reschedule
kickoff+4h  -> stop without another provider call
quota error -> stop safely and retain manual fallback
```

- [x] **Step 2: Confirm failure**

Run: `dotnet test backend/Bolao.slnx --filter FullyQualifiedName~MatchPollingHandlerTests`

Expected: FAIL.

- [x] **Step 3: Implement schedules**

For each published match, create a named EventBridge schedule using `rate(10 minutes)`, `StartDate = kickoff`, `EndDate = kickoff + 4 hours`, and a payload containing only `matchId`. Delete it immediately after a terminal or postponed state.

- [x] **Step 4: Implement provisional calculation**

Map API scorer names to local roster keys using accent-insensitive normalized full names. When zero or multiple local matches exist, store an unresolved mapping and block confirmation until an administrator selects the correct local player.

- [x] **Step 5: Verify jobs and Terraform configuration**

Run:

```bash
dotnet test backend/Bolao.slnx
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```

Expected: PASS.

## Task 13: Implement administration and confirmed publication

**Files:**

- Create: `backend/src/Bolao.Functions/Api/AdminEndpoints.cs`
- Create: `backend/src/Bolao.Functions/Admin/ResultConfirmationService.cs`
- Create: `backend/src/Bolao.Functions/Notifications/IWinnerNotificationService.cs`
- Create: `backend/src/Bolao.Functions/Notifications/SesWinnerNotificationService.cs`
- Create: `frontend/src/features/admin/AdminMatchPage.tsx`
- Create: `frontend/src/features/admin/ResultEditor.tsx`
- Create: `frontend/src/features/admin/ProvisionalLeaderboard.tsx`
- Test: `backend/tests/Bolao.Functions.Tests/Admin/ResultConfirmationServiceTests.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Notifications/WinnerNotificationServiceTests.cs`
- Test: `frontend/src/features/admin/AdminMatchPage.test.tsx`

- [ ] **Step 1: Write failing authorization tests**

An authenticated user without `admins` gets `403` for every `/admin/*` route. An admin can view provisional ranking while `/leaderboard` still returns the previous confirmed standings.

- [ ] **Step 2: Define and test admin routes**

```text
POST /admin/matches
PUT  /admin/matches/{id}
POST /admin/matches/{id}/sync
GET  /admin/matches/{id}/raw-result
GET  /admin/matches/{id}/provisional-leaderboard
PUT  /admin/matches/{id}/result
POST /admin/matches/{id}/confirm
```

Confirmation must fail when scorer mappings are unresolved or result totals are inconsistent with goal events.

- [ ] **Step 3: Implement confirmation audit**

Persist `ConfirmedBySub`, `ConfirmedAt`, immutable confirmed snapshot and monotonically increasing `ResultVersion`. Invoke the idempotent publication service from Task 5.

- [ ] **Step 4: Prevent automatic duplicate winner notifications**

Resolve the winner's email from Cognito only after the result is confirmed. Send a concise SES message identifying the match and public winner name, without including other participants or predictions. Claim `WinnerNotificationVersion` with a conditional write before calling SES and store `WinnerNotifiedAt` after success. A repeated confirmation of the same `ResultVersion` must not send again. Surface explicit SES failures to the admin for manual retry, noting that delivery providers cannot offer a strict exactly-once guarantee across a process crash.

- [ ] **Step 5: Implement the admin UI**

Check `npx shadcn@latest info`, read docs, and add only missing `alert-dialog`, `badge`, `field`, `input` and `button` components. Show raw provider status, mapped goals, card totals, unresolved players, editable official values, provisional leaderboard and explicit confirmation dialog. Disable confirmation until validation passes.

- [ ] **Step 6: Run backend and frontend tests**

Run: `dotnet test backend/Bolao.slnx && npm --prefix frontend run test:run`

Expected: PASS.

## Task 14: Add rules, privacy copy and retention operation

**Files:**

- Create: `frontend/src/features/legal/RulesPage.tsx`
- Create: `frontend/src/features/legal/PrivacyPage.tsx`
- Create: `backend/src/Bolao.Functions/Jobs/DataRetentionHandler.cs`
- Modify: `infra/lambda.tf`
- Modify: `infra/scheduler.tf`
- Test: `backend/tests/Bolao.Functions.Tests/Jobs/DataRetentionHandlerTests.cs`

- [ ] **Step 1: Write failing retention tests**

Seed a completed competition whose prize handover was 91 days ago and another at 89 days. Assert the handler deletes the first participant PII/account reference and retains the second while preserving anonymized aggregate results.

- [ ] **Step 2: Implement legal pages from the approved rules**

Include points, top-scorer tie reduction, 0-0 behavior, API-Football as reference, admin confirmation authority, 10-minute cutoff, edit timestamp rule, public-name format, prize validation and 90-day deletion window. Do not add unreviewed marketing or legal claims.

- [ ] **Step 3: Implement scheduled retention**

Run daily. Delete or anonymize expired participant records and request Cognito user deletion through an explicitly scoped IAM permission. Record only counts and internal operation IDs in logs.

- [ ] **Step 4: Verify**

Run: `dotnet test backend/Bolao.slnx && npm --prefix frontend run test:run`

Expected: PASS.

## Task 15: Add GitHub Actions deployments and complete operational handoff

**Files:**

- Create: `.github/workflows/infra.yml`
- Create: `.github/workflows/backend.yml`
- Create: `.github/workflows/frontend.yml`
- Create: `frontend/playwright.config.ts`
- Create: `frontend/e2e/prediction-flow.spec.ts`
- Create: `frontend/e2e/admin-result-flow.spec.ts`
- Modify: `README.md`

- [ ] **Step 1: Add a deterministic local test mode**

Configure the backend to use in-memory repositories, fake claims and recorded API-Football fixtures only when `ASPNETCORE_ENVIRONMENT=E2E`. Refuse to start this mode when `AWS_EXECUTION_ENV` is present.

- [ ] **Step 2: Write the participant E2E flow**

Test mobile viewport `390x844`:

```text
sign in -> complete profile -> select searchable players -> submit -> edit
-> observe updated timestamp -> advance server clock -> verify locked read-only form
-> verify other predictions become visible
```

- [ ] **Step 3: Write the admin/result E2E flow**

```text
admin opens provisional ranking -> resolves scorer mapping -> confirms result
-> public page displays round winner -> crown highlights first overall
-> refresh preserves confirmed ranking and does not duplicate points
```

- [ ] **Step 4: Add the Terraform workflow**

On pull requests touching `infra/**`, run `terraform fmt -check`, `init`, `validate` and `plan`, then upload the plan as a private workflow artifact. On pushes to the protected main branch, recreate the reviewed plan and apply it through a protected GitHub environment. Authenticate with `aws-actions/configure-aws-credentials` and GitHub OIDC; set only `id-token: write` and `contents: read`. Serialize applies with a concurrency group. Plan output must not be posted to public logs or pull-request comments because it can contain sensitive values.

- [ ] **Step 5: Add the backend deployment workflow**

On pushes to main touching `backend/**`, and on manual dispatch for the first deployment, run Release tests and `dotnet publish`, create one ZIP once, assume the dedicated backend deploy role through OIDC, and call `aws lambda update-function-code` for every configured Lambda name. Every function must receive the exact same ZIP checksum. Wait for each update to complete and fail the workflow if any function fails. Do not run Terraform or mutate Lambda configuration in this workflow.

- [ ] **Step 6: Add the frontend deployment workflow**

On pushes to main touching `frontend/**`, and on manual dispatch for the first deployment, run tests and build with non-secret API/Cognito values supplied as GitHub environment variables. Assume the dedicated frontend role through OIDC, run `aws s3 sync frontend/dist/ s3://$BUCKET --delete`, then create and wait for a CloudFront invalidation of `/*`. Grant this role access only to the UI bucket and its distribution.

- [ ] **Step 7: Document deployment and operations**

README must contain exact prerequisites and commands:

```bash
dotnet test backend/Bolao.slnx
npm --prefix frontend run test:run
npm --prefix frontend run build
terraform fmt -check -recursive infra
terraform -chdir=infra init
terraform -chdir=infra validate
terraform -chdir=infra plan
```

Also document the existing state bucket, one-time local apply for OIDC roles, protected GitHub environments, required repository/environment variables, adding an admin to the Cognito group, supplying `TF_VAR_api_football_key` from a protected GitHub secret, checking `ApiUsage`, manual result fallback, SES/domain migration and workflow rollback procedures.

- [ ] **Step 8: Run the complete verification suite**

Run:

```bash
dotnet test backend/Bolao.slnx --configuration Release
npm --prefix frontend run test:run
npm --prefix frontend run build
npm --prefix frontend run test:e2e
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```

Expected: every command exits 0; no browser console errors; no secrets or personal data appear in build artifacts or captured logs.

- [ ] **Step 9: Report the handoff without committing**

Provide changed-file summary, verification outputs, deployment prerequisites still requiring owner action (AWS credentials, API-Football key, Cognito test recipients, SES production access and DNS), and stop for the repository owner to commit.
