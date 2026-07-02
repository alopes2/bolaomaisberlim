# Termos e fluxos do bolão

Este documento é um mapa curto do domínio. Os nomes em inglês correspondem aos tipos usados no código.

## Termos

- **Participant**: pessoa autenticada. O identificador interno é o `sub` do Cognito. Nome completo e e-mail são privados.
- **Match**: jogo publicado, com seleções e horário oficial. O cutoff ocorre 10 minutos antes do início.
- **Prediction**: versão final do palpite de um participante para um jogo. Uma edição substitui a anterior e atualiza `SubmittedAt`.
- **ManualResultDraft**: rascunho administrativo com gols ordenados, cartões e eventual vencedor nos pênaltis. A ordem determina o primeiro autor do gol e as contagens determinam placar e artilheiros.
- **MatchResult**: snapshot confirmado e imutável derivado do `ManualResultDraft`.
- **ScoreBreakdown**: pontos obtidos por um palpite em cada categoria do jogo. O total máximo é 18.
- **Standing**: classificação acumulada de um participante: pontos totais, contadores de desempate, horário e jogos já aplicados.
- **ResultVersion**: identificador da versão confirmada. Reprocessar a mesma versão não aplica pontos novamente.
- **Leaderboard**: lista pública ordenada a partir dos `Standing` confirmados. Dados provisórios não são expostos.
- **PrizeHandedOverAt**: data registrada pela administração após a entrega do prêmio; inicia a janela de retenção do participante.

## Pontuação por jogo

| Critério | Pontos |
| --- | ---: |
| Placar exato, sem disputa de pênaltis ou com vencedor correto | 5 |
| Placar exato, mas vencedor nos pênaltis ausente ou incorreto | 4 |
| Apenas vencedor ou empate correto | 2 |
| Primeiro jogador a marcar | 3 |
| Artilheiro isolado de uma seleção | 3 por seleção |
| Um dos artilheiros empatados de uma seleção | 2 por seleção |
| Quantidade exata de cartões amarelos | 1 por seleção |
| Quantidade exata de cartões vermelhos | 1 por seleção |

Placar exato e resultado correto não acumulam pontos. Um empate com placar não exato continua valendo 2 pontos, independentemente do vencedor nos pênaltis. Em uma partida sem gols, não há pontos por primeiro gol ou artilheiro. O máximo permanece 18 pontos por jogo.

## Ordem do ranking

1. Maior total de pontos.
2. Maior quantidade de placares exatos.
3. Maior quantidade de acertos do primeiro jogador a marcar.
4. Menor horário de envio da versão final do palpite.

Editar um palpite substitui o horário anterior para o desempate. O ranking público usa somente resultados confirmados; o ranking provisório fica restrito à administração.

## Fluxo do palpite

1. O participante autentica com Google e completa o perfil.
2. A interface carrega o jogo atual e os jogadores de `assets/teams.json`.
3. O backend identifica a pessoa pelo claim `sub`; o corpo da requisição não escolhe o participante.
4. O backend valida jogadores, números e o horário usando o relógio do servidor.
5. Antes do cutoff, o palpite é criado ou substituído pela chave `(MatchId, ParticipantId)`.
6. No cutoff, envio e edição são bloqueados. Os palpites dos demais participantes passam a ser públicos.

## Fluxo do resultado e ranking

1. A administração adiciona os gols em ordem, informa cartões e, quando o placar é empate, pode escolher o vencedor nos pênaltis.
2. O sistema salva um `ManualResultDraft`, deriva placar, primeiro autor e artilheiros e mostra o ranking provisório somente à administração.
3. A administração revisa e confirma o resultado.
4. A confirmação calcula um `ScoreBreakdown` para cada `Prediction`.
5. Os pontos são aplicados ao `Standing` de cada participante e a `ResultVersion` é marcada como publicada.
6. Repetir a mesma publicação é um no-op; os pontos não são duplicados.
7. Somente depois da confirmação o ranking público e o vencedor da rodada são atualizados.

## Fluxo manual dos jogos

1. O primeiro jogo criado fica `Active`; enquanto houver um ativo, novos jogos ficam `Upcoming`.
2. Status não mudam automaticamente por horário. A administração conduz o ciclo de vida pelas ações manuais de criar e finalizar jogos.
3. Um jogo ativo só pode ser finalizado depois que seu resultado foi confirmado e publicado.
4. A finalização muda o atual para `Closed` e ativa o próximo `Upcoming` por horário e ID.
5. Sem próximo jogo, não há jogo ativo; o próximo cadastro passa a ser `Active`.

Jogos e dados históricos não são apagados. Registros legados `Archived` continuam reconhecidos e visíveis, mas o fluxo manual atual não atribui esse status. O gerenciamento completo está em [manual-match-management.md](manual-match-management.md).

## Fluxo de retenção

1. Após entregar o prêmio, a administração registra `PrizeHandedOverAt` no jogo.
2. O job diário calcula, para cada participante, a data mais recente entre as rodadas em que participou.
3. Depois de 90 dias, solicita a exclusão da conta no Cognito e remove ou anonimiza nome completo, nome público e referência de conta.
4. Pontuações agregadas podem permanecer sem a associação aos dados privados.

## Fluxo de deploy

1. Pull requests de infraestrutura executam formato, validação e plan privado.
2. A branch principal aplica Terraform somente pelo ambiente GitHub protegido.
3. Backend publica um único ZIP e verifica o mesmo checksum nas Lambdas de API e retenção.
4. Frontend sincroniza `dist/` no bucket privado e aguarda a invalidação do CloudFront.
5. As roles GitHub OIDC são externas a este Terraform; secrets não entram no repositório.

## Rotas principais

Rotas públicas: jogo atual, histórico, palpites após o cutoff e ranking confirmado.

Rotas privadas: perfil e leitura/gravação do próprio palpite. A identidade sempre vem do token validado.
