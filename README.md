# MaisBerlim Bolão da Copa

Bolão comunitário reutilizável para os jogos do Brasil. A SPA React roda em S3/CloudFront; a API e os jobs usam Lambda .NET 10, API Gateway, DynamoDB, Cognito, EventBridge Scheduler e API-Football.

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

A chave da API-Football é uma variável sensível e deve ser fornecida somente por ambiente:

```bash
export TF_VAR_api_football_key='...'
terraform -chdir=infra init
terraform -chdir=infra plan
```

O Terraform não gerencia o provider nem as roles GitHub OIDC. A primeira criação dos recursos pode ser iniciada manualmente no workflow `Infrastructure`, após configurar a role externa, ou executada localmente pelo dono com credenciais autorizadas. Revise sempre o plan; não armazene `terraform.tfvars`, plans ou state no repositório.

## GitHub Actions

Crie o ambiente protegido `production`, com aprovação obrigatória para deploy. Configure estas GitHub Environment/Repository Variables:

| Variável | Uso |
| --- | --- |
| `AWS_INFRA_ROLE_ARN` | role OIDC externa para plan/apply do Terraform |
| `AWS_BACKEND_ROLE_ARN` | role OIDC externa para atualizar somente código das Lambdas |
| `AWS_FRONTEND_ROLE_ARN` | role OIDC externa para S3 e invalidação CloudFront |
| `LAMBDA_FUNCTION_NAMES` | nomes das quatro Lambdas separados por espaço |
| `VITE_API_BASE_URL` | output `api_url` |
| `VITE_COGNITO_USER_POOL_ID` | output `cognito_user_pool_id` |
| `VITE_COGNITO_CLIENT_ID` | output `cognito_user_pool_client_id` |
| `FRONTEND_BUCKET_NAME` | output `frontend_bucket_name` |
| `CLOUDFRONT_DISTRIBUTION_ID` | output `cloudfront_distribution_id` |
| `SES_IDENTITY_ARN` | opcional; identidade SES verificada |
| `SES_FROM_EMAIL` | opcional; remetente correspondente |

Configure o GitHub Secret protegido `API_FOOTBALL_KEY`. O workflow de infraestrutura o expõe apenas ao Terraform como `TF_VAR_api_football_key`; o plan é armazenado como artifact privado e não é publicado em comentários ou logs.

As trust policies das roles externas devem restringir `sub` ao repositório/branch ou ao ambiente `production`. As permissões mínimas são:

- infraestrutura: backend S3 e recursos administrados em `infra/`;
- backend: `lambda:UpdateFunctionCode`, `lambda:GetFunction` nas quatro funções;
- frontend: escrita/remoção somente no bucket da UI e invalidação somente da distribuição correspondente.

## Administração

Adicione um usuário verificado ao grupo:

```bash
aws cognito-idp admin-add-user-to-group \
  --user-pool-id USER_POOL_ID \
  --username USERNAME \
  --group-name admins
```

A área de apuração usa `/admin?matchId=MATCH_ID`. O cadastro/ajuste de jogo também está disponível pela API administrativa e recebe `id`, `providerFixtureId`, `kickoff`, códigos FIFA e, após a entrega, `prizeHandedOverAt`.

O admin revisa o resultado bruto, resolve jogadores, corrige valores, consulta o ranking provisório e confirma. A confirmação grava `ConfirmedBySub`, `ConfirmedAt`, snapshot e `ResultVersion`; repetir a mesma versão não duplica pontos.

## Operação

- `ApiUsage` limita o consumo interno a 80 das 100 chamadas diárias da API-Football. Consulte a tabela indicada por `API_USAGE_TABLE_NAME` e o item `Provider=api-football` para diagnóstico.
- O polling roda a cada 10 minutos e para em resultado final, adiamento/suspensão, quota ou quatro horas após o início.
- Se a API externa falhar ou a quota terminar, use `PUT /admin/matches/{id}/result` e confirme manualmente.
- Depois da entrega do prêmio, grave `prizeHandedOverAt`. O job diário anonimiza PII e solicita exclusão da conta Cognito 90 dias após a data mais recente aplicável ao participante, preservando agregados.
- Logs não devem conter nomes, e-mails, tokens, palpites completos nem a chave da API.

## SES e domínio

Enquanto `ses_identity_arn`/`ses_from_email` não forem configurados, Cognito usa seu remetente padrão e a notificação customizada do vencedor fica desabilitada; ranking e confirmação continuam funcionando.

Para produção pública:

1. obtenha acesso de produção no SES;
2. verifique domínio/remetente, mesmo com DNS fora do Route 53;
3. forneça `ses_identity_arn` e `ses_from_email` ao Terraform;
4. aplique a mudança e teste com destinatários Cognito controlados;
5. confirme SPF/DKIM e monitore bounces.

Falhas normais do SES liberam o claim para retry manual e permanecem visíveis ao admin. Um crash entre o claim e o envio não permite garantia estrita de exactly-once do provedor.

## Rollback

- Backend: selecione no `workflow_dispatch` um ref conhecido ou reverta o commit e execute `Backend`; o mesmo ZIP é enviado a todas as Lambdas e o checksum é validado.
- Frontend: execute `Frontend` a partir de um ref conhecido ou reverta; o workflow sincroniza o build e invalida CloudFront.
- Infraestrutura: reverta a alteração Terraform por PR, revise o novo plan e aplique pelo ambiente protegido. Não restaure state manualmente como procedimento normal.
- Resultado incorreto: não altere standings diretamente. Corrija pelo fluxo administrativo antes da confirmação; após publicação, trate a correção como operação assistida e audite o snapshot/versionamento.

## Ações ainda dependentes do dono

- informar as três roles OIDC externas e suas trust policies;
- fornecer `API_FOOTBALL_KEY`;
- executar/revisar o primeiro plan/apply;
- criar usuários destinatários de teste no Cognito e um admin;
- obter SES production access e os registros DNS antes de ativar e-mail próprio.
