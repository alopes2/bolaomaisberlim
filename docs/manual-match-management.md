# Gerenciamento manual de jogos

Administradores gerenciam jogos e resultados pela página `/admin`. Não há importação, consulta externa ou transição automática de status.

## Criar e ativar jogos

Use **Adicionar jogo**, informe a data e hora de início e escolha mandante e visitante nas listas de seleções. As listas vêm de `assets/teams.json`, que continua sendo a fonte dos códigos FIFA, nomes, bandeiras e jogadores. A seleção escolhida em um campo é removida das opções do outro.

O backend gera o ID no formato `bra-nor-05-07`, combinando os códigos FIFA de mandante e visitante com o dia e o mês do início em `Europe/Berlin`. O ID não muda quando o jogo é editado. Se o ID gerado já existir, a criação falha com `409` e o código `match_exists`.

- Se não houver jogo `Active`, o primeiro jogo criado fica `Active` imediatamente.
- Se já houver um jogo `Active`, o novo jogo fica `Upcoming`.
- Só pode existir um jogo `Active`.
- Jogos concluídos permanecem `Closed`.
- Registros históricos que já estejam `Archived` continuam reconhecidos e visíveis, mas a interface e as rotas manuais atuais não atribuem esse status.

O sistema não muda status com base no relógio. A administração conduz o ciclo de vida pelas ações de criar e finalizar jogos; não há edição arbitrária de status.

O ID identifica o jogo em palpites e resultados e, por isso, não pode ser alterado depois da criação. A ação **Editar jogo** permite corrigir somente a data e hora de início e as duas seleções. O status continua sendo controlado pelas ações de criar e finalizar.

## Gerenciar seleções

Use **Gerenciar seleções** para marcar uma seleção como eliminada ou restaurá-la. Esse estado fica no DynamoDB e não altera `assets/teams.json`.

- Seleções eliminadas deixam de aparecer nas listas de novos jogos.
- Ao editar, uma seleção eliminada continua disponível somente no lado em que já está atribuída.
- Restaurar uma seleção a disponibiliza novamente para novos jogos.
- Jogos existentes, resultados, palpites e histórico não são removidos nem alterados.

## Registrar e confirmar o resultado

Abra o jogo na área administrativa e registre os gols na ordem em que aconteceram. Cada linha seleciona a seleção e o jogador. Use os botões de subir e descer para corrigir a ordem sem remover o gol. A ordem define o primeiro autor de gol; a quantidade de gols por jogador define os artilheiros, inclusive empates.

Informe também os totais de cartões amarelos e vermelhos de cada seleção. Se o placar derivado dos gols for um empate, é possível escolher a seleção vencedora nos pênaltis. A escolha fica desabilitada em qualquer outro placar.

Salvar mantém um rascunho editável. **Confirmar resultado** publica a versão usada na pontuação e atualiza o ranking. Um resultado confirmado não pode ser substituído pelo fluxo normal.

## Finalizar o jogo atual

**Finalizar jogo atual** exige que o jogo esteja `Active` e que seu resultado já tenha sido confirmado e publicado. A operação:

1. altera o jogo atual para `Closed`;
2. ativa o jogo `Upcoming` com o início mais próximo (o ID desempata horários iguais);
3. deixa o sistema sem jogo ativo quando não existe próximo jogo.

Nesse último caso, o participante vê `Nenhum bolao ativo no momento`. O próximo jogo criado passa a ser `Active`.

## Rotas administrativas

Todas exigem um token de administrador:

| Método e rota | Uso |
| --- | --- |
| `GET /admin/matches` | Lista os jogos e seus status. |
| `POST /admin/matches` | Cria um jogo manualmente e gera seu ID. |
| `PUT /admin/matches/{matchId}` | Atualiza data, hora e seleções; o ID da rota permanece imutável. |
| `GET /admin/teams` | Lista as seleções do roster e seu estado de eliminação. |
| `PUT /admin/teams/{fifaCode}/elimination` | Marca uma seleção como eliminada ou a restaura. |
| `GET /admin/matches/{matchId}/result` | Lê o rascunho manual do resultado. |
| `PUT /admin/matches/{matchId}/result` | Salva gols ordenados, cartões e eventual vencedor nos pênaltis. |
| `GET /admin/matches/{matchId}/provisional-leaderboard` | Calcula a classificação com o rascunho atual. |
| `POST /admin/matches/{matchId}/confirm` | Confirma e publica o resultado. |
| `POST /admin/matches/{matchId}/finish` | Fecha o jogo e ativa o próximo. |

Exemplo de criação:

```bash
curl -sS -i -X POST "$API_BASE_URL/admin/matches" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{
    "kickoff": "2026-07-01T18:00:00+02:00",
    "homeTeamFifaCode": "BRA",
    "awayTeamFifaCode": "FRA",
    "prizeHandedOverAt": null
  }'
```

## Erros estáveis

Erros da API usam Problem Details e incluem um campo `code`. Os códigos relevantes são:

- `invalid_match`, `match_exists` e `match_not_found` para cadastro e consulta;
- `team_not_found` para gerenciamento de seleções;
- `invalid_result` e `result_already_confirmed` para o rascunho e a confirmação;
- `match_not_active`, `confirmed_result_required` e `match_lifecycle_conflict` para a finalização.

Clientes devem decidir o comportamento pelo `code`, não pelo texto de `detail`.

## Dados existentes

A remoção da sincronização não apaga jogos, palpites, resultados, classificação, participantes ou entrega de prêmio. Atributos antigos do provedor que ainda existam em itens do DynamoDB são ignorados. Novas gravações não dependem nem recriam esses atributos.
