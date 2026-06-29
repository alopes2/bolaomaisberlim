import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

const scoring = [
  ['Placar exato', '5'],
  ['Vencedor ou empate correto, sem placar exato', '2'],
  ['Primeiro jogador a marcar', '3'],
  ['Artilheiro isolado de cada seleção', '3'],
  ['Um dos artilheiros empatados de cada seleção', '2'],
  ['Amarelos exatos de cada seleção', '1'],
  ['Vermelhos exatos de cada seleção', '1'],
]

export function RulesPage() {
  return (
    <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Regras do bolão</CardTitle>
          <CardDescription>Critérios usados em cada jogo do Brasil.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5 text-sm">
          <section className="flex flex-col gap-2">
            <h2 className="font-medium">Pontuação</h2>
            <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-2">
              {scoring.map(([label, points]) => (
                <div key={label} className="contents">
                  <dt>{label}</dt><dd>{points} pts</dd>
                </div>
              ))}
            </dl>
            <p className="text-muted-foreground">
              Placar exato e resultado correto não acumulam. Em 0–0 não há pontos por primeiro gol ou artilheiro. O máximo é 18 pontos.
            </p>
          </section>
          <section className="flex flex-col gap-2">
            <h2 className="font-medium">Prazo e desempate</h2>
            <p>Palpites e edições encerram 10 minutos antes do início. O relógio do servidor é a referência.</p>
            <ol className="list-decimal pl-5">
              <li>Maior total de pontos.</li>
              <li>Mais placares exatos.</li>
              <li>Mais acertos do primeiro jogador a marcar.</li>
              <li>Envio final mais antigo. Uma edição substitui o horário anterior.</li>
            </ol>
          </section>
          <section className="flex flex-col gap-2">
            <h2 className="font-medium">Apuração e prêmio</h2>
            <p>
              Placar, gols e cartões usam a API-Football como referência. A administração do MaisBerlim revisa os dados e o resultado confirmado é a decisão final para a pontuação.
            </p>
            <p>A identidade do vencedor será validada antes da entrega do prêmio.</p>
          </section>
        </CardContent>
      </Card>
    </main>
  )
}
