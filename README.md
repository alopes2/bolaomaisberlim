# MaisBerlim Bolão da Copa

Bolão comunitário dos jogos do Brasil para o MaisBerlim.

## Pré-requisitos

- .NET SDK 10
- Node.js 24 ou posterior
- npm 11 ou posterior
- Terraform 1.10 ou posterior

## Desenvolvimento local

```bash
dotnet test backend/Bolao.slnx
npm --prefix frontend install
npm --prefix frontend run test:run
npm --prefix frontend run build
```

O frontend local pode ser iniciado com:

```bash
npm --prefix frontend run dev
```

As instruções de infraestrutura e implantação serão adicionadas junto aos respectivos módulos.

