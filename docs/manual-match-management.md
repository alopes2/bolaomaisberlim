# Gerenciamento manual de jogos

Administradores gerenciam jogos e resultados pela página `/admin`. Não há importação, consulta externa ou transição automática de status.

## Criar e ativar jogos

Use **Adicionar jogo** e informe um ID único, data e hora de início e as duas seleções. Os códigos FIFA devem existir em `assets/teams.json`.

- Se não houver jogo `Active`, o primeiro jogo criado fica `Active` imediatamente.
- Se já houver um jogo `Active`, o novo jogo fica `Upcoming`.
- Só pode existir um jogo `Active`.
- Jogos concluídos permanecem `Closed`.
- Registros históricos que já estejam `Archived` continuam reconhecidos e visíveis, mas a interface e as rotas manuais atuais não atribuem esse status.

O sistema não muda status com base no relógio. A administração conduz o ciclo de vida pelas ações de criar e finalizar jogos; não há edição arbitrária de status.

O ID identifica o jogo em palpites e resultados e, por isso, não pode ser alterado depois da criação. A ação **Editar jogo** permite corrigir somente a data e hora de início e as duas seleções. O status continua sendo controlado pelas ações de criar e finalizar.

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
| `POST /admin/matches` | Cria um jogo manualmente. |
| `PUT /admin/matches/{matchId}` | Atualiza data, hora e seleções; o ID da rota permanece imutável. |
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
    "id": "wc2026-bra-fra",
    "kickoff": "2026-07-01T18:00:00+02:00",
    "homeTeamFifaCode": "BRA",
    "awayTeamFifaCode": "FRA",
    "prizeHandedOverAt": null
  }'
```

## Erros estáveis

Erros da API usam Problem Details e incluem um campo `code`. Os códigos relevantes são:

- `invalid_match`, `match_exists` e `match_not_found` para cadastro e consulta;
- `invalid_result` e `result_already_confirmed` para o rascunho e a confirmação;
- `match_not_active`, `confirmed_result_required` e `match_lifecycle_conflict` para a finalização.

Clientes devem decidir o comportamento pelo `code`, não pelo texto de `detail`.

## Dados existentes

A remoção da sincronização não apaga jogos, palpites, resultados, classificação, participantes ou entrega de prêmio. Atributos antigos do provedor que ainda existam em itens do DynamoDB são ignorados. Novas gravações não dependem nem recriam esses atributos.
