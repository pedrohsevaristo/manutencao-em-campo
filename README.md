# Manutenção em Campo

Projeto de portfólio para acompanhar chamados de manutenção. A supervisão registra e atribui pelo painel MVC. O técnico acompanha os próprios chamados no app React Native, inicia o atendimento e registra o serviço realizado. Cada mudança fica no histórico, com data e responsável.

## Stack e validação

- **Web e API:** C#, ASP.NET Core 10 MVC e API REST, com EF Core e SQL Server. A demonstração rápida usa SQLite.
- **Clientes:** React Native com Expo Web para o técnico e um cliente de consulta em .NET Framework 4.8.
- **Qualidade:** 22 testes automatizados de segurança e regressão, além do fluxo MVC e Expo Web validado no Chrome. [Revisão final de segurança](FINAL_SECURITY_REVIEW.md)

## Rodar a demonstração no Windows

Você precisa do SDK do .NET 10, do Node.js 22.13+ da série 22 (ou uma versão indicada em `mobile/package.json`) e do PowerShell 7. A demonstração usa dados fictícios e escuta apenas em localhost.

1. Na raiz do repositório, rode `pwsh -NoProfile -File scripts/Start-Painel.ps1` para iniciar o painel e a API. O script aceita `-Dotnet` com o caminho de um SDK instalado localmente.
2. Em outro terminal, rode `cd mobile` e `npm.cmd ci`. Defina `$env:EXPO_PUBLIC_LOCAL_DEMO = '1'` e execute `npm.cmd run web`.
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

O comando básico usa SQLite. Para uma nova demonstração com SQL Server, o Compose oferece a edição Developer em `127.0.0.1:14333`. Informe os caminhos das seguintes configurações no processo que vai subir o Compose:

- `MSSQL_BOOTSTRAP_PASSWORD_FILE` e `MSSQL_HEALTH_PASSWORD_FILE` devem apontar para dois arquivos distintos, fora do repositório. Cada arquivo deve conter uma senha em UTF-8 sem BOM e sem quebra de linha. Restrinja as permissões no host e conceda acesso de leitura à identidade do contêiner. O Compose entrega esses arquivos como secrets de leitura. O processo SQL ainda recebe a senha de bootstrap por variável de ambiente e pode retê-la durante a execução.
- `MSSQL_TLS_CERT_FILE` e `MSSQL_TLS_KEY_FILE` apontam para o certificado PEM assinado por uma CA e a chave privada. Para laboratório, uma CA exclusiva de teste é suficiente. O processo `mssql` copia o material para um `tmpfs` privado, e o SQL Server exige criptografia com TLS 1.2. Proteja as permissões também nos arquivos do host.
- `MSSQL_TLS_CA_FILE` aponta para a CA em PEM que assina o certificado. `MSSQL_HEALTH_TLS_NAME` indica o nome a conferir nessa conexão TLS e tem padrão `localhost`. O certificado precisa conter o nome esperado.
- `MSSQL_HEALTH_DATABASE` seleciona o banco de prontidão e tem padrão `ManutencaoEmCampo`. `MSSQL_HOST_PORT` pode trocar a porta publicada em loopback.

O healthcheck valida a CA e o nome do certificado por TLS obrigatório. Depois, executa `SELECT 1` no banco configurado com a conta `manutencao_health`. Provisione o login com o valor do arquivo de healthcheck, mapeie-o como usuário no banco e conceda somente `CONNECT`. O serviço pode ficar `unhealthy` até o banco e a conta existirem. Use uma identidade de provisionamento para essa preparação. O processo da aplicação usa sua conta própria.

O perfil local limita o contêiner a 2 CPUs, 3 GiB de memória e 512 processos/threads, com teto de 2 GiB no SQL Server. O Compose tenta reiniciar o processo até três vezes quando ele termina com erro. O estado `unhealthy` sinaliza indisponibilidade e pode alimentar alertas.

Para uma implantação real, escolha uma edição aprovada com base em licenciamento, capacidade e recursos necessários. Standard ou Enterprise licenciadas e serviços gerenciados são opções a avaliar. A edição Developer do Compose destina-se a desenvolvimento e testes. O [procedimento de backup e restauração](sql/RECOVERY.md) cobre o ensaio e a invalidação de sessões após recuperação.

Crie um projeto novo para a demonstração. Verifique se o nome escolhido e os recursos associados estão livres antes de iniciar. A configuração com secrets e o certificado assinado por CA deve estar pronta antes de recriar voluntariamente um ambiente anterior.

~~~powershell
docker compose -p manutencao-demo-nova up -d
~~~

Para provisionar esse banco recém-criado, conecte a identidade administrativa em uma sessão de bootstrap e use a validação por certificado. A senha é lida do arquivo montado no contêiner.

~~~powershell
docker compose -p manutencao-demo-nova exec sqlserver sh -ec 'SQLCMDPASSWORD=$(cat /run/secrets/mssql_bootstrap_password); export SQLCMDPASSWORD SSL_CERT_FILE=/run/tls-input/ca.pem SSL_CERT_DIR=/etc/maintenance-tls; exec /opt/mssql-tools18/bin/sqlcmd -S tcp:127.0.0.1,1433 -Nm -F "$MSSQL_HEALTH_TLS_NAME" -U sa -b'
~~~

Crie o banco selecionado em `MSSQL_HEALTH_DATABASE` e as identidades descritas abaixo. Finalize a sessão administrativa ao concluir. Proteja e rotacione as credenciais de bootstrap e mantenha a rotina da aplicação e o probe separados da conta administrativa.

1. Em um banco de demonstração separado, provisione duas identidades distintas. Use uma para migrations e outra para a execução.
2. Configure `ConnectionStrings__SqlServerMigrations` somente no processo de preparação. Para aplicar migrations em produção, execute `dotnet tool restore` e `dotnet ef database update --project src/ManutencaoEmCampo.Web`. Para migrar e inserir as contas fictícias em um banco de demonstração separado, use a preparação explícita abaixo.
3. Após aplicar o esquema, execute [o script de permissões](sql/runtime-permissions.sql) no banco da aplicação. Ele cria o papel `manutencao_runtime` com permissões nos objetos usados. Atribua explicitamente esse papel apenas à identidade de execução. A aplicação verifica privilégios administrativos na primeira abertura de conexão de cada processo e recusa identidades de execução com esses privilégios.
4. Configure `ConnectionStrings__SqlServer` com a identidade limitada para iniciar a aplicação. Ambas as conexões exigem certificado verificado. Use `Encrypt=Mandatory;TrustServerCertificate=False` e uma CA reconhecida. Em testes isolados, `Server Certificate` pode apontar para o arquivo público exato do certificado. Gerencie senhas e chaves pelo ambiente de implantação.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__Provider = 'SqlServer'
$env:Demo__Enabled = 'true'
# Defina Demo__Password para as contas fictícias e a conexão de migrations antes de executar.
dotnet run --project src/ManutencaoEmCampo.Web --no-launch-profile -- --initialize-sql-demo
```

Após a preparação, use a mesma senha fictícia em `DEMO_PASSWORD`, forneça apenas a conexão de execução e inicie a demo:

```powershell
pwsh -NoProfile -File scripts/Start-Painel.ps1 -Database SqlServer
```

O processo HTTP usa o esquema já preparado. A inicialização explícita para inserir contas fictícias é exclusiva de Development. Migrations e snapshot estão em `src/ManutencaoEmCampo.Web/Data/Migrations`.

## Cliente .NET Framework 4.8

Com a API ativa, execute no Windows:

```powershell
dotnet run --project src/ManutencaoEmCampo.Legacy -- --demo
```

Entre com uma conta fictícia da tabela. O cliente autentica, lista até dez chamados visíveis à conta e solicita o encerramento da sessão antes de sair. A senha é lida sem eco quando executado em um terminal interativo. Fora da demonstração, forneça a URL HTTPS final da API em vez de `--demo`, pois o cliente recusa redirecionamentos.

## Transporte e sessões fora da demonstração

- Configure um endpoint HTTPS e um certificado no Kestrel. Se houver um proxy, use passagem de TLS ou cifre também o trecho entre o proxy e o Kestrel. A aplicação rejeita HTTP sem TLS mesmo quando recebe um cabeçalho de esquema encaminhado.
- Defina `AllowedHosts` em ambientes fora de Development. Informe nomes/IPs exatos, separados por ponto e vírgula, sem portas ou curingas. O proxy deve preservar um `Host` permitido. O servidor ignora `X-Forwarded-Host`.
- No build distribuído do Expo, defina `EXPO_PUBLIC_API_URL` como a origem HTTPS final da API. Para um app web hospedado em outra origem HTTPS, configure `Mobile__WebOrigin` no servidor. A opção `EXPO_PUBLIC_LOCAL_DEMO=1` só permite HTTP em loopback no modo de desenvolvimento do Expo.
- O script de demonstração habilita HTTP explicitamente. O servidor exige `Development`, `Demo__Enabled=true`, `Demo__AllowHttp=true` e conexão e host de loopback.
- Aplique as migrations do SQL Server antes de atualizar o serviço. Na demo SQLite, a inicialização adiciona a nova estrutura preservando os dados existentes. Após a atualização, faça login novamente.
- Cada sessão tem registro próprio. Bearer dura até 30 minutos, e a sessão MVC até 8 horas. O logout revoga a sessão atual. O servidor verifica a conta, o carimbo de segurança e os papéis a cada requisição autenticada. Para desativar uma conta no código de administração, use `WorkUser.SetActive(false)` e persista com `UserManager.UpdateAsync`. Alterações de papel pelo `UserManager` também renovam o carimbo.

## API e qualidade

- `POST /api/session` emite um token de acesso, `GET /api/session` retorna o perfil autenticado e `DELETE /api/session` revoga o token usado. O painel usa cookies e proteção antifalsificação nos formulários.
- Respostas dos controladores usam `Cache-Control: no-store`. Ativos públicos conservam a política de cache dos arquivos estáticos.
- Os limites por minuto são 60 tentativas de login por origem, 8 por combinação de conta e origem, 120 operações e 40 listagens por conta autenticada. Ajuste com `Traffic__LoginPerSource`, `Traffic__LoginPerAccountAndSource`, `Traffic__OperationsPerAccount` e `Traffic__ListPerAccount`. O limite de corpo é 32 KiB. O login anônimo deixou de incrementar o bloqueio do Identity. Bloqueios já existentes continuam sendo respeitados. Respostas 429 incluem `Retry-After`.
- Use `Traffic__TrustedProxies__0` e índices seguintes para os IPs exatos dos proxies autorizados a encaminhar o endereço do cliente. A política considera uma etapa de encaminhamento. Sem essa lista, um proxy ou NAT que compartilhe um IP compartilha também a cota por origem. Cotas são locais a cada instância.
- Logs de produção em JSON e o contador `manutencao.security.events` registram resultados de autenticação, autorização, bloqueio e revogação. O consumidor de métricas deve assinar o medidor `ManutencaoEmCampo.Security`. Configure coleta, retenção, acesso e alertas no ambiente de implantação.
- `GET /api/chamados` aceita `status`, `page` e `pageSize`. Técnicos consultam apenas chamados atribuídos a eles. A consulta pagina no banco, com projeção dos campos da lista e índices para ordenação e filtros.
- A supervisão cria e atribui. O técnico responsável pode iniciar e concluir. A conclusão exige uma nota de 10 a 1.000 caracteres. Atualizações concorrentes retornam conflito e pedem recarga.
- O contrato OpenAPI fica em [localhost:5180/openapi/v1.json](http://localhost:5180/openapi/v1.json) em desenvolvimento.
- Com a demonstração ativa e credenciais fictícias, rode `node --test scripts/test-demo.mjs`. Os testes cobrem login, autorização, transições, histórico e paginação. Eles criam um chamado de teste no banco da demonstração. Para o app, rode `npm.cmd run typecheck` e `npm.cmd run build:web` em `mobile`.
- Após `dotnet build -c Release`, rode `node --experimental-strip-types --test scripts/test-security.mjs` para verificar revogação, compatibilidade com banco SQLite anterior, política HTTPS e redirecionamentos. O script inicia suas próprias instâncias em loopback e cria bancos descartáveis. Se o SDK for local, defina `DOTNET_EXE` com o caminho do executável para os testes.

## Build verificado e atualização de dependências

No Windows, com PowerShell 7, SDK .NET 10 e a versão de Node indicada em `mobile/package.json`, execute o comando abaixo na raiz.

~~~powershell
$env:EXPO_PUBLIC_API_URL = 'https://api.seu-dominio.example'
pwsh -NoProfile -File .\scripts\Build-Verified.ps1
~~~

Use a origem HTTPS da API para o build web distribuído. O script restaura os dois projetos .NET em modo bloqueado, audita dependências diretas e transitivas do NuGet e usa `npm ci` para instalar a partir do lockfile com verificação de integridade e scripts de instalação desabilitados. Também compila a solução em Release, checa TypeScript, exporta o Expo para web e roda as regressões de segurança, incluindo o cliente legado no Windows.

Achados altos e críticos interrompem o build. Achados de menor severidade ficam visíveis para revisão. Falhas ao obter os dados de auditoria também interrompem a verificação. A pasta `.local/build-verification/` recebe as auditorias em JSON, o inventário NuGet e o SBOM npm CycloneDX. O SBOM descreve o grafo do lockfile do Expo, incluindo dependências de desenvolvimento e variantes para outras plataformas.

### Atualização controlada

1. Revise o advisory e a matriz de compatibilidade do SDK. Altere as referências desejadas. Para o Expo, use o procedimento oficial do SDK para selecionar versões compatíveis.
2. Regenere os locks do NuGet com `dotnet restore ManutencaoEmCampo.slnx --configfile NuGet.Config -p:RestoreLockedMode=false --force-evaluate -m:1`. Para dependências npm, regenere o lock em `mobile` com `npm install --package-lock-only --ignore-scripts`.
3. Revise as versões, origens, hashes e mudanças transitivas. Rode o build verificado e versione os manifests junto dos locks correspondentes.
4. Para trocar a imagem SQL Server, obtenha o digest publicado pelo registro oficial da Microsoft para a plataforma alvo. Valide a nova referência com `docker compose config --images` e teste migrations, TLS e operações da aplicação em um contêiner isolado antes de integrar a alteração em `compose.yaml`.

O digest fixa o conteúdo da imagem. Atualizações de segurança da imagem exigem escolher e validar um novo digest. Os locks fixam o grafo de pacotes. Para comparar builds, registre também as versões do SDK .NET, Node, npm e a plataforma.

## Exemplo de uso de IA no debug

O projeto foi criado com apoio do Codex. No primeiro teste, `GET /entrar` retornou HTTP 500. Uma hipótese foi que o ASP.NET Core tentava gravar a chave de proteção de dados em um diretório bloqueado. A exceção `UnauthorizedAccessException` no provedor de chaves confirmou a origem. A correção usa chaves em memória durante a demonstração e mantém a validação antifalsificação ativa. O teste em `scripts/test-demo.mjs` reproduziu o 500 antes da correção e verifica a resposta e o token antifalsificação.
