# MaisBerlim Bolão da Copa

Bolão comunitário reutilizável para os jogos do Brasil. A SPA React roda em S3/CloudFront; a API e o job de retenção usam Lambda .NET 10, API Gateway, DynamoDB, Cognito e EventBridge Scheduler.

Os termos, fluxos, pontuação e desempates estão em [docs/domain-and-flow.md](docs/domain-and-flow.md).

## Pré-requisitos

- .NET SDK 10
- Node.js 24 ou posterior e npm 11
- Terraform 1.10 ou posterior
- AWS CLI v2 para operações manuais
- Credenciais AWS somente para quem executar Terraform localmente

## Verificação local

```bash
dotnet test backend/Bolao.slnx
npm --prefix frontend install
npm --prefix frontend run test:run
npm --prefix frontend run build
npm --prefix frontend run test:e2e
terraform fmt -check -recursive infra
terraform -chdir=infra init
terraform -chdir=infra validate
terraform -chdir=infra plan
```

`test:e2e` inicia API e frontend locais. A API usa repositórios em memória, autenticação falsa e fixtures determinísticos somente com `ASPNETCORE_ENVIRONMENT=E2E`; esse modo recusa iniciar quando `AWS_EXECUTION_ENV` existe.

Para desenvolvimento manual sem AWS:

```bash
ASPNETCORE_ENVIRONMENT=E2E ASPNETCORE_URLS=http://127.0.0.1:5080 \
  dotnet run --project backend/src/Bolao.Functions --no-launch-profile

VITE_E2E=true VITE_API_BASE_URL=http://127.0.0.1:5080 \
  npm --prefix frontend run dev -- --host 127.0.0.1
```

## Terraform

O root fica em `infra/`. O estado remoto já existente usa:

- bucket `andre-lopes-iac`;
- chave `bolaomaisberlim.tfstate`;
- região `eu-central-1`;
- criptografia e lock nativo do backend S3.

Inicialize e revise o plano antes de aplicar:

```bash
terraform -chdir=infra init
terraform -chdir=infra plan
```

O Terraform não gerencia o provider nem as roles GitHub OIDC. A primeira criação dos recursos pode ser iniciada manualmente no workflow `Infrastructure`, após configurar a role externa, ou executada localmente pelo dono com credenciais autorizadas. Revise sempre o plan; não armazene `terraform.tfvars`, plans ou state no repositório.

## GitHub Actions

Crie o ambiente protegido `production`, com aprovação obrigatória para deploy. Configure estas GitHub Environment/Repository Variables:

| Variável | Uso |
| --- | --- |
| `LAMBDA_FUNCTION_NAMES` | exatamente os nomes das Lambdas sobreviventes de API e retenção, separados por espaço, por exemplo `bolaomaisberlim-dev-api bolaomaisberlim-dev-retention` |
| `VITE_API_BASE_URL` | output `api_url` |
| `VITE_COGNITO_USER_POOL_ID` | output `cognito_user_pool_id` |
| `VITE_COGNITO_CLIENT_ID` | output `cognito_user_pool_client_id` |
| `VITE_COGNITO_DOMAIN` | output `cognito_domain` |
| `COGNITO_DOMAIN_PREFIX` | prefixo globalmente único, por exemplo `bolaomaisberlim-prod` |
| `COGNITO_CALLBACK_URLS` | array JSON com URLs de retorno, incluindo `/` final |
| `COGNITO_LOGOUT_URLS` | array JSON com URLs pós-logout, incluindo `/` final |
| `GOOGLE_CLIENT_ID` | client ID OAuth Web do Google |
| `ADMIN_EMAILS` | array JSON de e-mails Google administradores |
| `CLOUDFRONT_DISTRIBUTION_ID` | output `cloudfront_distribution_id` |
| `SES_IDENTITY_ARN` | opcional; identidade SES para notificar o vencedor |
| `SES_FROM_EMAIL` | opcional; remetente da notificação do vencedor |

Configure os GitHub Secrets protegidos:

- `AWS_ROLE_ARN`: role OIDC assumida pelos workflows; ela deve ter as permissões do workflow executado;
- `FRONTEND_BUCKET_NAME`: output `frontend_bucket_name`;
- `GOOGLE_CLIENT_SECRET`: segredo do client OAuth Web, exposto apenas como `TF_VAR_google_client_secret`.

Antes ou junto do primeiro deploy desta remoção, atualize `LAMBDA_FUNCTION_NAMES` no ambiente `production`. O workflow Backend falha se a lista não tiver exatamente uma função terminada em `-api` e uma terminada em `-retention`. Obtenha os nomes aplicados com:

```bash
terraform -chdir=infra output -json lambda_function_names |
  jq -r '[.api, .retention] | join(" ")'
```

O plan é armazenado como artifact privado e não é publicado em comentários ou logs. O segredo OAuth fica no state porque faz parte da configuração do provedor Google no Cognito; mantenha o backend do Terraform restrito. `ADMIN_EMAILS` não é secreto e controla claims, não usuários Cognito persistentes.

As trust policies das roles externas devem restringir `sub` ao repositório/branch ou ao ambiente `production`. As permissões mínimas são:

- infraestrutura: backend S3 e recursos administrados em `infra/`. Durante o primeiro apply desta remoção, inclua também `scheduler:ListSchedules` no grupo de schedules retido e `scheduler:DeleteSchedule` nos schedules `match-*` desse grupo, pois o workflow remove schedules antigos depois do apply;
- backend: `lambda:UpdateFunctionCode`, `lambda:GetFunction` somente nas funções de API e retenção;
- frontend: escrita/remoção somente no bucket da UI e invalidação somente da distribuição correspondente.

Quando a política permitir escopo por recurso, restrinja Scheduler aos ARNs `schedule-group/<grupo>` e `schedule/<grupo>/match-*`. Se `scheduler:ListSchedules` exigir `Resource: "*"` na política usada, mantenha apenas essa ação global e preserve `scheduler:DeleteSchedule` no padrão `match-*` do grupo.

## Administração

O Terraform normaliza `ADMIN_EMAILS`. No login, uma Lambda pré-token adiciona o grupo `admins` somente quando o Google informa o e-mail como verificado e ele está na lista. Remover um endereço da variável remove o acesso administrativo no próximo token emitido.

## Login com Google

No Google Cloud Console:

1. crie ou selecione um projeto e configure a tela de consentimento OAuth externa;
2. crie credenciais OAuth 2.0 do tipo **Aplicativo da Web**;
3. adicione `https://COGNITO_DOMAIN_PREFIX.auth.eu-central-1.amazoncognito.com/oauth2/idpresponse` aos URIs de redirecionamento autorizados;
4. configure `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET` no ambiente `production` do GitHub;
5. depois do apply, copie os outputs Cognito para as variáveis `VITE_COGNITO_*` e execute o workflow Frontend.

Use somente os escopos `openid`, `email` e `profile`. Login local, senha e código por e-mail não são expostos pelo app client.

A área de apuração usa `/admin?matchId=MATCH_ID`. O cadastro/ajuste de jogo também está disponível pela API administrativa e recebe `id`, `kickoff`, códigos FIFA e, após a entrega, `prizeHandedOverAt`.

Consulte [`docs/manual-match-management.md`](docs/manual-match-management.md) para criar jogos, registrar e confirmar resultados e finalizar o jogo atual.

O admin registra gols em ordem, cartões e eventual vencedor nos pênaltis, consulta o ranking provisório e confirma. A confirmação grava `ConfirmedBySub`, `ConfirmedAt`, snapshot e `ResultVersion`; repetir a mesma versão não duplica pontos.

## Operação

- Jogos e resultados são gerenciados manualmente em `/admin`; não há importação, consulta externa nem mudança automática de status.
- Use `PUT /admin/matches/{id}/result` para salvar o rascunho, `POST /admin/matches/{id}/confirm` para publicá-lo e `POST /admin/matches/{id}/finish` para fechar o jogo e ativar o próximo.
- Depois da entrega do prêmio, grave `prizeHandedOverAt`. O job diário anonimiza PII e solicita exclusão da conta Cognito 90 dias após a data mais recente aplicável ao participante, preservando agregados.
- Logs não devem conter nomes, e-mails, tokens nem palpites completos.

## SES e domínio

O Cognito usa a configuração padrão e o login Google não envia e-mail. Enquanto `ses_identity_arn`/`ses_from_email` não forem configurados, somente a notificação customizada do vencedor fica desabilitada; ranking e confirmação continuam funcionando.

Para produção pública:

1. obtenha acesso de produção no SES;
2. verifique domínio/remetente, mesmo com DNS fora do Route 53;
3. forneça `ses_identity_arn` e `ses_from_email` ao Terraform;
4. aplique a mudança e teste com destinatários controlados;
5. confirme SPF/DKIM e monitore bounces.

Falhas normais do SES liberam o claim para retry manual e permanecem visíveis ao admin. Um crash entre o claim e o envio não permite garantia estrita de exactly-once do provedor.

## Rollback

- Backend: selecione no `workflow_dispatch` um ref conhecido ou reverta o commit e execute `Backend`; o mesmo ZIP é enviado às Lambdas de API e retenção e o checksum é validado.
- Frontend: execute `Frontend` a partir de um ref conhecido ou reverta; o workflow sincroniza o build e invalida CloudFront.
- Infraestrutura: reverta a alteração Terraform por PR, revise o novo plan e aplique pelo ambiente protegido. Não restaure state manualmente como procedimento normal.
- Resultado incorreto: não altere standings diretamente. Corrija pelo fluxo administrativo antes da confirmação; após publicação, trate a correção como operação assistida e audite o snapshot/versionamento.

## Ações ainda dependentes do dono

- informar a role OIDC externa em `AWS_ROLE_ARN` e restringir sua trust policy;
- criar as credenciais OAuth Web do Google e fornecer o client ID/secret;
- executar/revisar o primeiro plan/apply;
- obter SES production access e os registros DNS antes de ativar e-mail próprio.
