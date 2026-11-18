# Auditoria de segurança do Manutenção em Campo

**Revisão de 18/11/2026** · Baseline Git `1c3d78e` · Auditoria local e revisão de código

## Resumo executivo

O controle por perfil e por chamado funcionou nos cenários locais testados. A API rejeitou usuários anônimos, tokens inválidos e acessos de um técnico a chamados de outro. O painel aplicou proteção antifalsificação nos formulários exercitados. A publicação com dados reais deve aguardar a revogação de sessões, a definição do transporte HTTPS e a revisão da configuração do ambiente de implantação.

| Crítica | Alta | Média | Baixa | Informativa |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 0 | 3 | 6 | 3 |

As três observações informativas cobrem versões afetadas em uma ferramenta do Expo e na biblioteca nativa do SQLite, sem condições de exploração identificadas no fluxo atual, e lacunas de reprodutibilidade dos builds. A contagem abrange a evidência disponível neste repositório e nos ambientes locais exercitados.

## Escopo, arquitetura e limites de confiança

| Componente | Tecnologia e versão observada | Entrada e dados tratados |
| --- | --- | --- |
| Servidor | SDK .NET 10.0.404. ASP.NET Core, Identity, OpenAPI e EF Core 10.0.15 | Formulários MVC, API JSON, cookies, tokens opacos, credenciais, chamados e nomes de responsáveis |
| Banco | Microsoft.Data.SqlClient 6.1.7 para SQL Server 2022 Developer no Compose. SQLite 3.53.3 via Microsoft.Data.Sqlite.Core 10.0.15 e SQLitePCLRaw 2.1.13 na demonstração | Consultas e gravações do EF Core, hashes de senha, papéis, dados de chamados e histórico |
| App | Expo 58.0.26, React 19.3.0, React Native 0.88.1, React Native Web 0.21.4, TypeScript 6.0.3 | E-mail e senha no login. Token de acesso em memória. API configurada via `EXPO_PUBLIC_API_URL` |
| Cliente Windows | .NET Framework 4.8, `HttpClient` e `DataContractJsonSerializer` | Endereço da API fornecido ao iniciar, credenciais e consulta autenticada de chamados |
| Build e operação | Node.js 22.23.5, npm 10.9.9, NuGet, dotnet-ef 10.0.15 e Docker Compose | Pacotes dos registros públicos, compilação, configuração por variáveis de ambiente e migration de SQL Server |

O navegador da supervisão envia o formulário ao servidor MVC e recebe um cookie do Identity. O app e o cliente Windows enviam credenciais para `POST /api/session` e usam o token de acesso em `Authorization`. MVC e API compartilham serviços e regras de domínio. Somente o servidor acessa o banco. O esquema bearer usa o ASP.NET Core Data Protection e carrega as permissões do usuário no ticket emitido. As telas e a API expõem dados de negócio conforme o perfil, incluindo descrições livres que devem ser tratadas como potencialmente sensíveis.

| Superfície | Rotas e controles observados |
| --- | --- |
| Sessão MVC | `GET/POST /entrar`, `POST /sair`, `GET /acesso-negado`. Cookie de sessão e antifalsificação nas operações de escrita. A entrada no painel exige supervisão. |
| Painel | `GET /`, `GET/POST /chamados/novo`, `GET /chamados/{id}`, `POST /chamados/{id}/responsavel`. Perfil de supervisor e validação do formulário. |
| Sessão da API | `POST /api/session` anônimo e limitado por IP. `GET /api/session` exige bearer e perfil operacional. |
| Dados da API | Listagem e detalhe em `GET /api/chamados` e `GET /api/chamados/{id}`. Criação, atribuição e lista de técnicos exigem supervisão. Início e conclusão exigem perfil técnico e atribuição ao próprio usuário. |
| Exposição auxiliar | CSS em `wwwroot`. OpenAPI em `/openapi/v1.json` em Development. Nenhum ponto de entrada para upload de arquivos ou para requisições de saída feitas pelo servidor em nome de um usuário foi identificado. |

Os limites principais são cliente–API, processo–banco, processo–armazenamento de chaves e build–registro de pacotes. Em Development, o script de demonstração escuta em loopback, habilita dados fictícios e aceita o app web na origem local configurada. Em Production, o código seleciona SQL Server, omite o seed de demonstração e inclui HSTS e redirecionamento HTTPS. A configuração efetiva de TLS, proxy, base de dados e persistência de chaves depende da implantação. O repositório não contém manifesto de infraestrutura ou pipeline de produção que permita validá-la.

## Metodologia

Revisão dos 48 arquivos versionados e das três revisões locais, incluindo código, views, configuração, migration, scripts, manifests e lockfiles. Foram cruzados o OWASP Top 10 de 2025, os controles pertinentes das edições anteriores e o OWASP API Security Top 10 de 2023. As verificações dinâmicas usaram instâncias independentes em loopback e contas fictícias. A simulação de indisponibilidade usou um endereço de banco em loopback sem um servidor ativo, sem interromper bancos existentes. As consultas externas foram restritas à leitura de documentação, advisories e metadados públicos, sem enviar inventário do projeto a serviços de auditoria. [OWASP Top 10 2025](https://top10.owasp.org/2025/) · [OWASP Top 10 2021](https://owasp.org/Top10/2021/) · [OWASP API Security Top 10 2023](https://api-security.owasp.org/editions/2023/en/0x11-t10/)

As severidades são específicas ao projeto, considerando o privilégio necessário, o alcance e o impacto. Nos achados, **C** indica confidencialidade, **I** integridade e **D** disponibilidade. As referências CVE aparecem somente quando há um identificador publicado para a dependência.

## Achados confirmados

### SEC-01 · Média · Sessões bearer mantêm privilégios até expirar

**CWE-613 e CWE-863 · OWASP A01 e A07 · API2.** O login emite um ticket com as claims atuais. O bearer é aceito mediante proteção criptográfica e expiração, sem consulta ao usuário, ao papel atual ou a uma revogação. O prazo configurado é de duas horas. O app limpa apenas o estado local ao sair. No fluxo MVC, o logout encerra o cookie no navegador, sem armazenamento de sessão no servidor configurado para revogar uma cópia anterior. O teste S02 confirmou a rejeição de tokens adulterados. A janela de revogação foi identificada por leitura de código e do framework, sem coletar ou reutilizar sessões alheias. [Program.cs, linhas 29–44](src/ManutencaoEmCampo.Web/Program.cs#L29-L44) · [SessionApiController.cs, linhas 16–30](src/ManutencaoEmCampo.Web/Controllers/SessionApiController.cs#L16-L30) · [App.tsx, linhas 247–254](mobile/src/App.tsx#L247-L254) · [AccountController.cs, linhas 39–44](src/ManutencaoEmCampo.Web/Controllers/AccountController.cs#L39-L44) · [ASP.NET Core bearer handler](https://source.dot.net/Microsoft.AspNetCore.Authentication.BearerToken/BearerTokenHandler.cs.html)

**Cenário e impacto.** Uma sessão previamente exposta ou associada a um funcionário desligado pode continuar autorizada. C e I podem ser afetadas conforme o perfil. D é secundária. **Correção.** Reduzir o tempo de vida, registrar sessões revogáveis, revalidar conta e papel com prazo definido e implementar revogação ao sair e ao alterar permissões. Definir um procedimento de reautenticação comum ao app e ao cliente legado. Avaliar a retirada do `refreshToken` atualmente retornado e sem rota de renovação.

### SEC-02 · Média · A proteção do transporte depende de configuração externa

**CWE-319 · OWASP A02 e A04 · API8.** O app aceita uma URL HTTP não local em `EXPO_PUBLIC_API_URL` e repassa a ela a senha e os tokens. O fallback HTTP em loopback é intencional na demonstração. No servidor, o mesmo código em `Production` redirecionou quando a porta HTTPS foi fornecida. Quando iniciado deliberadamente em loopback apenas com HTTP e sem porta HTTPS, processou as requisições. A proteção efetiva depende de TLS no servidor ou na borda, além das políticas do cliente. [api.ts, linhas 1 e 55–69](mobile/src/api.ts#L55-L69) · [Program.cs, linhas 74–89](src/ManutencaoEmCampo.Web/Program.cs#L74-L89)

**Cenário e impacto.** Se uma versão publicada usar uma URL HTTP ou se o serviço ficar acessível sem TLS, um intermediário pode observar credenciais e dados ou alterar respostas. C e I afetadas. D pode ser afetada por bloqueio de conteúdo misto ou falha de conexão. **Correção.** Exigir HTTPS e origem conhecida nos builds distribuídos. Preservar loopback HTTP apenas na configuração explícita de demonstração. Configurar TLS e rejeição de HTTP na borda e testar a trava de inicialização do ambiente público. Usar `SecurePolicy.Always` nos cookies de produção e configurar apenas proxies conhecidos.

### SEC-03 · Média · Cliente legado segue redirecionamentos nas requisições de login

**CWE-522 e CWE-319 · OWASP A04 e A07.** O cliente valida o esquema da URL inicial, instancia `HttpClient` com redirecionamentos automáticos padrão e envia e-mail e senha no corpo do POST. O .NET Framework segue também redirecionamentos de HTTPS para HTTP. Redirecionamentos que preservam método e corpo podem reenviar as credenciais. A limpeza automática de `Authorization` cobre o cabeçalho, enquanto o login carrega a senha no corpo. Evidência estática. [Program.cs do legado, linhas 17–49](src/ManutencaoEmCampo.Legacy/Program.cs#L17-L49) · [Microsoft · AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect)

**Cenário e impacto.** Uma API ou borda mal configurada que redirecione o login pode encaminhar credenciais para outra origem ou um trecho HTTP. C afetada e I indiretamente afetada se a conta for usada indevidamente. D pode ser afetada pela falha de login. **Correção.** Desabilitar redirecionamento automático e exigir o endereço canônico. Validar com redirecionamentos locais controlados após a correção.

```csharp
using var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var client = new HttpClient(handler)
{
    BaseAddress = origin,
    Timeout = TimeSpan.FromSeconds(10)
};
```

### SEC-04 · Baixa · Emissão de tokens e respostas autenticadas sem política explícita de cache

**CWE-525 · OWASP A04 e A07 · API2.** Em S02, `POST /api/session` e `GET /api/chamados` autenticado retornaram sem `Cache-Control`. A emissão retornou também um `refreshToken`, sem `Pragma`. Isso demonstra a ausência de instrução explícita ao cliente. O teste não constatou que um proxy ou navegador efetivamente guardou dados. [SessionApiController.cs, linhas 16–30](src/ManutencaoEmCampo.Web/Controllers/SessionApiController.cs#L16-L30) · [TicketsApiController.cs, linhas 8–21](src/ManutencaoEmCampo.Web/Controllers/TicketsApiController.cs#L8-L21) · [OWASP · gerenciamento de sessões](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)

**Cenário e impacto.** Um cliente ou cache com regras permissivas pode reter dados operacionais ou credenciais de sessão. C afetada. I pode ser afetada se um token ainda válido vazar. D sem impacto direto. **Correção.** Aplicar `Cache-Control: no-store` às respostas de sessão e dados privados. Por exemplo, aplicar `[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` às rotas apropriadas e conferir o cabeçalho após o proxy.

### SEC-05 · Baixa · Bloqueio de conta disponível a tentativas anônimas direcionadas

**CWE-645 · OWASP A06 e A07 · API2 e API4.** Os dois logins usam `lockoutOnFailure: true`. A política configura cinco falhas por conta, enquanto a cota por endereço admite até dez requisições por janela de um minuto. S09 confirmou o limite por IP. A condição de bloqueio direcionado decorre do código e da combinação de políticas. Nenhuma conta foi deliberadamente bloqueada no teste. [Program.cs, linhas 29–50](src/ManutencaoEmCampo.Web/Program.cs#L29-L50) · [AccountController.cs, linhas 16–26](src/ManutencaoEmCampo.Web/Controllers/AccountController.cs#L16-L26) · [SessionApiController.cs, linhas 16–23](src/ManutencaoEmCampo.Web/Controllers/SessionApiController.cs#L16-L23)

**Cenário e impacto.** Um terceiro que conhece um e-mail pode contribuir para o bloqueio temporário do titular. Em proxy que agrega IPs, uma cota também pode afetar vários usuários. D afetada. Sem impacto direto em C e I. **Correção.** Manter proteção contra senhas adivinhadas e combinar limites por conta e origem, atrasos graduais e alertas, com fluxo de recuperação. Validar a extração de IP somente de proxies confiáveis.

### SEC-06 · Baixa · Script SQL Server usa conta administrativa e dispensa verificação do certificado

**CWE-250 e CWE-295 · OWASP A02 e A04 · API8.** O perfil de demonstração conecta a aplicação como `sa` com `Encrypt=True;TrustServerCertificate=True`. A porta do Compose está limitada a `127.0.0.1` e a senha do SQL Server vem de variável de ambiente. O achado refere-se a esse perfil de demonstração. Os privilégios do banco de produção continuam por verificar. [Start-Painel.ps1, linhas 22–33](scripts/Start-Painel.ps1#L22-L33) · [compose.yaml, linhas 1–15](compose.yaml#L1-L15)

**Cenário e impacto.** Em caso de comprometimento do processo, a credencial administrativa amplia o alcance a toda a instância. Se a configuração for reproduzida fora de loopback, o TLS cifra a conexão sem assegurar a identidade do servidor. C, I e D afetadas. **Correção.** Separar o usuário de migrations do usuário de execução, conceder permissões mínimas no banco da aplicação e validar o certificado do servidor SQL em ambientes não locais.

### SEC-07 · Baixa · Host HTTP sem lista de nomes permitidos

**CWE-346 · OWASP A02 · API8.** `AllowedHosts` aceita qualquer host por padrão. A criação via API usa `CreatedAtAction` para gerar `Location`. S07 verificou o caminho retornado. A análise de roteamento do ASP.NET Core indica que a geração de URLs absolutas pode usar o host recebido. O repositório não aplica uma lista de hosts e não inclui configuração de borda. Evidência estática com verificação da resposta normal. [appsettings.json, linha 5](src/ManutencaoEmCampo.Web/appsettings.json#L5) · [TicketsApiController.cs, linhas 26–32](src/ManutencaoEmCampo.Web/Controllers/TicketsApiController.cs#L26-L32) · [Microsoft · segurança do Kestrel](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations)

**Cenário e impacto.** Na ausência de filtragem na borda, um consumidor que reutilize URLs recebidas pode confiar em um endereço de origem imprópria. I dos links afetada. C depende do comportamento de consumidores e D não é afetada diretamente. **Correção.** Validar hosts no serviço e na borda e usar referências relativas quando o contrato permitir. Conferir o impacto em aliases, probes e geração de links.

### SEC-08 · Baixa · Eventos de segurança e alertas incompletos

**CWE-778 · OWASP A09.** A aplicação registra eventos operacionais de chamados, incluindo nome e horário, mas não emite eventos estruturados próprios para as decisões de segurança de login, revogação e autorização. A política de rate limiting não possui callback de rejeição. O nível de log de framework é `Warning` e o repositório não define destino protegido, retenção ou alertas. O tratamento genérico de exceções permanece ativo. [appsettings.json, linhas 2–4](src/ManutencaoEmCampo.Web/appsettings.json#L2-L4) · [Program.cs, linhas 45–51 e 74](src/ManutencaoEmCampo.Web/Program.cs#L45-L51) · [Ticket.cs, linhas 75–88](src/ManutencaoEmCampo.Web/Tickets/Ticket.cs#L75-L88)

**Cenário e impacto.** Tentativas recorrentes, bloqueios e padrões de acesso incomuns podem passar sem alerta e dificultar investigações. O efeito sobre C, I e D é indireto. **Correção.** Emitir eventos com resultado, ID interno quando disponível, origem confiável e ID de correlação. Definir retenção, acesso e alertas. Excluir senhas, cookies, tokens e conteúdo livre dos chamados.

### SEC-09 · Baixa · Limites de custo insuficientemente ajustados às rotas

**CWE-770 · OWASP A10 · API4.** A política HTTP cobre o login. Os DTOs limitam os campos e a listagem limita tamanho e número de páginas, mas o corpo é processado pelo framework antes de validar as propriedades. O código não define teto por rota ajustado aos campos curtos. A listagem executa contagem e paginação por offset, inclusive em páginas profundas. A efetividade de limites adicionais na borda não pode ser inferida. Não houve teste de carga. [Program.cs, linhas 45–59](src/ManutencaoEmCampo.Web/Program.cs#L45-L59) · [Contracts.cs, linhas 21–62](src/ManutencaoEmCampo.Web/Tickets/Contracts.cs#L21-L62) · [TicketService.cs, linhas 14–24](src/ManutencaoEmCampo.Web/Tickets/TicketService.cs#L14-L24) · [Microsoft · limites do Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0)

**Cenário e impacto.** Requisições frequentes, corpos acima do necessário ou consultas repetidas por usuário autenticado podem elevar consumo de CPU e banco. D afetada. Sem efeito direto em C ou I. **Correção.** Definir teto de corpo em Kestrel e na borda, limite por conta nas rotas autenticadas e cota específica para operações de maior custo. Medir a paginação antes de adotar cursor. Confirmar que formulários e clientes atuais permanecem dentro dos limites.

## Observações informativas

### SEC-10 · Informativa · CVE de minimatch presente no lockfile, sem condição aplicável identificada

**CWE-405 · CVE-2026-118764 · GHSA-g3hv-75wg-m249 · OWASP A03.** O lockfile resolve `@expo/fingerprint 0.21.10 → minimatch 10.2.6`. O fabricante classifica a falha como moderada e indica correção em `11.0.1`. A condição inclui nível de otimização pelo menos 2. O código instalado de `@expo/fingerprint` documenta e usa o nível padrão 1 nos seus filtros. O frontend do projeto não importa `minimatch`, e não foi identificado fluxo de padrões de usuário para essa ferramenta. Não foi gerada carga para reproduzir o CVE. [Lockfile, linhas 1207 e 4685](mobile/package-lock.json#L4685) · [Advisory do mantenedor](https://github.com/isaacs/minimatch/security/advisories/GHSA-g3hv-75wg-m249) · [Advisory revisado](https://github.com/advisories/GHSA-g3hv-75wg-m249)

**Cenário e impacto.** Se uma futura ferramenta passar padrões não confiáveis no modo afetado, a disponibilidade da ferramenta pode cair. C e I sem efeito direto. **Correção.** Preferir atualização suportada da cadeia Expo que resolva uma versão corrigida. A restrição `^10.2.6` não inclui 11.x. Se for avaliado um override, testar a compatibilidade do fingerprint, a checagem TypeScript e o build web antes de publicá-lo.

### SEC-11 · Informativa · Reprodutibilidade e controles de release parciais

**OWASP A03 e A08 · API8. Sem CWE específico para a ausência desses controles de release.** O npm tem lockfile v3 e 494 entradas com integridade declarada. NuGet usa uma única fonte configurada e referências diretas fixadas. Faltam lockfiles NuGet, validação de segurança em CI versionada e digest para a imagem `2022-latest`. A existência de controles fora do repositório não foi avaliada. [NuGet.Config](NuGet.Config) · [package-lock.json](mobile/package-lock.json#L1) · [compose.yaml, linha 3](compose.yaml#L3)

**Cenário e impacto.** Uma resolução ou imagem alterada pode mudar o software que foi efetivamente revisado. C, I e D seriam afetadas de acordo com o conteúdo introduzido. **Correção.** Fixar o grafo NuGet com restauração bloqueada, usar `npm ci`, registrar a imagem por digest quando distribuída e escanear dependências e imagens em CI. Acompanhar atualizações do digest com revisão e testes.

### SEC-12 · Informativa · SQLite nativo anterior às correções de outubro

**CWE-400, CWE-416 e CWE-787 entre as classes publicadas · OWASP A03.** A DLL Windows x64 do pacote SQLitePCLRaw 2.1.13 reportou SQLite 3.53.3 e código-fonte de 26/06/2026. A consulta nativa confirmou FTS3/4/5, R-Tree e Session compilados. O fabricante lista correções na versão 3.54.0 para CVE-2026-119371 a CVE-2026-119379 e CVE-2026-119591. As condições relevantes incluem controle de SQL, arquivos de banco/journal ou recursos específicos das extensões. Neste projeto, o arquivo é definido na configuração local, o esquema não usa essas extensões e os endpoints revisados não aceitam SQL, bancos, journals ou changesets enviados pelo cliente. A presença da versão foi verificada. A exploração pela aplicação não foi constatada. [Program.cs, linhas 14–27](src/ManutencaoEmCampo.Web/Program.cs#L14-L27) · [WorkDbContext.cs, linhas 13–46](src/ManutencaoEmCampo.Web/Data/WorkDbContext.cs#L13-L46) · [Advisories oficiais do SQLite](https://www.sqlite.org/cves.html)

**Cenário e impacto.** Se o uso futuro permitir abrir bancos externos ou processar SQL arbitrário, os impactos potenciais variam de falha do processo a exposição de memória ou violação de integridade. C, I e D dependem do recurso usado. **Correção.** Atualizar o pacote nativo quando uma versão compatível estiver disponível. Confirmar o `sqlite3_libversion()` do binário distribuído, testar a migração e manter o banco de demonstração sob controle local.

## Cobertura OWASP e resultados

| Categoria | Evidência ou resultado |
| --- | --- |
| A01 · Controle de acesso | S01 e S03 confirmaram negação anônima, segregação horizontal e papéis. SEC-01 cobre permissões que permanecem no bearer. |
| A02 · Configuração | CORS local restrito, OpenAPI fora de produção e redirecionamento quando configurado. SEC-02, SEC-06 e SEC-07. |
| A03 · Cadeia de suprimentos | Lockfile e grafo efetivo auditados. SEC-10 a SEC-12. |
| A04 · Criptografia | Cookie `HttpOnly` e `SameSite=Lax` observados. Identity para senhas e Data Protection para sessões. SEC-02, SEC-03, SEC-04 e SEC-06. |
| A05 · Injeção | Consultas por LINQ sem SQL bruto nos fluxos revisados. S07 verificou persistência como texto e escape Razor. Sem entrada para shell no servidor. |
| A06 · Design | Regras de status, perfil e atribuição no serviço e no domínio. SEC-05 e SEC-09. |
| A07 · Autenticação | S02 rejeitou tokens inválidos e alterados. S09 observou 429 no login. SEC-01 a SEC-05 tratam as lacunas relevantes. |
| A08 · Integridade | O modelo usa token de concorrência e salva a alteração de estado com o histórico. CI e verificação dos artefatos são parciais. SEC-11. |
| A09 · Logs e alertas | Histórico operacional presente. SEC-08. |
| A10 · Erros excepcionais | S04 cobriu 400/415, S09 cobriu 429 e S12 cobriu uma falha de dependência. SEC-09. |
| API Top 10 · Itens adicionais | API1, API2 e API5 foram exercitados. S04 e DTOs cobrem parte de API3. SEC-05 e SEC-09 cobrem API4 e API6. API8 e API9 foram avaliados em configuração e inventário. Não foi encontrado endpoint de URL arbitrária para API7 nem consumo de APIs de terceiros pelo backend para API10. As questões de redirecionamento do cliente estão no SEC-03. |

| Teste | O que foi observado |
| --- | --- |
| S01–S03 | 401 na API sem sessão ou com token inválido, redirecionamento do painel, 403 para papel incorreto e 404 no acesso a chamado de outro técnico. |
| S04 | 400 para JSON inválido, tipos incompatíveis, campos fora do tamanho e filtros inválidos. 415 para tipo de conteúdo indevido. |
| S05–S06 | CORS autorizou só a origem de desenvolvimento exercitada. POSTs MVC sem antifalsificação foram rejeitados. Cookie MVC sozinho não autenticou a API. |
| S07–S09 | Texto com pontuação e marcação inerte persistiu e apareceu escapado no painel. OpenAPI apareceu em Development. A resposta de validação não refletiu a senha. O login retornou 429 em uma sequência curta com conta inexistente. |
| S10–S12 | Redirecionamento HTTP 307 com porta HTTPS configurada. Em outra instância Production isolada, OpenAPI/CORS de desenvolvimento indisponíveis e falha do banco retornou 500 genérico com ID de rastreio. |
| Regressão existente | Dois testes passaram para login, permissões, atribuição, transições, histórico e paginação. |

Nove verificações complementares em Development, três em Production local e dois testes existentes foram executados. As saídas e os scripts da auditoria foram guardados em `.local/security-audit`, ignorado pelo Git. O teste via HTTPS, incluindo a observação dinâmica do cabeçalho HSTS, foi bloqueado pela interceptação do certificado local pelo antivírus. O HSTS foi confirmado apenas no código. Não houve alteração na configuração do antivírus.

Na cobertura complementar de 2021, foram considerados A02 para criptografia, A03 para injeção, A05 para configuração, A06 para dependências e A10 para SSRF. Em especial, a API desserializa para DTOs conhecidos e o cliente .NET Framework usa tipos de resposta explícitos. Não foram encontrados desserializadores com resolução de tipos fornecidos pelo usuário, uploads, execução de comandos no servidor ou SQL bruto no código revisado. As checagens de código e os testes locais não equivalem a uma varredura de todo ambiente de produção.

## Dependências e segredos

- **npm.** Comparação local de 494 entradas, 436 nomes de pacotes, com o snapshot público OSV de 18/11/2026. Houve um único casamento de versão, o SEC-10. Os campos `resolved` verificados apontam para `registry.npmjs.org` e as entradas têm integridade declarada. Isso documenta o lockfile, sem atestar procedência de todas as publicações. React e React DOM 19.3.1 e TypeScript 7.0.2 estavam disponíveis. O Expo 58 instalado declara React e React DOM 19.3.0 como compatíveis. Atualizações devem respeitar a matriz do SDK e ser testadas. [Dados públicos OSV](https://google.github.io/osv.dev/data/) · [Expo SDK](https://docs.expo.dev/versions/latest/)
- **NuGet.** A comparação OSV analisou 57 pacotes da Web, 2 do legado e `dotnet-ef`. A CLI `dotnet package list --include-transitive --vulnerable` também não reportou pacotes vulneráveis nos dois projetos. O runtime auditado, 10.0.15, era o patch corrente da linha .NET 10 LTS na data. As versões 11.0.0 publicadas integram outra linha. [NuGet Audit](https://learn.microsoft.com/nuget/concepts/auditing-packages) · [Suporte .NET](https://dotnet.microsoft.com/pt-br/platform/support/policy)
- **Segredos.** A busca por padrões comuns de chaves privadas e tokens de provedores teve zero correspondências nas três revisões locais. O repositório contém uma senha pública de demonstração, omitida neste relatório. O seed padrão depende das flags de Development e o script fornecido restringe a escuta a loopback. Essa busca por padrões não substitui uma revisão de arquivos ignorados, históricos remotos ou credenciais implantadas.
- **Imagem e runtime nativo.** A auditoria de pacotes não cobre o conteúdo binário da imagem SQL Server, os patches do Windows nem os runtimes que forem usados em produção. A biblioteca nativa SQLite foi avaliada separadamente por versão e opções de compilação, no SEC-12.

## Riscos que exigem verificação adicional

- **Implantação real.** Certificado e HSTS vistos pelo cliente, hosts permitidos, proxy e encaminhamento confiável, configuração de origem para o app web, cabeçalhos de segurança, cookies marcados `Secure`, política de cache e exposição das portas. Avaliar CSP também no hosting do Expo web.
- **Chaves e contas.** Proteção, persistência e rotação do chaveiro de Data Protection, segredos fora dos artefatos, autorização de provisionamento de contas, resposta a desligamento e MFA para contas de supervisão. A tentativa de persistência automática do chaveiro de Production falhou sob as permissões locais da auditoria.
- **SQL Server e cliente nativo.** O mecanismo Docker retornou acesso negado nesta sessão, logo migration em instância real, grants, certificado do banco, restauração de backups e scan da imagem ficaram pendentes. Também ficaram pendentes o teste em Android/iOS e a inspeção de artefatos assinados de release.
- **Cenários adicionais.** Concorrência intensa, capacidade e alertas sob carga, diferenças de tempo na autenticação, cenários entre réplicas e operação de cache intermediário não foram aferidos. O eventual alcance depende de infraestrutura, tráfego e requisitos reais.

## Remediação priorizada

| Ordem | Trabalho | Critério de validação e impacto previsto |
| --- | --- | --- |
| P1 | SEC-01 · Revogação real de sessões e revalidação de papéis | App, MVC e legado continuam autenticando. Logout, desativação e mudança de papel encerram o acesso dentro do prazo definido. Sessões existentes podem precisar ser reiniciadas. |
| P1 | SEC-02 e SEC-03 · Transporte, origem canônica e redirecionamentos | Build distribuído exige HTTPS. Demo explícita segue em loopback. Cliente legado passa a exigir o endereço final. Verificar HSTS, cookie seguro e comportamento atrás de proxy. |
| P2 | SEC-04, SEC-06 e SEC-07 · Cache, privilégios, certificados e hosts | Respostas privadas têm `no-store`. Conta da aplicação não pode executar DDL. Migrations continuam usando identidade separada. Apenas hosts previstos passam na borda e no serviço. |
| P2 | SEC-05, SEC-08 e SEC-09 · Proteção contra abuso e monitoramento | Alertas correlacionáveis sem segredos, limites graduais por usuário/origem e cotas ajustadas ao tráfego. Exercitar login em rede compartilhada, formulários longos válidos e falhas benignas de dependência. |
| P3 | SEC-10 a SEC-12 · Manutenção e rastreabilidade do build | Cadeia Expo atualizada sem resolver pacote afetado ou com exceção temporária documentada. Builds reproduzíveis, auditorias no pipeline e evidência do runtime e da imagem efetivamente distribuídos. |

Antes de liberar o projeto, separar configurações de demo e produção, controlar acesso ao banco e à rede por menor privilégio, configurar monitoramento e verificar recuperação de backup. Reexecutar os testes após cada grupo de alterações. A aplicação foi preservada durante a auditoria. A implementação do plano de remediação fica para a etapa autorizada pelo responsável.

## Estado após a segunda fase de remediação

Esta seção registra a validação de 18/11/2026. As classificações, evidências e limitações acima representam a auditoria original. A primeira fase corrigiu SEC-01 a SEC-03. A segunda fase tratou SEC-04 a SEC-09 e reexecutou a suíte da primeira fase. SEC-10 a SEC-12 mantêm o estado do relatório original.

| Achado | Estado e correção | Evidência |
| --- | --- | --- |
| SEC-04 | Corrigido. Respostas dos controladores MVC e API recebem `Cache-Control: no-store`, incluindo emissão de tokens, dados privados, páginas e falhas de autenticação. Arquivos estáticos mantêm a política própria. | Teste com formulário MVC, painel autenticado, API anônima, emissão e consulta com bearer, criação/detalhe e CSS. |
| SEC-07 | Corrigido. A lista de hosts exatos é obrigatória fora de Development. O padrão de Development contém apenas loopback. A aplicação rejeita curingas e a API devolve `Location` relativa ao criar chamado. | Testes aceitam o host configurado, rejeitam outro host em loopback, impedem inicialização com curinga e confirmam o formato de `Location`. |
| SEC-05 | Corrigido no aplicativo. Login anônimo não incrementa o bloqueio global do Identity. A cota por origem e a cota compartilhada entre MVC e API por conta normalizada mais origem retornam 429 e `Retry-After`. Bloqueios já atribuídos pelo Identity permanecem válidos. | Tentativas com variação de maiúsculas e minúsculas esgotaram a cota sem alterar o bloqueio no banco. Outra conta na mesma origem e a conta-alvo em outra origem se autenticaram. Um cabeçalho de origem vindo de proxy não cadastrado não contornou a cota. |
| SEC-09 | Corrigido no aplicativo. Corpos acima de 32 KiB são recusados. Operações e listagens têm cotas separadas por conta. Páginas após o total pulam a consulta com offset. Logout permanece disponível após esgotar cotas operacionais. | Testes de corpo com tamanho declarado e fragmentado, CORS, descrição Unicode no limite válido, página profunda vazia, cotas isoladas e revogação após 429. |
| SEC-08 | Corrigido no aplicativo. Eventos JSON estruturados têm identificadores 4600–4608, superfície e correlação. O contador `manutencao.security.events` permite coletar os totais. | O teste capturou sucesso e falha de login, autenticação exigida, autorização recusada, bloqueio, revogação e invalidação. Verificou ausência de e-mails, senhas, tokens e IP de origem nos eventos. |
| SEC-06 | Corrigido no código e validado em SQL Server descartável. Execução e migrations têm configurações distintas. O runtime exige TLS com certificado verificado e rejeita `sa` e privilégios administrativos detectados na primeira conexão. O script de grants limita o papel de execução aos objetos necessários. | Migrations e seed explícito rodaram com uma identidade separada. A regressão e o cliente .NET Framework funcionaram usando a identidade limitada. O driver recusou certificado diferente e o aplicativo recusou a identidade de migrations em modo de execução. |

### Registro comparável

| Etapa | Suíte Node em ambiente isolado | Compatibilidade |
| --- | --- | --- |
| Antes da fase 2 | 13 aprovados na suíte da primeira fase | Baseline registrado antes das alterações |
| Grupo 1 | 15 aprovados, incluindo a execução das duas regressões de chamados | Fluxo MVC–Expo no Chrome e cliente legado local |
| Grupo 2 | 17 aprovados, incluindo regressões preexistentes | Fluxo MVC–Expo no Chrome e cliente legado local |
| Grupo 3 | 18 aprovados, incluindo regressões preexistentes | Fluxo MVC–Expo no Chrome e cliente legado local |
| Grupo 4 | 19 aprovados, incluindo regressões preexistentes | Fluxo MVC–Expo no Chrome, cliente legado e validação cumulativa em SQL Server real descartável |

O build Release e a checagem TypeScript passaram. O EF Core confirmou que não há mudança de modelo pendente. O reteste HTTPS confirmou HSTS em Production e cookies `Secure` no login por TLS. Os grupos foram validados incrementalmente com SQLite e o SQL Server foi verificado ao final com um banco, duas identidades e certificados fictícios. Bancos existentes não foram alterados. A validação do Expo nesta fase foi web. As saídas incrementais estão em `.local/security-phase2`, uma pasta ignorada pelo Git.

### Riscos residuais e próxima auditoria de infraestrutura

- Validar no Docker definitivo as redes e portas expostas, os hosts encaminhados, a lista de proxies confiáveis e o TLS em cada trecho. Revisar o armazenamento, as permissões e a rotação da chave e do certificado montados no SQL Server. A configuração do Compose passou pela validação sintática e o perfil TLS foi exercitado em contêiner descartável.
- Calibrar as cotas com o tráfego legítimo da implantação. Contadores são locais à instância. Redes compartilhadas podem dividir a cota da origem. Em múltiplas réplicas ou sob abuso distribuído, complementar os limites com controles na borda e validar o encaminhamento de IP.
- Configurar coleta protegida, retenção, alertas e consumo do medidor nos destinos de produção. O aplicativo produz eventos e métricas. A entrega a um coletor, o disparo de alertas e a retenção são verificações da infraestrutura.
- Conferir os privilégios efetivos e a separação das identidades provisionadas no ambiente real. O runtime verifica as permissões administrativas detectáveis no primeiro uso do banco. Mudanças posteriores nos grants exigem controle de alterações e nova verificação. Validar também limites do contêiner e do proxy, healthchecks definitivos, persistência e restauração de backups na auditoria Docker.

Referências de implementação em [README.md](README.md), [test-security.mjs](scripts/test-security.mjs) e [grants do SQL Server](sql/runtime-permissions.sql). [Microsoft · filtragem de hosts no Kestrel](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/host-filtering) · [Microsoft · opções de TLS do SqlClient](https://learn.microsoft.com/sql/connect/ado-net/encryption-and-certificate-validation)


## Remediação de dependências e builds · Fase 3

Validação realizada em 18/11/2026. O histórico acima registra o estado observado em cada fase anterior.

| Achado | Estado nesta fase | Versões e evidência |
| --- | --- | --- |
| SEC-10 | Risco residual registrado. Foi mantida a cadeia suportada do Expo. | `expo 58.0.26 → @expo/fingerprint 0.21.10 → minimatch 10.2.6`, antes e depois. A faixa publicada pelo fingerprint é `^10.2.6`. O CVE-2026-118764 (GHSA-g3hv-75wg-m249) afeta versões a partir de 7.0.0 e anteriores à 11.0.1. A correção começa em 11.0.1. O Expo manteve a série 10 por compatibilidade. O código instalado do fingerprint mantém a otimização padrão 1, abaixo do nível mínimo 2 indicado no advisory. O npm registrou um advisory moderado, refletido em três entradas pela propagação aos dependentes. A sugestão automática de downgrade para Expo 52.0.49 exigiria trocar a matriz do projeto. [Advisory](https://github.com/advisories/GHSA-g3hv-75wg-m249) · [Pacote publicado](https://registry.npmjs.org/@expo%2ffingerprint/0.21.10) · [Decisão do Expo](https://github.com/expo/expo/issues/52735) |
| SEC-11 | Controles de restauração, auditoria e inventário implementados. | Os dois projetos .NET usam locks com versões e hashes. O build força a restauração bloqueada e a consulta de advisories no NuGet.org, valida a origem HTTPS oficial e o SHA-512 no lock npm e usa `npm ci`. Achados altos, críticos ou falhas de auditoria interrompem o build. A imagem `2022-latest` passou a usar o digest `sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f` para SQL Server 2022 Developer, validado em `linux/amd64`. O npm gerou SBOM CycloneDX 1.5 com 494 componentes. O NuGet gerou o inventário JSON dos projetos. [Auditoria NuGet](https://learn.microsoft.com/nuget/concepts/auditing-packages) · [npm ci](https://docs.npmjs.com/cli/v10/commands/npm-ci/) · [npm sbom](https://docs.npmjs.com/cli/v10/commands/npm-sbom/) · [Imagem por digest](https://docs.docker.com/build/building/best-practices/#pin-base-image-versions) |
| SEC-12 | Corrigido para os CVEs catalogados no achado original. | O bundle SQLitePCLRaw e as dependências `core`, `provider.e_sqlite3` e `lib.e_sqlite3` passaram de 2.1.13 a 2.1.14. A DLL carregada no processo ASP.NET Core em Windows x64 passou de SQLite 3.53.3 a 3.54.0. Essa versão contém as correções de CVE-2026-119371 a CVE-2026-119379 e CVE-2026-119591. O EF Core e Microsoft.Data.Sqlite continuam em 10.0.15. A atualização usa o esquema existente. [Pacote nativo](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.14) · [Advisories SQLite](https://www.sqlite.org/cves.html) · [SQLite 3.54.0](https://www.sqlite.org/releaselog/3_54_0.html) |

### Verificações

| Verificação | Resultado |
| --- | --- |
| Restauração e integridade | Restauração NuGet bloqueada e `npm ci` aprovados. Em cópias descartáveis, a divergência entre manifesto e lock do NuGet foi recusada com `NU1004`. Uma entrada npm com integridade inválida interrompeu o build antes da instalação. |
| Build e compatibilidade | Release do servidor e do cliente .NET Framework 4.8 com zero erros e avisos. Checagem TypeScript, export web do Expo e conferência online da matriz do SDK com `expo install --check` aprovados. Ambiente Windows x64 com .NET SDK 10.0.404, Node 22.23.5 e npm 10.9.9. |
| Segurança e regressão | 19 testes aprovados, sem falhas ou pulos, incluindo MVC, API, sessões, HTTPS, SEC-01 a SEC-09 e o cliente legado. |
| SQLite e dados existentes | `sqlite3_libversion()` confirmou 3.54.0 na DLL identificada no processo do servidor. Na cópia de um banco fictício anterior, a demo aplicou a evolução existente de sessões e passou em 2/2 regressões. Foram preservados os 3 usuários, 7 chamados e 20 eventos anteriores. `PRAGMA integrity_check` retornou `ok`. |
| SQL Server real | `docker compose config --images` resolveu a referência fixada. A mesma imagem rodou em contêiner descartável. As duas migrations aplicaram, 2/2 regressões passaram e o cliente legado consultou a API. A identidade limitada funcionou. O certificado divergente e a identidade de migrations em modo de execução foram recusados. |
| Auditoria de dependências | NuGet.org retornou zero pacotes com advisories na solução. O npm retornou um advisory moderado de minimatch, refletido em três entradas no grafo, e zero achados altos ou críticos. O SQLite nativo também foi conferido na lista do fabricante. |

### Riscos residuais e limites

- O alerta de minimatch continua no grafo. A baixa exposição se baseia na configuração observada do fingerprint e em seus pontos de uso nesta versão. Reavaliar quando houver outra versão oficial do Expo ou do fingerprint.
- A restauração reproduz o grafo fixado. SDK, sistema operacional e ambiente influenciam os artefatos. Os relatórios de pacotes não abrangem o sistema operacional, as ferramentas instaladas, a imagem OCI ou o runtime de cada ambiente.
- A DLL SQLite foi verificada no Windows x64 e o Expo para web. A execução em Android e iOS, demais variantes nativas do SQLite e implantação real ficam pendentes de validação.
- Na auditoria Docker, inventariar o conteúdo e os advisories da imagem e do host, verificar procedência e política de atualização, além de rede, TLS, segredos e persistência já indicados na fase 2. Nesta etapa foi validado apenas o SQL Server isolado com dados fictícios.

Arquivos da fase incluem [Directory.Build.props](Directory.Build.props), [NuGet.Config](NuGet.Config), [compose.yaml](compose.yaml), [Build-Verified.ps1](scripts/Build-Verified.ps1), [projeto Web](src/ManutencaoEmCampo.Web/ManutencaoEmCampo.Web.csproj), [lock Web](src/ManutencaoEmCampo.Web/packages.lock.json), [lock legado](src/ManutencaoEmCampo.Legacy/packages.lock.json), [README.md](README.md) e este relatório.
