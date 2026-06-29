# Termos e fluxos do bolão

Este documento é um mapa curto do domínio. Os nomes em inglês correspondem aos tipos usados no código.

## Termos

- **Participant**: pessoa autenticada. O identificador interno é o `sub` do Cognito. Nome completo e e-mail são privados.
- **Match**: jogo publicado, com seleções e horário oficial. O cutoff ocorre 10 minutos antes do início.
- **Prediction**: versão final do palpite de um participante para um jogo. Uma edição substitui a anterior e atualiza `SubmittedAt`.
- **MatchResult**: snapshot do resultado de um jogo. Pode ser provisório, enquanto aguarda revisão, ou confirmado pela administração.
- **ScoreBreakdown**: pontos obtidos por um palpite em cada categoria do jogo. O total máximo é 18.
- **Standing**: classificação acumulada de um participante: pontos totais, contadores de desempate, horário e jogos já aplicados.
- **ResultVersion**: identificador da versão confirmada. Reprocessar a mesma versão não aplica pontos novamente.
- **Leaderboard**: lista pública ordenada a partir dos `Standing` confirmados. Dados provisórios não são expostos.

## Pontuação por jogo

| Critério | Pontos |
| --- | ---: |
| Placar exato | 5 |
| Apenas vencedor ou empate correto | 2 |
| Primeiro jogador a marcar | 3 |
| Artilheiro isolado de uma seleção | 3 por seleção |
| Um dos artilheiros empatados de uma seleção | 2 por seleção |
| Quantidade exata de cartões amarelos | 1 por seleção |
| Quantidade exata de cartões vermelhos | 1 por seleção |

Placar exato e resultado correto não acumulam pontos. Em uma partida sem gols, não há pontos por primeiro gol ou artilheiro. O máximo é 18 pontos por jogo.

## Ordem do ranking

1. Maior total de pontos.
2. Maior quantidade de placares exatos.
3. Maior quantidade de acertos do primeiro jogador a marcar.
4. Menor horário de envio da versão final do palpite.

Editar um palpite substitui o horário anterior para o desempate. O ranking público usa somente resultados confirmados; o ranking provisório fica restrito à administração.

## Fluxo do palpite

1. O participante autentica por código de e-mail e completa o perfil.
2. A interface carrega o jogo atual e os jogadores de `assets/teams.json`.
3. O backend identifica a pessoa pelo claim `sub`; o corpo da requisição não escolhe o participante.
4. O backend valida jogadores, números e o horário usando o relógio do servidor.
5. Antes do cutoff, o palpite é criado ou substituído pela chave `(MatchId, ParticipantId)`.
6. No cutoff, envio e edição são bloqueados. Os palpites dos demais participantes passam a ser públicos.

## Fluxo do resultado e ranking

1. A API-Football fornece placar, autores dos gols e cartões.
2. O sistema grava um `MatchResult` provisório, visível somente à administração.
3. A administração revisa e corrige os dados antes de confirmar.
4. A confirmação calcula um `ScoreBreakdown` para cada `Prediction`.
5. Os pontos são aplicados ao `Standing` de cada participante e a `ResultVersion` é marcada como publicada.
6. Repetir a mesma publicação é um no-op; os pontos não são duplicados.
7. Somente depois da confirmação o ranking público e o vencedor da rodada são atualizados.

## Rotas principais

Rotas públicas: jogo atual, histórico, palpites após o cutoff e ranking confirmado.

Rotas privadas: perfil e leitura/gravação do próprio palpite. A identidade sempre vem do token validado.
