# Manutenção em Campo

Projeto de portfólio para acompanhar chamados de manutenção. A supervisão registra e atribui pelo painel MVC. O técnico acompanha os próprios chamados no app React Native, inicia o atendimento e registra o serviço realizado. Cada mudança fica no histórico, com data e responsável.

## Rodar a demonstração no Windows

Você precisa do SDK do .NET 10, do Node.js 22.13+ da série 22 (ou uma versão indicada em `mobile/package.json`) e do PowerShell 7. A demonstração usa dados fictícios e escuta apenas em localhost.

1. Na raiz do repositório, rode `pwsh -NoProfile -File scripts/Start-Painel.ps1` para iniciar o painel e a API. O script aceita `-Dotnet` com o caminho de um SDK instalado localmente.
2. Em outro terminal, rode `cd mobile`, `npm.cmd ci` e `npm.cmd run web`.
3. Abra o painel em [localhost:5180](http://localhost:5180) e o app no navegador em [localhost:8081](http://localhost:8081).

| Perfil | E-mail |
| --- | --- |
| Supervisora · Marina Campos | `supervisor@demo.test` |
| Técnico · Rafael Lima | `tecnico@demo.test` |
| Técnica · Camila Rocha | `tecnica@demo.test` |

A senha fictícia padrão é `CampoLocal!2026`. A variável `DEMO_PASSWORD` permite escolher outra senha ao criar as contas em um banco novo. Reiniciar o servidor encerra as sessões do modo de demonstração.

### Demonstração de três minutos

1. Entre como supervisora. Abra **Novo chamado**, preencha os campos e registre.
2. No detalhe, atribua o chamado a **Rafael Lima**.
3. No app, entre como Rafael, abra o chamado e selecione **Iniciar atendimento**.
4. Informe a execução, por exemplo, `Vedação substituída e vazamento resolvido.` Depois selecione **Concluir atendimento**.
5. Atualize o painel e confira o status **Concluído** e os quatro eventos no histórico.

## SQL Server

O comando básico usa um arquivo SQLite local de demonstração. Para mostrar SQL Server localmente, inicie o Docker Desktop e defina `MSSQL_SA_PASSWORD` com uma senha forte da sua escolha. O Compose usa a edição Developer para desenvolvimento e mapeia o serviço somente para `127.0.0.1:14333`.

```powershell
pwsh -NoProfile -File scripts/Start-Painel.ps1 -Database SqlServer
```

O script aguarda o banco ficar pronto e o aplicativo aplica a migration antes de inserir os dados fictícios. A migration e o snapshot estão em `src/ManutencaoEmCampo.Web/Data/Migrations`.

## Cliente .NET Framework 4.8

Com a API ativa, execute no Windows:

```powershell
dotnet run --project src/ManutencaoEmCampo.Legacy
```

Entre com uma conta fictícia da tabela. O cliente autentica e lista até dez chamados visíveis à conta usando a mesma API. A senha é lida sem eco quando executado em um terminal interativo.

## API e qualidade

- `POST /api/session` emite um token de acesso. `GET /api/session` retorna o perfil autenticado. O painel usa cookies e proteção antifalsificação nos formulários.
- `GET /api/chamados` aceita `status`, `page` e `pageSize`. Técnicos consultam apenas chamados atribuídos a eles. A consulta pagina no banco, com projeção dos campos da lista e índices para ordenação e filtros.
- A supervisão cria e atribui. O técnico responsável pode iniciar e concluir. A conclusão exige uma nota de 10 a 1.000 caracteres. Atualizações concorrentes retornam conflito e pedem recarga.
- O contrato OpenAPI fica em [localhost:5180/openapi/v1.json](http://localhost:5180/openapi/v1.json) em desenvolvimento.
- Com a demonstração ativa e credenciais fictícias, rode `node --test scripts/test-demo.mjs`. Os testes cobrem login, autorização, transições, histórico e paginação. Eles criam um chamado de teste no banco da demonstração. Para o app, rode `npm.cmd run typecheck` e `npm.cmd run build:web` em `mobile`.

## Exemplo de uso de IA no debug

O projeto foi criado com apoio do Codex. No primeiro teste, `GET /entrar` retornou HTTP 500. Uma hipótese foi que o ASP.NET Core tentava gravar a chave de proteção de dados em um diretório bloqueado. A exceção `UnauthorizedAccessException` no provedor de chaves confirmou a origem. A correção usa chaves em memória durante a demonstração e mantém a validação antifalsificação ativa. O teste em `scripts/test-demo.mjs` reproduziu o 500 antes da correção e verifica a resposta e o token antifalsificação.
