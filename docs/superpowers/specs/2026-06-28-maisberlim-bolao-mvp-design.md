# MaisBerlim Bolão da Copa — Design do MVP

## Objetivo

Criar um bolão público para a comunidade brasileira em Berlim, começando pelo próximo jogo do Brasil e reutilizável nos jogos seguintes. Cada participante envia um palpite por jogo, acumula pontos e concorre a prêmios.

O MVP deve estar preparado para uso público, limitar abuso por e-mail verificado, fechar palpites antes da partida e publicar resultados somente após confirmação administrativa.

## Fora do escopo

- Criação de múltiplos bolões.
- Aplicativo nativo.
- Construtor genérico de perguntas.
- Dados ao vivo para o público.
- Escalações oficiais ou faltas cometidas.
- Autenticação por telefone ou redes sociais.
- Prova automática de que endereços de e-mail diferentes pertencem à mesma pessoa.

## Participantes e identidade

- O acesso usa autenticação sem senha do Amazon Cognito Essentials, com código de uso único enviado por e-mail.
- O cadastro solicita nome e sobrenome completos.
- O ranking público exibe apenas primeiro nome e inicial do sobrenome, por exemplo `André S.`.
- Em nomes públicos duplicados, o sistema acrescenta um código curto, por exemplo `André S. · 7K`.
- O identificador interno é o `sub` imutável do Cognito.
- O e-mail e o nome completo são privados e acessíveis somente à administração.
- A identidade real do vencedor é validada manualmente antes da entrega do prêmio.

Durante os testes, o Cognito usa seu remetente padrão. Para o lançamento público, o domínio do MaisBerlim será verificado no Amazon SES, a conta sairá do sandbox e o remetente passará a ser próprio. O DNS pode continuar fora do Route 53.

## Experiência pública

A interface será mobile-first e terá:

1. Cabeçalho do MaisBerlim.
2. Card do próximo jogo com seleções, horário em `Europe/Berlin` e contagem regressiva.
3. Login e cadastro por código enviado ao e-mail.
4. Formulário de palpite.
5. Confirmação do palpite e horário da última atualização.
6. Estado somente leitura após o encerramento.
7. Ranking acumulado, com o primeiro colocado em destaque e uma coroa.
8. Histórico dos jogos anteriores.
9. Regulamento, critérios de pontuação, desempate e privacidade.

Os palpites dos demais participantes permanecem ocultos até o encerramento das submissões. Após o prazo, podem ser consultados. Depois da confirmação administrativa do resultado, a página destaca o vencedor da rodada, atualiza o ranking público e envia uma notificação ao e-mail privado do vencedor.

## Formulário de palpite

Cada jogo tem perguntas fixas:

- Placar final.
- Primeiro jogador a marcar na partida.
- Artilheiro de cada seleção na partida.
- Quantidade de cartões amarelos de cada seleção.
- Quantidade de cartões vermelhos de cada seleção.

As opções de jogadores vêm de `assets/teams.json`. Não haverá consulta de elencos ou escalações à API-Football.

Cada seleção de jogador usa um dropdown pesquisável:

- O campo mostra o nome do jogador e permite filtrar digitando parte do nome.
- A busca ignora diferenças entre maiúsculas, minúsculas e acentos.
- O campo de artilheiro mostra somente os jogadores da seleção correspondente.
- O campo de primeiro gol mostra jogadores das duas seleções, agrupados por equipe.
- A digitação apenas filtra opções existentes; não é permitido cadastrar texto livre, garantindo que todo palpite use um jogador de `assets/teams.json`.
- O componente deve funcionar por teclado e expor rótulos adequados a leitores de tela.

Em uma partida sem gols, ninguém recebe pontos por primeiro gol ou artilheiro. Se dois ou mais jogadores empatarem como artilheiros de uma seleção, qualquer palpite em um desses jogadores é válido, com pontuação reduzida conforme as regras abaixo.

## Pontuação e desempate

Pontuação por jogo:

| Item | Pontos |
| --- | ---: |
| Placar exato | 5 |
| Apenas vencedor ou empate correto | 2 |
| Primeiro jogador a marcar | 3 |
| Artilheiro isolado de uma seleção | 3 por seleção |
| Um dos artilheiros empatados de uma seleção | 2 por seleção |
| Quantidade exata de amarelos | 1 por seleção |
| Quantidade exata de vermelhos | 1 por seleção |

Placar exato e resultado correto não são cumulativos. A pontuação máxima em um jogo é 18 pontos.

Ordem de desempate no ranking:

1. Maior quantidade de placares exatos.
2. Maior quantidade de acertos do primeiro jogador a marcar.
3. Menor horário de envio da versão final do palpite.

Cada edição substitui o horário anterior para fins de desempate.

## Prazo de envio

- O envio e a edição encerram 10 minutos antes do horário oficial da partida.
- O horário é armazenado em UTC e exibido em `Europe/Berlin`.
- O backend é a autoridade do prazo; o contador do frontend é apenas informativo.
- Alterações de horário recebidas da API-Football atualizam o prazo enquanto as submissões ainda estão abertas.
- Uma partida já bloqueada não é reaberta automaticamente por mudança posterior de horário.

## Administração

Administradores serão identificados por um grupo `admins` no Cognito. A área administrativa permite:

- Cadastrar e publicar jogos.
- Selecionar as duas equipes a partir de `assets/teams.json`.
- Corrigir o horário da partida.
- Iniciar uma sincronização respeitando o intervalo mínimo e o orçamento diário.
- Consultar dados brutos recebidos da API-Football.
- Consultar o ranking provisório, invisível ao público.
- Corrigir placar, autores dos gols ou cartões quando a API não corresponder aos dados oficiais.
- Confirmar o resultado e publicar a pontuação.

Cada resultado confirmado registra quem confirmou, quando confirmou e o snapshot usado no cálculo.

## Arquitetura

### Frontend

- React SPA, construída com Vite.
- shadcn/ui com Tailwind CSS e preset `radix-nova` para componentes e tokens visuais.
- Um `PlayerCombobox` pesquisável e reutilizável para primeiro gol e artilheiros; placar e cartões usam campos numéricos simples.
- Hospedagem estática em Amazon S3, distribuída por CloudFront.
- Inicialmente acessada pelo domínio padrão do CloudFront.
- Futuramente publicada em `bolao.maisberlim.com` sem mudança da aplicação.

Next.js não será usado: a API é separada, as telas principais são interativas e autenticadas, e o MVP não depende de renderização no servidor ou conteúdo editorial indexável.

### Backend

- Amazon API Gateway para endpoints HTTP.
- AWS Lambda com runtime gerenciado .NET 10 e código C# para regras de negócio e integração externa.
- Amazon DynamoDB para jogos, palpites, resultados, pontuação e controle de quota.
- Amazon Cognito Essentials para autenticação por e-mail OTP.
- Amazon EventBridge Scheduler para sincronização de partidas.
- Amazon SES para remetente próprio no lançamento público.

### Infraestrutura e implantação

- Terraform fica em `infra/` e gerencia os recursos AWS, configurações, permissões e outputs do MVP.
- O estado remoto usa o bucket S3 existente `andre-lopes-iac`, chave `bolaomaisberlim.tfstate`, região `eu-central-1`, criptografia habilitada e lock nativo do backend S3.
- GitHub Actions assume roles AWS por OIDC, sem access keys permanentes armazenadas no GitHub.
- O workflow de infraestrutura executa `terraform fmt`, `validate` e `plan` em pull requests; o `apply` ocorre somente na branch principal após aprovação do ambiente GitHub.
- O workflow de backend compila e testa o .NET 10, gera um único pacote versionado e publica esse mesmo artefato em todas as Lambdas.
- O workflow de frontend testa e compila a SPA, sincroniza `dist/` no bucket privado e invalida o CloudFront.
- Terraform é responsável pela configuração das Lambdas e do hosting; os workflows de aplicação são responsáveis apenas pelo código das Lambdas e pelo conteúdo do bucket da UI.
- Como o bucket de estado, o provider OIDC e as roles de deploy já existem fora deste projeto, este Terraform apenas provisiona a aplicação. Os workflows recebem os ARNs das roles por GitHub Variables.

### Entidades lógicas

- `Participant`: referência ao `sub`, nome público e código de desambiguação.
- `Match`: equipes, horário, prazo, identificador externo e estado.
- `Prediction`: participante, jogo, respostas, versão e horário da última gravação.
- `MatchResult`: dados importados, correções, estado provisório ou confirmado e auditoria da confirmação.
- `Standing`: totais e contadores usados no ranking e desempate.
- `ApiUsage`: chamadas feitas no dia e bloqueio preventivo da quota.

O desenho físico das tabelas deve garantir uma única previsão por par participante/jogo e atualizações condicionais para evitar concorrência no prazo.

## Fluxo de envio

1. O participante autentica com e-mail OTP.
2. O frontend carrega o jogo publicado e as opções de `teams.json` por meio da API.
3. O participante envia ou edita o palpite.
4. A Lambda verifica identidade, estado do jogo e prazo usando o relógio do servidor.
5. O DynamoDB cria ou substitui a única previsão do participante para aquele jogo.
6. A resposta retorna a versão salva e o horário usado no desempate.

## Sincronização e quota da API-Football

A conta gratuita oferece 100 chamadas por dia. Somente o backend acessa a API-Football; navegadores recebem dados persistidos no DynamoDB.

- Uma sincronização diária atualiza próximos jogos e horários.
- No horário da partida, o EventBridge inicia consultas a cada 10 minutos.
- O ciclo encerra imediatamente nos estados finais `FT`, `AET` ou `PEN`.
- O ciclo tem limite absoluto de quatro horas após o início.
- Partidas adiadas ou suspensas interrompem o ciclo e exigem reagendamento administrativo.
- Cada consulta por ID usa o endpoint de fixture, que já inclui placar, eventos e estatísticas.
- Um contador central bloqueia chamadas automáticas e administrativas ao atingir 80 no dia, reservando 20 chamadas para recuperação e diagnóstico.
- Se o limite interno ou a reserva bloquear novas chamadas, uma única sonda é permitida após 24 horas; o aumento de `x-ratelimit-requests-remaining` confirma o reset do provedor e reinicia o contador interno.
- O botão administrativo de sincronização possui intervalo mínimo.

O consumo máximo esperado para uma partida é de aproximadamente 25 chamadas, já incluindo eventos e estatísticas. Os dados de elencos e escalações nunca consomem quota.

## Apuração e publicação

1. A API-Football informa que a partida terminou.
2. A Lambda persiste placar, eventos e estatísticas como resultado provisório.
3. O cálculo idempotente gera pontos e ranking provisórios.
4. A administração confere os dados, corrige divergências se necessário e confirma.
5. Uma nova execução idempotente grava a classificação oficial.
6. O resultado, o vencedor da rodada e o ranking passam a ser públicos.
7. O sistema envia uma notificação por e-mail ao vencedor da rodada; reprocessamentos do mesmo resultado não criam um novo envio automático.

A contagem de cartões segue as estatísticas da API-Football, que será indicada no regulamento como fonte de referência. O resultado confirmado pela administração é a autoridade final do sistema.

## Segurança e privacidade

- Cognito e DynamoDB usam criptografia em repouso gerenciada pela AWS.
- Todo tráfego usa HTTPS/TLS.
- E-mail e nome completo não são duplicados no DynamoDB.
- Logs usam identificadores internos e não registram nomes, e-mails, tokens ou corpos de requisição.
- Endpoints administrativos exigem o grupo `admins`.
- Lambdas recebem somente as permissões IAM necessárias.
- Segredos e a chave da API-Football não são enviados ao frontend nem armazenados no código-fonte.
- A política de privacidade informará finalidade, acesso administrativo e exclusão dos dados até 90 dias após o fim da competição e da entrega dos prêmios.

## Falhas e recuperação

- Timeout ou indisponibilidade da API externa mantém o último estado persistido e permite nova tentativa no próximo intervalo.
- Quota esgotada interrompe chamadas e preserva a opção de resultado manual.
- Jogador da API sem correspondência em `teams.json` exige associação administrativa antes da confirmação.
- Reprocessar resultados não duplica pontos.
- Reprocessar resultados não cria outra notificação automática do vencedor.
- Resultado provisório nunca é exposto ao público, mas fica disponível para administradores.
- Falha parcial durante a publicação pode ser repetida com segurança.

## Verificação

### Testes unitários

- Placar exato versus apenas resultado correto.
- Primeiro jogador a marcar.
- Artilheiro isolado e empate de artilheiros.
- Partida sem gols.
- Contagem de cartões por seleção.
- Critérios de desempate e horário substituído após edição.
- Cálculo do prazo de 10 minutos.
- Limite diário de chamadas.

### Testes de integração

- Uma previsão por participante e jogo.
- Concorrência entre gravação e encerramento.
- Validação dos tokens e do grupo administrativo.
- Importação de respostas representativas da API-Football.
- Reprocessamento idempotente do resultado.
- Correção e confirmação administrativa.

### Testes ponta a ponta

- Autenticar, cadastrar nome, enviar, editar e consultar o palpite.
- Rejeitar envio após o prazo.
- Ocultar palpites antes do prazo e mostrá-los depois.
- Exibir ranking provisório somente para administradores.
- Publicar vencedor e ranking após confirmação.
- Validar a experiência principal em telas móveis.

## Critérios de sucesso do MVP

- Um participante autenticado consegue enviar exatamente um palpite por jogo e editá-lo antes do prazo.
- Nenhuma submissão é aceita nos 10 minutos anteriores à partida ou depois.
- O sistema opera dentro de 80 chamadas diárias automáticas/administrativas da API-Football.
- A administração consegue revisar, corrigir e confirmar resultados.
- O público nunca vê pontuação provisória.
- O ranking segue a pontuação e os desempates definidos, destaca o primeiro colocado e publica o vencedor da rodada.
- Dados pessoais permanecem privados e criptografados em trânsito e em repouso.
