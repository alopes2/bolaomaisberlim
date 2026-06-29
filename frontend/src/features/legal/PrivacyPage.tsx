import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

export function PrivacyPage() {
  return (
    <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Privacidade</CardTitle>
          <CardDescription>Dados usados no bolão MaisBerlim.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-sm">
          <p>
            O bolão usa e-mail verificado, nome e sobrenome para limitar uma participação por pessoa, administrar a competição e validar a entrega do prêmio.
          </p>
          <p>
            Publicamente mostramos somente o primeiro nome e a inicial do sobrenome, com um sufixo quando necessário para diferenciar nomes iguais. E-mail e nome completo ficam restritos à administração.
          </p>
          <p>
            Os dados são transmitidos por HTTPS e armazenados com a criptografia gerenciada pelos serviços AWS usados pelo projeto.
          </p>
          <p>
            A conta e os dados pessoais são removidos ou anonimizados 90 dias após a entrega do último prêmio relacionado à participação. Resultados agregados sem os dados privados podem ser preservados.
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
